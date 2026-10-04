#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using AutomationProperties = System.Windows.Automation.AutomationProperties;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using AkmlSql.Shell.Shared.Ui;
using AkmlSql.Shell.Shared.Ui.SqlPreview;
using AkmlSql.Shell.Shared.Ui.Theme;
using Typography = AkmlSql.Shell.Shared.Ui.Theme.Typography;

namespace AkmlSql.Shell.Shared.History
{
    /// <summary>
    /// WPF UserControl that provides a SQL Prompt-style 2-region layout for the SQL History tool window.
    /// Built programmatically (no XAML) for Shell.Shared compatibility across all 6 targets.
    /// Layout: top search + "Recent queries" toolbar, then a 2-column body
    /// [LEFT master list + version sub-panel split by a horizontal splitter | vertical splitter |
    /// RIGHT code preview (dark header + syntax-highlighted preview + metadata/Open bar)],
    /// then a bottom status strip.
    /// Features: search bar with line-art icons, date-grouped virtualized query list, favorites toggle,
    /// version history, syntax-highlighted code preview with search highlighting, infinite scroll,
    /// context menus, and all action commands.
    /// </summary>
    internal class HistoryToolWindowControl : ThemeAwareUserControl
    {
        private readonly HistoryViewModel _viewModel;

        // Main panels
        private ListView? _queryListView;
        private ListBox? _versionListBox;
        private SqlPreviewView? _codePreview;

        // Spec 040 (HIS-04): drops a slower version list for an entry selected earlier.
        private readonly VersionLoadGuard _versionGuard = new VersionLoadGuard();
        private TextBlock? _codePreviewHeaderTimestamp;
        private TextBlock? _codePreviewHeaderFilename;
        private TextBlock? _metadataServerLabel;
        private TextBlock? _metadataDatabaseLabel;
        private TextBlock? _metadataVersionLabel;

        // LEFT bottom version sub-panel header — relabelled "History for <file>" on selection.
        private TextBlock? _versionPanelHeader;

        // Toolbar favorites star — visual state mirrors HistoryViewModel.FavoritesOnly.
        private Button? _favoritesStarButton;
        private TextBlock? _favoritesStarGlyph;

        // Toolbar open/closed folder toggles — mirror HistoryViewModel.IsOpenFilter
        // (SQL Prompt's two folder icons: open-queries-only / closed-queries-only).
        private Path? _openFilterGlyph;
        private Path? _closedFilterGlyph;

        // Status bar elements
        private TextBlock? _statusCountLabel;
        private Ellipse? _statusSpinner;
        private Button? _retryButton;
        private TextBlock? _versionsEmptyText;
        private TextBlock? _previewEmptyText;

        // Centered placeholder overlaid on the query list — distinguishes a pipe-down engine
        // ("History unavailable") from a genuinely empty result ("No queries found").
        private StackPanel? _emptyStateOverlay;
        private TextBlock? _emptyStateText;

        // Infinite-scroll guard — prevents duplicate LoadMore fires while one is in flight.
        private bool _loadMoreInFlight;

        public HistoryToolWindowControl() : this(new HistoryViewModel()) { }

        /// <summary>Spec 040: the view model is injectable, so keys and menus can be tested headlessly.</summary>
        internal HistoryToolWindowControl(HistoryViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            DataContext = _viewModel;

            // Wire ViewModel events to DTE actions
            _viewModel.OpenInNewTabRequested += OnOpenInNewTabRequested;
            _viewModel.ReExecuteRequested += OnReExecuteRequested;
            _viewModel.CompareRequested += OnCompareRequested;
            _viewModel.ActivateDocument = ActivateOpenDocument;
            _viewModel.PromptRename = current => ShowInputDialog("Rename query", "Name:", current);

            BuildUi();

            // Initialize the ViewModel after UI is built
            Loaded += OnLoaded;
            Unloaded += (_, __) => ExecutionCapture.HistoryRecorded -= OnHistoryRecorded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Spec 040 (HIS-09): refresh live when a run or draft reaches History.
            ExecutionCapture.HistoryRecorded -= OnHistoryRecorded;
            ExecutionCapture.HistoryRecorded += OnHistoryRecorded;
            _viewModel.InitializeAsync();
        }

        private void OnHistoryRecorded(long? entryId) =>
            Dispatcher.BeginInvoke(new Action(() => _viewModel.OnHistoryRecorded()));

        // ================================================================
        // Main UI Construction
        // ================================================================

        private void BuildUi()
        {
            // Main layout: Row 0 = search + "Recent queries" toolbar, Row 1 = 2-region body, Row 2 = status strip
            var mainGrid = new Grid();
            mainGrid.SetResourceReference(BackgroundProperty, ThemeTokens.SurfaceCanvas);
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });    // Search + toolbar
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 2-region body
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });    // Status strip

            // ROW 0: Search bar + "Recent queries" toolbar
            var topBar = BuildTopBar();
            Grid.SetRow(topBar, 0);
            mainGrid.Children.Add(topBar);

            // ROW 1: 2-region grid (left master+versions | splitter | right preview)
            var panelsGrid = BuildTwoRegionGrid();
            Grid.SetRow(panelsGrid, 1);
            mainGrid.Children.Add(panelsGrid);

            // ROW 2: Status strip
            var statusBar = BuildStatusBar();
            Grid.SetRow(statusBar, 2);
            mainGrid.Children.Add(statusBar);

            // Keep the toolbar favorites-star visual in sync with FavoritesOnly even when it is reset
            // elsewhere (e.g. ClearFiltersCommand).
            _viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(HistoryViewModel.FavoritesOnly))
                    UpdateFavoritesStarVisual();
                else if (args.PropertyName == nameof(HistoryViewModel.IsOpenFilter))
                    UpdateOpenFilterVisual();
                else if (args.PropertyName == nameof(HistoryViewModel.IsLoading)
                      || args.PropertyName == nameof(HistoryViewModel.IsDisconnected)
                      || args.PropertyName == nameof(HistoryViewModel.TotalCount))
                    UpdateEmptyState();
                else if (args.PropertyName == nameof(HistoryViewModel.SelectedEntry))
                    SyncListSelection();
            };
            _viewModel.Entries.CollectionChanged += (_, __) => UpdateEmptyState();

            Content = mainGrid;
        }

        // ================================================================
        // ROW 0: Search + "Recent queries" toolbar
        // ================================================================

        private StackPanel BuildTopBar()
        {
            var panel = new StackPanel
            {
                Margin = new Thickness(8, 8, 8, 4)
            };

            // ----- Search row: rounded border holding [magnifier path] [textbox] [clear X] -----
            var searchBorder = new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 0, 6)
            };
            searchBorder.SetResourceReference(Border.BackgroundProperty, ThemeTokens.SurfaceInput);
            searchBorder.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderDefault);

            var searchDock = new DockPanel();

            // Line-art magnifier (NOT emoji) \u2014 circle + handle drawn with a Path geometry.
            var magnifier = BuildMagnifierIcon();
            DockPanel.SetDock(magnifier, Dock.Left);
            searchDock.Children.Add(magnifier);

            // Clear "X" button (right-docked) \u2014 fires ClearFiltersCommand.
            var clearButton = new Button
            {
                Width = 22,
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Padding = new Thickness(0),
                ToolTip = "Clear search",
                FocusVisualStyle = FocusVisualStyles.HighStakes,
                Content = BuildClearIcon(),
                Template = BuildBareButtonTemplate()
            };
            AutomationProperties.SetName(clearButton, "Clear search");
            clearButton.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty,
                new Binding(nameof(HistoryViewModel.ClearFiltersCommand)));
            DockPanel.SetDock(clearButton, Dock.Right);
            searchDock.Children.Add(clearButton);

            var helpButton = BuildSearchHelpButton();
            DockPanel.SetDock(helpButton, Dock.Right);
            searchDock.Children.Add(helpButton);

            // Search TextBox (fills remaining space) with placeholder overlay.
            var searchBox = new TextBox
            {
                Background = Brushes.Transparent, // theme-independent: lets the parent Border's background show through
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4, 6, 4, 6),
                FontSize = Typography.Body,
                VerticalContentAlignment = VerticalAlignment.Center,
                FocusVisualStyle = FocusVisualStyles.HighStakes
            };
            searchBox.SetResourceReference(TextBox.ForegroundProperty, ThemeTokens.TextPrimary);
            searchBox.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, ThemeTokens.TextPrimary);
            searchBox.SetBinding(TextBox.TextProperty,
                new Binding(nameof(HistoryViewModel.SearchText))
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                });

            var placeholderText = new TextBlock
            {
                Text = "Search",
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
                FontSize = Typography.Body
            };
            placeholderText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPlaceholder);

            var searchGrid = new Grid();
            searchGrid.Children.Add(searchBox);
            searchGrid.Children.Add(placeholderText);

            void SyncPlaceholder() =>
                placeholderText.Visibility = string.IsNullOrEmpty(searchBox.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            searchBox.TextChanged += (_, __) => SyncPlaceholder();
            searchBox.GotFocus += (_, __) => placeholderText.Visibility = Visibility.Collapsed;
            searchBox.LostFocus += (_, __) => SyncPlaceholder();

            // Typing searches after a short pause (the view model); Enter searches at once.
            searchBox.PreviewKeyDown += (s, e) => { if (HandleSearchKey(e.Key)) e.Handled = true; };
            KeyboardNavigation.SetTabIndex(searchBox, 1); // spec 040 (HIS-11): search → list → versions → preview
            AutomationProperties.SetName(searchBox, "Search");
            _searchBox = searchBox;

            searchDock.Children.Add(searchGrid); // LastChildFill \u2014 takes remaining space
            searchBorder.Child = searchDock;
            panel.Children.Add(searchBorder);

            // Spec 040 (HIS-07): Advanced search, and the active filters as removable chips.
            panel.Children.Add(BuildAdvancedSearch());
            panel.Children.Add(BuildFilterChips());

            // ----- "Recent queries" toolbar row -----
            var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };

            var iconStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(iconStack, Dock.Right);

            // Refresh \u2014 re-run the current search.
            var refreshButton = CreateToolbarIconButton(BuildRefreshIcon(), "Refresh", (_, __) =>
            {
                if (_viewModel.SearchCommand.CanExecute(null))
                    _viewModel.SearchCommand.Execute(null);
            });
            iconStack.Children.Add(refreshButton);

            // Favorites star \u2014 toggles FavoritesOnly + re-runs search; shows active state.
            _favoritesStarGlyph = new TextBlock
            {
                Text = "\u2605",
                FontSize = Typography.H4,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _favoritesStarButton = CreateToolbarIconButton(_favoritesStarGlyph, "Show starred queries only", (_, __) =>
            {
                _viewModel.FavoritesOnly = !_viewModel.FavoritesOnly;
                if (_viewModel.SearchCommand.CanExecute(null))
                    _viewModel.SearchCommand.Execute(null);
            });
            iconStack.Children.Add(_favoritesStarButton);
            UpdateFavoritesStarVisual();

            // Open / closed query filter toggles \u2014 SQL Prompt's two folder icons. Each cycles
            // HistoryViewModel.IsOpenFilter (null \u2192 this state \u2192 null) and re-runs the search;
            // open and closed are mutually exclusive.
            _openFilterGlyph = BuildFolderIcon(open: true);
            var openFilterButton = CreateToolbarIconButton(_openFilterGlyph, "Show open queries only",
                (_, __) => _viewModel.ToggleOpenFilter(open: true));
            iconStack.Children.Add(openFilterButton);

            _closedFilterGlyph = BuildFolderIcon(open: false);
            var closedFilterButton = CreateToolbarIconButton(_closedFilterGlyph, "Show closed queries only",
                (_, __) => _viewModel.ToggleOpenFilter(open: false));
            iconStack.Children.Add(closedFilterButton);
            UpdateOpenFilterVisual();

            // Source/server menu \u2014 small dropdown over Servers / Databases.
            var sourceButton = CreateToolbarIconButton(BuildSourceIcon(), "Filter by server or database", null);
            sourceButton.ContextMenu = BuildSourceMenu();
            sourceButton.Click += (s, __) =>
            {
                if (sourceButton.ContextMenu != null)
                {
                    sourceButton.ContextMenu.PlacementTarget = sourceButton;
                    sourceButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                    sourceButton.ContextMenu.IsOpen = true;
                }
            };
            iconStack.Children.Add(sourceButton);

            // Spec 040 (HIS-12): the window's own menu — Export and Clear history live here, not on a row.
            var moreGlyph = new TextBlock { Text = "\u22EF", FontSize = Typography.H4, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            moreGlyph.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            var moreButton = CreateToolbarIconButton(moreGlyph, "More actions", null);
            var moreMenu = new ContextMenu();
            moreMenu.Items.Add(new MenuItem { Header = "Export\u2026", Command = _viewModel.ExportCommand });
            moreMenu.Items.Add(new MenuItem { Header = "Clear history\u2026", Command = _viewModel.ClearHistoryCommand });
            moreButton.ContextMenu = moreMenu;
            moreButton.Click += (_, __) =>
            {
                moreMenu.PlacementTarget = moreButton;
                moreMenu.Placement = PlacementMode.Bottom;
                moreMenu.IsOpen = true;
            };
            iconStack.Children.Add(moreButton);
            _toolbarMoreMenu = moreMenu;

            toolbar.Children.Add(iconStack);

            var heading = new TextBlock
            {
                Text = "Recent queries",
                FontWeight = FontWeights.SemiBold,
                FontSize = Typography.Body,
                VerticalAlignment = VerticalAlignment.Center
            };
            heading.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            toolbar.Children.Add(heading); // LastChildFill

            panel.Children.Add(toolbar);
            return panel;
        }

        private TextBox? _searchBox;
        private ContextMenu? _toolbarMoreMenu;
        private static readonly FontFamily MonoFont = new FontFamily("Consolas");

        /// <summary>The query rows' template, for tests.</summary>
        internal DataTemplate? QueryItemTemplate => _queryListView?.ItemTemplate;

        /// <summary>The toolbar's ⋯ menu headers, for tests.</summary>
        internal IReadOnlyList<string> ToolbarMenuHeaders =>
            _toolbarMoreMenu?.Items.OfType<MenuItem>().Select(i => i.Header as string ?? string.Empty).ToList() ?? new List<string>();

        /// <summary>
        /// Spec 040 (HIS-07/HIS-11) — the search box's keys: Enter searches now; Esc clears the text,
        /// and when it is already empty, the filters. True when handled.
        /// </summary>
        internal bool HandleSearchKey(Key key)
        {
            if (key == Key.Enter)
            {
                _viewModel.SearchNow();
                return true;
            }
            if (key != Key.Escape) return false;

            if (!string.IsNullOrEmpty(_viewModel.SearchText)) _viewModel.SearchText = string.Empty;
            else if (_viewModel.ClearFiltersCommand.CanExecute(null)) _viewModel.ClearFiltersCommand.Execute(null);
            return true;
        }

        /// <summary>
        /// Spec 040 (HIS-11) — the list's keys on the selected query: Enter opens, Delete removes
        /// (after asking), F2 renames, Ctrl+C copies the SQL, Space stars or un-stars. True when handled.
        /// </summary>
        internal bool HandleListKey(Key key, ModifierKeys modifiers)
        {
            var entry = _viewModel.SelectedEntry;
            if (entry == null) return false;

            ICommand? command = null;
            if (modifiers == ModifierKeys.None)
            {
                switch (key)
                {
                    case Key.Enter: command = _viewModel.OpenInNewTabCommand; break;
                    case Key.Delete: command = _viewModel.DeleteCommand; break;
                    case Key.F2: command = _viewModel.RenameCommand; break;
                    case Key.Space: command = _viewModel.ToggleFavoriteCommand; break;
                }
            }
            else if (modifiers == ModifierKeys.Control && key == Key.C)
            {
                command = _viewModel.CopySqlCommand;
            }
            if (command == null) return false;

            if (command.CanExecute(entry)) command.Execute(entry);
            else if (_viewModel.IsLoading) RunWhenLoaded(command);
            return true;
        }

        /// <summary>
        /// A key pressed while the list reloads (Space just starred a row, say) runs once the load
        /// ends, on the row then selected — it used to be dropped without a word.
        /// </summary>
        private void RunWhenLoaded(ICommand command)
        {
            PropertyChangedEventHandler? handler = null;
            handler = (_, e) =>
            {
                if (e.PropertyName != nameof(HistoryViewModel.IsLoading) || _viewModel.IsLoading) return;
                _viewModel.PropertyChanged -= handler;
                var current = _viewModel.SelectedEntry;
                if (current != null && command.CanExecute(current)) command.Execute(current);
            };
            _viewModel.PropertyChanged += handler;
        }

        /// <summary>
        /// F2 from the host's Rename command (SSMS turns the key into it before the list sees it):
        /// renames the selected query when the list has focus. True when handled.
        /// </summary>
        internal bool RenameSelectedFromKeyboard()
        {
            if (_queryListView == null || !_queryListView.IsKeyboardFocusWithin || _viewModel.SelectedEntry == null) return false;
            // Posted: this runs inside the host's key filtering, where the rename dialog's modal
            // loop can't start.
            Dispatcher.BeginInvoke(new Action(() => HandleListKey(Key.F2, ModifierKeys.None)));
            return true;
        }

        /// <summary>Spec 040 (HIS-07) — "?" beside the search box: every search form, with an example.</summary>
        private Button BuildSearchHelpButton()
        {
            var glyph = new TextBlock { Text = "?", FontSize = Typography.Body, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            glyph.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            var button = new Button
            {
                Width = 22,
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Padding = new Thickness(0),
                ToolTip = "Search syntax",
                FocusVisualStyle = FocusVisualStyles.HighStakes,
                Content = glyph,
                Template = BuildBareButtonTemplate()
            };
            AutomationProperties.SetName(button, "Search syntax");

            var rows = new Grid { Margin = new Thickness(10, 8, 10, 8) };
            rows.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var r = 0;
            foreach (var (syntax, meaning) in HistorySearchParser.HelpRows)
            {
                rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var s = new TextBlock { Text = syntax, FontFamily = MonoFont, Margin = new Thickness(0, 2, 14, 2) };
                s.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
                var m = new TextBlock { Text = meaning, TextWrapping = TextWrapping.Wrap, MaxWidth = 320, Margin = new Thickness(0, 2, 0, 2) };
                m.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
                Grid.SetRow(s, r); Grid.SetRow(m, r); Grid.SetColumn(m, 1);
                rows.Children.Add(s); rows.Children.Add(m);
                r++;
            }
            var card = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = rows };
            card.SetResourceReference(Border.BackgroundProperty, ThemeTokens.SurfaceElevated);
            card.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderDefault);
            var popup = new System.Windows.Controls.Primitives.Popup
            {
                Child = card,
                StaysOpen = false,
                PlacementTarget = button,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                AllowsTransparency = true,
            };
            button.Click += (_, __) => popup.IsOpen = !popup.IsOpen;
            return button;
        }

        /// <summary>
        /// Spec 040 (HIS-07) — "Advanced search" under the search box: Period (with dates for
        /// Custom), Server, Database, Starred, Open and Reset. Every change searches at once.
        /// </summary>
        private FrameworkElement BuildAdvancedSearch()
        {
            var advanced = _viewModel.AdvancedSearch;
            var host = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };

            var toggle = new TextBlock { Text = "\u25B8 Advanced search", FontSize = Typography.Small, Cursor = Cursors.Hand, Focusable = true };
            toggle.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextLink);
            AutomationProperties.SetName(toggle, "Advanced search");
            host.Children.Add(toggle);

            var body = new Grid { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < 5; i++) body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            host.Children.Add(body);

            var applying = false;
            void Apply()
            {
                if (applying) return;
                _ = _viewModel.ApplyAdvancedSearchAsync();
            }

            TextBlock Label(string text, int row)
            {
                var t = new TextBlock { Text = text, FontSize = Typography.Small, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 8, 2) };
                t.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
                Grid.SetRow(t, row);
                body.Children.Add(t);
                return t;
            }

            // Period
            Label("Period", 0);
            var period = new ComboBox { FontSize = Typography.Small, Margin = new Thickness(0, 2, 0, 2) };
            foreach (var (_, label) in HistoryAdvancedSearch.Periods) period.Items.Add(label);
            ComboBoxTheming.Apply(period);
            AutomationProperties.SetName(period, "Period");
            Grid.SetRow(period, 0); Grid.SetColumn(period, 1);
            body.Children.Add(period);

            // Custom dates
            var dates = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            var from = new DatePicker { FontSize = Typography.Small, Width = 110 };
            var to = new DatePicker { FontSize = Typography.Small, Width = 110, Margin = new Thickness(6, 0, 0, 0) };
            dates.Children.Add(from); dates.Children.Add(to);
            AutomationProperties.SetName(from, "From");
            AutomationProperties.SetName(to, "To");
            var datesLabel = Label("From / to", 1);
            Grid.SetRow(dates, 1); Grid.SetColumn(dates, 1);
            body.Children.Add(dates);

            // Server / Database
            Label("Server", 2);
            var server = new ComboBox { FontSize = Typography.Small, Margin = new Thickness(0, 2, 0, 2) };
            ComboBoxTheming.Apply(server);
            AutomationProperties.SetName(server, "Server");
            Grid.SetRow(server, 2); Grid.SetColumn(server, 1);
            body.Children.Add(server);
            Label("Database", 3);
            var database = new ComboBox { FontSize = Typography.Small, Margin = new Thickness(0, 2, 0, 2) };
            ComboBoxTheming.Apply(database);
            AutomationProperties.SetName(database, "Database");
            Grid.SetRow(database, 3); Grid.SetColumn(database, 1);
            body.Children.Add(database);

            // Starred / Open / Reset
            var flags = new DockPanel { Margin = new Thickness(0, 4, 0, 2) };
            var starred = new CheckBox { Content = "Starred", FontSize = Typography.Small, Margin = new Thickness(0, 0, 12, 0) };
            var openOnly = new CheckBox { Content = "Open", FontSize = Typography.Small };
            starred.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
            openOnly.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
            var reset = new Button { Content = "Reset", FontSize = Typography.Small, Padding = new Thickness(8, 1, 8, 1) };
            DockPanel.SetDock(reset, Dock.Right);
            flags.Children.Add(reset);
            flags.Children.Add(starred);
            flags.Children.Add(openOnly);
            Grid.SetRow(flags, 4); Grid.SetColumnSpan(flags, 2);
            body.Children.Add(flags);

            const string AllServers = "All servers", AllDatabases = "All databases";
            void FillLists()
            {
                applying = true;
                try
                {
                    server.Items.Clear(); server.Items.Add(AllServers);
                    foreach (var s in _viewModel.Servers) server.Items.Add(s);
                    database.Items.Clear(); database.Items.Add(AllDatabases);
                    foreach (var d in _viewModel.Databases) database.Items.Add(d);
                }
                finally { applying = false; }
                ShowState();
            }

            void ShowState()
            {
                applying = true;
                try
                {
                    var index = Array.FindIndex(HistoryAdvancedSearch.Periods, p => p.Value == advanced.Period);
                    period.SelectedIndex = index < 0 ? 0 : index;
                    var custom = advanced.IsCustom ? Visibility.Visible : Visibility.Collapsed;
                    dates.Visibility = custom; datesLabel.Visibility = custom;
                    from.SelectedDate = advanced.From; to.SelectedDate = advanced.To;
                    server.SelectedItem = advanced.Server ?? AllServers;
                    database.SelectedItem = advanced.Database ?? AllDatabases;
                    starred.IsChecked = advanced.Starred;
                    openOnly.IsChecked = advanced.OpenOnly;
                }
                finally { applying = false; }
            }

            period.SelectionChanged += (_, __) =>
            {
                if (applying || period.SelectedIndex < 0) return;
                advanced.Period = HistoryAdvancedSearch.Periods[period.SelectedIndex].Value;
                ShowState();
                if (!advanced.IsCustom) Apply();
            };
            from.SelectedDateChanged += (_, __) => { if (applying) return; advanced.From = from.SelectedDate; Apply(); };
            to.SelectedDateChanged += (_, __) => { if (applying) return; advanced.To = to.SelectedDate; Apply(); };
            server.SelectionChanged += (_, __) =>
            {
                if (applying) return;
                advanced.Server = server.SelectedItem as string == AllServers ? null : server.SelectedItem as string;
                Apply();
            };
            database.SelectionChanged += (_, __) =>
            {
                if (applying) return;
                advanced.Database = database.SelectedItem as string == AllDatabases ? null : database.SelectedItem as string;
                Apply();
            };
            starred.Click += (_, __) => { advanced.Starred = starred.IsChecked == true; Apply(); };
            openOnly.Click += (_, __) => { advanced.OpenOnly = openOnly.IsChecked == true; Apply(); };
            reset.Click += (_, __) => { advanced.Reset(); ShowState(); Apply(); };

            async void Toggle()
            {
                var opening = body.Visibility != Visibility.Visible;
                body.Visibility = opening ? Visibility.Visible : Visibility.Collapsed;
                toggle.Text = (opening ? "\u25BE" : "\u25B8") + " Advanced search";
                if (!opening) return;
                try { await _viewModel.LoadFilterValuesAsync(); }
                catch (Exception ex) { Serilog.Log.Debug(ex, "History: filter values failed"); }
                FillLists();
            }
            toggle.MouseLeftButtonUp += (_, __) => Toggle();
            toggle.KeyDown += (_, e) => { if (e.Key == Key.Enter || e.Key == Key.Space) { Toggle(); e.Handled = true; } };

            // Chips removed elsewhere (or a remembered state) show here too.
            _viewModel.ActiveFilterChips.CollectionChanged += (_, __) => { if (body.Visibility == Visibility.Visible) ShowState(); };
            return host;
        }

        /// <summary>Spec 040 (HIS-07) — the active filters, each with a remove button.</summary>
        private FrameworkElement BuildFilterChips()
        {
            var panel = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            void Rebuild()
            {
                panel.Children.Clear();
                foreach (var chip in _viewModel.ActiveFilterChips)
                {
                    var text = new TextBlock { Text = chip.Label, FontSize = Typography.Small, VerticalAlignment = VerticalAlignment.Center };
                    text.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
                    var remove = new Button
                    {
                        Content = "\u00D7",
                        FontSize = Typography.Small,
                        Padding = new Thickness(4, 0, 2, 0),
                        BorderThickness = new Thickness(0),
                        Background = Brushes.Transparent,
                        Cursor = Cursors.Hand,
                        Command = chip.Remove,
                        CommandParameter = chip,
                        Template = BuildBareButtonTemplate(),
                        ToolTip = "Remove this filter",
                    };
                    remove.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextSecondary);
                    AutomationProperties.SetName(remove, "Remove filter " + chip.Label);
                    var pill = new Border
                    {
                        CornerRadius = new CornerRadius(9),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(8, 1, 4, 1),
                        Margin = new Thickness(0, 0, 4, 2),
                        Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { text, remove } },
                    };
                    pill.SetResourceReference(Border.BackgroundProperty, ThemeTokens.SurfaceSelection);
                    pill.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderSubtle);
                    panel.Children.Add(pill);
                }
                panel.Visibility = panel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            _viewModel.ActiveFilterChips.CollectionChanged += (_, __) => Rebuild();
            Rebuild();
            return panel;
        }

        /// <summary>Reflects <see cref="HistoryViewModel.FavoritesOnly"/> onto the toolbar star colour.</summary>
        private void UpdateFavoritesStarVisual()
        {
            if (_favoritesStarGlyph == null) return;
            _favoritesStarGlyph.SetResourceReference(TextBlock.ForegroundProperty,
                _viewModel.FavoritesOnly ? ThemeTokens.StatusWarning : ThemeTokens.TextSecondary);
        }

        /// <summary>
        /// Reflects <see cref="HistoryViewModel.IsOpenFilter"/> onto the two folder-toggle colours:
        /// the active state (open or closed) is drawn in the accent colour, the rest in the muted
        /// secondary colour. Kept in sync via the ViewModel's PropertyChanged (so a ClearFilters reset
        /// also clears the highlight).
        /// </summary>
        private void UpdateOpenFilterVisual()
        {
            _openFilterGlyph?.SetResourceReference(Shape.StrokeProperty,
                _viewModel.IsOpenFilter == true ? ThemeTokens.AccentPrimary : ThemeTokens.TextSecondary);
            _closedFilterGlyph?.SetResourceReference(Shape.StrokeProperty,
                _viewModel.IsOpenFilter == false ? ThemeTokens.AccentPrimary : ThemeTokens.TextSecondary);
        }

        /// <summary>Builds the Servers / Databases dropdown for the toolbar source/server button.</summary>
        private ContextMenu BuildSourceMenu()
        {
            var menu = new ContextMenu();

            var allItem = new MenuItem { Header = "All servers / databases" };
            allItem.Click += (_, __) =>
            {
                _viewModel.SelectedServer = null;
                _viewModel.SelectedDatabase = null;
                if (_viewModel.SearchCommand.CanExecute(null))
                    _viewModel.SearchCommand.Execute(null);
            };
            menu.Items.Add(allItem);

            // Populate Servers / Databases lazily each time it opens (collections refresh after search).
            menu.Opened += (_, __) =>
            {
                // Remove everything after the static "All" item before repopulating.
                while (menu.Items.Count > 1)
                    menu.Items.RemoveAt(menu.Items.Count - 1);

                _viewModel.RefreshDropdownsFromEntries();

                if (_viewModel.Servers.Count > 0)
                {
                    menu.Items.Add(new Separator());
                    var serversHeader = new MenuItem { Header = "Servers", IsEnabled = false };
                    menu.Items.Add(serversHeader);
                    foreach (var server in _viewModel.Servers)
                    {
                        var capturedServer = server;
                        var item = new MenuItem
                        {
                            Header = server,
                            IsCheckable = true,
                            IsChecked = string.Equals(_viewModel.SelectedServer, server, StringComparison.OrdinalIgnoreCase)
                        };
                        item.Click += (_, ___) =>
                        {
                            _viewModel.SelectedServer = capturedServer;
                            if (_viewModel.SearchCommand.CanExecute(null))
                                _viewModel.SearchCommand.Execute(null);
                        };
                        menu.Items.Add(item);
                    }
                }

                if (_viewModel.Databases.Count > 0)
                {
                    menu.Items.Add(new Separator());
                    var dbHeader = new MenuItem { Header = "Databases", IsEnabled = false };
                    menu.Items.Add(dbHeader);
                    foreach (var db in _viewModel.Databases)
                    {
                        var capturedDb = db;
                        var item = new MenuItem
                        {
                            Header = db,
                            IsCheckable = true,
                            IsChecked = string.Equals(_viewModel.SelectedDatabase, db, StringComparison.OrdinalIgnoreCase)
                        };
                        item.Click += (_, ___) =>
                        {
                            _viewModel.SelectedDatabase = capturedDb;
                            if (_viewModel.SearchCommand.CanExecute(null))
                                _viewModel.SearchCommand.Execute(null);
                        };
                        menu.Items.Add(item);
                    }
                }
            };

            return menu;
        }

        /// <summary>Creates a flat, theme-aware toolbar icon button with a bare (chromeless) template.</summary>
        private Button CreateToolbarIconButton(UIElement content, string toolTip, RoutedEventHandler? onClick)
        {
            var button = new Button
            {
                Content = content,
                Width = 26,
                Height = 24,
                Margin = new Thickness(2, 0, 0, 0),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                ToolTip = toolTip,
                FocusVisualStyle = FocusVisualStyles.HighStakes,
                Template = BuildBareButtonTemplate()
            };
            if (onClick != null) button.Click += onClick;
            AutomationProperties.SetName(button, toolTip); // spec 040 (T185): icon-only controls are named
            return button;
        }

        /// <summary>
        /// Bare button template: a rounded <see cref="Border"/> (TemplateBinding background / hover) wrapping
        /// the content. Used for chromeless toolbar / search-clear buttons.
        /// </summary>
        private static ControlTemplate BuildBareButtonTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);

            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };

            // Hover highlight via SurfaceHover.
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty,
                new DynamicResourceExtension(ThemeTokens.SurfaceHover), "Bd"));
            template.Triggers.Add(hover);

            return template;
        }

        // --- Line-art icon builders (theme-aware Path strokes; no emoji) ---

        private static Path BuildMagnifierIcon()
        {
            var geo = Geometry.Parse("M 5,5 A 4,4 0 1 0 5.01,5 M 8,8 L 11.5,11.5");
            var path = new Path
            {
                Data = geo,
                StrokeThickness = 1.4,
                Stretch = Stretch.None,
                Width = 16,
                Height = 18,
                Margin = new Thickness(8, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            path.SetResourceReference(Shape.StrokeProperty, ThemeTokens.TextSecondary);
            return path;
        }

        private static Path BuildClearIcon()
        {
            var geo = Geometry.Parse("M 3,3 L 9,9 M 9,3 L 3,9");
            var path = new Path
            {
                Data = geo,
                StrokeThickness = 1.4,
                Stretch = Stretch.None,
                Width = 12,
                Height = 12,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            path.SetResourceReference(Shape.StrokeProperty, ThemeTokens.TextSecondary);
            return path;
        }

        private static Path BuildRefreshIcon()
        {
            // Circular arrow: ~300\u00B0 arc with a small arrowhead.
            var geo = Geometry.Parse(
                "M 11,6 A 5,5 0 1 1 8.5,1.7 M 8.5,1.7 L 6.2,1.2 M 8.5,1.7 L 8.9,4.1");
            var path = new Path
            {
                Data = geo,
                StrokeThickness = 1.4,
                Stretch = Stretch.None,
                Width = 14,
                Height = 14,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            path.SetResourceReference(Shape.StrokeProperty, ThemeTokens.TextSecondary);
            return path;
        }

        private static Canvas BuildSourceIcon()
        {
            // Stacked-cylinder (database) glyph drawn with two ellipses + side strokes.
            var canvas = new Canvas { Width = 14, Height = 14 };

            void AddEllipse(double top)
            {
                var e = new System.Windows.Shapes.Ellipse { Width = 10, Height = 3.4, StrokeThickness = 1.2 };
                e.SetResourceReference(Shape.StrokeProperty, ThemeTokens.TextSecondary);
                Canvas.SetLeft(e, 2);
                Canvas.SetTop(e, top);
                canvas.Children.Add(e);
            }
            AddEllipse(2);
            AddEllipse(8);

            void AddSide(double x)
            {
                var line = new System.Windows.Shapes.Line { X1 = x, Y1 = 3.7, X2 = x, Y2 = 9.7, StrokeThickness = 1.2 };
                line.SetResourceReference(Shape.StrokeProperty, ThemeTokens.TextSecondary);
                canvas.Children.Add(line);
            }
            AddSide(2);
            AddSide(12);

            return canvas;
        }

        /// <summary>
        /// Line-art folder glyph for the open/closed query filter toggles. <paramref name="open"/>
        /// draws an open folder (tab + splayed front panel); otherwise a plain closed folder.
        /// Stroke colour is theme-driven and recoloured to the accent when the toggle is active
        /// (see <see cref="UpdateOpenFilterVisual"/>).
        /// </summary>
        private static Path BuildFolderIcon(bool open)
        {
            var data = open
                ? "M 2,10.5 L 2,4 L 5,4 L 6.5,5.5 L 12,5.5 M 2,10.5 L 13.5,10.5 L 15,6 L 3.5,6 Z"
                : "M 1.5,3.5 L 5,3.5 L 6.5,5 L 12.5,5 L 12.5,10.5 L 1.5,10.5 Z";
            var path = new Path
            {
                Data = Geometry.Parse(data),
                StrokeThickness = 1.3,
                Stretch = Stretch.None,
                Width = 17,
                Height = 14,
                VerticalAlignment = VerticalAlignment.Center,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            path.SetResourceReference(Shape.StrokeProperty, ThemeTokens.TextSecondary);
            return path;
        }

        // ================================================================
        // ROW 1: Two-region grid (left master+versions | splitter | right preview)
        // ================================================================

        private Grid BuildTwoRegionGrid()
        {
            var grid = new Grid();

            // Column definitions: left ~42% | splitter | right ~58%
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42, GridUnitType.Star), MinWidth = 220 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // splitter
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58, GridUnitType.Star), MinWidth = 240 });

            // LEFT region: master list (top) + version sub-panel (bottom), split by a horizontal splitter.
            var leftRegion = BuildLeftRegion();
            Grid.SetColumn(leftRegion, 0);
            grid.Children.Add(leftRegion);

            // Vertical splitter between left and right.
            var splitter = BuildSplitter(column: 1);
            grid.Children.Add(splitter);

            // RIGHT region: code preview.
            var rightPanel = BuildCodePreviewPanel();
            Grid.SetColumn(rightPanel, 2);
            grid.Children.Add(rightPanel);

            return grid;
        }

        /// <summary>
        /// LEFT region: a 2-row grid \u2014 top master list, a horizontal GridSplitter, bottom version sub-panel.
        /// </summary>
        private Grid BuildLeftRegion()
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62, GridUnitType.Star), MinHeight = 120 }); // master list
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // horizontal splitter
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38, GridUnitType.Star), MinHeight = 80 }); // versions

            var masterPanel = BuildQueryListPanel();
            Grid.SetRow(masterPanel, 0);
            grid.Children.Add(masterPanel);

            var hSplitter = new GridSplitter
            {
                Height = 3,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                ResizeDirection = GridResizeDirection.Rows,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext
            };
            hSplitter.SetResourceReference(GridSplitter.BackgroundProperty, ThemeTokens.BorderSplitter);
            Grid.SetRow(hSplitter, 1);
            grid.Children.Add(hSplitter);

            var versionPanel = BuildVersionHistoryPanel();
            Grid.SetRow(versionPanel, 2);
            grid.Children.Add(versionPanel);

            return grid;
        }

        private static GridSplitter BuildSplitter(int column)
        {
            var splitter = new GridSplitter
            {
                Width = 3,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext
            };
            splitter.SetResourceReference(GridSplitter.BackgroundProperty, ThemeTokens.BorderSplitter);
            Grid.SetColumn(splitter, column);
            return splitter;
        }

        // ================================================================
        // Left Panel: Query List
        // ================================================================

        private DockPanel BuildQueryListPanel()
        {
            var dock = new DockPanel();
            dock.SetResourceReference(DockPanel.BackgroundProperty, ThemeTokens.SurfacePanel);

            // Query ListView (date-grouped, virtualized).
            _queryListView = new ListView
            {
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0),
                SelectionMode = SelectionMode.Extended,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            _queryListView.SetResourceReference(ListView.BackgroundProperty, ThemeTokens.SurfacePanel);
            _queryListView.SetResourceReference(ListView.ForegroundProperty, ThemeTokens.TextPrimary);

            // Virtualization \u2014 grouping disables it unless IsVirtualizingWhenGrouping=true + ScrollUnit=Item.
            VirtualizingPanel.SetIsVirtualizing(_queryListView, true);
            VirtualizingPanel.SetVirtualizationMode(_queryListView, VirtualizationMode.Recycling);
            VirtualizingPanel.SetIsVirtualizingWhenGrouping(_queryListView, true);
            VirtualizingPanel.SetScrollUnit(_queryListView, ScrollUnit.Item);
            ScrollViewer.SetCanContentScroll(_queryListView, true);
            // Rows take the pane's width and trim, as in SQL Prompt: no sideways scrolling, and the
            // time/runs text can no longer run into the server name.
            ScrollViewer.SetHorizontalScrollBarVisibility(_queryListView, ScrollBarVisibility.Disabled);

            // Bind ItemsSource through a CollectionViewSource that groups by date bucket.
            var cvs = new CollectionViewSource { Source = _viewModel.Entries };
            cvs.GroupDescriptions.Add(
                new PropertyGroupDescription(nameof(HistoryEntryDto.ExecutedAt), new HistoryDateGroupConverter()));
            _queryListView.ItemsSource = cvs.View;

            // Item template: 2-line row per entry.
            _queryListView.ItemTemplate = CreateQueryItemTemplate();

            // ItemContainerStyle: selected item accent (3px left border).
            _queryListView.ItemContainerStyle = CreateQueryItemContainerStyle();

            // GroupStyle: collapsible chevron + bucket name header.
            _queryListView.GroupStyle.Add(CreateDateGroupStyle());

            // Spec 040 (HIS-12): the row menu, filled for the row it opens on.
            _queryListView.ContextMenu = _rowMenu;
            _queryListView.ContextMenuOpening += OnListContextMenuOpening;

            // Events \u2014 the selection chain (preview drive + metadata + version load) is preserved here.
            _queryListView.MouseDoubleClick += OnListViewDoubleClick;
            _queryListView.SelectionChanged += OnListViewSelectionChanged;
            _queryListView.PreviewKeyDown += (_, e) =>
            {
                if (Keyboard.FocusedElement is TextBox) return; // not while typing in the list (none today)
                if (HandleListKey(e.Key, Keyboard.Modifiers)) e.Handled = true;
            };
            // Spec 040 (HIS-11): starring or removing with the keyboard refreshes the list, which
            // takes the focused row away and drops focus onto the window, so the next key (F2,
            // Delete…) went nowhere. Focus goes back to the selected row once the new rows are in.
            _queryListView.IsKeyboardFocusWithinChanged += (_, e) =>
            {
                if ((bool)e.NewValue || !FocusFellBack(Keyboard.FocusedElement as DependencyObject, this)) return;
                Dispatcher.BeginInvoke(new Action(RestoreListFocus), System.Windows.Threading.DispatcherPriority.ContextIdle);
            };
            KeyboardNavigation.SetTabIndex(_queryListView, 2);
            AutomationProperties.SetName(_queryListView, "Queries");

            // Infinite scroll \u2014 load more when scrolled near the bottom.
            _queryListView.AddHandler(ScrollViewer.ScrollChangedEvent,
                new ScrollChangedEventHandler(OnQueryListScrollChanged));

            // Spec 040 (HIS-13): with rows still shown, a banner above them says the engine is gone.
            dock.Children.Add(BuildDisconnectedBanner());

            // Overlay a centered empty/disconnected placeholder on top of the list so a pipe-down
            // engine or a no-results search reads clearly instead of a silent blank list.
            var listGrid = new Grid();
            listGrid.Children.Add(_queryListView);
            listGrid.Children.Add(BuildEmptyStateOverlay());
            dock.Children.Add(listGrid);

            UpdateEmptyState();
            return dock;
        }

        private Border? _disconnectedBanner;

        /// <summary>
        /// Spec 040 (HIS-13): "History is unavailable…" with Retry, above rows that are still shown
        /// (the centered overlay covers an empty list instead).
        /// </summary>
        private FrameworkElement BuildDisconnectedBanner()
        {
            var text = new TextBlock
            {
                Text = "History is unavailable \u2014 the AKML engine isn't connected.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = Typography.Small,
                VerticalAlignment = VerticalAlignment.Center
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            var retry = new Button
            {
                Content = "Retry",
                Padding = new Thickness(10, 1, 10, 1),
                Margin = new Thickness(8, 0, 0, 0),
                FocusVisualStyle = FocusVisualStyles.HighStakes,
                Command = _viewModel.RetryCommand
            };
            DockPanel.SetDock(retry, Dock.Right);
            var row = new DockPanel();
            row.Children.Add(retry);
            row.Children.Add(text);
            _disconnectedBanner = new Border
            {
                Padding = new Thickness(8, 4, 8, 4),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = row,
                Visibility = Visibility.Collapsed
            };
            _disconnectedBanner.SetResourceReference(Border.BackgroundProperty, ThemeTokens.SurfaceElevated);
            _disconnectedBanner.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.StatusWarning);
            DockPanel.SetDock(_disconnectedBanner, Dock.Top);
            return _disconnectedBanner;
        }

        /// <summary>Builds the centered, non-interactive placeholder shown over an empty query list.</summary>
        private FrameworkElement BuildEmptyStateOverlay()
        {
            _emptyStateText = new TextBlock
            {
                Text = "No queries found.",
                FontSize = Typography.Body,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            _emptyStateText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);

            _emptyStateOverlay = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 240,
                Margin = new Thickness(16),
                Visibility = Visibility.Collapsed
            };
            _emptyStateOverlay.Children.Add(_emptyStateText);

            // Spec 040 (HIS-13): Retry while the engine is down (it also reloads by itself when it returns).
            _retryButton = new Button
            {
                Content = "Retry",
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(12, 2, 12, 2),
                HorizontalAlignment = HorizontalAlignment.Center,
                FocusVisualStyle = FocusVisualStyles.HighStakes,
                Command = _viewModel.RetryCommand,
                Visibility = Visibility.Collapsed
            };
            _emptyStateOverlay.Children.Add(_retryButton);
            return _emptyStateOverlay;
        }

        /// <summary>
        /// Pure decision for the centered placeholder over the query list (extracted for tests):
        /// whether to show it and which message. See <see cref="UpdateEmptyState"/>.
        /// </summary>
        internal static bool ShouldShowEmptyOverlay(bool isLoading, bool isDisconnected, int entryCount, out string message)
        {
            message = string.Empty;
            if (isLoading) return false;

            // Never draw the overlay on top of visible rows — when the engine dies after a
            // successful load, the stale list stays readable (PR #248 review finding #6).
            if (entryCount > 0) return false;

            message = isDisconnected
                ? "History is unavailable \u2014 the AKML engine isn't connected."
                : "No queries found.";
            return true;
        }

        /// <summary>
        /// Shows the centered placeholder over the query list: a distinct "engine not connected"
        /// message when the pipe is down, or "No queries found" for a genuinely empty result.
        /// Hidden while a search is loading or when the list has entries.
        /// </summary>
        private void UpdateEmptyState()
        {
            if (_emptyStateOverlay == null || _emptyStateText == null) return;

            var show = ShouldShowEmptyOverlay(
                _viewModel.IsLoading, _viewModel.IsDisconnected, _viewModel.Entries.Count, out var message);
            if (show) _emptyStateText.Text = message;
            _emptyStateOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (_retryButton != null)
                _retryButton.Visibility = show && _viewModel.IsDisconnected ? Visibility.Visible : Visibility.Collapsed;
            if (_disconnectedBanner != null)
                _disconnectedBanner.Visibility = _viewModel.IsDisconnected && !show && !_viewModel.IsLoading
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// GroupStyle for the date-bucketed master list: a collapsible chevron ToggleButton + bucket-name
        /// header, with the group's items presenter below. Toggling the chevron collapses/expands the rows.
        /// Implemented via a <see cref="GroupItem"/> ContainerStyle so the chevron can drive the
        /// <see cref="ItemsPresenter"/> visibility (a HeaderTemplate alone can't reach the presenter).
        /// Virtualization is preserved: the ListView keeps IsVirtualizingWhenGrouping=true + ScrollUnit=Item
        /// and WPF's default group panel is a VirtualizingStackPanel.
        /// </summary>
        private static GroupStyle CreateDateGroupStyle()
        {
            // ----- GroupItem template: [chevron + name] (Top) over an ItemsPresenter -----
            var rootPanel = new FrameworkElementFactory(typeof(StackPanel));

            // Header row.
            var headerDock = new FrameworkElementFactory(typeof(DockPanel));
            headerDock.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 6, 6, 2));

            var chevron = new FrameworkElementFactory(typeof(ToggleButton));
            chevron.Name = "GroupChevron";
            chevron.SetValue(ToggleButton.IsCheckedProperty, true); // expanded by default
            chevron.SetValue(FrameworkElement.CursorProperty, Cursors.Hand);
            chevron.SetValue(Control.BackgroundProperty, Brushes.Transparent);
            chevron.SetValue(Control.BorderThicknessProperty, new Thickness(0));
            chevron.SetValue(FrameworkElement.WidthProperty, 16.0);
            chevron.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            chevron.SetValue(DockPanel.DockProperty, Dock.Left);
            chevron.SetValue(ToggleButton.TemplateProperty, BuildChevronTemplate());
            headerDock.AppendChild(chevron);

            var name = new FrameworkElementFactory(typeof(TextBlock));
            // DataContext is the CollectionViewGroup. Spec 040 (HIS-05): "Today (12)".
            var header = new MultiBinding { StringFormat = "{0} ({1})" };
            header.Bindings.Add(new Binding("Name"));
            header.Bindings.Add(new Binding("ItemCount"));
            name.SetBinding(TextBlock.TextProperty, header);
            name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            name.SetValue(TextBlock.FontSizeProperty, Typography.Small);
            name.SetResourceBinding(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            name.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            name.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 0, 0, 0));
            name.SetValue(FrameworkElement.CursorProperty, Cursors.Hand);
            headerDock.AppendChild(name);

            rootPanel.AppendChild(headerDock);

            // Items presenter — visibility bound to the chevron's IsChecked (collapse/expand the rows).
            var itemsPresenter = new FrameworkElementFactory(typeof(ItemsPresenter));
            itemsPresenter.SetBinding(UIElement.VisibilityProperty, new Binding("IsChecked")
            {
                ElementName = "GroupChevron",
                Converter = new BoolToVisibilityConverter()
            });
            rootPanel.AppendChild(itemsPresenter);

            var containerTemplate = new ControlTemplate(typeof(GroupItem)) { VisualTree = rootPanel };

            var containerStyle = new Style(typeof(GroupItem));
            containerStyle.Setters.Add(new Setter(Control.TemplateProperty, containerTemplate));

            // A screen reader announces the group as it is shown ("Today (12)"), not just "Today".
            var groupName = new MultiBinding { StringFormat = "{0} ({1})" };
            groupName.Bindings.Add(new Binding("Name"));
            groupName.Bindings.Add(new Binding("ItemCount"));
            containerStyle.Setters.Add(new Setter(AutomationProperties.NameProperty, groupName));

            return new GroupStyle
            {
                ContainerStyle = containerStyle
            };
        }

        /// <summary>A rotating-triangle chevron template for the group ToggleButton.</summary>
        private static ControlTemplate BuildChevronTemplate()
        {
            var arrow = new FrameworkElementFactory(typeof(Path));
            arrow.Name = "Arrow";
            // Down-pointing triangle (expanded); rotated -90\u00B0 when collapsed.
            arrow.SetValue(Path.DataProperty, Geometry.Parse("M 0,0 L 8,0 L 4,5 Z"));
            arrow.SetResourceBinding(Shape.FillProperty, ThemeTokens.TextSecondary);
            arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrow.SetValue(FrameworkElement.RenderTransformOriginProperty, new Point(0.5, 0.5));
            arrow.SetValue(FrameworkElement.RenderTransformProperty, new RotateTransform(0));

            var template = new ControlTemplate(typeof(ToggleButton)) { VisualTree = arrow };

            // Collapsed (IsChecked=false): rotate the arrow to point right.
            var collapsed = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = false };
            collapsed.Setters.Add(new Setter(FrameworkElement.RenderTransformProperty,
                new RotateTransform(-90), "Arrow"));
            template.Triggers.Add(collapsed);

            return template;
        }

        private DataTemplate CreateQueryItemTemplate()
        {
            var template = new DataTemplate(typeof(HistoryEntryDto));

            // Root: DockPanel \u2014 far-left star toggle, far-right overflow, center two-line content.
            var outerDock = new FrameworkElementFactory(typeof(DockPanel));
            outerDock.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 4, 4, 4));

            // Spec 040 (HIS-02, FR-011): a 3 px accent bar marks a query that is open in a tab (it
            // replaces the red/green dot, which read "closed" as an error).
            var openBar = new FrameworkElementFactory(typeof(Border));
            openBar.SetValue(FrameworkElement.WidthProperty, 3.0);
            openBar.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            openBar.SetResourceBinding(Border.BackgroundProperty, ThemeTokens.AccentPrimary);
            openBar.SetValue(DockPanel.DockProperty, Dock.Left);
            openBar.SetBinding(VisibilityProperty,
                new Binding(nameof(HistoryEntryDto.IsOpen)) { Converter = new BoolToHiddenConverter() });
            openBar.SetValue(ToolTipProperty, "Open in a tab");
            outerDock.AppendChild(openBar);

            // Far-left star (spec 040, HIS-11): a focusable button, named for screen readers.
            var starButton = RowButtonFactory("Star query", Dock.Left, new Thickness(0, 0, 8, 0));
            starButton.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnRowStarClick));
            var starText = new FrameworkElementFactory(typeof(TextBlock));
            starText.SetBinding(TextBlock.TextProperty,
                new Binding(nameof(HistoryEntryDto.IsFavorite))
                {
                    Converter = new FavoriteIconConverter()
                });
            // Spec 040 (T185): the star's colour follows theme changes — resource references
            // switched by a trigger, not a converter that copied the brush once.
            var starStyle = new Style(typeof(TextBlock));
            starStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension(ThemeTokens.HistoryStarInactive)));
            var starredTrigger = new DataTrigger { Binding = new Binding(nameof(HistoryEntryDto.IsFavorite)), Value = true };
            starredTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension(ThemeTokens.HistoryStarActive)));
            starStyle.Triggers.Add(starredTrigger);
            starText.SetValue(FrameworkElement.StyleProperty, starStyle);
            starText.SetValue(TextBlock.FontSizeProperty, Typography.H4);
            starButton.AppendChild(starText);
            outerDock.AppendChild(starButton);

            // Far-right "\u22EE" (spec 040, HIS-12): the row menu, shown while the row is hovered or has focus.
            var overflowButton = RowButtonFactory("Query actions", Dock.Right, new Thickness(4, 0, 0, 0));
            overflowButton.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnOverflowClick));
            var overflowShown = new MultiBinding { Converter = new AnyTrueToVisibleConverter() };
            overflowShown.Bindings.Add(new Binding(nameof(IsMouseOver)) { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListViewItem), 1) });
            overflowShown.Bindings.Add(new Binding(nameof(IsKeyboardFocusWithin)) { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListViewItem), 1) });
            overflowButton.SetBinding(VisibilityProperty, overflowShown);
            var overflowText = new FrameworkElementFactory(typeof(TextBlock));
            overflowText.SetValue(TextBlock.TextProperty, "\u22EE");
            overflowText.SetResourceBinding(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            overflowText.SetValue(TextBlock.FontSizeProperty, Typography.H4);
            overflowButton.AppendChild(overflowText);
            outerDock.AppendChild(overflowButton);

            // Center: two-line content stack (fills via LastChildFill).
            var contentStack = new FrameworkElementFactory(typeof(StackPanel));

            // Line 1: filename (QueryNameConverter), bold.
            var nameText = new FrameworkElementFactory(typeof(TextBlock));
            nameText.SetBinding(TextBlock.TextProperty,
                new Binding { Converter = new QueryNameConverter() });
            nameText.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            nameText.SetValue(TextBlock.FontSizeProperty, Typography.Body);
            nameText.SetResourceBinding(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            nameText.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            nameText.SetValue(TextBlock.MaxHeightProperty, 18.0);
            nameText.SetValue(ToolTipProperty, "Right-click \u2192 Rename to give this query a custom name");
            contentStack.AppendChild(nameText);

            // Line 2: DockPanel \u2014 left: relative time \u00B7 exec count; right: \u25CF server\instance.
            var line2 = new FrameworkElementFactory(typeof(DockPanel));
            line2.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 0));

            // Right side: "server · database", then the environment badge (spec 040, HIS-05). With
            // Dock.Right the FIRST child added docks rightmost, so the badge goes in first.
            var badge = new FrameworkElementFactory(typeof(Border));
            badge.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            badge.SetValue(Border.PaddingProperty, new Thickness(4, 0, 4, 0));
            badge.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 0, 0));
            badge.SetValue(DockPanel.DockProperty, Dock.Right);
            badge.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            badge.SetBinding(Border.BackgroundProperty, EnvironmentBadgeBinding(EnvironmentBadgeConverter.Part.Background));
            badge.SetBinding(VisibilityProperty, EnvironmentBadgeBinding(EnvironmentBadgeConverter.Part.Visibility));
            var badgeText = new FrameworkElementFactory(typeof(TextBlock));
            badgeText.SetValue(TextBlock.FontSizeProperty, Typography.Small);
            badgeText.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            badgeText.SetBinding(TextBlock.TextProperty, EnvironmentBadgeBinding(EnvironmentBadgeConverter.Part.Label));
            badgeText.SetBinding(TextBlock.ForegroundProperty, EnvironmentBadgeBinding(EnvironmentBadgeConverter.Part.Foreground));
            badge.AppendChild(badgeText);
            line2.AppendChild(badge);

            var connText = new FrameworkElementFactory(typeof(TextBlock));
            connText.SetBinding(TextBlock.TextProperty, new MultiBinding
            {
                Converter = new ServerLabelConverter(),
                Bindings =
                {
                    new Binding(nameof(HistoryEntryDto.Server)),
                    new Binding(nameof(HistoryEntryDto.Database))
                }
            });
            connText.SetValue(TextBlock.FontSizeProperty, Typography.Small);
            connText.SetResourceBinding(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            connText.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            connText.SetValue(DockPanel.DockProperty, Dock.Right);
            connText.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            connText.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));
            line2.AppendChild(connText);


            // Left (fills): relative time + " \u00B7 " + "\u00D7N \u00B7 M versions" meta (HistoryRowDisplay.MetaFor;
            // separator + meta both hidden when the meta line is empty \u2014 see MetaVisibilityConverter).
            // A DockPanel, not a StackPanel: the meta gets the width that is left and trims, where a
            // StackPanel let it spill under the server name ("7 versions(local) \u00B7 Northwind").
            var leftMeta = new FrameworkElementFactory(typeof(DockPanel));
            leftMeta.SetValue(UIElement.ClipToBoundsProperty, true);

            var timeText = new FrameworkElementFactory(typeof(TextBlock));
            timeText.SetBinding(TextBlock.TextProperty,
                new Binding(nameof(HistoryEntryDto.ExecutedAt))
                {
                    Converter = new RelativeTimeConverter()
                });
            timeText.SetValue(TextBlock.FontSizeProperty, Typography.Small);
            timeText.SetResourceBinding(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            timeText.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            timeText.SetValue(DockPanel.DockProperty, Dock.Left);
            leftMeta.AppendChild(timeText);

            var dotSep = new FrameworkElementFactory(typeof(TextBlock));
            dotSep.SetValue(TextBlock.TextProperty, " \u00B7 ");
            dotSep.SetValue(TextBlock.FontSizeProperty, Typography.Small);
            dotSep.SetResourceBinding(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            dotSep.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            dotSep.SetValue(DockPanel.DockProperty, Dock.Left);
            dotSep.SetBinding(VisibilityProperty, CreateMetaVisibilityBinding());
            leftMeta.AppendChild(dotSep);

            var metaText = new FrameworkElementFactory(typeof(TextBlock));
            metaText.SetBinding(TextBlock.TextProperty, CreateMetaTextBinding());
            metaText.SetValue(TextBlock.FontSizeProperty, Typography.Small);
            metaText.SetResourceBinding(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            metaText.SetValue(TextBlock.FontStyleProperty, FontStyles.Italic);
            metaText.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            metaText.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            metaText.SetBinding(VisibilityProperty, CreateMetaVisibilityBinding());
            leftMeta.AppendChild(metaText);

            line2.AppendChild(leftMeta); // LastChildFill \u2014 takes remaining width
            contentStack.AppendChild(line2);

            outerDock.AppendChild(contentStack);

            template.VisualTree = outerDock;
            return template;
        }

        /// <summary>
        /// Infinite scroll: fires LoadMoreCommand when the list is scrolled near the bottom.
        /// Offsets are in item units (ScrollUnit=Item). Guarded by HasMoreEntries + an in-flight flag.
        /// </summary>
        private void OnQueryListScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_loadMoreInFlight) return;
            if (!_viewModel.HasMoreEntries) return;
            if (e.ExtentHeight <= 0) return;

            // Near bottom: within ~5 items of the end.
            const double thresholdItems = 5.0;
            if (e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - thresholdItems)
            {
                if (_viewModel.LoadMoreCommand.CanExecute(null))
                {
                    _loadMoreInFlight = true;

                    // Release the guard once the in-flight search clears IsLoading.
                    void Release(object s, PropertyChangedEventArgs args)
                    {
                        if (args.PropertyName == nameof(HistoryViewModel.IsLoading) && !_viewModel.IsLoading)
                        {
                            _loadMoreInFlight = false;
                            _viewModel.PropertyChanged -= Release;
                        }
                    }
                    _viewModel.PropertyChanged += Release;

                    _viewModel.LoadMoreCommand.Execute(null);

                    // If the load short-circuited (e.g. engine not connected) IsLoading never toggled,
                    // so Release will never fire — clear the guard immediately to avoid a stuck flag.
                    if (!_viewModel.IsLoading)
                    {
                        _viewModel.PropertyChanged -= Release;
                        _loadMoreInFlight = false;
                    }
                }
            }
        }

        private static Style CreateQueryItemContainerStyle()
        {
            var style = new Style(typeof(ListViewItem));

            // Default: see-through background, 3px left border (will fill on selection).
            // The transparent default is theme-independent \u2014 a "no chrome" placeholder that the
            // triggers below replace once an item is selected or hovered.
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(3, 0, 0, 0)));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2, 0, 2, 0)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));

            // Spec 040 (T185): a screen reader reads the query's name, not the row's type name.
            style.Setters.Add(new Setter(AutomationProperties.NameProperty, new Binding { Converter = new QueryNameConverter() }));

            // Selected state: accent background + left border
            var selectedTrigger = new Trigger
            {
                Property = ListViewItem.IsSelectedProperty,
                Value = true
            };
            selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(ThemeTokens.SurfaceSelection)));
            selectedTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension(ThemeTokens.AccentPrimary)));
            style.Triggers.Add(selectedTrigger);

            // Mouse over (not selected)
            var hoverTrigger = new MultiTrigger();
            hoverTrigger.Conditions.Add(new Condition(UIElement.IsMouseOverProperty, true));
            hoverTrigger.Conditions.Add(new Condition(ListViewItem.IsSelectedProperty, false));
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(ThemeTokens.SurfaceHover)));
            style.Triggers.Add(hoverTrigger);

            return style;
        }

        private readonly ContextMenu _rowMenu = new ContextMenu();

        /// <summary>
        /// Spec 040 (HIS-12) — fills the row menu for <paramref name="entry"/>: every item acts on
        /// that row, in SQL Prompt's order (contracts/ui.md §4).
        /// </summary>
        internal ContextMenu FillRowMenu(HistoryEntryDto entry)
        {
            _rowMenu.Items.Clear();
            void Add(string header, ICommand command) =>
                _rowMenu.Items.Add(new MenuItem { Header = header, Command = command, CommandParameter = entry });

            Add("Open query", _viewModel.OpenInNewTabCommand);
            Add("Copy SQL", _viewModel.CopySqlCommand);
            Add("Re-execute", _viewModel.ReExecuteCommand);
            Add("Rename query", _viewModel.RenameCommand);
            Add("Compare\u2026", _viewModel.CompareCommand);
            _rowMenu.Items.Add(new Separator());
            Add("Remove query and its history", _viewModel.DeleteCommand);
            Add("Remove queries older than this\u2026", _viewModel.RemoveOlderThanCommand);
            return _rowMenu;
        }

        /// <summary>Right-click or the menu key: the menu for the row under the pointer, else the selected row.</summary>
        private void OnListContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var entry = (FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject)?.DataContext as HistoryEntryDto)
                        ?? _viewModel.SelectedEntry;
            if (entry == null)
            {
                e.Handled = true;
                return;
            }
            FillRowMenu(entry);
        }

        private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
        {
            while (node != null && node is not T)
                node = node is Visual || node is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(node)
                    : LogicalTreeHelper.GetParent(node);
            return node as T;
        }

        /// <summary>A chromeless, focusable row button (star, ⋯) named for screen readers; kept out of the Tab order (the list's keys reach them).</summary>
        private static FrameworkElementFactory RowButtonFactory(string name, Dock dock, Thickness margin)
        {
            var button = new FrameworkElementFactory(typeof(Button));
            button.SetValue(Control.TemplateProperty, BuildBareButtonTemplate());
            button.SetValue(Control.BackgroundProperty, Brushes.Transparent);
            button.SetValue(Control.BorderThicknessProperty, new Thickness(0));
            button.SetValue(Control.PaddingProperty, new Thickness(2, 0, 2, 0));
            button.SetValue(UIElement.FocusableProperty, true);
            button.SetValue(Control.IsTabStopProperty, false);
            // The default focus visual: a shared Style in a template would be sealed into it.
            button.SetValue(DockPanel.DockProperty, dock);
            button.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            button.SetValue(FrameworkElement.MarginProperty, margin);
            button.SetValue(FrameworkElement.CursorProperty, Cursors.Hand);
            button.SetValue(FrameworkElement.ToolTipProperty, name);
            button.SetValue(AutomationProperties.NameProperty, name);
            return button;
        }

        // ================================================================
        // LEFT-bottom Panel: Version sub-panel ("History for <file>")
        // ================================================================

        private DockPanel BuildVersionHistoryPanel()
        {
            var dock = new DockPanel();
            dock.SetResourceReference(DockPanel.BackgroundProperty, ThemeTokens.SurfacePanel);

            // Header — relabelled "History for <file>" when an entry is selected (see LoadVersionHistory).
            _versionPanelHeader = new TextBlock
            {
                Text = "HISTORY",
                FontWeight = FontWeights.SemiBold,
                FontSize = Typography.Small,
                Padding = new Thickness(10, 8, 10, 6),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _versionPanelHeader.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            DockPanel.SetDock(_versionPanelHeader, Dock.Top);
            dock.Children.Add(_versionPanelHeader);

            // Version ListBox
            _versionListBox = new ListBox
            {
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0)
            };
            _versionListBox.SetResourceReference(ListBox.BackgroundProperty, ThemeTokens.SurfacePanel);
            _versionListBox.SetResourceReference(ListBox.ForegroundProperty, ThemeTokens.TextPrimary);
            _versionListBox.SelectionChanged += OnVersionSelectionChanged;
            KeyboardNavigation.SetTabIndex(_versionListBox, 3);
            AutomationProperties.SetName(_versionListBox, "Versions");

            // Spec 040 (HIS-10): "Compare with current" on an earlier version.
            var compareItem = new MenuItem { Header = "Compare with current", Command = _viewModel.CompareWithCurrentCommand };
            _versionListBox.ContextMenu = new ContextMenu { Items = { compareItem } };
            _versionListBox.ContextMenuOpening += (_, e) =>
            {
                var item = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) ?? _versionListBox.SelectedItem as ListBoxItem;
                var index = item == null ? -1 : _versionListBox.Items.IndexOf(item);
                if (index <= 0 || item?.Tag is not HistoryVersionDto version)
                {
                    e.Handled = true; // none, or the current text itself
                    return;
                }
                compareItem.CommandParameter = version;
            };

            // Spec 040 (HIS-12): an empty state instead of a blank pane.
            _versionsEmptyText = new TextBlock
            {
                Text = "No earlier versions.",
                FontSize = Typography.Small,
                Margin = new Thickness(10, 4, 10, 4),
                Visibility = Visibility.Collapsed
            };
            _versionsEmptyText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);

            var body = new Grid();
            body.Children.Add(_versionListBox);
            body.Children.Add(_versionsEmptyText);
            dock.Children.Add(body);

            return dock;
        }

        // ================================================================
        // Right region: Code Preview (dark header + preview + metadata/Open bar)
        // ================================================================

        private DockPanel BuildCodePreviewPanel()
        {
            var dock = new DockPanel();
            dock.SetResourceReference(DockPanel.BackgroundProperty, ThemeTokens.EditorPopupBackground);

            // --- TOP: heavier dark header bar — filename left, ISO timestamp right ---
            var headerBar = new Border
            {
                Padding = new Thickness(10, 7, 10, 7),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            headerBar.SetResourceReference(Border.BackgroundProperty, ThemeTokens.SurfaceElevated);
            headerBar.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderDefault);

            var headerRow = new DockPanel();

            _codePreviewHeaderTimestamp = new TextBlock
            {
                FontSize = Typography.Small,
                VerticalAlignment = VerticalAlignment.Center
            };
            _codePreviewHeaderTimestamp.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            DockPanel.SetDock(_codePreviewHeaderTimestamp, Dock.Right);
            headerRow.Children.Add(_codePreviewHeaderTimestamp);

            _codePreviewHeaderFilename = new TextBlock
            {
                Text = "Preview",
                FontWeight = FontWeights.SemiBold,
                FontSize = Typography.Body,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _codePreviewHeaderFilename.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            headerRow.Children.Add(_codePreviewHeaderFilename); // LastChildFill

            headerBar.Child = headerRow;
            DockPanel.SetDock(headerBar, Dock.Top);
            dock.Children.Add(headerBar);

            // --- BOTTOM: metadata + action bar (● server · database | vN of M | Open) ---
            var metaBar = new DockPanel
            {
                Margin = new Thickness(10, 6, 10, 8)
            };

            // Prominent primary-styled "Open" button (right-docked) — OpenInNewTabCommand.
            var openButton = new Button
            {
                Content = "Open",
                Padding = new Thickness(16, 4, 16, 4),
                Cursor = Cursors.Hand,
                FontSize = Typography.Small,
                FontWeight = FontWeights.SemiBold,
                BorderThickness = new Thickness(0),
                FocusVisualStyle = FocusVisualStyles.HighStakes,
                ToolTip = "Open this query in a new editor tab",
                Template = BuildPrimaryButtonTemplate()
            };
            openButton.SetResourceReference(Control.BackgroundProperty, ThemeTokens.AccentPrimary);
            openButton.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextOnAccent);
            openButton.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty,
                new Binding(nameof(HistoryViewModel.OpenInNewTabCommand)));
            DockPanel.SetDock(openButton, Dock.Right);
            metaBar.Children.Add(openButton);

            _metadataVersionLabel = new TextBlock
            {
                FontSize = Typography.Small,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 10, 0)
            };
            _metadataVersionLabel.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            DockPanel.SetDock(_metadataVersionLabel, Dock.Right);
            metaBar.Children.Add(_metadataVersionLabel);

            var metaLeft = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            // ● server (Status.Success dot role)
            _metadataServerLabel = new TextBlock
            {
                FontSize = Typography.Small,
                VerticalAlignment = VerticalAlignment.Center
            };
            _metadataServerLabel.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.StatusSuccess);
            metaLeft.Children.Add(_metadataServerLabel);

            // Separator
            var metaSeparator = new TextBlock
            {
                Text = " · ",
                FontSize = Typography.Small,
                VerticalAlignment = VerticalAlignment.Center
            };
            metaSeparator.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            metaLeft.Children.Add(metaSeparator);

            // Database name
            _metadataDatabaseLabel = new TextBlock
            {
                FontSize = Typography.Small,
                VerticalAlignment = VerticalAlignment.Center
            };
            _metadataDatabaseLabel.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            metaLeft.Children.Add(_metadataDatabaseLabel);

            metaBar.Children.Add(metaLeft); // LastChildFill

            DockPanel.SetDock(metaBar, Dock.Bottom);
            dock.Children.Add(metaBar);

            // --- CENTER: read-only, selectable preview of the FULL text (added LAST = fills) ---
            // Spec 040 (HIS-01, HIS-11): the rows carry only 500 characters; the preview fetches the
            // whole query (HistoryViewModel.GetPreviewTextAsync) and can be selected and copied.
            _codePreview = new SqlPreviewView { Padding = new Thickness(4, 6, 4, 6) };
            _codePreview.SetResourceReference(Control.BackgroundProperty, ThemeTokens.EditorPopupBackground);
            KeyboardNavigation.SetTabIndex(_codePreview, 4);
            AutomationProperties.SetName(_codePreview, "Preview");

            // Spec 040 (HIS-12): an empty state instead of a blank pane.
            _previewEmptyText = new TextBlock
            {
                Text = "Select a query to see it here.",
                FontSize = Typography.Body,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            _previewEmptyText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);

            var previewGrid = new Grid();
            previewGrid.Children.Add(_codePreview);
            previewGrid.Children.Add(_previewEmptyText);
            dock.Children.Add(previewGrid);

            return dock;
        }

        /// <summary>
        /// Primary (accent) button template: rounded <see cref="Border"/> with TemplateBinding background,
        /// hover/pressed accent states. Used for the prominent right-pane "Open" button.
        /// </summary>
        private static ControlTemplate BuildPrimaryButtonTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);

            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty,
                new DynamicResourceExtension(ThemeTokens.AccentPrimaryHover), "Bd"));
            template.Triggers.Add(hover);

            var pressed = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Border.BackgroundProperty,
                new DynamicResourceExtension(ThemeTokens.AccentPrimaryPressed), "Bd"));
            template.Triggers.Add(pressed);

            return template;
        }

        // ================================================================
        // ROW 2: Status strip
        // ================================================================

        private DockPanel BuildStatusBar()
        {
            var bar = new DockPanel
            {
                Margin = new Thickness(8, 2, 8, 4)
            };
            bar.SetResourceReference(DockPanel.BackgroundProperty, ThemeTokens.SurfaceCanvas);

            // Spec 040 (HIS-13): a spinner while a search runs (the CLAUDE.md editor-margin pattern).
            var spinner = new Ellipse
            {
                Width = 12,
                Height = 12,
                StrokeThickness = 1.6,
                StrokeDashArray = new DoubleCollection { 10, 30 }, // ~90° arc, ~270° gap
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(0),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                ToolTip = "Searching\u2026"
            };
            spinner.SetResourceReference(Shape.StrokeProperty, ThemeTokens.EditorSpinnerStroke);
            AutomationProperties.SetName(spinner, "Searching");
            spinner.SetBinding(VisibilityProperty,
                new Binding(nameof(HistoryViewModel.IsLoading))
                {
                    Converter = new BoolToVisibilityConverter()
                });
            spinner.IsVisibleChanged += (_, __) =>
            {
                var rotate = (RotateTransform)spinner.RenderTransform;
                if (spinner.IsVisible)
                    rotate.BeginAnimation(RotateTransform.AngleProperty, new System.Windows.Media.Animation.DoubleAnimation
                    {
                        From = 0,
                        To = 360,
                        Duration = TimeSpan.FromMilliseconds(1100),
                        RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
                    });
                else
                    rotate.BeginAnimation(RotateTransform.AngleProperty, null);
            };
            _statusSpinner = spinner;
            DockPanel.SetDock(spinner, Dock.Right);
            bar.Children.Add(spinner);

            // Total count
            _statusCountLabel = new TextBlock
            {
                FontSize = Typography.Small,
                VerticalAlignment = VerticalAlignment.Center
            };
            _statusCountLabel.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            _statusCountLabel.SetBinding(TextBlock.TextProperty,
                new Binding(nameof(HistoryViewModel.TotalCount))
                {
                    StringFormat = "{0} entries found"
                });
            bar.Children.Add(_statusCountLabel);

            return bar;
        }

        // ================================================================
        // Event Handlers
        // ================================================================

        #region Event Handlers

        private void OnListViewDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Double-click opens the SQL in a new editor tab (not just copy)
            if (_viewModel.OpenInNewTabCommand.CanExecute(null))
            {
                _viewModel.OpenInNewTabCommand.Execute(null);
            }
        }

        private void OnListViewSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_queryListView != null)
            {
                _viewModel.UpdateSelectedEntries(_queryListView.SelectedItems);
            }

            // Update the code preview with search highlighting
            UpdatePreviewWithHighlighting();

            // Update the bottom metadata bar
            UpdateMetadataBar();

            // Load version history for the selected entry
            LoadVersionHistory();
        }

        /// <summary>
        /// Spec 040 (HIS-09): a new search selects its first row in the view model; select it in the
        /// list too, or the row looks unselected and the preview keeps saying "Select a query".
        /// Selecting it raises SelectionChanged, which fills the preview.
        /// </summary>
        /// <summary>
        /// True when keyboard focus fell back to nothing, to <paramref name="control"/> or to one of
        /// its ancestors — what happens when the focused row is removed — rather than going to
        /// something the user chose (the search box, the editor).
        /// </summary>
        internal static bool FocusFellBack(DependencyObject? focused, DependencyObject control) =>
            focused == null
            || ReferenceEquals(focused, control)
            || (focused is Visual ancestor && control is Visual visual && ancestor.IsAncestorOf(visual));

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        /// <summary>
        /// Whether Windows focus is still in this window — after a click on Object Explorer or the
        /// results grid (not WPF) WPF's focused element is null too, and focus must not be taken back.
        /// </summary>
        private bool WindowHasFocus() =>
            PresentationSource.FromVisual(this) is System.Windows.Interop.HwndSource source && GetFocus() == source.Handle;

        /// <summary>Puts keyboard focus back on the selected row (or the list) after a refresh took it away.</summary>
        private void RestoreListFocus()
        {
            if (_queryListView == null || !FocusFellBack(Keyboard.FocusedElement as DependencyObject, this) || !WindowHasFocus()) return;
            var item = _queryListView.SelectedItem;
            if (item != null)
            {
                _queryListView.ScrollIntoView(item);
                _queryListView.UpdateLayout();
                if (_queryListView.ItemContainerGenerator.ContainerFromItem(item) is ListViewItem row && row.Focus()) return;
            }
            _queryListView.Focus();
        }

        private void SyncListSelection()
        {
            if (_queryListView == null) return;
            var entry = _viewModel.SelectedEntry;
            if (entry == null || _queryListView.SelectedItems.Contains(entry)) return;
            _queryListView.SelectedItem = entry;
            _queryListView.ScrollIntoView(entry);
        }

        /// <summary>
        /// Updates the code preview with one merged Run-emission pass that composes
        /// (a) SQL syntax coloring (keyword / string / comment foreground from a lightweight tokenizer)
        /// with (b) search-match background highlighting (from <see cref="FindHighlightRegions"/>).
        /// Also refreshes the dark header (filename + ISO timestamp).
        /// </summary>
        private async void UpdatePreviewWithHighlighting()
        {
            if (_codePreview == null) return;

            var entry = _viewModel.SelectedEntry;
            if (_previewEmptyText != null)
                _previewEmptyText.Visibility = entry == null ? Visibility.Visible : Visibility.Collapsed;
            if (entry == null)
            {
                _codePreview.Show(string.Empty, null);
                if (_codePreviewHeaderTimestamp != null)
                    _codePreviewHeaderTimestamp.Text = string.Empty;
                if (_codePreviewHeaderFilename != null)
                    _codePreviewHeaderFilename.Text = "Preview";
                return;
            }

            // Header — filename (left) + ISO timestamp (right).
            if (_codePreviewHeaderFilename != null)
            {
                _codePreviewHeaderFilename.Text = QueryDisplayName(entry);
            }
            if (_codePreviewHeaderTimestamp != null)
            {
                if (DateTime.TryParse(entry.ExecutedAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var dt))
                {
                    _codePreviewHeaderTimestamp.Text = HistoryTimeFormat.Absolute(dt.ToLocalTime());
                }
                else
                {
                    _codePreviewHeaderTimestamp.Text = string.Empty;
                }
            }

            // The row's first 500 characters at once, then the full text when it arrives —
            // unless the user has selected another entry meanwhile.
            RenderPreview(entry.SqlText ?? string.Empty);
            try
            {
                var full = await _viewModel.GetPreviewTextAsync(entry);
                if (full != null && ReferenceEquals(_viewModel.SelectedEntry, entry)
                    && _versionListBox?.SelectedItem == null)
                    RenderPreview(full);
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug(ex, "HistoryToolWindowControl: full preview text failed");
            }
        }

        /// <summary>
        /// Shows <paramref name="sqlText"/> in the preview: syntax colours and search-match
        /// highlights come from the shared <see cref="SqlPreviewView"/>. Used by both the
        /// entry-selection preview and the version-selection preview.
        /// </summary>
        private void RenderPreview(string sqlText)
        {
            _codePreview?.Show(sqlText ?? string.Empty, CurrentHighlightTerms());
        }

        /// <summary>
        /// The words of the current search, for highlighting. Delegates to the shared quote-aware
        /// extractor <see cref="AkmlSql.Core.Text.HistorySearchTerms.Extract"/> (the same rules as
        /// the web History page).
        /// </summary>
        private IReadOnlyList<string> CurrentHighlightTerms()
        {
            var searchText = _viewModel.SearchText;
            return string.IsNullOrWhiteSpace(searchText)
                ? Array.Empty<string>()
                : AkmlSql.Core.Text.HistorySearchTerms.Extract(searchText).ToList();
        }

        /// <summary>
        /// Updates the bottom metadata bar in the code preview panel.
        /// </summary>
        private void UpdateMetadataBar()
        {
            var entry = _viewModel.SelectedEntry;

            if (_metadataServerLabel != null)
            {
                _metadataServerLabel.Text = entry != null
                    ? "\u25CF " + (entry.Server ?? "")
                    : "";
                // Spec 040 (HIS-02): green only while the query is open in a tab.
                _metadataServerLabel.SetResourceReference(TextBlock.ForegroundProperty,
                    entry?.IsOpen == true ? ThemeTokens.StatusSuccess : ThemeTokens.TextSecondary);
            }

            if (_metadataDatabaseLabel != null)
            {
                _metadataDatabaseLabel.Text = entry?.Database ?? "";
            }

            if (_metadataVersionLabel != null)
            {
                // Will be updated when versions load
                _metadataVersionLabel.Text = "";
            }
        }

        /// <summary>
        /// Loads version history for the currently selected entry.
        /// </summary>
        private async void LoadVersionHistory()
        {
            if (_versionListBox == null) return;
            _versionListBox.Items.Clear();
            if (_versionsEmptyText != null) _versionsEmptyText.Visibility = Visibility.Collapsed;

            var entry = _viewModel.SelectedEntry;
            if (entry == null)
            {
                if (_versionPanelHeader != null)
                    _versionPanelHeader.Text = "HISTORY";
                return;
            }

            // Relabel the LEFT-bottom version sub-panel header "History for <file>".
            if (_versionPanelHeader != null)
                _versionPanelHeader.Text = "History for " + QueryDisplayName(entry);

            var token = _versionGuard.Begin();
            try
            {
                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected) return;

                // Spec 040 (HIS-04): the whole grouped query's runs and snapshots — the same set the
                // row's "M versions" counts.
                var actionRequest = new HistoryActionRequest
                {
                    Action = HistoryActions.GetVersions,
                    EntryIds = new[] { entry.Id },
                    GroupScope = true
                };

                var response = await client.SendRequestAsync<HistoryActionResponse, HistoryActionRequest>(
                    MessageTypes.HistoryAction, actionRequest, timeoutMs: 5000);

                // A later selection has started its own load: this answer is for another entry.
                if (!_versionGuard.IsCurrent(token)) return;
                _versionListBox.Items.Clear();

                if (response.Success && response.Versions != null)
                {
                    int total = response.Versions.Length;
                    int versionNumber = total;

                    foreach (var version in response.Versions)
                    {
                        bool isCurrent = versionNumber == total;
                        var label = isCurrent
                            ? $"v{versionNumber} (current)"
                            : $"v{versionNumber}";

                        // Parse timestamp for display
                        var timestampText = "";
                        if (DateTime.TryParse(version.SavedAt, CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind, out var savedDt))
                        {
                            timestampText = HistoryTimeFormat.Absolute(savedDt.ToLocalTime());
                        }

                        // Spec 040 (HIS-12): a page glyph, and "server · environment" under the time.
                        var itemPanel = new StackPanel();
                        var itemRow = new DockPanel { Margin = new Thickness(4, 4, 4, 4) };
                        var glyph = BuildPageIcon();
                        DockPanel.SetDock(glyph, Dock.Left);
                        itemRow.Children.Add(glyph);
                        itemRow.Children.Add(itemPanel);

                        var versionLabel = new TextBlock
                        {
                            Text = label,
                            FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal,
                            FontSize = Typography.Small
                        };
                        versionLabel.SetResourceReference(TextBlock.ForegroundProperty,
                            isCurrent ? ThemeTokens.TextLink : ThemeTokens.TextSecondary);
                        itemPanel.Children.Add(versionLabel);

                        if (!string.IsNullOrEmpty(timestampText))
                        {
                            var timeLabel = new TextBlock
                            {
                                Text = timestampText,
                                FontSize = Typography.Small,
                                Margin = new Thickness(0, 1, 0, 0)
                            };
                            timeLabel.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
                            itemPanel.Children.Add(timeLabel);
                        }

                        var where = VersionConnectionLabel(version.Server ?? entry.Server, version.Database ?? entry.Database);
                        if (where.Length > 0)
                        {
                            var whereLabel = new TextBlock
                            {
                                Text = where,
                                FontSize = Typography.Small,
                                TextTrimming = TextTrimming.CharacterEllipsis
                            };
                            whereLabel.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
                            itemPanel.Children.Add(whereLabel);
                        }

                        var item = new ListBoxItem
                        {
                            Content = itemRow,
                            Tag = version,
                            ToolTip = $"Version {versionNumber} - {version.SavedAt}"
                        };
                        AutomationProperties.SetName(item, $"{label} {timestampText}".Trim());
                        _versionListBox.Items.Add(item);

                        versionNumber--;
                    }

                    // Update version count in metadata bar
                    if (_metadataVersionLabel != null && total > 0)
                    {
                        _metadataVersionLabel.Text = $"v{total} of {total}";
                    }
                    if (_versionsEmptyText != null && total <= 1)
                    {
                        _versionListBox.Items.Clear();
                        _versionsEmptyText.Visibility = Visibility.Visible;
                    }
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug(ex, "HistoryToolWindowControl: failed to load versions");
            }
        }

        /// <summary>
        /// When a version is selected in the version list, update the preview to show that version's SQL.
        /// </summary>
        private void OnVersionSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_codePreview == null || _versionListBox == null) return;

            if (_versionListBox.SelectedItem is ListBoxItem item && item.Tag is HistoryVersionDto version)
            {
                // Same merged syntax + search highlighting pass as the entry-selection preview.
                RenderPreview(version.SqlText);

                // Spec 040 (HIS-10): Open, Copy SQL and Re-execute now use this version — unless it
                // is the current text (the first row).
                _viewModel.SelectedVersion = _versionListBox.SelectedIndex > 0 ? version : null;

                // Update version label in metadata bar
                if (_metadataVersionLabel != null)
                {
                    int selectedIndex = _versionListBox.SelectedIndex;
                    int total = _versionListBox.Items.Count;
                    int versionNum = total - selectedIndex;
                    _metadataVersionLabel.Text = $"v{versionNum} of {total}";
                }
            }
        }

        /// <summary>
        /// Shows a simple WPF input dialog and returns the entered text, or null if cancelled.
        /// </summary>
        private static string? ShowInputDialog(string windowName, string prompt, string defaultValue)
        {
            var dialog = new Window
            {
                Title = WindowTitles.For(windowName),
                Width = 400,
                Height = 170,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize
            };
            WindowIcon.Apply(dialog);
            ThemeRegistry.Instance.AttachTo(dialog);
            dialog.SetResourceReference(Window.BackgroundProperty, ThemeTokens.SurfaceCanvas);
            dialog.SetResourceReference(Window.ForegroundProperty, ThemeTokens.TextPrimary);

            var panel = new StackPanel { Margin = new Thickness(Spacing.Lg) };

            var label = new TextBlock
            {
                Text = prompt,
                Margin = new Thickness(0, 0, 0, Spacing.Sm)
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            panel.Children.Add(label);

            var textBox = new TextBox
            {
                Text = defaultValue,
                Margin = new Thickness(0, 0, 0, Spacing.Md),
                Padding = new Thickness(6, 4, 6, 4),
                FocusVisualStyle = FocusVisualStyles.HighStakes
            };
            textBox.SetResourceReference(TextBox.BackgroundProperty, ThemeTokens.SurfaceInput);
            textBox.SetResourceReference(TextBox.ForegroundProperty, ThemeTokens.TextPrimary);
            textBox.SetResourceReference(TextBox.BorderBrushProperty, ThemeTokens.BorderDefault);
            textBox.SelectAll();
            panel.Children.Add(textBox);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            string? result = null;

            var okButton = new Button
            {
                Content = "OK",
                Width = 75,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };
            okButton.Click += (s, args) =>
            {
                result = textBox.Text;
                dialog.Close();
            };
            buttonPanel.Children.Add(okButton);

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 75,
                IsCancel = true
            };
            cancelButton.Click += (s, args) => dialog.Close();
            buttonPanel.Children.Add(cancelButton);

            panel.Children.Add(buttonPanel);
            dialog.Content = panel;

            // Parent the dialog to the VS/SSMS main window via DTE HWND.
            // Application.Current?.MainWindow is null in SSMS isolated-shell hosts,
            // so the DTE path is the only reliable option. See HistoryDiffWindow.cs
            // for the canonical pattern (CLAUDE.md "WPF UI conventions").
            try
            {
                var dte = (EnvDTE.DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte?.MainWindow != null)
                {
                    var helper = new System.Windows.Interop.WindowInteropHelper(dialog);
                    helper.Owner = (IntPtr)dte.MainWindow.HWnd;
                }
            }
            catch { /* Non-critical — CenterOwner falls back to screen centering */ }

            dialog.ShowDialog();
            return result;
        }

        /// <summary>
        /// Handles clicking on the favorite star icon in the list view.
        /// Finds the associated entry and toggles its favorite status.
        /// </summary>
        private void OnRowStarClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement button && button.DataContext is HistoryEntryDto entry)
            {
                // Spec 040 (HIS-04): the star acts on its own row, whatever is selected.
                if (_viewModel.ToggleFavoriteCommand.CanExecute(entry))
                {
                    _viewModel.ToggleFavoriteCommand.Execute(entry);
                }
                e.Handled = true;
            }
        }

        /// <summary>The row's ⋯: its own menu, below the button (spec 040, HIS-12: it acts on that row).</summary>
        private void OnOverflowClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement button && button.DataContext is HistoryEntryDto entry)
            {
                var menu = FillRowMenu(entry);
                menu.PlacementTarget = button;
                menu.Placement = PlacementMode.Bottom;
                menu.IsOpen = true;
                e.Handled = true;
            }
        }

        /// <summary>Switches to the open document named <paramref name="fullName"/> (Open query on an open query).</summary>
        private bool ActivateOpenDocument(string fullName)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var dte = (EnvDTE.DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte == null) return false;
                foreach (EnvDTE.Document document in dte.Documents)
                {
                    if (string.Equals(document.FullName, fullName, StringComparison.OrdinalIgnoreCase))
                    {
                        document.Activate();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug(ex, "History: could not switch to the open query");
            }
            return false;
        }

        /// <summary>
        /// Opens the given SQL text in a new editor tab via DTE,
        /// and sets the connection to the original server/database if available.
        /// </summary>
        private void OnOpenInNewTabRequested(string sqlText, string? server, string? database, string? sessionKey)
            => HistoryQueryOpener.OpenInNewTab(sqlText, server, database, sessionKey, "History.sql");

        /// <summary>
        /// Spec 040 (HIS-12): re-execute on the entry's connection. Without one, the text opens in a
        /// new tab and the status bar asks the user to connect and run it.
        /// </summary>
        private void OnReExecuteRequested(string sqlText, string? server, string? database)
        {
            try
            {
                if (!HistoryQueryOpener.OpenInNewTab(sqlText, server, database, sessionKey: null, "ReExecute.sql"))
                {
                    _viewModel.Notify("Connect, then run (F5).");
                    return;
                }

                var dte = (EnvDTE.DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE));
                try
                {
                    dte?.ExecuteCommand("Query.Execute");
                }
                catch (Exception)
                {
                    // Query.Execute may not be available in all hosts; the tab is open.
                    Serilog.Log.Debug("HistoryToolWindowControl: Query.Execute not available, SQL opened in new tab");
                    _viewModel.Notify("Connect, then run (F5).");
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "HistoryToolWindowControl: failed to re-execute SQL");
            }
        }

        /// <summary>Shows the side-by-side comparison (spec 040, HIS-10).</summary>
        private void OnCompareRequested(HistoryCompareSide left, HistoryCompareSide right)
        {
            try
            {
                var diffWindow = new HistoryDiffWindow(left, right);
                diffWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "HistoryToolWindowControl: failed to show diff view");
            }
        }

        #endregion

        // ================================================================
        // Helpers
        // ================================================================

        /// <summary>
        /// Returns the display name for an entry — its session name (TabTitle, now populated by the
        /// engine's query-session grouping), else a trimmed raw-SQL fallback for the rare sessionless
        /// row. Mirrors <see cref="QueryNameConverter"/> for use by the right-pane filename header and
        /// the "History for &lt;file&gt;" version-panel header.
        /// </summary>
        private static string QueryDisplayName(HistoryEntryDto entry)
        {
            if (entry == null) return "Preview";
            return HistoryRowDisplay.DisplayNameFor(entry);
        }

        /// <summary>
        /// Fresh MultiBinding (ExecutionCount, VersionCount) → the "×N · M versions" text via
        /// <see cref="MetaConverter"/>. A new instance per call — WPF Binding/MultiBinding objects
        /// cannot be shared across multiple target properties.
        /// </summary>
        private static MultiBinding CreateMetaTextBinding()
        {
            var binding = new MultiBinding { Converter = new MetaConverter() };
            binding.Bindings.Add(new Binding(nameof(HistoryEntryDto.ExecutionCount)));
            binding.Bindings.Add(new Binding(nameof(HistoryEntryDto.VersionCount)));
            binding.Bindings.Add(new Binding(nameof(HistoryEntryDto.Status)));
            return binding;
        }

        /// <summary>Fresh MultiBinding (ExecutionCount, VersionCount) → Visibility via <see cref="MetaVisibilityConverter"/>.</summary>
        private static MultiBinding CreateMetaVisibilityBinding()
        {
            var binding = new MultiBinding { Converter = new MetaVisibilityConverter() };
            binding.Bindings.Add(new Binding(nameof(HistoryEntryDto.ExecutionCount)));
            binding.Bindings.Add(new Binding(nameof(HistoryEntryDto.VersionCount)));
            binding.Bindings.Add(new Binding(nameof(HistoryEntryDto.Status)));
            return binding;
        }

        /// <summary>Fresh MultiBinding (Server, Database) → one part of the environment badge.</summary>
        private static MultiBinding EnvironmentBadgeBinding(EnvironmentBadgeConverter.Part part)
        {
            var binding = new MultiBinding { Converter = new EnvironmentBadgeConverter(part) };
            binding.Bindings.Add(new Binding(nameof(HistoryEntryDto.Server)));
            binding.Bindings.Add(new Binding(nameof(HistoryEntryDto.Database)));
            return binding;
        }

        // ================================================================
        // Value Converters
        // ================================================================

        #region Value Converters

        /// <summary>true → Visible, false → Hidden (keeps the row layout stable).</summary>
        private class BoolToHiddenConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
                => value is true ? Visibility.Visible : Visibility.Hidden;

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>
        /// Converts the full HistoryEntryDto to a display name (see <see cref="HistoryRowDisplay"/>):
        /// TabTitle (the session name) if set, otherwise a trimmed raw-SQL fallback. The "right-click to
        /// rename" hint remains the row TextBlock tooltip.
        /// </summary>
        private class QueryNameConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value is HistoryEntryDto entry)
                    return HistoryRowDisplay.DisplayNameFor(entry);
                return "";
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>
        /// Spec 040 (HIS-05): the row's connection, "server · database" (<see cref="HistoryRowDisplay.ConnectionLabel"/>).
        /// </summary>
        private class ServerLabelConverter : IMultiValueConverter
        {
            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
                => HistoryRowDisplay.ConnectionLabel(
                    values.Length > 0 ? values[0] as string : null,
                    values.Length > 1 ? values[1] as string : null);

            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>Formats ISO 8601 ExecutedAt string to a relative time format.</summary>
        private class RelativeTimeConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value is string isoDate && DateTime.TryParse(isoDate, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var dt))
                {
                    var local = dt.ToLocalTime();
                    var elapsed = DateTime.Now - local;

                    if (elapsed.TotalMinutes < 1) return "just now";
                    if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}m ago";
                    if (elapsed.TotalHours < 24) return local.ToString("HH:mm", CultureInfo.CurrentCulture);
                    if (local.Date == DateTime.Today.AddDays(-1))
                        return "Yesterday " + local.ToString("HH:mm", CultureInfo.CurrentCulture);
                    return HistoryTimeFormat.Absolute(local);
                }
                return value?.ToString() ?? "";
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>Converts ExecutionStatus int to a Unicode icon character.</summary>
        private class StatusIconConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value is int status)
                {
                    return status switch
                    {
                        0 => "\u2713", // check mark (Success)
                        1 => "\u2717", // cross mark (Error)
                        2 => "\u25CB", // circle (Cancelled)
                        _ => "?"
                    };
                }
                return "?";
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>Converts ExecutionStatus int (0=Success, 1=Error, 2=Cancelled) to a theme-aware status brush.</summary>
        private class StatusColorConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                string key;
                if (value is int status)
                {
                    key = status switch
                    {
                        0 => ThemeTokens.StatusSuccess,
                        1 => ThemeTokens.StatusDanger,
                        2 => ThemeTokens.StatusWarning,
                        _ => ThemeTokens.TextDisabled
                    };
                }
                else
                {
                    key = ThemeTokens.TextDisabled;
                }
                return ThemeRegistry.Instance.Resources[key];
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>Converts ExecutionStatus int to a human-readable text.</summary>
        private class StatusTextConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value is int status)
                {
                    return status switch
                    {
                        0 => "Success",
                        1 => "Error",
                        2 => "Cancelled",
                        _ => "Unknown"
                    };
                }
                return "Unknown";
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>Formats ISO 8601 ExecutedAt string to a user-friendly format.</summary>
        private class ExecutedAtConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value is string isoDate && DateTime.TryParse(isoDate, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var dt))
                {
                    var local = dt.ToLocalTime();
                    if (local.Date == DateTime.Today)
                        return local.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
                    if (local.Date == DateTime.Today.AddDays(-1))
                        return "Yesterday " + local.ToString("HH:mm", CultureInfo.CurrentCulture);
                    return HistoryTimeFormat.Absolute(local);
                }
                return value?.ToString() ?? "";
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>Formats duration in milliseconds to a human-readable string.</summary>
        private class DurationConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value is long ms)
                {
                    if (ms < 1000) return $"{ms}ms";
                    if (ms < 60000) return $"{ms / 1000.0:F1}s";
                    return $"{ms / 60000.0:F1}m";
                }
                return "";
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>Trims SQL text to ~200 chars and collapses whitespace for preview.</summary>
        private class SqlPreviewTrimConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value is string sql)
                {
                    // Collapse whitespace for single-line preview
                    var collapsed = System.Text.RegularExpressions.Regex.Replace(sql, @"\s+", " ").Trim();
                    if (collapsed.Length > 200)
                        return collapsed.Substring(0, 200) + "...";
                    return collapsed;
                }
                return "";
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>Formats Server > Database > Username connection info.</summary>
        private class ConnectionInfoConverter : IMultiValueConverter
        {
            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            {
                var server = values.Length > 0 ? values[0] as string : null;
                var database = values.Length > 1 ? values[1] as string : null;
                var username = values.Length > 2 ? values[2] as string : null;

                var parts = new List<string>();
                if (!string.IsNullOrEmpty(server)) parts.Add(server);
                if (!string.IsNullOrEmpty(database)) parts.Add(database);
                if (!string.IsNullOrEmpty(username)) parts.Add(username);

                return string.Join(" > ", parts);
            }

            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>Converts IsFavorite bool to a star icon.</summary>
        private class FavoriteIconConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                return value is true ? "\u2605" : "\u2606"; // filled star vs empty star
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>
        /// Formats the "×N · M versions" meta line (<see cref="HistoryRowDisplay.MetaFor"/>) from a
        /// two-value MultiBinding over ExecutionCount and VersionCount.
        /// </summary>
        private class MetaConverter : IMultiValueConverter
        {
            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
                => HistoryRowDisplay.MetaFor(ExecCountOf(values), VersionCountOf(values), StatusOf(values));

            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>Shows the meta line (and its leading separator) only when it carries information.</summary>
        private class MetaVisibilityConverter : IMultiValueConverter
        {
            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
                => string.IsNullOrEmpty(HistoryRowDisplay.MetaFor(ExecCountOf(values), VersionCountOf(values), StatusOf(values)))
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        private static int ExecCountOf(object[] values) =>
            values.Length > 0 && values[0] is int ec ? ec : 0;

        private static int VersionCountOf(object[] values) =>
            values.Length > 1 && values[1] is int vc ? vc : 0;

        private static int StatusOf(object[] values) =>
            values.Length > 2 && values[2] is int status ? status : 0;

        /// <summary>
        /// Spec 040 (HIS-05) — the row's environment badge: the label of the first colouring rule the
        /// row's server and database match, on the rule's colour with black or white text.
        /// </summary>
        private sealed class EnvironmentBadgeConverter : IMultiValueConverter
        {
            internal enum Part { Label, Background, Foreground, Visibility }

            private readonly Part _part;
            public EnvironmentBadgeConverter(Part part) => _part = part;

            public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            {
                var rule = AkmlSql.Shell.Shared.Tabs.EnvironmentDetector.Match(
                    values.Length > 0 ? values[0] as string : null,
                    values.Length > 1 ? values[1] as string : null);
                var show = rule != null && !string.IsNullOrWhiteSpace(rule.Label);
                switch (_part)
                {
                    case Part.Visibility: return show ? Visibility.Visible : Visibility.Collapsed;
                    case Part.Label: return show ? rule!.Label : string.Empty;
                    case Part.Background: return show ? AkmlSql.Shell.Shared.Tabs.HexBrush.Get(rule!.Color) : Brushes.Transparent;
                    default: return show ? AkmlSql.Shell.Shared.Tabs.HexBrush.ContrastFor(rule!.Color) : Brushes.Transparent;
                }
            }

            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>Visible when any bound value is true, else Hidden (the row keeps its layout).</summary>
        private sealed class AnyTrueToVisibleConverter : IMultiValueConverter
        {
            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
                => values.Any(v => v is true) ? Visibility.Visible : Visibility.Hidden;

            public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>
        /// Spec 040 (HIS-12): a version row's "server · environment" — the environment is the label of
        /// the colouring rule the server and database match; without one, "server · database".
        /// </summary>
        private static string VersionConnectionLabel(string? server, string? database)
        {
            var rule = AkmlSql.Shell.Shared.Tabs.EnvironmentDetector.Match(server, database);
            return rule != null && !string.IsNullOrWhiteSpace(rule.Label)
                ? HistoryRowDisplay.ConnectionLabel(server, rule.Label)
                : HistoryRowDisplay.ConnectionLabel(server, database);
        }

        /// <summary>A small line-art page with a folded corner (a version row's glyph).</summary>
        private static Path BuildPageIcon()
        {
            var path = new Path
            {
                Data = Geometry.Parse("M 0.5,0.5 L 6.5,0.5 L 9.5,3.5 L 9.5,12.5 L 0.5,12.5 Z M 6.5,0.5 L 6.5,3.5 L 9.5,3.5"),
                StrokeThickness = 1,
                Width = 10,
                Height = 13,
                Margin = new Thickness(0, 2, 6, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            path.SetResourceReference(Shape.StrokeProperty, ThemeTokens.TextSecondary);
            return path;
        }

        /// <summary>Converts bool to Visibility.</summary>
        private class BoolToVisibilityConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                return value is true ? Visibility.Visible : Visibility.Collapsed;
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        #endregion
    }
}
