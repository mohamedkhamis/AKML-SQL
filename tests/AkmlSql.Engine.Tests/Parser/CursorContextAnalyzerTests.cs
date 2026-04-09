using Xunit;
using AkmlSql.Engine.Parser;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Tests.Parser;

public class CursorContextAnalyzerTests
{
    private readonly CursorContextAnalyzer _analyzer = new();

    private static IList<TSqlParserToken> Tokenize(string sql)
    {
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        parser.Parse(reader, out _);
        // Use GetTokenStream to get the token list
        var svc = new TsqlParserService();
        return svc.GetTokenStream(sql);
    }

    // ── Empty tokens ─────────────────────────────────────────────────────

    [Fact]
    public void Analyze_EmptyTokens_ReturnsDefaultContext()
    {
        var context = _analyzer.Analyze([], 0);

        Assert.Equal(ClauseType.Unknown, context.ClauseType);
        Assert.False(context.InComment);
        Assert.False(context.InString);
    }

    // ── InComment detection ───────────────────────────────────────────────

    [Fact]
    public void Analyze_CursorInSingleLineComment_InCommentTrue()
    {
        var sql = "-- SELECT col";
        var tokens = Tokenize(sql);

        var context = _analyzer.Analyze(tokens, sql.Length);

        Assert.True(context.InComment);
    }

    [Fact]
    public void Analyze_CursorInMultilineComment_InCommentTrue()
    {
        var sql = "/* SELECT col */";
        var tokens = Tokenize(sql);

        var context = _analyzer.Analyze(tokens, 5);

        Assert.True(context.InComment);
    }

    // ── InString detection ────────────────────────────────────────────────

    [Fact]
    public void Analyze_CursorInStringLiteral_InStringTrue()
    {
        var sql = "SELECT 'hello world'";
        var tokens = Tokenize(sql);

        // Position inside the string literal
        var context = _analyzer.Analyze(tokens, sql.IndexOf("hello") + 2);

        Assert.True(context.InString);
    }

    // ── ClauseType detection ──────────────────────────────────────────────

    [Fact]
    public void Analyze_CursorAfterSelect_ClauseTypeSelect()
    {
        var sql = "SELECT ";
        var tokens = Tokenize(sql);

        var context = _analyzer.Analyze(tokens, sql.Length);

        Assert.Equal(ClauseType.Select, context.ClauseType);
    }

    [Fact]
    public void Analyze_CursorAfterFrom_ClauseTypeFrom()
    {
        var sql = "SELECT col FROM ";
        var tokens = Tokenize(sql);

        var context = _analyzer.Analyze(tokens, sql.Length);

        Assert.Equal(ClauseType.From, context.ClauseType);
    }

    [Fact]
    public void Analyze_CursorAfterWhere_ClauseTypeWhere()
    {
        var sql = "SELECT col FROM tbl WHERE ";
        var tokens = Tokenize(sql);

        var context = _analyzer.Analyze(tokens, sql.Length);

        Assert.Equal(ClauseType.Where, context.ClauseType);
    }

    [Fact]
    public void Analyze_CursorAfterGroupBy_ClauseTypeGroupBy()
    {
        // Regression guard: ScriptDom tokenizes GROUP as a dedicated keyword token
        // (not Identifier), so the analyzer must handle TSqlTokenType.By by walking
        // back to match on the text "GROUP".
        var sql = "SELECT a, COUNT(*) FROM tbl GROUP BY ";
        var tokens = Tokenize(sql);

        var context = _analyzer.Analyze(tokens, sql.Length);

        Assert.Equal(ClauseType.GroupBy, context.ClauseType);
    }

    [Fact]
    public void Analyze_CursorAfterOrderBy_ClauseTypeOrderBy()
    {
        var sql = "SELECT * FROM tbl ORDER BY ";
        var tokens = Tokenize(sql);

        var context = _analyzer.Analyze(tokens, sql.Length);

        Assert.Equal(ClauseType.OrderBy, context.ClauseType);
    }

    [Fact]
    public void Analyze_CursorInGroupByAfterComma_ClauseTypeGroupBy()
    {
        // Cursor is in the GROUP BY list after a comma — still GroupBy context.
        var sql = "SELECT a, b, COUNT(*) FROM tbl GROUP BY a, ";
        var tokens = Tokenize(sql);

        var context = _analyzer.Analyze(tokens, sql.Length);

        Assert.Equal(ClauseType.GroupBy, context.ClauseType);
    }

    [Fact]
    public void Analyze_CursorAfterHaving_ClauseTypeHaving()
    {
        var sql = "SELECT a, COUNT(*) FROM tbl GROUP BY a HAVING ";
        var tokens = Tokenize(sql);

        var context = _analyzer.Analyze(tokens, sql.Length);

        Assert.Equal(ClauseType.Having, context.ClauseType);
    }

    [Fact]
    public void Analyze_CursorAfterJoinOn_ClauseTypeJoinOn()
    {
        var sql = "SELECT * FROM a JOIN b ON ";
        var tokens = Tokenize(sql);

        var context = _analyzer.Analyze(tokens, sql.Length);

        Assert.Equal(ClauseType.JoinOn, context.ClauseType);
    }

    [Fact]
    public void Analyze_CursorAfterLeftJoin_ClauseTypeJoinTable()
    {
        var sql = "SELECT * FROM a LEFT JOIN ";
        var tokens = Tokenize(sql);

        var context = _analyzer.Analyze(tokens, sql.Length);

        Assert.Equal(ClauseType.JoinTable, context.ClauseType);
    }

    [Fact]
    public void Analyze_CursorAfterSemicolon_ClauseTypeUnknown()
    {
        // A semicolon ends the previous statement. A cursor immediately after one
        // should not inherit any clause type from the prior statement.
        var sql = "SELECT * FROM tbl; ";
        var tokens = Tokenize(sql);

        var context = _analyzer.Analyze(tokens, sql.Length);

        Assert.Equal(ClauseType.Unknown, context.ClauseType);
    }

    [Fact]
    public void Analyze_CursorAfterUnion_ClauseTypeSelect()
    {
        // Set operators act as soft statement boundaries so the next SELECT
        // gets a fresh context.
        var sql = "SELECT a FROM t1 UNION SELECT ";
        var tokens = Tokenize(sql);

        var context = _analyzer.Analyze(tokens, sql.Length);

        Assert.Equal(ClauseType.Select, context.ClauseType);
    }

    // ── PrecedingDot ──────────────────────────────────────────────────────

    [Fact]
    public void Analyze_CursorAfterDot_PrecedingDotTrue()
    {
        // Cursor must be INSIDE the token after the dot to trigger PrecedingDot
        var sql = "SELECT t.col";
        var tokens = Tokenize(sql);
        // Position cursor inside "col" — dot ends at offset 9, col starts at 9; cursor at 10 is inside col
        int cursorPos = "SELECT t.c".Length;  // = 10

        var context = _analyzer.Analyze(tokens, cursorPos);

        Assert.True(context.PrecedingDot);
    }

    [Fact]
    public void Analyze_CursorAfterDot_DotPrefixSet()
    {
        var sql = "SELECT t.col";
        var tokens = Tokenize(sql);
        int cursorPos = "SELECT t.c".Length;  // cursor inside "col"

        var context = _analyzer.Analyze(tokens, cursorPos);

        Assert.Equal("t", context.DotPrefix);
    }

    // ── PartialText ───────────────────────────────────────────────────────

    [Fact]
    public void Analyze_PartialIdentifier_PartialTextExtracted()
    {
        var sql = "SELECT sel";
        var tokens = Tokenize(sql);

        var context = _analyzer.Analyze(tokens, sql.Length);

        // Should capture the partial identifier being typed
        Assert.NotEmpty(context.PartialText);
    }

    // ── CursorOffset ─────────────────────────────────────────────────────

    [Fact]
    public void Analyze_CursorOffsetPreserved()
    {
        var sql = "SELECT 1";
        var tokens = Tokenize(sql);

        var context = _analyzer.Analyze(tokens, 7);

        Assert.Equal(7, context.CursorOffset);
    }
}
