using AkmlSql.Core.Models.Analysis;
using Google.Protobuf;
using PgSqlParser;

namespace AkmlSql.PostgreSql.Analysis.Rules.Performance;

// Spec 042 (task T163, FR-150, FR-153): the performance rules PGPE001 to PGPE010. Each rule says in its description which
// PostgreSQL behaviour it rests on; the manual section it links to is named in the constructor.

/// <summary>PGPE001 - SELECT * returns every column, now and after the table changes.</summary>
public sealed class Pgpe001SelectStar : PgRuleBase
{
    public Pgpe001SelectStar() : base("PGPE001", "Performance", "SELECT *", DiagnosticSeverity.Warning,
        "SELECT * sends every column of every row to the client, including columns added later, and rules out an index-only scan. "
        + "It is not reported inside EXISTS, where the select list is never evaluated, and COUNT(*) is not a column list.",
        "sql-select") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            if (s.Raw == null) continue;
            s.Raw.Walk(v =>
            {
                if (v.Node is ResTarget { Val.ColumnRef: { } reference } && v.Parent is SelectStmt && PgTreeFacts.EndsInStar(reference) && !InsideExists(v))
                    report.Node(s, reference, "SELECT * returns every column; list the columns the query needs.");
                return WalkAction.Continue;
            });
        }
    }

    // The target list of an EXISTS subquery (or of one arm of a set operation inside it) is never evaluated.
    private static bool InsideExists(NodeVisit visit)
    {
        foreach (var ancestor in visit.Ancestors)
        {
            if (ancestor is SelectStmt) continue;
            return ancestor is SubLink { SubLinkType: SubLinkType.ExistsSublink };
        }
        return false;
    }
}

/// <summary>PGPE002 - a pattern that starts with a wildcard cannot use an ordinary index.</summary>
public sealed class Pgpe002LeadingWildcardLike : PgRuleBase
{
    public Pgpe002LeadingWildcardLike() : base("PGPE002", "Performance", "Leading wildcard in LIKE", DiagnosticSeverity.Warning,
        "A B-tree index can answer LIKE and ILIKE only when the pattern starts with fixed text. A pattern that starts with % scans the whole table; "
        + "a trigram index (pg_trgm) is the usual remedy.",
        "indexes-types") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            foreach (var e in s.All<A_Expr>())
            {
                if (e.Kind is not (A_Expr_Kind.AexprLike or A_Expr_Kind.AexprIlike)) continue;
                var pattern = PgTreeFacts.StringConstant(e.Rexpr);
                if (pattern != null && pattern.StartsWith('%'))
                    report.Node(s, e.Rexpr.AConst, "The pattern starts with %, so no ordinary index can be used.");
            }
        }
    }
}

/// <summary>PGPE003 - a function around a column hides the column from its index.</summary>
public sealed class Pgpe003FunctionOnColumn : PgRuleBase
{
    public Pgpe003FunctionOnColumn() : base("PGPE003", "Performance", "Function on a column in a predicate", DiagnosticSeverity.Warning,
        "An index on a column is used for a condition on the column itself. When the column is wrapped in a function (lower(name) = 'x') the planner "
        + "cannot use that index; an expression index on the function, or a rewritten condition, restores it.",
        "indexes-expressional") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            if (s.Raw == null) continue;

            var roots = new List<IMessage>();
            s.Raw.Walk(v =>
            {
                if (v.FieldName is "WhereClause" or "Quals") roots.Add(v.Node);
                return WalkAction.Continue;
            });

            var seen = new HashSet<IMessage>(ReferenceEqualityComparer.Instance);
            foreach (var root in roots)
            {
                var candidates = (root is A_Expr self ? new[] { self } : Array.Empty<A_Expr>()).Concat(root.Descendants<A_Expr>());
                foreach (var e in candidates)
                {
                    if (!seen.Add(e) || !IsPredicate(e)) continue;
                    foreach (var side in new[] { e.Lexpr, e.Rexpr })
                    {
                        if (side?.FuncCall is not { } call) continue;
                        var column = call.Descendants<ColumnRef>().FirstOrDefault(c => !PgTreeFacts.EndsInStar(c));
                        if (column == null) continue;
                        report.Node(s, call, $"{PgTreeFacts.FunctionName(call)}() is applied to column {string.Join(".", PgTreeFacts.Parts(column))}; an index on the column cannot be used.");
                    }
                }
            }
        }
    }

    private static bool IsPredicate(A_Expr e) =>
        PgTreeFacts.IsComparison(e) || e.Kind is A_Expr_Kind.AexprLike or A_Expr_Kind.AexprIlike or A_Expr_Kind.AexprIn
            or A_Expr_Kind.AexprBetween or A_Expr_Kind.AexprNotBetween;
}

/// <summary>PGPE004 - NOT IN with a subquery that can return NULL matches nothing.</summary>
public sealed class Pgpe004NotInNullable : PgRuleBase
{
    public Pgpe004NotInNullable() : base("PGPE004", "Performance", "NOT IN over a nullable column", DiagnosticSeverity.Warning,
        "x NOT IN (subquery) is unknown, not true, for every row when the subquery returns a NULL, so the query silently returns no rows; "
        + "it also cannot be planned as an anti-join. NOT EXISTS has neither problem. Needs the schema to know the column is nullable.",
        "functions-subquery", requiresSchema: true) { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        var cache = ctx.SchemaCache;
        if (cache == null) return;

        foreach (var s in ctx.Statements)
        {
            foreach (var link in s.All<SubLink>())
            {
                var isNotIn = link.SubLinkType == SubLinkType.AnySublink && IsNegated(s, link);
                var isAll = link.SubLinkType == SubLinkType.AllSublink && link.OperName.Count == 1 && PgTreeFacts.LastName(link.OperName) == "<>";
                if (!isNotIn && !isAll) continue;

                var select = link.Subselect?.SelectStmt;
                if (select == null || PgTreeFacts.IsSetOperation(select) || select.TargetList.Count != 1) continue;
                if (select.TargetList[0].ResTarget?.Val?.ColumnRef is not { } reference) continue;

                var relations = PgTreeFacts.RelationsIn(select.FromClause).ToList();
                var column = PgSchemaLookup.ResolveColumn(cache, relations, reference);
                if (column is not { IsNullable: true } || GuardedByNotNull(select, reference)) continue;

                report.Node(s, reference, $"Column {column.ColumnName} can be NULL; if one row is NULL, NOT IN matches nothing. Use NOT EXISTS, or filter the NULLs out.");
            }
        }
    }

    // The tree holds `x NOT IN (...)` as NOT( x = ANY (...) ).
    private static bool IsNegated(PgScriptStatement statement, SubLink link) =>
        statement.All<BoolExpr>().Any(b => b.Boolop == BoolExprType.NotExpr && b.Args.Count == 1 && ReferenceEquals(b.Args[0].SubLink, link));

    private static bool GuardedByNotNull(SelectStmt select, ColumnRef column)
    {
        if (select.WhereClause == null) return false;
        var name = PgTreeFacts.Parts(column)[^1];
        return PgTreeFacts.SelfAndDescendants<NullTest>(select.WhereClause).Any(t =>
            t.Nulltesttype == NullTestType.IsNotNull && t.Arg?.ColumnRef is { } c && PgTreeFacts.Parts(c)[^1] == name);
    }
}

/// <summary>PGPE005 - comparing an integer column with a fractional constant casts the column.</summary>
public sealed class Pgpe005ImplicitCast : PgRuleBase
{
    public Pgpe005ImplicitCast() : base("PGPE005", "Performance", "Implicit cast of a column", DiagnosticSeverity.Information,
        "There is no operator between an integer and a numeric, so integer_column = 1.5 converts the column to numeric for every row and an index on it is not used. "
        + "Write the constant in the column's type, or compare ranges. (A quoted literal such as '5' takes the column's type and is not affected.) Needs the schema.",
        "typeconv-oper", requiresSchema: true) { }

    private static readonly HashSet<string> IntegerTypes = new(StringComparer.OrdinalIgnoreCase) { "smallint", "integer", "bigint", "int2", "int4", "int8" };

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        var cache = ctx.SchemaCache;
        if (cache == null) return;

        foreach (var s in ctx.Statements)
        {
            if (s.Raw == null) continue;
            var relations = RelationsOf(s);
            if (relations.Count == 0) continue;

            foreach (var e in s.All<A_Expr>())
            {
                if (!PgTreeFacts.IsComparison(e)) continue;
                CheckSides(s, report, cache, relations, e.Lexpr, e.Rexpr);
                CheckSides(s, report, cache, relations, e.Rexpr, e.Lexpr);
            }
        }
    }

    private static void CheckSides(PgScriptStatement s, PgReporter report, AkmlSql.Engine.Schema.DatabaseCache cache,
        IReadOnlyList<RangeVar> relations, Node? columnSide, Node? constantSide)
    {
        if (columnSide?.ColumnRef is not { } reference || !PgTreeFacts.IsFractionalConstant(constantSide)) return;
        var column = PgSchemaLookup.ResolveColumn(cache, relations, reference);
        if (column == null || !IntegerTypes.Contains(column.TypeName)) return;

        report.Node(s, reference, $"Column {column.ColumnName} is {column.TypeName}; comparing it with a fractional number casts the column to numeric and skips its index.");
    }

    private static List<RangeVar> RelationsOf(PgScriptStatement s)
    {
        var list = new List<RangeVar>();
        foreach (var select in s.All<SelectStmt>()) list.AddRange(PgTreeFacts.RelationsIn(select.FromClause));
        foreach (var update in s.All<UpdateStmt>())
        {
            list.Add(update.Relation);
            list.AddRange(PgTreeFacts.RelationsIn(update.FromClause));
        }
        foreach (var delete in s.All<DeleteStmt>())
        {
            list.Add(delete.Relation);
            list.AddRange(PgTreeFacts.RelationsIn(delete.UsingClause));
        }
        return list;
    }
}

/// <summary>PGPE006 - ORDER BY in a subquery or CTE is not guaranteed to survive, and costs a sort.</summary>
public sealed class Pgpe006OrderByInSubquery : PgRuleBase
{
    public Pgpe006OrderByInSubquery() : base("PGPE006", "Performance", "ORDER BY in a subquery", DiagnosticSeverity.Information,
        "The order of rows produced by a subquery or a common table expression is not kept by the query that uses it (SQL defines a table as unordered), "
        + "so the sort is wasted unless a LIMIT, OFFSET or FETCH depends on it. DISTINCT ON and ARRAY(...) subqueries do depend on the order and are not reported.",
        "sql-select") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            if (s.Raw == null) continue;
            s.Raw.Walk(v =>
            {
                if (v.Node is SelectStmt select && select.SortClause.Count > 0 && !PgTreeFacts.HasRowLimit(select) && IsNested(v) && !select.DistinctClause.Any(n => n.NodeCase != Node.NodeOneofCase.None))
                    report.Node(s, select.SortClause[0].SortBy.Node.Unwrap(), "ORDER BY in a subquery has no effect on the outer query and costs a sort; remove it or add a LIMIT.");
                return WalkAction.Continue;
            });
        }
    }

    private static bool IsNested(NodeVisit visit) => visit.Parent switch
    {
        CommonTableExpr => true,
        RangeSubselect => true,
        SubLink link => link.SubLinkType != SubLinkType.ArraySublink,
        _ => false,
    };
}

/// <summary>PGPE007 - counting rows only to compare the count with zero.</summary>
public sealed class Pgpe007CountForExistence : PgRuleBase
{
    public Pgpe007CountForExistence() : base("PGPE007", "Performance", "COUNT(*) used as an existence test", DiagnosticSeverity.Information,
        "count(*) has to visit every matching row, while EXISTS stops at the first one. Comparing a count with 0 (or testing it for >= 1) only asks whether a row exists.",
        "functions-subquery") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            if (s.Raw == null) continue;

            var inHaving = new HashSet<IMessage>(ReferenceEqualityComparer.Instance);
            s.Raw.Walk(v =>
            {
                if (v.FieldName == "HavingClause")
                {
                    inHaving.Add(v.Node);
                    foreach (var d in v.Node.Descendants()) inHaving.Add(d);
                }
                return WalkAction.Continue;
            });

            foreach (var e in s.All<A_Expr>())
            {
                if (inHaving.Contains(e) || !PgTreeFacts.IsComparison(e)) continue;
                var op = PgTreeFacts.Operator(e);
                if (!IsCountTest(e.Lexpr, e.Rexpr, op) && !IsCountTest(e.Rexpr, e.Lexpr, Mirror(op))) continue;

                var at = s.LocationOf(e);      // an operator expression is located at its operator
                if (at < 0) at = s.Start;
                report.At(at, at + op.Length, "Counting rows to compare the count with zero is an existence test; use EXISTS (SELECT 1 ...), which stops at the first row.");
            }
        }
    }

    // count(...) on one side, 0 or 1 on the other, with an operator that asks "is there at least one row" (or none).
    private static bool IsCountTest(Node? countSide, Node? numberSide, string op)
    {
        if (!IsCount(countSide) || PgTreeFacts.IntegerConstant(numberSide) is not { } n) return false;
        return (n == 0 && op is ">" or "=" or "<>" or "!=") || (n == 1 && op is ">=" or "<");
    }

    private static string Mirror(string op) => op switch { "<" => ">", ">" => "<", "<=" => ">=", ">=" => "<=", _ => op };

    // count(...) itself, or a scalar subquery whose only column is count(...).
    private static bool IsCount(Node? side)
    {
        if (side?.FuncCall is { } call) return PgTreeFacts.FunctionName(call) == "count" && call.Over == null;
        var select = side?.SubLink is { SubLinkType: SubLinkType.ExprSublink } link ? link.Subselect?.SelectStmt : null;
        return select is { TargetList.Count: 1 } && select.TargetList[0].ResTarget?.Val?.FuncCall is { } inner && PgTreeFacts.FunctionName(inner) == "count";
    }
}

/// <summary>PGPE008 - a correlated subquery in the select list runs once per row of the outer query.</summary>
public sealed class Pgpe008CorrelatedSubquery : PgRuleBase
{
    public Pgpe008CorrelatedSubquery() : base("PGPE008", "Performance", "Correlated subquery in the select list", DiagnosticSeverity.Hint,
        "A scalar subquery in the select list that refers to the outer query is evaluated for each outer row. A join, a lateral join or an aggregate over a grouped "
        + "subquery lets the planner choose a better strategy. Only references qualified with the outer table's name or alias are recognised.",
        "sql-select") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            foreach (var select in s.All<SelectStmt>())
            {
                var outer = ExposedNames(select.FromClause);
                if (outer.Count == 0) continue;

                foreach (var target in select.TargetList)
                {
                    if (target.ResTarget?.Val?.SubLink is not { SubLinkType: SubLinkType.ExprSublink } link || link.Subselect?.SelectStmt is not { } inner) continue;

                    var own = ExposedNames(inner.FromClause);
                    var correlated = inner.Descendants<ColumnRef>().Any(c =>
                    {
                        var parts = PgTreeFacts.Parts(c);
                        return parts.Count >= 2 && outer.Contains(parts[0]) && !own.Contains(parts[0]);
                    });
                    if (correlated)
                        report.Node(s, link, "This subquery refers to the outer query, so it runs once for every row; consider a join or a lateral join.");
                }
            }
        }
    }

    private static HashSet<string> ExposedNames(IEnumerable<Node> items)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (item.JoinExpr != null)
            {
                names.UnionWith(ExposedNames(new[] { item.JoinExpr.Larg, item.JoinExpr.Rarg }));
                continue;
            }
            if (PgTreeFacts.ExposedName(item) is { } name) names.Add(name);
        }
        return names;
    }
}

/// <summary>PGPE009 - a very long IN list.</summary>
public sealed class Pgpe009LargeInList : PgRuleBase
{
    public const int Limit = 100;

    public Pgpe009LargeInList() : base("PGPE009", "Performance", "Large IN list", DiagnosticSeverity.Information,
        "An IN list is expanded into one comparison per value, so a list of more than 100 values is slow to parse and to plan. "
        + "Pass the values as one array (= ANY($1)) or join to a VALUES list.",
        "functions-comparisons") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            foreach (var e in s.All<A_Expr>())
            {
                if (e.Kind != A_Expr_Kind.AexprIn || e.Rexpr?.List is not { } list || list.Items.Count <= Limit) continue;
                report.Node(s, e.Lexpr?.Unwrap() ?? e, $"The IN list has {list.Items.Count} values; use = ANY(array) or join to a VALUES list.");
            }
        }
    }
}

/// <summary>PGPE010 - a join with no join condition multiplies the rows of both sides.</summary>
public sealed class Pgpe010CrossJoin : PgRuleBase
{
    public Pgpe010CrossJoin() : base("PGPE010", "Performance", "Cross join", DiagnosticSeverity.Warning,
        "Tables listed with commas and no condition that relates them, or CROSS JOIN, produce every combination of rows: a thousand rows against a thousand is a million. "
        + "Set-returning functions and LATERAL items are expected to be combined with the rows before them and are not reported.",
        "queries-table-expressions") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            // CROSS JOIN is explicit: the two words side by side.
            var sig = s.Tokens.Sig;
            for (var i = 0; i + 1 < sig.Count; i++)
            {
                if (sig[i].IsWordEqual("CROSS") && sig[i + 1].IsWordEqual("JOIN") && !JoinsAFunctionOrLateral(sig, i + 2))
                    report.At(sig[i].Token.Start, sig[i + 1].Token.End, "CROSS JOIN pairs every row of one side with every row of the other.");
            }

            foreach (var select in s.All<SelectStmt>())
            {
                if (select.FromClause.Count < 2) continue;
                var extra = select.FromClause.Skip(1).Where(IsPlainTable).ToList();
                if (extra.Count == 0 || HasJoinPredicate(select.WhereClause)) continue;

                var first = extra[0];
                if (first.RangeVar != null) report.Node(s, first.RangeVar, "No condition relates this table to the others in FROM, so every combination of rows is produced.");
                else report.Statement(s, "No condition relates the tables in FROM, so every combination of rows is produced.");
            }
        }
    }

    // CROSS JOIN LATERAL (...) and CROSS JOIN unnest(...) are the usual way to expand each row, not a cartesian product.
    private static bool JoinsAFunctionOrLateral(IReadOnlyList<AkmlSql.PostgreSql.Completion.PgSig> sig, int next) =>
        next < sig.Count && (sig[next].IsWordEqual("LATERAL") || sig[next].IsWordEqual("ROWS")
            || (sig[next].IsName && next + 1 < sig.Count && (sig[next + 1].IsOpen || sig[next + 1].IsDot && next + 3 < sig.Count && sig[next + 3].IsOpen)));

    // A table or a derived table that does not depend on the items before it. Functions and LATERAL items do.
    private static bool IsPlainTable(Node item) =>
        item.RangeVar != null || item.JoinExpr != null || (item.RangeSubselect is { Lateral: false });

    private static bool HasJoinPredicate(Node? where)
    {
        if (where == null) return false;
        return PgTreeFacts.SelfAndDescendants<A_Expr>(where).Any(e => e.Lexpr?.ColumnRef != null && e.Rexpr?.ColumnRef != null);
    }
}
