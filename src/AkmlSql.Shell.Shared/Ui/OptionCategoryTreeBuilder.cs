using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AkmlSql.Shell.Shared.Ui
{
    /// <summary>
    /// T099: Builds TreeView items for all option categories in the Profile Editor.
    /// Uses programmatic WPF only (no XAML) for Shell.Shared compatibility.
    /// </summary>
    internal static class OptionCategoryTreeBuilder
    {
        /// <summary>
        /// Category grouping structure: top-level categories with optional sub-items.
        /// </summary>
        private static readonly CategoryGroup[] Groups = new[]
        {
            new CategoryGroup("Layout",
                "Whitespace",
                "Lists",
                "Parentheses"),
            new CategoryGroup("Casing",
                "Casing"),
            new CategoryGroup("Statements",
                "DML (SELECT/INSERT/UPDATE/DELETE)",
                "JOINs",
                "DDL (CREATE/ALTER)"),
            new CategoryGroup("Control Flow & Expressions",
                "Control Flow (IF/WHILE/TRY)",
                "CASE Expressions",
                "CTEs (WITH)",
                "Expressions"),
            new CategoryGroup("Actions",
                "Format Actions"),
        };

        /// <summary>
        /// Populates the <see cref="TreeView"/> with category items.
        /// Each top-level group is a <see cref="TreeViewItem"/> with child items for each category.
        /// Groups with a single category are flattened to a single item.
        /// </summary>
        public static void BuildTree(TreeView tree, ProfileEditorViewModel viewModel)
        {
            if (tree == null) throw new ArgumentNullException(nameof(tree));
            if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));

            tree.Items.Clear();

            var theme = ThemeManager.Instance;
            var foreground = new SolidColorBrush(theme.Foreground);

            foreach (var group in Groups)
            {
                if (group.Categories.Length == 1)
                {
                    // Single-category group: show as flat item
                    var item = CreateCategoryItem(group.Categories[0], foreground);
                    tree.Items.Add(item);
                }
                else
                {
                    // Multi-category group
                    var groupItem = new TreeViewItem
                    {
                        Header = CreateGroupHeader(group.Name, foreground),
                        IsExpanded = true,
                        Foreground = foreground,
                    };

                    foreach (var cat in group.Categories)
                    {
                        var childItem = CreateCategoryItem(cat, foreground);
                        groupItem.Items.Add(childItem);
                    }

                    tree.Items.Add(groupItem);
                }
            }

            // Select the first leaf item
            SelectFirstLeaf(tree);
        }

        /// <summary>
        /// Filters the tree view items based on a search string. Items not matching are collapsed.
        /// </summary>
        public static void FilterTree(TreeView tree, ProfileEditorViewModel viewModel, string filter)
        {
            if (tree == null) return;

            if (string.IsNullOrWhiteSpace(filter))
            {
                // Show all
                SetAllVisibility(tree.Items, Visibility.Visible);
                return;
            }

            var matchingCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allSearchResults = viewModel.SearchAllOptions(filter);
            foreach (var opt in allSearchResults)
            {
                // Find which category this option belongs to
                foreach (var cat in ProfileEditorViewModel.CategoryNames)
                {
                    var opts = viewModel.GetOptionsForCategory(cat);
                    foreach (var o in opts)
                    {
                        if (string.Equals(o.Key, opt.Key, StringComparison.OrdinalIgnoreCase))
                        {
                            matchingCategories.Add(cat);
                            break;
                        }
                    }
                }
            }

            foreach (var item in tree.Items)
            {
                var treeItem = item as TreeViewItem;
                if (treeItem == null) continue;

                if (treeItem.Items.Count > 0)
                {
                    // Group item — check children
                    bool anyChildVisible = false;
                    foreach (var child in treeItem.Items)
                    {
                        var childItem = child as TreeViewItem;
                        if (childItem == null) continue;

                        var catName = childItem.Tag as string;
                        if (catName != null && matchingCategories.Contains(catName))
                        {
                            childItem.Visibility = Visibility.Visible;
                            anyChildVisible = true;
                        }
                        else
                        {
                            childItem.Visibility = Visibility.Collapsed;
                        }
                    }
                    treeItem.Visibility = anyChildVisible ? Visibility.Visible : Visibility.Collapsed;
                }
                else
                {
                    // Leaf item
                    var catName = treeItem.Tag as string;
                    treeItem.Visibility = (catName != null && matchingCategories.Contains(catName))
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
            }
        }

        /// <summary>
        /// Highlights matching text in a TreeViewItem's header.
        /// </summary>
        public static void HighlightMatch(TreeViewItem item, string searchText)
        {
            // Minimal highlight — bold the item if it matches
            if (item == null) return;

            var textBlock = FindTextBlock(item.Header as FrameworkElement);
            if (textBlock == null) return;

            if (!string.IsNullOrWhiteSpace(searchText) &&
                textBlock.Text.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                textBlock.FontWeight = FontWeights.Bold;
            }
            else
            {
                textBlock.FontWeight = FontWeights.Normal;
            }
        }

        // -------------------------------------------------------------------
        // Private helpers
        // -------------------------------------------------------------------

        private static TreeViewItem CreateCategoryItem(string categoryName, Brush foreground)
        {
            var textBlock = new TextBlock
            {
                Text = categoryName,
                Foreground = foreground,
                Margin = new Thickness(2, 1, 2, 1),
            };

            return new TreeViewItem
            {
                Header = textBlock,
                Tag = categoryName,
                Foreground = foreground,
            };
        }

        private static FrameworkElement CreateGroupHeader(string groupName, Brush foreground)
        {
            return new TextBlock
            {
                Text = groupName,
                FontWeight = FontWeights.SemiBold,
                Foreground = foreground,
                Margin = new Thickness(2, 2, 2, 2),
            };
        }

        private static void SelectFirstLeaf(TreeView tree)
        {
            foreach (var item in tree.Items)
            {
                var treeItem = item as TreeViewItem;
                if (treeItem == null) continue;

                if (treeItem.Items.Count > 0)
                {
                    foreach (var child in treeItem.Items)
                    {
                        var childItem = child as TreeViewItem;
                        if (childItem != null)
                        {
                            childItem.IsSelected = true;
                            return;
                        }
                    }
                }
                else
                {
                    treeItem.IsSelected = true;
                    return;
                }
            }
        }

        private static void SetAllVisibility(ItemCollection items, Visibility visibility)
        {
            foreach (var item in items)
            {
                var treeItem = item as TreeViewItem;
                if (treeItem == null) continue;

                treeItem.Visibility = visibility;
                if (treeItem.Items.Count > 0)
                {
                    SetAllVisibility(treeItem.Items, visibility);
                }
            }
        }

        private static TextBlock FindTextBlock(FrameworkElement element)
        {
            if (element is TextBlock tb)
                return tb;
            return null;
        }

        // -------------------------------------------------------------------
        // Nested types
        // -------------------------------------------------------------------

        private sealed class CategoryGroup
        {
            public string Name { get; }
            public string[] Categories { get; }

            public CategoryGroup(string name, params string[] categories)
            {
                Name = name;
                Categories = categories;
            }
        }
    }
}
