#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AkmlSql.Core.Config;
using AkmlSql.Core.Text;
using AkmlSql.Shell.Shared.Ui.SqlPreview;
using AkmlSql.Shell.Shared.Ui.Theme;

namespace AkmlSql.Shell.Shared.History
{
    /// <summary>Spec 040 (HIS-10): one side of a History comparison — a name, a time (ISO) and the text.</summary>
    internal sealed class HistoryCompareSide
    {
        public HistoryCompareSide(string name, string? time, string text)
        {
            Name = name ?? string.Empty;
            Time = time;
            Text = text ?? string.Empty;
        }

        public string Name { get; }
        public string? Time { get; }
        public string Text { get; }

        /// <summary>"‹name› — ‹time›", or the name alone when the time is unknown.</summary>
        public string Header =>
            DateTime.TryParse(Time, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
                ? $"{Name} — {HistoryTimeFormat.Absolute(at.ToLocalTime())}"
                : Name;
    }

    /// <summary>
    /// Spec 040 (HIS-10) — the two sides of a comparison laid out line by line from
    /// <see cref="LineDiff.Diff"/>: a line missing on one side is a blank filler line there, so
    /// the sides stay aligned. Each side has its text, its gutter numbers (blank on fillers) and a
    /// tint per added, removed or changed line.
    /// </summary>
    internal static class HistoryDiffLayout
    {
        internal sealed class Side
        {
            public Side(string text, IReadOnlyList<int?> numbers, IReadOnlyDictionary<int, string> tints)
            {
                Text = text;
                Numbers = numbers;
                Tints = tints;
            }

            public string Text { get; }
            public IReadOnlyList<int?> Numbers { get; }
            public IReadOnlyDictionary<int, string> Tints { get; }
        }

        internal sealed class Result
        {
            public Result(Side left, Side right, int added, int removed, int changed)
            {
                Left = left;
                Right = right;
                Added = added;
                Removed = removed;
                Changed = changed;
            }

            public Side Left { get; }
            public Side Right { get; }
            public int Added { get; }
            public int Removed { get; }
            public int Changed { get; }

            /// <summary>"2 changed · 1 added · 3 removed", or "No differences".</summary>
            public string Summary
            {
                get
                {
                    var parts = new List<string>(3);
                    if (Changed > 0) parts.Add($"{Changed} changed");
                    if (Added > 0) parts.Add($"{Added} added");
                    if (Removed > 0) parts.Add($"{Removed} removed");
                    return parts.Count == 0 ? "No differences" : string.Join(" · ", parts);
                }
            }
        }

        public static Result Build(string? left, string? right)
        {
            var rows = LineDiff.Diff(left, right);
            var leftLines = new List<string>(rows.Count);
            var rightLines = new List<string>(rows.Count);
            var leftNumbers = new List<int?>(rows.Count);
            var rightNumbers = new List<int?>(rows.Count);
            var leftTints = new Dictionary<int, string>();
            var rightTints = new Dictionary<int, string>();
            int added = 0, removed = 0, changed = 0;

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                leftLines.Add(row.LeftLine.HasValue ? row.LeftText : string.Empty);
                rightLines.Add(row.RightLine.HasValue ? row.RightText : string.Empty);
                leftNumbers.Add(row.LeftLine);
                rightNumbers.Add(row.RightLine);
                switch (row.Kind)
                {
                    case LineDiffKind.Added:
                        rightTints[i] = ThemeTokens.StatusSuccess;
                        added++;
                        break;
                    case LineDiffKind.Removed:
                        leftTints[i] = ThemeTokens.StatusDanger;
                        removed++;
                        break;
                    case LineDiffKind.Changed:
                        leftTints[i] = ThemeTokens.StatusWarning;
                        rightTints[i] = ThemeTokens.StatusWarning;
                        changed++;
                        break;
                }
            }

            return new Result(
                new Side(string.Join("\n", leftLines), leftNumbers, leftTints),
                new Side(string.Join("\n", rightLines), rightNumbers, rightTints),
                added, removed, changed);
        }
    }

    /// <summary>
    /// Side-by-side comparison of two SQL texts from SQL History: two versions of one query
    /// ("Compare with current") or two queries. Spec 040 (HIS-10): each side is headed by its name
    /// and time; lines are aligned, with added, removed and changed lines tinted; the two sides
    /// scroll together.
    /// </summary>
    internal class HistoryDiffWindow : ThemeAwareWindow
    {
        private readonly HistoryCompareSide _left;
        private readonly HistoryCompareSide _right;
        private bool _syncing;

        public HistoryDiffWindow(HistoryCompareSide left, HistoryCompareSide right)
        {
            _left = left ?? throw new ArgumentNullException(nameof(left));
            _right = right ?? throw new ArgumentNullException(nameof(right));

            Title = WindowTitles.For("SQL History comparison");
            Width = 1000;
            Height = 600;

            BuildUi();
        }

        /// <summary>The two previews, for tests.</summary>
        internal SqlPreviewView? LeftView { get; private set; }
        internal SqlPreviewView? RightView { get; private set; }
        internal TextBlock? LeftHeader { get; private set; }
        internal TextBlock? RightHeader { get; private set; }

        private void BuildUi()
        {
            var layout = HistoryDiffLayout.Build(_left.Text, _right.Text);

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // Headers
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // Summary + buttons

            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            LeftHeader = MakeHeader(_left.Header);
            Grid.SetRow(LeftHeader, 0); Grid.SetColumn(LeftHeader, 0);
            mainGrid.Children.Add(LeftHeader);

            RightHeader = MakeHeader(_right.Header);
            Grid.SetRow(RightHeader, 0); Grid.SetColumn(RightHeader, 2);
            mainGrid.Children.Add(RightHeader);

            LeftView = MakeSide(layout.Left);
            Grid.SetRow(LeftView, 1); Grid.SetColumn(LeftView, 0);
            mainGrid.Children.Add(LeftView);

            var splitter = new GridSplitter
            {
                Width = 3,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            };
            splitter.SetResourceReference(BackgroundProperty, ThemeTokens.BorderSplitter);
            Grid.SetRow(splitter, 1); Grid.SetColumn(splitter, 1);
            mainGrid.Children.Add(splitter);

            RightView = MakeSide(layout.Right);
            Grid.SetRow(RightView, 1); Grid.SetColumn(RightView, 2);
            mainGrid.Children.Add(RightView);

            // The two sides are aligned line by line, so they scroll together.
            LeftView.Scroller.ScrollChanged += (_, e) => Follow(RightView.Scroller, e);
            RightView.Scroller.ScrollChanged += (_, e) => Follow(LeftView.Scroller, e);

            var bottom = new DockPanel { Margin = new Thickness(Spacing.Sm, Spacing.Xs, Spacing.Sm, Spacing.Sm) };
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal };
            buttonPanel.Children.Add(MakeButton("Copy left", () => CopyText(_left.Text)));
            buttonPanel.Children.Add(MakeButton("Copy right", () => CopyText(_right.Text)));
            var closeBtn = MakeButton("Close", () => Close());
            closeBtn.IsCancel = true;
            closeBtn.FocusVisualStyle = FocusVisualStyles.HighStakes;
            buttonPanel.Children.Add(closeBtn);
            DockPanel.SetDock(buttonPanel, Dock.Right);
            bottom.Children.Add(buttonPanel);

            var summary = new TextBlock
            {
                Text = layout.Summary,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
                VerticalAlignment = VerticalAlignment.Center,
            };
            summary.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
            bottom.Children.Add(summary);

            Grid.SetRow(bottom, 2);
            Grid.SetColumnSpan(bottom, 3);
            mainGrid.Children.Add(bottom);

            Content = mainGrid;
        }

        private void Follow(ScrollViewer other, ScrollChangedEventArgs e)
        {
            if (_syncing) return;
            _syncing = true;
            try
            {
                other.ScrollToVerticalOffset(e.VerticalOffset);
                other.ScrollToHorizontalOffset(e.HorizontalOffset);
            }
            finally { _syncing = false; }
        }

        private static SqlPreviewView MakeSide(HistoryDiffLayout.Side side)
        {
            var view = new SqlPreviewView
            {
                Margin = new Thickness(Spacing.Xs),
                GutterNumbers = side.Numbers,
            };
            view.SetResourceReference(BackgroundProperty, ThemeTokens.EditorPopupBackground);
            view.Show(side.Text, null);
            view.LineTints = side.Tints;
            return view;
        }

        private static void CopyText(string text)
        {
            try { Clipboard.SetText(text); }
            catch (System.Runtime.InteropServices.ExternalException) { /* clipboard busy; the user can retry */ }
        }

        private static TextBlock MakeHeader(string text)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontFamily = Typography.UiFont,
                FontWeight = Typography.WeightBold,
                FontSize = Typography.BodyStrong,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Padding = new Thickness(Spacing.Sm, Spacing.Xs, Spacing.Sm, Spacing.Xs),
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            return tb;
        }

        private static Button MakeButton(string content, Action onClick)
        {
            var btn = new Button
            {
                Content = content,
                Padding = new Thickness(Spacing.Md, Spacing.Xs, Spacing.Md, Spacing.Xs),
                Margin = new Thickness(Spacing.Xs, 0, 0, 0),
            };
            btn.Click += (_, _) => onClick();
            return btn;
        }
    }
}
