using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Dialogs;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Regression test for the wheel-dead Options nav. The sidebar TreeView used to be wrapped in
    /// a second ScrollViewer: the wrapper gave the tree unbounded height, so the tree's OWN
    /// template ScrollViewer never scrolled — yet WPF's inner ScrollViewer swallows mouse-wheel
    /// events even when it cannot scroll. Once every nav group became permanently expanded the
    /// sidebar overflowed and wheel scrolling went dead. The tree must scroll itself.
    /// </summary>
    public class OptionsNavScrollTests
    {
        [StaFact]
        public void NavTree_ScrollsItself_WhenSidebarOverflows()
        {
            var settings = new AppSettings { Theme = "Light" };
            var dialog = new SettingsWindow(settings);
            var window = dialog.TestBuildWindowForRenderTest();

            // Constrain the window so the fully-expanded nav cannot fit vertically.
            window.Width = 900;
            window.Height = 400;
            window.ShowInTaskbar = false;
            window.ShowActivated = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -10_000; // off-screen
            window.Top = -10_000;
            try
            {
                window.Show();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);

                var navTree = LogicalTree.Descendants<TreeView>(window).First();

                // The tree must NOT be wrapped in an outer ScrollViewer — nesting is what made
                // the wheel dead (the inner ScrollViewer handles wheel events it cannot act on).
                Assert.False(navTree.Parent is ScrollViewer,
                    "nav TreeView must not be nested inside another ScrollViewer");

                // And the tree's OWN template ScrollViewer must be the one doing the scrolling:
                // with the height constrained it has genuine scrollable extent.
                var innerScroller = VisualDescendants<ScrollViewer>(navTree).FirstOrDefault();
                Assert.NotNull(innerScroller);
                Assert.True(innerScroller!.ScrollableHeight > 0,
                    $"expected the nav's internal ScrollViewer to overflow (ScrollableHeight was {innerScroller.ScrollableHeight})");
            }
            finally
            {
                window.Close();
            }
        }

        private static System.Collections.Generic.IEnumerable<T> VisualDescendants<T>(DependencyObject root)
            where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) yield return match;
                foreach (var nested in VisualDescendants<T>(child))
                    yield return nested;
            }
        }
    }
}
