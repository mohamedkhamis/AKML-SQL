using Xunit;
using AkmlSql.Formatting.Actions;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Formatting.Tests.Actions;

public class ToggleBracketsActionTests
{
    private readonly ToggleBracketsAction _action = new();

    // ── Add brackets ──────────────────────────────────────────────────────

    [Fact]
    public void Execute_AddBrackets_WrapsIdentifiers()
    {
        var profile = new FormattingProfile
        {
            FormatActions =
            {
                AddSquareBrackets = true
            }
        };

        var result = _action.Execute("SELECT col FROM tbl", profile);

        Assert.True(result.Success);
        Assert.Contains("[col]", result.FormattedText);
        Assert.Contains("[tbl]", result.FormattedText);
    }

    [Fact]
    public void Execute_AddBrackets_WasModifiedTrue()
    {
        var profile = new FormattingProfile
        {
            FormatActions =
            {
                AddSquareBrackets = true
            }
        };

        var result = _action.Execute("SELECT col FROM tbl", profile);

        Assert.True(result.WasModified);
    }

    // ── Remove brackets ───────────────────────────────────────────────────

    [Fact]
    public void Execute_RemoveBrackets_UnwrapsIdentifiers()
    {
        var profile = new FormattingProfile
        {
            FormatActions =
            {
                AddSquareBrackets = false
            }
        };

        var result = _action.Execute("SELECT [col] FROM [tbl]", profile);

        Assert.True(result.Success);
        Assert.DoesNotContain("[col]", result.FormattedText);
        Assert.DoesNotContain("[tbl]", result.FormattedText);
    }

    [Fact]
    public void Execute_RemoveBrackets_NoBrackets_Unchanged()
    {
        var profile = new FormattingProfile
        {
            FormatActions =
            {
                AddSquareBrackets = false
            }
        };

        var result = _action.Execute("SELECT col FROM tbl", profile);

        Assert.True(result.Success);
        Assert.False(result.WasModified);
    }

    // ── No double brackets ─────────────────────────────────────────────────

    [Fact]
    public void Execute_AddBrackets_AlreadyBracketed_NoDoubleBrackets()
    {
        var profile = new FormattingProfile
        {
            FormatActions =
            {
                AddSquareBrackets = true
            }
        };

        // Parser will see [col] as a QuotedIdentifier, not an Identifier
        // so no extra brackets should be added
        var result = _action.Execute("SELECT [col] FROM [tbl]", profile);

        Assert.True(result.Success);
        Assert.DoesNotContain("[[", result.FormattedText);
    }

    // ── Empty SQL ─────────────────────────────────────────────────────────

    [Fact]
    public void Execute_EmptySql_NoThrow()
    {
        var profile = new FormattingProfile { FormatActions = { AddSquareBrackets = true } };

        var ex = Record.Exception(() => _action.Execute("", profile));

        Assert.Null(ex);
    }

    // ── ValidationPassed ─────────────────────────────────────────────────

    [Fact]
    public void Execute_ValidSql_ValidationPassed()
    {
        var profile = new FormattingProfile { FormatActions = { AddSquareBrackets = true } };

        var result = _action.Execute("SELECT col FROM tbl", profile);

        Assert.True(result.ValidationPassed);
    }
}
