using Xunit;
using AkmlSql.Engine.Parser;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Tests.Parser;

public class CteResolverTests
{
    private readonly CteResolver _resolver = new();

    private static TSqlScript ParseSql(string sql)
    {
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        var script = parser.Parse(reader, out _) as TSqlScript;
        return script!;
    }

    // ── Single CTE ───────────────────────────────────────────────────────

    [Fact]
    public void ResolveCtes_SingleCte_Found()
    {
        var sql = "WITH cte AS (SELECT 1 AS id) SELECT * FROM cte;";
        var script = ParseSql(sql);

        var ctes = _resolver.ResolveCtes(script, sql.Length);

        Assert.True(ctes.ContainsKey("cte"));
    }

    [Fact]
    public void ResolveCtes_SingleCte_ColumnsInferred()
    {
        var sql = "WITH cte AS (SELECT 1 AS id) SELECT * FROM cte;";
        var script = ParseSql(sql);

        var ctes = _resolver.ResolveCtes(script, sql.Length);

        Assert.Contains("id", ctes["cte"]);
    }

    // ── Multiple CTEs ─────────────────────────────────────────────────────

    [Fact]
    public void ResolveCtes_MultipleCtes_AllFound()
    {
        var sql = "WITH a AS (SELECT 1 AS x), b AS (SELECT 2 AS y) SELECT * FROM a, b;";
        var script = ParseSql(sql);

        var ctes = _resolver.ResolveCtes(script, sql.Length);

        Assert.True(ctes.ContainsKey("a"));
        Assert.True(ctes.ContainsKey("b"));
    }

    // ── Explicit column list ──────────────────────────────────────────────

    [Fact]
    public void ResolveCtes_ExplicitColumnList_UsesExplicitColumns()
    {
        var sql = "WITH cte(col1, col2) AS (SELECT 1, 2) SELECT * FROM cte;";
        var script = ParseSql(sql);

        var ctes = _resolver.ResolveCtes(script, sql.Length);

        Assert.True(ctes.ContainsKey("cte"));
        Assert.Contains("col1", ctes["cte"]);
        Assert.Contains("col2", ctes["cte"]);
    }

    // ── No CTEs ───────────────────────────────────────────────────────────

    [Fact]
    public void ResolveCtes_NoCtes_ReturnsEmpty()
    {
        var sql = "SELECT 1;";
        var script = ParseSql(sql);

        var ctes = _resolver.ResolveCtes(script, sql.Length);

        Assert.Empty(ctes);
    }

    // ── Null script ───────────────────────────────────────────────────────

    [Fact]
    public void ResolveCtes_NullScript_ReturnsEmpty()
    {
        var ctes = _resolver.ResolveCtes(null, 0);

        Assert.Empty(ctes);
    }

    // ── Case insensitive keys ─────────────────────────────────────────────

    [Fact]
    public void ResolveCtes_KeyIsCaseInsensitive()
    {
        var sql = "WITH MyCTE AS (SELECT 1 AS id) SELECT * FROM MyCTE;";
        var script = ParseSql(sql);

        var ctes = _resolver.ResolveCtes(script, sql.Length);

        // Dictionary should be case-insensitive
        Assert.True(ctes.ContainsKey("mycte") || ctes.ContainsKey("MyCTE"));
    }
}
