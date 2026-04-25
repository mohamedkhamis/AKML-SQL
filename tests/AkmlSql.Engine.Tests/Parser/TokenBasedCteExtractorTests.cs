using Xunit;
using AkmlSql.Engine.Parser;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Tests.Parser;

public class TokenBasedCteExtractorTests
{
    private static IList<TSqlParserToken> Tokenize(string sql)
    {
        var svc = new TsqlParserService();
        return svc.GetTokenStream(sql);
    }

    [Fact]
    public void Extract_CursorInSecondCte_ReturnsFirstCteOnly()
    {
        // Cte1 is fully defined; Cte2 body is incomplete (cursor inside it).
        // Cte1 should be visible; Cte2 should NOT (self-reference).
        var sql = "WITH Cte1 AS (SELECT 1 AS a), Cte2 AS (SELECT * FROM ";
        var tokens = Tokenize(sql);

        var ctes = TokenBasedCteExtractor.Extract(tokens, sql.Length);

        Assert.Contains("Cte1", ctes);
        Assert.DoesNotContain("Cte2", ctes);
    }

    [Fact]
    public void Extract_CursorInsideFirstCte_DoesNotReturnFirstCte()
    {
        // `WITH Cte1 AS (SELECT * FROM |)` — cursor inside Cte1's body.
        // Suggesting Cte1 here would imply self-reference.
        var sql = "WITH Cte1 AS (SELECT * FROM ";
        var tokens = Tokenize(sql);

        var ctes = TokenBasedCteExtractor.Extract(tokens, sql.Length);

        Assert.DoesNotContain("Cte1", ctes);
    }

    [Fact]
    public void Extract_CursorAfterCompletedCteList_ReturnsAllCtes()
    {
        // `WITH a AS (...), b AS (...) |` — both CTEs should be visible to the
        // body query the user is about to write.
        var sql = "WITH a AS (SELECT 1 AS x), b AS (SELECT 2 AS y) ";
        var tokens = Tokenize(sql);

        var ctes = TokenBasedCteExtractor.Extract(tokens, sql.Length);

        Assert.Contains("a", ctes);
        Assert.Contains("b", ctes);
    }

    [Fact]
    public void Extract_NoCtes_ReturnsEmpty()
    {
        var sql = "SELECT * FROM Customers";
        var tokens = Tokenize(sql);

        var ctes = TokenBasedCteExtractor.Extract(tokens, sql.Length);

        Assert.Empty(ctes);
    }

    [Fact]
    public void Extract_WithNoLockHint_NotMistakenForCte()
    {
        // `WITH (NOLOCK)` is a table hint, not a CTE clause.
        var sql = "SELECT * FROM Customers WITH (NOLOCK) WHERE ";
        var tokens = Tokenize(sql);

        var ctes = TokenBasedCteExtractor.Extract(tokens, sql.Length);

        Assert.Empty(ctes);
    }
}
