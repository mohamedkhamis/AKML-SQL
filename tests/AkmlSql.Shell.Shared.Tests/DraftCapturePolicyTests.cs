#nullable enable
using System.Linq;
using AkmlSql.Shell.Shared.History;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T120, HIS-13/HIS-14) — which query text History captures without a run: a query
    /// tab's text that was never run (a draft), cut to the size limit, and the dirty query tabs the
    /// autosave snapshots.
    /// </summary>
    public sealed class DraftCapturePolicyTests
    {
        [Theory]
        [InlineData(@"C:\Queries\Monthly.sql", "SELECT 1", false, true)]
        [InlineData("SQLQuery3.sql", "SELECT 1", false, true)]
        [InlineData(@"C:\Temp\SQLQuery12", "SELECT 1", false, true)]
        [InlineData(@"C:\Queries\Monthly.sql", "SELECT 1", true, false)]   // has a session: a version, not a draft
        [InlineData(@"C:\Queries\Monthly.sql", "   \r\n ", false, false)]  // nothing to keep
        [InlineData(@"C:\Queries\notes.txt", "SELECT 1", false, false)]    // not a query document
        [InlineData(null, "SELECT 1", false, false)]
        public void Captures_only_unrun_query_text(string? name, string? text, bool hasKey, bool expected)
            => Assert.Equal(expected, DraftCapturePolicy.ShouldCaptureDraft(name, text, hasKey));

        [Fact]
        public void Text_within_the_limit_is_unchanged()
        {
            var text = new string('x', 16 * 1024);
            Assert.Same(text, DraftCapturePolicy.TruncateToLimit(text, 16));
        }

        [Fact]
        public void Longer_text_is_cut_with_a_note()
        {
            var text = new string('x', 20000);

            var cut = DraftCapturePolicy.TruncateToLimit(text, 16);

            Assert.StartsWith(new string('x', 16 * 1024), cut);
            Assert.EndsWith("-- [truncated by AKML SQL: query larger than 16 KB]", cut);
            Assert.Equal(16 * 1024, cut.IndexOf("\r\n-- [truncated", System.StringComparison.Ordinal));
        }

        [Fact]
        public void The_limit_is_clamped_to_16_to_1024_KB()
        {
            var text = new string('x', 20 * 1024);
            Assert.StartsWith(new string('x', 16 * 1024) + "\r\n", DraftCapturePolicy.TruncateToLimit(text, 1));
        }

        [Fact]
        public void Autosave_targets_only_dirty_query_documents()
        {
            var targets = DraftCapturePolicy.SelectAutosaveTargets(new[]
            {
                (@"C:\Queries\a.sql", true),
                (@"C:\Queries\b.sql", false),
                ("SQLQuery4.sql", true),
                (@"C:\Queries\readme.md", true),
            });

            Assert.Equal(new[] { @"C:\Queries\a.sql", "SQLQuery4.sql" }, targets.ToArray());
        }
    }
}
