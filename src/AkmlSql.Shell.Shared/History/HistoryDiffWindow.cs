#nullable enable
using System.Windows;
using System.Windows.Controls;
using AkmlSql.Shell.Shared.Ui.Theme;

namespace AkmlSql.Shell.Shared.History
{
    /// <summary>
    /// Side-by-side diff window for comparing two SQL history entries. Two read-only
    /// monospaced text areas with header labels.
    /// </summary>
    internal class HistoryDiffWindow : ThemeAwareWindow
    {
        private readonly string _leftSql;
        private readonly string _rightSql;

        public HistoryDiffWindow(string leftSql, string rightSql)
        {
            _leftSql = leftSql;
            _rightSql = rightSql;

            Title = "SQL History Comparison";
            Width = 1000;
            Height = 600;

            BuildUi();
        }

        private void BuildUi()
        {
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // Headers
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // Button row

            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Headers
            var leftHeader = MakeHeader("Entry 1 (Left)");
            Grid.SetRow(leftHeader, 0); Grid.SetColumn(leftHeader, 0);
            mainGrid.Children.Add(leftHeader);

            var rightHeader = MakeHeader("Entry 2 (Right)");
            Grid.SetRow(rightHeader, 0); Grid.SetColumn(rightHeader, 2);
            mainGrid.Children.Add(rightHeader);

            // Left SQL
            var leftTextBox = MakeReadOnlySqlBox(_leftSql);
            Grid.SetRow(leftTextBox, 1); Grid.SetColumn(leftTextBox, 0);
            mainGrid.Children.Add(leftTextBox);

            // Splitter
            var splitter = new GridSplitter
            {
                Width = 3,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            };
            splitter.SetResourceReference(BackgroundProperty, ThemeTokens.BorderSplitter);
            Grid.SetRow(splitter, 1); Grid.SetColumn(splitter, 1);
            mainGrid.Children.Add(splitter);

            // Right SQL
            var rightTextBox = MakeReadOnlySqlBox(_rightSql);
            Grid.SetRow(rightTextBox, 1); Grid.SetColumn(rightTextBox, 2);
            mainGrid.Children.Add(rightTextBox);

            // Button row
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(Spacing.Sm, Spacing.Xs, Spacing.Sm, Spacing.Sm),
            };

            buttonPanel.Children.Add(MakeButton("Copy Left", () =>
            {
                try { Clipboard.SetText(_leftSql); } catch { /* clipboard may be locked */ }
            }));
            buttonPanel.Children.Add(MakeButton("Copy Right", () =>
            {
                try { Clipboard.SetText(_rightSql); } catch { /* clipboard may be locked */ }
            }));

            var closeBtn = MakeButton("Close", () => Close());
            closeBtn.IsCancel = true;
            closeBtn.FocusVisualStyle = FocusVisualStyles.HighStakes;
            buttonPanel.Children.Add(closeBtn);

            Grid.SetRow(buttonPanel, 2);
            Grid.SetColumn(buttonPanel, 0);
            Grid.SetColumnSpan(buttonPanel, 3);
            mainGrid.Children.Add(buttonPanel);

            Content = mainGrid;
        }

        private static TextBlock MakeHeader(string text)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontFamily = Typography.UiFont,
                FontWeight = Typography.WeightBold,
                FontSize = Typography.BodyStrong,
                Padding = new Thickness(Spacing.Sm, Spacing.Xs, Spacing.Sm, Spacing.Xs),
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            return tb;
        }

        private static TextBox MakeReadOnlySqlBox(string sql)
        {
            var tb = new TextBox
            {
                Text = sql,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = Typography.MonoFont,
                FontSize = Typography.Body,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(Spacing.Xs),
                Padding = new Thickness(Spacing.Xs),
            };
            tb.SetResourceReference(BackgroundProperty, ThemeTokens.EditorPopupBackground);
            tb.SetResourceReference(ForegroundProperty, ThemeTokens.TextPrimary);
            tb.SetResourceReference(BorderBrushProperty, ThemeTokens.BorderDefault);
            return tb;
        }

        private static Button MakeButton(string content, System.Action onClick)
        {
            var btn = new Button
            {
                Content = content,
                Padding = new Thickness(Spacing.Md, Spacing.Xs, Spacing.Md, Spacing.Xs),
                Margin = new Thickness(0, 0, Spacing.Xs, 0),
            };
            btn.Click += (_, _) => onClick();
            return btn;
        }
    }
}
