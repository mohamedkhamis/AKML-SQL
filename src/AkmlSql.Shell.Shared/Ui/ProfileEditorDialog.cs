using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AkmlSql.Shell.Shared.Ui.Theme;
using Microsoft.VisualStudio.PlatformUI;

namespace AkmlSql.Shell.Shared.Ui
{
    /// <summary>
    /// T097/T100/T102/T103: WPF DialogWindow for editing formatting profiles.
    /// Left pane: TreeView for categories + dynamic option controls.
    /// Right pane: SQL preview (before/after) with syntax coloring.
    /// Bottom: Cancel, Save, Save &amp; Apply buttons.
    /// Programmatic WPF only (no XAML) for Shell.Shared compatibility.
    /// </summary>
    internal class ProfileEditorDialog : DialogWindow
    {
        private readonly ProfileEditorViewModel _viewModel;
        private TreeView _categoryTree;
        private StackPanel _optionsPanel;
        private ScrollViewer _optionsScroller;
        private RichTextBox _previewBefore;
        private RichTextBox _previewAfter;
        private TextBox _searchBox;
        private SqlPreviewRenderer _beforeRenderer;
        private SqlPreviewRenderer _afterRenderer;
        private Label _categoryLabel;
        private Button _resetCategoryBtn;

        public ProfileEditorDialog(ProfileEditorViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            Title = "AKML SQL - Profile Editor";
            Width = 1100;
            Height = 750;
            MinWidth = 800;
            MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            HasHelpButton = false;

            BuildUi();
            DataContext = _viewModel;

            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        private void BuildUi()
        {
            // Spec 020 (US1 T021): direct token lookups from ThemeRegistry; palette pre-freezes brushes.
            var res = Theme.ThemeRegistry.Instance.Resources;
            var bg     = (SolidColorBrush)res[Theme.ThemeTokens.SurfaceCanvas];
            var fg     = (SolidColorBrush)res[Theme.ThemeTokens.TextPrimary];
            var border = (SolidColorBrush)res[Theme.ThemeTokens.BorderDefault];

            // Main grid: 2 columns (left options, right preview) + 1 row for content + 1 row for buttons
            var mainGrid = new Grid { Background = bg };
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) }); // Splitter
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ================================================================
            // LEFT PANE: Search + TreeView + Options
            // ================================================================
            var leftPanel = new Grid();
            leftPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Search
            leftPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(180) }); // Tree
            leftPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Category header
            leftPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Options

            // Search box with placeholder
            var searchPanel = new Grid { Margin = new Thickness(6, 6, 6, 2) };
            _searchBox = new TextBox
            {
                Padding = new Thickness(4, 2, 4, 2),
                Background = bg,
                Foreground = fg,
                BorderBrush = border,
            };
            _searchBox.TextChanged += OnSearchTextChanged;

            var searchPlaceholder = new TextBlock
            {
                Text = "Search options...",
                Margin = new Thickness(6, 3, 0, 0),
                IsHitTestVisible = false,
            };
            searchPlaceholder.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPlaceholder);

            // Update placeholder visibility manually (avoids System.Xaml dependency for Binding)
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

            // Category tree
            _categoryTree = new TreeView
            {
                Margin = new Thickness(6, 2, 6, 2),
                Background = bg,
                Foreground = fg,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
            };
            Grid.SetRow(_categoryTree, 1);
            leftPanel.Children.Add(_categoryTree);

            // Category header with reset button
            var catHeader = new DockPanel { Margin = new Thickness(6, 4, 6, 2) };
            _categoryLabel = new Label
            {
                Content = "Whitespace",
                FontWeight = FontWeights.SemiBold,
                Foreground = fg,
                Padding = new Thickness(0),
            };
            _resetCategoryBtn = new Button
            {
                Content = "Reset Category",
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            _resetCategoryBtn.Click += OnResetCategoryClick;
            DockPanel.SetDock(_resetCategoryBtn, Dock.Right);
            catHeader.Children.Add(_resetCategoryBtn);
            catHeader.Children.Add(_categoryLabel);
            Grid.SetRow(catHeader, 2);
            leftPanel.Children.Add(catHeader);

            // Options panel in a scroll viewer
            _optionsPanel = new StackPanel { Margin = new Thickness(2) };
            _optionsScroller = new ScrollViewer
            {
                Content = _optionsPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(6, 2, 6, 6),
                Background = bg,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),
            };
            Grid.SetRow(_optionsScroller, 3);
            leftPanel.Children.Add(_optionsScroller);

            Grid.SetColumn(leftPanel, 0);
            mainGrid.Children.Add(leftPanel);

            // ================================================================
            // SPLITTER
            // ================================================================
            var splitter = new GridSplitter
            {
                Width = 5,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = border,
            };
            Grid.SetColumn(splitter, 1);
            mainGrid.Children.Add(splitter);

            // ================================================================
            // RIGHT PANE: Before/After preview
            // ================================================================
            var rightPanel = new Grid();
            rightPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rightPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rightPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rightPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var beforeLabel = new Label
            {
                Content = "Original SQL",
                FontWeight = FontWeights.SemiBold,
                Foreground = fg,
                Margin = new Thickness(6, 6, 6, 0),
            };
            Grid.SetRow(beforeLabel, 0);
            rightPanel.Children.Add(beforeLabel);

            _previewBefore = new RichTextBox
            {
                IsReadOnly = true,
                Margin = new Thickness(6, 2, 6, 2),
                BorderBrush = border,
                BorderThickness = new Thickness(1),
            };
            _beforeRenderer = new SqlPreviewRenderer(_previewBefore);
            Grid.SetRow(_previewBefore, 1);
            rightPanel.Children.Add(_previewBefore);

            var afterLabel = new Label
            {
                Content = "Formatted Preview",
                FontWeight = FontWeights.SemiBold,
                Foreground = fg,
                Margin = new Thickness(6, 4, 6, 0),
            };
            Grid.SetRow(afterLabel, 2);
            rightPanel.Children.Add(afterLabel);

            _previewAfter = new RichTextBox
            {
                IsReadOnly = true,
                Margin = new Thickness(6, 2, 6, 6),
                BorderBrush = border,
                BorderThickness = new Thickness(1),
            };
            _afterRenderer = new SqlPreviewRenderer(_previewAfter);
            Grid.SetRow(_previewAfter, 3);
            rightPanel.Children.Add(_previewAfter);

            Grid.SetColumn(rightPanel, 2);
            mainGrid.Children.Add(rightPanel);

            // ================================================================
            // BOTTOM BUTTONS
            // ================================================================
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(6, 4, 6, 6),
            };

            var undoBtn = new Button { Content = "Undo", Width = 70, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(4, 2, 4, 2) };
            undoBtn.Click += (s, e) => _viewModel.Undo();
            var redoBtn = new Button { Content = "Redo", Width = 70, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(4, 2, 4, 2) };
            redoBtn.Click += (s, e) => _viewModel.Redo();

            var resetAllBtn = new Button { Content = "Reset All", Width = 80, Margin = new Thickness(4, 0, 16, 0), Padding = new Thickness(4, 2, 4, 2) };
            resetAllBtn.Click += OnResetAllClick;

            var cancelBtn = new Button { Content = "Cancel", Width = 80, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(4, 2, 4, 2), IsCancel = true };
            cancelBtn.Click += (s, e) => { DialogResult = false; Close(); };

            var saveBtn = new Button { Content = "Save", Width = 80, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(4, 2, 4, 2) };
            saveBtn.Click += OnSaveClick;

            var saveApplyBtn = new Button { Content = "Save && Apply", Width = 100, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(4, 2, 4, 2), IsDefault = true };
            saveApplyBtn.Click += OnSaveApplyClick;

            buttonPanel.Children.Add(undoBtn);
            buttonPanel.Children.Add(redoBtn);
            buttonPanel.Children.Add(resetAllBtn);
            buttonPanel.Children.Add(cancelBtn);
            buttonPanel.Children.Add(saveBtn);
            buttonPanel.Children.Add(saveApplyBtn);

            Grid.SetRow(buttonPanel, 1);
            Grid.SetColumnSpan(buttonPanel, 3);
            mainGrid.Children.Add(buttonPanel);

            Content = mainGrid;

            // Populate
            OptionCategoryTreeBuilder.BuildTree(_categoryTree, _viewModel);
            _categoryTree.SelectedItemChanged += OnCategorySelected;
            BuildOptionControls(_viewModel.SelectedCategory);
            UpdatePreview();
        }

        // -------------------------------------------------------------------
        // T100: Dynamic option controls
        // -------------------------------------------------------------------

        private void BuildOptionControls(string category)
        {
            _optionsPanel.Children.Clear();
            _categoryLabel.Content = category;

            var options = _viewModel.GetOptionsForCategory(category);
            // Spec 020 (US1 T021): direct token lookup from ThemeRegistry; palette pre-freezes.
            var fg = (SolidColorBrush)Theme.ThemeRegistry.Instance.Resources[Theme.ThemeTokens.TextPrimary];

            foreach (var opt in options)
            {
                bool isMatch = !string.IsNullOrWhiteSpace(_searchBox.Text) &&
                    (opt.DisplayName.IndexOf(_searchBox.Text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     opt.Key.IndexOf(_searchBox.Text, StringComparison.OrdinalIgnoreCase) >= 0);

                var panel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(2, 3, 2, 3),
                };

                var label = new TextBlock
                {
                    Text = opt.DisplayName,
                    Width = 220,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = fg,
                    FontWeight = isMatch ? FontWeights.Bold : FontWeights.Normal,
                    ToolTip = opt.Key,
                };
                panel.Children.Add(label);

                switch (opt.Kind)
                {
                    case OptionKind.Boolean:
                        {
                            var checkBox = new CheckBox
                            {
                                IsChecked = GetBoolValue(opt.Key),
                                VerticalAlignment = VerticalAlignment.Center,
                                Tag = opt.Key,
                            };
                            checkBox.Checked += (s, e) => _viewModel.SetOption(opt.Key, true);
                            checkBox.Unchecked += (s, e) => _viewModel.SetOption(opt.Key, false);
                            panel.Children.Add(checkBox);
                        }
                        break;

                    case OptionKind.Choice:
                        {
                            var comboBox = new ComboBox
                            {
                                Width = 160,
                                VerticalAlignment = VerticalAlignment.Center,
                                Tag = opt.Key,
                            };
                            foreach (var choice in opt.Choices)
                            {
                                comboBox.Items.Add(choice);
                            }
                            var currentVal = _viewModel.GetOption(opt.Key)?.ToString() ?? "";
                            comboBox.SelectedItem = currentVal;
                            if (comboBox.SelectedItem == null && comboBox.Items.Count > 0)
                                comboBox.SelectedIndex = 0;

                            comboBox.SelectionChanged += (s, e) =>
                            {
                                if (comboBox.SelectedItem != null)
                                    _viewModel.SetOption(opt.Key, comboBox.SelectedItem.ToString());
                            };
                            panel.Children.Add(comboBox);
                        }
                        break;

                    case OptionKind.Numeric:
                        {
                            var numericPanel = new StackPanel { Orientation = Orientation.Horizontal };
                            var textBox = new TextBox
                            {
                                Width = 60,
                                VerticalAlignment = VerticalAlignment.Center,
                                Text = GetIntValue(opt.Key).ToString(),
                                Tag = opt.Key,
                            };

                            var minVal = opt.Min;
                            var maxVal = opt.Max;
                            var key = opt.Key;

                            textBox.LostFocus += (s, e) =>
                            {
                                if (int.TryParse(textBox.Text, out var val))
                                {
                                    val = Math.Max(minVal, Math.Min(maxVal, val));
                                    textBox.Text = val.ToString();
                                    _viewModel.SetOption(key, val);
                                }
                                else
                                {
                                    textBox.Text = GetIntValue(key).ToString();
                                }
                            };

                            textBox.PreviewTextInput += (s, e) =>
                            {
                                foreach (var ch in e.Text)
                                {
                                    if (!char.IsDigit(ch))
                                    {
                                        e.Handled = true;
                                        break;
                                    }
                                }
                            };

                            var rangeLabel = new TextBlock
                            {
                                Text = string.Format(" ({0}-{1})", opt.Min, opt.Max),
                                VerticalAlignment = VerticalAlignment.Center,
                                FontSize = 11,
                            };
                            rangeLabel.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);

                            numericPanel.Children.Add(textBox);
                            numericPanel.Children.Add(rangeLabel);
                            panel.Children.Add(numericPanel);
                        }
                        break;
                }

                _optionsPanel.Children.Add(panel);
            }
        }

        // -------------------------------------------------------------------
        // Event handlers
        // -------------------------------------------------------------------

        private void OnCategorySelected(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var selected = _categoryTree.SelectedItem as TreeViewItem;
            if (selected == null) return;

            var catName = selected.Tag as string;
            if (catName == null) return; // Group header, not a category

            _viewModel.SelectedCategory = catName;
            BuildOptionControls(catName);
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            var text = _searchBox.Text;
            _viewModel.SearchText = text;

            OptionCategoryTreeBuilder.FilterTree(_categoryTree, _viewModel, text);

            // If searching, show matching options across all categories
            if (!string.IsNullOrWhiteSpace(text))
            {
                BuildSearchResults(text);
            }
            else
            {
                BuildOptionControls(_viewModel.SelectedCategory);
            }
        }

        private void BuildSearchResults(string filter)
        {
            _optionsPanel.Children.Clear();
            _categoryLabel.Content = "Search Results";

            var results = _viewModel.SearchAllOptions(filter);
            // Spec 020 (US1 T021): direct token lookup from ThemeRegistry; palette pre-freezes.
            var fg = (SolidColorBrush)Theme.ThemeRegistry.Instance.Resources[Theme.ThemeTokens.TextPrimary];

            string lastCategory = null;

            foreach (var opt in results)
            {
                var cat = opt.Category;
                if (cat != lastCategory)
                {
                    var catLabel = new TextBlock
                    {
                        Text = GetCategoryDisplayName(cat),
                        FontWeight = FontWeights.SemiBold,
                        Foreground = fg,
                        Margin = new Thickness(2, 8, 2, 2),
                    };
                    _optionsPanel.Children.Add(catLabel);
                    lastCategory = cat;
                }

                var panel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(10, 3, 2, 3),
                };

                var label = new TextBlock
                {
                    Text = opt.DisplayName,
                    Width = 210,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = fg,
                    FontWeight = FontWeights.Bold,
                    ToolTip = opt.Key,
                };
                panel.Children.Add(label);

                switch (opt.Kind)
                {
                    case OptionKind.Boolean:
                        var cb = new CheckBox { IsChecked = GetBoolValue(opt.Key), VerticalAlignment = VerticalAlignment.Center, Tag = opt.Key };
                        cb.Checked += (s, _) => _viewModel.SetOption(opt.Key, true);
                        cb.Unchecked += (s, _) => _viewModel.SetOption(opt.Key, false);
                        panel.Children.Add(cb);
                        break;
                    case OptionKind.Choice:
                        var combo = new ComboBox { Width = 160, VerticalAlignment = VerticalAlignment.Center, Tag = opt.Key };
                        foreach (var c in opt.Choices) combo.Items.Add(c);
                        combo.SelectedItem = _viewModel.GetOption(opt.Key)?.ToString() ?? "";
                        if (combo.SelectedItem == null && combo.Items.Count > 0) combo.SelectedIndex = 0;
                        combo.SelectionChanged += (s, _) =>
                        {
                            if (combo.SelectedItem != null) _viewModel.SetOption(opt.Key, combo.SelectedItem.ToString());
                        };
                        panel.Children.Add(combo);
                        break;
                    case OptionKind.Numeric:
                        var tb = new TextBox { Width = 60, VerticalAlignment = VerticalAlignment.Center, Text = GetIntValue(opt.Key).ToString(), Tag = opt.Key };
                        var capturedOpt = opt;
                        tb.LostFocus += (s, _) =>
                        {
                            if (int.TryParse(tb.Text, out var v))
                            {
                                v = Math.Max(capturedOpt.Min, Math.Min(capturedOpt.Max, v));
                                tb.Text = v.ToString();
                                _viewModel.SetOption(capturedOpt.Key, v);
                            }
                        };
                        panel.Children.Add(tb);
                        break;
                }

                _optionsPanel.Children.Add(panel);
            }
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ProfileEditorViewModel.FormattedPreview))
            {
                // Marshal to UI thread
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.BeginInvoke(new Action(UpdateAfterPreview));
                }
                else
                {
                    UpdateAfterPreview();
                }
            }
        }

        private void OnResetCategoryClick(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                string.Format("Reset all options in '{0}' to defaults?", _viewModel.SelectedCategory),
                "Reset Category",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _viewModel.ResetCategory(_viewModel.SelectedCategory);
                BuildOptionControls(_viewModel.SelectedCategory);
            }
        }

        private void OnResetAllClick(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Reset all profile options to defaults? This cannot be undone.",
                "Reset All Options",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                _viewModel.ResetToDefaults();
                BuildOptionControls(_viewModel.SelectedCategory);
            }
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                _viewModel.Save();
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save profile: " + ex.Message, "Save Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnSaveApplyClick(object sender, RoutedEventArgs e)
        {
            try
            {
                _viewModel.SaveAndApply();
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to save profile: " + ex.Message, "Save Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // -------------------------------------------------------------------
        // Preview
        // -------------------------------------------------------------------

        private void UpdatePreview()
        {
            _beforeRenderer.Render(_viewModel.SampleSql);
            UpdateAfterPreview();
            _viewModel.RefreshPreview();
        }

        private void UpdateAfterPreview()
        {
            var text = _viewModel.FormattedPreview;
            if (string.IsNullOrEmpty(text))
                text = _viewModel.SampleSql;

            _afterRenderer.Render(text);
        }

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------

        private bool GetBoolValue(string key)
        {
            var val = _viewModel.GetOption(key);
            if (val is bool b) return b;
            return false;
        }

        private int GetIntValue(string key)
        {
            var val = _viewModel.GetOption(key);
            if (val is int i) return i;
            if (val != null && int.TryParse(val.ToString(), out var parsed)) return parsed;
            return 0;
        }

        private static string GetCategoryDisplayName(string prefix)
        {
            switch (prefix)
            {
                case "whitespace": return "Whitespace";
                case "casing": return "Casing";
                case "list": return "Lists";
                case "parenthesis": return "Parentheses";
                case "dml": return "DML (SELECT/INSERT/UPDATE/DELETE)";
                case "join": return "JOINs";
                case "ddl": return "DDL (CREATE/ALTER)";
                case "controlFlow": return "Control Flow (IF/WHILE/TRY)";
                case "case": return "CASE Expressions";
                case "cte": return "CTEs (WITH)";
                case "expression": return "Expressions";
                case "formatActions": return "Format Actions";
                default: return prefix;
            }
        }

    }
}
