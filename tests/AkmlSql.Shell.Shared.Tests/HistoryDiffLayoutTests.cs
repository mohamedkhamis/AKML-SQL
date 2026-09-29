#nullable enable
using AkmlSql.Shell.Shared.History;
using AkmlSql.Shell.Shared.Ui.Theme;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T138, HIS-10) — the compare window's layout: both sides aligned line by line with
    /// blank filler lines, each side numbered by its own lines, and added, removed and changed lines
    /// tinted; the sides are headed by name and time.
    /// </summary>
    public sealed class HistoryDiffLayoutTests
    {
        [Fact]
        public void Sides_are_aligned_with_filler_lines_and_tinted()
        {
            var result = HistoryDiffLayout.Build("SELECT a\nFROM t\nWHERE x = 1", "SELECT a\nFROM t2\nWHERE x = 1\nORDER BY a");

            Assert.Equal(new int?[] { 1, 2, 3, null }, result.Left.Numbers);
            Assert.Equal(new int?[] { 1, 2, 3, 4 }, result.Right.Numbers);
            Assert.Equal("SELECT a\nFROM t\nWHERE x = 1\n", result.Left.Text);

            Assert.Equal(ThemeTokens.StatusWarning, result.Left.Tints[1]);
            Assert.Equal(ThemeTokens.StatusWarning, result.Right.Tints[1]);
            Assert.Equal(ThemeTokens.StatusSuccess, result.Right.Tints[3]);
            Assert.False(result.Left.Tints.ContainsKey(3)); // a filler line is not tinted
            Assert.Equal("1 changed · 1 added", result.Summary);
        }

        [Fact]
        public void Removed_lines_are_tinted_on_the_left()
        {
            var result = HistoryDiffLayout.Build("a\nb\nc", "a\nc");

            Assert.Equal(new int?[] { 1, 2, 3 }, result.Left.Numbers);
            Assert.Equal(new int?[] { 1, null, 2 }, result.Right.Numbers);
            Assert.Equal(ThemeTokens.StatusDanger, result.Left.Tints[1]);
            Assert.Equal("1 removed", result.Summary);
        }

        [Fact]
        public void Identical_texts_have_no_differences()
            => Assert.Equal("No differences", HistoryDiffLayout.Build("SELECT 1", "SELECT 1").Summary);

        [Fact]
        public void Sides_are_headed_by_name_and_time()
        {
            var local = new System.DateTime(2026, 9, 28, 14, 30, 0, System.DateTimeKind.Local);
            var side = new HistoryCompareSide("Monthly totals", local.ToUniversalTime().ToString("o"), "SELECT 1");

            Assert.Equal("Monthly totals — " + HistoryTimeFormat.Absolute(local), side.Header);
            Assert.Equal("q1", new HistoryCompareSide("q1", null, "").Header);
        }
    }
}
