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
    private int _maxSuggestions = 50;

    public CompletionEngine(TsqlParserService parserService)
    {
        _parserService = parserService;

        // Register built-in providers (order matters for routing priority)
        RegisterProvider(new ColumnProvider());
        RegisterProvider(new ObjectProvider());
        RegisterProvider(new KeywordProvider());
        RegisterProvider(new JoinProvider());
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
    {
        try
        {
            // Fast tier: tokenize for context analysis
            var tokens = _parserService.GetTokenStream(documentText);
            var context = _contextAnalyzer.Analyze(tokens, cursorOffset);

            // Suppress in comments/strings
            if (context.InComment || context.InString)
            {
                return new CompletionResponse { Items = [] };
            }

            // Full tier: parse for alias resolution (if available)
            var script = _parserService.ParseWithSuffix(documentText, out _);
            if (script != null)
            {
                var aliases = _aliasResolver.ResolveAliases(script, cursorOffset);
                foreach (var (alias, tableRef) in aliases)
                    context.AvailableAliases[alias] = tableRef.FullName;
            }

            // Fallback: if AST parsing failed or produced no aliases, extract aliases
            // from the token stream. This handles incomplete SQL like
            // "SELECT * FROM BomItems b JOIN " where the parser can't produce an AST.
            if (context.AvailableAliases.Count == 0 && context.ClauseType == ClauseType.From)
            {
                var fallbackAliases = TokenBasedAliasExtractor.Extract(tokens, cursorOffset);
                foreach (var (alias, fullName) in fallbackAliases)
                    context.AvailableAliases[alias] = fullName;

                if (fallbackAliases.Count > 0)
                    Log.Debug("Alias fallback: extracted {Count} aliases from tokens", fallbackAliases.Count);
            }

            // Route to providers
            var allItems = new List<CompletionItem>();
            foreach (var provider in _providers)
            {
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
