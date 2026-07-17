using Xunit;
using AkmlSql.Engine.Parser;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Tests.Parser;

public class AliasResolverTests
{
    private readonly AliasResolver _resolver = new();

    private static TSqlScript ParseSql(string sql)
    {
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        var script = parser.Parse(reader, out _) as TSqlScript;
        return script!;
    }

    // ── Simple alias ──────────────────────────────────────────────────────

    [Fact]
    public void ResolveAliases_SimpleAlias_Found()
    {
        var sql = "SELECT o.id FROM dbo.Orders o WHERE o.id = 1;";
        var script = ParseSql(sql);

        var aliases = _resolver.ResolveAliases(script, sql.Length);

        Assert.True(aliases.ContainsKey("o"));
    }

    [Fact]
    public void ResolveAliases_SimpleAlias_TableNameCorrect()
    {
        var sql = "SELECT o.id FROM dbo.Orders o;";
        var script = ParseSql(sql);

        var aliases = _resolver.ResolveAliases(script, sql.Length);

        Assert.True(aliases.ContainsKey("o"));
        Assert.Equal("Orders", aliases["o"].TableName);
    }

    // ── Multiple aliases ──────────────────────────────────────────────────

    [Fact]
    public void ResolveAliases_MultipleAliases_AllFound()
    {
        var sql = "SELECT o.id, c.name FROM dbo.Orders o JOIN dbo.Customers c ON o.CustomerId = c.id;";
        var script = ParseSql(sql);

        var aliases = _resolver.ResolveAliases(script, sql.Length);

        Assert.True(aliases.ContainsKey("o"));
        Assert.True(aliases.ContainsKey("c"));
    }

    // ── No alias — table name used as key ────────────────────────────────

    [Fact]
    public void ResolveAliases_NoAlias_TableNameIsKey()
    {
        var sql = "SELECT id FROM dbo.Orders;";
        var script = ParseSql(sql);

        var aliases = _resolver.ResolveAliases(script, sql.Length);

        Assert.True(aliases.ContainsKey("Orders"));
    }

    // ── JOIN alias ────────────────────────────────────────────────────────

    [Fact]
    public void ResolveAliases_JoinAlias_Found()
    {
        var sql = "SELECT o.id FROM dbo.Orders o INNER JOIN dbo.Customers c ON o.CustomerId = c.id;";
        var script = ParseSql(sql);

        var aliases = _resolver.ResolveAliases(script, sql.Length);

        Assert.True(aliases.ContainsKey("c"));
        Assert.Equal("Customers", aliases["c"].TableName);
    }

    // ── Derived table alias ───────────────────────────────────────────────

    [Fact]
    public void ResolveAliases_SubqueryAlias_NoThrow()
    {
        var sql = "SELECT sub.id FROM (SELECT 1 AS id) sub;";
        var script = ParseSql(sql);

        var ex = Record.Exception(() => _resolver.ResolveAliases(script, sql.Length));

        Assert.Null(ex);
    }

    // ── Null script ───────────────────────────────────────────────────────

    [Fact]
    public void ResolveAliases_NullScript_ReturnsEmpty()
    {
        var aliases = _resolver.ResolveAliases(null, 0);

        Assert.Empty(aliases);
    }

    // ── Self-join ─────────────────────────────────────────────────────────

    [Fact]
    public void ResolveAliases_SelfJoin_BothAliasesFound()
    {
        var sql = "SELECT e1.Name, e2.Name FROM Employees e1 JOIN Employees e2 ON e1.ManagerId = e2.Id;";
        var script = ParseSql(sql);

        var aliases = _resolver.ResolveAliases(script, sql.Length);

        Assert.True(aliases.ContainsKey("e1"));
        Assert.True(aliases.ContainsKey("e2"));
    }

    // ── Spec 032 US2 (T015) — A3: DML statements get full AST resolution ─────

    [Fact]
    public void CursorScope_AliasedUpdate_ResolvesAliasFromAst()
    {
        // Parses cleanly, yet CursorScopeFinder only visited QuerySpecification —
        // UPDATE fell to the (formerly poisoned) token fallback.
        var sql = "UPDATE o SET Price = 1 FROM dbo.Orders o WHERE o.OrderDate > '2020-01-01';";
        var script = ParseSql(sql);
        var caret = sql.IndexOf("o.OrderDate", StringComparison.Ordinal) + 2;

        var aliases = _resolver.ResolveAliasesInCursorScope(script, caret);

        Assert.True(aliases.ContainsKey("o"));
        Assert.Equal("Orders", aliases["o"].TableName);
    }

    [Fact]
    public void CursorScope_AliasedDelete_ResolvesAliasFromAst()
    {
        var sql = "DELETE o FROM dbo.Orders o WHERE o.OrderID = 1;";
        var script = ParseSql(sql);
        var caret = sql.IndexOf("o.OrderID", StringComparison.Ordinal) + 2;

        var aliases = _resolver.ResolveAliasesInCursorScope(script, caret);

        Assert.True(aliases.ContainsKey("o"));
        Assert.Equal("Orders", aliases["o"].TableName);
    }

    [Fact]
    public void CursorScope_Merge_ResolvesTargetAndSourceAliases()
    {
        var sql = "MERGE dbo.Orders AS tgt USING dbo.OrderDetails AS src ON tgt.OrderID = src.OrderID " +
                  "WHEN MATCHED THEN UPDATE SET tgt.OrderDate = GETDATE();";
        var script = ParseSql(sql);
        var caret = sql.IndexOf("tgt.OrderDate", StringComparison.Ordinal) + 4;

        var aliases = _resolver.ResolveAliasesInCursorScope(script, caret);

        Assert.True(aliases.ContainsKey("tgt"));
        Assert.Equal("Orders", aliases["tgt"].TableName);
        Assert.True(aliases.ContainsKey("src"));
        Assert.Equal("OrderDetails", aliases["src"].TableName);
    }

    // ── Spec 032 US2 (T015) — A4: correlated subqueries merge ancestor scopes ─

    [Fact]
    public void CursorScope_CorrelatedSubquery_SeesOuterAliases()
    {
        var sql = "SELECT * FROM dbo.Orders o WHERE EXISTS " +
                  "(SELECT 1 FROM dbo.OrderDetails od WHERE od.OrderID = o.OrderID);";
        var script = ParseSql(sql);
        var caret = sql.IndexOf("od.OrderID", StringComparison.Ordinal) + 3;

        var aliases = _resolver.ResolveAliasesInCursorScope(script, caret, includeOuterScopes: true);

        Assert.True(aliases.ContainsKey("od"), "inner scope");
        Assert.True(aliases.ContainsKey("o"), "outer (correlated) scope must be merged in");
        Assert.Equal("Orders", aliases["o"].TableName);
    }

    [Fact]
    public void CursorScope_CorrelatedSubquery_InnerWinsOnConflict()
    {
        var sql = "SELECT * FROM dbo.Orders x WHERE EXISTS " +
                  "(SELECT 1 FROM dbo.OrderDetails x WHERE x.Quantity > 1);";
        var script = ParseSql(sql);
        var caret = sql.IndexOf("x.Quantity", StringComparison.Ordinal) + 2;

        var aliases = _resolver.ResolveAliasesInCursorScope(script, caret, includeOuterScopes: true);

        Assert.Equal("OrderDetails", aliases["x"].TableName);
    }
}
