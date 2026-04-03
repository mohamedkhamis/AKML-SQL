using Xunit;
using AkmlSql.Engine.Parser;

namespace AkmlSql.Engine.Tests.Parser;

public class SuffixCompletionHelperTests
{
    private const string Dummy = "__akml_dummy__";

    // ── Empty / whitespace ─────────────────────────────────────────────────

    [Fact]
    public void AppendDummyTokens_EmptyString_ReturnsSelectDummy()
    {
        Assert.Equal($"SELECT {Dummy}", SuffixCompletionHelper.AppendDummyTokens(""));
    }

    [Fact]
    public void AppendDummyTokens_WhitespaceOnly_ReturnsSelectDummy()
    {
        Assert.Equal($"SELECT {Dummy}", SuffixCompletionHelper.AppendDummyTokens("   "));
    }

    // ── SELECT ─────────────────────────────────────────────────────────────

    [Fact]
    public void AppendDummyTokens_EndsWithSELECT_AppendsDummy()
    {
        var result = SuffixCompletionHelper.AppendDummyTokens("SELECT");
        Assert.Equal($"SELECT {Dummy}", result);
    }

    [Fact]
    public void AppendDummyTokens_EndsWithSelect_CaseInsensitive()
    {
        var result = SuffixCompletionHelper.AppendDummyTokens("select");
        Assert.Equal($"select {Dummy}", result);
    }

    // ── FROM ──────────────────────────────────────────────────────────────

    [Fact]
    public void AppendDummyTokens_EndsWithFROM_AppendsDummy()
    {
        var result = SuffixCompletionHelper.AppendDummyTokens("SELECT 1 FROM");
        Assert.Equal($"SELECT 1 FROM {Dummy}", result);
    }

    // ── JOIN variants ─────────────────────────────────────────────────────

    [Fact]
    public void AppendDummyTokens_EndsWithJOIN_AppendsDummyWithOn()
    {
        var result = SuffixCompletionHelper.AppendDummyTokens("SELECT 1 FROM T JOIN");
        Assert.Equal($"SELECT 1 FROM T JOIN {Dummy} ON 1=1", result);
    }

    [Fact]
    public void AppendDummyTokens_EndsWithINNER_JOIN_AppendsDummyWithOn()
    {
        var result = SuffixCompletionHelper.AppendDummyTokens("SELECT 1 FROM T INNER JOIN");
        Assert.Equal($"SELECT 1 FROM T INNER JOIN {Dummy} ON 1=1", result);
    }

    [Fact]
    public void AppendDummyTokens_EndsWithLEFT_JOIN_AppendsDummyWithOn()
    {
        var result = SuffixCompletionHelper.AppendDummyTokens("SELECT 1 FROM T LEFT JOIN");
        Assert.Equal($"SELECT 1 FROM T LEFT JOIN {Dummy} ON 1=1", result);
    }

    [Fact]
    public void AppendDummyTokens_EndsWithRIGHT_JOIN_AppendsDummyWithOn()
    {
        var result = SuffixCompletionHelper.AppendDummyTokens("SELECT 1 FROM T RIGHT JOIN");
        Assert.Equal($"SELECT 1 FROM T RIGHT JOIN {Dummy} ON 1=1", result);
    }

    [Fact]
    public void AppendDummyTokens_EndsWithCROSS_JOIN_AppendsDummy()
    {
        var result = SuffixCompletionHelper.AppendDummyTokens("SELECT 1 FROM T CROSS JOIN");
        Assert.Contains(Dummy, result);
    }

    [Fact]
    public void AppendDummyTokens_EndsWithLEFT_OUTER_JOIN_AppendsDummyWithOn()
    {
        var result = SuffixCompletionHelper.AppendDummyTokens("SELECT 1 FROM T LEFT OUTER JOIN");
        Assert.Equal($"SELECT 1 FROM T LEFT OUTER JOIN {Dummy} ON 1=1", result);
    }

    [Fact]
    public void AppendDummyTokens_EndsWithRIGHT_OUTER_JOIN_AppendsDummyWithOn()
    {
        var result = SuffixCompletionHelper.AppendDummyTokens("SELECT 1 FROM T RIGHT OUTER JOIN");
        Assert.Equal($"SELECT 1 FROM T RIGHT OUTER JOIN {Dummy} ON 1=1", result);
    }

    [Fact]
    public void AppendDummyTokens_EndsWithFULL_OUTER_JOIN_AppendsDummyWithOn()
    {
        var result = SuffixCompletionHelper.AppendDummyTokens("SELECT 1 FROM T FULL OUTER JOIN");
        Assert.Equal($"SELECT 1 FROM T FULL OUTER JOIN {Dummy} ON 1=1", result);
    }

    [Fact]
    public void AppendDummyTokens_EndsWithFULL_JOIN_AppendsDummyWithOn()
    {
        var result = SuffixCompletionHelper.AppendDummyTokens("SELECT 1 FROM T FULL JOIN");
        Assert.Equal($"SELECT 1 FROM T FULL JOIN {Dummy} ON 1=1", result);
    }

    // ── WHERE / AND / OR ──────────────────────────────────────────────────

    [Fact]
    public void AppendDummyTokens_EndsWithWHERE_AppendsExpression()
    {
        var result = SuffixCompletionHelper.AppendDummyTokens("SELECT 1 FROM T WHERE");
        Assert.Equal($"SELECT 1 FROM T WHERE {Dummy} = 1", result);
    }

    [Fact]
    public void AppendDummyTokens_EndsWithAND_AppendsExpression()
    {
        var result = SuffixCompletionHelper.AppendDummyTokens("SELECT 1 FROM T WHERE x=1 AND");
        Assert.Equal($"SELECT 1 FROM T WHERE x=1 AND {Dummy} = 1", result);
    }

    [Fact]
    public void AppendDummyTokens_EndsWithOR_AppendsExpression()
    {
        var result = SuffixCompletionHelper.AppendDummyTokens("SELECT 1 FROM T WHERE x=1 OR");
        Assert.Equal($"SELECT 1 FROM T WHERE x=1 OR {Dummy} = 1", result);
    }

    // ── SET ───────────────────────────────────────────────────────────────

    [Fact]
    public void AppendDummyTokens_EndsWithSET_AppendsColEqValue()
    {
        var result = SuffixCompletionHelper.AppendDummyTokens("UPDATE T SET");
        Assert.Equal($"UPDATE T SET {Dummy} = 1", result);
    }

    // ── Dot ───────────────────────────────────────────────────────────────

    [Fact]
    public void AppendDummyTokens_EndsWithDot_AppendsDummyNoDot()
    {
        var result = SuffixCompletionHelper.AppendDummyTokens("SELECT dbo.");
        Assert.Equal($"SELECT dbo.{Dummy}", result);
    }

    // ── EXEC / EXECUTE ────────────────────────────────────────────────────

    [Fact]
    public void AppendDummyTokens_EndsWithEXEC_AppendsDummy()
    {
        var result = SuffixCompletionHelper.AppendDummyTokens("EXEC");
        Assert.Equal($"EXEC {Dummy}", result);
    }

    [Fact]
    public void AppendDummyTokens_EndsWithEXECUTE_AppendsDummy()
    {
        var result = SuffixCompletionHelper.AppendDummyTokens("EXECUTE");
        Assert.Equal($"EXECUTE {Dummy}", result);
    }

    // ── ORDER BY / GROUP BY ───────────────────────────────────────────────

    [Fact]
    public void AppendDummyTokens_EndsWithORDER_BY_AppendsDummy()
    {
        var result = SuffixCompletionHelper.AppendDummyTokens("SELECT 1 FROM T ORDER BY");
        Assert.Equal($"SELECT 1 FROM T ORDER BY {Dummy}", result);
    }

    [Fact]
    public void AppendDummyTokens_EndsWithGROUP_BY_AppendsDummy()
    {
        var result = SuffixCompletionHelper.AppendDummyTokens("SELECT x FROM T GROUP BY");
        Assert.Equal($"SELECT x FROM T GROUP BY {Dummy}", result);
    }

    // ── Default ───────────────────────────────────────────────────────────

    [Fact]
    public void AppendDummyTokens_OtherText_AppendsDummy()
    {
        var result = SuffixCompletionHelper.AppendDummyTokens("SELECT col1, col2");
        Assert.Equal($"SELECT col1, col2 {Dummy}", result);
    }

    [Fact]
    public void AppendDummyTokens_TrimsTrailingWhitespace()
    {
        var result = SuffixCompletionHelper.AppendDummyTokens("SELECT   ");
        // TrimEnd makes "SELECT" then hits the SELECT branch
        Assert.Equal($"SELECT {Dummy}", result);
    }

    // ── IsDummyIdentifier ─────────────────────────────────────────────────

    [Fact]
    public void IsDummyIdentifier_MatchesDummy()
    {
        Assert.True(SuffixCompletionHelper.IsDummyIdentifier(Dummy));
    }

    [Fact]
    public void IsDummyIdentifier_CaseInsensitive()
    {
        Assert.True(SuffixCompletionHelper.IsDummyIdentifier(Dummy.ToUpperInvariant()));
    }

    [Fact]
    public void IsDummyIdentifier_OtherIdentifier_ReturnsFalse()
    {
        Assert.False(SuffixCompletionHelper.IsDummyIdentifier("MyTable"));
    }

    [Fact]
    public void IsDummyIdentifier_Empty_ReturnsFalse()
    {
        Assert.False(SuffixCompletionHelper.IsDummyIdentifier(""));
    }
}
