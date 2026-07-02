using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Dialogs;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Drives the fourth Options-parity batch (report §4 recs #5/#6 + file 08 §2.1):
    /// (a) a consolidated "Connections &amp; Memory" pane owning the SQL-auth credential settings
    /// (previously on Suggestions › Behavior) and the schema-cache storage/memory knobs
    /// (previously on Suggestions › Database) — mirroring SQL Prompt's "Connections &amp; memory";
    /// (b) a clickable "?" help affordance on every page header, with the help block collapsed
    /// until requested (vs the old always-visible paragraph);
    /// (c) the "Show the object definition box" toggle (Suggestions › Tooltips) that SQL Prompt
    /// exposes and AKML previously hard-wired on.
    /// </summary>
    public class OptionsConnectionsHelpTests
    {
        [StaFact]
        public void ConnectionsMemoryPage_IsRegistered_AndOwnsItsRelocatedSettings()
        {
            var settings = new AppSettings();
            settings.IntelliSense.EnableSqlAuthCredentials = false; // default true
            settings.Cache.MaxDatabases = 17;                        // default 10
            settings.Cache.PersistToDisk = false;                    // flip whatever default is
            var expectedPersist = settings.Cache.PersistToDisk;

            var dialog = new SettingsWindow(settings);
            _ = dialog.TestBuildWindowForRenderTest();

            var controls = GetPrivateDictionary(dialog, "_pageControlsByKey");
            Assert.True(controls.Contains("ConnectionsMemory"),
                "Expected a consolidated 'ConnectionsMemory' page in _pageControlsByKey.");

            // The relocated rows must be attributed to the new page in the search index
            // (discriminates against a vacuous value round-trip with no controls).
            Assert.Equal("ConnectionsMemory", PageKeyForSearchLabel(dialog, "Use SQL Server-auth credentials for IntelliSense"));
            Assert.Equal("ConnectionsMemory", PageKeyForSearchLabel(dialog, "Max cached databases"));
            Assert.Equal("ConnectionsMemory", PageKeyForSearchLabel(dialog, "Persist cache to disk"));

            var saved = dialog.GetSettings();
            Assert.False(saved.IntelliSense.EnableSqlAuthCredentials);
            Assert.Equal(17, saved.Cache.MaxDatabases);
            Assert.Equal(expectedPersist, saved.Cache.PersistToDisk);
        }

        [StaFact]
        public void EveryPageHeader_HasHelpButton_AndHelpBlockIsCollapsedByDefault()
        {
            var dialog = new SettingsWindow(new AppSettings { Theme = "Light" });
            var window = dialog.TestBuildWindowForRenderTest();

            var treeView = FindTreeView(window);
            Assert.NotNull(treeView);

            var leaves = new List<TreeViewItem>();
            CollectLeafTreeViewItems(treeView!, leaves);
            Assert.True(leaves.Count > 0);

            foreach (var leaf in leaves)
            {
                leaf.IsSelected = true;
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    () => { }, System.Windows.Threading.DispatcherPriority.Render);

                var pageKey = leaf.Tag as string ?? leaf.Header?.ToString() ?? "(unknown)";

                // (a) A "?" help affordance exists AND is a real, keyboard-reachable Button
                // (PR #248 review finding #8: a mouse-only Border regressed FR-044 help access
                // for keyboard and screen-reader users).
                Button? helpButton = null;
                foreach (var btn in EnumerateElements<Button>(window))
                {
                    if (btn.Content is TextBlock tb && tb.Text == "?") { helpButton = btn; break; }
                }
                Assert.True(helpButton != null, $"Page '{pageKey}' has no '?' help Button in its header.");
                Assert.True(helpButton!.Focusable, $"Page '{pageKey}' help button is not keyboard-focusable.");
                Assert.False(string.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetName(helpButton)),
                    $"Page '{pageKey}' help button has no AutomationProperties.Name for assistive tech.");

                // (b) The help block itself starts collapsed (shown on demand via the "?").
                var helpBlock = FindByName(window, "PageHelpBlock");
                if (helpBlock != null) // pages with empty Help render no block at all
                {
                    Assert.Equal(Visibility.Collapsed, helpBlock.Visibility);
                }
            }
        }

        [StaFact]
        public void ObjectDefinitionBoxToggle_RoundTrips_OnTooltipsPage()
        {
            var settings = new AppSettings();
            settings.CompletionPolish.ShowObjectDefinitionBox = false; // default true

            var dialog = new SettingsWindow(settings);
            _ = dialog.TestBuildWindowForRenderTest();

            Assert.Equal("CompletionPolish", PageKeyForSearchLabel(dialog, "Show the object definition box"));
            Assert.False(dialog.GetSettings().CompletionPolish.ShowObjectDefinitionBox);
        }

        // ─── helpers (mirrors WindowChromeTests / SpecialCharactersPageTests) ───

        private static IDictionary GetPrivateDictionary(SettingsWindow dialog, string field)
        {
            var f = typeof(SettingsWindow).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(f);
            return (IDictionary)f!.GetValue(dialog)!;
        }

        private static string? PageKeyForSearchLabel(SettingsWindow dialog, string label)
        {
            var f = typeof(SettingsWindow).GetField("_searchIndex", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(f);
            var index = (IEnumerable)f!.GetValue(dialog)!;

            foreach (var entry in index)
            {
                var t = entry.GetType();
                var entryLabel = (string)t.GetProperty("Label")!.GetValue(entry)!;
                if (entryLabel == label)
                    return (string)t.GetProperty("PageKey")!.GetValue(entry)!;
            }
            return null;
        }

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

        private static void CollectLeafTreeViewItems(DependencyObject root, List<TreeViewItem> sink)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is TreeViewItem tvi)
                {
                    if (tvi.Items.Count == 0) sink.Add(tvi);
                    else CollectLeafTreeViewItems(tvi, sink);
                }
                else if (child is DependencyObject dep)
                {
                    CollectLeafTreeViewItems(dep, sink);
                }
            }
        }

        private static IEnumerable<T> EnumerateElements<T>(DependencyObject root) where T : DependencyObject
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is T match) yield return match;
                if (child is DependencyObject dep)
                {
                    foreach (var nested in EnumerateElements<T>(dep))
                        yield return nested;
                }
            }
        }

        private static FrameworkElement? FindByName(DependencyObject root, string name)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is FrameworkElement fe && fe.Name == name) return fe;
                if (child is DependencyObject dep)
                {
                    var found = FindByName(dep, name);
                    if (found != null) return found;
                }
            }
            return null;
        }
    }
}
