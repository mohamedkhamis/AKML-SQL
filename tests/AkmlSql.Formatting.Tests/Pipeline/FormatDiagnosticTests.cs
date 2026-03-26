using Xunit;
using AkmlSql.Formatting.Pipeline;

namespace AkmlSql.Formatting.Tests.Pipeline;

public class FormatDiagnosticTests
{
    // ── DiagnosticSeverity ────────────────────────────────────────────────

    [Fact]
    public void DiagnosticSeverity_Values()
    {
        Assert.Equal(0, (int)DiagnosticSeverity.Info);
        Assert.Equal(1, (int)DiagnosticSeverity.Warning);
        Assert.Equal(2, (int)DiagnosticSeverity.Error);
    }

    // ── FormatDiagnostic ──────────────────────────────────────────────────

    [Fact]
    public void FormatDiagnostic_Defaults()
    {
        var d = new FormatDiagnostic();
        Assert.Equal(DiagnosticSeverity.Info, d.Severity);
        Assert.Equal(string.Empty, d.Message);
        Assert.Equal(-1, d.Offset);
        Assert.Equal(-1, d.Line);
    }

    [Fact]
    public void FormatDiagnostic_Mutations()
    {
        var d = new FormatDiagnostic
        {
            Severity = DiagnosticSeverity.Error,
            Message = "Unexpected token",
            Offset = 42,
            Line = 3
        };
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Equal("Unexpected token", d.Message);
        Assert.Equal(42, d.Offset);
        Assert.Equal(3, d.Line);
    }

    [Fact]
    public void FormatDiagnostic_Warning()
    {
        var d = new FormatDiagnostic { Severity = DiagnosticSeverity.Warning, Message = "Hint" };
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Equal("Hint", d.Message);
    }

    // ── FormatResult ──────────────────────────────────────────────────────

    [Fact]
    public void FormatResult_Defaults()
    {
        var r = new FormatResult();
        Assert.False(r.Success);
        Assert.Equal(string.Empty, r.FormattedText);
        Assert.False(r.WasModified);
        Assert.False(r.ValidationPassed);
        Assert.Empty(r.Diagnostics);
        Assert.Equal(0L, r.ElapsedMs);
    }

    [Fact]
    public void FormatResult_Mutations()
    {
        var r = new FormatResult
        {
            Success = true,
            FormattedText = "SELECT 1",
            WasModified = true,
            ValidationPassed = true,
            Diagnostics = [new FormatDiagnostic { Message = "Ok" }],
            ElapsedMs = 15
        };
        Assert.True(r.Success);
        Assert.Equal("SELECT 1", r.FormattedText);
        Assert.True(r.WasModified);
        Assert.True(r.ValidationPassed);
        Assert.Single(r.Diagnostics);
        Assert.Equal(15L, r.ElapsedMs);
    }
}
