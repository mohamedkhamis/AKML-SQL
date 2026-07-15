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
    private readonly ColumnProvider _columnProvider = new();
    private readonly AliasProvider _aliasProvider = new();
    private int _maxSuggestions = 50;
    private static readonly HashSet<string> _emptySchemaScope = new(StringComparer.OrdinalIgnoreCase);

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
    /// Default <see cref="SchemaQualifyMode.Always"/> — SQL Prompt parity: committing a table
    /// from the suggestion list inserts the owner-qualified name ("dbo.Customers").
    /// </summary>
    public SchemaQualifyMode SchemaQualifyMode { get; set; } = SchemaQualifyMode.Always;

    /// <summary>
    /// Controls whether inserted object names are wrapped in square brackets.
    /// <list type="bullet">
    ///   <item><see cref="BracketMode.Always"/> — always insert <c>[Name]</c>.</item>
    ///   <item><see cref="BracketMode.WhenRequired"/> (default) — bracket only identifiers
    ///         that contain spaces, reserved words, or other characters that require escaping.</item>
    ///   <item><see cref="BracketMode.Never"/> — never insert brackets, even for reserved words.</item>
    /// </list>
    /// Maps to <c>IntelliSense.Qualification.BracketMode</c>.
    /// </summary>
    public BracketMode BracketMode { get; set; } = BracketMode.WhenRequired;

    /// <summary>
    /// Spec 030 R6 / T032 / FR-012 — column suggestion scope. Maps to
    /// <c>IntelliSense.SuggestionTypes.ColumnScope</c>; pushed onto <see cref="ColumnProvider"/>
    /// per request. <see cref="ColumnSuggestionScope.All"/> suggests columns from every table
    /// even before a FROM clause exists.
    /// </summary>
    public ColumnSuggestionScope ColumnScopeMode { get; set; } = ColumnSuggestionScope.ReferencedOnly;

    // Spec 030 T035 / FR-015 — alias generation policy, pushed onto AliasProvider per request.
    public bool AliasIncludeAs { get; set; } = true;
    public IReadOnlyDictionary<string, string> AliasObjectMap { get; set; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> AliasPrefixesToIgnore { get; set; } = Array.Empty<string>();

    // Spec 030 T036 / FR-016 — suggestion connection scope, pushed onto ObjectProvider per request.
    /// <summary>Schemas the object suggestion list is limited to (case-insensitive). Empty = all.</summary>
    public IReadOnlyCollection<string> ScopeSchemas { get; set; } = Array.Empty<string>();
    /// <summary>
    /// False when the connected database is excluded from a non-empty database allow-list — the
    /// connected database's object/schema suggestions are then suppressed. Computed in the handler
    /// from the session's database name; default true (no restriction).
    /// </summary>
    public bool DatabaseInScope { get; set; } = true;
    /// <summary>When true, linked servers loaded into the schema cache are surfaced as
    /// object-reference completions (FR-016). Pushed into <c>ObjectProvider</c> per request.</summary>
    public bool IncludeLinkedServers { get; set; }

    public CompletionEngine(TsqlParserService parserService)
    {
        _parserService = parserService;

        // Register built-in providers (order matters for routing priority).
        // Spec 021 T101: DatabaseProvider lives in AkmlSql.Engine (it has a SqlClient
        // dependency for prefetching the database list, which cannot run in WASM).
        // The engine registers it externally via RegisterProvider after construction.
        // The web edition simply doesn't register it -- USE-keyword completion falls through.
        RegisterProvider(new SmartGroupByProvider());
        RegisterProvider(_columnProvider);
        RegisterProvider(_objectProvider);
        RegisterProvider(new KeywordProvider());
        RegisterProvider(_joinProvider);
        RegisterProvider(_joinOnFkProvider);
        RegisterProvider(new VariableProvider());
        RegisterProvider(new SnippetProvider());
        RegisterProvider(_aliasProvider);
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

    // Linked-server suggestions carry an explicit flag (set only by ObjectProvider.ToLinkedServerItem);
    // used to pin them past the suggestion cap. ObjectType cannot discriminate here —
    // DatabaseProvider also emits Database-typed items for USE-clause completion.
    private static bool IsLinkedServerItem(CompletionItem item)
        => item.IsLinkedServer;

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

                // Spec 030 (T029): track #temp tables (CREATE TABLE #t / SELECT ... INTO #t) visible
                // at the cursor so ColumnProvider can offer their columns, mirroring CTE handling.
                var tempTracker = new TempTableTracker();
                foreach (var (tmpName, tmpColumns) in tempTracker.TrackTempTables(script, cursorOffset))
                    context.AvailableTempTables[tmpName] = tmpColumns;
            }

            // Spec 030: a #temp declared before the cursor is lost when the tail is mid-edit (the full
            // parse fails on a partial `#t.` or empty SELECT list), so the prior CREATE TABLE #t never
            // reaches the tracker above. Recover by re-parsing the prefix trimmed of any trailing partial
            // identifier / dot / '#'. Mirrors the CTE prefix-recovery; gated on a '#' before the cursor
            // so it only costs an extra parse for likely-temp documents.
            int hashIdx = documentText.IndexOf('#');
            if (hashIdx >= 0 && hashIdx < cursorOffset && cursorOffset <= documentText.Length)
            {
                int p = cursorOffset;
                while (p > 0)
                {
                    char ch = documentText[p - 1];
                    if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '.' || ch == '#') p--;
                    else break;
                }
                if (p <= 0) p = cursorOffset;
                var tempPrefix = documentText.Substring(0, p);
                var tempPrefixScript = _parserService.ParseWithSuffix(tempPrefix, out _);
                if (tempPrefixScript != null)
                {
                    foreach (var (tmpName, tmpColumns) in new TempTableTracker().TrackTempTables(tempPrefixScript, tempPrefix.Length))
                        if (!context.AvailableTempTables.ContainsKey(tmpName))
                            context.AvailableTempTables[tmpName] = tmpColumns;
                }
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
            _joinProvider.SchemaQualifyMode = SchemaQualifyMode;

            // Push IntelliSense policy flags into ObjectProvider before each request.
            _objectProvider.IncludeSystemObjects = IncludeSystemObjects;
            _objectProvider.SchemaQualifyMode = SchemaQualifyMode;
            _objectProvider.BracketMode = BracketMode;

            // Push column-suggestion scope and connection scope into ColumnProvider (FR-012 / T032,
            // FR-016 / T036). ScopeSchemas is shared with ObjectProvider — same normalization.
            _columnProvider.ColumnScopeMode = ColumnScopeMode;
            _columnProvider.ScopeSchemas = ScopeSchemas is { Count: > 0 }
                ? new HashSet<string>(ScopeSchemas, StringComparer.OrdinalIgnoreCase)
                : _emptySchemaScope;

            // Push connection scope into ObjectProvider (FR-016 / T036). ScopeSchemas is normalized to a
            // case-insensitive set; an empty set means "no restriction".
            _objectProvider.ObjectsInScope = DatabaseInScope;
            _objectProvider.ScopeSchemas = ScopeSchemas is { Count: > 0 }
                ? new HashSet<string>(ScopeSchemas, StringComparer.OrdinalIgnoreCase)
                : _emptySchemaScope;
            _objectProvider.IncludeLinkedServers = IncludeLinkedServers;

            // Push alias-generation policy into AliasProvider (FR-015 / T035).
            _aliasProvider.IncludeAs = AliasIncludeAs;
            _aliasProvider.ObjectAliasMap = AliasObjectMap;
            _aliasProvider.PrefixesToIgnore = AliasPrefixesToIgnore;

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

            // Truncate — but never drop the (few, deliberate) linked-server suggestions behind the
            // cap. They rank below local objects/schemas by design (SortPriority 400), so in a
            // database with more than _maxSuggestions higher-priority objects a bare "FROM " would
            // otherwise silently hide every linked server. The explicit IsLinkedServer flag (set
            // only by ObjectProvider.ToLinkedServerItem) identifies them here.
            var isIncomplete = allItems.Count > _maxSuggestions;
            if (isIncomplete)
            {
                var pinned = allItems.Where(IsLinkedServerItem).ToList();
                if (pinned.Count == 0 || pinned.Count >= _maxSuggestions)
                {
                    allItems = allItems.Take(_maxSuggestions).ToList();
                }
                else
                {
                    allItems = allItems.Where(i => !IsLinkedServerItem(i))
                        .Take(_maxSuggestions - pinned.Count)
                        .Concat(pinned)
                        .OrderBy(i => i.SortPriority)
                        .ThenBy(i => i.DisplayText, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
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
