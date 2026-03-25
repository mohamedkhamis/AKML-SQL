#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Serilog;

namespace AkmlSql.Shell.Shared.Productivity.Navigation
{
    /// <summary>
    /// WPF UserControl for inline peek definition display. Shows a read-only SQL definition
    /// below the current line in the editor as an adornment. Press Escape to dismiss.
    /// </summary>
    internal sealed class PeekDefinitionControl : UserControl
    {
        private readonly TextBox _contentBox;
        private readonly TextBlock _headerBlock;

        /// <summary>
        /// Raised when the user presses Escape or clicks the close button.
        /// The host adornment layer should handle this to remove the control.
        /// </summary>
        public event EventHandler? Dismissed;

        /// <summary>
        /// Raised when the user double-clicks the content, requesting a full definition view.
        /// </summary>
        public event EventHandler? OpenFullRequested;

        public PeekDefinitionControl(string definition, string objectName, string objectType)
        {
            // Build the control layout
            var grid = new System.Windows.Controls.Grid
            {
                MaxHeight = 300,
                MaxWidth = 800,
                MinWidth = 400
            };

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Header bar with object info and close button
            var headerPanel = new DockPanel
            {
                Background = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)),
                LastChildFill = true
            };

            var closeButton = new Button
            {
                Content = "X",
                Width = 24,
                Height = 24,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(4, 0, 4, 0),
                ToolTip = "Close (Escape)"
            };
            closeButton.Click += (_, __) => Dismissed?.Invoke(this, EventArgs.Empty);
            DockPanel.SetDock(closeButton, Dock.Right);
            headerPanel.Children.Add(closeButton);

            _headerBlock = new TextBlock
            {
                Text = $"  {objectType}: {objectName}",
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 2, 4, 2)
            };
            headerPanel.Children.Add(_headerBlock);

            System.Windows.Controls.Grid.SetRow(headerPanel, 0);
            grid.Children.Add(headerPanel);

            // Content area with read-only SQL text
            _contentBox = new TextBox
            {
                Text = definition,
                IsReadOnly = true,
                FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New"),
                FontSize = 12,
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)),
                BorderThickness = new Thickness(1),
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(8, 4, 8, 4)
            };

            _contentBox.MouseDoubleClick += (_, __) => OpenFullRequested?.Invoke(this, EventArgs.Empty);

            System.Windows.Controls.Grid.SetRow(_contentBox, 1);
            grid.Children.Add(_contentBox);

            // Wrap in a border for consistent appearance
            var border = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Child = grid,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 8,
                    Opacity = 0.3,
                    ShadowDepth = 2
                }
            };

            Content = border;

            // Handle Escape key
            KeyDown += OnKeyDown;
            Loaded += (_, __) =>
            {
                _contentBox.Focus();
            };
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Dismissed?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Updates the displayed definition text.
        /// </summary>
        public void UpdateContent(string definition, string objectName, string objectType)
        {
            _contentBox.Text = definition;
            _headerBlock.Text = $"  {objectType}: {objectName}";
        }
    }
}
