using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion.Providers;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using Serilog;

namespace AkmlSql.Engine.Completion;

public class CompletionEngine
{
    private readonly List<ICompletionProvider> _providers = [];
    private readonly TsqlParserService _parserService;
    private readonly CursorContextAnalyzer _contextAnalyzer = new();
    private readonly AliasResolver _aliasResolver = new();
    private readonly JoinProvider _joinProvider = new();
    private readonly JoinOnFkProvider _joinOnFkProvider = new();
    private readonly ObjectProvider _objectProvider = new();
    private int _maxSuggestions = 50;

    /// <summary>
    /// When enabled, the completion pipeline generates new aliases for tables inserted
    /// via completion and for FK-assisted JOIN targets (<c>Orders o ON o.CustomerId = ...</c>).
    /// When disabled, table names are inserted unaliased. Orthogonal to
    /// <see cref="JoinAssistEnabled"/> — the alias-generation toggle doesn't disable JOIN
    /// assist entirely.
    /// </summary>
    public bool TableAliasEnabled { get; set; } = false;

    /// <summary>
    /// Master switch for FK-assisted JOIN completion. When enabled, <see cref="JoinProvider"/>
    /// emits full <c>TABLE ON left.fk = right.pk</c> insertion text for JOIN targets, and
    /// <see cref="JoinOnFkProvider"/> emits ready-made FK equality predicates in the
    /// <c>ON</c> clause. Default enabled.
    /// </summary>
    public bool JoinAssistEnabled { get; set; } = true;

    /// <summary>
    /// When false, <see cref="KeywordProvider"/> is skipped and no keyword items appear
    /// in the completion list. Maps to <c>IntelliSense.SuggestionTypes.IncludeKeywords</c>.
    /// Default true.
    /// </summary>
    public bool IncludeKeywords { get; set; } = true;

    /// <summary>
    /// When false, system stored procedures from <see cref="Dictionaries.SystemProcDictionary"/>
    /// are excluded from the completion list. Maps to
    /// <c>IntelliSense.SuggestionTypes.IncludeSystemObjects</c>. Default true.
    /// </summary>
    public bool IncludeSystemObjects { get; set; } = true;

    /// <summary>
    /// When <c>true</c> (default), <see cref="JoinOnFkProvider"/> falls back to
    /// column-name matching for CTE join participants when no FK is found.
    /// When <c>false</c>, only FK-based ON-clause suggestions are emitted.
    /// Maps to <c>IntelliSense.JoinOptions.MatchByColumnName</c>.
    /// </summary>
    public bool MatchByColumnName { get; set; } = true;

    /// <summary>
    /// Controls how object names are qualified when inserted.
    /// Maps to <c>IntelliSense.Qualification.SchemaMode</c>.
    /// Default <see cref="SchemaQualifyMode.NonDefaultOnly"/>.
    /// </summary>
    public SchemaQualifyMode SchemaQualifyMode { get; set; } = SchemaQualifyMode.NonDefaultOnly;

    public CompletionEngine(TsqlParserService parserService)
    {
        _parserService = parserService;

        // Register built-in providers (order matters for routing priority)
        RegisterProvider(new SmartGroupByProvider());
        RegisterProvider(new DatabaseProvider());
        RegisterProvider(new ColumnProvider());
        RegisterProvider(_objectProvider);
        RegisterProvider(new KeywordProvider());
        RegisterProvider(_joinProvider);
        RegisterProvider(_joinOnFkProvider);
        RegisterProvider(new VariableProvider());
        RegisterProvider(new SnippetProvider());
        RegisterProvider(new AliasProvider());
    }

    public void RegisterProvider(ICompletionProvider provider)
    {
        _providers.Add(provider);
        Log.Debug("Registered completion provider: {Name}", provider.Name);
    }

    public void SetMaxSuggestions(int max)
    {
        _maxSuggestions = max;
    }

    public CompletionResponse GetCompletions(string documentText, int cursorOffset, DatabaseCache? cache)
        => GetCompletions(documentText, cursorOffset, cache, sessionId: string.Empty);

    public CompletionResponse GetCompletions(string documentText, int cursorOffset, DatabaseCache? cache, string sessionId)
    {
        try
        {
            // Fast tier: tokenize for context analysis
            var tokens = _parserService.GetTokenStream(documentText);
            var context = _contextAnalyzer.Analyze(tokens, cursorOffset);
            context.SessionId = sessionId ?? string.Empty;

            // Attach the token stream to the context so providers like
            // SmartGroupByProvider can re-scan the SELECT list without re-tokenizing.
            Providers.SmartGroupByContextExtensions.AttachTokens(context, tokens);

            // Suppress in comments/strings
            if (context.InComment || context.InString)
            {
                return new CompletionResponse { Items = [] };
            }

            // Full tier: parse for alias resolution (if available)
            var script = _parserService.ParseWithSuffix(documentText, out _);
            if (script != null)
            {
                // Use the scope-aware resolver so completion respects FROM-clause
                // boundaries. For `WITH cte AS (SELECT * FROM Inner) SELECT |`
                // FROM cte`, the cursor's outer scope sees only `cte` — not `Inner`
                // (which lives inside the CTE body). Without this, ColumnProvider
                // gets multi-alias context, qualifies CTE columns as `cte.Col`, and
                // filters them out when the user types a bare partial.
                var aliases = _aliasResolver.ResolveAliasesInCursorScope(script, cursorOffset);
                foreach (var (alias, tableRef) in aliases)
                    context.AvailableAliases[alias] = tableRef.FullName;

                // AST-based CTE resolution populates both names AND column lists.
                // The fallback below covers the case where parsing failed mid-CTE.
                var cteResolver = new CteResolver();
                var astCtes = cteResolver.ResolveCtes(script, cursorOffset);
                foreach (var (name, columns) in astCtes)
                    context.AvailableCtes[name] = columns;

                // Source-table tracking lets JoinOnFkProvider look up real FK
                // relationships between two CTEs via the underlying tables their
                // bodies select from.
                var astCteSources = cteResolver.ResolveCteSources(script, cursorOffset);
                foreach (var (name, sources) in astCteSources)
                    context.AvailableCteSources[name] = sources;
            }

            // Prefix-parse recovery: when content AFTER the cursor breaks the
            // full parse, the WITH clause that precedes the cursor still parses
            // cleanly on its own. Re-run CteResolver on the prefix so column
            // completion has access to CTE columns even with malformed tail content.
            //
            // Run when EITHER no CTEs are known yet OR any known CTE has an empty
            // column list (the token-based fallback registers CTE names without
            // columns when the parser can't recover them — we'd rather fill those
            // in via the prefix parse than leave them empty).
            bool anyCteMissingColumns =
                context.AvailableCtes.Count == 0 ||
                context.AvailableCtes.Values.Any(c => c.Count == 0);
            if (anyCteMissingColumns && cursorOffset > 0 && cursorOffset <= documentText.Length)
            {
                // Trim trailing partial identifier / dot characters so the prefix
                // is parseable by ParseWithSuffix. For `... FROM Cte1.<cursor>`,
                // the trailing `.` and any partial-identifier chars are not valid
                // in any SQL recovery context, so walking back over them gives a
                // clean syntactic suffix that the suffix-completer can fill in.
                int prefixLen = cursorOffset;
                while (prefixLen > 0)
                {
                    char ch = documentText[prefixLen - 1];
                    if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '.')
                    {
                        prefixLen--;
                        continue;
                    }
                    break;
                }
                if (prefixLen <= 0) prefixLen = cursorOffset; // pure-identifier doc — fall back to original
                var prefix = documentText.Substring(0, prefixLen);
                var prefixScript = _parserService.ParseWithSuffix(prefix, out _);
                if (prefixScript != null)
                {
                    var prefixResolver = new CteResolver();
                    var prefixCtes = prefixResolver.ResolveCtes(prefixScript, prefix.Length);
                    foreach (var (name, columns) in prefixCtes)
                    {
                        // Always overwrite empty entries; never overwrite a list
                        // that already has columns (keep authoritative AST data).
                        if (!context.AvailableCtes.TryGetValue(name, out var existing) || existing.Count == 0)
                            context.AvailableCtes[name] = columns;
                    }

                    var prefixSources = prefixResolver.ResolveCteSources(prefixScript, prefix.Length);
                    foreach (var (name, sources) in prefixSources)
                        if (!context.AvailableCteSources.ContainsKey(name))
                            context.AvailableCteSources[name] = sources;
                }
            }

            // Fallback: if AST parsing failed or produced no aliases, extract aliases
            // from the token stream. This handles incomplete SQL like
            // "SELECT BomItems." or "SELECT * FROM BomItems b JOIN " where the
            // parser can't produce an AST. Run regardless of clause type — the user
            // may be in SELECT, WHERE, ORDER BY, etc. and still need alias/table
            // resolution for dot completions.
            if (context.AvailableAliases.Count == 0)
            {
                var fallbackAliases = TokenBasedAliasExtractor.Extract(tokens, cursorOffset);
                foreach (var (alias, fullName) in fallbackAliases)
                    context.AvailableAliases[alias] = fullName;

                if (fallbackAliases.Count > 0)
                    Log.Debug("Alias fallback: extracted {Count} aliases from tokens", fallbackAliases.Count);
            }

            // CTE fallback — runs whenever the AST missed CTEs (e.g. the user is
            // typing inside an incomplete second CTE, so the batch doesn't parse).
            // Extracts CTE names from the token stream so they show up in FROM/JOIN
            // completion even while the SQL is unfinished.
            if (context.AvailableCtes.Count == 0)
            {
                var fallbackCtes = TokenBasedCteExtractor.Extract(tokens, cursorOffset);
                foreach (var name in fallbackCtes)
                    if (!context.AvailableCtes.ContainsKey(name))
                        context.AvailableCtes[name] = [];

                if (fallbackCtes.Count > 0)
                    Log.Debug("CTE fallback: extracted {Count} CTEs from tokens: {Names}",
                        fallbackCtes.Count, string.Join(", ", fallbackCtes));
            }

            Log.Debug(
                "Completion context: clause={Clause} inCteBody={InCte} partial='{Partial}' dotPrefix='{Dot}' aliases={Aliases} ctes={Ctes}",
                context.ClauseType, context.IsInCteBody, context.PartialText, context.DotPrefix,
                context.AvailableAliases.Count, context.AvailableCtes.Count);

            // Push current toggles into the providers that need them. JoinProvider
            // always runs when JoinAssist is enabled — AutoAlias only decides whether
            // the inserted JOIN target carries a fresh alias or uses its bare name.
            _joinProvider.UseAliases = TableAliasEnabled;

            // Push IntelliSense policy flags into ObjectProvider before each request.
            _objectProvider.IncludeSystemObjects = IncludeSystemObjects;
            _objectProvider.SchemaQualifyMode = SchemaQualifyMode;

            // Push join options into JoinOnFkProvider before each request.
            _joinOnFkProvider.MatchByColumnName = MatchByColumnName;

            // Route to providers
            var allItems = new List<CompletionItem>();
            foreach (var provider in _providers)
            {
                // AliasProvider (suggesting "o", "od" after a table name in FROM) is
                // purely an alias-generation feature — skip when the toggle is off.
                if (!TableAliasEnabled && provider is AliasProvider)
                    continue;

                // FK-assisted JOIN providers gate on the JoinAssist master switch.
                if (!JoinAssistEnabled && (provider is JoinProvider || provider is JoinOnFkProvider))
                    continue;

                // IncludeKeywords = false: skip KeywordProvider entirely.
                if (!IncludeKeywords && provider is KeywordProvider)
                    continue;

                if (provider.CanHandle(context, cache))
                {
                    var items = provider.GetCompletions(context, cache);
                    allItems.AddRange(items);
                }
            }

            // Apply fuzzy filter if partial text
            if (!string.IsNullOrEmpty(context.PartialText))
            {
                allItems = allItems
                    .Select(item => (item, score: FuzzyMatcher.Score(context.PartialText, item.DisplayText)))
                    .Where(x => x.score > 0)
                    .OrderByDescending(x => x.score)
                    .ThenBy(x => x.item.SortPriority)
                    .Select(x => x.item)
                    .ToList();
            }
            else
            {
                allItems = allItems
                    .OrderBy(i => i.SortPriority)
                    .ThenBy(i => i.DisplayText, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            // Truncate
            var isIncomplete = allItems.Count > _maxSuggestions;
            if (isIncomplete)
            {
                allItems = allItems.Take(_maxSuggestions).ToList();
            }

            return new CompletionResponse
            {
                Items = allItems.ToArray(),
                IsIncomplete = isIncomplete
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Completion engine error at offset {Offset}", cursorOffset);
            return new CompletionResponse { Items = [] };
        }
    }
}
