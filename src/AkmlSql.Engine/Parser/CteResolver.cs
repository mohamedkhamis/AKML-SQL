using System.Diagnostics.CodeAnalysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using ScriptDomTableReference = Microsoft.SqlServer.TransactSql.ScriptDom.TableReference;
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
    /// Resolve CTEs visible at the cursor, returning each CTE's underlying source
    /// tables (the tables referenced in the CTE body's FROM/JOIN clauses). Used by
    /// <c>JoinOnFkProvider</c> to look up FK relationships between two CTEs by
    /// walking through to the real tables they're built on.
    /// </summary>
    public Dictionary<string, List<(string Schema, string Table)>> ResolveCteSources(
        TSqlScript? script, int cursorOffset)
    {
        var result = new Dictionary<string, List<(string, string)>>(StringComparer.OrdinalIgnoreCase);
        if (script == null) return result;

        foreach (var batch in script.Batches)
        {
            if (cursorOffset < batch.StartOffset ||
                cursorOffset > batch.StartOffset + batch.FragmentLength)
                continue;

            var visitor = new CteVisitor();
            batch.Accept(visitor);

            foreach (var cte in visitor.Ctes)
            {
                var qe = cte.QueryExpression;
                if (qe != null &&
                    cursorOffset > qe.StartOffset &&
                    cursorOffset <= qe.StartOffset + qe.FragmentLength)
                    continue;

                var sources = new List<(string, string)>();
                CollectNamedTablesFromQuery(qe, sources);
                result[cte.ExpressionName.Value] = sources;
            }
        }

        return result;
    }

    private static void CollectNamedTablesFromQuery(
        QueryExpression? qe, List<(string Schema, string Table)> sources)
    {
        if (qe is QuerySpecification qs && qs.FromClause != null)
        {
            foreach (var tref in qs.FromClause.TableReferences)
                CollectNamedTablesFromTableRef(tref, sources);
        }
        else if (qe is BinaryQueryExpression bin)
        {
            CollectNamedTablesFromQuery(bin.FirstQueryExpression, sources);
            CollectNamedTablesFromQuery(bin.SecondQueryExpression, sources);
        }
        else if (qe is QueryParenthesisExpression paren)
        {
            CollectNamedTablesFromQuery(paren.QueryExpression, sources);
        }
    }

    private static void CollectNamedTablesFromTableRef(
        ScriptDomTableReference tref, List<(string Schema, string Table)> sources)
    {
        switch (tref)
        {
            case NamedTableReference named:
                var name = named.SchemaObject?.BaseIdentifier?.Value;
                if (!string.IsNullOrEmpty(name))
                {
                    var schema = named.SchemaObject?.SchemaIdentifier?.Value ?? "dbo";
                    sources.Add((schema, name!));
                }
                return;
            case QualifiedJoin qj:
                CollectNamedTablesFromTableRef(qj.FirstTableReference, sources);
                CollectNamedTablesFromTableRef(qj.SecondTableReference, sources);
                return;
            case UnqualifiedJoin uj:
                CollectNamedTablesFromTableRef(uj.FirstTableReference, sources);
                CollectNamedTablesFromTableRef(uj.SecondTableReference, sources);
                return;
            // QueryDerivedTable and TVF — skip; their inner contents are not
            // direct sources of the CTE projection.
        }
    }

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
