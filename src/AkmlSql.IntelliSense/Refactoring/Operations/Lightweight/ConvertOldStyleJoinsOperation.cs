using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Refactoring.Operations.Lightweight;

/// <summary>
/// Converts old-style comma-separated joins in the FROM clause to explicit INNER JOIN ... ON syntax.
/// Non-equi-join predicates are left in the WHERE clause with a warning.
/// </summary>
public class ConvertOldStyleJoinsOperation : ILightweightOperation
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

            var warnings = new List<string>();
            // Sort descending to splice from end to start
            var oldStyleSpecs = collector.Specs
                .Where(IsOldStyleJoin)
                .OrderByDescending(s => s.StartOffset)
                .ToList();

            if (oldStyleSpecs.Count == 0)
                return (context.DocumentText, []);

            var text = context.DocumentText;

            foreach (var spec in oldStyleSpecs)
            {
                if (context.HasSelection)
                {
                    if (spec.StartOffset < context.SelectionStart ||
                        spec.StartOffset >= context.SelectionStart + context.SelectionLength)
                        continue;
                }

                var result = RewriteQuery(spec, text, warnings);
                if (result != null)
                    text = result;
            }

            return (text, [.. warnings]);
        }
        catch (Exception)
        {
            return (context.DocumentText, []);
        }
    }

    private static bool IsOldStyleJoin(QuerySpecification spec)
    {
        if (spec.FromClause == null) return false;
        var refs = spec.FromClause.TableReferences;
        // Old-style = multiple NamedTableReference items at top level (no JoinTableReference)
        return refs != null &&
               refs.Count >= 2 &&
               refs.All(r => r is NamedTableReference) &&
               !refs.Any(r => r is JoinTableReference);
    }

    private string? RewriteQuery(
        QuerySpecification spec,
        string originalText,
        List<string> warnings)
    {
        var fromClause = spec.FromClause!;
        var tableRefs = fromClause.TableReferences.Cast<NamedTableReference>().ToList();

        // Build alias → table-name map
        var aliasMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tr in tableRefs)
        {
            var alias = tr.Alias?.Value ?? tr.SchemaObject?.BaseIdentifier?.Value ?? string.Empty;
            var tblName = tr.SchemaObject?.BaseIdentifier?.Value ?? string.Empty;
            if (!string.IsNullOrEmpty(alias))
                aliasMap[alias] = tblName;
        }

        // Partition WHERE predicates
        var equiPredicates = new List<BooleanComparisonExpression>();
        var nonEquiPredicates = new List<BooleanExpression>();

        if (spec.WhereClause?.SearchCondition != null)
            PartitionPredicates(spec.WhereClause.SearchCondition, aliasMap, equiPredicates, nonEquiPredicates);

        // Emit warnings for non-equi predicates
        foreach (var ne in nonEquiPredicates)
        {
            var condText = originalText.Substring(ne.StartOffset, ne.FragmentLength);
            warnings.Add($"Non-equi join condition left in WHERE: {condText}");
        }

        // Build FROM with INNER JOINs
        var sb = new StringBuilder();
        sb.Append("FROM ");
        sb.Append(GetTableRefText(tableRefs[0], originalText));

        for (int i = 1; i < tableRefs.Count; i++)
        {
            sb.Append("\r\nINNER JOIN ");
            sb.Append(GetTableRefText(tableRefs[i], originalText));

            // Find equi predicates that reference this table
            var alias = tableRefs[i].Alias?.Value ?? tableRefs[i].SchemaObject?.BaseIdentifier?.Value ?? string.Empty;
            var onPredicates = equiPredicates
                .Where(p => PredicateInvolvesAlias(p, alias, originalText))
                .ToList();

            if (onPredicates.Count > 0)
            {
                sb.Append(" ON ");
                sb.Append(string.Join(" AND ", onPredicates.Select(p =>
                    originalText.Substring(p.StartOffset, p.FragmentLength))));
            }
        }

        // Build WHERE with remaining non-equi predicates
        string? newWhere = null;
        if (nonEquiPredicates.Count > 0)
        {
            newWhere = "WHERE " + string.Join(" AND ", nonEquiPredicates.Select(p =>
                originalText.Substring(p.StartOffset, p.FragmentLength)));
        }

        // Determine the region to replace: from FROM clause start to end of WHERE clause
        int regionStart = fromClause.StartOffset;
        int regionEnd = fromClause.StartOffset + fromClause.FragmentLength;

        if (spec.WhereClause != null)
            regionEnd = spec.WhereClause.StartOffset + spec.WhereClause.FragmentLength;

        var replacement = sb.ToString();
        if (newWhere != null)
            replacement += "\r\n" + newWhere;

        return originalText[..regionStart] + replacement + originalText[regionEnd..];
    }

    private static string GetTableRefText(NamedTableReference tr, string text)
    {
        return text.Substring(tr.StartOffset, tr.FragmentLength);
    }

    private static void PartitionPredicates(
        BooleanExpression expr,
        Dictionary<string, string> aliasMap,
        List<BooleanComparisonExpression> equi,
        List<BooleanExpression> nonEqui)
    {
        if (expr is BooleanBinaryExpression binary &&
            binary.BinaryExpressionType == BooleanBinaryExpressionType.And)
        {
            PartitionPredicates(binary.FirstExpression, aliasMap, equi, nonEqui);
            PartitionPredicates(binary.SecondExpression, aliasMap, equi, nonEqui);
            return;
        }

        if (expr is BooleanComparisonExpression cmp &&
            cmp.ComparisonType == BooleanComparisonType.Equals)
        {
            var leftAlias = GetColumnAlias(cmp.FirstExpression);
            var rightAlias = GetColumnAlias(cmp.SecondExpression);

            if (leftAlias != null && rightAlias != null &&
                aliasMap.ContainsKey(leftAlias) && aliasMap.ContainsKey(rightAlias) &&
                !leftAlias.Equals(rightAlias, StringComparison.OrdinalIgnoreCase))
            {
                equi.Add(cmp);
                return;
            }
        }

        nonEqui.Add(expr);
    }

    private static string? GetColumnAlias(ScalarExpression expr)
    {
        if (expr is ColumnReferenceExpression col)
        {
            var ids = col.MultiPartIdentifier?.Identifiers;
            if (ids != null && ids.Count >= 2)
                return ids[0].Value;
        }
        return null;
    }

    private static bool PredicateInvolvesAlias(
        BooleanComparisonExpression predicate,
        string alias,
        string text)
    {
        var leftAlias = GetColumnAlias(predicate.FirstExpression);
        var rightAlias = GetColumnAlias(predicate.SecondExpression);
        return string.Equals(leftAlias, alias, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(rightAlias, alias, StringComparison.OrdinalIgnoreCase);
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
