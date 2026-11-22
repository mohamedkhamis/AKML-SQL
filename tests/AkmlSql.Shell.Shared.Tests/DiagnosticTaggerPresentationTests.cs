#nullable enable
using AkmlSql.Shell.Shared.Analysis;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 047 (UX-001, SSMS-001, contracts/analysis-display.md §1–2) — with "Advisory findings: Margin marker only"
    /// (<c>codeAnalysis.squiggleStyle = "marker"</c>, the default) an Information or Hint finding, such as a missing
    /// semicolon, gets the margin dot only: no squiggle under the SQL. Warnings and errors are always underlined. Any
    /// other value underlines everything, as before.
    /// </summary>
    public sealed class DiagnosticTaggerPresentationTests
    {
        [Theory]
        [InlineData(0, false)]   // Hint
        [InlineData(1, false)]   // Information
        [InlineData(2, true)]    // Warning
        [InlineData(3, true)]    // Error
        public void Marker_mode_underlines_only_warnings_and_errors(int severity, bool underlined)
        {
            Assert.Equal(underlined, DiagnosticTagger.ShouldTag(severity, "marker"));
        }

        [Theory]
        [InlineData("underline")]
        [InlineData("dotted")]
        [InlineData("solid")]
        [InlineData(null)]
        [InlineData("")]
        public void Every_other_style_underlines_every_severity(string? style)
        {
            for (var severity = 0; severity <= 3; severity++)
                Assert.True(DiagnosticTagger.ShouldTag(severity, style), $"severity {severity}, style '{style}'");
        }
    }
}
