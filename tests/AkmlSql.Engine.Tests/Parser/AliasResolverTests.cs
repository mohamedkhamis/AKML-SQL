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
}
