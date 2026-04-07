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
    /// Code-only WPF popup replicating Redgate SQL Prompt's completion list.
    /// Dark theme, colored type badges, fuzzy filter, keyboard navigation.
    /// No XAML — built entirely in C# to work across all 6 shared project hosts.
    /// </summary>
    internal sealed class AkmlCompletionPopup : Border
    {
        private readonly ListBox _listBox;
        private readonly TextBlock _footer;
        private readonly TextBlock _loadingText;
        private readonly StackPanel _root;

        private CompletionItemModel[] _allItems = Array.Empty<CompletionItemModel>();
        private CompletionItemModel[] _filteredItems = Array.Empty<CompletionItemModel>();
        private string _currentFilter = string.Empty;
        private string _databaseName = string.Empty;
        private bool _isOpen;

        /// <summary>
        /// Raised when the selected completion item changes (keyboard navigation or filter).
        /// The controller subscribes to this to trigger QuickInfo requests with debounce.
        /// </summary>
        public event EventHandler<CompletionItemModel> SelectionChanged;

        private const int MaxVisibleItems = 15;
        private const double ItemHeight = 22;
        private const double DefaultPopupWidth = 380;
        private const double MinPopupWidth = 280;
        private const double MaxPopupWidth = 900;
        private const double MinPopupHeight = 100;
        private const double MaxPopupHeight = 800;

        // SQL Prompt dark theme colors
        private static readonly SolidColorBrush BgBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)));
        private static readonly SolidColorBrush BorderBrush_ = Freeze(new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)));
        private static readonly SolidColorBrush SelectedBg = Freeze(new SolidColorBrush(Color.FromRgb(0x09, 0x47, 0x71)));
        private static readonly SolidColorBrush TextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4)));
        private static readonly SolidColorBrush SecondaryBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)));
        private static readonly SolidColorBrush FooterBg = Freeze(new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)));
        private static readonly SolidColorBrush TransparentBrush = Freeze(new SolidColorBrush(Colors.Transparent));

        // Resize state
        private bool _isResizing;
        private Point _resizeStart;
        private double _resizeStartWidth;
        private double _resizeStartHeight;
        private readonly Border _resizeGrip;

        public AkmlCompletionPopup()
        {
            // Root container
            _root = new StackPanel();

            // List box with custom item rendering
            _listBox = new ListBox
            {
                Background = BgBrush,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                MaxHeight = MaxVisibleItems * ItemHeight,
                Focusable = false,
                SelectionMode = SelectionMode.Single
            };

            // Remove default list box styling that causes focus issues
            _listBox.ItemContainerStyle = CreateItemContainerStyle();

            // Loading text
            _loadingText = new TextBlock
            {
                Text = "Loading...",
                Foreground = SecondaryBrush,
                FontSize = 12,
                Padding = new Thickness(8, 6, 8, 6),
                Visibility = Visibility.Collapsed
            };

            // Footer with resize grip
            var footerGrid = new Grid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _footer = new TextBlock
            {
                Foreground = SecondaryBrush,
                FontSize = 11,
                Padding = new Thickness(8, 3, 8, 3),
                Background = FooterBg,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_footer, 0);

            // Resize grip: triangle in bottom-right corner
            _resizeGrip = new Border
            {
                Width = 14,
                Height = 14,
                Cursor = Cursors.SizeNWSE,
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Child = new TextBlock
                {
                    Text = "\u25E2", // ◢ triangle
                    Foreground = SecondaryBrush,
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(_resizeGrip, 1);

            _resizeGrip.MouseLeftButtonDown += OnResizeGripMouseDown;
            _resizeGrip.MouseMove += OnResizeGripMouseMove;
            _resizeGrip.MouseLeftButtonUp += OnResizeGripMouseUp;

            footerGrid.Children.Add(_footer);
            footerGrid.Children.Add(_resizeGrip);
            footerGrid.Background = FooterBg;

            _root.Children.Add(_loadingText);
            _root.Children.Add(_listBox);
            _root.Children.Add(footerGrid);

            // Border styling
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
            Width = DefaultPopupWidth;
            Focusable = false;
        }

        /// <summary>Set the database name shown in footer.</summary>
        public void SetDatabase(string dbName)
        {
            _databaseName = dbName ?? string.Empty;
            UpdateFooter();
        }

        /// <summary>Show loading state.</summary>
        public void ShowLoading()
        {
            _loadingText.Visibility = Visibility.Visible;
            _listBox.Visibility = Visibility.Collapsed;
            _footer.Text = "";
            _isOpen = true;
        }

        /// <summary>Set the full list of completion items from the Engine.</summary>
        public void SetItems(CompletionItemModel[] items)
        {
            _allItems = items ?? Array.Empty<CompletionItemModel>();
            _loadingText.Visibility = Visibility.Collapsed;
            _listBox.Visibility = Visibility.Visible;
            ApplyFilter();
        }

        /// <summary>Filter displayed items by partial text (client-side, no round-trip).</summary>
        public void SetFilter(string text)
        {
            _currentFilter = text ?? string.Empty;
            ApplyFilter();
        }

        /// <summary>Move selection up (delta=-1) or down (delta=+1). Wraps at boundaries.</summary>
        public void MoveSelection(int delta)
        {
            if (_filteredItems.Length == 0) return;
            var idx = _listBox.SelectedIndex + delta;
            if (idx < 0) idx = _filteredItems.Length - 1;
            if (idx >= _filteredItems.Length) idx = 0;
            _listBox.SelectedIndex = idx;
            _listBox.ScrollIntoView(_listBox.SelectedItem);
            RaiseSelectionChanged();
        }

        /// <summary>Get the currently selected item, or null if none.</summary>
        public CompletionItemModel GetSelectedItem()
        {
            if (_listBox.SelectedIndex < 0 || _listBox.SelectedIndex >= _filteredItems.Length)
                return null;
            return _filteredItems[_listBox.SelectedIndex];
        }

        /// <summary>True if popup is showing and has items.</summary>
        public bool IsOpen => _isOpen && _filteredItems.Length > 0;

        /// <summary>Show the popup.</summary>
        public void Show()
        {
            _isOpen = true;
        }

        /// <summary>Hide the popup and reset state.</summary>
        public void Hide()
        {
            _isOpen = false;
            _currentFilter = string.Empty;
        }

        private void ApplyFilter()
        {
            _filteredItems = string.IsNullOrEmpty(_currentFilter)
                ? _allItems.OrderBy(i => i.SortPriority)
                    .ThenBy(i => i.DisplayText, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : _allItems.Where(i => i.MatchesFilter(_currentFilter))
                    .OrderBy(i => i.FilterScore(_currentFilter))
                    .ThenBy(i => i.SortPriority)
                    .ThenBy(i => i.DisplayText, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            RenderItems();
            UpdateFooter();

            if (_filteredItems.Length == 0)
            {
                Hide();
            }
            else
            {
                _isOpen = true;
                if (_listBox.SelectedIndex < 0 && _filteredItems.Length > 0)
                    _listBox.SelectedIndex = 0;
            }
        }

        private void RenderItems()
        {
            _listBox.Items.Clear();
            foreach (var item in _filteredItems)
            {
                _listBox.Items.Add(CreateItemVisual(item));
            }

            if (_filteredItems.Length > 0)
            {
                _listBox.SelectedIndex = 0;
                RaiseSelectionChanged();
            }
        }

        private void RaiseSelectionChanged()
        {
            var item = GetSelectedItem();
            if (item != null)
                SelectionChanged?.Invoke(this, item);
        }

        private UIElement CreateItemVisual(CompletionItemModel item)
        {
            // Badge: semi-transparent background with colored letter (SQL Prompt style)
            var badgeBgColor = item.IconColor;
            badgeBgColor.A = (byte)(255 * item.IconBackgroundOpacity); // 20% opacity (15% for Keyword)
            var bgBrush = new SolidColorBrush(badgeBgColor);
            bgBrush.Freeze();
            var letterBrush = new SolidColorBrush(item.IconColor);
            letterBrush.Freeze();
            var badge = new Border
            {
                Width = 18,
                Height = 16,
                CornerRadius = new CornerRadius(2),
                Background = bgBrush,
                Margin = new Thickness(4, 0, 6, 0),
                Child = new TextBlock
                {
                    Text = item.IconLetter,
                    Foreground = letterBrush,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            // Display text
            var displayText = new TextBlock
            {
                Text = item.DisplayText,
                Foreground = TextBrush,
                FontSize = 12,
                FontFamily = new FontFamily("Consolas"),
                VerticalAlignment = VerticalAlignment.Center
            };

            // Secondary text (type info, row count)
            var secondaryText = new TextBlock
            {
                Text = item.SecondaryText,
                Foreground = SecondaryBrush,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(8, 0, 4, 0)
            };

            // Row layout
            var grid = new Grid { Height = ItemHeight };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid.SetColumn(badge, 0);
            Grid.SetColumn(displayText, 1);
            Grid.SetColumn(secondaryText, 2);

            grid.Children.Add(badge);
            grid.Children.Add(displayText);
            grid.Children.Add(secondaryText);

            return grid;
        }

        private void UpdateFooter()
        {
            var total = _allItems.Length;
            var shown = _filteredItems.Length;
            var db = string.IsNullOrEmpty(_databaseName) ? "" : $" \u2022 {_databaseName}";
            _footer.Text = $"{shown} of {total} objects{db}";
        }

        private static Style CreateItemContainerStyle()
        {
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, TransparentBrush));
            style.Setters.Add(new Setter(ListBoxItem.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(ListBoxItem.MarginProperty, new Thickness(0)));
            style.Setters.Add(new Setter(ListBoxItem.FocusableProperty, false));

            // Selected item highlight
            var selectedTrigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, SelectedBg));
            style.Triggers.Add(selectedTrigger);

            // Mouse over highlight
            var hoverTrigger = new Trigger { Property = ListBoxItem.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(ListBoxItem.BackgroundProperty,
                Freeze(new SolidColorBrush(Color.FromRgb(0x2A, 0x2D, 0x2E)))));
            style.Triggers.Add(hoverTrigger);

            return style;
        }

        // ─── Resize Grip Handlers ───────────────────────────────────────

        private void OnResizeGripMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isResizing = true;
            _resizeStart = _resizeGrip.PointToScreen(e.GetPosition(_resizeGrip));
            _resizeStartWidth = ActualWidth;
            _resizeStartHeight = _listBox.MaxHeight;
            _resizeGrip.CaptureMouse();
            e.Handled = true;
        }

        private void OnResizeGripMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isResizing) return;

            var currentPos = _resizeGrip.PointToScreen(e.GetPosition(_resizeGrip));
            var deltaX = currentPos.X - _resizeStart.X;
            var deltaY = currentPos.Y - _resizeStart.Y;

            var newWidth = Math.Max(MinPopupWidth, Math.Min(MaxPopupWidth, _resizeStartWidth + deltaX));
            Width = newWidth;

            var newListHeight = Math.Max(MinPopupHeight, Math.Min(MaxPopupHeight, _resizeStartHeight + deltaY));
            _listBox.MaxHeight = newListHeight;

            e.Handled = true;
        }

        private void OnResizeGripMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isResizing) return;
            _isResizing = false;
            _resizeGrip.ReleaseMouseCapture();
            e.Handled = true;
        }

        private static SolidColorBrush Freeze(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }
    }
}
