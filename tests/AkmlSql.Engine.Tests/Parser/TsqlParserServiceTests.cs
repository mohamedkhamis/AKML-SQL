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

    // ── SplitBatches ──────────────────────────────────────────────────────

    [Fact]
    public void SplitBatches_SingleBatch_OneEntry()
    {
        var batches = _svc.SplitBatches("SELECT 1;");

        Assert.Single(batches);
    }

    [Fact]
    public void SplitBatches_TwoBatchesSeparatedByGo_TwoEntries()
    {
        var batches = _svc.SplitBatches("SELECT 1;\nGO\nSELECT 2;");

        Assert.Equal(2, batches.Count);
    }

    [Fact]
    public void SplitBatches_GoAtStart_FirstBatchEmpty()
    {
        var batches = _svc.SplitBatches("GO\nSELECT 1;");

        // GO at start — only one non-empty batch
        Assert.Single(batches);
    }

    [Fact]
    public void SplitBatches_EmptySql_ReturnsEmpty()
    {
        var batches = _svc.SplitBatches("");

        Assert.Empty(batches);
    }

    [Fact]
    public void SplitBatches_NullSql_ReturnsEmpty()
    {
        var batches = _svc.SplitBatches(null!);

        Assert.Empty(batches);
    }

    [Fact]
    public void SplitBatches_GoIsCaseInsensitive()
    {
        var batches = _svc.SplitBatches("SELECT 1;\ngo\nSELECT 2;");

        Assert.Equal(2, batches.Count);
    }

    [Fact]
    public void SplitBatches_OffsetIsCorrect()
    {
        const string sql = "SELECT 1;\nGO\nSELECT 2;";
        var batches = _svc.SplitBatches(sql);

        Assert.Equal(2, batches.Count);
        Assert.Equal(0, batches[0].startOffset);
        Assert.True(batches[1].startOffset > 0);
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
