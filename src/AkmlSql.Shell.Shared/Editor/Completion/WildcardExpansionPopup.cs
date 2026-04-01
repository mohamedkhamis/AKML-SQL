using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AkmlSql.Shell.Shared.Editor.Completion
{
    /// <summary>
    /// SQL Prompt-style checkbox popup for wildcard expansion.
    /// Dark themed, code-only WPF (no XAML). Shows columns grouped by table
    /// with checkboxes for selecting which columns to include in the expansion.
    /// </summary>
    internal sealed class WildcardExpansionPopup : Border
    {
        private readonly StackPanel _root;
        private readonly StackPanel _itemsPanel;
        private readonly TextBlock _footer;
        private bool _isOpen;

        private readonly List<ColumnRow> _columnRows = new List<ColumnRow>();
        private int _selectedIndex = -1;

        private List<TableGroupData> _tableGroups = new List<TableGroupData>();

        private const double PopupWidth = 420;
        private const double ItemHeight = 22;
        private const int MaxVisibleItems = 18;

        // SQL Prompt dark theme colors (same as AkmlCompletionPopup)
        private static readonly SolidColorBrush BgBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)));
        private static readonly SolidColorBrush BorderBrush_ = Freeze(new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)));
        private static readonly SolidColorBrush SelectedBg = Freeze(new SolidColorBrush(Color.FromRgb(0x09, 0x47, 0x71)));
        private static readonly SolidColorBrush TextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4)));
        private static readonly SolidColorBrush DimTextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x6A)));
        private static readonly SolidColorBrush SecondaryBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)));
        private static readonly SolidColorBrush FooterBg = Freeze(new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)));
        private static readonly SolidColorBrush HeaderBg = Freeze(new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)));
        private static readonly SolidColorBrush ColumnBadgeBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF9, 0xA8, 0x25)));
        private static readonly SolidColorBrush CheckMarkBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x53)));

        public WildcardExpansionPopup()
        {
            _itemsPanel = new StackPanel();

            var scrollViewer = new ScrollViewer
            {
                Content = _itemsPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = MaxVisibleItems * ItemHeight,
                Focusable = false,
                Background = BgBrush
            };

            _footer = new TextBlock
            {
                Foreground = SecondaryBrush,
                FontSize = 11,
                Padding = new Thickness(8, 3, 8, 3),
                Background = FooterBg,
                Text = "Space: toggle | Tab/Enter: expand | Esc: cancel"
            };

            _root = new StackPanel();
            _root.Children.Add(scrollViewer);
            _root.Children.Add(_footer);

            Background = BgBrush;
            BorderBrush = BorderBrush_;
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(3);
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 12,
                ShadowDepth = 4,
                Opacity = 0.5,
                Color = Colors.Black
            };
            Child = _root;
            Width = PopupWidth;
            Focusable = false;
        }

        public bool IsOpen
        {
            get { return _isOpen; }
        }

        /// <summary>
        /// Populate the popup with table groups and their columns.
        /// All columns are checked by default.
        /// </summary>
        public void SetData(IEnumerable<TableGroupData> groups)
        {
            _tableGroups = groups.ToList();
            _columnRows.Clear();
            _itemsPanel.Children.Clear();
            _selectedIndex = -1;

            bool multiTable = _tableGroups.Count > 1;

            foreach (var group in _tableGroups)
            {
                if (multiTable)
                {
                    var header = CreateTableHeader(group.TableName);
                    _itemsPanel.Children.Add(header);
                }

                foreach (var col in group.Columns)
                {
                    var row = new ColumnRow
                    {
                        IsChecked = true,
                        ColumnName = col.ColumnName,
                        TypeDisplay = col.TypeDisplay,
                        Qualifier = group.Qualifier
                    };
                    row.Visual = CreateColumnVisual(row);
                    _columnRows.Add(row);
                    _itemsPanel.Children.Add(row.Visual);
                }
            }

            if (_columnRows.Count > 0)
            {
                _selectedIndex = 0;
                UpdateSelection();
            }

            UpdateFooter();
            _isOpen = true;
        }

        /// <summary>Move selection up (-1) or down (+1). Wraps at boundaries.</summary>
        public void MoveSelection(int delta)
        {
            if (_columnRows.Count == 0) return;
            _selectedIndex += delta;
            if (_selectedIndex < 0) _selectedIndex = _columnRows.Count - 1;
            if (_selectedIndex >= _columnRows.Count) _selectedIndex = 0;
            UpdateSelection();
        }

        /// <summary>Toggle checkbox on the currently selected row.</summary>
        public void ToggleSelected()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _columnRows.Count) return;
            var row = _columnRows[_selectedIndex];
            row.IsChecked = !row.IsChecked;
            UpdateRowVisual(row);
            UpdateFooter();
        }

        /// <summary>Check all columns.</summary>
        public void CheckAll()
        {
            foreach (var row in _columnRows)
            {
                row.IsChecked = true;
                UpdateRowVisual(row);
            }
            UpdateFooter();
        }

        /// <summary>Uncheck all columns.</summary>
        public void UncheckAll()
        {
            foreach (var row in _columnRows)
            {
                row.IsChecked = false;
                UpdateRowVisual(row);
            }
            UpdateFooter();
        }

        /// <summary>
        /// Get the checked columns as qualifier.column pairs, preserving table group order.
        /// Returns null if no columns are checked.
        /// </summary>
        public List<QualifiedColumn> GetCheckedColumns()
        {
            var result = new List<QualifiedColumn>();
            foreach (var row in _columnRows)
            {
                if (row.IsChecked)
                {
                    result.Add(new QualifiedColumn
                    {
                        Qualifier = row.Qualifier,
                        ColumnName = row.ColumnName
                    });
                }
            }
            return result.Count > 0 ? result : null;
        }

        /// <summary>Hide the popup and reset state.</summary>
        public void Hide()
        {
            _isOpen = false;
            _columnRows.Clear();
            _itemsPanel.Children.Clear();
            _selectedIndex = -1;
        }

        private UIElement CreateTableHeader(string tableName)
        {
            return new Border
            {
                Background = HeaderBg,
                Padding = new Thickness(8, 3, 8, 3),
                Child = new TextBlock
                {
                    Text = tableName,
                    Foreground = TextBrush,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    FontFamily = new FontFamily("Consolas")
                }
            };
        }

        private Border CreateColumnVisual(ColumnRow row)
        {
            var checkMark = new TextBlock
            {
                Text = "\u2713",
                Foreground = CheckMarkBrush,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = row.IsChecked ? Visibility.Visible : Visibility.Collapsed
            };

            var checkBox = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(2),
                BorderBrush = SecondaryBrush,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(6, 0, 4, 0),
                Background = BgBrush,
                Child = checkMark
            };

            var badge = new Border
            {
                Width = 18,
                Height = 16,
                CornerRadius = new CornerRadius(2),
                Background = ColumnBadgeBrush,
                Margin = new Thickness(2, 0, 6, 0),
                Child = new TextBlock
                {
                    Text = "C",
                    Foreground = Brushes.White,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            var nameText = new TextBlock
            {
                Text = row.ColumnName,
                Foreground = row.IsChecked ? TextBrush : DimTextBrush,
                FontSize = 12,
                FontFamily = new FontFamily("Consolas"),
                VerticalAlignment = VerticalAlignment.Center
            };

            var typeText = new TextBlock
            {
                Text = row.TypeDisplay,
                Foreground = SecondaryBrush,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(8, 0, 4, 0)
            };

            var grid = new Grid { Height = ItemHeight };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid.SetColumn(checkBox, 0);
            Grid.SetColumn(badge, 1);
            Grid.SetColumn(nameText, 2);
            Grid.SetColumn(typeText, 3);

            grid.Children.Add(checkBox);
            grid.Children.Add(badge);
            grid.Children.Add(nameText);
            grid.Children.Add(typeText);

            return new Border
            {
                Child = grid,
                Background = Brushes.Transparent,
                Padding = new Thickness(0)
            };
        }

        private void UpdateRowVisual(ColumnRow row)
        {
            if (row.Visual == null) return;
            var grid = (Grid)row.Visual.Child;

            var checkBorder = (Border)grid.Children[0];
            var checkMark = (TextBlock)checkBorder.Child;
            checkMark.Visibility = row.IsChecked ? Visibility.Visible : Visibility.Collapsed;

            var nameText = (TextBlock)grid.Children[2];
            nameText.Foreground = row.IsChecked ? TextBrush : DimTextBrush;
        }

        private void UpdateSelection()
        {
            for (int i = 0; i < _columnRows.Count; i++)
            {
                var row = _columnRows[i];
                if (row.Visual != null)
                {
                    row.Visual.Background = (i == _selectedIndex) ? SelectedBg : Brushes.Transparent;
                }
            }

            if (_selectedIndex >= 0 && _selectedIndex < _columnRows.Count)
            {
                var visual = _columnRows[_selectedIndex].Visual;
                if (visual != null) visual.BringIntoView();
            }
        }

        private void UpdateFooter()
        {
            int total = _columnRows.Count;
            int checkedCount = _columnRows.Count(r => r.IsChecked);
            _footer.Text = string.Format("{0}/{1} columns selected | Space: toggle | Tab: expand", checkedCount, total);
        }

        private static SolidColorBrush Freeze(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }

        internal class ColumnRow
        {
            public bool IsChecked;
            public string ColumnName = string.Empty;
            public string TypeDisplay = string.Empty;
            public string Qualifier = string.Empty;
            public Border Visual;
        }

        internal class QualifiedColumn
        {
            public string Qualifier = string.Empty;
            public string ColumnName = string.Empty;
        }

        internal class TableGroupData
        {
            public string TableName = string.Empty;
            public string Qualifier = string.Empty;
            public ColumnData[] Columns = Array.Empty<ColumnData>();
        }

        internal class ColumnData
        {
            public string ColumnName = string.Empty;
            public string TypeDisplay = string.Empty;
        }
    }
}
