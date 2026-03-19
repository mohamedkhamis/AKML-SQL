using Microsoft.SqlServer.TransactSql.ScriptDom;
using Serilog;

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
    public Dictionary<string, List<string>> TrackTempTables(TSqlScript script, int cursorOffset)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        if (script == null)
            return result;

        foreach (var batch in script.Batches)
        {
            if (cursorOffset < batch.StartOffset ||
                cursorOffset > batch.StartOffset + batch.FragmentLength)
                continue;

            var visitor = new TempTableVisitor(cursorOffset);
            batch.Accept(visitor);

            foreach (var (name, columns) in visitor.TempTables)
            {
                // Later declarations overwrite earlier ones (e.g., drop and recreate)
                result[name] = columns;
            }
        }

        return result;
    }

    private class TempTableVisitor : TSqlFragmentVisitor
    {
        private readonly int _cursorOffset;
        public List<(string Name, List<string> Columns)> TempTables { get; } = [];

        public TempTableVisitor(int cursorOffset)
        {
            _cursorOffset = cursorOffset;
        }

        /// <summary>
        /// Handle CREATE TABLE #tempName (col1 type1, col2 type2, ...)
        /// </summary>
        public override void Visit(CreateTableStatement node)
        {
            // Only include definitions that appear before the cursor
            if (node.StartOffset > _cursorOffset)
                return;

            var tableName = GetTableName(node.SchemaObjectName);
            if (tableName == null || !tableName.StartsWith("#"))
                return;

            var columns = new List<string>();
            if (node.Definition?.ColumnDefinitions != null)
            {
                foreach (var colDef in node.Definition.ColumnDefinitions)
                {
                    if (colDef.ColumnIdentifier?.Value != null)
                        columns.Add(colDef.ColumnIdentifier.Value);
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
            if (node.StartOffset > _cursorOffset)
                return;

            if (node.Into == null)
                return;

            if (node.QueryExpression is not QuerySpecification querySpec)
                return;

            var tableName = GetTableName(node.Into);
            if (tableName == null || !tableName.StartsWith("#"))
                return;

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
                                columns.Add(identifiers[identifiers.Count - 1].Value);
                        }
                        else
                        {
                            columns.Add($"Column{columns.Count + 1}");
                        }
                        break;

                    case SelectStarExpression:
                        // SELECT * INTO #temp — can't resolve columns without schema info
                        break;
                }
            }

            TempTables.Add((tableName, columns));
        }

        private static string? GetTableName(SchemaObjectName? schemaObjectName)
        {
            if (schemaObjectName == null)
                return null;

            return schemaObjectName.BaseIdentifier?.Value;
        }
    }
}
