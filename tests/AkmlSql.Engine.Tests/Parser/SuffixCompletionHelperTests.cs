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

    // ── Spec 032 US2 (T016) — RepairAtCursor: repair AT the caret, not the tail ──

    private static bool ParsesClean(string sql)
    {
        var parser = new Microsoft.SqlServer.TransactSql.ScriptDom.TSql170Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        parser.Parse(reader, out var errors);
        return errors == null || errors.Count == 0;
    }

    [Fact]
    public void RepairAtCursor_IncompleteSelectInsideSubquery_Parses()
    {
        // Broken AT the caret (inside parens), valid after it — tail-append can't fix this.
        var sql = "SELECT * FROM dbo.Orders o WHERE o.OrderID IN (SELECT  FROM dbo.OrderDetails)";
        var caret = sql.IndexOf("(SELECT ", StringComparison.Ordinal) + "(SELECT ".Length;
        Assert.False(ParsesClean(sql), "precondition: the raw SQL must not parse");

        var repaired = SuffixCompletionHelper.RepairAtCursor(sql, caret);

        Assert.True(ParsesClean(repaired), $"expected repaired SQL to parse: {repaired}");
        Assert.Contains(Dummy, repaired);
    }

    [Fact]
    public void RepairAtCursor_CaretInsideLaterCteBody_Parses()
    {
        // Campaign E6 shape: prefix-parse used to die on the unbalanced parens of a later CTE body.
        var sql = "WITH x AS (SELECT OrderID FROM dbo.Orders), y AS (SELECT  FROM dbo.OrderDetails) SELECT * FROM y";
        var caret = sql.IndexOf("AS (SELECT  FROM dbo.OrderDetails", StringComparison.Ordinal) + "AS (SELECT ".Length;
        Assert.False(ParsesClean(sql), "precondition: the raw SQL must not parse");

        var repaired = SuffixCompletionHelper.RepairAtCursor(sql, caret);

        Assert.True(ParsesClean(repaired), $"expected repaired SQL to parse: {repaired}");
    }

    [Fact]
    public void RepairAtCursor_ValidTail_KeepsSuffixIntact()
    {
        var sql = "SELECT * FROM dbo.Orders o WHERE o.OrderID IN (SELECT  FROM dbo.OrderDetails)";
        var caret = sql.IndexOf("(SELECT ", StringComparison.Ordinal) + "(SELECT ".Length;

        var repaired = SuffixCompletionHelper.RepairAtCursor(sql, caret);

        Assert.EndsWith("FROM dbo.OrderDetails)", repaired);
    }

    // ── Spec 032 US2/US7 (T016, H4) — keyword tail-match needs a word boundary ──

    [Fact]
    public void AppendDummyTokens_IdentifierEndingInOr_NotTreatedAsOrOperator()
    {
        // "…dbo.Or" is a partially typed identifier, not the OR operator.
        var result = SuffixCompletionHelper.AppendDummyTokens("SELECT * FROM dbo.Or");

        Assert.False(result.EndsWith("= 1"), $"identifier tail misread as OR: {result}");
    }

    [Fact]
    public void AppendDummyTokens_IdentifierEndingInAnd_NotTreatedAsAndOperator()
    {
        var result = SuffixCompletionHelper.AppendDummyTokens("SELECT * FROM Grand");

        Assert.False(result.EndsWith("= 1"), $"identifier tail misread as AND: {result}");
    }

    [Fact]
    public void AppendDummyTokens_RealOrKeyword_StillRepaired()
    {
        var result = SuffixCompletionHelper.AppendDummyTokens("SELECT * FROM T WHERE a = 1 OR");

        Assert.EndsWith("= 1", result);
    }
}
