using Microsoft.SqlServer.TransactSql.ScriptDom;
// ReSharper disable UnusedMember.Global
// ReSharper disable UseIndexFromEndExpression

namespace AkmlSql.Engine.Parser;

/// <summary>
/// T070: Walks AST for CREATE TABLE #name and SELECT INTO #name statements
/// to extract temp table names and their column definitions.
/// </summary>
public class TempTableTracker
{
    /// <summary>
    /// Track all temp tables declared in the script visible at the cursor offset.
    /// Returns a dictionary of temp table name → column names.
    /// </summary>
    /// <summary>
    /// Spec 032 F3: for `SELECT * INTO #t FROM src` shapes, the source tables per temp
    /// table — populated by the latest <see cref="TrackTempTables"/> call so the engine
    /// can star-expand empty column lists from the schema cache.
    /// </summary>
    public Dictionary<string, List<(string Schema, string Table)>> StarSources { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, List<string>> TrackTempTables(TSqlScript? script, int cursorOffset)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        if (script == null)
        {
            return result;
        }

        // Spec 032 F2: when the trailing statement is mid-edit, the parsed extent shrinks
        // and the cursor falls outside every batch — which used to drop ALL definitions.
        // Definitions BEFORE the caret are what matter: fall back to the last batch.
        var containing = script.Batches.FirstOrDefault(b =>
            cursorOffset >= b.StartOffset && cursorOffset <= b.StartOffset + b.FragmentLength)
            ?? script.Batches.LastOrDefault();
        if (containing == null)
        {
            return result;
        }

        var visitor = new TempTableVisitor(cursorOffset);
        containing.Accept(visitor);

        foreach (var (name, columns) in visitor.TempTables)
        {
            // Later declarations overwrite earlier ones (e.g., drop and recreate)
            result[name] = columns;
        }

        foreach (var (name, srcList) in visitor.StarSources)
        {
            StarSources[name] = srcList;
        }

        return result;
    }

    private class TempTableVisitor(int cursorOffset) : TSqlFragmentVisitor
    {
        public List<(string Name, List<string> Columns)> TempTables { get; } = [];

        /// <summary>Spec 032 F3: source tables of `SELECT * INTO #t FROM src` definitions.</summary>
        public Dictionary<string, List<(string Schema, string Table)>> StarSources { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Handle CREATE TABLE #tempName (col1 type1, col2 type2, ...)
        /// </summary>
        public override void Visit(CreateTableStatement node)
        {
            // Only include definitions that appear before the cursor
            if (node.StartOffset > cursorOffset)
            {
                return;
            }

            var tableName = GetTableName(node.SchemaObjectName);
            if (tableName == null || !tableName.StartsWith("#"))
            {
                return;
            }

            var columns = new List<string>();
            if (node.Definition?.ColumnDefinitions != null)
            {
                foreach (var colDef in node.Definition.ColumnDefinitions)
                {
                    if (colDef.ColumnIdentifier?.Value != null)
                    {
                        columns.Add(colDef.ColumnIdentifier.Value);
                    }
                }
            }

            TempTables.Add((tableName, columns));
        }

        /// <summary>
        /// Handle SELECT ... INTO #tempName
        /// </summary>
        public override void Visit(SelectStatement node)
        {
            // Only include definitions that appear before the cursor
            if (node.StartOffset > cursorOffset)
            {
                return;
            }

            if (node.Into == null)
            {
                return;
            }

            if (node.QueryExpression is not QuerySpecification querySpec)
            {
                return;
            }

            var tableName = GetTableName(node.Into);
            if (tableName == null || !tableName.StartsWith("#"))
            {
                return;
            }

            // Infer columns from the SELECT list
            var columns = new List<string>();
            foreach (var element in querySpec.SelectElements)
            {
                switch (element)
                {
                    case SelectScalarExpression scalar:
                        if (scalar.ColumnName != null)
                        {
                            columns.Add(scalar.ColumnName.Value);
                        }
                        else if (scalar.Expression is ColumnReferenceExpression colRef)
                        {
                            var identifiers = colRef.MultiPartIdentifier?.Identifiers;
                            if (identifiers is { Count: > 0 })
                            {
                                columns.Add(identifiers[identifiers.Count - 1].Value);
                            }
                        }
                        else
                        {
                            columns.Add($"Column{columns.Count + 1}");
                        }
                        break;

                    case SelectStarExpression:
                        // SELECT * INTO #temp — no schema info HERE; record the FROM sources
                        // so the engine can star-expand from the cache (spec 032 F3).
                        if (querySpec.FromClause != null)
                        {
                            var sources = StarSources.TryGetValue(tableName, out var existing)
                                ? existing
                                : StarSources[tableName] = [];
                            foreach (var tref in querySpec.FromClause.TableReferences)
                                CollectNamedSources(tref, sources);
                        }
                        break;
                }
            }

            TempTables.Add((tableName, columns));
        }

        private static void CollectNamedSources(
            Microsoft.SqlServer.TransactSql.ScriptDom.TableReference tref, List<(string, string)> sources)
        {
            switch (tref)
            {
                case NamedTableReference named when named.SchemaObject?.BaseIdentifier?.Value is { } name:
                    sources.Add((named.SchemaObject?.SchemaIdentifier?.Value ?? "dbo", name));
                    return;
                case JoinTableReference join:
                    CollectNamedSources(join.FirstTableReference, sources);
                    CollectNamedSources(join.SecondTableReference, sources);
                    return;
            }
        }

        private static string? GetTableName(SchemaObjectName? schemaObjectName)
        {
            if (schemaObjectName == null)
            {
                return null;
            }

            return schemaObjectName.BaseIdentifier?.Value;
        }
    }
}
