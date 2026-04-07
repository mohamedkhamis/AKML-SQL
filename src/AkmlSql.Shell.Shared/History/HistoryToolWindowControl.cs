#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using AkmlSql.Shell.Shared.Ui;

namespace AkmlSql.Shell.Shared.History
{
    /// <summary>
    /// WPF UserControl that provides a SQL Prompt-style 3-panel layout for the SQL History tool window.
    /// Built programmatically (no XAML) for Shell.Shared compatibility across all 6 targets.
    /// Layout: top search/filter bar, then left query list | middle version history | right code preview.
    /// Features: search bar, filter tabs (All/Starred/Open/Closed), virtualized query list,
    /// version history, code preview with search highlighting, context menus, and all action commands.
    /// </summary>
    internal class HistoryToolWindowControl : UserControl
    {
        private readonly HistoryViewModel _viewModel;

        // Main panels
        private ListView? _queryListView;
        private ListBox? _versionListBox;
        private TextBlock? _codePreviewTextBlock;
        private TextBlock? _codePreviewHeaderTimestamp;
        private TextBlock? _metadataServerLabel;
        private TextBlock? _metadataDatabaseLabel;
        private TextBlock? _metadataVersionLabel;

        // Filter tab borders (for visual toggle)
        private Border? _filterAll;
        private Border? _filterStarred;
        private Border? _filterOpen;
        private Border? _filterClosed;
        private string _activeFilter = "all";

        // Status bar elements
        private TextBlock? _statusCountLabel;
        private TextBlock? _statusLoadingLabel;
        private Button? _loadMoreButton;

        public HistoryToolWindowControl()
        {
            _viewModel = new HistoryViewModel();
            DataContext = _viewModel;

            // Wire ViewModel events to DTE actions
            _viewModel.OpenInNewTabRequested += OnOpenInNewTabRequested;
            _viewModel.ReExecuteRequested += OnReExecuteRequested;
            _viewModel.CompareRequested += OnCompareRequested;

            BuildUi();

            // Initialize the ViewModel after UI is built
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _viewModel.InitializeAsync();
        }

        // ================================================================
        // Main UI Construction
        // ================================================================

        private void BuildUi()
        {
            var theme = ThemeManager.Instance;

            // Freeze all brushes for performance
            var windowBg = Freeze(theme.HistoryWindowBackground);
            var panelBg = Freeze(theme.HistoryPanelBackground);
            var metaFg = Freeze(theme.HistoryMetadata);

            // Main layout: Row 0 = top bar, Row 1 = 3-panel area, Row 2 = status bar
            var mainGrid = new Grid { Background = windowBg };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });    // Search + filter tabs
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 3-panel
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });    // Status bar

            // ROW 0: Search bar + filter tabs
            var topBar = BuildTopBar(theme);
            Grid.SetRow(topBar, 0);
            mainGrid.Children.Add(topBar);

            // ROW 1: 3-panel grid with splitters
            var panelsGrid = BuildThreePanelGrid(theme);
            Grid.SetRow(panelsGrid, 1);
            mainGrid.Children.Add(panelsGrid);

            // ROW 2: Status bar
            var statusBar = BuildStatusBar(theme);
            Grid.SetRow(statusBar, 2);
            mainGrid.Children.Add(statusBar);

            Content = mainGrid;
        }

        // ================================================================
        // ROW 0: Top bar (search + filter tabs)
        // ================================================================

        private StackPanel BuildTopBar(ThemeManager theme)
        {
            var panel = new StackPanel
            {
                Margin = new Thickness(8, 8, 8, 4)
            };

            // ----- Search row -----
            var searchRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };

            // Advanced search link (right-docked)
            var advSearchLink = new TextBlock
            {
                Text = "Advanced search",
                Foreground = Freeze(theme.HistoryVersionCurrent),
                FontSize = 11,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                TextDecorations = TextDecorations.Underline,
                ToolTip = "Use prefix filters: server:, db:, sql:, starred:yes, open:yes"
            };
            advSearchLink.MouseLeftButtonDown += (_, __) =>
            {
                // Insert example prefix text to help the user
                _viewModel.SearchText = "server: db: sql:SELECT ";
            };
            DockPanel.SetDock(advSearchLink, Dock.Right);
            searchRow.Children.Add(advSearchLink);

            // Search TextBox with rounded border
            var searchBorder = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = Freeze(theme.HistorySearchBackground),
                BorderBrush = Freeze(theme.HistorySearchBorder),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0)
            };

            var searchBox = new TextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Freeze(theme.HistoryQueryName),
                CaretBrush = Freeze(theme.HistoryQueryName),
                Padding = new Thickness(8, 6, 8, 6),
                FontSize = 12,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            searchBox.SetBinding(TextBox.TextProperty,
                new Binding(nameof(HistoryViewModel.SearchText))
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                });

            // Placeholder behavior
            var placeholderText = new TextBlock
            {
                Text = "\U0001F50D Search SQL history...",
                Foreground = Freeze(theme.PlaceholderText),
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
                FontSize = 12
            };

            var searchGrid = new Grid();
            searchGrid.Children.Add(searchBox);
            searchGrid.Children.Add(placeholderText);

            // Show/hide placeholder based on text
            searchBox.TextChanged += (_, __) =>
            {
                placeholderText.Visibility = string.IsNullOrEmpty(searchBox.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            };
            searchBox.GotFocus += (_, __) =>
            {
                placeholderText.Visibility = Visibility.Collapsed;
            };
            searchBox.LostFocus += (_, __) =>
            {
                placeholderText.Visibility = string.IsNullOrEmpty(searchBox.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            };

            // Enter key triggers search
            searchBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && _viewModel.SearchCommand.CanExecute(null))
                {
                    _viewModel.SearchCommand.Execute(null);
                }
            };

            searchBorder.Child = searchGrid;
            searchRow.Children.Add(searchBorder);
            panel.Children.Add(searchRow);

            // ----- Filter tabs row -----
            var tabRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 4)
            };

            _filterAll = CreateFilterTab("\U0001F4CB All", "all", theme, isActive: true);
            _filterStarred = CreateFilterTab("\u2B50 Starred", "starred", theme);
            _filterOpen = CreateFilterTab("\U0001F4C2 Open", "open", theme);
            _filterClosed = CreateFilterTab("\U0001F4D5 Closed", "closed", theme);

            tabRow.Children.Add(_filterAll);
            tabRow.Children.Add(_filterStarred);
            tabRow.Children.Add(_filterOpen);
            tabRow.Children.Add(_filterClosed);

            panel.Children.Add(tabRow);
            return panel;
        }

        private Border CreateFilterTab(string label, string filterKey, ThemeManager theme, bool isActive = false)
        {
            var activeBg = Freeze(theme.HistoryActiveFilterBackground);
            var activeBorder = Freeze(theme.HistoryActiveFilterBorder);
            var inactiveBg = Freeze(theme.HistoryInactiveFilterBackground);
            var inactiveBorder = Freeze(theme.HistoryInactiveFilterBorder);

            var textBlock = new TextBlock
            {
                Text = label,
                Foreground = Freeze(theme.HistoryQueryName),
                FontSize = 11.5,
                Padding = new Thickness(10, 4, 10, 4),
                TextAlignment = TextAlignment.Center
            };

            var border = new Border
            {
                CornerRadius = new CornerRadius(5),
                Background = isActive ? activeBg : inactiveBg,
                BorderBrush = isActive ? activeBorder : inactiveBorder,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand,
                Child = textBlock,
                Tag = filterKey
            };

            border.MouseLeftButtonDown += OnFilterTabClick;

            return border;
        }

        private void OnFilterTabClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border clicked && clicked.Tag is string filterKey)
            {
                _activeFilter = filterKey;
                UpdateFilterTabVisuals();
                ApplyFilterAndSearch();
            }
        }

        private void UpdateFilterTabVisuals()
        {
            var theme = ThemeManager.Instance;
            var activeBg = Freeze(theme.HistoryActiveFilterBackground);
            var activeBorder = Freeze(theme.HistoryActiveFilterBorder);
            var inactiveBg = Freeze(theme.HistoryInactiveFilterBackground);
            var inactiveBorder = Freeze(theme.HistoryInactiveFilterBorder);

            var tabs = new[] { _filterAll, _filterStarred, _filterOpen, _filterClosed };
            foreach (var tab in tabs)
            {
                if (tab == null) continue;
                var isActive = (string)tab.Tag == _activeFilter;
                tab.Background = isActive ? activeBg : inactiveBg;
                tab.BorderBrush = isActive ? activeBorder : inactiveBorder;
            }
        }

        private void ApplyFilterAndSearch()
        {
            // Reset all filters before applying the active tab
            _viewModel.FavoritesOnly = false;
            _viewModel.IsOpenFilter = null;
            _viewModel.SelectedStatus = null;

            switch (_activeFilter)
            {
                case "starred":
                    _viewModel.FavoritesOnly = true;
                    break;
                case "open":
                    _viewModel.IsOpenFilter = true;
                    break;
                case "closed":
                    _viewModel.IsOpenFilter = false;
                    break;
            }

            if (_viewModel.SearchCommand.CanExecute(null))
            {
                _viewModel.SearchCommand.Execute(null);
            }
        }

        // ================================================================
        // ROW 1: Three-panel grid
        // ================================================================

        private Grid BuildThreePanelGrid(ThemeManager theme)
        {
            var grid = new Grid();

            // Column definitions: query list | splitter | version history | splitter | code preview
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320, GridUnitType.Star), MinWidth = 200 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // splitter
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170, GridUnitType.Star), MinWidth = 120 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // splitter
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400, GridUnitType.Star), MinWidth = 200 });

            // Left panel: Query List
            var leftPanel = BuildQueryListPanel(theme);
            Grid.SetColumn(leftPanel, 0);
            grid.Children.Add(leftPanel);

            // Splitter 1
            var splitter1 = new GridSplitter
            {
                Width = 3,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = Freeze(theme.HistorySearchBorder),
                ResizeBehavior = GridResizeBehavior.PreviousAndNext
            };
            Grid.SetColumn(splitter1, 1);
            grid.Children.Add(splitter1);

            // Middle panel: Version History
            var middlePanel = BuildVersionHistoryPanel(theme);
            Grid.SetColumn(middlePanel, 2);
            grid.Children.Add(middlePanel);

            // Splitter 2
            var splitter2 = new GridSplitter
            {
                Width = 3,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = Freeze(theme.HistorySearchBorder),
                ResizeBehavior = GridResizeBehavior.PreviousAndNext
            };
            Grid.SetColumn(splitter2, 3);
            grid.Children.Add(splitter2);

            // Right panel: Code Preview
            var rightPanel = BuildCodePreviewPanel(theme);
            Grid.SetColumn(rightPanel, 4);
            grid.Children.Add(rightPanel);

            return grid;
        }

        // ================================================================
        // Left Panel: Query List
        // ================================================================

        private DockPanel BuildQueryListPanel(ThemeManager theme)
        {
            var panelBg = Freeze(theme.HistoryPanelBackground);
            var metaFg = Freeze(theme.HistoryMetadata);

            var dock = new DockPanel { Background = panelBg };

            // Header
            var header = new TextBlock
            {
                Text = "QUERIES",
                FontWeight = FontWeights.SemiBold,
                FontSize = 10,
                Foreground = metaFg,
                Padding = new Thickness(10, 8, 10, 6),
                // Letter spacing via character spacing is not directly supported in WPF TextBlock,
                // so we just use uppercase + semibold for the same visual effect
            };
            DockPanel.SetDock(header, Dock.Top);
            dock.Children.Add(header);

            // Action bar below header
            var actionBar = BuildActionBar(theme);
            DockPanel.SetDock(actionBar, Dock.Bottom);
            dock.Children.Add(actionBar);

            // Query ListView
            _queryListView = new ListView
            {
                Background = panelBg,
                Foreground = Freeze(theme.HistoryQueryName),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0),
                SelectionMode = SelectionMode.Extended,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };

            // Enable virtualization
            VirtualizingPanel.SetIsVirtualizing(_queryListView, true);
            VirtualizingPanel.SetVirtualizationMode(_queryListView, VirtualizationMode.Recycling);
            ScrollViewer.SetCanContentScroll(_queryListView, true);

            // Bind ItemsSource
            _queryListView.SetBinding(ItemsControl.ItemsSourceProperty,
                new Binding(nameof(HistoryViewModel.Entries)));

            // Item template: custom card-like layout per entry
            _queryListView.ItemTemplate = CreateQueryItemTemplate(theme);

            // ItemContainerStyle: selected item accent
            _queryListView.ItemContainerStyle = CreateQueryItemContainerStyle(theme);

            // Context menu
            _queryListView.ContextMenu = BuildQueryContextMenu();

            // Events
            _queryListView.MouseDoubleClick += OnListViewDoubleClick;
            _queryListView.SelectionChanged += OnListViewSelectionChanged;

            dock.Children.Add(_queryListView);

            return dock;
        }

        private DataTemplate CreateQueryItemTemplate(ThemeManager theme)
        {
            var template = new DataTemplate(typeof(HistoryEntryDto));

            // Outer grid: icon column | content column | star+overflow column
            var outerGrid = new FrameworkElementFactory(typeof(Grid));
            outerGrid.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 6, 4, 6));

            // Column definitions
            var col0 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col0.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            var col1 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col1.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            var col2 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col2.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);

            // We cannot add ColumnDefinitions via FrameworkElementFactory children.
            // Instead, use a DockPanel-based layout which works with FrameworkElementFactory.

            var outerDock = new FrameworkElementFactory(typeof(DockPanel));
            outerDock.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 4, 4, 4));

            // Right side: Star + overflow menu
            var rightStack = new FrameworkElementFactory(typeof(StackPanel));
            rightStack.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            rightStack.SetValue(DockPanel.DockProperty, Dock.Right);
            rightStack.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
            rightStack.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 0, 0, 0));

            // Star icon
            var starText = new FrameworkElementFactory(typeof(TextBlock));
            starText.SetBinding(TextBlock.TextProperty,
                new Binding(nameof(HistoryEntryDto.IsFavorite))
                {
                    Converter = new FavoriteIconConverter()
                });
            starText.SetBinding(TextBlock.ForegroundProperty,
                new Binding(nameof(HistoryEntryDto.IsFavorite))
                {
                    Converter = new FavoriteColorConverter()
                });
            starText.SetValue(TextBlock.FontSizeProperty, 14.0);
            starText.SetValue(FrameworkElement.CursorProperty, Cursors.Hand);
            starText.SetValue(ToolTipProperty, "Toggle favorite");
            starText.AddHandler(UIElement.MouseLeftButtonDownEvent,
                new MouseButtonEventHandler(OnFavoriteStarClick));
            rightStack.AppendChild(starText);

            // Overflow "..." button
            var overflowText = new FrameworkElementFactory(typeof(TextBlock));
            overflowText.SetValue(TextBlock.TextProperty, " \u22EE");
            overflowText.SetValue(TextBlock.ForegroundProperty, Freeze(theme.HistoryMetadata));
            overflowText.SetValue(TextBlock.FontSizeProperty, 14.0);
            overflowText.SetValue(FrameworkElement.CursorProperty, Cursors.Hand);
            overflowText.SetValue(ToolTipProperty, "More actions");
            overflowText.AddHandler(UIElement.MouseLeftButtonDownEvent,
                new MouseButtonEventHandler(OnOverflowClick));
            rightStack.AppendChild(overflowText);

            outerDock.AppendChild(rightStack);

            // Left side: Status icon
            var statusIcon = new FrameworkElementFactory(typeof(TextBlock));
            statusIcon.SetBinding(TextBlock.TextProperty,
                new Binding(nameof(HistoryEntryDto.IsOpen))
                {
                    Converter = new OpenClosedIconConverter()
                });
            statusIcon.SetBinding(TextBlock.ForegroundProperty,
                new Binding(nameof(HistoryEntryDto.IsOpen))
                {
                    Converter = new OpenClosedColorConverter()
                });
            statusIcon.SetValue(TextBlock.FontSizeProperty, 16.0);
            statusIcon.SetValue(DockPanel.DockProperty, Dock.Left);
            statusIcon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
            statusIcon.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            outerDock.AppendChild(statusIcon);

            // Center: content stack (name, connection, time)
            var contentStack = new FrameworkElementFactory(typeof(StackPanel));

            // Row 1: Query name (TabTitle or first 60 chars of SQL)
            var nameText = new FrameworkElementFactory(typeof(TextBlock));
            nameText.SetBinding(TextBlock.TextProperty,
                new Binding { Converter = new QueryNameConverter() });
            nameText.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            nameText.SetValue(TextBlock.FontSizeProperty, 12.0);
            nameText.SetValue(TextBlock.ForegroundProperty, Freeze(theme.HistoryQueryName));
            nameText.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            nameText.SetValue(TextBlock.MaxHeightProperty, 18.0);
            contentStack.AppendChild(nameText);

            // Row 2: Server -> Database
            var connText = new FrameworkElementFactory(typeof(TextBlock));
            connText.SetBinding(TextBlock.TextProperty, new MultiBinding
            {
                Converter = new ServerArrowDatabaseConverter(),
                Bindings =
                {
                    new Binding(nameof(HistoryEntryDto.Server)),
                    new Binding(nameof(HistoryEntryDto.Database))
                }
            });
            connText.SetValue(TextBlock.FontSizeProperty, 10.5);
            connText.SetValue(TextBlock.ForegroundProperty, Freeze(theme.HistoryMetadata));
            connText.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            connText.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 0));
            contentStack.AppendChild(connText);

            // Row 3: Relative timestamp + execution count
            var timeText = new FrameworkElementFactory(typeof(TextBlock));
            timeText.SetBinding(TextBlock.TextProperty,
                new Binding(nameof(HistoryEntryDto.ExecutedAt))
                {
                    Converter = new RelativeTimeConverter()
                });
            timeText.SetValue(TextBlock.FontSizeProperty, 10.0);
            timeText.SetValue(TextBlock.ForegroundProperty, Freeze(theme.HistoryMetadata));
            timeText.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 0));
            contentStack.AppendChild(timeText);

            // Row 4: Execution count (only visible when > 1)
            var execCountText = new FrameworkElementFactory(typeof(TextBlock));
            execCountText.SetBinding(TextBlock.TextProperty,
                new Binding(nameof(HistoryEntryDto.ExecutionCount))
                {
                    Converter = new ExecCountConverter()
                });
            execCountText.SetValue(TextBlock.FontSizeProperty, 9.5);
            execCountText.SetValue(TextBlock.ForegroundProperty, Freeze(theme.HistoryMetadata));
            execCountText.SetValue(TextBlock.FontStyleProperty, FontStyles.Italic);
            execCountText.SetBinding(VisibilityProperty,
                new Binding(nameof(HistoryEntryDto.ExecutionCount))
                {
                    Converter = new ExecCountVisibilityConverter()
                });
            contentStack.AppendChild(execCountText);

            outerDock.AppendChild(contentStack);

            template.VisualTree = outerDock;
            return template;
        }

        private Style CreateQueryItemContainerStyle(ThemeManager theme)
        {
            var style = new Style(typeof(ListViewItem));

            // Default: transparent background, no border
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(3, 0, 0, 0)));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2, 0, 2, 0)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));

            // Selected state: accent background + left border
            var selectedTrigger = new Trigger
            {
                Property = ListViewItem.IsSelectedProperty,
                Value = true
            };
            selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, Freeze(theme.HistorySelectedBackground)));
            selectedTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, Freeze(theme.HistorySelectedBorder)));
            style.Triggers.Add(selectedTrigger);

            // Mouse over (not selected)
            var hoverTrigger = new MultiTrigger();
            hoverTrigger.Conditions.Add(new Condition(UIElement.IsMouseOverProperty, true));
            hoverTrigger.Conditions.Add(new Condition(ListViewItem.IsSelectedProperty, false));
            var hoverBg = Freeze(theme.HistorySearchBackground);
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, hoverBg));
            style.Triggers.Add(hoverTrigger);

            return style;
        }

        private ContextMenu BuildQueryContextMenu()
        {
            var contextMenu = new ContextMenu();

            var copySqlItem = new MenuItem { Header = "Copy SQL" };
            copySqlItem.SetBinding(MenuItem.CommandProperty,
                new Binding(nameof(HistoryViewModel.CopySqlCommand)));
            contextMenu.Items.Add(copySqlItem);

            var openItem = new MenuItem { Header = "Open in New Tab" };
            openItem.SetBinding(MenuItem.CommandProperty,
                new Binding(nameof(HistoryViewModel.OpenInNewTabCommand)));
            contextMenu.Items.Add(openItem);

            var reExecItem = new MenuItem { Header = "Re-execute" };
            reExecItem.SetBinding(MenuItem.CommandProperty,
                new Binding(nameof(HistoryViewModel.ReExecuteCommand)));
            contextMenu.Items.Add(reExecItem);

            contextMenu.Items.Add(new Separator());

            var renameItem = new MenuItem { Header = "Rename" };
            renameItem.Click += OnRenameMenuItemClick;
            contextMenu.Items.Add(renameItem);

            var favItem = new MenuItem { Header = "Toggle Favorite" };
            favItem.SetBinding(MenuItem.CommandProperty,
                new Binding(nameof(HistoryViewModel.ToggleFavoriteCommand)));
            contextMenu.Items.Add(favItem);

            contextMenu.Items.Add(new Separator());

            var compareItem = new MenuItem { Header = "Compare (select 2)" };
            compareItem.SetBinding(MenuItem.CommandProperty,
                new Binding(nameof(HistoryViewModel.CompareCommand)));
            contextMenu.Items.Add(compareItem);

            var exportItem = new MenuItem { Header = "Export..." };
            exportItem.SetBinding(MenuItem.CommandProperty,
                new Binding(nameof(HistoryViewModel.ExportCommand)));
            contextMenu.Items.Add(exportItem);

            contextMenu.Items.Add(new Separator());

            var deleteItem = new MenuItem { Header = "Delete" };
            deleteItem.SetBinding(MenuItem.CommandProperty,
                new Binding(nameof(HistoryViewModel.DeleteCommand)));
            contextMenu.Items.Add(deleteItem);

            return contextMenu;
        }

        private StackPanel BuildActionBar(ThemeManager theme)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(6, 4, 6, 6),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            var btnStyle = new Style(typeof(Button));
            btnStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 2, 6, 2)));
            btnStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 3, 0)));
            btnStyle.Setters.Add(new Setter(Control.FontSizeProperty, 10.5));

            var copyBtn = new Button { Content = "Copy", Style = btnStyle, ToolTip = "Copy full SQL to clipboard" };
            copyBtn.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty,
                new Binding(nameof(HistoryViewModel.CopySqlCommand)));
            panel.Children.Add(copyBtn);

            var openBtn = new Button { Content = "Open", Style = btnStyle, ToolTip = "Open in new editor tab" };
            openBtn.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty,
                new Binding(nameof(HistoryViewModel.OpenInNewTabCommand)));
            panel.Children.Add(openBtn);

            var reExecBtn = new Button { Content = "Re-run", Style = btnStyle, ToolTip = "Re-execute selected query" };
            reExecBtn.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty,
                new Binding(nameof(HistoryViewModel.ReExecuteCommand)));
            panel.Children.Add(reExecBtn);

            var compareBtn = new Button { Content = "Compare", Style = btnStyle, ToolTip = "Compare two selected entries" };
            compareBtn.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty,
                new Binding(nameof(HistoryViewModel.CompareCommand)));
            panel.Children.Add(compareBtn);

            // Visual separator
            panel.Children.Add(new Border
            {
                Width = 1,
                Background = Freeze(theme.HistorySearchBorder),
                Margin = new Thickness(3, 0, 3, 0),
                VerticalAlignment = VerticalAlignment.Stretch
            });

            var exportBtn = new Button { Content = "Export", Style = btnStyle, ToolTip = "Export history" };
            exportBtn.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty,
                new Binding(nameof(HistoryViewModel.ExportCommand)));
            panel.Children.Add(exportBtn);

            var deleteBtn = new Button { Content = "Delete", Style = btnStyle, ToolTip = "Delete selected" };
            deleteBtn.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty,
                new Binding(nameof(HistoryViewModel.DeleteCommand)));
            panel.Children.Add(deleteBtn);

            return panel;
        }

        // ================================================================
        // Middle Panel: Version History
        // ================================================================

        private DockPanel BuildVersionHistoryPanel(ThemeManager theme)
        {
            var panelBg = Freeze(theme.HistoryPanelBackground);
            var metaFg = Freeze(theme.HistoryMetadata);

            var dock = new DockPanel { Background = panelBg };

            // Header
            var header = new TextBlock
            {
                Text = "HISTORY",
                FontWeight = FontWeights.SemiBold,
                FontSize = 10,
                Foreground = metaFg,
                Padding = new Thickness(10, 8, 10, 6)
            };
            DockPanel.SetDock(header, Dock.Top);
            dock.Children.Add(header);

            // Version ListBox
            _versionListBox = new ListBox
            {
                Background = panelBg,
                Foreground = Freeze(theme.HistoryQueryName),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0)
            };
            _versionListBox.SelectionChanged += OnVersionSelectionChanged;

            dock.Children.Add(_versionListBox);

            return dock;
        }

        // ================================================================
        // Right Panel: Code Preview
        // ================================================================

        private DockPanel BuildCodePreviewPanel(ThemeManager theme)
        {
            var previewBg = Freeze(theme.HistoryCodePreviewBackground);
            var metaFg = Freeze(theme.HistoryMetadata);
            var queryNameFg = Freeze(theme.HistoryQueryName);

            var dock = new DockPanel { Background = previewBg };

            // Header row: "CODE PREVIEW" + timestamp
            var headerRow = new DockPanel
            {
                Margin = new Thickness(10, 8, 10, 4)
            };

            _codePreviewHeaderTimestamp = new TextBlock
            {
                FontSize = 10,
                Foreground = metaFg,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(_codePreviewHeaderTimestamp, Dock.Right);
            headerRow.Children.Add(_codePreviewHeaderTimestamp);

            var headerLabel = new TextBlock
            {
                Text = "CODE PREVIEW",
                FontWeight = FontWeights.SemiBold,
                FontSize = 10,
                Foreground = metaFg
            };
            headerRow.Children.Add(headerLabel);

            DockPanel.SetDock(headerRow, Dock.Top);
            dock.Children.Add(headerRow);

            // Bottom metadata bar: server | database | version
            var metaBar = new DockPanel
            {
                Margin = new Thickness(10, 4, 10, 8),
                Background = Freeze(theme.HistoryPanelBackground)
            };

            _metadataVersionLabel = new TextBlock
            {
                FontSize = 10,
                Foreground = metaFg,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(_metadataVersionLabel, Dock.Right);
            metaBar.Children.Add(_metadataVersionLabel);

            var metaLeft = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Green circle + server name
            _metadataServerLabel = new TextBlock
            {
                FontSize = 10.5,
                Foreground = Freeze(theme.HistoryOpenIcon),
                VerticalAlignment = VerticalAlignment.Center
            };
            metaLeft.Children.Add(_metadataServerLabel);

            // Separator
            metaLeft.Children.Add(new TextBlock
            {
                Text = " | ",
                FontSize = 10.5,
                Foreground = metaFg,
                VerticalAlignment = VerticalAlignment.Center
            });

            // Database name
            _metadataDatabaseLabel = new TextBlock
            {
                FontSize = 10.5,
                Foreground = metaFg,
                VerticalAlignment = VerticalAlignment.Center
            };
            metaLeft.Children.Add(_metadataDatabaseLabel);

            metaBar.Children.Add(metaLeft);

            DockPanel.SetDock(metaBar, Dock.Bottom);
            dock.Children.Add(metaBar);

            // Code preview TextBlock in ScrollViewer
            _codePreviewTextBlock = new TextBlock
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11.0,
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(10, 6, 10, 6),
                Foreground = queryNameFg,
                Background = previewBg
            };
            var previewScroll = new ScrollViewer
            {
                Content = _codePreviewTextBlock,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = previewBg
            };

            dock.Children.Add(previewScroll);

            return dock;
        }

        // ================================================================
        // ROW 2: Status bar
        // ================================================================

        private DockPanel BuildStatusBar(ThemeManager theme)
        {
            var metaFg = Freeze(theme.HistoryMetadata);
            var windowBg = Freeze(theme.HistoryWindowBackground);

            var bar = new DockPanel
            {
                Background = windowBg,
                Margin = new Thickness(8, 2, 8, 4)
            };

            // Load More button on the right
            _loadMoreButton = new Button
            {
                Content = "Load More",
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(4, 0, 0, 0),
                FontSize = 10.5
            };
            _loadMoreButton.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty,
                new Binding(nameof(HistoryViewModel.LoadMoreCommand)));
            _loadMoreButton.SetBinding(VisibilityProperty,
                new Binding(nameof(HistoryViewModel.HasMoreEntries))
                {
                    Converter = new BoolToVisibilityConverter()
                });
            DockPanel.SetDock(_loadMoreButton, Dock.Right);
            bar.Children.Add(_loadMoreButton);

            // Loading indicator
            _statusLoadingLabel = new TextBlock
            {
                Text = "Loading...",
                Foreground = metaFg,
                FontStyle = FontStyles.Italic,
                FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            _statusLoadingLabel.SetBinding(VisibilityProperty,
                new Binding(nameof(HistoryViewModel.IsLoading))
                {
                    Converter = new BoolToVisibilityConverter()
                });
            DockPanel.SetDock(_statusLoadingLabel, Dock.Right);
            bar.Children.Add(_statusLoadingLabel);

            // Total count
            _statusCountLabel = new TextBlock
            {
                Foreground = metaFg,
                FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Center
            };
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
        /// Updates the code preview TextBlock with search match highlighting.
        /// When SearchText is active, matching segments get the HistorySearchHighlight background.
        /// Supports multi-term highlighting: splits by OR, strips NOT terms, extracts quoted phrases,
        /// removes prefix filters (server:, database:, name:, sql:, starred:, open:), and merges
        /// overlapping highlight regions.
        /// </summary>
        private void UpdatePreviewWithHighlighting()
        {
            if (_codePreviewTextBlock == null) return;
            _codePreviewTextBlock.Inlines.Clear();

            var entry = _viewModel.SelectedEntry;
            if (entry == null)
            {
                if (_codePreviewHeaderTimestamp != null)
                    _codePreviewHeaderTimestamp.Text = string.Empty;
                return;
            }

            // Update the timestamp in the header
            if (_codePreviewHeaderTimestamp != null)
            {
                if (DateTime.TryParse(entry.ExecutedAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var dt))
                {
                    _codePreviewHeaderTimestamp.Text = dt.ToLocalTime()
                        .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
                }
            }

            var sqlText = entry.SqlText ?? string.Empty;
            var searchText = _viewModel.SearchText;

            if (string.IsNullOrWhiteSpace(searchText))
            {
                // No search active: plain text
                _codePreviewTextBlock.Inlines.Add(new Run(sqlText));
                return;
            }

            // Parse search text into individual highlight terms
            var highlightTerms = ExtractHighlightTerms(searchText);

            if (highlightTerms.Count == 0)
            {
                // All terms were prefix filters or NOT terms — no highlighting needed
                _codePreviewTextBlock.Inlines.Add(new Run(sqlText));
                return;
            }

            // Find all match regions across all terms
            var regions = FindHighlightRegions(sqlText, highlightTerms);

            if (regions.Count == 0)
            {
                // No matches found
                _codePreviewTextBlock.Inlines.Add(new Run(sqlText));
                return;
            }

            // Render the text with highlighted regions
            var theme = ThemeManager.Instance;
            var highlightBrush = Freeze(theme.HistorySearchHighlight);

            int pos = 0;
            foreach (var region in regions)
            {
                // Add non-matching segment before this region
                if (region.Start > pos)
                {
                    _codePreviewTextBlock.Inlines.Add(new Run(sqlText.Substring(pos, region.Start - pos)));
                }

                // Add highlighted segment
                var matchRun = new Run(sqlText.Substring(region.Start, region.Length))
                {
                    Background = highlightBrush
                };
                _codePreviewTextBlock.Inlines.Add(matchRun);

                pos = region.Start + region.Length;
            }

            // Add remaining text after last highlight
            if (pos < sqlText.Length)
            {
                _codePreviewTextBlock.Inlines.Add(new Run(sqlText.Substring(pos)));
            }
        }

        /// <summary>
        /// Extracts highlight terms from the search text.
        /// - Splits by OR (case-sensitive word boundary) into separate groups
        /// - Strips NOT-prefixed terms (these filter but should not highlight)
        /// - Extracts quoted phrases as whole terms (quotes stripped for matching)
        /// - Removes prefix filters (server:, database:, db:, name:, sql:, starred:, open:)
        /// - Each remaining word/phrase becomes a highlight term
        /// </summary>
        private static List<string> ExtractHighlightTerms(string searchText)
        {
            var terms = new List<string>();

            // Split by OR at word boundaries: " OR " or start/end anchored OR
            var orParts = Regex.Split(searchText, @"(?<=\s)OR(?=\s)|^OR(?=\s)|(?<=\s)OR$");

            foreach (var orPart in orParts)
            {
                var part = orPart.Trim();
                if (string.IsNullOrEmpty(part)) continue;

                // Tokenize the part respecting quoted strings
                var tokens = TokenizeForHighlight(part);

                bool skipNext = false;
                for (int i = 0; i < tokens.Count; i++)
                {
                    var token = tokens[i];

                    // Skip AND keyword (FTS5 boolean, not a highlight term)
                    if (string.Equals(token, "AND", StringComparison.Ordinal))
                        continue;

                    // NOT: skip the NOT keyword and the next token (the negated term)
                    if (string.Equals(token, "NOT", StringComparison.Ordinal))
                    {
                        skipNext = true;
                        continue;
                    }

                    if (skipNext)
                    {
                        skipNext = false;
                        continue;
                    }

                    // Check for prefix filters — skip these entirely
                    if (IsPrefixFilter(token))
                        continue;

                    // Strip quotes from quoted phrases
                    var term = token;
                    if (term.Length >= 2 && term.StartsWith("\"", StringComparison.Ordinal)
                                        && term.EndsWith("\"", StringComparison.Ordinal))
                    {
                        term = term.Substring(1, term.Length - 2);
                    }

                    // Strip trailing wildcard (*) used for FTS5 prefix search
                    if (term.EndsWith("*", StringComparison.Ordinal))
                    {
                        term = term.Substring(0, term.Length - 1);
                    }

                    if (!string.IsNullOrWhiteSpace(term) && !terms.Contains(term))
                    {
                        terms.Add(term);
                    }
                }
            }

            return terms;
        }

        /// <summary>
        /// Tokenizes a search string respecting quoted phrases.
        /// Quoted strings (including prefix:&quot;value&quot;) are kept as single tokens.
        /// </summary>
        private static List<string> TokenizeForHighlight(string text)
        {
            var tokens = new List<string>();
            var sb = new System.Text.StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    sb.Append(c);
                    continue;
                }

                if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (sb.Length > 0)
                    {
                        tokens.Add(sb.ToString());
                        sb.Clear();
                    }
                    continue;
                }

                sb.Append(c);
            }

            if (sb.Length > 0)
            {
                tokens.Add(sb.ToString());
            }

            return tokens;
        }

        /// <summary>
        /// Returns true if the token is a prefix filter (server:val, database:val, db:val,
        /// sql:val, name:val, starred:val, open:val). These should not be highlighted.
        /// </summary>
        private static bool IsPrefixFilter(string token)
        {
            var colonIdx = token.IndexOf(':');
            if (colonIdx <= 0) return false;

            var prefix = token.Substring(0, colonIdx).ToLowerInvariant();
            switch (prefix)
            {
                case "server":
                case "database":
                case "db":
                case "sql":
                case "name":
                case "starred":
                case "open":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Finds all highlight regions in the SQL text for the given terms.
        /// Performs case-insensitive matching. Overlapping regions are merged.
        /// Returns a sorted, non-overlapping list of (Start, Length) regions.
        /// </summary>
        private static List<HighlightRegion> FindHighlightRegions(string sqlText, List<string> terms)
        {
            var regions = new List<HighlightRegion>();

            foreach (var term in terms)
            {
                if (string.IsNullOrEmpty(term)) continue;

                int pos = 0;
                while (pos < sqlText.Length)
                {
                    int matchIdx = sqlText.IndexOf(term, pos, StringComparison.OrdinalIgnoreCase);
                    if (matchIdx < 0) break;

                    regions.Add(new HighlightRegion(matchIdx, term.Length));
                    pos = matchIdx + 1; // advance by 1 to find overlapping matches from different terms
                }
            }

            if (regions.Count == 0) return regions;

            // Sort by start position, then by length descending (longer matches first for merging)
            regions.Sort((a, b) =>
            {
                int cmp = a.Start.CompareTo(b.Start);
                return cmp != 0 ? cmp : b.Length.CompareTo(a.Length);
            });

            // Merge overlapping/adjacent regions
            var merged = new List<HighlightRegion> { regions[0] };
            for (int i = 1; i < regions.Count; i++)
            {
                var last = merged[merged.Count - 1];
                var current = regions[i];

                if (current.Start <= last.Start + last.Length)
                {
                    // Overlapping or adjacent — extend the last region
                    int newEnd = Math.Max(last.Start + last.Length, current.Start + current.Length);
                    merged[merged.Count - 1] = new HighlightRegion(last.Start, newEnd - last.Start);
                }
                else
                {
                    merged.Add(current);
                }
            }

            return merged;
        }

        /// <summary>
        /// Represents a highlighted region in the SQL text (start index + length).
        /// </summary>
        private readonly struct HighlightRegion
        {
            public readonly int Start;
            public readonly int Length;

            public HighlightRegion(int start, int length)
            {
                Start = start;
                Length = length;
            }
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

            var entry = _viewModel.SelectedEntry;
            if (entry == null) return;

            try
            {
                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected) return;

                var actionRequest = new HistoryActionRequest
                {
                    Action = HistoryActions.GetVersions,
                    EntryIds = new[] { entry.Id }
                };

                var response = await client.SendRequestAsync<HistoryActionResponse, HistoryActionRequest>(
                    MessageTypes.HistoryAction, actionRequest, timeoutMs: 5000);

                if (response.Success && response.Versions != null)
                {
                    var theme = ThemeManager.Instance;
                    var versionCurrentFg = Freeze(theme.HistoryVersionCurrent);
                    var metaFg = Freeze(theme.HistoryMetadata);
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
                            timestampText = savedDt.ToLocalTime()
                                .ToString("MMM dd, HH:mm", CultureInfo.CurrentCulture);
                        }

                        var itemPanel = new StackPanel { Margin = new Thickness(4, 4, 4, 4) };

                        var versionLabel = new TextBlock
                        {
                            Text = label,
                            FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal,
                            FontSize = 11.5,
                            Foreground = isCurrent ? versionCurrentFg : metaFg
                        };
                        itemPanel.Children.Add(versionLabel);

                        if (!string.IsNullOrEmpty(timestampText))
                        {
                            var timeLabel = new TextBlock
                            {
                                Text = timestampText,
                                FontSize = 9.5,
                                Foreground = metaFg,
                                Margin = new Thickness(0, 1, 0, 0)
                            };
                            itemPanel.Children.Add(timeLabel);
                        }

                        var item = new ListBoxItem
                        {
                            Content = itemPanel,
                            Tag = version.SqlText,
                            ToolTip = $"Version {versionNumber} - {version.SavedAt}"
                        };
                        _versionListBox.Items.Add(item);

                        versionNumber--;
                    }

                    // Update version count in metadata bar
                    if (_metadataVersionLabel != null && total > 0)
                    {
                        _metadataVersionLabel.Text = $"v{total} of {total}";
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
            if (_codePreviewTextBlock == null || _versionListBox == null) return;

            if (_versionListBox.SelectedItem is ListBoxItem item && item.Tag is string versionSql)
            {
                _codePreviewTextBlock.Inlines.Clear();
                _codePreviewTextBlock.Inlines.Add(new Run(versionSql));

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
        /// Handles the Rename context menu click. Shows a simple input dialog
        /// and sends an IPC message to update the entry's tab_title.
        /// </summary>
        private async void OnRenameMenuItemClick(object sender, RoutedEventArgs e)
        {
            var entry = _viewModel.SelectedEntry;
            if (entry == null) return;

            // Show a simple WPF input dialog for the new name
            var currentName = entry.TabTitle ?? string.Empty;
            var newName = ShowInputDialog("Rename History Entry", "Enter new name:", currentName);
            if (newName == null || newName == currentName) return;

            try
            {
                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected) return;

                var actionRequest = new HistoryActionRequest
                {
                    Action = HistoryActions.Rename,
                    EntryIds = new[] { entry.Id },
                    NewName = newName
                };

                var response = await client.SendRequestAsync<HistoryActionResponse, HistoryActionRequest>(
                    MessageTypes.HistoryAction, actionRequest, timeoutMs: 5000);

                if (response.Success)
                {
                    // Update the local entry for immediate feedback
                    entry.TabTitle = newName;
                    // Refresh the list
                    _viewModel.SearchCommand.Execute(null);
                }
                else
                {
                    Serilog.Log.Warning("HistoryToolWindowControl: rename failed: {Error}", response.Error);
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "HistoryToolWindowControl: rename failed");
            }
        }

        /// <summary>
        /// Shows a simple WPF input dialog and returns the entered text, or null if cancelled.
        /// </summary>
        private static string? ShowInputDialog(string title, string prompt, string defaultValue)
        {
            var theme = ThemeManager.Instance;

            var dialog = new Window
            {
                Title = title,
                Width = 400,
                Height = 170,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Owner = Application.Current?.MainWindow,
                Background = new SolidColorBrush(theme.HistoryWindowBackground)
            };

            var panel = new StackPanel { Margin = new Thickness(16) };

            var label = new TextBlock
            {
                Text = prompt,
                Margin = new Thickness(0, 0, 0, 8),
                Foreground = new SolidColorBrush(theme.HistoryQueryName)
            };
            panel.Children.Add(label);

            var textBox = new TextBox
            {
                Text = defaultValue,
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(6, 4, 6, 4),
                Background = new SolidColorBrush(theme.HistorySearchBackground),
                Foreground = new SolidColorBrush(theme.HistoryQueryName),
                BorderBrush = new SolidColorBrush(theme.HistorySearchBorder)
            };
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

            // Set owner to prevent dialog from going behind the main VS/SSMS window
            try { dialog.Owner = System.Windows.Application.Current?.MainWindow; }
            catch { /* Non-critical -- centering may not work but dialog still functions */ }

            dialog.ShowDialog();
            return result;
        }

        /// <summary>
        /// Handles clicking on the favorite star icon in the list view.
        /// Finds the associated entry and toggles its favorite status.
        /// </summary>
        private void OnFavoriteStarClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock textBlock && textBlock.DataContext is HistoryEntryDto entry)
            {
                // Temporarily set the selected entry to this entry for the toggle command
                _viewModel.SelectedEntry = entry;
                if (_viewModel.ToggleFavoriteCommand.CanExecute(null))
                {
                    _viewModel.ToggleFavoriteCommand.Execute(null);
                }
                e.Handled = true;
            }
        }

        /// <summary>
        /// Handles clicking on the overflow "..." icon to open the context menu.
        /// </summary>
        private void OnOverflowClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock textBlock && textBlock.DataContext is HistoryEntryDto entry)
            {
                _viewModel.SelectedEntry = entry;

                // Open the query list's context menu at the overflow icon position
                if (_queryListView?.ContextMenu != null)
                {
                    _queryListView.ContextMenu.PlacementTarget = textBlock;
                    _queryListView.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                    _queryListView.ContextMenu.IsOpen = true;
                }
                e.Handled = true;
            }
        }

        /// <summary>
        /// Opens the given SQL text in a new editor tab via DTE,
        /// and sets the connection to the original server/database if available.
        /// </summary>
        private void OnOpenInNewTabRequested(string sqlText, string? server, string? database)
        {
            try
            {
                var dte = (EnvDTE.DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte == null)
                {
                    Serilog.Log.Warning("HistoryToolWindowControl: DTE service unavailable");
                    return;
                }

                dte.ItemOperations.NewFile(
                    @"General\Sql File",
                    "History.sql",
                    EnvDTE.Constants.vsViewKindCode);

                var activeDoc = dte.ActiveDocument;
                var textDocument = activeDoc?.Object("TextDocument") as EnvDTE.TextDocument;
                if (textDocument != null)
                {
                    var editPoint = textDocument.StartPoint.CreateEditPoint();
                    editPoint.Insert(sqlText);
                    textDocument.Selection.StartOfDocument();
                }

                // Try to set the connection on the new query window via SSMS ScriptFactory
                if (!string.IsNullOrEmpty(server))
                {
                    try
                    {
                        Serilog.Log.Debug("History: restoring connection to {Server}.{Database}", server, database);
                        var sfType = Type.GetType(
                            "Microsoft.SqlServer.Management.UI.VSIntegration.ScriptFactory, SqlWorkbench.Interfaces");
                        if (sfType != null && activeDoc != null)
                        {
                            var sfProp = sfType.GetProperty("Instance",
                                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                            var scriptFactory = sfProp?.GetValue(null);
                            if (scriptFactory != null)
                            {
                                // Try to get the current script and set its connection
                                var getCurrentScript = sfType.GetMethod("GetCurrentScript");
                                var currentScript = getCurrentScript?.Invoke(scriptFactory, null);
                                if (currentScript != null)
                                {
                                    // Build a connection string and use SetConnectionInfo
                                    var connStr = string.IsNullOrEmpty(database)
                                        ? $"Data Source={server};Integrated Security=True;Trust Server Certificate=True"
                                        : $"Data Source={server};Initial Catalog={database};Integrated Security=True;Trust Server Certificate=True";
                                    var setConn = currentScript.GetType().GetMethod("SetConnectionInfo");
                                    if (setConn != null)
                                    {
                                        setConn.Invoke(currentScript, new object[] { connStr });
                                        Serilog.Log.Information("History: connection set to {Server}.{Database}", server, database);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception connEx)
                    {
                        Serilog.Log.Debug(connEx, "History: connection restore failed (non-fatal)");
                    }
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "HistoryToolWindowControl: failed to open SQL in new tab");
            }
        }

        /// <summary>
        /// Opens the SQL in a new tab and executes it via DTE's ExecuteCommand.
        /// </summary>
        private void OnReExecuteRequested(string sqlText)
        {
            try
            {
                var dte = (EnvDTE.DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte == null)
                {
                    Serilog.Log.Warning("HistoryToolWindowControl: DTE service unavailable");
                    return;
                }

                // Open SQL in a new tab first
                dte.ItemOperations.NewFile(
                    @"General\Sql File",
                    "ReExecute.sql",
                    EnvDTE.Constants.vsViewKindCode);

                var activeDoc = dte.ActiveDocument;
                var textDocument = activeDoc?.Object("TextDocument") as EnvDTE.TextDocument;
                if (textDocument != null)
                {
                    var editPoint = textDocument.StartPoint.CreateEditPoint();
                    editPoint.Insert(sqlText);
                    textDocument.Selection.StartOfDocument();

                    // Execute the query (works in SSMS)
                    try
                    {
                        dte.ExecuteCommand("Query.Execute");
                    }
                    catch (Exception)
                    {
                        // Query.Execute may not be available in all hosts; just open the tab
                        Serilog.Log.Debug("HistoryToolWindowControl: Query.Execute not available, SQL opened in new tab");
                    }
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "HistoryToolWindowControl: failed to re-execute SQL");
            }
        }

        /// <summary>
        /// Shows a side-by-side diff view comparing two SQL texts.
        /// </summary>
        private void OnCompareRequested(string leftSql, string rightSql)
        {
            try
            {
                var diffWindow = new HistoryDiffWindow(leftSql, rightSql);
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
        /// Creates and freezes a SolidColorBrush from a Color.
        /// </summary>
        private static SolidColorBrush Freeze(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        // ================================================================
        // Value Converters
        // ================================================================

        #region Value Converters

        /// <summary>Converts IsOpen bool to open/closed folder icon.</summary>
        private class OpenClosedIconConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                return value is true ? "\U0001F4C2" : "\U0001F4D5"; // open folder vs closed book
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>Converts IsOpen bool to a theme-aware color (green for open, red for closed).</summary>
        private class OpenClosedColorConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                var theme = ThemeManager.Instance;
                var color = value is true ? theme.HistoryOpenIcon : theme.HistoryClosedIcon;
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>
        /// Converts the full HistoryEntryDto to a display name:
        /// TabTitle if set, otherwise first 60 chars of SQL with whitespace collapsed.
        /// </summary>
        private class QueryNameConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value is HistoryEntryDto entry)
                {
                    if (!string.IsNullOrWhiteSpace(entry.TabTitle))
                        return entry.TabTitle;

                    var sql = entry.SqlText ?? string.Empty;
                    var collapsed = System.Text.RegularExpressions.Regex.Replace(sql, @"\s+", " ").Trim();
                    if (collapsed.Length > 60)
                        return collapsed.Substring(0, 60) + "...";
                    return collapsed;
                }
                return "";
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>Formats Server -> Database for the connection line.</summary>
        private class ServerArrowDatabaseConverter : IMultiValueConverter
        {
            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            {
                var server = values.Length > 0 ? values[0] as string : null;
                var database = values.Length > 1 ? values[1] as string : null;

                if (!string.IsNullOrEmpty(server) && !string.IsNullOrEmpty(database))
                    return $"{server}\u2192{database}";
                if (!string.IsNullOrEmpty(server))
                    return server;
                if (!string.IsNullOrEmpty(database))
                    return database;
                return "";
            }

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
                    return local.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
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

        /// <summary>Converts ExecutionStatus int to a color brush.</summary>
        private class StatusColorConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value is int status)
                {
                    return status switch
                    {
                        0 => Brushes.Green,
                        1 => Brushes.Red,
                        2 => Brushes.Orange,
                        _ => Brushes.Gray
                    };
                }
                return Brushes.Gray;
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
                    return local.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
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

        /// <summary>Converts IsFavorite bool to a theme-aware color.</summary>
        private class FavoriteColorConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                var theme = ThemeManager.Instance;
                var color = value is true ? theme.HistoryStarActive : theme.HistoryStarInactive;
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>Formats execution count for deduplicated entries.</summary>
        private class ExecCountConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value is int count && count > 1)
                    return $"Executed {count} times";
                return "";
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>Shows execution count only when greater than 1.</summary>
        private class ExecCountVisibilityConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value is int count && count > 1)
                    return Visibility.Visible;
                return Visibility.Collapsed;
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
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
