using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Completion.Providers;
using AkmlSql.Engine.Parser;
using MessagePack;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M3 task T072 + M5 task T109. Routes IntelliSense
/// completion through the engine bridge when the bridge is open; when the bridge
/// is closed, falls back to the most-recently-used IndexedDB schema snapshot and
/// synthesises a completion list from the cached schemas + objects (+ columns when
/// Phase B is present). Column-context-aware completion (e.g. dot-after-alias)
/// requires a parser pass on the document text and is deferred — the offline
/// fallback returns the full schema/object surface so the user has something
/// useful while disconnected (FR-016).
/// </summary>
public interface ICompletionService
{
    /// <param name="liveDocumentText">
    /// The current editor text, marshalled fresh from JS on every request. Used only by the
    /// OFFLINE path (the smart GROUP BY action needs to parse the live SELECT list); the online
    /// path ignores it — the engine already holds the document in its session. Passing it as a
    /// call argument (rather than on <see cref="CompletionRequest"/>) keeps it off the engine
    /// wire and avoids the debounced-session staleness that would hide the item during fast typing.
    /// </param>
    Task<CompletionResponse> CompleteAsync(
        CompletionRequest request, CancellationToken ct, string? liveDocumentText = null);
}

internal sealed class CompletionService : ICompletionService
{
    private readonly IEngineBridge _bridge;
    private readonly ISchemaCacheStore? _cache;
    private readonly IDialectGate? _gate;

    public CompletionService(IEngineBridge bridge, ISchemaCacheStore? cache = null, IDialectGate? gate = null, ISqlConnectionService? connection = null,
        ISnippetStore? snippets = null)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _cache = cache;   // null in tests that only exercise the online path
        _gate = gate;
        _connection = connection;
        _snippets = snippets;
        if (_snippets != null) _snippets.Changed += OnSnippetsChanged;
    }

    private readonly ISqlConnectionService? _connection;

    // Spec 047 (SN-001): the user's own snippets, offered beside the built-ins. The list is kept per kind until the
    // store says it changed (a save, delete or import), so typing does not read IndexedDB on every keystroke.
    private readonly ISnippetStore? _snippets;
    private readonly object _snippetLock = new();
    private readonly Dictionary<DatabaseKind, CompletionItem[]> _userSnippetItems = new();
    private int _snippetVersion;

    private void OnSnippetsChanged()
    {
        lock (_snippetLock)
        {
            _snippetVersion++;
            _userSnippetItems.Clear();
        }
    }

    public async Task<CompletionResponse> CompleteAsync(
        CompletionRequest request, CancellationToken ct, string? liveDocumentText = null)
    {
        if (_bridge.State != BridgeState.Open)
        {
            // T109 cache-backed fallback: if a schema snapshot is available locally,
            // synthesise a completion list from it. Returns empty when no cache.
            if (_cache != null)
            {
                return await BuildFromCacheAsync(request.CursorOffset, liveDocumentText).ConfigureAwait(false);
            }
            return new CompletionResponse();
        }
        try
        {
            var response = await _bridge.SendAsync<CompletionRequest, CompletionResponse>(
                MessageTypes.RequestCompletion, request, ct).ConfigureAwait(false);
            return await WithUserSnippetsAsync(response, liveDocumentText, request.CursorOffset, engineReplied: true).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Bridge dropped mid-call -- try cache before returning empty.
            if (_cache != null)
            {
                return await BuildFromCacheAsync(request.CursorOffset, liveDocumentText).ConfigureAwait(false);
            }
            return new CompletionResponse();
        }
    }

    private async Task<CompletionResponse> BuildFromCacheAsync(int cursorOffset, string? liveDocumentText)
    {
        // Spec 042 (FR-065, FR-090): connected-only. The offline list is T-SQL keywords, @variables and [brackets];
        // PostgreSQL gets completion from the engine, and an engine that is gone means no suggestions, not wrong ones.
        if (_gate?.IsPostgres == true) return new CompletionResponse();

        // Keywords are ALWAYS available offline — they don't depend on any cache.
        // When no schema snapshot is present (fresh browser, never paired with an
        // engine, OR paired but the engine has no SQL session yet), the user still
        // gets SQL keywords. This makes "type WHERE then space" produce a useful
        // popup instead of nothing. Schemas / objects / columns layer on top
        // whenever a snapshot is available.
        var items = new List<CompletionItem>();
        AppendKeywords(items);

        // SQL-Prompt-style smart GROUP BY (spec 027 follow-up): when the cursor sits in a
        // GROUP BY clause, prepend the top-priority "Add columns from SELECT" item. This is
        // the SAME item the engine emits online via SmartGroupByProvider; offline we run the
        // provider's text-based extraction against the LIVE document. It needs no DatabaseCache,
        // so it works even when no schema snapshot has been fetched yet.
        TryPrependSmartGroupBy(items, cursorOffset, liveDocumentText);

        // Re-review REV-01: the connected database's snapshot only (the last-used one only when no connection is known).
        var active = await _cache!.GetActiveAsync(_connection).ConfigureAwait(false);
        if (active?.PhaseA != null && active.PhaseA.Length > 0)
        {
            SchemaPhasePayload? payloadA = null;
            try { payloadA = MessagePackSerializer.Deserialize<SchemaPhasePayload>(active.PhaseA); }
            catch (MessagePackSerializationException) { /* keywords only */ }

            // Phase B is optional; if present, it overrides Phase A's view (it's a
            // superset). The column items only appear when Phase B is cached.
            SchemaPhasePayload? payloadB = null;
            if (active.PhaseB != null && active.PhaseB.Length > 0)
            {
                try { payloadB = MessagePackSerializer.Deserialize<SchemaPhasePayload>(active.PhaseB); }
                catch (MessagePackSerializationException) { /* fall through with Phase A only */ }
            }

            if (payloadA != null || payloadB != null)
            {
                AppendSchemaSurface(items, payloadB ?? payloadA!);
                if (payloadB != null) AppendColumns(items, payloadB);
            }
        }

        return await WithUserSnippetsAsync(new CompletionResponse
        {
            Items = items.ToArray(),
            IsIncomplete = false,
        }, liveDocumentText, cursorOffset, engineReplied: false).ConfigureAwait(false);
    }

    /// <summary>
    /// Spec 047 (SN-001, contracts/workspace-ai.md §1): add the user's own snippets of the tab's dialect where a snippet
    /// belongs — where the engine offered its built-ins, or at a statement start. The engine filters its list by the typed
    /// word, so for a shortcode no built-in matches it returns no snippet at all; the statement-start rule covers that too.
    /// A user snippet with a built-in's shortcode replaces the built-in, as the expansion lookup does.
    /// </summary>
    private async Task<CompletionResponse> WithUserSnippetsAsync(CompletionResponse response, string? text, int caret, bool engineReplied)
    {
        if (_snippets == null || response == null) return response!;
        var items = response.Items ?? Array.Empty<CompletionItem>();
        var engineOffersSnippets = engineReplied && items.Any(i => i.ObjectType == (int)CompletionObjectType.Snippet);
        if (!engineOffersSnippets && !AtStatementStart(text, caret)) return response;

        var mine = await UserSnippetItemsAsync(_gate?.IsPostgres == true ? DatabaseKind.PostgreSql : DatabaseKind.SqlServer).ConfigureAwait(false);
        if (mine.Length == 0) return response;

        var shortcodes = new HashSet<string>(mine.Select(m => m.DisplayText), StringComparer.OrdinalIgnoreCase);
        response.Items = items
            .Where(i => !(i.ObjectType == (int)CompletionObjectType.Snippet && shortcodes.Contains(i.DisplayText)))
            .Concat(mine)
            .ToArray();
        return response;
    }

    private async Task<CompletionItem[]> UserSnippetItemsAsync(DatabaseKind kind)
    {
        int version;
        lock (_snippetLock)
        {
            if (_userSnippetItems.TryGetValue(kind, out var kept)) return kept;
            version = _snippetVersion;
        }

        CompletionItem[] items;
        try
        {
            var list = await _snippets!.ListAsync(kind).ConfigureAwait(false);
            items = list.Where(s => !s.IsBuiltIn && !string.IsNullOrWhiteSpace(s.Metadata.Shortcode)).Select(ToCompletionItem).ToArray();
        }
        catch
        {
            return Array.Empty<CompletionItem>();   // a store that cannot be read must never break the popup
        }

        lock (_snippetLock)
        {
            if (version == _snippetVersion) _userSnippetItems[kind] = items;   // not a list read before a change
        }
        return items;
    }

    /// <summary>Data-model §10: a user snippet as a completion item; the editor expands its body on accept.</summary>
    private static CompletionItem ToCompletionItem(WebSnippet snippet) => new()
    {
        DisplayText = snippet.Metadata.Shortcode,
        InsertText = string.Join("\n", snippet.Body ?? Array.Empty<string>()),
        ObjectType = (int)CompletionObjectType.Snippet,
        SecondaryText = snippet.Metadata.Title,
        SortPriority = 500,
        SourceObject = string.Empty,
    };

    /// <summary>
    /// Spec 047 (SN-001): whether the word being typed at <paramref name="caret"/> starts a statement: nothing but comments
    /// before it, or a <c>;</c>, a <c>GO</c> (with or without a count) or a blank line between it and the code before. A
    /// null text is unknown, so no.
    /// </summary>
    internal static bool AtStatementStart(string? text, int caret)
    {
        if (text == null) return false;
        caret = Math.Max(0, Math.Min(caret, text.Length));
        var start = caret;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_')) start--;
        if (start == 0) return true;

        IList<Microsoft.SqlServer.TransactSql.ScriptDom.TSqlParserToken> tokens;
        try { tokens = _offlineParser.GetTokenStream(text.Substring(0, start)); }
        catch { return false; }

        var newlines = 0;
        for (var i = tokens.Count - 1; i >= 0; i--)
        {
            var token = tokens[i];
            switch (token.TokenType)
            {
                case Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.EndOfFile:
                    continue;
                case Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.WhiteSpace:
                    newlines += (token.Text ?? string.Empty).Count(c => c == '\n');
                    if (newlines >= 2) return true;   // a blank line
                    continue;
                case Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.SingleLineComment:
                case Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.MultilineComment:
                    newlines = 0;   // a comment line is not a blank one
                    continue;
                case Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.Semicolon:
                case Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.Go:
                    return true;
                case Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.Integer:
                    return IsGoCount(tokens, i);   // GO 3
                default:
                    return false;
            }
        }
        return true;   // only comments and space before it: the start of the document
    }

    private static bool IsGoCount(IList<Microsoft.SqlServer.TransactSql.ScriptDom.TSqlParserToken> tokens, int integerIndex)
    {
        for (var i = integerIndex - 1; i >= 0; i--)
        {
            var type = tokens[i].TokenType;
            if (type == Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.WhiteSpace && !(tokens[i].Text ?? string.Empty).Contains('\n')) continue;
            return type == Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.Go;
        }
        return false;
    }

    /// <summary>
    /// Runs the engine's <see cref="SmartGroupByProvider"/> extraction against the LIVE document
    /// text (marshalled fresh from JS, never the debounced session) and, when the caret is in a
    /// GROUP BY clause with no partial token, prepends the "Add columns from SELECT" action so it
    /// sorts first. Mirrors the engine's online behaviour (same item, same gating) — purely
    /// parser-driven, so it needs no schema cache. Best-effort: a null/empty document, an
    /// out-of-range offset, or a parse failure leaves the list untouched.
    /// </summary>
    // Reused across offline completions to avoid rebuilding the ScriptDom parser on every
    // keystroke trigger. TsqlParserService locks parser construction but NOT tokenization; a
    // shared instance is safe here only because Blazor WASM is single-threaded. SmartGroupByProvider
    // is stateless — held only to call its authoritative CanHandle gate so the offline surface
    // cannot drift from the engine.
    private static readonly TsqlParserService _offlineParser = new();
    private static readonly SmartGroupByProvider _smartGroupBy = new();

    private static void TryPrependSmartGroupBy(List<CompletionItem> items, int cursorOffset, string? liveDocumentText)
    {
        if (string.IsNullOrEmpty(liveDocumentText)) return;
        if (cursorOffset < 0 || cursorOffset > liveDocumentText.Length) return;

        try
        {
            var tokens = _offlineParser.GetTokenStream(liveDocumentText);
            var context = new CursorContextAnalyzer().Analyze(tokens, cursorOffset);
            // Reuse the provider's authoritative eligibility gate (GROUP BY clause, no partial
            // token) so the offline surface can never diverge from the engine/online path.
            if (!_smartGroupBy.CanHandle(context, null)) return;

            var item = SmartGroupByProvider.BuildSmartItem(tokens, cursorOffset);
            if (item != null) items.Insert(0, item);
        }
        catch
        {
            // Offline best-effort — never let a parse failure break the completion list.
        }
    }

    // The minimal SQL keyword surface. The online engine has a richer set, but for
    // offline this is enough to keep IntelliSense feeling alive while disconnected.
    private static readonly string[] BasicKeywords =
    {
        "SELECT", "FROM", "WHERE", "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "OUTER",
        "ON", "AND", "OR", "NOT", "IN", "LIKE", "BETWEEN", "IS", "NULL", "AS",
        "GROUP BY", "ORDER BY", "HAVING", "DISTINCT", "TOP", "OFFSET", "FETCH",
        "INSERT", "UPDATE", "DELETE", "MERGE", "INTO", "VALUES", "SET",
        "CREATE", "ALTER", "DROP", "TABLE", "VIEW", "INDEX", "PROCEDURE", "FUNCTION",
        "BEGIN", "END", "DECLARE", "IF", "ELSE", "WHILE", "RETURN", "CASE", "WHEN", "THEN",
    };

    private static void AppendKeywords(List<CompletionItem> items)
    {
        foreach (var kw in BasicKeywords)
        {
            items.Add(new CompletionItem
            {
                DisplayText = kw,
                InsertText = kw,
                ObjectType = (int)CompletionObjectType.Keyword,
                SortPriority = 50,
            });
        }
    }

    // Every InsertText below goes through SqlIdentifier.QuoteIfNeeded, the same rule the engine's
    // ObjectProvider applies. This offline path is what runs when the engine is unreachable, and it
    // used to insert raw names -- so picking "Order Details" produced `Order Details`, which is not
    // valid T-SQL, and it did so precisely when the engine was down, which is exactly when a user
    // is least able to tell why the query they just built will not run.
    //
    // DisplayText stays unquoted on purpose: it is the popup label, and the editor fuzzy-matches
    // what the user typed against it. "Ord" should find "Order Details"; it would not find
    // "[Order Details]" by prefix.
    private static void AppendSchemaSurface(List<CompletionItem> items, SchemaPhasePayload payload)
    {
        foreach (var schema in payload.Schemas)
        {
            items.Add(new CompletionItem
            {
                DisplayText = schema.Name,
                InsertText = SqlIdentifier.QuoteIfNeeded(schema.Name),
                ObjectType = (int)CompletionObjectType.Schema,
                SortPriority = 30,
            });
            foreach (var obj in schema.Objects)
            {
                items.Add(new CompletionItem
                {
                    DisplayText = $"{obj.SchemaName}.{obj.ObjectName}",
                    InsertText = SqlIdentifier.QuoteIfNeeded(obj.ObjectName),
                    ObjectType = MapObjectType(obj.ObjectType),
                    SecondaryText = obj.SchemaName,
                    SourceObject = $"{obj.SchemaName}.{obj.ObjectName}",
                    SortPriority = 10,
                });
            }
        }
    }

    private static void AppendColumns(List<CompletionItem> items, SchemaPhasePayload payload)
    {
        foreach (var schema in payload.Schemas)
        {
            foreach (var obj in schema.Objects)
            {
                foreach (var col in obj.Columns)
                {
                    // Bare column form — what the user types in a single-table query.
                    items.Add(new CompletionItem
                    {
                        DisplayText = col.Name,
                        InsertText = SqlIdentifier.QuoteIfNeeded(col.Name),
                        ObjectType = (int)CompletionObjectType.Column,
                        SecondaryText = col.TypeName,
                        SourceObject = $"{obj.SchemaName}.{obj.ObjectName}",
                        SortPriority = 20,
                    });
                    // Qualified `table.column` form — what the user needs in ORDER BY /
                    // GROUP BY / WHERE after `SELECT *,col` so the engine doesn't reject
                    // with "Ambiguous column name". CM's fuzzy filter naturally surfaces
                    // the qualified item the moment the user types the table prefix
                    // (e.g. "martyrs.cr" → `martyrs.created_at`); the bare item is the
                    // first hit for short prefixes ("cr" → both `created_at` and
                    // `martyrs.created_at`, with `created_at` ranked higher by length).
                    items.Add(new CompletionItem
                    {
                        DisplayText = $"{obj.ObjectName}.{col.Name}",
                        // Each part quoted on its own: `[Order Details].UnitPrice`, never
                        // `[Order Details.UnitPrice]`, which would name a single identifier.
                        InsertText = SqlIdentifier.QuoteIfNeeded(obj.ObjectName, col.Name),
                        ObjectType = (int)CompletionObjectType.Column,
                        SecondaryText = col.TypeName,
                        SourceObject = $"{obj.SchemaName}.{obj.ObjectName}",
                        SortPriority = 25,   // slightly lower than bare so bare wins on tie
                    });
                }
            }
        }
    }

    private static int MapObjectType(int dbObjectType)
    {
        // Maps engine's DbObjectType (Table=0, View=1, Procedure=2, …) to the
        // CompletionObjectType enum (Table=0, View=1, Column=2, …, Procedure=6).
        return dbObjectType switch
        {
            0 => (int)CompletionObjectType.Table,
            1 => (int)CompletionObjectType.View,
            2 => (int)CompletionObjectType.Procedure,
            3 or 4 or 5 => (int)CompletionObjectType.Function,
            _ => (int)CompletionObjectType.Table,
        };
    }
}
