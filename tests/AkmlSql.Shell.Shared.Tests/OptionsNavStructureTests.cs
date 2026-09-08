using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Dialogs;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Pins the Options navigation grouping to SQL Prompt's layout (report §4 rec #2). SQL Prompt
    /// nests <b>Aliases</b> under <b>Inserted code</b> and <b>Join conditions</b> under
    /// <b>Suggestions</b>; AKML historically had them swapped. These tests assert each leaf sits
    /// under the correct parent group so the grouping can't silently regress.
    /// </summary>
    public class OptionsNavStructureTests
    {
        [StaFact]
        public void Aliases_IsUnder_InsertedCode()
            => AssertLeafParent(pageKey: "Aliases", expectedGroupHeader: "Inserted Code");

        [StaFact]
        public void JoinCompletion_IsUnder_Suggestions()
            => AssertLeafParent(pageKey: "JoinOptions", expectedGroupHeader: "Suggestions");

        /// <summary>
        /// Spec 037 (US1, FR-017, T019): building the window with an initial page key selects
        /// that page's nav leaf and shows the page — the chat card's Add AI agent button must
        /// land the user on the AI Assistance page with no navigation of their own.
        /// </summary>
        [StaFact]
        public void Initial_page_key_selects_the_ai_assistance_leaf()
        {
            var dialog = new SettingsWindow(new AppSettings { Theme = "Light" });
            var window = dialog.TestBuildWindowForRenderTest("AI Assistance");

            var tree = FindTreeView(window);
            Assert.NotNull(tree);

            TreeViewItem? aiLeaf = null;
            foreach (var obj in tree!.Items)
            {
                if (obj is TreeViewItem item && (item.Tag as string) == "AI Assistance")
                {
                    aiLeaf = item;
                    break;
                }
            }

            Assert.NotNull(aiLeaf);
            Assert.True(aiLeaf!.IsSelected, "the deep-linked AI Assistance leaf must be selected");

            // The selected page is the one shown: the content host now holds the AI page's rows.
            Assert.Contains(FindTextBlocks(window), t => t.Text == "AI Provider");
        }

        private static void AssertLeafParent(string pageKey, string expectedGroupHeader)
        {
            var dialog = new SettingsWindow(new AppSettings { Theme = "Light" });
            var window = dialog.TestBuildWindowForRenderTest();

            var tree = FindTreeView(window);
            Assert.NotNull(tree);

            string? parentHeader = null;
            foreach (var obj in tree!.Items)
            {
                if (obj is not TreeViewItem group) continue;
                foreach (var childObj in group.Items)
                {
                    if (childObj is TreeViewItem child && (child.Tag as string) == pageKey)
                    {
                        parentHeader = group.Header?.ToString();
                        break;
                    }
                }
                if (parentHeader != null) break;
            }

            Assert.Equal(expectedGroupHeader, parentHeader);
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

        private static List<TextBlock> FindTextBlocks(DependencyObject root)
        {
            var found = new List<TextBlock>();
            Walk(root, found);
            return found;
        }

        private static void Walk(DependencyObject node, List<TextBlock> found)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(node))
            {
                if (child is TextBlock tb)
                    found.Add(tb);
                if (child is DependencyObject d)
                    Walk(d, found);
            }
        }
    }
}
