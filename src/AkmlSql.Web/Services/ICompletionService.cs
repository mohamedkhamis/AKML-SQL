using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
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

    public CompletionService(IEngineBridge bridge, ISchemaCacheStore? cache = null)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _cache = cache;   // null in tests that only exercise the online path
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
            return await _bridge.SendAsync<CompletionRequest, CompletionResponse>(
                MessageTypes.RequestCompletion, request, ct).ConfigureAwait(false);
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

        // Pick the most-recently-used snapshot. The store sorts ascending; the user's
        // active session collapses to the LAST entry. Multi-server scenarios would
        // need an explicit active-session pointer; deferred for T109.
        var snapshots = await _cache!.ListAsync().ConfigureAwait(false);
        var active = snapshots.Count > 0 ? snapshots[snapshots.Count - 1] : null;
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

        return new CompletionResponse
        {
            Items = items.ToArray(),
            IsIncomplete = false,
        };
    }

    /// <summary>
    /// Runs the engine's <see cref="SmartGroupByProvider"/> extraction against the LIVE document
    /// text (marshalled fresh from JS, never the debounced session) and, when the caret is in a
    /// GROUP BY clause with no partial token, prepends the "Add columns from SELECT" action so it
    /// sorts first. Mirrors the engine's online behaviour (same item, same gating) — purely
    /// parser-driven, so it needs no schema cache. Best-effort: a null/empty document, an
    /// out-of-range offset, or a parse failure leaves the list untouched.
    /// </summary>
    private static void TryPrependSmartGroupBy(List<CompletionItem> items, int cursorOffset, string? liveDocumentText)
    {
        if (string.IsNullOrEmpty(liveDocumentText)) return;
        if (cursorOffset < 0 || cursorOffset > liveDocumentText!.Length) return;

        try
        {
            var tokens = new TsqlParserService().GetTokenStream(liveDocumentText);
            var context = new CursorContextAnalyzer().Analyze(tokens, cursorOffset);
            // Gate exactly as SmartGroupByProvider.CanHandle does: GROUP BY, no partial token.
            if (context.ClauseType != ClauseType.GroupBy || !string.IsNullOrEmpty(context.PartialText))
            {
                return;
            }

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

    private static void AppendSchemaSurface(List<CompletionItem> items, SchemaPhasePayload payload)
    {
        foreach (var schema in payload.Schemas)
        {
            items.Add(new CompletionItem
            {
                DisplayText = schema.Name,
                InsertText = schema.Name,
                ObjectType = (int)CompletionObjectType.Schema,
                SortPriority = 30,
            });
            foreach (var obj in schema.Objects)
            {
                items.Add(new CompletionItem
                {
                    DisplayText = $"{obj.SchemaName}.{obj.ObjectName}",
                    InsertText = obj.ObjectName,
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
                        InsertText = col.Name,
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
                        InsertText = $"{obj.ObjectName}.{col.Name}",
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
