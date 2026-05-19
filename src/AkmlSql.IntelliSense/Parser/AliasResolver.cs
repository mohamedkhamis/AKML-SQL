using Microsoft.SqlServer.TransactSql.ScriptDom;
using ScriptDomTableReference = Microsoft.SqlServer.TransactSql.ScriptDom.TableReference;

namespace AkmlSql.Engine.Parser;

public class AliasResolver
{
    /// <summary>
    /// Walk AST to build alias → (schema, table) dictionary for the SQL statement
    /// containing the cursor. Statements are scoped per-semicolon: aliases from a
    /// previous statement (e.g. <c>SELECT ... FROM A; SELECT ... FROM |</c>) are
    /// NOT included, otherwise <see cref="JoinProvider"/> would suggest cross-statement joins.
    ///
    /// Self-join scenario (T044): handled naturally because each NamedTableReference
    /// with a distinct alias produces a separate entry. For example:
    ///   FROM Employees e1 JOIN Employees e2 ON e1.ManagerId = e2.EmployeeId
    /// yields: { "e1" → dbo.Employees, "e2" → dbo.Employees }.
    /// </summary>
    public Dictionary<string, TableReference> ResolveAliases(TSqlScript? script, int cursorOffset)
    {
        var aliases = new Dictionary<string, TableReference>(StringComparer.OrdinalIgnoreCase);

        if (script == null)
        {
            return aliases;
        }

        foreach (var batch in script.Batches)
        {
            if (cursorOffset < batch.StartOffset ||
                cursorOffset > batch.StartOffset + batch.FragmentLength)
            {
                continue;
            }

            // Find the specific statement that contains the cursor.
            // If the cursor falls between statements (e.g. immediately after a ';'
            // or while typing a new statement), no statement contains it — return empty.
            TSqlStatement? cursorStatement = null;
            foreach (var statement in batch.Statements)
            {
                int stmtStart = statement.StartOffset;
                int stmtEnd = stmtStart + statement.FragmentLength;
                if (cursorOffset >= stmtStart && cursorOffset <= stmtEnd)
                {
                    cursorStatement = statement;
                    break;
                }
            }

            if (cursorStatement == null)
            {
                // Cursor is between statements (after a ';' or in a partial new statement).
                // Don't pull aliases from neighboring statements.
                continue;
            }

            var visitor = new AliasVisitor();
            cursorStatement.Accept(visitor);

            foreach (var (alias, tableRef) in visitor.Aliases)
                aliases[alias] = tableRef;
        }

        return aliases;
    }

    /// <summary>
    /// Like <see cref="ResolveAliases"/>, but restricted to the FROM clause of the
    /// QuerySpecification that immediately contains the cursor. Used by the wildcard
    /// expansion handler so that <c>SELECT * FROM Cte1</c> sees only <c>Cte1</c> —
    /// not the tables referenced inside <c>Cte1</c>'s own body, sibling CTE bodies,
    /// or unrelated subqueries elsewhere in the same statement.
    /// </summary>
    public Dictionary<string, TableReference> ResolveAliasesInCursorScope(
        TSqlScript? script, int cursorOffset)
    {
        var aliases = new Dictionary<string, TableReference>(StringComparer.OrdinalIgnoreCase);
        if (script == null) return aliases;

        // Find the smallest QuerySpecification whose extent contains the cursor.
        var finder = new CursorScopeFinder(cursorOffset);
        foreach (var batch in script.Batches)
        {
            if (cursorOffset < batch.StartOffset ||
                cursorOffset > batch.StartOffset + batch.FragmentLength)
                continue;
            batch.Accept(finder);
        }

        var cursorSpec = finder.Result;
        if (cursorSpec?.FromClause == null) return aliases;

        // Walk only the immediate TableReferences of this FROM clause. Recurse into
        // JoinTableReferences (joins are siblings at the same scope) but NOT into
        // QueryDerivedTables — a derived table's inner FROM is a separate scope.
        foreach (var tableRef in cursorSpec.FromClause.TableReferences)
            CollectFromTableRef(tableRef, aliases);

        return aliases;
    }

    private static void CollectFromTableRef(
        ScriptDomTableReference tref, Dictionary<string, TableReference> aliases)
    {
        switch (tref)
        {
            case NamedTableReference named:
            {
                var tableName = named.SchemaObject?.BaseIdentifier?.Value;
                if (tableName == null) return;
                var schemaName = named.SchemaObject?.SchemaIdentifier?.Value ?? "dbo";
                var alias = named.Alias?.Value;
                var key = !string.IsNullOrEmpty(alias) ? alias! : tableName;
                aliases[key] = new TableReference { SchemaName = schemaName, TableName = tableName };
                return;
            }
            case QueryDerivedTable derived:
            {
                // Collect the alias only — do NOT walk into derived.QueryExpression.
                var alias = derived.Alias?.Value;
                if (!string.IsNullOrEmpty(alias))
                    aliases[alias!] = new TableReference { SchemaName = string.Empty, TableName = $"(derived:{alias})" };
                return;
            }
            case SchemaObjectFunctionTableReference tvf:
            {
                var name = tvf.SchemaObject?.BaseIdentifier?.Value;
                if (name == null) return;
                var schemaName = tvf.SchemaObject?.SchemaIdentifier?.Value ?? "dbo";
                var alias = tvf.Alias?.Value;
                var key = !string.IsNullOrEmpty(alias) ? alias! : name;
                aliases[key] = new TableReference { SchemaName = schemaName, TableName = name };
                return;
            }
            case JoinTableReference join:
            {
                CollectFromTableRef(join.FirstTableReference, aliases);
                CollectFromTableRef(join.SecondTableReference, aliases);
                return;
            }
        }
    }

    private class CursorScopeFinder : TSqlFragmentVisitor
    {
        private readonly int _cursor;
        public QuerySpecification? Result { get; private set; }
        public CursorScopeFinder(int cursor) => _cursor = cursor;

        public override void Visit(QuerySpecification node)
        {
            if (_cursor >= node.StartOffset && _cursor <= node.StartOffset + node.FragmentLength)
            {
                // The default visitor walks children after Visit(). The deepest match
                // wins because we keep overwriting on each match.
                Result = node;
            }
        }
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

                // ReSharper disable once ConvertIfStatementToConditionalTernaryExpression
                if (!string.IsNullOrEmpty(alias))
                {
                    Aliases.Add((alias, tableRef));
                }
                else
                {
                    Aliases.Add((tableName, tableRef));
                }
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

                Aliases.Add(!string.IsNullOrEmpty(alias) ? (alias, tableRef) : (funcName, tableRef));
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
