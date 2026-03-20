using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Parser;

public class AliasResolver
{
    /// <summary>
    /// Walk AST to build alias → (schema, table) dictionary for a batch.
    /// Self-join scenario (T044): handled naturally because each NamedTableReference
    /// with a distinct alias produces a separate entry. For example:
    ///   FROM Employees e1 JOIN Employees e2 ON e1.ManagerId = e2.EmployeeId
    /// yields: { "e1" → dbo.Employees, "e2" → dbo.Employees }.
    /// </summary>
    public Dictionary<string, TableReference> ResolveAliases(TSqlScript script, int cursorOffset)
    {
        var aliases = new Dictionary<string, TableReference>(StringComparer.OrdinalIgnoreCase);

        if (script == null) return aliases;

        foreach (var batch in script.Batches)
        {
            if (cursorOffset < batch.StartOffset ||
                cursorOffset > batch.StartOffset + batch.FragmentLength)
                continue;

            var visitor = new AliasVisitor();
            batch.Accept(visitor);

            foreach (var (alias, tableRef) in visitor.Aliases)
                aliases[alias] = tableRef;
        }

        return aliases;
    }

    private class AliasVisitor : TSqlFragmentVisitor
    {
        public List<(string Alias, TableReference Ref)> Aliases { get; } = [];

        public override void Visit(NamedTableReference node)
        {
            var tableName = node.SchemaObject?.BaseIdentifier?.Value;
            var schemaName = node.SchemaObject?.SchemaIdentifier?.Value ?? "dbo";
            var alias = node.Alias?.Value;

            if (tableName != null)
            {
                var tableRef = new TableReference
                {
                    SchemaName = schemaName,
                    TableName = tableName
                };

                if (!string.IsNullOrEmpty(alias))
                    Aliases.Add((alias, tableRef));
                else
                    Aliases.Add((tableName, tableRef));
            }
        }

        /// <summary>
        /// Handle derived tables (subqueries in FROM) with aliases.
        /// e.g., FROM (SELECT ...) AS dt
        /// </summary>
        public override void Visit(QueryDerivedTable node)
        {
            var alias = node.Alias?.Value;
            if (!string.IsNullOrEmpty(alias))
            {
                Aliases.Add((alias, new TableReference
                {
                    SchemaName = string.Empty,
                    TableName = $"(derived:{alias})"
                }));
            }
        }

        /// <summary>
        /// Handle table-valued function calls with aliases.
        /// e.g., FROM dbo.MyFunc(1) AS f
        /// </summary>
        public override void Visit(SchemaObjectFunctionTableReference node)
        {
            var funcName = node.SchemaObject?.BaseIdentifier?.Value;
            var schemaName = node.SchemaObject?.SchemaIdentifier?.Value ?? "dbo";
            var alias = node.Alias?.Value;

            if (funcName != null)
            {
                var tableRef = new TableReference
                {
                    SchemaName = schemaName,
                    TableName = funcName
                };

                if (!string.IsNullOrEmpty(alias))
                    Aliases.Add((alias, tableRef));
                else
                    Aliases.Add((funcName, tableRef));
            }
        }
    }
}

public class TableReference
{
    public string SchemaName { get; set; } = "dbo";
    public string TableName { get; set; } = string.Empty;

    public string FullName => $"{SchemaName}.{TableName}";
}
