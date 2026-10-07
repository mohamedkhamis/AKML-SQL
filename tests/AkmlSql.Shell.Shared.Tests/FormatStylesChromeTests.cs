#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AkmlSql.Shell.Shared.Formatting;
using AkmlSql.Shell.Shared.Ui.SqlPreview;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// The Format Styles window's scroll viewers: inside SSMS the shell themes scroll bars to the
    /// IDE through resources, which left the live preview with light stock bars in AKML's dark
    /// theme. The window's ScrollViewer template sets its own bar style directly, and must still
    /// scroll like the stock one.
    /// </summary>
    [Collection("AkmlSql ThemeRegistry")]
    public class FormatStylesChromeTests
    {
        private static (ScrollViewer Viewer, ScrollBar Horizontal, ScrollBar Vertical) Viewer()
        {
            var viewer = new ScrollViewer
            {
                Style = FormatStylesChrome.ScrollViewerStyle,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Border { Width = 2000, Height = 1500 },
            };
            viewer.Measure(new Size(400, 300));
            viewer.Arrange(new Rect(0, 0, 400, 300));
            viewer.UpdateLayout();
            var horizontal = (ScrollBar)viewer.Template.FindName("PART_HorizontalScrollBar", viewer);
            var vertical = (ScrollBar)viewer.Template.FindName("PART_VerticalScrollBar", viewer);
            return (viewer, horizontal, vertical);
        }

        [StaFact]
        public void The_bars_carry_the_slim_style_directly()
        {
            var (_, horizontal, vertical) = Viewer();

            // Set by the template on the bar itself, which outranks any implicit style a resource
            // the host adds could supply.
            foreach (var bar in new[] { horizontal, vertical })
            {
                Assert.Same(FormatStylesChrome.ScrollBarStyle, bar.Style);
                Assert.Equal(BaseValueSource.ParentTemplate,
                    DependencyPropertyHelper.GetValueSource(bar, FrameworkElement.StyleProperty).BaseValueSource);
            }
            Assert.Equal(Visibility.Visible, horizontal.Visibility);
            Assert.Equal(Visibility.Visible, vertical.Visibility);
        }

        [StaFact]
        public void It_scrolls_like_the_stock_viewer()
        {
            var (viewer, horizontal, vertical) = Viewer();

            viewer.ScrollToHorizontalOffset(300);
            viewer.ScrollToVerticalOffset(200);
            viewer.UpdateLayout();
            Assert.Equal(300, viewer.HorizontalOffset);
            Assert.Equal(300, horizontal.Value);   // the bar follows the viewer
            Assert.Equal(200, vertical.Value);

            // Paging on a bar goes back to the viewer through the scroll commands.
            ScrollBar.PageRightCommand.Execute(null, horizontal);
            ScrollBar.PageDownCommand.Execute(null, vertical);
            viewer.UpdateLayout();
            Assert.True(viewer.HorizontalOffset > 300, $"page right left the offset at {viewer.HorizontalOffset}");
            Assert.True(viewer.VerticalOffset > 200, $"page down left the offset at {viewer.VerticalOffset}");
        }

        [StaFact]
        public void The_live_preview_uses_it()
        {
            var preview = new SqlPreviewView();
            preview.Scroller.Style = FormatStylesChrome.ScrollViewerStyle; // as the window does
            preview.Text = "SELECT 'a very long line that runs well past the right edge of a narrow preview' AS Note;";
            preview.Measure(new Size(200, 120));
            preview.Arrange(new Rect(0, 0, 200, 120));
            preview.UpdateLayout();

            var horizontal = (ScrollBar)preview.Scroller.Template.FindName("PART_HorizontalScrollBar", preview.Scroller);
            Assert.Same(FormatStylesChrome.ScrollBarStyle, horizontal.Style);
            Assert.Equal(Visibility.Visible, horizontal.Visibility);
        }
    }
}
