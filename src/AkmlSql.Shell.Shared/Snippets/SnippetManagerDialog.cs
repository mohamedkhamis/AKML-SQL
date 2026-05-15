#nullable enable
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ui;
using AkmlSql.Shell.Shared.Ui.Theme;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.Win32;

namespace AkmlSql.Shell.Shared.Snippets
{
    /// <summary>
    /// WPF DialogWindow for managing SQL code snippets.
    /// Left pane: search + categorized snippet list.
    /// Right pane: edit fields (shortcode, name, description, category, tags, body).
    /// Bottom: New, Duplicate, Delete, Import, Export, Save, Close buttons.
    /// Programmatic WPF only (no XAML) for Shell.Shared compatibility.
    /// </summary>
    internal sealed class SnippetManagerDialog : DialogWindow
    {
        private readonly SnippetManagerViewModel _viewModel;
        private ListBox? _snippetList;
        private TextBox? _searchBox;
        private TextBox? _shortcodeBox;
        private TextBox? _nameBox;
        private TextBox? _descriptionBox;
        private TextBox? _categoryBox;
        private TextBox? _tagsBox;
        private TextBox? _bodyBox;
        private Button? _deleteButton;
        private Button? _duplicateButton;
        private Button? _saveButton;
        private TextBlock? _statusText;

        public SnippetManagerDialog(SnippetManagerViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            Title = "AKML SQL - Snippet Manager";
            Width = 900;
            Height = 650;
            MinWidth = 700;
            MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            HasHelpButton = false;

            BuildUi();
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            ((INotifyCollectionChanged)_viewModel.FilteredSnippets).CollectionChanged += OnFilteredSnippetsChanged;
            _viewModel.LoadSnippets();
        }

        private void BuildUi()
        {
            // Spec 020 (US1 T021): replaced legacy ThemeManager.Instance.<Color> reads with direct
            // brush lookups from ThemeRegistry. Brushes are already frozen at palette-build time
            // (ThemePalette), so the per-call allocation + Freeze() noise is no longer needed.
            var res = ThemeRegistry.Instance.Resources;
            var bg          = (SolidColorBrush)res[ThemeTokens.SurfaceCanvas];
            var fg          = (SolidColorBrush)res[ThemeTokens.TextPrimary];
            var border      = (SolidColorBrush)res[ThemeTokens.BorderDefault];
            var highlight   = (SolidColorBrush)res[ThemeTokens.SurfaceHover];
            var accent      = (SolidColorBrush)res[ThemeTokens.AccentPrimary];
            var editorPanel = (SolidColorBrush)res[ThemeTokens.SurfaceElevated];
            var placeholder = (SolidColorBrush)res[ThemeTokens.TextPlaceholder];
            var splitter    = (SolidColorBrush)res[ThemeTokens.BorderSplitter];

            // ================================================================
            // MAIN GRID: 3 rows — content (Star), status bar (Auto), button bar (Auto)
            // ================================================================
            var mainGrid = new Grid { Background = bg };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ================================================================
            // CONTENT: 3 columns — left list (250), splitter (5), right editor (Star)
            // ================================================================
            var contentGrid = new Grid();
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(contentGrid, 0);
            mainGrid.Children.Add(contentGrid);

            // ================================================================
            // LEFT PANEL: Search + ListBox
            // ================================================================
            var leftPanel = new Grid { Margin = new Thickness(6) };
            leftPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Search
            leftPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // List

            // Search box with placeholder
            var searchPanel = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            _searchBox = new TextBox
            {
                Padding = new Thickness(4, 3, 4, 3),
                Background = editorPanel,
                Foreground = fg,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
            };
            _searchBox.TextChanged += OnSearchTextChanged;

            var searchPlaceholder = new TextBlock
            {
                Text = "Search snippets...",
                Foreground = placeholder,
                Margin = new Thickness(6, 4, 0, 0),
                IsHitTestVisible = false,
            };
            _searchBox.TextChanged += (s, _) =>
            {
                searchPlaceholder.Visibility = string.IsNullOrEmpty(_searchBox.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            };

            searchPanel.Children.Add(_searchBox);
            searchPanel.Children.Add(searchPlaceholder);
            Grid.SetRow(searchPanel, 0);
            leftPanel.Children.Add(searchPanel);

            // Snippet list
            _snippetList = new ListBox
            {
                Background = editorPanel,
                Foreground = fg,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
            };
            _snippetList.SelectionChanged += OnSnippetListSelectionChanged;
            Grid.SetRow(_snippetList, 1);
            leftPanel.Children.Add(_snippetList);

            Grid.SetColumn(leftPanel, 0);
            contentGrid.Children.Add(leftPanel);

            // ================================================================
            // SPLITTER
            // ================================================================
            var gridSplitter = new GridSplitter
            {
                Width = 5,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = splitter,
            };
            Grid.SetColumn(gridSplitter, 1);
            contentGrid.Children.Add(gridSplitter);

            // ================================================================
            // RIGHT PANEL: Edit fields + Body
            // ================================================================
            var rightPanel = new Grid { Margin = new Thickness(6) };
            // Rows: Shortcode, Name, Description, Category, Tags, Body label, Body editor
            rightPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Shortcode
            rightPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: Name
            rightPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: Description
            rightPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 3: Category
            rightPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 4: Tags
            rightPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 5: Separator
            rightPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 6: Body label
            rightPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 7: Body editor

            // 2 columns: label (Auto) + field (Star)
            rightPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rightPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // --- Row 0: Shortcode ---
            var shortcodeLabel = CreateFieldLabel("Shortcode:", fg);
            Grid.SetRow(shortcodeLabel, 0);
            Grid.SetColumn(shortcodeLabel, 0);
            rightPanel.Children.Add(shortcodeLabel);

            _shortcodeBox = CreateFieldTextBox(fg, editorPanel, border);
            Grid.SetRow(_shortcodeBox, 0);
            Grid.SetColumn(_shortcodeBox, 1);
            rightPanel.Children.Add(_shortcodeBox);

            // --- Row 1: Name ---
            var nameLabel = CreateFieldLabel("Name:", fg);
            Grid.SetRow(nameLabel, 1);
            Grid.SetColumn(nameLabel, 0);
            rightPanel.Children.Add(nameLabel);

            _nameBox = CreateFieldTextBox(fg, editorPanel, border);
            Grid.SetRow(_nameBox, 1);
            Grid.SetColumn(_nameBox, 1);
            rightPanel.Children.Add(_nameBox);

            // --- Row 2: Description ---
            var descLabel = CreateFieldLabel("Description:", fg);
            Grid.SetRow(descLabel, 2);
            Grid.SetColumn(descLabel, 0);
            rightPanel.Children.Add(descLabel);

            _descriptionBox = CreateFieldTextBox(fg, editorPanel, border);
            Grid.SetRow(_descriptionBox, 2);
            Grid.SetColumn(_descriptionBox, 1);
            rightPanel.Children.Add(_descriptionBox);

            // --- Row 3: Category ---
            var catLabel = CreateFieldLabel("Category:", fg);
            Grid.SetRow(catLabel, 3);
            Grid.SetColumn(catLabel, 0);
            rightPanel.Children.Add(catLabel);

            _categoryBox = CreateFieldTextBox(fg, editorPanel, border);
            Grid.SetRow(_categoryBox, 3);
            Grid.SetColumn(_categoryBox, 1);
            rightPanel.Children.Add(_categoryBox);

            // --- Row 4: Tags ---
            var tagsLabel = CreateFieldLabel("Tags:", fg);
            Grid.SetRow(tagsLabel, 4);
            Grid.SetColumn(tagsLabel, 0);
            rightPanel.Children.Add(tagsLabel);

            _tagsBox = CreateFieldTextBox(fg, editorPanel, border);
            _tagsBox.ToolTip = "Comma-separated tags";
            Grid.SetRow(_tagsBox, 4);
            Grid.SetColumn(_tagsBox, 1);
            rightPanel.Children.Add(_tagsBox);

            // --- Row 5: Separator ---
            var separator = new Border
            {
                Height = 1,
                Background = border,
                Margin = new Thickness(0, 6, 0, 6),
            };
            Grid.SetRow(separator, 5);
            Grid.SetColumn(separator, 0);
            Grid.SetColumnSpan(separator, 2);
            rightPanel.Children.Add(separator);

            // --- Row 6: Body label ---
            var bodyLabel = new TextBlock
            {
                Text = "Body:",
                Foreground = fg,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetRow(bodyLabel, 6);
            Grid.SetColumn(bodyLabel, 0);
            Grid.SetColumnSpan(bodyLabel, 2);
            rightPanel.Children.Add(bodyLabel);

            // --- Row 7: Body editor ---
            _bodyBox = new TextBox
            {
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = editorPanel,
                Foreground = fg,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),
            };
            Grid.SetRow(_bodyBox, 7);
            Grid.SetColumn(_bodyBox, 0);
            Grid.SetColumnSpan(_bodyBox, 2);
            rightPanel.Children.Add(_bodyBox);

            Grid.SetColumn(rightPanel, 2);
            contentGrid.Children.Add(rightPanel);

            // ================================================================
            // STATUS BAR
            // ================================================================
            _statusText = new TextBlock
            {
                Foreground = placeholder,
                Margin = new Thickness(10, 2, 10, 2),
                FontSize = 11,
                Text = string.Empty,
            };
            Grid.SetRow(_statusText, 1);
            mainGrid.Children.Add(_statusText);

            // ================================================================
            // BUTTON BAR
            // ================================================================
            var buttonBar = new DockPanel
            {
                Margin = new Thickness(6, 4, 6, 6),
                LastChildFill = false,
            };

            // Left-aligned action buttons
            var leftButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
            };
            DockPanel.SetDock(leftButtons, Dock.Left);

            var newBtn = CreateButton("New", accent, fg);
            newBtn.Click += OnNewClick;
            leftButtons.Children.Add(newBtn);

            _duplicateButton = CreateButton("Duplicate", null, null);
            _duplicateButton.Margin = new Thickness(4, 0, 0, 0);
            _duplicateButton.Click += OnDuplicateClick;
            leftButtons.Children.Add(_duplicateButton);

            _deleteButton = CreateButton("Delete", null, null);
            _deleteButton.Margin = new Thickness(4, 0, 0, 0);
            _deleteButton.Click += OnDeleteClick;
            leftButtons.Children.Add(_deleteButton);

            var importBtn = CreateButton("Import", null, null);
            importBtn.Margin = new Thickness(12, 0, 0, 0);
            importBtn.Click += OnImportClick;
            leftButtons.Children.Add(importBtn);

            var exportBtn = CreateButton("Export", null, null);
            exportBtn.Margin = new Thickness(4, 0, 0, 0);
            exportBtn.Click += OnExportClick;
            leftButtons.Children.Add(exportBtn);

            buttonBar.Children.Add(leftButtons);

            // Right-aligned Save + Close buttons
            var rightButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
            };
            DockPanel.SetDock(rightButtons, Dock.Right);

            _saveButton = CreateButton("Save", accent, fg);
            _saveButton.Width = 80;
            _saveButton.Click += OnSaveClick;
            rightButtons.Children.Add(_saveButton);

            var closeBtn = CreateButton("Close", null, null);
            closeBtn.Width = 80;
            closeBtn.Margin = new Thickness(4, 0, 0, 0);
            closeBtn.IsCancel = true;
            closeBtn.Click += OnCloseClick;
            rightButtons.Children.Add(closeBtn);

            buttonBar.Children.Add(rightButtons);

            Grid.SetRow(buttonBar, 2);
            mainGrid.Children.Add(buttonBar);

            Content = mainGrid;

            // Initial state: nothing selected
            UpdateEditFieldsEnabled();
        }

        // -------------------------------------------------------------------
        // List item rendering
        // -------------------------------------------------------------------

        private void RefreshSnippetList()
        {
            if (_snippetList == null) return;

            _snippetList.Items.Clear();

            // Spec 020 (US1 T021): direct token lookups; palette pre-freezes.
            var res = ThemeRegistry.Instance.Resources;
            var fg = (SolidColorBrush)res[ThemeTokens.TextPrimary];
            var accent = (SolidColorBrush)res[ThemeTokens.AccentPrimary];
            var placeholder = (SolidColorBrush)res[ThemeTokens.TextPlaceholder];

            // Group snippets by source
            string lastGroup = null;
            var ordered = _viewModel.FilteredSnippets
                .OrderBy(s => s.Source)
                .ThenBy(s => s.Shortcode, StringComparer.OrdinalIgnoreCase);

            foreach (var snippet in ordered)
            {
                var groupName = GetSourceDisplayName(snippet.Source);
                if (groupName != lastGroup)
                {
                    // Group header (non-selectable)
                    var header = new ListBoxItem
                    {
                        Content = new TextBlock
                        {
                            Text = groupName,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = accent,
                            Margin = new Thickness(2, 6, 0, 2),
                        },
                        IsHitTestVisible = false,
                        IsEnabled = false,
                        Focusable = false,
                    };
                    _snippetList.Items.Add(header);
                    lastGroup = groupName;
                }

                // Snippet item
                var itemPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(8, 1, 0, 1),
                };

                var icon = new TextBlock
                {
                    Text = "S",
                    FontWeight = FontWeights.Bold,
                    Foreground = accent,
                    Width = 16,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                itemPanel.Children.Add(icon);

                var shortcode = new TextBlock
                {
                    Text = snippet.Shortcode ?? "(no shortcode)",
                    FontWeight = FontWeights.SemiBold,
                    Foreground = fg,
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                itemPanel.Children.Add(shortcode);

                var name = new TextBlock
                {
                    Text = " - " + (snippet.Name ?? ""),
                    Foreground = placeholder,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                itemPanel.Children.Add(name);

                var item = new ListBoxItem
                {
                    Content = itemPanel,
                    Tag = snippet,
                    Padding = new Thickness(4, 3, 4, 3),
                };
                _snippetList.Items.Add(item);
            }

            // Re-select current snippet if still present
            if (_viewModel.SelectedSnippet != null)
            {
                SelectListItemBySnippet(_viewModel.SelectedSnippet);
            }
        }

        private void SelectListItemBySnippet(SnippetInfo snippet)
        {
            if (_snippetList == null) return;
            foreach (var item in _snippetList.Items)
            {
                var lbi = item as ListBoxItem;
                if (lbi?.Tag is SnippetInfo si && si.Id == snippet.Id)
                {
                    _snippetList.SelectedItem = lbi;
                    lbi.BringIntoView();
                    return;
                }
            }
        }

        private static string GetSourceDisplayName(int source)
        {
            switch (source)
            {
                case 1: return "Personal";
                case 2: return "Team";
                case 3: return "Built-In";
                default: return "Other";
            }
        }

        // -------------------------------------------------------------------
        // Event handlers
        // -------------------------------------------------------------------

        private void OnSnippetListSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_snippetList == null) return;

            var selectedItem = _snippetList.SelectedItem as ListBoxItem;
            if (selectedItem?.Tag is SnippetInfo snippet)
            {
                _viewModel.SelectedSnippet = snippet;
            }
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_searchBox != null)
            {
                _viewModel.SearchText = _searchBox.Text;
            }
        }

        private void OnFilteredSnippetsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(RefreshSnippetList));
            }
            else
            {
                RefreshSnippetList();
            }
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => HandlePropertyChanged(e.PropertyName)));
            }
            else
            {
                HandlePropertyChanged(e.PropertyName);
            }
        }

        private void HandlePropertyChanged(string? propertyName)
        {
            switch (propertyName)
            {
                case nameof(SnippetManagerViewModel.SelectedSnippet):
                    UpdateEditFieldsFromViewModel();
                    UpdateEditFieldsEnabled();
                    break;

                case nameof(SnippetManagerViewModel.IsBuiltIn):
                    UpdateEditFieldsEnabled();
                    break;

                case nameof(SnippetManagerViewModel.IsEditing):
                    UpdateEditFieldsEnabled();
                    break;

                case nameof(SnippetManagerViewModel.EditShortcode):
                    if (_shortcodeBox != null && _shortcodeBox.Text != _viewModel.EditShortcode)
                        _shortcodeBox.Text = _viewModel.EditShortcode;
                    break;

                case nameof(SnippetManagerViewModel.EditName):
                    if (_nameBox != null && _nameBox.Text != _viewModel.EditName)
                        _nameBox.Text = _viewModel.EditName;
                    break;

                case nameof(SnippetManagerViewModel.EditDescription):
                    if (_descriptionBox != null && _descriptionBox.Text != _viewModel.EditDescription)
                        _descriptionBox.Text = _viewModel.EditDescription;
                    break;

                case nameof(SnippetManagerViewModel.EditCategory):
                    if (_categoryBox != null && _categoryBox.Text != _viewModel.EditCategory)
                        _categoryBox.Text = _viewModel.EditCategory;
                    break;

                case nameof(SnippetManagerViewModel.EditTags):
                    if (_tagsBox != null && _tagsBox.Text != _viewModel.EditTags)
                        _tagsBox.Text = _viewModel.EditTags;
                    break;

                case nameof(SnippetManagerViewModel.EditBody):
                    if (_bodyBox != null && _bodyBox.Text != _viewModel.EditBody)
                        _bodyBox.Text = _viewModel.EditBody;
                    break;

                case nameof(SnippetManagerViewModel.StatusMessage):
                    if (_statusText != null)
                        _statusText.Text = _viewModel.StatusMessage;
                    break;
            }
        }

        private void UpdateEditFieldsFromViewModel()
        {
            if (_shortcodeBox != null) _shortcodeBox.Text = _viewModel.EditShortcode;
            if (_nameBox != null) _nameBox.Text = _viewModel.EditName;
            if (_descriptionBox != null) _descriptionBox.Text = _viewModel.EditDescription;
            if (_categoryBox != null) _categoryBox.Text = _viewModel.EditCategory;
            if (_tagsBox != null) _tagsBox.Text = _viewModel.EditTags;
            if (_bodyBox != null) _bodyBox.Text = _viewModel.EditBody;
        }

        private void UpdateEditFieldsEnabled()
        {
            bool canEdit = _viewModel.IsEditing && !_viewModel.IsBuiltIn;
            bool hasSelection = _viewModel.SelectedSnippet != null || _viewModel.IsEditing;

            if (_shortcodeBox != null) _shortcodeBox.IsEnabled = canEdit;
            if (_nameBox != null) _nameBox.IsEnabled = canEdit;
            if (_descriptionBox != null) _descriptionBox.IsEnabled = canEdit;
            if (_categoryBox != null) _categoryBox.IsEnabled = canEdit;
            if (_tagsBox != null) _tagsBox.IsEnabled = canEdit;
            if (_bodyBox != null) _bodyBox.IsEnabled = canEdit;

            if (_deleteButton != null) _deleteButton.IsEnabled = canEdit && _viewModel.SelectedSnippet != null;
            if (_duplicateButton != null) _duplicateButton.IsEnabled = hasSelection;
            if (_saveButton != null) _saveButton.IsEnabled = canEdit;
        }

        // -------------------------------------------------------------------
        // Button click handlers
        // -------------------------------------------------------------------

        private void OnNewClick(object sender, RoutedEventArgs e)
        {
            _viewModel.NewSnippet();
            if (_snippetList != null) _snippetList.SelectedItem = null;
            if (_shortcodeBox != null) _shortcodeBox.Focus();
        }

        private void OnDuplicateClick(object sender, RoutedEventArgs e)
        {
            _viewModel.DuplicateSnippet();
            if (_snippetList != null) _snippetList.SelectedItem = null;
            if (_shortcodeBox != null) _shortcodeBox.Focus();
        }

        private void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            if (_viewModel.SelectedSnippet == null) return;

            var result = MessageBox.Show(
                string.Format("Delete snippet '{0}'? This cannot be undone.",
                    _viewModel.SelectedSnippet.Name ?? _viewModel.SelectedSnippet.Shortcode),
                "Delete Snippet",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                SyncEditFieldsToViewModel();
                _viewModel.DeleteSnippet();
            }
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            SyncEditFieldsToViewModel();
            _viewModel.SaveSnippet();
        }

        private void OnImportClick(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Import Snippet",
                Filter = "AKML Snippet files (*.akmlsnippet)|*.akmlsnippet|JSON files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = ".akmlsnippet",
                Multiselect = false,
            };

            if (dlg.ShowDialog(this) == true)
            {
                try
                {
                    var json = File.ReadAllText(dlg.FileName);
                    ImportSnippetJson(json);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to import snippet: " + ex.Message,
                        "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ImportSnippetJson(string json)
        {
            try
            {
                var manager = Ipc.EngineLifecycle.Manager;
                if (manager?.Client == null)
                {
                    MessageBox.Show("Engine not available.", "Import Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Use SnippetImport (not SnippetSave) — HandleImport uses ImportJsonOptions
                // which supports case-insensitive property matching and JSON comments
                var request = new AkmlSql.Core.Ipc.Messages.SnippetImportRequest
                {
                    FileContent = json,
                    SourceFormat = 0 // Auto-detect
                };

                var response = Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.Run(async () =>
                    await manager.Client.SendRequestAsync<
                        AkmlSql.Core.Ipc.Messages.SnippetImportResponse,
                        AkmlSql.Core.Ipc.Messages.SnippetImportRequest>(
                        MessageTypes.SnippetImport, request, timeoutMs: 10_000));

                if (response != null && response.Success && response.ImportedCount > 0)
                {
                    _viewModel.LoadSnippets();
                    if (_statusText != null)
                        _statusText.Text = $"Imported {response.ImportedCount} snippet(s).";
                }
                else
                {
                    var errorMsg = response?.FailedDetails?.Length > 0
                        ? string.Join("\n", response.FailedDetails)
                        : "Failed to import snippet.";
                    MessageBox.Show(errorMsg, "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to import snippet: " + ex.Message,
                    "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnExportClick(object sender, RoutedEventArgs e)
        {
            if (_viewModel.SelectedSnippet == null)
            {
                MessageBox.Show("No snippet selected to export.", "Export",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SyncEditFieldsToViewModel();

            var snippet = _viewModel.SelectedSnippet;
            var defaultFileName = !string.IsNullOrWhiteSpace(snippet.Shortcode)
                ? snippet.Shortcode + ".akmlsnippet"
                : "snippet.akmlsnippet";

            var dlg = new SaveFileDialog
            {
                Title = "Export Snippet",
                Filter = "AKML Snippet files (*.akmlsnippet)|*.akmlsnippet|JSON files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = ".akmlsnippet",
                FileName = defaultFileName,
            };

            if (dlg.ShowDialog(this) == true)
            {
                try
                {
                    var tags = string.IsNullOrWhiteSpace(_viewModel.EditTags)
                        ? Array.Empty<string>()
                        : _viewModel.EditTags.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(t => t.Trim())
                            .Where(t => t.Length > 0)
                            .ToArray();

                    var bodyLines = (_viewModel.EditBody ?? string.Empty)
                        .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

                    var snippetObj = new System.Collections.Generic.Dictionary<string, object>
                    {
                        ["metadata"] = new System.Collections.Generic.Dictionary<string, object>
                        {
                            ["id"] = snippet.Id ?? Guid.NewGuid().ToString(),
                            ["shortcode"] = _viewModel.EditShortcode ?? string.Empty,
                            ["name"] = _viewModel.EditName ?? string.Empty,
                            ["description"] = _viewModel.EditDescription ?? string.Empty,
                            ["category"] = _viewModel.EditCategory ?? string.Empty,
                            ["tags"] = tags
                        },
                        ["variables"] = Array.Empty<object>(),
                        ["body"] = bodyLines
                    };

                    var json = System.Text.Json.JsonSerializer.Serialize(snippetObj,
                        new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(dlg.FileName, json);

                    if (_statusText != null)
                        _statusText.Text = "Snippet exported to " + Path.GetFileName(dlg.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to export snippet: " + ex.Message,
                        "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------

        /// <summary>
        /// Copies the current text box values back into the ViewModel edit properties.
        /// This is needed because we use programmatic WPF without data binding.
        /// </summary>
        private void SyncEditFieldsToViewModel()
        {
            if (_shortcodeBox != null) _viewModel.EditShortcode = _shortcodeBox.Text;
            if (_nameBox != null) _viewModel.EditName = _nameBox.Text;
            if (_descriptionBox != null) _viewModel.EditDescription = _descriptionBox.Text;
            if (_categoryBox != null) _viewModel.EditCategory = _categoryBox.Text;
            if (_tagsBox != null) _viewModel.EditTags = _tagsBox.Text;
            if (_bodyBox != null) _viewModel.EditBody = _bodyBox.Text;
        }

        private static TextBlock CreateFieldLabel(string text, SolidColorBrush foreground)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = foreground,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 3, 8, 3),
                Width = 80,
            };
        }

        private static TextBox CreateFieldTextBox(
            SolidColorBrush foreground,
            SolidColorBrush background,
            SolidColorBrush borderBrush)
        {
            return new TextBox
            {
                Foreground = foreground,
                Background = background,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 3, 4, 3),
                Margin = new Thickness(0, 3, 0, 3),
            };
        }

        private static Button CreateButton(
            string text,
            SolidColorBrush? background,
            SolidColorBrush? foreground)
        {
            var btn = new Button
            {
                Content = text,
                Padding = new Thickness(12, 4, 12, 4),
                MinWidth = 70,
            };

            if (background != null)
            {
                btn.Background = background;
            }
            if (foreground != null)
            {
                btn.Foreground = foreground;
            }

            return btn;
        }

        protected override void OnClosed(EventArgs e)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            ((System.Collections.Specialized.INotifyCollectionChanged)_viewModel.FilteredSnippets)
                .CollectionChanged -= OnFilteredSnippetsChanged;
            base.OnClosed(e);
        }
    }
}
