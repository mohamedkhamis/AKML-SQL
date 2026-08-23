using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Analysis;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Per-line aggregation behind the warning glyph margin: engine issues (1-based lines) are
    /// grouped to one glyph per 0-based snapshot line, stale out-of-snapshot lines are dropped
    /// (same policy as DiagnosticTagger), and the strongest severity wins the glyph colour.
    /// </summary>
    public class WarningGlyphLineIndexTests
    {
        private static CodeIssueInfo Issue(int line, int severity, string rule = "PE002") =>
            new() { Line = line, Severity = severity, RuleId = rule };

        [Fact]
        public void Groups_issues_by_zero_based_line()
        {
            var byLine = WarningGlyphLineIndex.GroupByLine(
                new[] { Issue(1, 2), Issue(3, 1), Issue(3, 3) }, snapshotLineCount: 10);

            Assert.Equal(2, byLine.Count);
            Assert.Single(byLine[0]);
            Assert.Equal(2, byLine[2].Count);
        }

        [Fact]
        public void Drops_issues_beyond_the_snapshot()
        {
            var byLine = WarningGlyphLineIndex.GroupByLine(
                new[] { Issue(1, 2), Issue(99, 2), Issue(0, 2) }, snapshotLineCount: 5);

            Assert.Single(byLine);
            Assert.True(byLine.ContainsKey(0));
        }

        [Fact]
        public void Strongest_severity_wins_the_glyph()
        {
            var issues = new[] { Issue(7, 1), Issue(7, 3), Issue(7, 2) };

            Assert.Equal(3, WarningGlyphLineIndex.MaxSeverity(issues));
        }

        [Fact]
        public void Empty_issue_set_produces_no_lines()
        {
            Assert.Empty(WarningGlyphLineIndex.GroupByLine(
                System.Array.Empty<CodeIssueInfo>(), snapshotLineCount: 5));
        }
    }
}
