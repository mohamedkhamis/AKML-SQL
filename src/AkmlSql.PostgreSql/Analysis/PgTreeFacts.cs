using Google.Protobuf;
using Google.Protobuf.Collections;
using PgSqlParser;

namespace AkmlSql.PostgreSql.Analysis;

/// <summary>
/// Spec 042 (task T162) - small, pure readers over the parse tree that several rules share: the text of a name list, the
/// function a call names, the type a column has, whether a node is a comparison. Nothing here reports; it answers questions.
/// </summary>
internal static class PgTreeFacts
{
    private static readonly HashSet<string> ComparisonOperators = new(StringComparer.Ordinal)
    {
        "=", "<>", "!=", "<", ">", "<=", ">=",
    };

    /// <summary>The parts of a qualified name (String nodes), with a star as "*".</summary>
    public static List<string> Names(IEnumerable<Node> nodes) =>
        nodes.Select(n => n.String?.Sval ?? (n.AStar != null ? "*" : string.Empty)).ToList();

    public static string LastName(IEnumerable<Node> nodes) => Names(nodes).LastOrDefault() ?? string.Empty;

    /// <summary>The lower-case function name of a call, without its schema.</summary>
    public static string FunctionName(FuncCall call) => LastName(call.Funcname).ToLowerInvariant();

    /// <summary>The lower-case type name without its schema: <c>timestamp</c> for pg_catalog.timestamp, <c>serial</c> for serial.</summary>
    public static string TypeLower(TypeName type) => LastName(type.Names).ToLowerInvariant();

    /// <summary>The parts of a column reference: <c>a.b</c> is ["a", "b"], <c>t.*</c> is ["t", "*"].</summary>
    public static List<string> Parts(ColumnRef reference) => Names(reference.Fields);

    /// <summary>True when the reference ends in a star (<c>*</c> or <c>t.*</c>).</summary>
    public static bool EndsInStar(ColumnRef reference) =>
        reference.Fields.Count > 0 && reference.Fields[reference.Fields.Count - 1].AStar != null;

    /// <summary>The operator of an A_Expr (<c>=</c>, <c>~~</c>), empty when it has none.</summary>
    public static string Operator(A_Expr expression) => expression.Name.Count > 0 ? LastName(expression.Name) : string.Empty;

    /// <summary>True for <c>=</c>, <c>&lt;&gt;</c>, <c>!=</c>, <c>&lt;</c>, <c>&gt;</c>, <c>&lt;=</c>, <c>&gt;=</c> between two operands.</summary>
    public static bool IsComparison(A_Expr expression) =>
        expression.Kind == A_Expr_Kind.AexprOp && ComparisonOperators.Contains(Operator(expression));

    /// <summary>The text of a string constant, or null when the node is not one.</summary>
    public static string? StringConstant(Node? node) => node?.AConst is { Isnull: false, ValCase: A_Const.ValOneofCase.Sval } c ? c.Sval.Sval : null;

    /// <summary>The value of an integer constant, or null.</summary>
    public static int? IntegerConstant(Node? node) => node?.AConst is { Isnull: false, ValCase: A_Const.ValOneofCase.Ival } c ? c.Ival.Ival : null;

    /// <summary>True when the node is a number written with a fraction or exponent (<c>1.5</c>, <c>2e3</c>).</summary>
    public static bool IsFractionalConstant(Node? node) => node?.AConst is { Isnull: false, ValCase: A_Const.ValOneofCase.Fval };

    public static bool IsConstant(Node? node) => node?.AConst != null;

    /// <summary>The definition element named <paramref name="name"/> in an option list, or null.</summary>
    public static DefElem? Option(IEnumerable<Node> options, string name) =>
        options.Select(o => o.DefElem).FirstOrDefault(d => d != null && string.Equals(d.Defname, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The bare table name a range variable spells, with its schema when given: <c>sales.orders</c>.</summary>
    public static string QualifiedName(RangeVar relation) =>
        string.IsNullOrEmpty(relation.Schemaname) ? relation.Relname : relation.Schemaname + "." + relation.Relname;

    /// <summary>True when the SelectStmt is one arm of a set operation (UNION, INTERSECT, EXCEPT) rather than a query of its own.</summary>
    public static bool IsSetOperation(SelectStmt select) => select.Op != SetOperation.SetopNone;

    /// <summary>True when the query has a LIMIT, OFFSET or FETCH.</summary>
    public static bool HasRowLimit(SelectStmt select) => select.LimitCount != null || select.LimitOffset != null;

    /// <summary>The column definitions in a statement: those of CREATE TABLE, ALTER TABLE ADD COLUMN / ALTER COLUMN TYPE, composite types.</summary>
    public static IEnumerable<ColumnDef> ColumnDefinitions(PgScriptStatement statement) => statement.All<ColumnDef>();

    /// <summary>All the types a statement names, wherever it names them (column definitions, casts, parameters, return types).</summary>
    public static IEnumerable<TypeName> TypesNamed(PgScriptStatement statement) => statement.All<TypeName>();

    /// <summary>The single-name table references in a FROM list, joins unfolded; subqueries and functions are not included.</summary>
    public static IEnumerable<RangeVar> RelationsIn(IEnumerable<Node> fromItems)
    {
        foreach (var item in fromItems)
        {
            if (item.RangeVar != null) yield return item.RangeVar;
            else if (item.JoinExpr != null)
            {
                foreach (var r in RelationsIn(new[] { item.JoinExpr.Larg, item.JoinExpr.Rarg })) yield return r;
            }
        }
    }

    /// <summary>All join nodes under a FROM list.</summary>
    public static IEnumerable<JoinExpr> JoinsIn(IEnumerable<Node> fromItems)
    {
        foreach (var item in fromItems)
        {
            if (item.JoinExpr == null) continue;
            yield return item.JoinExpr;
            foreach (var j in JoinsIn(new[] { item.JoinExpr.Larg, item.JoinExpr.Rarg })) yield return j;
        }
    }

    /// <summary>The name a FROM item is known by in the query: its alias, or the table name when it has none.</summary>
    public static string? ExposedName(Node item)
    {
        if (item.RangeVar != null) return item.RangeVar.Alias?.Aliasname is { Length: > 0 } a ? a : item.RangeVar.Relname;
        if (item.RangeSubselect != null) return item.RangeSubselect.Alias?.Aliasname;
        if (item.RangeFunction != null) return item.RangeFunction.Alias?.Aliasname;
        return null;
    }

    /// <summary>The node itself when it is a <typeparamref name="T"/>, then every <typeparamref name="T"/> below it (Descendants alone leaves the root out).</summary>
    public static IEnumerable<T> SelfAndDescendants<T>(Node? node) where T : class, IMessage
    {
        if (node == null) yield break;
        var inner = node.Unwrap();
        if (inner is T self) yield return self;
        foreach (var d in inner.Descendants<T>()) yield return d;
    }

    /// <summary>The expression nodes (not their wrappers) of a repeated Node field.</summary>
    public static IEnumerable<IMessage> Unwrap(RepeatedField<Node> nodes) => nodes.Select(n => n.Unwrap());
}
