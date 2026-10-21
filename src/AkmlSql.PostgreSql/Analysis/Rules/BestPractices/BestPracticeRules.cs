using AkmlSql.Core.Models.Analysis;
using Google.Protobuf;
using PgSqlParser;

namespace AkmlSql.PostgreSql.Analysis.Rules.BestPractices;

// Spec 042 (task T164, FR-150, FR-153): the best-practice rules PGBP001 to PGBP010.

/// <summary>PGBP001 - INSERT without a column list depends on the table's column order.</summary>
public sealed class Pgbp001InsertWithoutColumns : PgRuleBase
{
    public Pgbp001InsertWithoutColumns() : base("PGBP001", "BestPractices", "INSERT without a column list", DiagnosticSeverity.Warning,
        "Without a column list, INSERT matches values to the table's columns by position. Dropping a column, or rebuilding the table with its columns in another order, "
        + "changes what the statement means or makes it fail. Naming the columns keeps it correct.",
        "sql-insert") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            foreach (var insert in s.All<InsertStmt>())
            {
                // INSERT ... DEFAULT VALUES has no source query and needs no list.
                if (insert.Cols.Count == 0 && insert.SelectStmt != null)
                    report.Node(s, insert.Relation, "List the target columns: INSERT INTO table (column, ...).");
            }
        }
    }
}

/// <summary>PGBP002 - UPDATE or DELETE without WHERE changes every row.</summary>
public sealed class Pgbp002WriteWithoutWhere : PgRuleBase
{
    public Pgbp002WriteWithoutWhere() : base("PGBP002", "BestPractices", "UPDATE or DELETE without WHERE", DiagnosticSeverity.Error,
        "UPDATE and DELETE without a WHERE clause apply to every row of the table, and in autocommit mode the change is committed at once. "
        + "Write WHERE true to say it is intended, or use TRUNCATE to empty a table.",
        "sql-update") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            foreach (var update in s.All<UpdateStmt>())
            {
                if (update.WhereClause == null)
                    report.Node(s, update.Relation, "UPDATE without WHERE changes every row of the table.");
            }
            foreach (var delete in s.All<DeleteStmt>())
            {
                if (delete.WhereClause == null)
                    report.Node(s, delete.Relation, "DELETE without WHERE removes every row of the table.");
            }
        }
    }
}

/// <summary>PGBP003 - tables joined with commas.</summary>
public sealed class Pgbp003CommaJoin : PgRuleBase
{
    public Pgbp003CommaJoin() : base("PGBP003", "BestPractices", "Comma-separated table list", DiagnosticSeverity.Information,
        "FROM a, b WHERE a.id = b.id is the pre-SQL-92 way to join. The join condition sits in WHERE, where forgetting it silently produces a cross join, "
        + "and outer joins cannot be written. JOIN ... ON keeps the condition with the join. Set-returning functions and LATERAL items are not reported.",
        "queries-table-expressions") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            foreach (var select in s.All<SelectStmt>()) Check(s, report, select.FromClause);
            foreach (var update in s.All<UpdateStmt>()) Check(s, report, update.FromClause);
            foreach (var delete in s.All<DeleteStmt>()) Check(s, report, delete.UsingClause);
        }
    }

    private static void Check(PgScriptStatement s, PgReporter report, IList<Node> items)
    {
        if (items.Count < 2) return;
        var second = items.Skip(1).FirstOrDefault(i => i.RangeVar != null || i.JoinExpr != null || i.RangeSubselect is { Lateral: false });
        if (second == null) return;

        const string message = "Tables are listed with commas; join them with JOIN ... ON.";
        if (second.RangeVar != null) report.Node(s, second.RangeVar, message);
        else report.Statement(s, message);
    }
}

/// <summary>PGBP004 - several tables in FROM, some without an alias.</summary>
public sealed class Pgbp004MissingAliases : PgRuleBase
{
    public Pgbp004MissingAliases() : base("PGBP004", "BestPractices", "Tables without aliases in a multi-table query", DiagnosticSeverity.Information,
        "When a query reads from several tables, columns should be qualified with a short alias; without aliases the same table cannot appear twice, "
        + "and a column added to another table later can make an unqualified name ambiguous.",
        "queries-table-expressions") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            foreach (var select in s.All<SelectStmt>())
            {
                var relations = PgTreeFacts.RelationsIn(select.FromClause).ToList();
                if (relations.Count < 2) continue;
                foreach (var relation in relations.Where(r => r.Alias == null))
                    report.Node(s, relation, $"Give {PgTreeFacts.QualifiedName(relation)} an alias; the query reads from {relations.Count} tables.");
            }
        }
    }
}

/// <summary>PGBP005 - an unqualified column name that two of the query's tables both have.</summary>
public sealed class Pgbp005AmbiguousColumn : PgRuleBase
{
    public Pgbp005AmbiguousColumn() : base("PGBP005", "BestPractices", "Ambiguous unqualified column", DiagnosticSeverity.Warning,
        "PostgreSQL refuses a query in which an unqualified column name belongs to more than one table of the FROM list (\"column reference is ambiguous\"). "
        + "Columns named in JOIN ... USING are merged and are exempt. Needs the schema to know each table's columns.",
        "queries-table-expressions", requiresSchema: true) { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        var cache = ctx.SchemaCache;
        if (cache == null) return;

        foreach (var s in ctx.Statements)
        {
            foreach (var select in s.All<SelectStmt>())
            {
                if (select.Op != SetOperation.SetopNone || select.FromClause.Count == 0) continue;
                var joins = PgTreeFacts.JoinsIn(select.FromClause).ToList();
                if (joins.Any(j => j.IsNatural)) continue;

                var tables = PgTreeFacts.RelationsIn(select.FromClause)
                    .Select(r => PgSchemaLookup.FindRelation(cache, r))
                    .Where(t => t != null)
                    .Cast<AkmlSql.Engine.Schema.Models.DatabaseObject>()
                    .ToList();
                if (tables.Count < 2) continue;

                var merged = new HashSet<string>(joins.SelectMany(j => j.UsingClause).Select(n => n.String?.Sval ?? string.Empty), StringComparer.OrdinalIgnoreCase);
                var outputNames = new HashSet<string>(select.TargetList.Select(t => t.ResTarget?.Name ?? string.Empty), StringComparer.OrdinalIgnoreCase);

                foreach (var (reference, isOrdering) in ColumnsOfThisQuery(select))
                {
                    var parts = PgTreeFacts.Parts(reference);
                    if (parts.Count != 1 || parts[0] == "*" || merged.Contains(parts[0])) continue;
                    if (isOrdering && outputNames.Contains(parts[0])) continue;      // ORDER BY / GROUP BY may name an output column

                    var owners = tables.Where(t => PgSchemaLookup.FindColumn(t, parts[0]) != null).Select(t => t.ObjectName).ToList();
                    if (owners.Count >= 2)
                        report.Node(s, reference, $"Column {parts[0]} exists in {string.Join(" and ", owners)}; qualify it with a table alias.");
                }
            }
        }
    }

    // The column references that belong to this query's own scope: not those of a subquery, which has its own FROM.
    private static IEnumerable<(ColumnRef Reference, bool IsOrdering)> ColumnsOfThisQuery(SelectStmt select)
    {
        var found = new List<(ColumnRef, bool)>();

        void Collect(IEnumerable<Node> roots, bool ordering)
        {
            foreach (var root in roots)
            {
                root.Unwrap().Walk(v =>
                {
                    if (v.Node is SelectStmt) return WalkAction.SkipChildren;
                    if (v.Node is ColumnRef c) found.Add((c, ordering));
                    return WalkAction.Continue;
                });
            }
        }

        Collect(select.TargetList, false);
        Collect(select.WhereClause != null ? new[] { select.WhereClause } : Array.Empty<Node>(), false);
        Collect(select.HavingClause != null ? new[] { select.HavingClause } : Array.Empty<Node>(), false);
        Collect(select.GroupClause, true);
        Collect(select.SortClause, true);
        Collect(PgTreeFacts.JoinsIn(select.FromClause).Where(j => j.Quals != null).Select(j => j.Quals), false);
        return found;
    }
}

/// <summary>PGBP006 - SELECT ... INTO creates a table, not a variable.</summary>
public sealed class Pgbp006SelectInto : PgRuleBase
{
    public Pgbp006SelectInto() : base("PGBP006", "BestPractices", "SELECT INTO a table", DiagnosticSeverity.Information,
        "In SQL, SELECT ... INTO new_table creates a table; inside PL/pgSQL the same words assign variables, which is a common mix-up. "
        + "CREATE TABLE AS says what the statement does, and accepts IF NOT EXISTS and storage options.",
        "sql-selectinto") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            foreach (var select in s.All<SelectStmt>())
            {
                if (select.IntoClause?.Rel != null)
                    report.Node(s, select.IntoClause.Rel, "SELECT ... INTO creates a table; use CREATE TABLE ... AS SELECT.");
            }
        }
    }
}

/// <summary>PGBP007 - BEGIN with no COMMIT or ROLLBACK later in the script.</summary>
public sealed class Pgbp007OpenTransaction : PgRuleBase
{
    public Pgbp007OpenTransaction() : base("PGBP007", "BestPractices", "Transaction never ended", DiagnosticSeverity.Warning,
        "A transaction that is not ended holds its locks and its snapshot until the session closes, blocks VACUUM from removing dead rows, and loses its changes if the "
        + "connection drops. End every BEGIN or START TRANSACTION with COMMIT or ROLLBACK.",
        "sql-begin") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        PgScriptStatement? open = null;
        foreach (var s in ctx.Statements)
        {
            var t = s.Stmt?.TransactionStmt;
            if (t == null) continue;
            switch (t.Kind)
            {
                case TransactionStmtKind.TransStmtBegin:
                case TransactionStmtKind.TransStmtStart:
                    open ??= s;
                    break;
                case TransactionStmtKind.TransStmtCommit:
                case TransactionStmtKind.TransStmtRollback:
                case TransactionStmtKind.TransStmtPrepare:
                    if (!t.Chain) open = null;     // COMMIT AND CHAIN starts the next transaction at once
                    break;
            }
        }

        if (open != null)
            report.Statement(open, "This transaction is not ended by a COMMIT or ROLLBACK later in the script.");
    }
}

/// <summary>PGBP008 - SET search_path changes what the unqualified names after it mean.</summary>
public sealed class Pgbp008SetSearchPath : PgRuleBase
{
    public Pgbp008SetSearchPath() : base("PGBP008", "BestPractices", "SET search_path in a script", DiagnosticSeverity.Information,
        "Unqualified table and function names are looked up in the schemas of search_path, in order. Changing it in the middle of a script changes what every later "
        + "unqualified name refers to, and the setting lasts for the session unless SET LOCAL is used inside a transaction.",
        "ddl-schemas") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            var set = s.Stmt?.VariableSetStmt;
            if (set != null && string.Equals(set.Name, "search_path", StringComparison.OrdinalIgnoreCase)
                && set.Kind is VariableSetKind.VarSetValue or VariableSetKind.VarSetCurrent or VariableSetKind.VarSetMulti)
            {
                report.Statement(s, "search_path is changed here; later unqualified names may mean different objects.");
                continue;
            }

            foreach (var call in s.All<FuncCall>())
            {
                if (PgTreeFacts.FunctionName(call) == "set_config" && call.Args.Count > 0
                    && string.Equals(PgTreeFacts.StringConstant(call.Args[0]), "search_path", StringComparison.OrdinalIgnoreCase))
                    report.Node(s, call, "search_path is changed here; later unqualified names may mean different objects.");
            }
        }
    }
}

/// <summary>PGBP009 - NATURAL JOIN joins on whatever columns happen to share a name.</summary>
public sealed class Pgbp009NaturalJoin : PgRuleBase
{
    public Pgbp009NaturalJoin() : base("PGBP009", "BestPractices", "NATURAL JOIN", DiagnosticSeverity.Warning,
        "NATURAL JOIN joins on every pair of columns with the same name. Adding a column to either table (a created_at, an id) changes the join silently. "
        + "JOIN ... USING (column) names the columns and keeps the merged-column behaviour.",
        "queries-table-expressions") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            if (!s.All<JoinExpr>().Any(j => j.IsNatural)) continue;
            var sig = s.Tokens.Sig;
            for (var i = 0; i < sig.Count; i++)
            {
                if (sig[i].IsWordEqual("NATURAL"))
                    report.Token(sig[i], "NATURAL JOIN joins on every column the tables share by name; use JOIN ... USING (column).");
            }
        }
    }
}

/// <summary>PGBP010 - a WITH query nothing refers to.</summary>
public sealed class Pgbp010UnusedCte : PgRuleBase
{
    public Pgbp010UnusedCte() : base("PGBP010", "BestPractices", "Unused common table expression", DiagnosticSeverity.Information,
        "A WITH query that the rest of the statement never reads is dead code (PostgreSQL does not run it). A data-modifying WITH query (INSERT, UPDATE, DELETE) "
        + "runs whether or not it is read, and is not reported.",
        "queries-with") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            if (s.Raw == null) continue;
            var relations = s.All<RangeVar>().ToList();

            foreach (var cte in s.All<CommonTableExpr>())
            {
                var query = cte.Ctequery;
                if (query == null || query.InsertStmt != null || query.UpdateStmt != null || query.DeleteStmt != null || query.MergeStmt != null) continue;

                var own = new HashSet<IMessage>(query.Descendants<RangeVar>(), ReferenceEqualityComparer.Instance);
                var used = relations.Any(r => string.IsNullOrEmpty(r.Schemaname) && !own.Contains(r)
                    && string.Equals(r.Relname, cte.Ctename, StringComparison.Ordinal));
                if (!used)
                    report.Node(s, cte, $"The WITH query {cte.Ctename} is never used.");
            }
        }
    }
}
