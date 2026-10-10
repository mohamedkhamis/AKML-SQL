using Xunit;
using AkmlSql.Engine.Parser;

namespace AkmlSql.Engine.Tests.Parser;

public class TsqlParserServiceTests
{
    private readonly TsqlParserService _svc = new();

    // ── GetTokenStream ────────────────────────────────────────────────────

    [Fact]
    public void GetTokenStream_SimpleSelect_ReturnsTokens()
    {
        var tokens = _svc.GetTokenStream("SELECT 1");

        Assert.NotEmpty(tokens);
    }

    [Fact]
    public void GetTokenStream_EmptySql_ReturnsTokensOrEmpty()
    {
        var ex = Record.Exception(() => _svc.GetTokenStream(""));
        Assert.Null(ex);
    }

    // ── Parse ─────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_ValidSql_ReturnsScript()
    {
        var script = _svc.Parse("SELECT 1;", out var errors);

        Assert.NotNull(script);
        Assert.Empty(errors);
    }

    [Fact]
    public void Parse_InvalidSql_ReturnsErrors()
    {
        var script = _svc.Parse("SELECT FROM WHERE", out var errors);

        // Should produce parse errors
        Assert.True(errors.Count > 0 || script == null,
            "Expected parse errors for invalid SQL");
    }

    [Fact]
    public void Parse_EmptySql_ReturnsScriptOrNull()
    {
        var ex = Record.Exception(() => _svc.Parse("", out _));
        Assert.Null(ex);
    }

    // ── ParseWithSuffix ───────────────────────────────────────────────────

    [Fact]
    public void ParseWithSuffix_IncompleteSelect_ParsesSuccessfully()
    {
        // Incomplete SQL — ParseWithSuffix appends dummy tokens
        var script = _svc.ParseWithSuffix("SELECT ", out _);

        // Should not throw; result may be null or have batches
        Assert.True(true); // just testing no exception
    }

    [Fact]
    public void ParseWithSuffix_ValidSql_ReturnsScript()
    {
        var script = _svc.ParseWithSuffix("SELECT 1;", out _);

        Assert.NotNull(script);
    }

    // ── SplitBatches (spec 041 T075, research R16, N3) ─────────────────────
    // GO separates batches as SSMS's sqlcmd-style splitter does: a GO token at the start of a line
    // (ScriptDom's lexer decides — never inside a string, a comment or [GO]), an optional repeat
    // count, a trailing comment; anything else on the GO line refuses the whole script.

    [Fact]
    public void SplitBatches_SingleBatch_OneEntry()
    {
        var split = _svc.SplitBatches("SELECT 1;");

        var batch = Assert.Single(split.Batches);
        Assert.Equal("SELECT 1;", batch.Text);
        Assert.Equal(1, batch.StartLine);
        Assert.Equal(1, batch.RepeatCount);
        Assert.Null(split.SeparatorError);
    }

    [Fact]
    public void SplitBatches_TwoBatchesSeparatedByGo_TwoEntries()
    {
        var split = _svc.SplitBatches("SELECT 1;\nGO\nSELECT 2;");

        Assert.Equal(2, split.Batches.Count);
        Assert.Equal("SELECT 1;\n", split.Batches[0].Text);
        Assert.Equal("SELECT 2;", split.Batches[1].Text);
        Assert.Equal(new[] { 0, 1 }, split.Batches.Select(b => b.Index));
    }

    [Fact]
    public void SplitBatches_GoAtStart_FirstBatchEmpty()
    {
        var split = _svc.SplitBatches("GO\nSELECT 1;");

        // GO at start — only one non-empty batch, which starts on line 2
        var batch = Assert.Single(split.Batches);
        Assert.Equal(2, batch.StartLine);
        Assert.Equal(0, batch.Index);
    }

    [Fact]
    public void SplitBatches_EmptySql_ReturnsEmpty()
    {
        Assert.Empty(_svc.SplitBatches("").Batches);
    }

    [Fact]
    public void SplitBatches_NullSql_ReturnsEmpty()
    {
        Assert.Empty(_svc.SplitBatches(null).Batches);
    }

    [Fact]
    public void SplitBatches_GoIsCaseInsensitive()
    {
        Assert.Equal(2, _svc.SplitBatches("SELECT 1;\ngo\nSELECT 2;").Batches.Count);
        Assert.Equal(2, _svc.SplitBatches("SELECT 1;\nGo\nSELECT 2;").Batches.Count);
    }

    [Fact]
    public void SplitBatches_OffsetIsCorrect()
    {
        const string sql = "SELECT 1;\nGO\nSELECT 2;";
        var split = _svc.SplitBatches(sql);

        Assert.Equal(0, split.Batches[0].StartOffset);
        Assert.Equal(sql.IndexOf("GO", StringComparison.Ordinal), split.Batches[0].EndOffset);
        Assert.Equal(sql.IndexOf("SELECT 2", StringComparison.Ordinal), split.Batches[1].StartOffset);
        Assert.Equal(sql.Length, split.Batches[1].EndOffset);
        Assert.Equal(sql.Substring(split.Batches[1].StartOffset, split.Batches[1].EndOffset - split.Batches[1].StartOffset), split.Batches[1].Text);
    }

    [Theory]
    [InlineData("SELECT 1;\n  GO  \nSELECT 2;")]          // indented, trailing spaces
    [InlineData("SELECT 1;\r\nGO\r\nSELECT 2;")]          // CRLF
    [InlineData("SELECT 1;\n/* note */ GO\nSELECT 2;")]   // a block comment before GO on the same line
    [InlineData("SELECT 1;\nGO -- next batch\nSELECT 2;")] // a trailing comment
    public void SplitBatches_GoOnItsOwnLine_Separates(string sql)
    {
        var split = _svc.SplitBatches(sql);

        Assert.Null(split.SeparatorError);
        Assert.Equal(2, split.Batches.Count);
        Assert.StartsWith("SELECT 2;", split.Batches[1].Text);
        Assert.Equal(3, split.Batches[1].StartLine);
    }

    [Fact]
    public void SplitBatches_GoAtEndOfFile_LeavesOneBatch()
    {
        var split = _svc.SplitBatches("SELECT 1;\nGO");

        var batch = Assert.Single(split.Batches);
        Assert.Equal("SELECT 1;\n", batch.Text);
    }

    [Fact]
    public void SplitBatches_TwoGosInARow_SkipTheEmptyBatch()
    {
        var split = _svc.SplitBatches("SELECT 1;\nGO\nGO\nSELECT 2;");

        Assert.Equal(2, split.Batches.Count);
        Assert.Equal(4, split.Batches[1].StartLine);
        Assert.Equal(1, split.Batches[1].Index);
    }

    [Theory]
    [InlineData("SELECT 'a\nGO\nb';")]                 // inside a string
    [InlineData("SELECT 1; -- GO\nSELECT 2;")]          // inside a line comment
    [InlineData("/*\nGO\n*/ SELECT 1;")]                // inside a block comment
    [InlineData("SELECT 1 AS\n[GO]\n;")]                // a bracketed identifier
    [InlineData("SELECT category FROM t;")]             // a word containing go
    public void SplitBatches_GoThatIsNotASeparator_KeepsOneBatch(string sql)
    {
        var split = _svc.SplitBatches(sql);

        Assert.Null(split.SeparatorError);
        Assert.Equal(sql, Assert.Single(split.Batches).Text);
    }

    [Theory]
    [InlineData("SELECT 1;\nGO 3\nSELECT 2;", 3)]
    [InlineData("SELECT 1;\nGO 3 -- c\nSELECT 2;", 3)]
    [InlineData("SELECT 1;\ngo 12\nSELECT 2;", 12)]
    public void SplitBatches_RepeatCount_AppliesToTheBatchBeforeIt(string sql, int expected)
    {
        var split = _svc.SplitBatches(sql);

        Assert.Null(split.SeparatorError);
        Assert.Equal(expected, split.Batches[0].RepeatCount);
        Assert.Equal(1, split.Batches[1].RepeatCount);
    }

    [Theory]
    [InlineData("SELECT 1;\nGO SELECT 2;")]
    [InlineData("SELECT 1;\nGO;")]
    [InlineData("SELECT 1;\nGO 0\nSELECT 2;")]
    [InlineData("SELECT 1;\nGO -1\nSELECT 2;")]
    [InlineData("SELECT 1;\nGO 2 3\nSELECT 2;")]
    [InlineData("SELECT 1;\nGO 99999999999\nSELECT 2;")]
    public void SplitBatches_AnythingElseOnTheGoLine_IsASeparatorError(string sql)
    {
        var split = _svc.SplitBatches(sql);

        Assert.Equal(TsqlParserService.GoSeparatorError, split.SeparatorError);
        Assert.Equal("Incorrect syntax was encountered while parsing GO.", split.SeparatorError);
    }

    [Fact]
    public void SplitBatches_UnterminatedString_SwallowsTheRestIntoTheLastBatch()
    {
        const string sql = "SELECT 1;\nGO\nSELECT 'oops;\nGO\nSELECT 3;";
        var split = _svc.SplitBatches(sql);

        Assert.Equal(2, split.Batches.Count);
        Assert.Equal("SELECT 'oops;\nGO\nSELECT 3;", split.Batches[1].Text);
    }

    [Fact]
    public void SplitBatches_WhitespaceOnlyBatches_AreSkipped()
    {
        var split = _svc.SplitBatches("  \n\nGO\n   \nGO\nSELECT 1;");

        var batch = Assert.Single(split.Batches);
        Assert.Equal(6, batch.StartLine);
    }

    [Fact]
    public void SplitBatches_StartLine_CountsTheLinesAfterThePreviousGo_IncludingBlankOnes()
    {
        const string sql = "SELECT 1;\nSELECT 2;\nGO\n\n\nSELECT 3;\nGO\nSELECT 4;";
        var split = _svc.SplitBatches(sql);

        Assert.Equal(new[] { 1, 4, 8 }, split.Batches.Select(b => b.StartLine));
        // The server numbers lines from the batch's own first line: the blank lines are part of it.
        Assert.StartsWith("\n\nSELECT 3;", split.Batches[1].Text);
    }

    // ── SetServerVersion ──────────────────────────────────────────────────

    [Fact]
    public void SetServerVersion_SameVersion_NoException()
    {
        var ex = Record.Exception(() => _svc.SetServerVersion(17));
        Assert.Null(ex);
    }

    [Fact]
    public void SetServerVersion_DifferentVersions_ParsesCorrectly()
    {
        _svc.SetServerVersion(16);
        var script1 = _svc.Parse("SELECT 1;", out var errors1);

        _svc.SetServerVersion(17);
        var script2 = _svc.Parse("SELECT 1;", out var errors2);

        Assert.Empty(errors1);
        Assert.Empty(errors2);
    }
}
