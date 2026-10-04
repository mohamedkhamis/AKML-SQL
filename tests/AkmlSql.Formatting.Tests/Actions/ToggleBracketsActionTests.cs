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

    [Fact]
    public void Add_brackets_leaves_keywords_and_built_in_names_alone()
    {
        // The lexer calls NOCOUNT, max, DATEADD, the date part and NOLOCK identifiers too;
        // bracketed, SQL Server rejects every one of them.
        const string sql = "SET NOCOUNT ON; DECLARE @x varchar(max); "
                           + "SELECT DATEADD(day, 1, GETDATE()) AS Tomorrow, o.OrderID FROM dbo.Orders AS o WITH (NOLOCK)";

        var result = new ToggleBracketsAction(addBrackets: true).Execute(sql, new FormattingProfile());

        Assert.True(result.Success);
        var text = result.FormattedText;
        Assert.Contains("SET NOCOUNT ON", text);
        Assert.Contains("(max)", text);
        Assert.Contains("DATEADD(day,", text);
        Assert.Contains("GETDATE()", text);
        Assert.Contains("WITH (NOLOCK)", text);
        Assert.Contains("[dbo].[Orders] AS [o]", text);
        Assert.Contains("[o].[OrderID]", text);
        Assert.Contains("AS [Tomorrow]", text);
        Assert.Contains("[varchar](max)", text);

        var parser = new Microsoft.SqlServer.TransactSql.ScriptDom.TSql170Parser(true);
        using var reader = new System.IO.StringReader(text);
        parser.Parse(reader, out var errors);
        Assert.Empty(errors);
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
