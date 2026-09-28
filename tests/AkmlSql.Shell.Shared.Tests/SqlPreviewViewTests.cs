#nullable enable
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AkmlSql.Shell.Shared.Ui.SqlPreview;
using AkmlSql.Shell.Shared.Ui.Theme;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T004, FR-010/FR-021/FR-033/FR-044) — the shared read-only SQL preview: tab stops at
    /// the style's width, token colours from live theme tokens, search and line highlights, a
    /// truncation notice for huge text, and selectable (not editable) text.
    /// </summary>
    [Collection("AkmlSql ThemeRegistry")]
    public class SqlPreviewViewTests
    {
        [Theory]
        [InlineData("ab\tc", 4, "ab  c")]
        [InlineData("\tx", 2, "  x")]
        [InlineData("abcd\tx", 4, "abcd    x")]
        [InlineData("no tabs", 4, "no tabs")]
        public void ExpandTabs_advances_to_the_next_tab_stop(string input, int size, string expected)
            => Assert.Equal(expected, SqlPreviewView.ExpandTabs(input, size));

        [Fact]
        public void ExpandTabs_restarts_the_tab_stops_on_every_line()
            => Assert.Equal("ab  c\r\n    d\nx   y", SqlPreviewView.ExpandTabs("ab\tc\r\n\td\nx\ty", 4));

        [StaFact]
        public void Tokens_are_coloured_from_the_live_theme_tokens()
        {
            var view = new SqlPreviewView { Text = "SELECT 'x' -- note" };

            AssertForeground(view, "SELECT", ThemeTokens.AccentPrimary);
            AssertForeground(view, "'x'", ThemeTokens.StatusSuccess);
            AssertForeground(view, "-- note", ThemeTokens.TextSecondary);
        }

        [StaFact]
        public void Highlight_terms_get_the_match_background()
        {
            var view = new SqlPreviewView { Text = "SELECT UnitPrice FROM dbo.Products" };
            view.HighlightTerms = new[] { "unitprice" };

            var run = Runs(view).Single(r => r.Text == "UnitPrice");
            Assert.Same(Resource(view, ThemeTokens.HistoryMatchHighlight), run.Background);
            Assert.Null(Runs(view).First(r => r.Text == "SELECT").Background);
        }

        [StaFact]
        public void Highlight_lines_set_that_lines_background()
        {
            var view = new SqlPreviewView { Text = "SELECT 1\nFROM dbo.T\nWHERE 1 = 1" };
            view.HighlightLines = new[] { 1 };

            var lines = view.Lines;
            Assert.Equal(3, lines.Count);
            Assert.Null(lines[0].Background);
            Assert.Same(Resource(view, ThemeTokens.SurfaceSelection), lines[1].Background);
            Assert.Null(lines[2].Background);
        }

        [StaFact]
        public void Text_over_the_limit_ends_with_the_notice_line()
        {
            var view = new SqlPreviewView { MaxDisplayChars = 1024 };
            view.Text = new string('x', 5000);

            var last = view.Lines.Last();
            Assert.Equal("— Showing the first 1 KB. Open the query to see all of it.", TextOf(last));
            Assert.Equal(1024, view.Lines.Take(view.Lines.Count - 1).Sum(p => TextOf(p).Length));
        }

        [StaFact]
        public void Text_is_read_only_but_selectable()
        {
            var view = new SqlPreviewView { Text = "SELECT 1" };
            var box = LogicalTree.Descendants<RichTextBox>(view).Single();

            Assert.True(box.IsReadOnly);
            Assert.True(box.Focusable);
            Assert.Contains(box.ContextMenu!.Items.OfType<MenuItem>(), m => (string)m.Header == "Copy all");
        }

        [StaFact]
        public void Line_numbers_follow_the_line_count_and_can_be_hidden()
        {
            var view = new SqlPreviewView { Text = "a\nb\nc" };
            var gutter = view.Gutter;
            Assert.Equal("1\n2\n3", gutter.Text);
            Assert.Equal(Visibility.Visible, gutter.Visibility);

            view.ShowLineNumbers = false;
            Assert.Equal(Visibility.Collapsed, gutter.Visibility);
        }

        private static void AssertForeground(SqlPreviewView view, string text, string token)
        {
            var run = Runs(view).First(r => r.Text == text);
            Assert.Same(Resource(view, token), run.Foreground);
        }

        private static System.Collections.Generic.IEnumerable<Run> Runs(SqlPreviewView view)
            => view.Lines.SelectMany(p => p.Inlines.OfType<Run>());

        private static string TextOf(Paragraph p) => string.Concat(p.Inlines.OfType<Run>().Select(r => r.Text));

        private static Brush Resource(FrameworkElement view, string key) => (Brush)view.FindResource(key);
    }
}
