#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using AkmlSql.Shell.Shared.Ui;
using AkmlSql.Shell.Shared.Ui.Theme;

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
        private TextBlock? _codePreviewTextBlock;
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
        private TextBlock? _statusLoadingLabel;

        // Centered placeholder overlaid on the query list — distinguishes a pipe-down engine
        // ("History unavailable") from a genuinely empty result ("No queries found").
        private StackPanel? _emptyStateOverlay;
        private TextBlock? _emptyStateText;

        // Infinite-scroll guard — prevents duplicate LoadMore fires while one is in flight.
        private bool _loadMoreInFlight;

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
                ToolTip = "Clear search and filters",
                FocusVisualStyle = FocusVisualStyles.HighStakes,
                Content = BuildClearIcon(),
                Template = BuildBareButtonTemplate()
            };
            clearButton.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty,
                new Binding(nameof(HistoryViewModel.ClearFiltersCommand)));
            DockPanel.SetDock(clearButton, Dock.Right);
            searchDock.Children.Add(clearButton);

            // Search TextBox (fills remaining space) with placeholder overlay.
            var searchBox = new TextBox
            {
                Background = Brushes.Transparent, // theme-independent: lets the parent Border's background show through
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4, 6, 4, 6),
                FontSize = 12,
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
                Text = "Search SQL history...",
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
                FontSize = 12
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

            // Enter key triggers search (keeps SearchCommand + HistorySearchParser path).
            searchBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && _viewModel.SearchCommand.CanExecute(null))
                {
                    _viewModel.SearchCommand.Execute(null);
                }
            };

            searchDock.Children.Add(searchGrid); // LastChildFill \u2014 takes remaining space
            searchBorder.Child = searchDock;
            panel.Children.Add(searchBorder);

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
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _favoritesStarButton = CreateToolbarIconButton(_favoritesStarGlyph, "Show favorites only", (_, __) =>
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
            var sourceButton = CreateToolbarIconButton(BuildSourceIcon(), "Source / Server", null);
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

            toolbar.Children.Add(iconStack);

            var heading = new TextBlock
            {
                Text = "Recent queries",
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            heading.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            toolbar.Children.Add(heading); // LastChildFill

            panel.Children.Add(toolbar);
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

            // Bind ItemsSource through a CollectionViewSource that groups by date bucket.
            var cvs = new CollectionViewSource { Source = _viewModel.Entries };
            cvs.GroupDescriptions.Add(
                new PropertyGroupDescription(nameof(HistoryEntryDto.ExecutedAt), new DateBucketConverter()));
            _queryListView.ItemsSource = cvs.View;

            // Item template: 2-line row per entry.
            _queryListView.ItemTemplate = CreateQueryItemTemplate();

            // ItemContainerStyle: selected item accent (3px left border).
            _queryListView.ItemContainerStyle = CreateQueryItemContainerStyle();

            // GroupStyle: collapsible chevron + bucket name header.
            _queryListView.GroupStyle.Add(CreateDateGroupStyle());

            // Context menu \u2014 all entry actions (Copy/Open/Re-run/Compare/Export/Delete + Rename/Favorite).
            _queryListView.ContextMenu = BuildQueryContextMenu();

            // Events \u2014 the selection chain (preview drive + metadata + version load) is preserved here.
            _queryListView.MouseDoubleClick += OnListViewDoubleClick;
            _queryListView.SelectionChanged += OnListViewSelectionChanged;

            // Infinite scroll \u2014 load more when scrolled near the bottom.
            _queryListView.AddHandler(ScrollViewer.ScrollChangedEvent,
                new ScrollChangedEventHandler(OnQueryListScrollChanged));

            // Overlay a centered empty/disconnected placeholder on top of the list so a pipe-down
            // engine or a no-results search reads clearly instead of a silent blank list.
            var listGrid = new Grid();
            listGrid.Children.Add(_queryListView);
            listGrid.Children.Add(BuildEmptyStateOverlay());
            dock.Children.Add(listGrid);

            UpdateEmptyState();
            return dock;
        }

        /// <summary>Builds the centered, non-interactive placeholder shown over an empty query list.</summary>
        private FrameworkElement BuildEmptyStateOverlay()
        {
            _emptyStateText = new TextBlock
            {
                Text = "No queries found.",
                FontSize = 12,
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
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };
            _emptyStateOverlay.Children.Add(_emptyStateText);
            return _emptyStateOverlay;
        }

        /// <summary>
        /// Shows the centered placeholder over the query list: a distinct "engine not connected"
        /// message when the pipe is down, or "No queries found" for a genuinely empty result.
        /// Hidden while a search is loading or when the list has entries.
        /// </summary>
        private void UpdateEmptyState()
        {
            if (_emptyStateOverlay == null || _emptyStateText == null) return;

            if (_viewModel.IsLoading)
            {
                _emptyStateOverlay.Visibility = Visibility.Collapsed;
                return;
            }
            if (_viewModel.IsDisconnected)
            {
                _emptyStateText.Text = "History unavailable — the AKML engine is not connected.";
                _emptyStateOverlay.Visibility = Visibility.Visible;
                return;
            }
            _emptyStateText.Text = "No queries found.";
            _emptyStateOverlay.Visibility = _viewModel.Entries.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
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
            name.SetBinding(TextBlock.TextProperty, new Binding("Name")); // DataContext is the CollectionViewGroup
            name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            name.SetValue(TextBlock.FontSizeProperty, 10.5);
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

            // Far-left star toggle (reuses FavoriteIconConverter / FavoriteColorConverter + OnFavoriteStarClick).
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
            starText.SetValue(DockPanel.DockProperty, Dock.Left);
            starText.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            starText.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            starText.SetValue(FrameworkElement.CursorProperty, Cursors.Hand);
            starText.SetValue(ToolTipProperty, "Toggle favorite");
            starText.AddHandler(UIElement.MouseLeftButtonDownEvent,
                new MouseButtonEventHandler(OnFavoriteStarClick));
            outerDock.AppendChild(starText);

            // Far-right overflow "\u22EE" button (opens the query context menu).
            var overflowText = new FrameworkElementFactory(typeof(TextBlock));
            overflowText.SetValue(TextBlock.TextProperty, "\u22EE");
            overflowText.SetResourceBinding(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            overflowText.SetValue(TextBlock.FontSizeProperty, 14.0);
            overflowText.SetValue(DockPanel.DockProperty, Dock.Right);
            overflowText.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            overflowText.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 0, 0, 0));
            overflowText.SetValue(FrameworkElement.CursorProperty, Cursors.Hand);
            overflowText.SetValue(ToolTipProperty, "More actions");
            overflowText.AddHandler(UIElement.MouseLeftButtonDownEvent,
                new MouseButtonEventHandler(OnOverflowClick));
            outerDock.AppendChild(overflowText);

            // Center: two-line content stack (fills via LastChildFill).
            var contentStack = new FrameworkElementFactory(typeof(StackPanel));

            // Line 1: filename (QueryNameConverter), bold.
            var nameText = new FrameworkElementFactory(typeof(TextBlock));
            nameText.SetBinding(TextBlock.TextProperty,
                new Binding { Converter = new QueryNameConverter() });
            nameText.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            nameText.SetValue(TextBlock.FontSizeProperty, 12.0);
            nameText.SetResourceBinding(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            nameText.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            nameText.SetValue(TextBlock.MaxHeightProperty, 18.0);
            nameText.SetValue(ToolTipProperty, "Right-click \u2192 Rename to give this query a custom name");
            contentStack.AppendChild(nameText);

            // Line 2: DockPanel \u2014 left: relative time \u00B7 exec count; right: \u25CF server\instance.
            var line2 = new FrameworkElementFactory(typeof(DockPanel));
            line2.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 0));

            // Right side, rendered as "\u25CF server\instance": with Dock.Right the FIRST child added docks
            // rightmost, so add the server text first then the dot \u2014 the dot lands left-adjacent to it.
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
            connText.SetValue(TextBlock.FontSizeProperty, 10.0);
            connText.SetResourceBinding(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            connText.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            connText.SetValue(DockPanel.DockProperty, Dock.Right);
            connText.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            line2.AppendChild(connText);

            var connDot = new FrameworkElementFactory(typeof(TextBlock));
            connDot.SetValue(TextBlock.TextProperty, "\u25CF ");
            connDot.SetBinding(TextBlock.ForegroundProperty,
                new Binding(nameof(HistoryEntryDto.IsOpen))
                {
                    Converter = new OpenClosedColorConverter()
                });
            connDot.SetValue(TextBlock.FontSizeProperty, 9.0);
            connDot.SetValue(DockPanel.DockProperty, Dock.Right);
            connDot.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            line2.AppendChild(connDot);

            // Left (fills): relative time + " \u00B7 " + exec count (separator hidden when count <= 1).
            var leftMeta = new FrameworkElementFactory(typeof(StackPanel));
            leftMeta.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            var timeText = new FrameworkElementFactory(typeof(TextBlock));
            timeText.SetBinding(TextBlock.TextProperty,
                new Binding(nameof(HistoryEntryDto.ExecutedAt))
                {
                    Converter = new RelativeTimeConverter()
                });
            timeText.SetValue(TextBlock.FontSizeProperty, 10.0);
            timeText.SetResourceBinding(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            timeText.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            leftMeta.AppendChild(timeText);

            var dotSep = new FrameworkElementFactory(typeof(TextBlock));
            dotSep.SetValue(TextBlock.TextProperty, " \u00B7 ");
            dotSep.SetValue(TextBlock.FontSizeProperty, 10.0);
            dotSep.SetResourceBinding(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            dotSep.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            dotSep.SetBinding(VisibilityProperty,
                new Binding(nameof(HistoryEntryDto.ExecutionCount))
                {
                    Converter = new ExecCountVisibilityConverter()
                });
            leftMeta.AppendChild(dotSep);

            var execCountText = new FrameworkElementFactory(typeof(TextBlock));
            execCountText.SetBinding(TextBlock.TextProperty,
                new Binding(nameof(HistoryEntryDto.ExecutionCount))
                {
                    Converter = new ExecCountConverter()
                });
            execCountText.SetValue(TextBlock.FontSizeProperty, 10.0);
            execCountText.SetResourceBinding(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            execCountText.SetValue(TextBlock.FontStyleProperty, FontStyles.Italic);
            execCountText.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            execCountText.SetBinding(VisibilityProperty,
                new Binding(nameof(HistoryEntryDto.ExecutionCount))
                {
                    Converter = new ExecCountVisibilityConverter()
                });
            leftMeta.AppendChild(execCountText);

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

            // Spec 030 T074 (FR-041) — remove all entries older than the selected one (favorites kept).
            var removeOlderItem = new MenuItem { Header = "Remove older than this..." };
            removeOlderItem.SetBinding(MenuItem.CommandProperty,
                new Binding(nameof(HistoryViewModel.RemoveOlderThanCommand)));
            contextMenu.Items.Add(removeOlderItem);

            return contextMenu;
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
                FontSize = 10,
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

            dock.Children.Add(_versionListBox);

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
                FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            _codePreviewHeaderTimestamp.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            DockPanel.SetDock(_codePreviewHeaderTimestamp, Dock.Right);
            headerRow.Children.Add(_codePreviewHeaderTimestamp);

            _codePreviewHeaderFilename = new TextBlock
            {
                Text = "Preview",
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
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
                FontSize = 11.5,
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
                FontSize = 10.5,
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
                FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            _metadataServerLabel.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.StatusSuccess);
            metaLeft.Children.Add(_metadataServerLabel);

            // Separator
            var metaSeparator = new TextBlock
            {
                Text = " · ",
                FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            metaSeparator.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            metaLeft.Children.Add(metaSeparator);

            // Database name
            _metadataDatabaseLabel = new TextBlock
            {
                FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            _metadataDatabaseLabel.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            metaLeft.Children.Add(_metadataDatabaseLabel);

            metaBar.Children.Add(metaLeft); // LastChildFill

            DockPanel.SetDock(metaBar, Dock.Bottom);
            dock.Children.Add(metaBar);

            // --- CENTER: monospaced read-only preview in a ScrollViewer (added LAST = fills) ---
            _codePreviewTextBlock = new TextBlock
            {
                FontFamily = AkmlSql.Shell.Shared.Ui.Theme.Typography.MonoFont,
                FontSize = 11.0,
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(10, 6, 10, 6)
            };
            _codePreviewTextBlock.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            _codePreviewTextBlock.SetResourceReference(TextBlock.BackgroundProperty, ThemeTokens.EditorPopupBackground);

            var previewScroll = new ScrollViewer
            {
                Content = _codePreviewTextBlock,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            previewScroll.SetResourceReference(ScrollViewer.BackgroundProperty, ThemeTokens.EditorPopupBackground);

            dock.Children.Add(previewScroll);

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

            // Loading indicator
            _statusLoadingLabel = new TextBlock
            {
                Text = "Loading...",
                FontStyle = FontStyles.Italic,
                FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            _statusLoadingLabel.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
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
                FontSize = 10.5,
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
        /// Updates the code preview with one merged Run-emission pass that composes
        /// (a) SQL syntax coloring (keyword / string / comment foreground from a lightweight tokenizer)
        /// with (b) search-match background highlighting (from <see cref="FindHighlightRegions"/>).
        /// Also refreshes the dark header (filename + ISO timestamp).
        /// </summary>
        private void UpdatePreviewWithHighlighting()
        {
            if (_codePreviewTextBlock == null) return;

            var entry = _viewModel.SelectedEntry;
            if (entry == null)
            {
                _codePreviewTextBlock.Inlines.Clear();
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

            RenderPreview(entry.SqlText ?? string.Empty);
        }

        /// <summary>
        /// Renders <paramref name="sqlText"/> into the preview TextBlock with one merged pass:
        /// syntax-color foreground (live theme tokens) + search-match background. Used by both the
        /// entry-selection preview drive and the version-selection drive so both share coloring.
        /// </summary>
        private void RenderPreview(string sqlText)
        {
            if (_codePreviewTextBlock == null) return;
            _codePreviewTextBlock.Inlines.Clear();

            if (string.IsNullOrEmpty(sqlText)) return;

            // (a) Tokenize ALWAYS (coloring is not gated on search). Spans cover every character so
            //     the concatenated Run text equals sqlText verbatim. Shared Core tokenizer.
            var tokens = AkmlSql.Core.Text.SqlPreviewTokenizer.Tokenize(sqlText);

            // (b) Search-match regions (background only). Empty when no search / no match.
            var regions = new List<HighlightRegion>();
            var searchText = _viewModel.SearchText;
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                var terms = ExtractHighlightTerms(searchText);
                if (terms.Count > 0)
                    regions = FindHighlightRegions(sqlText, terms);
            }

            // Walk token spans, clipping each against the sorted match regions so each emitted Run
            // is a sub-span whose foreground = the token colour and whose background = match highlight
            // iff the sub-span lies inside a region. Run brushes are LIVE-bound (theme-switch safe).
            int regionIdx = 0;
            foreach (var token in tokens)
            {
                int spanStart = token.Start;
                int spanEnd = token.Start + token.Length;
                int cursor = spanStart;

                // Advance past regions that end before this span.
                while (regionIdx < regions.Count &&
                       regions[regionIdx].Start + regions[regionIdx].Length <= spanStart)
                {
                    regionIdx++;
                }

                int localRegion = regionIdx;
                while (cursor < spanEnd)
                {
                    // Find the next region overlapping [cursor, spanEnd).
                    while (localRegion < regions.Count &&
                           regions[localRegion].Start + regions[localRegion].Length <= cursor)
                    {
                        localRegion++;
                    }

                    if (localRegion >= regions.Count || regions[localRegion].Start >= spanEnd)
                    {
                        // No more overlap in this span — emit the remainder unhighlighted.
                        EmitRun(sqlText.Substring(cursor, spanEnd - cursor), token.Kind, highlighted: false);
                        cursor = spanEnd;
                        break;
                    }

                    var region = regions[localRegion];
                    int regionStart = Math.Max(region.Start, cursor);
                    int regionEnd = Math.Min(region.Start + region.Length, spanEnd);

                    // Plain segment before the region.
                    if (regionStart > cursor)
                    {
                        EmitRun(sqlText.Substring(cursor, regionStart - cursor), token.Kind, highlighted: false);
                    }

                    // Highlighted segment (clipped to this span).
                    if (regionEnd > regionStart)
                    {
                        EmitRun(sqlText.Substring(regionStart, regionEnd - regionStart), token.Kind, highlighted: true);
                    }

                    cursor = regionEnd;
                }
            }
        }

        /// <summary>Emits one preview Run with a live-bound foreground (token colour) and optional match background.</summary>
        private void EmitRun(string text, string kind, bool highlighted)
        {
            if (_codePreviewTextBlock == null || text.Length == 0) return;

            var run = new Run(text);

            // Foreground: theme-aware per token kind (NO hardcoded blue). Kinds are the shared
            // AkmlSql.Core.Text.SqlPreviewTokenizer constants.
            string fgKey;
            if (kind == AkmlSql.Core.Text.SqlPreviewTokenizer.KindKeyword) fgKey = ThemeTokens.AccentPrimary;
            else if (kind == AkmlSql.Core.Text.SqlPreviewTokenizer.KindString) fgKey = ThemeTokens.StatusSuccess;
            else if (kind == AkmlSql.Core.Text.SqlPreviewTokenizer.KindComment) fgKey = ThemeTokens.TextSecondary;
            else fgKey = ThemeTokens.TextPrimary;
            run.SetResourceReference(TextElement.ForegroundProperty, fgKey);

            // Background: search-match highlight (live-bound) only on matched sub-spans.
            if (highlighted)
                run.SetResourceReference(TextElement.BackgroundProperty, ThemeTokens.HistoryMatchHighlight);

            _codePreviewTextBlock.Inlines.Add(run);
        }

        /// <summary>
        /// Extracts highlight terms from the search text. Delegates to the shared, canonical
        /// quote-aware extractor <see cref="AkmlSql.Core.Text.HistorySearchTerms.Extract"/> (one
        /// implementation shared with the web History page). This adopts the web's quote-aware rules:
        /// a double-quoted span is one term (quotes stripped); bare AND/OR/NOT are dropped
        /// (case-insensitive); the value of metadata prefixes (server:/db:/database:/name:/starred:/
        /// is:/open:) is dropped entirely while sql: keeps its value; unknown prefixes stay literal;
        /// and a single trailing FTS5 <c>*</c> is stripped.
        /// </summary>
        private static List<string> ExtractHighlightTerms(string searchText) =>
            AkmlSql.Core.Text.HistorySearchTerms.Extract(searchText).ToList();

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
            if (entry == null)
            {
                if (_versionPanelHeader != null)
                    _versionPanelHeader.Text = "HISTORY";
                return;
            }

            // Relabel the LEFT-bottom version sub-panel header "History for <file>".
            if (_versionPanelHeader != null)
                _versionPanelHeader.Text = "History for " + QueryDisplayName(entry);

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

                        var itemPanel = new StackPanel { Margin = new Thickness(4, 4, 4, 4) };

                        var versionLabel = new TextBlock
                        {
                            Text = label,
                            FontWeight = isCurrent ? FontWeights.SemiBold : FontWeights.Normal,
                            FontSize = 11.5
                        };
                        versionLabel.SetResourceReference(TextBlock.ForegroundProperty,
                            isCurrent ? ThemeTokens.TextLink : ThemeTokens.TextSecondary);
                        itemPanel.Children.Add(versionLabel);

                        if (!string.IsNullOrEmpty(timestampText))
                        {
                            var timeLabel = new TextBlock
                            {
                                Text = timestampText,
                                FontSize = 9.5,
                                Margin = new Thickness(0, 1, 0, 0)
                            };
                            timeLabel.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
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
                // Same merged syntax + search highlighting pass as the entry-selection preview.
                RenderPreview(versionSql);

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
            var dialog = new Window
            {
                Title = title,
                Width = 400,
                Height = 170,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize
            };
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

                // Capture the PREVIOUSLY active document's auth mode BEFORE we create
                // the new tab (which will steal focus). This lets us build a connection
                // string that matches the user's current SSMS session (AAD vs Windows)
                // instead of always hardcoding Integrated Security — otherwise history
                // restore would fail for AAD-authenticated users just like Phase A did.
                var preExistingAuth = AkmlSql.Shell.Shared.Editor.SsmsConnectionDetector.AuthMode.Unknown;
                try
                {
                    var prevDoc = dte.ActiveDocument;
                    if (prevDoc != null)
                    {
                        var (mode, _) = AkmlSql.Shell.Shared.Editor.SsmsConnectionDetector.ReadAuthModeFromDocument(prevDoc);
                        preExistingAuth = mode;
                    }
                }
                catch { /* best effort */ }

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
                                    // Match the user's current SSMS auth mode. We can't restore
                                    // a password (SQL auth) or replay an interactive AAD flow, so
                                    // for those modes we fall back to Integrated Security and let
                                    // SSMS prompt the user if the token isn't cached.
                                    string authClause =
                                        preExistingAuth == AkmlSql.Shell.Shared.Editor.SsmsConnectionDetector.AuthMode.AzureAdIntegrated
                                            ? "Authentication=Active Directory Integrated"
                                            : "Integrated Security=True";
                                    var connStr = string.IsNullOrEmpty(database)
                                        ? $"Data Source={server};{authClause};Trust Server Certificate=True"
                                        : $"Data Source={server};Initial Catalog={database};{authClause};Trust Server Certificate=True";
                                    var setConn = currentScript.GetType().GetMethod("SetConnectionInfo");
                                    if (setConn != null)
                                    {
                                        setConn.Invoke(currentScript, new object[] { connStr });
                                        Serilog.Log.Information("History: connection set to {Server}.{Database} (auth={Auth})",
                                            server, database, preExistingAuth);
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
        /// Returns the display name for an entry — its TabTitle, else a collapsed 60-char SQL preview,
        /// else a placeholder. Mirrors <see cref="QueryNameConverter"/> for use by the right-pane filename
        /// header and the "History for &lt;file&gt;" version-panel header.
        /// </summary>
        private static string QueryDisplayName(HistoryEntryDto entry)
        {
            if (entry == null) return "Preview";
            return AkmlSql.Core.Models.History.HistoryDisplayName.Of(entry.TabTitle, entry.SqlText);
        }

        // ================================================================
        // Value Converters
        // ================================================================

        #region Value Converters

        /// <summary>Converts IsOpen bool to a theme-aware brush via Status.Success / Status.Danger.</summary>
        private class OpenClosedColorConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                var key = value is true ? ThemeTokens.StatusSuccess : ThemeTokens.StatusDanger;
                return ThemeRegistry.Instance.Resources[key];
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>
        /// Converts the full HistoryEntryDto to a display name (shared Core helper):
        /// TabTitle if set, otherwise first 60 chars of SQL with whitespace collapsed, otherwise
        /// "(Untitled query)". The "right-click to rename" hint remains the row TextBlock tooltip.
        /// </summary>
        private class QueryNameConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value is HistoryEntryDto entry)
                    return AkmlSql.Core.Models.History.HistoryDisplayName.Of(entry.TabTitle, entry.SqlText);
                return "";
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>Formats Server -> Database for the connection line.</summary>
        /// <summary>
        /// Renders the compact list-row connection label as just the server\instance \u2014 SQL Prompt
        /// shows only the server here; the database is surfaced in the right-pane metadata bar, so
        /// the old "server\u2192database" suffix made the rows busier than the reference. Falls back to the
        /// database name when no server is recorded.
        /// </summary>
        private class ServerLabelConverter : IMultiValueConverter
        {
            public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
            {
                var server = values.Length > 0 ? values[0] as string : null;
                var database = values.Length > 1 ? values[1] as string : null;

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

        /// <summary>Converts IsFavorite bool to a theme-aware brush: Status.Warning when active, Text.Disabled otherwise.</summary>
        private class FavoriteColorConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                var key = value is true ? ThemeTokens.StatusWarning : ThemeTokens.TextDisabled;
                return ThemeRegistry.Instance.Resources[key];
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
