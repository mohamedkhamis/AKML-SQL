using System.Diagnostics.CodeAnalysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;
// ReSharper disable UnusedMember.Global

namespace AkmlSql.Engine.Parser;

/// <summary>
/// T069, T072: Walks AST CommonTableExpression nodes to extract CTE names and their column lists.
/// Handles both explicit column lists and inferred columns from the SELECT clause.
/// Supports nested CTE resolution where one CTE references another.
/// </summary>
// ReSharper disable once UnusedMember.Global
[SuppressMessage("ReSharper", "GrammarMistakeInComment")]
public class CteResolver
{
    /// <summary>
    /// Resolve all CTEs visible at the given cursor offset.
    /// Returns a dictionary of CTE name → column names.
    /// </summary>
    public Dictionary<string, List<string>> ResolveCtes(TSqlScript? script, int cursorOffset)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        if (script == null)
        {
            return result;
        }

        foreach (var batch in script.Batches)
        {
            if (cursorOffset < batch.StartOffset ||
                cursorOffset > batch.StartOffset + batch.FragmentLength)
            {
                continue;
            }

            var visitor = new CteVisitor();
            batch.Accept(visitor);

            // T072: Resolve CTEs in order (later CTEs can reference earlier ones).
            // Exclude the CTE whose own QueryExpression contains the cursor — a
            // non-recursive CTE can't reference itself, so suggesting its name as
            // a FROM target inside its own body would be misleading.
            foreach (var cte in visitor.Ctes)
            {
                var qe = cte.QueryExpression;
                if (qe != null &&
                    cursorOffset > qe.StartOffset &&
                    cursorOffset <= qe.StartOffset + qe.FragmentLength)
                {
                    continue;
                }
                var columns = ResolveCteColumns(cte, result);
                result[cte.ExpressionName.Value] = columns;
            }
        }

        return result;
    }

    /// <summary>
    /// Resolve columns for a single CTE, handling both explicit column lists
    /// and inferred columns from the SELECT.
    /// </summary>
    private static List<string> ResolveCteColumns(
        CommonTableExpression cte,
        Dictionary<string, List<string>> resolvedCtes)
    {
        var columns = new List<string>();

        // Case 1: Explicit column list — WITH cte (col1, col2) AS (...)
        if (cte.Columns.Count > 0)
        {
            foreach (var col in cte.Columns)
                columns.Add(col.Value);
            return columns;
        }

        // Case 2: Infer columns from the SELECT of the CTE query
        var queryExpression = cte.QueryExpression;
        InferColumnsFromQuery(queryExpression, columns, resolvedCtes);

        return columns;
    }

    /// <summary>
    /// T072: Infer column names from a query expression, handling nested CTE references.
    /// </summary>
    private static void InferColumnsFromQuery(
        QueryExpression? queryExpression,
        List<string> columns,
        Dictionary<string, List<string>> resolvedCtes)
    {
        if (queryExpression is QuerySpecification querySpec)
        {
            foreach (var element in querySpec.SelectElements)
            {
                switch (element)
                {
                    case SelectScalarExpression scalar:
                        if (scalar.ColumnName != null)
                        {
                            // Aliased: SELECT expr AS alias
                            columns.Add(scalar.ColumnName.Value);
                        }
                        else if (scalar.Expression is ColumnReferenceExpression colRef)
                        {
                            // Direct column reference: SELECT col or SELECT t.col
                            var identifiers = colRef.MultiPartIdentifier?.Identifiers;
                            if (identifiers is { Count: > 0 })
                            {
                                // ReSharper disable once UseIndexFromEndExpression
                                columns.Add(identifiers[identifiers.Count - 1].Value);
                            }
                        }
                        else
                        {
                            // Expression without alias — use a placeholder
                            columns.Add($"Expr{columns.Count + 1}");
                        }
                        break;

                    case SelectStarExpression star:
                        // SELECT * — try to resolve from known CTEs or tables
                        if (star.Qualifier != null && star.Qualifier.Identifiers.Count > 0)
                        {
                            // ReSharper disable once UseIndexFromEndExpression
                            var qualifierName = star.Qualifier.Identifiers[star.Qualifier.Identifiers.Count - 1].Value;
                            if (resolvedCtes.TryGetValue(qualifierName, out var cteColumns))
                            {
                                columns.AddRange(cteColumns);
                            }
                        }
                        // Unqualified SELECT * — we can't fully resolve without schema info
                        break;
                }
            }
        }
        else if (queryExpression is BinaryQueryExpression binaryQuery)
        {
            // UNION/INTERSECT/EXCEPT — columns come from the first query
            InferColumnsFromQuery(binaryQuery.FirstQueryExpression, columns, resolvedCtes);
        }
        else if (queryExpression is QueryParenthesisExpression parenQuery)
        {
            InferColumnsFromQuery(parenQuery.QueryExpression, columns, resolvedCtes);
        }
    }

    private class CteVisitor : TSqlFragmentVisitor
    {
        public List<CommonTableExpression> Ctes { get; } = [];

        public override void Visit(CommonTableExpression node)
        {
            Ctes.Add(node);
        }
    }
}
