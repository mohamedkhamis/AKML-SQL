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

        // Spec 030 T034 / FR-014 — the suggestions box is grouped by category. The ListBox holds a
        // mix of non-selectable category-header rows and item rows; _rows is the parallel model
        // (header vs item) and _categoryStarts holds the ListBox index of each category's first item
        // (for Ctrl+Up/Down category navigation). Selection only ever lands on item rows.
        private Row[] _rows = Array.Empty<Row>();
        private int[] _categoryStarts = Array.Empty<int>();

        // FR-014 — show/hide owner (schema) names. Static so the choice persists across re-opens and
        // every editor view in the session; toggled live from the footer affordance. Default on
        // (matches the engine's owner-qualified DisplayText). Only the *display* is affected — the
        // committed InsertText keeps the full qualification.
        private static bool _showOwnerNames = true;
        private TextBlock _ownerToggle;

        /// <summary>A rendered row: either a category header or a completion item.</summary>
        private sealed class Row
        {
            public bool IsHeader;
            public CompletionItemModel Item;
            public static Row HeaderRow(string _) => new Row { IsHeader = true };
            public static Row ItemRow(CompletionItemModel item) => new Row { IsHeader = false, Item = item };
        }

        /// <summary>Maps a completion item's <c>ObjectType</c> to its category-group label (FR-014).</summary>
        private static string CategoryOf(int objectType) => objectType switch
        {
            0  => "Tables",
            1  => "Views",
            2  => "Columns",
            3  => "Keywords",
            4  => "Snippets",
            5  => "Functions",
            6  => "Stored Procedures",
            7  => "Schemas",
            8  => "Databases",
            9  => "Variables",
            10 => "Aliases",
            11 => "Parameters",
            12 => "Actions",
            _  => "Other",
        };

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

            // Footer: owner-name toggle (left) · count (center) · resize grip (right)
            var footerGrid = new Grid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });               // owner toggle
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // count
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });               // grip
            footerGrid.SetResourceReference(Grid.BackgroundProperty, ThemeTokens.SurfaceCanvas);

            // FR-014 — owner (schema) name show/hide affordance. Non-focusable click toggle; flips the
            // session-wide static and re-renders so object labels gain/lose the "schema." prefix.
            _ownerToggle = new TextBlock
            {
                FontSize          = Typography.Small,
                Padding           = new Thickness(Spacing.Sm, 3, Spacing.Sm, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor            = Cursors.Hand,
                ToolTip           = "Show or hide schema (owner) names in suggestions"
            };
            _ownerToggle.MouseLeftButtonDown += (s, e) =>
            {
                // Toggling owner names changes only the display strings — the row structure, ordering
                // and count are identical after re-render, so the prior index maps to the same item.
                // Preserve the user's place instead of snapping back to the top (display-only change).
                int prevSel = _listBox.SelectedIndex;
                _showOwnerNames = !_showOwnerNames;
                UpdateOwnerToggleVisual();
                RenderItems();
                if (prevSel > 0 && prevSel < _rows.Length && !_rows[prevSel].IsHeader)
                {
                    _listBox.SelectedIndex = prevSel;
                    _listBox.ScrollIntoView(_listBox.SelectedItem);
                    RaiseSelectionChanged();
                }
                e.Handled = true;
            };
            UpdateOwnerToggleVisual();
            Grid.SetColumn(_ownerToggle, 0);

            _footer = new TextBlock
            {
                FontSize          = Typography.Small,
                Padding           = new Thickness(Spacing.Sm, 3, Spacing.Sm, 3),
                VerticalAlignment = VerticalAlignment.Center
            };
            _footer.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            _footer.SetResourceReference(TextBlock.BackgroundProperty, ThemeTokens.SurfaceCanvas);
            Grid.SetColumn(_footer, 1);

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
            Grid.SetColumn(_resizeGrip, 2);

            _resizeGrip.MouseLeftButtonDown += OnResizeGripMouseDown;
            _resizeGrip.MouseMove += OnResizeGripMouseMove;
            _resizeGrip.MouseLeftButtonUp += OnResizeGripMouseUp;

            footerGrid.Children.Add(_ownerToggle);
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
            _listBox.Items.Clear();
            _footer.Text = "";
            // Drop the previous batch's model so navigation/commit can't act on stale rows while the
            // fresh results are in flight (IsOpen becomes false until SetItems repopulates).
            ClearItemModel();
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

        /// <summary>Move selection up (delta=-1) or down (delta=+1), skipping category headers. Wraps.</summary>
        public void MoveSelection(int delta)
        {
            if (_rows.Length == 0 || _filteredItems.Length == 0) return;
            int idx = _listBox.SelectedIndex;
            if (idx < 0) idx = FirstItemIndex();
            // Step in the requested direction until we land on an item row (never a header).
            for (int step = 0; step < _rows.Length; step++)
            {
                idx += delta;
                if (idx < 0) idx = _rows.Length - 1;
                if (idx >= _rows.Length) idx = 0;
                if (!_rows[idx].IsHeader) break;
            }
            _listBox.SelectedIndex = idx;
            _listBox.ScrollIntoView(_listBox.SelectedItem);
            RaiseSelectionChanged();
        }

        /// <summary>
        /// FR-014 — jump selection to the first item of the previous (delta=-1) or next (delta=+1)
        /// category, wrapping at the ends. Bound to Ctrl+Up / Ctrl+Down by the controller.
        /// </summary>
        public void MoveCategory(int delta)
        {
            if (_categoryStarts.Length == 0) return;
            int cur = _listBox.SelectedIndex;
            int ci = 0;
            for (int i = 0; i < _categoryStarts.Length; i++)
                if (_categoryStarts[i] <= cur) ci = i;
            int target = ci + delta;
            if (target < 0) target = _categoryStarts.Length - 1;
            if (target >= _categoryStarts.Length) target = 0;
            _listBox.SelectedIndex = _categoryStarts[target];
            _listBox.ScrollIntoView(_listBox.SelectedItem);
            RaiseSelectionChanged();
        }

        /// <summary>Index of the first non-header (item) row, or -1 when there are no item rows.</summary>
        private int FirstItemIndex()
        {
            for (int i = 0; i < _rows.Length; i++)
                if (!_rows[i].IsHeader) return i;
            return -1;
        }

        /// <summary>Get the currently selected item, or null if none (or a header row is selected).</summary>
        public CompletionItemModel GetSelectedItem()
        {
            int idx = _listBox.SelectedIndex;
            if (idx < 0 || idx >= _rows.Length) return null;
            return _rows[idx].IsHeader ? null : _rows[idx].Item;
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
            // Clear the grouped model so a re-trigger can't navigate/commit the previous batch before
            // fresh results arrive — the controller gates nav/commit on IsOpen (← _filteredItems).
            ClearItemModel();
        }

        /// <summary>
        /// Drops the grouped-row model (header/item rows + category starts) so navigation/commit can't
        /// act on the previous batch's rows while a re-trigger is in flight. Deliberately leaves
        /// <see cref="_filteredItems"/> intact: post-T034 nav/commit index <see cref="_rows"/> (an empty
        /// <see cref="_rows"/> already makes <see cref="MoveSelection"/>/<see cref="MoveCategory"/>/
        /// <see cref="GetSelectedItem"/> safe no-ops), and clearing _filteredItems would flip
        /// <see cref="IsOpen"/> false mid-load and stop the controller's native-IntelliSense suppress timer.
        /// </summary>
        private void ClearItemModel()
        {
            _rows = Array.Empty<Row>();
            _categoryStarts = Array.Empty<int>();
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
                // RenderItems() already selected the first item row (skipping the leading header).
            }
        }

        private void RenderItems()
        {
            // Keep the footer affordance in sync with what we are about to render — _showOwnerNames is
            // a session-wide static, so a toggle in another popup can leave this one's label stale.
            UpdateOwnerToggleVisual();

            _listBox.Items.Clear();

            // Group by category in FIRST-APPEARANCE order. _filteredItems is already ordered by
            // relevance (SortPriority / filter score), so the first category seen contains the most
            // relevant item — grouping this way keeps the existing relevance ordering at the category
            // level instead of imposing a fixed category order (FR-014).
            var order = new List<string>();
            var buckets = new Dictionary<string, List<CompletionItemModel>>();
            foreach (var item in _filteredItems)
            {
                var cat = CategoryOf(item.ObjectType);
                if (!buckets.TryGetValue(cat, out var list))
                {
                    buckets[cat] = list = new List<CompletionItemModel>();
                    order.Add(cat);
                }
                list.Add(item);
            }

            var rows = new List<Row>(_filteredItems.Length + order.Count);
            var starts = new List<int>(order.Count);
            foreach (var cat in order)
            {
                rows.Add(Row.HeaderRow(cat));
                _listBox.Items.Add(CreateHeaderVisual(cat));
                starts.Add(_listBox.Items.Count);   // index of the first item we are about to add
                foreach (var item in buckets[cat])
                {
                    int rowIndex = _listBox.Items.Count;
                    rows.Add(Row.ItemRow(item));
                    _listBox.Items.Add(CreateItemVisual(item, rowIndex));
                }
            }

            _rows = rows.ToArray();
            _categoryStarts = starts.ToArray();

            int first = FirstItemIndex();
            if (first >= 0)
            {
                _listBox.SelectedIndex = first;
                RaiseSelectionChanged();
            }
        }

        /// <summary>Non-selectable category header row (FR-014).</summary>
        private UIElement CreateHeaderVisual(string category)
        {
            var label = new TextBlock
            {
                Text              = category.ToUpperInvariant(),
                FontSize          = Typography.Small,
                FontWeight        = FontWeights.SemiBold,
                Margin            = new Thickness(Spacing.Sm, 2, Spacing.Sm, 1),
                VerticalAlignment = VerticalAlignment.Center
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);

            var grid = new Grid { Height = ItemHeight - 4, Background = Brushes.Transparent };
            grid.Children.Add(label);
            // Headers must not select on click — swallow so the ListBox keeps the current item row.
            grid.MouseLeftButtonDown += (s, e) => e.Handled = true;
            return grid;
        }

        /// <summary>FR-014 — the displayed label, with the owner (schema) prefix stripped when hidden.</summary>
        private static string DisplayLabelFor(CompletionItemModel item)
            => _showOwnerNames ? item.DisplayText : StripOwner(item.DisplayText, item.ObjectType);

        /// <summary>
        /// Strips a single leading "schema." owner prefix from an object's display name (Table/View/
        /// Function/Procedure only) when owner names are hidden. Multi-part names (db.schema.obj) and
        /// non-object items are left untouched.
        /// </summary>
        private static string StripOwner(string display, int objectType)
        {
            if (string.IsNullOrEmpty(display)) return display;
            // Only object kinds that the engine may owner-qualify.
            if (objectType is 0 or 1 or 5 or 6)
            {
                int dot = display.IndexOf('.');
                if (dot > 0 && dot < display.Length - 1 && display.IndexOf('.', dot + 1) < 0)
                    return display.Substring(dot + 1);
            }
            return display;
        }

        /// <summary>Footer affordance label reflecting the current owner-name visibility.</summary>
        private void UpdateOwnerToggleVisual()
        {
            if (_ownerToggle == null) return;
            _ownerToggle.Text = _showOwnerNames ? "owner: on" : "owner: off";
            _ownerToggle.SetResourceReference(TextBlock.ForegroundProperty,
                _showOwnerNames ? ThemeTokens.TextPrimary : ThemeTokens.TextSecondary);
        }

        private void RaiseSelectionChanged()
        {
            var item = GetSelectedItem();
            if (item != null)
                SelectionChanged?.Invoke(this, item);
        }

        private UIElement CreateItemVisual(CompletionItemModel item, int rowIndex)
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

            // Display text (owner/schema prefix optionally hidden — FR-014; InsertText is unaffected)
            var displayText = new TextBlock
            {
                Text              = DisplayLabelFor(item),
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
                if (rowIndex >= 0 && _listBox.SelectedIndex != rowIndex)
                {
                    _listBox.SelectedIndex = rowIndex;
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
