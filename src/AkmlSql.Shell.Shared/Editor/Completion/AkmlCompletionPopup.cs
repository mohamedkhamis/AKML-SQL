using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AkmlSql.Shell.Shared.Ui.Theme;

namespace AkmlSql.Shell.Shared.Editor.Completion
{
    /// <summary>
    /// Code-only WPF popup replicating Redgate SQL Prompt's completion list. Coloured type
    /// badges, fuzzy filter, keyboard navigation. Chrome flows through <see cref="ThemeRegistry"/>;
    /// the per-item type colours come from <see cref="CompletionItemModel"/> which is an FR-003
    /// domain-icon carveout (theme-independent).
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
        /// Raised when the selected completion item changes (keyboard navigation, filter, or a
        /// mouse click on a row). The controller subscribes to this to trigger QuickInfo requests
        /// with debounce.
        /// </summary>
        public event EventHandler<CompletionItemModel> SelectionChanged;

        /// <summary>
        /// Raised when the user double-clicks an item row — the controller commits it exactly like
        /// Tab/Enter (SQL Prompt parity; mirrors <see cref="WildcardExpansionPopup.CommitRequested"/>).
        /// </summary>
        public event EventHandler<CompletionItemModel> ItemCommitRequested;

        private const int    MaxVisibleItems   = 15;
        private const double ItemHeight        = 22;
        private const double DefaultPopupWidth = 380;
        private const double MinPopupWidth     = 280;
        private const double MaxPopupWidth     = 900;
        private const double MinPopupHeight    = 100;
        private const double MaxPopupHeight    = 800;

        // Resize state
        private bool _isResizing;
        private Point _resizeStart;
        private double _resizeStartWidth;
        private double _resizeStartHeight;
        private readonly Border _resizeGrip;

        public AkmlCompletionPopup()
        {
            // Attach the theme registry so SetResourceReference on this Border AND any
            // visual-tree descendant resolves through ThemeRegistry.Resources.
            ThemeRegistry.Instance.AttachTo(this);

            _root = new StackPanel();

            // List box with custom item rendering. The container Style relies on the registry
            // having been attached above for its DynamicResource setters to find tokens.
            _listBox = new ListBox
            {
                BorderThickness   = new Thickness(0),
                Padding           = new Thickness(0),
                MaxHeight         = MaxVisibleItems * ItemHeight,
                Focusable         = false,
                SelectionMode     = SelectionMode.Single,
                ItemContainerStyle = CreateItemContainerStyle()
            };
            _listBox.SetResourceReference(ListBox.BackgroundProperty, ThemeTokens.EditorPopupBackground);

            // Loading text
            _loadingText = new TextBlock
            {
                Text       = "Loading...",
                FontSize   = Typography.Body,
                Padding    = new Thickness(Spacing.Sm, 6, Spacing.Sm, 6),
                Visibility = Visibility.Collapsed
            };
            _loadingText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);

            // Footer with resize grip
            var footerGrid = new Grid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footerGrid.SetResourceReference(Grid.BackgroundProperty, ThemeTokens.SurfaceCanvas);

            _footer = new TextBlock
            {
                FontSize          = Typography.Small,
                Padding           = new Thickness(Spacing.Sm, 3, Spacing.Sm, 3),
                VerticalAlignment = VerticalAlignment.Center
            };
            _footer.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            _footer.SetResourceReference(TextBlock.BackgroundProperty, ThemeTokens.SurfaceCanvas);
            Grid.SetColumn(_footer, 0);

            // Resize grip: triangle in bottom-right corner
            var gripGlyph = new TextBlock
            {
                Text                = "◢",
                FontSize            = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            };
            gripGlyph.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            _resizeGrip = new Border
            {
                Width               = 14,
                Height              = 14,
                Cursor              = Cursors.SizeNWSE,
                Background          = Brushes.Transparent,   // theme-independent: hit-test region only
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment   = VerticalAlignment.Bottom,
                Child               = gripGlyph
            };
            Grid.SetColumn(_resizeGrip, 1);

            _resizeGrip.MouseLeftButtonDown += OnResizeGripMouseDown;
            _resizeGrip.MouseMove += OnResizeGripMouseMove;
            _resizeGrip.MouseLeftButtonUp += OnResizeGripMouseUp;

            footerGrid.Children.Add(_footer);
            footerGrid.Children.Add(_resizeGrip);

            _root.Children.Add(_loadingText);
            _root.Children.Add(_listBox);
            _root.Children.Add(footerGrid);

            // Border styling
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
            Width     = DefaultPopupWidth;
            Focusable = false;

            // Spec 020 US4 (T063) — Ctrl-held semi-transparency. Per SQL Prompt's UX, holding Ctrl
            // while the popup is open makes it semi-transparent so the user can read the editor
            // text behind it. The popup itself is not focusable, so we poll Keyboard.IsKeyDown
            // (which reads the global modifier state) while the popup is visible. Polling is
            // bounded to the popup's visible lifetime — no background work when hidden.
            IsVisibleChanged += OnPopupIsVisibleChanged;
        }

        // -------------------------------------------------------------------
        // T063 — Ctrl-held semi-transparency
        // -------------------------------------------------------------------

        private System.Windows.Threading.DispatcherTimer? _ctrlPollTimer;

        private void OnPopupIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue)
            {
                if (_ctrlPollTimer == null)
                {
                    _ctrlPollTimer = new System.Windows.Threading.DispatcherTimer(
                        System.Windows.Threading.DispatcherPriority.Input)
                    {
                        Interval = TimeSpan.FromMilliseconds(50),
                    };
                    _ctrlPollTimer.Tick += OnCtrlPollTick;
                }
                _ctrlPollTimer.Start();
            }
            else
            {
                _ctrlPollTimer?.Stop();
                Opacity = 1.0; // restore full opacity when hidden so the next Show starts clean
            }
        }

        private void OnCtrlPollTick(object sender, EventArgs e)
        {
            // Use Keyboard.IsKeyDown so we read the live OS modifier state — the popup itself
            // is non-focusable so KeyDown events don't reach it directly.
            var ctrlHeld = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            // Per spec FR-005 acceptance: hold Ctrl → semi-transparent (60% opaque) so editor text
            // remains readable; release Ctrl → fully opaque. Never go below 40% so the popup
            // doesn't disappear under accidental Ctrl-Alt combos etc.
            var targetOpacity = ctrlHeld ? 0.6 : 1.0;
            if (Math.Abs(Opacity - targetOpacity) > 0.001)
            {
                Opacity = targetOpacity;
            }
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
            // Badge: semi-transparent background with coloured letter (SQL Prompt style).
            // item.IconColor is an FR-003 domain icon constant -- theme-independent.
            var badgeBgColor = item.IconColor;
            badgeBgColor.A = (byte)(255 * item.IconBackgroundOpacity); // 20% (15% for Keyword)
            var bgBrush = new SolidColorBrush(badgeBgColor);
            bgBrush.Freeze();
            var letterBrush = new SolidColorBrush(item.IconColor);
            letterBrush.Freeze();
            var badge = new Border
            {
                Width        = 18,
                Height       = 16,
                CornerRadius = new CornerRadius(2),
                Background   = bgBrush,
                Margin       = new Thickness(4, 0, 6, 0),
                Child = new TextBlock
                {
                    Text                = item.IconLetter,
                    Foreground          = letterBrush,
                    FontSize            = 10,
                    FontWeight          = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center
                }
            };

            // Display text
            var displayText = new TextBlock
            {
                Text              = item.DisplayText,
                FontSize          = Typography.Body,
                FontFamily        = Typography.MonoFont,
                VerticalAlignment = VerticalAlignment.Center
            };
            displayText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);

            // Secondary text (type info, row count)
            var secondaryText = new TextBlock
            {
                Text                = item.SecondaryText,
                FontSize            = Typography.Small,
                VerticalAlignment   = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin              = new Thickness(Spacing.Sm, 0, 4, 0)
            };
            secondaryText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);

            // Row layout. Transparent (not null) background so the WHOLE row hit-tests — with a
            // null background, clicks between/after the text blocks fell through and the mouse
            // did nothing on the list.
            var grid = new Grid { Height = ItemHeight, Background = Brushes.Transparent };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid.SetColumn(badge, 0);
            Grid.SetColumn(displayText, 1);
            Grid.SetColumn(secondaryText, 2);

            grid.Children.Add(badge);
            grid.Children.Add(displayText);
            grid.Children.Add(secondaryText);

            // Mouse interaction (SQL Prompt parity; mirrors WildcardExpansionPopup):
            //   • single click → select the row under the mouse (highlight + QuickInfo follow);
            //   • double click → commit the item, same as Tab/Enter.
            // The popup and its items are non-focusable so this never steals editor focus; the
            // event is always handled so the click cannot bubble on into the editor view.
            grid.MouseLeftButtonDown += (sender, e) =>
            {
                int idx = Array.IndexOf(_filteredItems, item);
                if (idx >= 0 && _listBox.SelectedIndex != idx)
                {
                    _listBox.SelectedIndex = idx;
                    RaiseSelectionChanged();
                }
                if (e.ClickCount == 2)
                {
                    var selected = GetSelectedItem();
                    if (selected != null)
                        ItemCommitRequested?.Invoke(this, selected);
                }
                e.Handled = true;
            };

            return grid;
        }

        private void UpdateFooter()
        {
            var total = _allItems.Length;
            var shown = _filteredItems.Length;
            var db = string.IsNullOrEmpty(_databaseName) ? "" : $" • {_databaseName}";
            _footer.Text = $"{shown} of {total} objects{db}";
        }

        private static Style CreateItemContainerStyle()
        {
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(ListBoxItem.BackgroundProperty,      Brushes.Transparent));
            style.Setters.Add(new Setter(ListBoxItem.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(ListBoxItem.PaddingProperty,         new Thickness(0)));
            style.Setters.Add(new Setter(ListBoxItem.MarginProperty,          new Thickness(0)));
            style.Setters.Add(new Setter(ListBoxItem.FocusableProperty,       false));
            // Stretch the row visual to the full item width so the click handler on the row grid
            // covers the whole line, not just the rendered text.
            style.Setters.Add(new Setter(ListBoxItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));

            // Selected item highlight
            var selectedTrigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(ListBoxItem.BackgroundProperty,
                new DynamicResourceExtension(ThemeTokens.SurfaceSelectionStrong)));
            style.Triggers.Add(selectedTrigger);

            // Mouse over highlight (only when not selected)
            var hoverTrigger = new MultiTrigger();
            hoverTrigger.Conditions.Add(new Condition(ListBoxItem.IsMouseOverProperty, true));
            hoverTrigger.Conditions.Add(new Condition(ListBoxItem.IsSelectedProperty, false));
            hoverTrigger.Setters.Add(new Setter(ListBoxItem.BackgroundProperty,
                new DynamicResourceExtension(ThemeTokens.SurfaceHover)));
            style.Triggers.Add(hoverTrigger);

            return style;
        }

        // --- Resize Grip Handlers ---------------------------------------------

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
    }
}
