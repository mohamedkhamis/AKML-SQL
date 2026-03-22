using Xunit;
using AkmlSql.Formatting.Actions;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Formatting.Tests.Actions;

public class RemoveSemicolonsActionTests
{
    private readonly RemoveSemicolonsAction _action = new();
    private static FormattingProfile DefaultProfile() => new();

    // ── Basic removal ────────────────────────────────────────────────────

    [Fact]
    public void Execute_StatementWithSemicolon_RemovesSemicolon()
    {
        var result = _action.Execute("SELECT 1;", DefaultProfile());

        Assert.True(result.Success);
        Assert.DoesNotContain(";", result.FormattedText);
    }

    [Fact]
    public void Execute_StatementWithSemicolon_WasModifiedTrue()
    {
        var result = _action.Execute("SELECT 1;", DefaultProfile());

        Assert.True(result.WasModified);
    }

    // ── No semicolons ─────────────────────────────────────────────────────

    [Fact]
    public void Execute_NoSemicolons_WasModifiedFalse()
    {
        var result = _action.Execute("SELECT 1", DefaultProfile());

        Assert.False(result.WasModified);
    }

    [Fact]
    public void Execute_NoSemicolons_OriginalTextPreserved()
    {
        const string sql = "SELECT col FROM tbl";
        var result = _action.Execute(sql, DefaultProfile());

        Assert.Equal(sql, result.FormattedText);
    }

    // ── Multiple semicolons ───────────────────────────────────────────────

    [Fact]
    public void Execute_MultipleSemicolons_AllRemoved()
    {
        var sql = "SELECT 1;\nSELECT 2;\nSELECT 3;";

        var result = _action.Execute(sql, DefaultProfile());

        Assert.True(result.Success);
        Assert.DoesNotContain(";", result.FormattedText);
    }

    // ── Empty SQL ─────────────────────────────────────────────────────────

    [Fact]
    public void Execute_EmptySql_NoThrow()
    {
        var ex = Record.Exception(() => _action.Execute("", DefaultProfile()));

        Assert.Null(ex);
    }

    // ── Success flag ─────────────────────────────────────────────────────

    [Fact]
    public void Execute_ValidSql_SuccessTrue()
    {
        var result = _action.Execute("SELECT 1;", DefaultProfile());

        Assert.True(result.Success);
    }

    // ── ValidationPassed ─────────────────────────────────────────────────

    [Fact]
    public void Execute_ValidSql_ValidationPassed()
    {
        var result = _action.Execute("SELECT 1;", DefaultProfile());

        Assert.True(result.ValidationPassed);
    }
}
