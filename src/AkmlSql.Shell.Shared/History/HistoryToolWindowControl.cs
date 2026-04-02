#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// WPF UserControl that provides the UI for the SQL History tool window.
    /// Built programmatically (no XAML) for Shell.Shared compatibility across all 6 targets.
    /// Features: search bar, filter dropdowns, date pickers, virtualized list view,
    /// day-grouped entries with status icons, action buttons (copy, open, re-execute,
    /// compare, toggle favorite, delete, export), and inline diff view.
    /// </summary>
    internal class HistoryToolWindowControl : UserControl
    {
        private readonly HistoryViewModel _viewModel;
        private ListView? _listView;
        private TextBlock? _previewTextBlock;
        private ListBox? _versionListBox;

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

        private void BuildUi()
        {
            var theme = ThemeManager.Instance;
            var bg = new SolidColorBrush(theme.Background);
            var fg = new SolidColorBrush(theme.Foreground);
            var border = new SolidColorBrush(theme.Border);
            bg.Freeze();
            fg.Freeze();
            border.Freeze();

            // Main layout: search bar on top, list in center, preview/versions, status, action bar
            var mainGrid = new Grid { Background = bg };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Search/filter bar
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) }); // ListView
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Preview + Versions
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Status bar / load more
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Action buttons

            // ================================================================
            // ROW 0: Search and filter bar
            // ================================================================
            var filterPanel = BuildFilterPanel(bg, fg, border);
            Grid.SetRow(filterPanel, 0);
            mainGrid.Children.Add(filterPanel);

            // ================================================================
            // ROW 1: History entries ListView with virtualization
            // ================================================================
            _listView = BuildListView(bg, fg, border);
            Grid.SetRow(_listView, 1);
            mainGrid.Children.Add(_listView);

            // ================================================================
            // ROW 2: Preview panel (code preview + version history)
            // ================================================================
            var previewPanel = BuildPreviewPanel(bg, fg, border);
            Grid.SetRow(previewPanel, 2);
            mainGrid.Children.Add(previewPanel);

            // ================================================================
            // ROW 3: Status bar with total count and Load More button
            // ================================================================
            var statusBar = BuildStatusBar(bg, fg);
            Grid.SetRow(statusBar, 3);
            mainGrid.Children.Add(statusBar);

            // ================================================================
            // ROW 4: Action buttons
            // ================================================================
            var actionBar = BuildActionBar(bg, fg, border);
            Grid.SetRow(actionBar, 4);
            mainGrid.Children.Add(actionBar);

            Content = mainGrid;
        }

        private StackPanel BuildFilterPanel(Brush bg, Brush fg, Brush border)
        {
            var panel = new StackPanel { Margin = new Thickness(4) };

            // Row 1: Search text box + Search/Clear buttons
            var searchRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };

            var clearBtn = new Button
            {
                Content = "Clear",
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(2, 0, 0, 0),
                ToolTip = "Clear all filters"
            };
            clearBtn.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty,
                new Binding(nameof(HistoryViewModel.ClearFiltersCommand)));
            DockPanel.SetDock(clearBtn, Dock.Right);
            searchRow.Children.Add(clearBtn);

            var searchBtn = new Button
            {
                Content = "Search",
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(2, 0, 0, 0),
                ToolTip = "Search history"
            };
            searchBtn.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty,
                new Binding(nameof(HistoryViewModel.SearchCommand)));
            DockPanel.SetDock(searchBtn, Dock.Right);
            searchRow.Children.Add(searchBtn);

            var searchBox = new TextBox
            {
                Padding = new Thickness(4, 2, 4, 2),
                ToolTip = "Search SQL text (full-text search)"
            };
            searchBox.SetBinding(TextBox.TextProperty,
                new Binding(nameof(HistoryViewModel.SearchText))
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                });
            // Enter key triggers search
            searchBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && _viewModel.SearchCommand.CanExecute(null))
                {
                    _viewModel.SearchCommand.Execute(null);
                }
            };
            searchRow.Children.Add(searchBox);

            panel.Children.Add(searchRow);

            // Row 2: Filter dropdowns (Server, Database, Status, Date range, Favorites)
            var filterRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };

            // Server filter
            filterRow.Children.Add(new Label { Content = "Server:", Foreground = fg, Padding = new Thickness(0, 2, 2, 2) });
            var serverCombo = new ComboBox
            {
                Width = 120,
                IsEditable = true,
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "Filter by server"
            };
            serverCombo.SetBinding(ItemsControl.ItemsSourceProperty,
                new Binding(nameof(HistoryViewModel.Servers)));
            serverCombo.SetBinding(ComboBox.TextProperty,
                new Binding(nameof(HistoryViewModel.SelectedServer))
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                });
            filterRow.Children.Add(serverCombo);

            // Database filter
            filterRow.Children.Add(new Label { Content = "Database:", Foreground = fg, Padding = new Thickness(0, 2, 2, 2) });
            var dbCombo = new ComboBox
            {
                Width = 120,
                IsEditable = true,
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "Filter by database"
            };
            dbCombo.SetBinding(ItemsControl.ItemsSourceProperty,
                new Binding(nameof(HistoryViewModel.Databases)));
            dbCombo.SetBinding(ComboBox.TextProperty,
                new Binding(nameof(HistoryViewModel.SelectedDatabase))
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                });
            filterRow.Children.Add(dbCombo);

            // Status filter
            filterRow.Children.Add(new Label { Content = "Status:", Foreground = fg, Padding = new Thickness(0, 2, 2, 2) });
            var statusCombo = new ComboBox
            {
                Width = 90,
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "Filter by execution status"
            };
            statusCombo.Items.Add(new ComboBoxItem { Content = "(All)", Tag = null });
            statusCombo.Items.Add(new ComboBoxItem { Content = "Success", Tag = 0 });
            statusCombo.Items.Add(new ComboBoxItem { Content = "Error", Tag = 1 });
            statusCombo.Items.Add(new ComboBoxItem { Content = "Cancelled", Tag = 2 });
            statusCombo.SelectedIndex = 0;
            statusCombo.SelectionChanged += (s, e) =>
            {
                if (statusCombo.SelectedItem is ComboBoxItem item)
                {
                    _viewModel.SelectedStatus = item.Tag as int?;
                }
            };
            filterRow.Children.Add(statusCombo);

            // Favorites toggle
            var favCheckBox = new CheckBox
            {
                Content = "Favorites only",
                Foreground = fg,
                Margin = new Thickness(0, 2, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Show only favorite entries"
            };
            favCheckBox.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
                new Binding(nameof(HistoryViewModel.FavoritesOnly))
                {
                    Mode = BindingMode.TwoWay
                });
            filterRow.Children.Add(favCheckBox);

            panel.Children.Add(filterRow);

            // Row 3: Date range pickers
            var dateRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };

            dateRow.Children.Add(new Label { Content = "From:", Foreground = fg, Padding = new Thickness(0, 2, 2, 2) });
            var dateFromPicker = new DatePicker
            {
                Width = 130,
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "Start date filter"
            };
            dateFromPicker.SetBinding(DatePicker.SelectedDateProperty,
                new Binding(nameof(HistoryViewModel.DateFrom))
                {
                    Mode = BindingMode.TwoWay
                });
            dateRow.Children.Add(dateFromPicker);

            dateRow.Children.Add(new Label { Content = "To:", Foreground = fg, Padding = new Thickness(0, 2, 2, 2) });
            var dateToPicker = new DatePicker
            {
                Width = 130,
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "End date filter"
            };
            dateToPicker.SetBinding(DatePicker.SelectedDateProperty,
                new Binding(nameof(HistoryViewModel.DateTo))
                {
                    Mode = BindingMode.TwoWay
                });
            dateRow.Children.Add(dateToPicker);

            panel.Children.Add(dateRow);

            return panel;
        }

        private ListView BuildListView(Brush bg, Brush fg, Brush border)
        {
            var listView = new ListView
            {
                Background = bg,
                Foreground = fg,
                BorderBrush = border,
                BorderThickness = new Thickness(0, 1, 0, 1),
                Margin = new Thickness(0),
                SelectionMode = SelectionMode.Extended // Allow multi-select for Compare and Delete
            };

            // Enable virtualization for performance with large history
            VirtualizingPanel.SetIsVirtualizing(listView, true);
            VirtualizingPanel.SetVirtualizationMode(listView, VirtualizationMode.Recycling);
            ScrollViewer.SetCanContentScroll(listView, true);

            // Set ItemsSource binding
            listView.SetBinding(ItemsControl.ItemsSourceProperty,
                new Binding(nameof(HistoryViewModel.Entries)));

            // Create a GridView for columnar display
            var gridView = new GridView();

            // Favorite star column (clickable to toggle)
            gridView.Columns.Add(new GridViewColumn
            {
                Header = "",
                Width = 30,
                CellTemplate = CreateFavoriteTemplate()
            });

            // Status icon column
            gridView.Columns.Add(new GridViewColumn
            {
                Header = "",
                Width = 28,
                CellTemplate = CreateStatusIconTemplate()
            });

            // Time column
            gridView.Columns.Add(new GridViewColumn
            {
                Header = "Time",
                Width = 130,
                CellTemplate = CreateTextTemplate(nameof(HistoryEntryDto.ExecutedAt),
                    new ExecutedAtConverter())
            });

            // SQL preview column
            gridView.Columns.Add(new GridViewColumn
            {
                Header = "SQL",
                Width = 350,
                CellTemplate = CreateSqlPreviewTemplate()
            });

            // Server > Database column
            gridView.Columns.Add(new GridViewColumn
            {
                Header = "Connection",
                Width = 160,
                CellTemplate = CreateConnectionTemplate()
            });

            // Duration column
            gridView.Columns.Add(new GridViewColumn
            {
                Header = "Duration",
                Width = 75,
                CellTemplate = CreateTextTemplate(nameof(HistoryEntryDto.DurationMs),
                    new DurationConverter())
            });

            // Row count column
            gridView.Columns.Add(new GridViewColumn
            {
                Header = "Rows",
                Width = 65,
                CellTemplate = CreateTextTemplate(nameof(HistoryEntryDto.RowCount))
            });

            listView.View = gridView;

            // Build context menu for right-click actions
            var contextMenu = new ContextMenu();
            var copySqlMenuItem = new MenuItem { Header = "Copy SQL" };
            copySqlMenuItem.SetBinding(MenuItem.CommandProperty,
                new Binding(nameof(HistoryViewModel.CopySqlCommand)));
            contextMenu.Items.Add(copySqlMenuItem);

            var openMenuItem = new MenuItem { Header = "Open in New Tab" };
            openMenuItem.SetBinding(MenuItem.CommandProperty,
                new Binding(nameof(HistoryViewModel.OpenInNewTabCommand)));
            contextMenu.Items.Add(openMenuItem);

            contextMenu.Items.Add(new Separator());

            var renameMenuItem = new MenuItem { Header = "Rename" };
            renameMenuItem.Click += OnRenameMenuItemClick;
            contextMenu.Items.Add(renameMenuItem);

            var favMenuItem = new MenuItem { Header = "Toggle Favorite" };
            favMenuItem.SetBinding(MenuItem.CommandProperty,
                new Binding(nameof(HistoryViewModel.ToggleFavoriteCommand)));
            contextMenu.Items.Add(favMenuItem);

            contextMenu.Items.Add(new Separator());

            var deleteMenuItem = new MenuItem { Header = "Delete" };
            deleteMenuItem.SetBinding(MenuItem.CommandProperty,
                new Binding(nameof(HistoryViewModel.DeleteCommand)));
            contextMenu.Items.Add(deleteMenuItem);

            listView.ContextMenu = contextMenu;

            // Double-click to copy SQL to clipboard
            listView.MouseDoubleClick += OnListViewDoubleClick;

            // Selection changed: update ViewModel's selected entries for multi-select actions
            listView.SelectionChanged += OnListViewSelectionChanged;

            return listView;
        }

        private DockPanel BuildStatusBar(Brush bg, Brush fg)
        {
            var statusBar = new DockPanel
            {
                Background = bg,
                Margin = new Thickness(4, 2, 4, 2)
            };

            // Load More button on the right
            var loadMoreBtn = new Button
            {
                Content = "Load More",
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(4, 0, 0, 0)
            };
            loadMoreBtn.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty,
                new Binding(nameof(HistoryViewModel.LoadMoreCommand)));
            loadMoreBtn.SetBinding(VisibilityProperty,
                new Binding(nameof(HistoryViewModel.HasMoreEntries))
                {
                    Converter = new BoolToVisibilityConverter()
                });
            DockPanel.SetDock(loadMoreBtn, Dock.Right);
            statusBar.Children.Add(loadMoreBtn);

            // Loading indicator
            var loadingLabel = new Label
            {
                Content = "Loading...",
                Foreground = fg,
                FontStyle = FontStyles.Italic
            };
            loadingLabel.SetBinding(VisibilityProperty,
                new Binding(nameof(HistoryViewModel.IsLoading))
                {
                    Converter = new BoolToVisibilityConverter()
                });
            DockPanel.SetDock(loadingLabel, Dock.Right);
            statusBar.Children.Add(loadingLabel);

            // Total count display
            var countLabel = new Label { Foreground = fg };
            countLabel.SetBinding(ContentProperty,
                new Binding(nameof(HistoryViewModel.TotalCount))
                {
                    StringFormat = "{0} entries found"
                });
            statusBar.Children.Add(countLabel);

            return statusBar;
        }

        private StackPanel BuildActionBar(Brush bg, Brush fg, Brush border)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(4, 2, 4, 4),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            var buttonStyle = new Style(typeof(Button));
            buttonStyle.Setters.Add(new Setter(PaddingProperty, new Thickness(8, 2, 8, 2)));
            buttonStyle.Setters.Add(new Setter(MarginProperty, new Thickness(0, 0, 4, 0)));

            // "Copy SQL" button — copies selected entry's full SQL to clipboard
            var copyBtn = new Button { Content = "Copy SQL", Style = buttonStyle, ToolTip = "Copy full SQL text to clipboard" };
            copyBtn.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty,
                new Binding(nameof(HistoryViewModel.CopySqlCommand)));
            panel.Children.Add(copyBtn);

            // "Open in New Tab" button — opens full SQL in a new editor tab
            var openBtn = new Button { Content = "Open in New Tab", Style = buttonStyle, ToolTip = "Open SQL in a new editor tab" };
            openBtn.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty,
                new Binding(nameof(HistoryViewModel.OpenInNewTabCommand)));
            panel.Children.Add(openBtn);

            // "Re-execute" button — re-executes the selected query
            var reexecBtn = new Button { Content = "Re-execute", Style = buttonStyle, ToolTip = "Re-execute the selected query" };
            reexecBtn.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty,
                new Binding(nameof(HistoryViewModel.ReExecuteCommand)));
            panel.Children.Add(reexecBtn);

            // "Compare" button — compares two selected entries side-by-side
            var compareBtn = new Button { Content = "Compare", Style = buttonStyle, ToolTip = "Compare two selected entries (select exactly 2)" };
            compareBtn.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty,
                new Binding(nameof(HistoryViewModel.CompareCommand)));
            panel.Children.Add(compareBtn);

            // Separator
            panel.Children.Add(new Border
            {
                Width = 1,
                Background = border,
                Margin = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Stretch
            });

            // "Toggle Favorite" button — toggles favorite on the selected entry
            var favBtn = new Button { Content = "\u2605 Favorite", Style = buttonStyle, ToolTip = "Toggle favorite on selected entry" };
            favBtn.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty,
                new Binding(nameof(HistoryViewModel.ToggleFavoriteCommand)));
            panel.Children.Add(favBtn);

            // "Delete" button — deletes selected entries
            var deleteBtn = new Button { Content = "Delete", Style = buttonStyle, ToolTip = "Delete selected entries" };
            deleteBtn.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty,
                new Binding(nameof(HistoryViewModel.DeleteCommand)));
            panel.Children.Add(deleteBtn);

            // "Export" button — exports filtered history
            var exportBtn = new Button { Content = "Export", Style = buttonStyle, ToolTip = "Export history entries to file (CSV, JSON, or SQL)" };
            exportBtn.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty,
                new Binding(nameof(HistoryViewModel.ExportCommand)));
            panel.Children.Add(exportBtn);

            return panel;
        }

        /// <summary>
        /// Builds the preview panel with SQL code preview (with search highlighting)
        /// and a version history list on the right side.
        /// </summary>
        private Grid BuildPreviewPanel(Brush bg, Brush fg, Brush border)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // SQL preview with highlighting
            _previewTextBlock = new TextBlock
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11.0,
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(4),
                Foreground = fg,
                Background = bg
            };
            var previewScroll = new ScrollViewer
            {
                Content = _previewTextBlock,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                BorderBrush = border,
                BorderThickness = new Thickness(0, 0, 1, 0)
            };
            Grid.SetColumn(previewScroll, 0);
            grid.Children.Add(previewScroll);

            // Version history list
            var versionPanel = new DockPanel { Background = bg };

            var versionHeader = new TextBlock
            {
                Text = "Versions",
                FontWeight = FontWeights.SemiBold,
                Foreground = fg,
                Padding = new Thickness(4, 2, 4, 2)
            };
            DockPanel.SetDock(versionHeader, Dock.Top);
            versionPanel.Children.Add(versionHeader);

            _versionListBox = new ListBox
            {
                Background = bg,
                Foreground = fg,
                BorderThickness = new Thickness(0)
            };
            _versionListBox.SelectionChanged += OnVersionSelectionChanged;
            versionPanel.Children.Add(_versionListBox);

            Grid.SetColumn(versionPanel, 1);
            grid.Children.Add(versionPanel);

            return grid;
        }

        #region DataTemplate Factories

        private static DataTemplate CreateStatusIconTemplate()
        {
            var template = new DataTemplate(typeof(HistoryEntryDto));
            var factory = new FrameworkElementFactory(typeof(TextBlock));
            factory.SetBinding(TextBlock.TextProperty,
                new Binding(nameof(HistoryEntryDto.Status))
                {
                    Converter = new StatusIconConverter()
                });
            factory.SetBinding(TextBlock.ForegroundProperty,
                new Binding(nameof(HistoryEntryDto.Status))
                {
                    Converter = new StatusColorConverter()
                });
            factory.SetValue(TextBlock.FontSizeProperty, 14.0);
            factory.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
            factory.SetValue(ToolTipProperty, new Binding(nameof(HistoryEntryDto.Status))
            {
                Converter = new StatusTextConverter()
            });
            template.VisualTree = factory;
            return template;
        }

        private static DataTemplate CreateTextTemplate(string propertyName, IValueConverter? converter = null)
        {
            var template = new DataTemplate(typeof(HistoryEntryDto));
            var factory = new FrameworkElementFactory(typeof(TextBlock));
            var binding = new Binding(propertyName);
            if (converter != null) binding.Converter = converter;
            factory.SetBinding(TextBlock.TextProperty, binding);
            factory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            template.VisualTree = factory;
            return template;
        }

        private static DataTemplate CreateSqlPreviewTemplate()
        {
            var template = new DataTemplate(typeof(HistoryEntryDto));

            var panel = new FrameworkElementFactory(typeof(StackPanel));

            // SQL text preview (first ~200 chars, single line)
            var sqlText = new FrameworkElementFactory(typeof(TextBlock));
            sqlText.SetBinding(TextBlock.TextProperty, new Binding(nameof(HistoryEntryDto.SqlText))
            {
                Converter = new SqlPreviewTrimConverter()
            });
            sqlText.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            sqlText.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Consolas"));
            sqlText.SetValue(TextBlock.FontSizeProperty, 11.0);
            panel.AppendChild(sqlText);

            // Execution count line (only shown when deduplicated)
            var execCount = new FrameworkElementFactory(typeof(TextBlock));
            execCount.SetBinding(TextBlock.TextProperty, new Binding(nameof(HistoryEntryDto.ExecutionCount))
            {
                Converter = new ExecCountConverter()
            });
            execCount.SetValue(TextBlock.FontSizeProperty, 10.0);
            execCount.SetValue(TextBlock.ForegroundProperty, Brushes.Gray);
            execCount.SetBinding(VisibilityProperty,
                new Binding(nameof(HistoryEntryDto.ExecutionCount))
                {
                    Converter = new ExecCountVisibilityConverter()
                });
            panel.AppendChild(execCount);

            template.VisualTree = panel;
            return template;
        }

        private static DataTemplate CreateConnectionTemplate()
        {
            var template = new DataTemplate(typeof(HistoryEntryDto));
            var factory = new FrameworkElementFactory(typeof(TextBlock));
            factory.SetBinding(TextBlock.TextProperty, new MultiBinding
            {
                Converter = new ConnectionInfoConverter(),
                Bindings =
                {
                    new Binding(nameof(HistoryEntryDto.Server)),
                    new Binding(nameof(HistoryEntryDto.Database)),
                    new Binding(nameof(HistoryEntryDto.Username))
                }
            });
            factory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            factory.SetValue(TextBlock.FontSizeProperty, 10.0);
            template.VisualTree = factory;
            return template;
        }

        /// <summary>
        /// Creates the favorite column template with a clickable star icon.
        /// Filled star for favorites, empty star outline for non-favorites.
        /// </summary>
        private DataTemplate CreateFavoriteTemplate()
        {
            var template = new DataTemplate(typeof(HistoryEntryDto));
            var factory = new FrameworkElementFactory(typeof(TextBlock));
            factory.SetBinding(TextBlock.TextProperty,
                new Binding(nameof(HistoryEntryDto.IsFavorite))
                {
                    Converter = new FavoriteIconConverter()
                });
            factory.SetBinding(TextBlock.ForegroundProperty,
                new Binding(nameof(HistoryEntryDto.IsFavorite))
                {
                    Converter = new FavoriteColorConverter()
                });
            factory.SetValue(TextBlock.FontSizeProperty, 14.0);
            factory.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
            factory.SetValue(CursorProperty, Cursors.Hand);
            factory.SetValue(ToolTipProperty, "Click to toggle favorite");

            // Handle click on the star to toggle favorite
            factory.AddHandler(MouseLeftButtonDownEvent,
                new MouseButtonEventHandler(OnFavoriteStarClick));

            template.VisualTree = factory;
            return template;
        }

        #endregion

        #region Event Handlers

        private void OnListViewDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel.CopySqlCommand.CanExecute(null))
            {
                _viewModel.CopySqlCommand.Execute(null);
            }
        }

        private void OnListViewSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_listView != null)
            {
                _viewModel.UpdateSelectedEntries(_listView.SelectedItems);
            }

            // Update the code preview with search highlighting
            UpdatePreviewWithHighlighting();

            // Load version history for the selected entry
            LoadVersionHistory();
        }

        /// <summary>
        /// Updates the code preview TextBlock with search match highlighting.
        /// When SearchText is active, matching segments get a yellow background.
        /// </summary>
        private void UpdatePreviewWithHighlighting()
        {
            if (_previewTextBlock == null) return;
            _previewTextBlock.Inlines.Clear();

            var entry = _viewModel.SelectedEntry;
            if (entry == null) return;

            var sqlText = entry.SqlText ?? string.Empty;
            var searchText = _viewModel.SearchText;

            if (string.IsNullOrWhiteSpace(searchText))
            {
                // No search active: plain text
                _previewTextBlock.Inlines.Add(new Run(sqlText));
                return;
            }

            // Highlight matching segments with yellow background
            var highlightBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0x3B));
            highlightBrush.Freeze();

            int pos = 0;
            while (pos < sqlText.Length)
            {
                int matchIdx = sqlText.IndexOf(searchText, pos, StringComparison.OrdinalIgnoreCase);
                if (matchIdx < 0)
                {
                    // No more matches: add remaining text
                    _previewTextBlock.Inlines.Add(new Run(sqlText.Substring(pos)));
                    break;
                }

                // Add non-matching segment before the match
                if (matchIdx > pos)
                {
                    _previewTextBlock.Inlines.Add(new Run(sqlText.Substring(pos, matchIdx - pos)));
                }

                // Add highlighted matching segment
                var matchRun = new Run(sqlText.Substring(matchIdx, searchText.Length))
                {
                    Background = highlightBrush
                };
                _previewTextBlock.Inlines.Add(matchRun);

                pos = matchIdx + searchText.Length;
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
                    foreach (var version in response.Versions)
                    {
                        var item = new ListBoxItem
                        {
                            Content = version.SavedAt,
                            Tag = version.SqlText,
                            ToolTip = $"Version {version.Id} - {version.SavedAt}"
                        };
                        _versionListBox.Items.Add(item);
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
            if (_previewTextBlock == null || _versionListBox == null) return;

            if (_versionListBox.SelectedItem is ListBoxItem item && item.Tag is string versionSql)
            {
                _previewTextBlock.Inlines.Clear();
                _previewTextBlock.Inlines.Add(new Run(versionSql));
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
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize
            };

            var panel = new StackPanel { Margin = new Thickness(12) };

            var label = new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) };
            panel.Children.Add(label);

            var textBox = new TextBox
            {
                Text = defaultValue,
                Margin = new Thickness(0, 0, 0, 12)
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
            catch { /* Non-critical — centering may not work but dialog still functions */ }

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
        /// Opens the given SQL text in a new editor tab via DTE.
        /// </summary>
        private void OnOpenInNewTabRequested(string sqlText)
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

        #region Value Converters

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

                var parts = new System.Collections.Generic.List<string>();
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

        /// <summary>Converts IsFavorite bool to a color (gold for favorites, gray for non-favorites).</summary>
        private class FavoriteColorConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                return value is true ? Brushes.Gold : Brushes.Gray;
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
