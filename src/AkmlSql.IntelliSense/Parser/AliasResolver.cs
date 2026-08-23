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
    /// Like <see cref="ResolveAliases"/>, but restricted to the scope that immediately
    /// contains the cursor. Used by the wildcard expansion handler so that
    /// <c>SELECT * FROM Cte1</c> sees only <c>Cte1</c> — not the tables referenced inside
    /// <c>Cte1</c>'s own body, sibling CTE bodies, or unrelated subqueries elsewhere in
    /// the same statement.
    /// <para>
    /// Spec 032 (A3/A4): scopes now include UPDATE/DELETE/MERGE specifications (their
    /// FROM clause + target), not just QuerySpecification. With
    /// <paramref name="includeOuterScopes"/> true (completion), ancestor scopes containing
    /// the cursor are merged in outer-first so correlated subqueries keep the outer
    /// aliases and the INNER scope wins on conflicts. The default (false) keeps the
    /// innermost-scope-only behavior wildcard expansion depends on (`SELECT *` must
    /// expand the inner FROM only).
    /// </para>
    /// </summary>
    public Dictionary<string, TableReference> ResolveAliasesInCursorScope(
        TSqlScript? script, int cursorOffset, bool includeOuterScopes = false)
    {
        var aliases = new Dictionary<string, TableReference>(StringComparer.OrdinalIgnoreCase);
        if (script == null) return aliases;

        // Collect every scope whose extent contains the cursor, in pre-order (outer → inner).
        var finder = new CursorScopeFinder(cursorOffset);
        foreach (var batch in script.Batches)
        {
            if (cursorOffset < batch.StartOffset ||
                cursorOffset > batch.StartOffset + batch.FragmentLength)
                continue;
            batch.Accept(finder);
        }

        if (finder.Scopes.Count == 0) return aliases;

        var scopes = includeOuterScopes
            ? (IEnumerable<TSqlFragment>)finder.Scopes
            : [finder.Scopes[finder.Scopes.Count - 1]];

        // Outer → inner: dictionary overwrite makes the inner scope win on conflicts.
        foreach (var scope in scopes)
            CollectScope(scope, aliases);

        return aliases;
    }

    /// <summary>
    /// Spec 032 (A4) — projected columns of derived tables visible at the cursor, keyed by
    /// alias. Consumed by <c>CompletionEngine</c>, which registers them alongside CTEs so
    /// <c>d.|</c> over <c>FROM (SELECT …) d</c> offers the derived projection instead of
    /// nothing (the alias map itself only carries a <c>(derived:alias)</c> placeholder).
    /// </summary>
    public Dictionary<string, List<string>> ResolveDerivedTableProjections(
        TSqlScript? script, int cursorOffset)
        => ResolveDerivedTableProjections(script, cursorOffset, out _);

    /// <summary>Overload exposing each derived table's SOURCE tables so a `SELECT *` body
    /// (zero inferable columns) can be star-expanded from the schema cache (spec 032 E4).</summary>
    public Dictionary<string, List<string>> ResolveDerivedTableProjections(
        TSqlScript? script, int cursorOffset,
        out Dictionary<string, List<(string Schema, string Table)>> sources)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        sources = new Dictionary<string, List<(string, string)>>(StringComparer.OrdinalIgnoreCase);
        if (script == null) return result;

        foreach (var batch in script.Batches)
        {
            if (cursorOffset < batch.StartOffset ||
                cursorOffset > batch.StartOffset + batch.FragmentLength)
                continue;

            foreach (var statement in batch.Statements)
            {
                if (cursorOffset < statement.StartOffset ||
                    cursorOffset > statement.StartOffset + statement.FragmentLength)
                    continue;

                var visitor = new DerivedTableVisitor();
                statement.Accept(visitor);
                foreach (var derived in visitor.DerivedTables)
                {
                    var alias = derived.Alias?.Value;
                    if (string.IsNullOrEmpty(alias) || result.ContainsKey(alias!)) continue;

                    var columns = new List<string>();
                    CteResolver.InferColumnsFromQuery(derived.QueryExpression, columns, result);
                    result[alias!] = columns; // empty for `SELECT *` bodies — expanded via sources

                    if (derived.QueryExpression is QuerySpecification { FromClause: not null } qs)
                    {
                        var srcList = new List<(string, string)>();
                        foreach (var tref in qs.FromClause.TableReferences)
                            CollectNamedSources(tref, srcList);
                        if (srcList.Count > 0) sources[alias!] = srcList;
                    }
                }
            }
        }

        return result;
    }

    private static void CollectNamedSources(ScriptDomTableReference tref, List<(string, string)> sources)
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

    /// <summary>Collects the aliases a single scope contributes (spec 032 A3).</summary>
    private static void CollectScope(TSqlFragment scope, Dictionary<string, TableReference> aliases)
    {
        switch (scope)
        {
            case QuerySpecification qs:
                if (qs.FromClause == null) return;
                // Walk only the immediate TableReferences of this FROM clause. Recurse into
                // JoinTableReferences (joins are siblings at the same scope) but NOT into
                // QueryDerivedTables — a derived table's inner FROM is a separate scope.
                foreach (var tableRef in qs.FromClause.TableReferences)
                    CollectFromTableRef(tableRef, aliases);
                return;

            case UpdateSpecification u:
                // Target FIRST, FROM second: in `UPDATE o SET … FROM Orders o` the target is
                // the bare alias — the FROM mapping must overwrite it (the token-fallback
                // equivalent of "FROM/JOIN wins", cluster A2).
                if (u.Target != null) CollectFromTableRef(u.Target, aliases);
                if (u.FromClause != null)
                    foreach (var tableRef in u.FromClause.TableReferences)
                        CollectFromTableRef(tableRef, aliases);
                return;

            case DeleteSpecification d:
                if (d.Target != null) CollectFromTableRef(d.Target, aliases);
                if (d.FromClause != null)
                    foreach (var tableRef in d.FromClause.TableReferences)
                        CollectFromTableRef(tableRef, aliases);
                return;

            case MergeSpecification m:
                if (m.Target != null) CollectFromTableRef(m.Target, aliases);
                // ScriptDom quirk: the MERGE target's alias (`MERGE dbo.Orders AS tgt`) lives on
                // MergeSpecification.TableAlias — NOT on the target NamedTableReference — so the
                // plain collect registers the bare table name; re-key it under the alias.
                if (m.TableAlias?.Value is { Length: > 0 } mergeAlias && m.Target is NamedTableReference mergeTarget)
                {
                    var targetName = mergeTarget.SchemaObject?.BaseIdentifier?.Value;
                    if (targetName != null)
                    {
                        aliases.Remove(targetName);
                        aliases[mergeAlias] = new TableReference
                        {
                            SchemaName = mergeTarget.SchemaObject?.SchemaIdentifier?.Value ?? "dbo",
                            TableName = targetName,
                        };
                    }
                }
                if (m.TableReference != null) CollectFromTableRef(m.TableReference, aliases);
                return;
        }
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

        /// <summary>Every scope containing the cursor, in pre-order (outer → inner) —
        /// the visitor walks parents before children, so append order IS nesting order.
        /// Spec 032 A3: UPDATE/DELETE/MERGE specifications count as scopes too.</summary>
        public List<TSqlFragment> Scopes { get; } = [];

        public CursorScopeFinder(int cursor) => _cursor = cursor;

        private bool Contains(TSqlFragment node)
            => _cursor >= node.StartOffset && _cursor <= node.StartOffset + node.FragmentLength;

        public override void Visit(QuerySpecification node)
        {
            if (Contains(node)) Scopes.Add(node);
        }

        public override void Visit(UpdateSpecification node)
        {
            if (Contains(node)) Scopes.Add(node);
        }

        public override void Visit(DeleteSpecification node)
        {
            if (Contains(node)) Scopes.Add(node);
        }

        public override void Visit(MergeSpecification node)
        {
            if (Contains(node)) Scopes.Add(node);
        }
    }

    /// <summary>Collects every derived table in a statement (spec 032 A4).</summary>
    private class DerivedTableVisitor : TSqlFragmentVisitor
    {
        public List<QueryDerivedTable> DerivedTables { get; } = [];

        public override void Visit(QueryDerivedTable node) => DerivedTables.Add(node);
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
