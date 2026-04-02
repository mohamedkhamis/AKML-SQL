#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AkmlSql.Shell.Shared.Ui;

namespace AkmlSql.Shell.Shared.History
{
    /// <summary>
    /// A simple side-by-side diff window for comparing two SQL history entries.
    /// Shows two read-only text areas with monospaced font, along with
    /// basic header labels indicating "Left" and "Right".
    /// </summary>
    internal class HistoryDiffWindow : Window
    {
        private readonly string _leftSql;
        private readonly string _rightSql;
        private ScrollViewer? _leftScrollViewer;
        private ScrollViewer? _rightScrollViewer;
        private bool _isScrollSyncing;

        public HistoryDiffWindow(string leftSql, string rightSql)
        {
            _leftSql = leftSql;
            _rightSql = rightSql;

            Title = "SQL History Comparison";
            Width = 1000;
            Height = 600;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            // Try to set the owner to the active VS window
            try
            {
                var dte = (EnvDTE.DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte?.MainWindow != null)
                {
                    var hwnd = new System.Windows.Interop.WindowInteropHelper(this);
                    hwnd.Owner = (IntPtr)dte.MainWindow.HWnd;
                }
            }
            catch
            {
                // Not critical if we can't set owner
            }

            BuildUi();
        }

        private void BuildUi()
        {
            var theme = ThemeManager.Instance;
            var bg = new SolidColorBrush(theme.Background);
            var fg = new SolidColorBrush(theme.Foreground);
            var borderBrush = new SolidColorBrush(theme.Border);
            bg.Freeze();
            fg.Freeze();
            borderBrush.Freeze();

            Background = bg;

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // Headers
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // Button row

            // Two columns for side-by-side
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) }); // Splitter
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Headers
            var leftHeader = new TextBlock
            {
                Text = "Entry 1 (Left)",
                FontWeight = FontWeights.Bold,
                Foreground = fg,
                Padding = new Thickness(8, 4, 8, 4)
            };
            Grid.SetRow(leftHeader, 0);
            Grid.SetColumn(leftHeader, 0);
            mainGrid.Children.Add(leftHeader);

            var rightHeader = new TextBlock
            {
                Text = "Entry 2 (Right)",
                FontWeight = FontWeights.Bold,
                Foreground = fg,
                Padding = new Thickness(8, 4, 8, 4)
            };
            Grid.SetRow(rightHeader, 0);
            Grid.SetColumn(rightHeader, 2);
            mainGrid.Children.Add(rightHeader);

            // Left SQL text area
            var leftTextBox = new TextBox
            {
                Text = _leftSql,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Background = new SolidColorBrush(theme.PreviewBackground),
                Foreground = fg,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(4),
                Padding = new Thickness(4)
            };
            Grid.SetRow(leftTextBox, 1);
            Grid.SetColumn(leftTextBox, 0);
            mainGrid.Children.Add(leftTextBox);

            // Splitter
            var splitter = new GridSplitter
            {
                Width = 3,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = borderBrush,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext
            };
            Grid.SetRow(splitter, 1);
            Grid.SetColumn(splitter, 1);
            mainGrid.Children.Add(splitter);

            // Right SQL text area
            var rightTextBox = new TextBox
            {
                Text = _rightSql,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Background = new SolidColorBrush(theme.PreviewBackground),
                Foreground = fg,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(4),
                Padding = new Thickness(4)
            };
            Grid.SetRow(rightTextBox, 1);
            Grid.SetColumn(rightTextBox, 2);
            mainGrid.Children.Add(rightTextBox);

            // Button row
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(8, 4, 8, 8)
            };

            var copyLeftBtn = new Button
            {
                Content = "Copy Left",
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(0, 0, 4, 0)
            };
            copyLeftBtn.Click += (s, e) =>
            {
                try { Clipboard.SetText(_leftSql); } catch { /* Clipboard may be locked */ }
            };
            buttonPanel.Children.Add(copyLeftBtn);

            var copyRightBtn = new Button
            {
                Content = "Copy Right",
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(0, 0, 4, 0)
            };
            copyRightBtn.Click += (s, e) =>
            {
                try { Clipboard.SetText(_rightSql); } catch { /* Clipboard may be locked */ }
            };
            buttonPanel.Children.Add(copyRightBtn);

            var closeBtn = new Button
            {
                Content = "Close",
                Padding = new Thickness(12, 4, 12, 4),
                IsCancel = true
            };
            closeBtn.Click += (s, e) => Close();
            buttonPanel.Children.Add(closeBtn);

            Grid.SetRow(buttonPanel, 2);
            Grid.SetColumn(buttonPanel, 0);
            Grid.SetColumnSpan(buttonPanel, 3);
            mainGrid.Children.Add(buttonPanel);

            Content = mainGrid;
        }
    }
}
