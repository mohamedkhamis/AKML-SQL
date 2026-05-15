using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Dialogs;
using AkmlSql.Shell.Shared.Ui.Theme;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    public class WindowChromeTests
    {
        /// <summary>
        /// Regression test for the light-theme submenu invisibility bug (Task 1 fix):
        /// every nested TreeViewItem (child of a group header) must have the themed style
        /// applied — specifically, an explicit ForegroundProperty setter with a non-white value.
        ///
        /// Root cause: TreeView.ItemContainerStyle only styles direct children; nested items
        /// fall back to the system ControlTextBrushKey which was overridden to white, making
        /// them invisible on the white sidebar. The fix uses TreeView.Resources[typeof(TreeViewItem)]
        /// which cascades to all depths.
        ///
        /// This test discriminates: with ItemContainerStyle (bug), nested items have Style==null,
        /// so GetForegroundFromStyle returns null and the Assert.NotNull fails. With
        /// Resources[typeof(TreeViewItem)] (fix), every item at every depth has Style set.
        /// </summary>
        [StaFact]
        public void TreeViewItems_AllVisibleInLightTheme()
        {
            // Arrange
            var settings = new AppSettings { Theme = "Light" };
            var dialog = new SettingsWindow(settings);
            var window = dialog.TestBuildWindowForRenderTest();

            // Pump the dispatcher so styles are applied
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
            window.UpdateLayout();

            // The sidebar background in light theme: SurfaceSidebar = #FFFFFF
            var sidebarBg = ((SolidColorBrush)ThemePalette.Light.Brushes[ThemeTokens.SurfaceSidebar]).Color;

            // Collect all TreeViewItems via the logical tree (always populated, no visual tree needed).
            var treeView = FindTreeView(window);
            Assert.NotNull(treeView);

            // Only check NESTED items (children of parent groups): these are the ones that were
            // invisible before the fix. Top-level items got the style via ItemContainerStyle;
            // nested items did not.
            var nestedItems = new List<TreeViewItem>();
            foreach (var obj in treeView!.Items)
            {
                if (obj is TreeViewItem parent)
                {
                    foreach (var childObj in parent.Items)
                    {
                        if (childObj is TreeViewItem child)
                            nestedItems.Add(child);
                    }
                }
            }

            Assert.True(nestedItems.Count > 0, "Expected to find nested TreeViewItems (children of group headers)");

            // Every nested item's Style must be set (non-null) and contain an explicit
            // ForegroundProperty setter with a non-sidebar color. With ItemContainerStyle
            // (the bug), nested items have Style==null and GetForegroundFromStyle returns null.
            foreach (var item in nestedItems)
            {
                var styledFg = GetForegroundFromStyle(item);
                Assert.NotNull(styledFg); // Fails if Style is null (ItemContainerStyle bug)
                Assert.NotEqual(sidebarBg, styledFg!.Color);
            }
        }

        /// <summary>
        /// Dark-theme variant of TreeViewItems_AllVisibleInLightTheme.
        /// Verifies the same Resources[typeof(TreeViewItem)] fix applies when SettingsWindow
        /// is constructed with Theme = "Dark". Nested items must have the themed style
        /// with a Foreground color that differs from the dark sidebar background.
        /// </summary>
        [StaFact]
        public void TreeViewItems_AllVisibleInDarkTheme()
        {
            // Arrange — settings drives dark palette selection directly
            var settings = new AppSettings { Theme = "Dark" };
            var dialog = new SettingsWindow(settings);
            var window = dialog.TestBuildWindowForRenderTest();

            // Pump the dispatcher so styles are applied
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
            window.UpdateLayout();

            // The sidebar background in dark theme: SurfaceSidebar = #1E1E2E
            var sidebarBg = ((SolidColorBrush)ThemePalette.Dark.Brushes[ThemeTokens.SurfaceSidebar]).Color;

            // Collect all TreeViewItems via the logical tree (always populated, no visual tree needed).
            var treeView = FindTreeView(window);
            Assert.NotNull(treeView);

            // Only check NESTED items (children of parent groups): these are the ones that were
            // invisible before the fix. Top-level items got the style via ItemContainerStyle;
            // nested items did not.
            var nestedItems = new List<TreeViewItem>();
            foreach (var obj in treeView!.Items)
            {
                if (obj is TreeViewItem parent)
                {
                    foreach (var childObj in parent.Items)
                    {
                        if (childObj is TreeViewItem child)
                            nestedItems.Add(child);
                    }
                }
            }

            Assert.True(nestedItems.Count > 0, "Expected to find nested TreeViewItems (children of group headers)");

            // Every nested item's Style must be set (non-null) and contain an explicit
            // ForegroundProperty setter with a non-sidebar color. With ItemContainerStyle
            // (the bug), nested items have Style==null and GetForegroundFromStyle returns null.
            foreach (var item in nestedItems)
            {
                var styledFg = GetForegroundFromStyle(item);
                Assert.NotNull(styledFg); // Fails if Style is null (ItemContainerStyle bug)
                Assert.NotEqual(sidebarBg, styledFg!.Color);
            }
        }

        /// <summary>
        /// Verifies that every leaf page in the settings navigation tree has a "Restore Defaults"
        /// link in its page header. Each leaf TreeViewItem (Items.Count == 0) is selected in
        /// sequence; after selection the content panel must contain at least one TextBlock with
        /// Text == "Restore Defaults".
        ///
        /// All 15 pages are built eagerly in BuildPages() and stored in _pages[key], so no
        /// lazy rendering is needed — selecting a leaf sets _contentHost.Content synchronously.
        /// </summary>
        [StaFact]
        public void PageHeader_HasRestoreLink_ForEveryPage()
        {
            // Arrange — light theme is fine; this test is theme-independent
            var settings = new AppSettings { Theme = "Light" };
            var dialog = new SettingsWindow(settings);
            var window = dialog.TestBuildWindowForRenderTest();

            // Find the navigation TreeView
            var treeView = FindTreeView(window);
            Assert.NotNull(treeView);

            // Collect all leaf TreeViewItems (items with no children, i.e., actual pages)
            var leafItems = new List<TreeViewItem>();
            CollectLeafTreeViewItems(treeView!, leafItems);

            // Phase 1 has exactly 15 leaves (Behavior, Database, Styles, Productivity, Navigation,
            // Refactoring, History, Execution Warnings, Query Results, Execution, Color,
            // Code Analysis, Snippets, AI Assistance, Main).
            Assert.True(leafItems.Count >= 14,
                $"Expected at least 14 leaf pages, found {leafItems.Count}");

            // For each leaf: select it (synchronous), pump dispatcher, assert "Restore Defaults" exists
            foreach (var leaf in leafItems)
            {
                leaf.IsSelected = true;

                // Belt-and-suspenders: pump to Render priority so any pending layout work completes
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);

                // Walk the logical tree looking for the "Restore Defaults" TextBlock
                bool found = false;
                foreach (var tb in EnumerateTextBlocks(window))
                {
                    if (tb.Text == "Restore Defaults")
                    {
                        found = true;
                        break;
                    }
                }

                var pageKey = leaf.Tag as string ?? leaf.Header?.ToString() ?? "(unknown)";
                Assert.True(found,
                    $"Page '{pageKey}' (header='{leaf.Header}') does not contain a 'Restore Defaults' TextBlock.");
            }
        }

        /// <summary>
        /// Phase 2 C.6 regression test: every page key registered in
        /// <c>_pageBuilders</c> must have a matching case in
        /// <c>ResetPageToDefaultsCore</c>. The Phase 1 final reviewer flagged
        /// that the OnResetThisPageClick switch coverage is brittle when new
        /// pages are added — a missing case used to silently no-op. This test
        /// reflects on <c>_pageBuilders.Keys</c>, calls
        /// <c>ResetPageToDefaultsCore(key)</c> for each, and asserts no throw.
        /// The default branch in <c>ResetPageToDefaultsCore</c> throws
        /// <c>InvalidOperationException</c> for unhandled keys, so a missing
        /// case fails the test loudly.
        /// </summary>
        [Fact]
        public void ResetPageToDefaultsCore_HasCaseForEveryRegisteredPageKey()
        {
            var settings = new AppSettings();
            var dialog = new SettingsWindow(settings);

            var pageBuildersField = typeof(SettingsWindow).GetField(
                "_pageBuilders",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(pageBuildersField);
            var pageBuilders = (IDictionary)pageBuildersField!.GetValue(dialog)!;
            Assert.True(pageBuilders.Count > 0, "Expected at least one registered IPageBuilder");

            var resetMethod = typeof(SettingsWindow).GetMethod(
                "ResetPageToDefaultsCore",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(resetMethod);

            foreach (DictionaryEntry entry in pageBuilders)
            {
                var key = (string)entry.Key;
                try
                {
                    resetMethod!.Invoke(dialog, new object[] { key });
                }
                catch (TargetInvocationException tie)
                    when (tie.InnerException is System.InvalidOperationException ioe)
                {
                    Assert.Fail(
                        $"ResetPageToDefaultsCore is missing a case for page key '{key}'. " +
                        $"When adding a page to _pageBuilders, also add a case to " +
                        $"ResetPageToDefaultsCore. Inner: {ioe.Message}");
                }
            }
        }

        /// <summary>
        /// Companion to <see cref="ResetPageToDefaultsCore_HasCaseForEveryRegisteredPageKey"/>
        /// — guards against the Phase 2 Block C bug where 5 newly-registered
        /// pages (SuggestionTypes / Qualification / InsertOptions / JoinOptions
        /// / Labs) had entries in <c>_pageBuilders</c> but were absent from
        /// the hardcoded dispatch lists in <c>LoadSettingsToControls</c> /
        /// <c>SaveControlsToSettings</c>. Their controls rendered but never
        /// loaded from settings and silently discarded changes on OK.
        ///
        /// After the fix, both methods iterate <c>_pageControlsByKey.Values</c>
        /// directly. This test asserts the postcondition that the two
        /// dictionaries stay 1:1 after BuildPages — every registered IPageBuilder
        /// must produce an IPageControls in <c>_pageControlsByKey</c>. A future
        /// page that fails to land in the controls dict (e.g. a Build override
        /// that returns null or skips registration) is caught here.
        /// </summary>
        [StaFact]
        public void PageControls_RegisteredForEveryPageBuilder()
        {
            var settings = new AppSettings { Theme = "Light" };
            var dialog = new SettingsWindow(settings);
            // BuildWindowInner runs BuildPages which populates _pageControlsByKey.
            _ = dialog.TestBuildWindowForRenderTest();

            var pageBuildersField = typeof(SettingsWindow).GetField(
                "_pageBuilders",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var controlsField = typeof(SettingsWindow).GetField(
                "_pageControlsByKey",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(pageBuildersField);
            Assert.NotNull(controlsField);

            var pageBuilders = (IDictionary)pageBuildersField!.GetValue(dialog)!;
            var pageControls = (IDictionary)controlsField!.GetValue(dialog)!;

            Assert.True(pageBuilders.Count > 0, "Expected at least one registered IPageBuilder");
            Assert.Equal(pageBuilders.Count, pageControls.Count);

            foreach (DictionaryEntry entry in pageBuilders)
            {
                var key = (string)entry.Key;
                Assert.True(
                    pageControls.Contains(key),
                    $"_pageBuilders has '{key}' but _pageControlsByKey does not. " +
                    "Either BuildPages skipped this builder or the IPageBuilder.Build " +
                    "implementation did not register its IPageControls. " +
                    "LoadSettingsToControls and SaveControlsToSettings iterate " +
                    "_pageControlsByKey.Values, so a missing entry causes silent " +
                    "data loss for the page.");
            }
        }

        // ─── Helpers (logical tree) ──────────────────────────────────────────

        /// <summary>
        /// Walks the logical tree to find the first TreeView descendant.
        /// Used by the light/dark theme tests.
        /// </summary>
        private static TreeView? FindTreeView(DependencyObject root)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is TreeView tv) return tv;
                if (child is DependencyObject dep)
                {
                    var found = FindTreeView(dep);
                    if (found != null) return found;
                }
            }
            return null;
        }

        /// <summary>
        /// Recursively collects all TreeViewItems that are leaves (Items.Count == 0).
        /// These correspond to actual pages (Tag != null in SettingsWindow.AddTreeGroup/AddTreeLeaf).
        /// </summary>
        private static void CollectLeafTreeViewItems(DependencyObject root, List<TreeViewItem> sink)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is TreeViewItem tvi)
                {
                    if (tvi.Items.Count == 0)
                        sink.Add(tvi);
                    else
                        CollectLeafTreeViewItems(tvi, sink);
                }
                else if (child is DependencyObject dep)
                {
                    CollectLeafTreeViewItems(dep, sink);
                }
            }
        }

        /// <summary>
        /// Recursively enumerates all TextBlock descendants via the logical tree.
        /// </summary>
        private static IEnumerable<TextBlock> EnumerateTextBlocks(DependencyObject root)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is TextBlock tb)
                    yield return tb;
                if (child is DependencyObject dep)
                {
                    foreach (var nested in EnumerateTextBlocks(dep))
                        yield return nested;
                }
            }
        }

        /// <summary>
        /// Returns the SolidColorBrush from the ForegroundProperty Setter in this item's Style.
        /// Returns null if the item has no Style, or if the Style has no ForegroundProperty setter.
        /// With TreeView.ItemContainerStyle, nested items have Style==null — this returns null.
        /// With TreeView.Resources[typeof(TreeViewItem)], every item has the style — non-null.
        /// </summary>
        private static SolidColorBrush? GetForegroundFromStyle(TreeViewItem item)
        {
            if (item.Style == null) return null;
            foreach (var setter in item.Style.Setters)
            {
                if (setter is Setter s && s.Property == Control.ForegroundProperty
                    && s.Value is SolidColorBrush brush)
                {
                    return brush;
                }
            }
            return null;
        }
    }
}
