using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AkmlSql.Shell.Shared.Ui.Theme;

namespace AkmlSql.Shell.Shared.Editor.Completion
{
    /// <summary>
    /// SQL Prompt-style checkbox popup for wildcard expansion.
    /// Code-only WPF (no XAML). Shows columns grouped by table with checkboxes for selecting
    /// which columns to include in the expansion. Chrome flows through <see cref="ThemeRegistry"/>
    /// so the popup tracks Light / Dark / High Contrast variants.
    /// </summary>
    internal sealed class WildcardExpansionPopup : Border
    {
        // SQL Prompt Column badge — the gold "C" mark is a domain icon (FR-003 semantic constant,
        // theme-independent — same role as the Column entry in SqlPromptIcons.cs).
        private static readonly SolidColorBrush ColumnBadgeBrush =
            FrozenBrush(Color.FromRgb(0xF9, 0xA8, 0x25));

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

        public WildcardExpansionPopup()
        {
            // Attach the theme registry to ourselves so SetResourceReference on this Border AND
            // any visual-tree descendant resolves through ThemeRegistry.Resources.
            ThemeRegistry.Instance.AttachTo(this);

            _itemsPanel = new StackPanel();

            var scrollViewer = new ScrollViewer
            {
                Content                       = _itemsPanel,
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight                     = MaxVisibleItems * ItemHeight,
                Focusable                     = false
            };
            scrollViewer.SetResourceReference(ScrollViewer.BackgroundProperty, ThemeTokens.EditorPopupBackground);

            _footer = new TextBlock
            {
                FontSize = Typography.Small,
                Padding  = new Thickness(Spacing.Sm, 3, Spacing.Sm, 3),
                Text     = "Space: toggle | Tab/Enter: expand | Esc: cancel"
            };
            _footer.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            _footer.SetResourceReference(TextBlock.BackgroundProperty, ThemeTokens.SurfaceCanvas);

            _root = new StackPanel();
            _root.Children.Add(scrollViewer);
            _root.Children.Add(_footer);

            SetResourceReference(BackgroundProperty,  ThemeTokens.EditorPopupBackground);
            SetResourceReference(BorderBrushProperty, ThemeTokens.EditorPopupBorder);
            BorderThickness = new Thickness(1);
            CornerRadius    = new CornerRadius(3);
            Effect          = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius  = 12,
                ShadowDepth = 4,
                Opacity     = 0.5,
                Color       = Colors.Black
            };
            Child     = _root;
            Width     = PopupWidth;
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
                        IsChecked   = true,
                        ColumnName  = col.ColumnName,
                        TypeDisplay = col.TypeDisplay,
                        Qualifier   = group.Qualifier
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
                        Qualifier  = row.Qualifier,
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
            var headerText = new TextBlock
            {
                Text       = tableName,
                FontSize   = Typography.Body,
                FontWeight = FontWeights.SemiBold,
                FontFamily = Typography.MonoFont
            };
            headerText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);

            var border = new Border
            {
                Padding = new Thickness(Spacing.Sm, 3, Spacing.Sm, 3),
                Child   = headerText
            };
            border.SetResourceReference(Border.BackgroundProperty, ThemeTokens.SurfaceElevated);
            return border;
        }

        private Border CreateColumnVisual(ColumnRow row)
        {
            var checkMark = new TextBlock
            {
                Text                = "✓",
                FontSize            = Typography.Small,
                FontWeight          = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
                Visibility          = row.IsChecked ? Visibility.Visible : Visibility.Collapsed
            };
            checkMark.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.StatusSuccess);

            var checkBox = new Border
            {
                Width           = 14,
                Height          = 14,
                CornerRadius    = new CornerRadius(2),
                BorderThickness = new Thickness(1),
                Margin          = new Thickness(6, 0, 4, 0),
                Child           = checkMark
            };
            checkBox.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.TextSecondary);
            checkBox.SetResourceReference(Border.BackgroundProperty,  ThemeTokens.EditorPopupBackground);

            // Domain badge — Column = gold (FR-003 carveout).
            var badge = new Border
            {
                Width        = 18,
                Height       = 16,
                CornerRadius = new CornerRadius(2),
                Background   = ColumnBadgeBrush,
                Margin       = new Thickness(2, 0, 6, 0),
                Child = new TextBlock
                {
                    Text                = "C",
                    Foreground          = Brushes.White,   // theme-independent: white-on-gold reads same in both themes
                    FontSize            = 10,
                    FontWeight          = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center
                }
            };

            var nameText = new TextBlock
            {
                Text              = row.ColumnName,
                FontSize          = Typography.Body,
                FontFamily        = Typography.MonoFont,
                VerticalAlignment = VerticalAlignment.Center
            };
            nameText.SetResourceReference(TextBlock.ForegroundProperty,
                row.IsChecked ? ThemeTokens.TextPrimary : ThemeTokens.TextDisabled);

            var typeText = new TextBlock
            {
                Text                = row.TypeDisplay,
                FontSize            = Typography.Small,
                VerticalAlignment   = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin              = new Thickness(Spacing.Sm, 0, 4, 0)
            };
            typeText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);

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
                Child      = grid,
                Background = Brushes.Transparent,   // theme-independent placeholder; selected state replaces this
                Padding    = new Thickness(0)
            };
        }

        private void UpdateRowVisual(ColumnRow row)
        {
            if (row.Visual == null) return;
            var grid = (Grid)row.Visual.Child;

            var checkBorder = (Border)grid.Children[0];
            var checkMark   = (TextBlock)checkBorder.Child;
            checkMark.Visibility = row.IsChecked ? Visibility.Visible : Visibility.Collapsed;

            var nameText = (TextBlock)grid.Children[2];
            nameText.SetResourceReference(TextBlock.ForegroundProperty,
                row.IsChecked ? ThemeTokens.TextPrimary : ThemeTokens.TextDisabled);
        }

        private void UpdateSelection()
        {
            for (int i = 0; i < _columnRows.Count; i++)
            {
                var row = _columnRows[i];
                if (row.Visual == null) continue;
                if (i == _selectedIndex)
                {
                    row.Visual.SetResourceReference(Border.BackgroundProperty, ThemeTokens.SurfaceSelectionStrong);
                }
                else
                {
                    row.Visual.Background = Brushes.Transparent;   // theme-independent placeholder
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

        private static SolidColorBrush FrozenBrush(Color color)
        {
            var b = new SolidColorBrush(color);
            b.Freeze();
            return b;
        }

        internal class ColumnRow
        {
            public bool IsChecked;
            public string ColumnName  = string.Empty;
            public string TypeDisplay = string.Empty;
            public string Qualifier   = string.Empty;
            public Border Visual;
        }

        internal class QualifiedColumn
        {
            public string Qualifier  = string.Empty;
            public string ColumnName = string.Empty;
        }

        internal class TableGroupData
        {
            public string TableName  = string.Empty;
            public string Qualifier  = string.Empty;
            public ColumnData[] Columns = Array.Empty<ColumnData>();
        }

        internal class ColumnData
        {
            public string ColumnName  = string.Empty;
            public string TypeDisplay = string.Empty;
        }
    }
}
