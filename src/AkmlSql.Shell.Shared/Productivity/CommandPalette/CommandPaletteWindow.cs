#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AkmlSql.Core.Models.Productivity;
using AkmlSql.Shell.Shared.Ui.Theme;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Productivity.CommandPalette
{
    /// <summary>
    /// WPF Popup-based Command Palette window.
    /// Centered on the main IDE window with a TextBox at top and ListBox below.
    /// Handles Up/Down/Enter/Escape keys. Auto-dismisses on focus loss.
    /// </summary>
    internal sealed class CommandPaletteWindow
    {
        private const string PlaceholderText = "Type a command...";

        private Window? _window;
        private CommandPaletteViewModel? _viewModel;
        private TextBox? _searchBox;
        private ListBox? _listBox;

        /// <summary>
        /// Shows the Command Palette window. If already shown, brings it to front.
        /// Must be called on the UI thread.
        /// </summary>
        public void Show()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                if (_window != null && _window.IsVisible)
                {
                    _window.Activate();
                    _searchBox?.Focus();
                    return;
                }

                _viewModel = new CommandPaletteViewModel();
                _viewModel.CloseRequested += OnCloseRequested;

                _window = CreateWindow();
                _window.Show();
                _searchBox?.Focus();

                Log.Debug("CommandPaletteWindow: shown");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "CommandPaletteWindow: failed to show");
            }
        }

        /// <summary>
        /// Closes the Command Palette window if it is open.
        /// </summary>
        public void Close()
        {
            try
            {
                if (_window != null)
                {
                    _window.Close();
                    _window = null;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "CommandPaletteWindow: error during close");
            }
        }

        #region Window creation

        private Window CreateWindow()
        {
            var window = new Window
            {
                Title = "Command Palette",
                Width = 600,
                Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Topmost = true
            };
            ThemeRegistry.Instance.AttachTo(window);
            window.SetResourceReference(Window.BackgroundProperty, ThemeTokens.SurfacePanel);
            window.SetResourceReference(Window.ForegroundProperty, ThemeTokens.TextPrimary);

            // Try to set the owner to the main IDE window
            try
            {
                var mainWindow = Application.Current?.MainWindow;
                if (mainWindow != null)
                {
                    window.Owner = mainWindow;
                    window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }
            }
            catch
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            // Build the UI
            var rootPanel = new DockPanel { Margin = new Thickness(1) };
            rootPanel.SetResourceReference(DockPanel.BackgroundProperty, ThemeTokens.SurfaceCanvas);

            // Border for visual frame — accent-coloured edge highlighting the popup.
            var border = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4)
            };
            border.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.AccentPrimary);
            border.SetResourceReference(Border.BackgroundProperty, ThemeTokens.SurfacePanel);

            var mainPanel = new DockPanel();

            // Search TextBox at top
            _searchBox = new TextBox
            {
                FontSize = 16,
                Padding = new Thickness(10, Spacing.Sm, 10, Spacing.Sm),
                BorderThickness = new Thickness(0),
                FocusVisualStyle = FocusVisualStyles.HighStakes,
                Tag = PlaceholderText,
                Text = PlaceholderText
            };
            _searchBox.SetResourceReference(TextBox.BackgroundProperty, ThemeTokens.SurfaceInput);
            _searchBox.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, ThemeTokens.TextPrimary);
            ApplyPlaceholderState(_searchBox, isPlaceholder: true);

            _searchBox.GotFocus += (s, e) =>
            {
                if (_searchBox.Text == PlaceholderText)
                {
                    _searchBox.Text = "";
                    ApplyPlaceholderState(_searchBox, isPlaceholder: false);
                }
            };
            _searchBox.LostFocus += (s, e) =>
            {
                if (string.IsNullOrEmpty(_searchBox.Text))
                {
                    _searchBox.Text = PlaceholderText;
                    ApplyPlaceholderState(_searchBox, isPlaceholder: true);
                }
            };

            _searchBox.TextChanged += OnSearchTextChanged;
            _searchBox.PreviewKeyDown += OnSearchKeyDown;

            DockPanel.SetDock(_searchBox, Dock.Top);
            mainPanel.Children.Add(_searchBox);

            // Separator under the search box
            var separator = new Border
            {
                Height = 1
            };
            separator.SetResourceReference(Border.BackgroundProperty, ThemeTokens.BorderDefault);
            DockPanel.SetDock(separator, Dock.Top);
            mainPanel.Children.Add(separator);

            // Results ListBox
            _listBox = new ListBox
            {
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            _listBox.SetResourceReference(ListBox.BackgroundProperty, ThemeTokens.SurfacePanel);
            _listBox.SetResourceReference(ListBox.ForegroundProperty, ThemeTokens.TextPrimary);
            ScrollViewer.SetHorizontalScrollBarVisibility(_listBox, ScrollBarVisibility.Disabled);

            // Custom ItemTemplate
            _listBox.ItemTemplate = CreateItemTemplate();

            // Bind ItemsSource to FilteredCommands
            _listBox.SetBinding(ItemsControl.ItemsSourceProperty,
                new Binding("FilteredCommands") { Source = _viewModel });
            _listBox.SetBinding(ListBox.SelectedIndexProperty,
                new Binding("SelectedIndex") { Source = _viewModel, Mode = BindingMode.TwoWay });

            _listBox.MouseDoubleClick += OnListBoxDoubleClick;

            _listBox.ItemContainerStyle = CreateListBoxItemStyle();

            mainPanel.Children.Add(_listBox);

            border.Child = mainPanel;
            rootPanel.Children.Add(border);
            window.Content = rootPanel;

            // Auto-dismiss on deactivation
            window.Deactivated += (s, e) =>
            {
                try { window.Close(); } catch { /* ignore */ }
            };

            window.KeyDown += OnWindowKeyDown;

            return window;
        }

        /// <summary>
        /// Switches the search box's foreground between placeholder and primary states using
        /// <see cref="FrameworkElement.SetResourceReference(DependencyProperty, object)"/> so the
        /// chosen colour stays live across theme switches even while the box holds the placeholder.
        /// </summary>
        private static void ApplyPlaceholderState(TextBox box, bool isPlaceholder)
        {
            box.SetResourceReference(
                TextBox.ForegroundProperty,
                isPlaceholder ? ThemeTokens.TextPlaceholder : ThemeTokens.TextPrimary);
        }

        private static Style CreateListBoxItemStyle()
        {
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(Spacing.Sm, 6, Spacing.Sm, 6)));
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
            var mouseOverTrigger = new MultiTrigger();
            mouseOverTrigger.Conditions.Add(new Condition(UIElement.IsMouseOverProperty, true));
            mouseOverTrigger.Conditions.Add(new Condition(ListBoxItem.IsSelectedProperty, false));
            mouseOverTrigger.Setters.Add(new Setter(Control.BackgroundProperty,
                new DynamicResourceExtension(ThemeTokens.SurfaceHover)));
            style.Triggers.Add(mouseOverTrigger);

            return style;
        }

        private static DataTemplate CreateItemTemplate()
        {
            var template = new DataTemplate(typeof(CommandEntry));

            // Outer DockPanel: category (left), shortcut (right), name (fill).
            var dockFactory = new FrameworkElementFactory(typeof(DockPanel));

            // Shortcut hint (right-aligned, secondary tone)
            var shortcutFactory = new FrameworkElementFactory(typeof(TextBlock));
            shortcutFactory.SetBinding(TextBlock.TextProperty, new Binding("KeyboardShortcut"));
            shortcutFactory.SetResourceBinding(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            shortcutFactory.SetValue(TextBlock.FontSizeProperty, 12.0);
            shortcutFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            shortcutFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(Spacing.Sm, 0, 0, 0));
            shortcutFactory.SetValue(DockPanel.DockProperty, Dock.Right);
            dockFactory.AppendChild(shortcutFactory);

            // Category label (further dimmed, before name)
            var categoryFactory = new FrameworkElementFactory(typeof(TextBlock));
            categoryFactory.SetBinding(TextBlock.TextProperty, new Binding("Category"));
            categoryFactory.SetResourceBinding(TextBlock.ForegroundProperty, ThemeTokens.TextDisabled);
            categoryFactory.SetValue(TextBlock.FontSizeProperty, 11.0);
            categoryFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            categoryFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            categoryFactory.SetValue(DockPanel.DockProperty, Dock.Left);
            dockFactory.AppendChild(categoryFactory);

            // Command name (bold, left-aligned, primary tone)
            var nameFactory = new FrameworkElementFactory(typeof(TextBlock));
            nameFactory.SetBinding(TextBlock.TextProperty, new Binding("Name"));
            nameFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            nameFactory.SetValue(TextBlock.FontSizeProperty, 13.0);
            nameFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            nameFactory.SetResourceBinding(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            dockFactory.AppendChild(nameFactory);

            template.VisualTree = dockFactory;
            return template;
        }

        #endregion

        #region Event handlers

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_viewModel == null || _searchBox == null) return;

            var text = _searchBox.Text;
            if (text == PlaceholderText)
                text = "";

            _viewModel.SearchText = text;
        }

        private void OnSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (_viewModel == null) return;

            switch (e.Key)
            {
                case Key.Down:
                    _viewModel.MoveDown();
                    _listBox?.ScrollIntoView(_listBox.SelectedItem);
                    e.Handled = true;
                    break;

                case Key.Up:
                    _viewModel.MoveUp();
                    _listBox?.ScrollIntoView(_listBox.SelectedItem);
                    e.Handled = true;
                    break;

                case Key.Enter:
                    _viewModel.ExecuteSelected();
                    e.Handled = true;
                    break;

                case Key.Escape:
                    _viewModel.RequestClose();
                    e.Handled = true;
                    break;
            }
        }

        private void OnListBoxDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel == null || _listBox == null) return;

            if (_listBox.SelectedItem is CommandEntry entry)
            {
                _viewModel.ExecuteCommand(entry);
            }
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                _viewModel?.RequestClose();
                e.Handled = true;
            }
        }

        private void OnCloseRequested()
        {
            Close();
        }

        #endregion
    }
}
