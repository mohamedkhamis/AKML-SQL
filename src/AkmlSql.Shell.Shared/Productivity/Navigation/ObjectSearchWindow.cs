#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using AkmlSql.Shell.Shared.Ui.Theme;
using Serilog;
using Task = System.Threading.Tasks.Task;

namespace AkmlSql.Shell.Shared.Productivity.Navigation
{
    /// <summary>
    /// WPF popup window for searching database objects by name, similar to the VS Command Palette.
    /// Shows a TextBox for search input and a ListBox with matching results.
    /// Enter navigates to the selected object's definition.
    /// </summary>
    internal sealed class ObjectSearchWindow : ThemeAwareWindow
    {
        private static readonly FontFamily MonoFont = new FontFamily("Cascadia Code, Consolas");

        private readonly TextBox _searchBox;
        private readonly ListBox _resultsList;
        private readonly TextBlock _statusBlock;
        private readonly ObservableCollection<ObjectSearchResultViewModel> _results = new();
        private Timer? _searchDebounceTimer;
        private CancellationTokenSource? _currentSearchCts;
        private string _sessionId = string.Empty;
        private bool _disposed;

        private const int SearchDebounceMs = 200;
        private const int MaxResults = 50;

        /// <summary>
        /// Raised when the user selects an object to navigate to.
        /// The event args contain the selected object's schema and name.
        /// </summary>
        public event EventHandler<ObjectSelectedEventArgs>? ObjectSelected;

        public ObjectSearchWindow(string sessionId)
        {
            _sessionId = sessionId;

            Title = "AKML SQL - Object Search";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Width = 600;
            Height = 450;
            Topmost = true;
            ShowInTaskbar = false;

            // Transparency lets the rounded mainBorder show through on the corners; this overrides
            // the SurfaceCanvas background that ThemeAwareWindow sets via SetResourceReference.
            AllowsTransparency = true;
            Background = Brushes.Transparent;

            var mainBorder = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(Spacing.Sm),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 16,
                    Opacity = 0.5,
                    ShadowDepth = 4
                }
            };
            mainBorder.SetResourceReference(Border.BackgroundProperty, ThemeTokens.SurfacePanel);
            mainBorder.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.AccentPrimary);

            var grid = new System.Windows.Controls.Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Search input
            _searchBox = new TextBox
            {
                FontSize = 16,
                FontFamily = Typography.UiFont,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(Spacing.Sm, 6, Spacing.Sm, 6),
                Margin = new Thickness(0, 0, 0, Spacing.Sm),
                FocusVisualStyle = FocusVisualStyles.HighStakes
            };
            _searchBox.SetResourceReference(TextBox.BackgroundProperty, ThemeTokens.SurfaceInput);
            _searchBox.SetResourceReference(TextBox.ForegroundProperty, ThemeTokens.TextPrimary);
            _searchBox.SetResourceReference(TextBox.BorderBrushProperty, ThemeTokens.AccentPrimary);
            _searchBox.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, ThemeTokens.TextPrimary);
            _searchBox.TextChanged += OnSearchTextChanged;
            _searchBox.PreviewKeyDown += OnSearchBoxKeyDown;
            System.Windows.Controls.Grid.SetRow(_searchBox, 0);
            grid.Children.Add(_searchBox);

            // Results list
            _resultsList = new ListBox
            {
                ItemsSource = _results,
                BorderThickness = new Thickness(1),
                FontFamily = MonoFont,
                FontSize = 13
            };
            _resultsList.SetResourceReference(ListBox.BackgroundProperty, ThemeTokens.SurfacePanel);
            _resultsList.SetResourceReference(ListBox.ForegroundProperty, ThemeTokens.TextPrimary);
            _resultsList.SetResourceReference(ListBox.BorderBrushProperty, ThemeTokens.BorderDefault);
            _resultsList.MouseDoubleClick += OnResultDoubleClick;
            _resultsList.PreviewKeyDown += OnResultsKeyDown;
            _resultsList.ItemContainerStyle = CreateListBoxItemStyle();
            _resultsList.ItemTemplate = CreateItemTemplate();

            System.Windows.Controls.Grid.SetRow(_resultsList, 1);
            grid.Children.Add(_resultsList);

            // Status bar
            _statusBlock = new TextBlock
            {
                Text = "Type to search database objects...",
                FontSize = 11,
                Margin = new Thickness(Spacing.Xs, Spacing.Xs, Spacing.Xs, 0)
            };
            _statusBlock.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            System.Windows.Controls.Grid.SetRow(_statusBlock, 2);
            grid.Children.Add(_statusBlock);

            mainBorder.Child = grid;
            Content = mainBorder;

            // Global key handlers
            KeyDown += OnWindowKeyDown;
            Loaded += (_, __) => _searchBox.Focus();
            Deactivated += (_, __) => Close();
        }

        private static Style CreateListBoxItemStyle()
        {
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(Spacing.Xs, 2, Spacing.Xs, 2)));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, FocusVisualStyles.HighStakes));

            // Selected state: strong-accent fill with on-accent text.
            var selectedTrigger = new Trigger
            {
                Property = ListBoxItem.IsSelectedProperty,
                Value = true
            };
            selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty,
                new DynamicResourceExtension(ThemeTokens.SurfaceSelectionStrong)));
            selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty,
                new DynamicResourceExtension(ThemeTokens.TextOnAccent)));
            style.Triggers.Add(selectedTrigger);

            // Hover (not selected)
            var hoverTrigger = new MultiTrigger();
            hoverTrigger.Conditions.Add(new Condition(UIElement.IsMouseOverProperty, true));
            hoverTrigger.Conditions.Add(new Condition(ListBoxItem.IsSelectedProperty, false));
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty,
                new DynamicResourceExtension(ThemeTokens.SurfaceHover)));
            style.Triggers.Add(hoverTrigger);

            return style;
        }

        private static DataTemplate CreateItemTemplate()
        {
            var factory = new FrameworkElementFactory(typeof(StackPanel));
            factory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            factory.SetValue(MarginProperty, new Thickness(Spacing.Xs, 2, Spacing.Xs, 2));

            // Type icon (uses TextLink so the affordance reads as link-like in both themes)
            var iconFactory = new FrameworkElementFactory(typeof(TextBlock));
            iconFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("TypeIcon"));
            iconFactory.SetValue(WidthProperty, 24.0);
            iconFactory.SetValue(TextBlock.FontSizeProperty, 12.0);
            iconFactory.SetResourceBinding(TextBlock.ForegroundProperty, ThemeTokens.TextLink);
            factory.AppendChild(iconFactory);

            // Object display name (schema.name)
            var nameFactory = new FrameworkElementFactory(typeof(TextBlock));
            nameFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("DisplayName"));
            nameFactory.SetResourceBinding(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            factory.AppendChild(nameFactory);

            // Object type label (Table / View / Procedure / ...) -- secondary tone
            var typeFactory = new FrameworkElementFactory(typeof(TextBlock));
            typeFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("TypeLabel"));
            typeFactory.SetValue(MarginProperty, new Thickness(Spacing.Sm, 0, 0, 0));
            typeFactory.SetResourceBinding(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            typeFactory.SetValue(TextBlock.FontSizeProperty, 11.0);
            factory.AppendChild(typeFactory);

            return new DataTemplate { VisualTree = factory };
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        }

        private void OnSearchBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down && _results.Count > 0)
            {
                _resultsList.SelectedIndex = 0;
                _resultsList.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && _results.Count > 0)
            {
                SelectCurrentItem();
                e.Handled = true;
            }
        }

        private void OnResultsKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SelectCurrentItem();
                e.Handled = true;
            }
            else if (e.Key == Key.Up && _resultsList.SelectedIndex == 0)
            {
                _searchBox.Focus();
                e.Handled = true;
            }
        }

        private void OnResultDoubleClick(object sender, MouseButtonEventArgs e)
        {
            SelectCurrentItem();
        }

        private void SelectCurrentItem()
        {
            if (_resultsList.SelectedItem is ObjectSearchResultViewModel vm)
            {
                ObjectSelected?.Invoke(this, new ObjectSelectedEventArgs(vm.SchemaName, vm.ObjectName, vm.ObjectType));
                Close();
            }
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            _searchDebounceTimer?.Dispose();
            _searchDebounceTimer = new Timer(OnSearchDebounceElapsed, null, SearchDebounceMs, Timeout.Infinite);
        }

        private void OnSearchDebounceElapsed(object? state)
        {
            if (_disposed) return;
            Dispatcher.Invoke(() => { if (!_disposed) _ = PerformSearchAsync(); });
        }

        private async Task PerformSearchAsync()
        {
            var searchText = _searchBox.Text;

            if (string.IsNullOrWhiteSpace(searchText) || searchText.Length < 2)
            {
                _results.Clear();
                _statusBlock.Text = "Type at least 2 characters to search...";
                return;
            }

            _currentSearchCts?.Cancel();
            _currentSearchCts = new CancellationTokenSource();

            try
            {
                _statusBlock.Text = "Searching...";

                var manager = EngineLifecycle.Manager;
                if (manager?.Client == null || !manager.Client.IsConnected)
                {
                    _statusBlock.Text = "Engine not connected";
                    return;
                }

                var request = new ObjectSearchRequest
                {
                    SessionId = _sessionId,
                    SearchText = searchText,
                    MaxResults = MaxResults
                };

                var response = await Task.Run(async () =>
                    await manager.Client.SendRequestAsync<ObjectSearchResponse, ObjectSearchRequest>(
                        MessageTypes.ObjectSearch, request, timeoutMs: 5000));

                if (_currentSearchCts?.IsCancellationRequested == true)
                    return;

                _results.Clear();

                if (response.Success && response.Results != null)
                {
                    foreach (var result in response.Results)
                    {
                        _results.Add(new ObjectSearchResultViewModel(result));
                    }

                    _statusBlock.Text = $"{response.Results.Length} result(s) found";
                }
                else
                {
                    _statusBlock.Text = response.Error ?? "No results";
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when search is cancelled
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ObjectSearchWindow: search failed");
                _statusBlock.Text = "Search failed";
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _disposed = true;
            _searchDebounceTimer?.Dispose();
            _currentSearchCts?.Cancel();
            _currentSearchCts?.Dispose();
            base.OnClosed(e);
        }
    }

    /// <summary>
    /// View model for a single object search result in the <see cref="ObjectSearchWindow"/>.
    /// </summary>
    internal sealed class ObjectSearchResultViewModel(ObjectSearchResultDto dto)
    {
        public string SchemaName { get; } = dto.SchemaName;
        public string ObjectName { get; } = dto.ObjectName;
        public string ObjectType { get; } = dto.ObjectType;
        public string DisplayName => $"{SchemaName}.{ObjectName}";
        public string TypeLabel => $"({ObjectType})";

        public string TypeIcon => ObjectType switch
        {
            "Table" => "T",
            "View" => "V",
            "Procedure" => "P",
            "ScalarFunction" or "TableFunction" or "InlineFunction" => "F",
            "Synonym" => "S",
            "Sequence" => "#",
            _ => "?"
        };
    }

    /// <summary>
    /// Event args for when a user selects an object from the search window.
    /// </summary>
    internal sealed class ObjectSelectedEventArgs(string schemaName, string objectName, string objectType) : EventArgs
    {
        public string SchemaName { get; } = schemaName;
        public string ObjectName { get; } = objectName;
        public string ObjectType { get; } = objectType;
    }
}
