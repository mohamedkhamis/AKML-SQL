using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Dialogs;
using AkmlSql.Shell.Shared.Dialogs.Pages;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T149, OPT-04, FR-050) took SQL Prompt's arrangement; its one-to-three-setting pages
    /// are now sections of combined pages (Pages/CombinedPages.cs) — twelve pages: group headers,
    /// leaf labels, their order and the page keys. Each page's breadcrumb
    /// (<c>IPageBuilder.Display</c>) is its tree path, and its Title the last segment.
    /// </summary>
    public class OptionsNavStructureTests
    {
        /// <summary>The tree, top to bottom: (group or null for a top-level leaf, label, page key).</summary>
        private static readonly (string? Group, string Label, string PageKey)[] Expected =
        {
            (null, "General", "General"),
            ("Suggestions", "Behavior", "SuggestionsBehavior"),
            ("Suggestions", "Lists & connections", "SuggestionsLists"),
            ("Suggestions", "Warnings & highlighting", "Safety"),
            (null, "Inserted code", "InsertedCode"),
            (null, "Format", "Formatting"),
            ("Queries", "Results & execution", "ResultsExecution"),
            ("Queries", "History", "History"),
            ("Queries", "Color", "Tabs & UI"),
            (null, "Editor", "EditorAll"),
            (null, "Code analysis", "Code Analysis"),
            (null, "AI assistance", "AI Assistance"),
        };

        [StaFact]
        public void The_tree_matches_the_contract_exactly()
        {
            var tree = FindTreeView(new SettingsWindow(new AppSettings { Theme = "Light" }).TestBuildWindowForRenderTest());
            Assert.NotNull(tree);

            Assert.Equal(Expected.Select(Describe), Leaves(tree!).Select(Describe));
        }

        [StaFact]
        public void Every_breadcrumb_is_its_tree_path_and_the_title_its_last_segment()
        {
            var dialog = new SettingsWindow(new AppSettings { Theme = "Light" });
            var builders = (Dictionary<string, IPageBuilder>)typeof(SettingsWindow)
                .GetField("_pageBuilders", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;

            foreach (var (group, label, key) in Expected)
            {
                Assert.True(builders.TryGetValue(key, out var page), $"no page builder for '{key}'");
                var path = group == null ? label : group + " › " + label;
                Assert.Equal(path, page!.Display);
                Assert.Equal(label, page.Title);
                Assert.Equal(key, page.Key);
            }
        }

        [StaFact]
        public void Group_headers_and_leaves_are_in_sentence_case()
        {
            foreach (var (group, label, _) in Expected)
                foreach (var text in new[] { group, label }.Where(t => t != null))
                {
                    // Every word after the first is lower case, apart from acronyms (AI) and proper names.
                    var words = text!.Split(' ').Skip(1).Where(w => w.Length > 1 && w != "AI");
                    Assert.All(words, w => Assert.False(char.IsUpper(w[0]), $"'{text}' is not sentence case"));
                }
        }

        /// <summary>
        /// Spec 037 (US1, FR-017, T019): building the window with an initial page key selects
        /// that page's nav leaf and shows the page — the chat card's Add AI agent button must
        /// land the user on the AI assistance page with no navigation of their own.
        /// </summary>
        [StaFact]
        public void Initial_page_key_selects_the_ai_assistance_leaf()
        {
            var dialog = new SettingsWindow(new AppSettings { Theme = "Light" });
            var window = dialog.TestBuildWindowForRenderTest("AI Assistance");

            var tree = FindTreeView(window);
            Assert.NotNull(tree);

            var aiLeaf = tree!.Items.OfType<TreeViewItem>().FirstOrDefault(i => (i.Tag as string) == "AI Assistance");
            Assert.NotNull(aiLeaf);
            Assert.Equal("AI assistance", aiLeaf!.Header);
            Assert.True(aiLeaf.IsSelected, "the deep-linked AI assistance leaf must be selected");

            // The selected page is the one shown: the content host now holds the AI page's rows.
            Assert.Contains(FindTextBlocks(window), t => t.Text == "AI Provider");
        }

        private static string Describe((string? Group, string Label, string PageKey) leaf) =>
            (leaf.Group == null ? "" : leaf.Group + " / ") + leaf.Label + " [" + leaf.PageKey + "]";

        /// <summary>The tree's leaves in display order, with the header of the group each sits in.</summary>
        private static List<(string? Group, string Label, string PageKey)> Leaves(TreeView tree)
        {
            var leaves = new List<(string?, string, string)>();
            foreach (var item in tree.Items.OfType<TreeViewItem>())
            {
                if (item.Tag is string key)
                {
                    leaves.Add((null, item.Header?.ToString() ?? "", key));
                    continue;
                }
                foreach (var child in item.Items.OfType<TreeViewItem>())
                    leaves.Add((item.Header?.ToString(), child.Header?.ToString() ?? "", child.Tag as string ?? ""));
            }
            return leaves;
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
