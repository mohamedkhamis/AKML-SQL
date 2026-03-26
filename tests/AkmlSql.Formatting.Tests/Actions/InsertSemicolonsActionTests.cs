using Xunit;
using AkmlSql.Formatting.Actions;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Formatting.Tests.Actions;

public class InsertSemicolonsActionTests
{
    private readonly InsertSemicolonsAction _action = new();
    private static FormattingProfile DefaultProfile() => new();

    // ── Basic insertion ───────────────────────────────────────────────────

    [Fact]
    public void Execute_StatementWithoutSemicolon_InsertsSemicolon()
    {
        var result = _action.Execute("SELECT 1", DefaultProfile());

        Assert.True(result.Success);
        Assert.Contains(";", result.FormattedText);
    }

    [Fact]
    public void Execute_StatementWithSemicolon_NoDoubleSemicolon()
    {
        var result = _action.Execute("SELECT 1;", DefaultProfile());

        Assert.True(result.Success);
        Assert.DoesNotContain(";;", result.FormattedText);
    }

    [Fact]
    public void Execute_StatementAlreadyHasSemicolon_WasModifiedFalse()
    {
        var result = _action.Execute("SELECT 1;", DefaultProfile());

        Assert.False(result.WasModified);
    }

    // ── Multiple statements ───────────────────────────────────────────────

    [Fact]
    public void Execute_MultipleStatements_AllGetSemicolons()
    {
        var sql = "SELECT 1\nSELECT 2\nSELECT 3";

        var result = _action.Execute(sql, DefaultProfile());

        Assert.True(result.Success);
        // Each statement should have a semicolon inserted
        var count = result.FormattedText.Count(c => c == ';');
        Assert.Equal(3, count);
    }

    [Fact]
    public void Execute_MixedStatements_OnlyMissingOnesInserted()
    {
        var sql = "SELECT 1;\nSELECT 2\nSELECT 3;";

        var result = _action.Execute(sql, DefaultProfile());

        Assert.True(result.Success);
        var count = result.FormattedText.Count(c => c == ';');
        Assert.Equal(3, count);
    }

    // ── Whitespace handling ───────────────────────────────────────────────

    [Fact]
    public void Execute_TrailingWhitespace_SemicolonInsertedBeforeWhitespace()
    {
        // Semicolon should go after last non-whitespace char, before trailing spaces
        var result = _action.Execute("SELECT 1   ", DefaultProfile());

        Assert.True(result.Success);
        Assert.Contains(";", result.FormattedText);
    }

    // ── WasModified ──────────────────────────────────────────────────────

    [Fact]
    public void Execute_MissingSemicolon_WasModifiedTrue()
    {
        var result = _action.Execute("SELECT 1", DefaultProfile());

        Assert.True(result.WasModified);
    }

    // ── Empty SQL ─────────────────────────────────────────────────────────

    [Fact]
    public void Execute_EmptySql_NoThrow()
    {
        var ex = Record.Exception(() => _action.Execute("", DefaultProfile()));

        Assert.Null(ex);
    }

    [Fact]
    public void Execute_EmptySql_WasModifiedFalse()
    {
        var result = _action.Execute("", DefaultProfile());

        Assert.False(result.WasModified);
    }

    // ── ValidationPassed ─────────────────────────────────────────────────

    [Fact]
    public void Execute_ValidSql_ValidationPassed()
    {
        var result = _action.Execute("SELECT 1", DefaultProfile());

        Assert.True(result.ValidationPassed);
    }
}
