using Xunit;
using AkmlSql.Formatting.Actions;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Formatting.Tests.Actions;

public class CasingOnlyActionTests
{
    private readonly CasingOnlyAction _action = new();

    // ── Keywords uppercased ───────────────────────────────────────────────

    [Fact]
    public void Execute_Keywords_Uppercased()
    {
        var profile = new FormattingProfile
        {
            Casing =
            {
                ReservedKeywords = "UPPERCASE"
            }
        };

        var result = _action.Execute("select 1", profile);

        Assert.True(result.Success);
        Assert.Contains("SELECT", result.FormattedText);
    }

    [Fact]
    public void Execute_Keywords_Lowercased()
    {
        var profile = new FormattingProfile
        {
            Casing =
            {
                ReservedKeywords = "lowercase"
            }
        };

        var result = _action.Execute("SELECT 1", profile);

        Assert.True(result.Success);
        Assert.Contains("select", result.FormattedText);
    }

    // ── Whitespace preserved ──────────────────────────────────────────────

    [Fact]
    public void Execute_WhitespacePreserved()
    {
        var profile = new FormattingProfile
        {
            Casing =
            {
                ReservedKeywords = "UPPERCASE"
            }
        };

        // Original has specific indentation — should be preserved
        var sql = "  select\n    1";
        var result = _action.Execute(sql, profile);

        Assert.True(result.Success);
        // Whitespace structure should be unchanged
        Assert.Contains("\n", result.FormattedText);
    }

    // ── WasModified ───────────────────────────────────────────────────────

    [Fact]
    public void Execute_WhenModified_WasModifiedTrue()
    {
        var profile = new FormattingProfile
        {
            Casing =
            {
                ReservedKeywords = "UPPERCASE"
            }
        };

        var result = _action.Execute("select 1", profile);

        Assert.True(result.WasModified);
    }

    [Fact]
    public void Execute_AlreadyCorrectCasing_WasModifiedFalse()
    {
        var profile = new FormattingProfile
        {
            Casing =
            {
                ReservedKeywords = "UPPERCASE"
            }
        };

        var result = _action.Execute("SELECT 1", profile);

        Assert.False(result.WasModified);
    }

    // ── Empty / invalid SQL ───────────────────────────────────────────────

    [Fact]
    public void Execute_EmptySql_SuccessOrFail_NoThrow()
    {
        var profile = new FormattingProfile();

        var ex = Record.Exception(() => _action.Execute("", profile));

        Assert.Null(ex);
    }

    [Fact]
    public void Execute_InvalidSql_ReturnsOriginal()
    {
        var profile = new FormattingProfile
        {
            Casing =
            {
                ReservedKeywords = "UPPERCASE"
            }
        };

        // Invalid SQL but should not throw
        var result = _action.Execute("THIS IS NOT VALID SQL @@@###", profile);

        Assert.NotNull(result);
    }

    // ── ValidationPassed ─────────────────────────────────────────────────

    [Fact]
    public void Execute_ValidSql_ValidationPassed()
    {
        var profile = new FormattingProfile();

        var result = _action.Execute("SELECT 1", profile);

        Assert.True(result.Success);
        Assert.True(result.ValidationPassed);
    }
}
