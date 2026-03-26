using Xunit;
using AkmlSql.Formatting.Actions;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.Pipeline;

namespace AkmlSql.Formatting.Tests.Actions;

public class ToggleAsKeywordActionTests
{
    private readonly ToggleAsKeywordAction _action = new();

    // ── Remove AS ─────────────────────────────────────────────────────────

    [Fact]
    public void Execute_RemoveAs_RemovesAliasAs()
    {
        var profile = new FormattingProfile
        {
            FormatActions =
            {
                AddAsKeyword = false
            }
        };

        var result = _action.Execute("SELECT col AS alias FROM tbl", profile);

        Assert.True(result.Success);
        Assert.DoesNotContain(" AS ", result.FormattedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_RemoveAs_AliasStillPresent()
    {
        // After removing AS, the alias identifier should remain
        var profile = new FormattingProfile
        {
            FormatActions =
            {
                AddAsKeyword = false
            }
        };

        var result = _action.Execute("SELECT col AS myalias FROM tbl", profile);

        Assert.Contains("myalias", result.FormattedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_RemoveAs_NoAs_WasModifiedFalse()
    {
        var profile = new FormattingProfile
        {
            FormatActions =
            {
                AddAsKeyword = false
            }
        };

        // No AS keyword in the SQL
        var result = _action.Execute("SELECT col myalias FROM tbl", profile);

        Assert.False(result.WasModified);
    }

    [Fact]
    public void Execute_RemoveAs_WasModifiedTrue_WhenAsPresent()
    {
        var profile = new FormattingProfile
        {
            FormatActions =
            {
                AddAsKeyword = false
            }
        };

        var result = _action.Execute("SELECT col AS alias FROM tbl", profile);

        Assert.True(result.WasModified);
    }

    // ── Add AS (stub behaviour) ───────────────────────────────────────────

    [Fact]
    public void Execute_AddAs_ReturnsSuccess()
    {
        var profile = new FormattingProfile
        {
            FormatActions =
            {
                AddAsKeyword = true
            }
        };

        var result = _action.Execute("SELECT col alias FROM tbl", profile);

        // Current implementation is a stub — returns Success=true, WasModified=false
        Assert.True(result.Success);
    }

    [Fact]
    public void Execute_AddAs_WasModifiedFalse_StubImplementation()
    {
        var profile = new FormattingProfile
        {
            FormatActions =
            {
                AddAsKeyword = true
            }
        };

        var result = _action.Execute("SELECT col alias FROM tbl", profile);

        // Stub returns unchanged
        Assert.False(result.WasModified);
    }

    [Fact]
    public void Execute_AddAs_HasInfoDiagnostic()
    {
        var profile = new FormattingProfile
        {
            FormatActions =
            {
                AddAsKeyword = true
            }
        };

        var result = _action.Execute("SELECT col alias FROM tbl", profile);

        Assert.NotEmpty(result.Diagnostics);
        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Info);
    }

    // ── Empty SQL ─────────────────────────────────────────────────────────

    [Fact]
    public void Execute_EmptySql_NoThrow()
    {
        var profile = new FormattingProfile { FormatActions = { AddAsKeyword = false } };

        var ex = Record.Exception(() => _action.Execute("", profile));

        Assert.Null(ex);
    }
}
