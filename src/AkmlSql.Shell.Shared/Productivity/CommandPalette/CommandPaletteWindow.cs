#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using AkmlSql.Core.Models.Productivity;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Productivity.CommandPalette
{
    /// <summary>
    /// T036: WPF Popup-based Command Palette window.
    /// Centered on the main IDE window with a TextBox at top and ListBox below.
    /// Handles Up/Down/Enter/Escape keys. Auto-dismisses on focus loss.
    /// </summary>
    internal sealed class CommandPaletteWindow
    {
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
                Background = new SolidColorBrush(Color.FromRgb(37, 37, 38)),
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Topmost = true
            };

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
            rootPanel.Background = new SolidColorBrush(Color.FromRgb(45, 45, 48));

            // Border for visual frame
            var border = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromRgb(37, 37, 38))
            };

            var mainPanel = new DockPanel();

            // Search TextBox at top
            _searchBox = new TextBox
            {
                FontSize = 16,
                Padding = new Thickness(10, 8, 10, 8),
                Background = new SolidColorBrush(Color.FromRgb(51, 51, 55)),
                Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                BorderThickness = new Thickness(0),
                CaretBrush = new SolidColorBrush(Colors.White)
            };

            // Watermark placeholder behavior via GotFocus/LostFocus
            _searchBox.Tag = "Type a command...";
            _searchBox.Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150));
            _searchBox.Text = "Type a command...";
            _searchBox.GotFocus += (s, e) =>
            {
                if (_searchBox.Text == "Type a command...")
                {
                    _searchBox.Text = "";
                    _searchBox.Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220));
                }
            };
            _searchBox.LostFocus += (s, e) =>
            {
                if (string.IsNullOrEmpty(_searchBox.Text))
                {
                    _searchBox.Text = "Type a command...";
                    _searchBox.Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150));
                }
            };

            _searchBox.TextChanged += OnSearchTextChanged;
            _searchBox.PreviewKeyDown += OnSearchKeyDown;

            DockPanel.SetDock(_searchBox, Dock.Top);
            mainPanel.Children.Add(_searchBox);

            // Separator
            var separator = new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(63, 63, 70))
            };
            DockPanel.SetDock(separator, Dock.Top);
            mainPanel.Children.Add(separator);

            // Results ListBox
            _listBox = new ListBox
            {
                Background = new SolidColorBrush(Color.FromRgb(37, 37, 38)),
                BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_listBox, ScrollBarVisibility.Disabled);

            // Custom ItemTemplate
            var itemTemplate = CreateItemTemplate();
            _listBox.ItemTemplate = itemTemplate;

            // Bind ItemsSource to FilteredCommands
            _listBox.SetBinding(ItemsControl.ItemsSourceProperty,
                new Binding("FilteredCommands") { Source = _viewModel });
            _listBox.SetBinding(ListBox.SelectedIndexProperty,
                new Binding("SelectedIndex") { Source = _viewModel, Mode = BindingMode.TwoWay });

            _listBox.MouseDoubleClick += OnListBoxDoubleClick;

            // Style the ListBox selection colors
            var listBoxStyle = new Style(typeof(ListBoxItem));
            listBoxStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
            listBoxStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));

            var selectedTrigger = new Trigger
            {
                Property = ListBoxItem.IsSelectedProperty,
                Value = true
            };
            selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty,
                new SolidColorBrush(Color.FromRgb(9, 71, 113))));
            selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty,
                new SolidColorBrush(Colors.White)));
            listBoxStyle.Triggers.Add(selectedTrigger);

            var mouseOverTrigger = new Trigger
            {
                Property = UIElement.IsMouseOverProperty,
                Value = true
            };
            mouseOverTrigger.Setters.Add(new Setter(Control.BackgroundProperty,
                new SolidColorBrush(Color.FromRgb(62, 62, 64))));
            listBoxStyle.Triggers.Add(mouseOverTrigger);

            _listBox.ItemContainerStyle = listBoxStyle;

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

        private static DataTemplate CreateItemTemplate()
        {
            var template = new DataTemplate(typeof(CommandEntry));

            var gridFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.Grid));

            // Two columns: command name (left), shortcut (right)
            var col1 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col1.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            var col2 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col2.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);

            var colDefs = new FrameworkElementFactory(typeof(System.Windows.Controls.Grid));
            // We'll use a DockPanel instead for simpler layout
            var dockFactory = new FrameworkElementFactory(typeof(DockPanel));

            // Shortcut hint (right-aligned, gray)
            var shortcutFactory = new FrameworkElementFactory(typeof(TextBlock));
            shortcutFactory.SetBinding(TextBlock.TextProperty, new Binding("KeyboardShortcut"));
            shortcutFactory.SetValue(TextBlock.ForegroundProperty,
                new SolidColorBrush(Color.FromRgb(128, 128, 128)));
            shortcutFactory.SetValue(TextBlock.FontSizeProperty, 12.0);
            shortcutFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            shortcutFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));
            shortcutFactory.SetValue(DockPanel.DockProperty, Dock.Right);
            dockFactory.AppendChild(shortcutFactory);

            // Category label (dimmed, before name)
            var categoryFactory = new FrameworkElementFactory(typeof(TextBlock));
            categoryFactory.SetBinding(TextBlock.TextProperty, new Binding("Category"));
            categoryFactory.SetValue(TextBlock.ForegroundProperty,
                new SolidColorBrush(Color.FromRgb(100, 100, 100)));
            categoryFactory.SetValue(TextBlock.FontSizeProperty, 11.0);
            categoryFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            categoryFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            categoryFactory.SetValue(DockPanel.DockProperty, Dock.Left);
            dockFactory.AppendChild(categoryFactory);

            // Command name (bold, left-aligned)
            var nameFactory = new FrameworkElementFactory(typeof(TextBlock));
            nameFactory.SetBinding(TextBlock.TextProperty, new Binding("Name"));
            nameFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            nameFactory.SetValue(TextBlock.FontSizeProperty, 13.0);
            nameFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            nameFactory.SetValue(TextBlock.ForegroundProperty,
                new SolidColorBrush(Color.FromRgb(220, 220, 220)));
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
            if (text == "Type a command...")
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
