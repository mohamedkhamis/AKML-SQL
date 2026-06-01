using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Refactoring.Operations.Lightweight;

/// <summary>
/// Appends a GROUP BY clause listing all non-aggregated columns in a SELECT statement
/// that has no existing GROUP BY clause.
/// </summary>
public class AddGroupByColumnsOperation : ILightweightOperation
{
    private static readonly HashSet<string> AggregateNames =
        new(StringComparer.OrdinalIgnoreCase)
        { "COUNT", "SUM", "AVG", "MIN", "MAX", "COUNT_BIG" };

    public (string modifiedText, string[] warnings) Apply(RefactoringContext context)
    {
        try
        {
            if (string.IsNullOrEmpty(context.DocumentText))
                return (context.DocumentText, []);

            var script = context.Script;
            if (script == null)
                return (context.DocumentText, []);

            var collector = new QuerySpecCollector();
            script.Accept(collector);

            if (collector.Specs.Count == 0)
                return (context.DocumentText, []);

            var text = context.DocumentText;

            // Process in descending offset order
            var candidates = collector.Specs
                .Where(s => s.GroupByClause == null)
                .OrderByDescending(s => s.StartOffset)
                .ToList();

            foreach (var spec in candidates)
            {
                if (context.HasSelection)
                {
                    if (spec.StartOffset < context.SelectionStart ||
                        spec.StartOffset >= context.SelectionStart + context.SelectionLength)
                        continue;
                }

                var nonAggCols = GetNonAggregatedColumnTexts(spec, text);
                if (nonAggCols.Count == 0) continue;

                // Determine insertion point: end of the query spec fragment
                // We insert after the last element in SELECT, before ORDER BY / HAVING
                int insertOffset = spec.StartOffset + spec.FragmentLength;

                // Trim trailing whitespace from insertion offset
                while (insertOffset > 0 && char.IsWhiteSpace(text[insertOffset - 1]))
                    insertOffset--;

                var groupByClause = $"\r\nGROUP BY {string.Join(", ", nonAggCols)}";
                text = text.Insert(insertOffset, groupByClause);
            }

            return (text, []);
        }
        catch (Exception)
        {
            return (context.DocumentText, []);
        }
    }

    private static List<string> GetNonAggregatedColumnTexts(QuerySpecification spec, string text)
    {
        var result = new List<string>();

        foreach (var element in spec.SelectElements)
        {
            if (element is SelectScalarExpression sse)
            {
                if (!IsAggregate(sse.Expression))
                {
                    var colText = text.Substring(sse.Expression.StartOffset, sse.Expression.FragmentLength);
                    // Use alias if present, otherwise the expression text
                    result.Add(colText);
                }
            }
        }

        return result;
    }

    private static bool IsAggregate(ScalarExpression expr)
    {
        if (expr is FunctionCall fc)
        {
            var funcName = fc.FunctionName?.Value;
            if (!string.IsNullOrEmpty(funcName) && AggregateNames.Contains(funcName))
                return true;
        }
        return false;
    }

    private sealed class QuerySpecCollector : TSqlFragmentVisitor
    {
        public List<QuerySpecification> Specs { get; } = [];
        public override void Visit(QuerySpecification node)
        {
            Specs.Add(node);
        }
    }
}
