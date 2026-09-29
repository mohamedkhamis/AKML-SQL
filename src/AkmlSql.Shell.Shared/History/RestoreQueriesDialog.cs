#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ui.Theme;

namespace AkmlSql.Shell.Shared.History
{
    /// <summary>
    /// Spec 040 (T143, HIS-14) — "Restore queries": the queries that were open when SSMS last closed,
    /// each with its name, <c>server · database</c> and time, all checked; <b>Restore selected</b>
    /// reopens the checked ones, <b>Not now</b> none. Follows the current theme.
    /// </summary>
    internal sealed class RestoreQueriesDialog : ThemeAwareWindow
    {
        private readonly List<(HistoryEntryDto Entry, CheckBox Box)> _rows = new List<(HistoryEntryDto, CheckBox)>();
        private bool _restore;

        public RestoreQueriesDialog(IReadOnlyList<HistoryEntryDto> entries)
        {
            Title = "AKML SQL – Restore queries";
            Width = 520;
            SizeToContent = SizeToContent.Height;
            MaxHeight = 560;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;

            var root = new DockPanel { Margin = new Thickness(Spacing.Lg) };

            var intro = new TextBlock
            {
                Text = entries.Count == 1
                    ? "This query was open when SSMS last closed. Restore it?"
                    : $"These {entries.Count} queries were open when SSMS last closed. Restore them?",
                TextWrapping = TextWrapping.Wrap,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
                Margin = new Thickness(0, 0, 0, Spacing.Md),
            };
            intro.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            DockPanel.SetDock(intro, Dock.Top);
            root.Children.Add(intro);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, Spacing.Md, 0, 0),
            };
            RestoreButton = new Button
            {
                Content = "Restore selected",
                IsDefault = true,
                Padding = new Thickness(Spacing.Md, Spacing.Xs, Spacing.Md, Spacing.Xs),
                Margin = new Thickness(0, 0, Spacing.Sm, 0),
                FocusVisualStyle = FocusVisualStyles.HighStakes,
            };
            RestoreButton.Click += (_, __) => { _restore = true; Close(); };
            var notNow = new Button
            {
                Content = "Not now",
                IsCancel = true,
                Padding = new Thickness(Spacing.Md, Spacing.Xs, Spacing.Md, Spacing.Xs),
                FocusVisualStyle = FocusVisualStyles.HighStakes,
            };
            buttons.Children.Add(RestoreButton);
            buttons.Children.Add(notNow);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            var list = new StackPanel();
            foreach (var entry in entries)
            {
                var name = new TextBlock { Text = HistoryRowDisplay.DisplayNameFor(entry), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
                name.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
                var detail = new TextBlock { Text = DetailFor(entry), FontSize = Typography.Body - 1, TextTrimming = TextTrimming.CharacterEllipsis };
                detail.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
                var box = new CheckBox
                {
                    IsChecked = true,
                    Margin = new Thickness(0, 0, 0, Spacing.Sm),
                    VerticalContentAlignment = VerticalAlignment.Top,
                    Content = new StackPanel { Children = { name, detail } },
                };
                box.Checked += (_, __) => UpdateRestoreEnabled();
                box.Unchecked += (_, __) => UpdateRestoreEnabled();
                _rows.Add((entry, box));
                list.Children.Add(box);
            }
            var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 380 };
            root.Children.Add(scroll);

            Content = root;
        }

        internal Button RestoreButton { get; }

        /// <summary>The rows' check boxes, in order (for tests).</summary>
        internal IReadOnlyList<CheckBox> Boxes => _rows.Select(r => r.Box).ToList();

        /// <summary>The checked entries.</summary>
        internal IReadOnlyList<HistoryEntryDto> Checked => _rows.Where(r => r.Box.IsChecked == true).Select(r => r.Entry).ToList();

        /// <summary>"server · database · time".</summary>
        internal static string DetailFor(HistoryEntryDto entry)
        {
            var parts = new List<string>(2);
            var where = HistoryRowDisplay.ConnectionLabel(entry.Server, entry.Database);
            if (where.Length > 0) parts.Add(where);
            if (DateTime.TryParse(entry.ExecutedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at))
                parts.Add(HistoryTimeFormat.Absolute(at.ToLocalTime()));
            return string.Join(" · ", parts);
        }

        private void UpdateRestoreEnabled() => RestoreButton.IsEnabled = _rows.Any(r => r.Box.IsChecked == true);

        /// <summary>Shows the dialog; returns the queries to restore (none for "Not now" or closing it).</summary>
        public static IReadOnlyList<HistoryEntryDto> Choose(IReadOnlyList<HistoryEntryDto> entries)
        {
            if (entries == null || entries.Count == 0) return Array.Empty<HistoryEntryDto>();
            var dialog = new RestoreQueriesDialog(entries);
            dialog.ShowDialog();
            return dialog._restore ? dialog.Checked : Array.Empty<HistoryEntryDto>();
        }
    }
}
