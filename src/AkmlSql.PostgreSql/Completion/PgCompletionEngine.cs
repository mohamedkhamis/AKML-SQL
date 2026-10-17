using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Schema;
using AkmlSql.PostgreSql.Completion.Providers;
using AkmlSql.PostgreSql.Reference;
using Serilog;

namespace AkmlSql.PostgreSql.Completion;

/// <summary>
/// Spec 042 (task T101, FR-061, FR-064, FR-065) - completion for a PostgreSQL session. It runs the PostgreSQL providers over a
/// <see cref="PgCursorContext"/> and answers with the same <see cref="CompletionResponse"/> the SQL Server engine does, ranked
/// the same way: a typed prefix is matched fuzzily, ties go to the provider's priority, and the list is cut at the suggestion
/// cap with <c>IsIncomplete</c> set. It shares nothing with the T-SQL <c>CompletionEngine</c> but the result type, the fuzzy
/// matcher and the schema cache.
/// </summary>
public sealed class PgCompletionEngine
{
    private readonly IPgCompletionProvider[] _providers =
    {
        new PgJoinOnFkProvider(),
        new PgColumnProvider(),
        new PgAliasCteProvider(),
        new PgSchemaObjectProvider(),
        new PgFunctionProvider(),
        new PgTypeProvider(),
        new PgKeywordProvider(),
        new PgSnippetProvider(),
    };

    private readonly PgReference _reference;

    public PgCompletionEngine() : this(PgReference.Instance) { }

    public PgCompletionEngine(PgReference reference) => _reference = reference;

    public CompletionResponse GetCompletions(string? text, int caret, DatabaseCache? cache, PgCompletionOptions? options = null)
    {
        options ??= new PgCompletionOptions();
        try
        {
            var context = PgCursorContextAnalyzer.Analyze(text, caret);
            if (context.Suppressed) return new CompletionResponse();

            // FR-052: a server of release N is offered only what release N has.
            var input = new PgCompletionInput(context, cache, _reference.For(options.ServerMajor), options);

            var all = new List<CompletionItem>();
            if (!context.AfterDot && PgKeywordProvider.IsFixedContinuation(context))
            {
                // IS, GROUP, LEFT, NULLS, ...: the grammar allows only a few words here, so a column or a function would be wrong.
                new PgKeywordProvider().Collect(input, all);
            }
            else
            {
                foreach (var provider in _providers)
                    provider.Collect(input, all);
            }

            return Rank(all, context.Prefix, options);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PostgreSQL completion failed at offset {Offset}", caret);
            return new CompletionResponse();
        }
    }

    private static CompletionResponse Rank(List<CompletionItem> items, string prefix, PgCompletionOptions options)
    {
        var distinct = items
            .GroupBy(i => (i.DisplayText, i.ObjectType, i.SourceObject))
            .Select(g => g.OrderBy(i => i.SortPriority).First())
            .ToList();

        IEnumerable<CompletionItem> ranked;
        if (prefix.Length > 0)
        {
            ranked = distinct
                .Select(item => (item, score: FuzzyMatcher.Score(prefix, item.FilterText ?? item.DisplayText)))
                .Where(x => options.FuzzyMatch ? x.score > 0 : x.score >= 800)
                .OrderByDescending(x => x.score)
                .ThenBy(x => x.item.SortPriority)
                .ThenBy(x => x.item.DisplayText, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.item);
        }
        else
        {
            ranked = distinct
                .OrderBy(i => i.SortPriority)
                .ThenBy(i => i.DisplayText, StringComparer.OrdinalIgnoreCase);
        }

        var list = ranked.ToList();
        var max = Math.Max(5, options.MaxSuggestions);
        var incomplete = list.Count > max;
        return new CompletionResponse { Items = (incomplete ? list.Take(max) : list).ToArray(), IsIncomplete = incomplete };
    }
}
