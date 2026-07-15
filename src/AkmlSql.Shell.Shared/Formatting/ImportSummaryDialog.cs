#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ui.Theme;

namespace AkmlSql.Shell.Shared.Formatting
{
    /// <summary>
    /// Spec 031 FR-012 — modal summary shown after a SQL Prompt style import completes. Lists
    /// every classified option (<see cref="ProfileImportOptionReport"/>) sorted by status
    /// (mapped → pending render → unsupported → unknown) so the user can see at a glance what
    /// mapped, what's pending a formatter render stage, and what AKML doesn't recognise yet.
    ///
    /// <para>
    /// Programmatic WPF only, mirrors <see cref="History.HistoryDiffWindow"/>: <see
    /// cref="ThemeAwareWindow"/> base (handles Background/Foreground token wiring, DTE-HWND
    /// owner fallback, <c>WindowStartupLocation.CenterOwner</c>), frozen brushes, <c>IsCancel</c>
    /// close button. Launched from <c>FormatStylesEditorWindow.ShowImportSummaryDialog</c>, which
    /// sets <see cref="Window.Owner"/> to itself explicitly before <c>ShowDialog()</c> — this
    /// dialog is nested inside an already-open AKML modal (not launched directly from a VS
    /// command), so the owner must be the open editor window rather than the DTE main window:
    /// only that makes WPF disable the editor window for the summary's lifetime and lets
    /// CenterOwner center on the right surface. <see cref="ThemeAwareWindow"/>'s DTE-owner
    /// fallback still guards the design-time/DTE-unreachable case (it only assigns Owner when
    /// null).
    /// </para>
    /// </summary>
    internal sealed class ImportSummaryDialog : ThemeAwareWindow
    {
        /// <summary>Display order for each <c>ProfileImportOptionReport.Status</c> value.</summary>
        private static readonly string[] StatusOrder =
        {
            "mapped",
            "mapped-pending-render",
            "unsupported",
            "unknown",
        };

        public ImportSummaryDialog(string profileName, string summaryText, ProfileImportOptionReport[]? reports)
        {
            Title = $"AKML SQL — Import Summary: {profileName}";
            Width = 720;
            Height = 560;
            MinWidth = 520;
            MinHeight = 360;

            BuildUi(summaryText, SortByStatus(reports));
        }

        private static List<ProfileImportOptionReport> SortByStatus(ProfileImportOptionReport[]? reports)
        {
            var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < StatusOrder.Length; i++) rank[StatusOrder[i]] = i;

            return (reports ?? Array.Empty<ProfileImportOptionReport>())
                .OrderBy(r => rank.TryGetValue(r.Status ?? string.Empty, out var i) ? i : int.MaxValue)
                .ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void BuildUi(string summaryText, List<ProfileImportOptionReport> reports)
        {
            var root = new Grid();
            root.SetResourceReference(Panel.BackgroundProperty, ThemeTokens.SurfaceCanvas);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // header
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // list
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // footer

            var header = new TextBlock
            {
                Text = summaryText,
                FontFamily = Typography.UiFont,
                FontSize = Typography.BodyStrong,
                FontWeight = Typography.WeightSemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(Spacing.Lg, Spacing.Lg, Spacing.Lg, Spacing.Md),
            };
            header.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var listView = BuildReportList(reports);
            Grid.SetRow(listView, 1);
            root.Children.Add(listView);

            var footer = new Border
            {
                Padding = new Thickness(Spacing.Lg, Spacing.Sm, Spacing.Lg, Spacing.Md),
                BorderThickness = new Thickness(0, 1, 0, 0),
            };
            footer.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.BorderSubtle);
            footer.SetResourceReference(Panel.BackgroundProperty, ThemeTokens.SurfacePanel);

            // FR-005 / CLAUDE.md WPF convention — Close is the only action here (read-only report),
            // so IsCancel = true is sufficient; there is no destructive default button to guard against.
            var closeBtn = new Button
            {
                Content = "Close",
                Padding = new Thickness(Spacing.Lg, Spacing.Sm, Spacing.Lg, Spacing.Sm),
                MinWidth = 80,
                HorizontalAlignment = HorizontalAlignment.Right,
                IsCancel = true,
                FontFamily = Typography.UiFont,
                FontSize = Typography.Body,
            };
            closeBtn.Click += (_, _) => Close();
            footer.Child = closeBtn;
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            Content = root;
        }

        private static ListView BuildReportList(List<ProfileImportOptionReport> reports)
        {
            var listView = new ListView
            {
                Margin = new Thickness(Spacing.Lg, 0, Spacing.Lg, Spacing.Md),
                BorderThickness = new Thickness(1),
                FontFamily = Typography.UiFont,
                FontSize = Typography.Small,
                ItemsSource = reports,
                ItemContainerStyle = BuildRowStyle(),
            };
            listView.SetResourceReference(Control.BackgroundProperty, ThemeTokens.SurfaceInput);
            listView.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextPrimary);
            listView.SetResourceReference(Control.BorderBrushProperty, ThemeTokens.BorderDefault);

            var gridView = new GridView
            {
                ColumnHeaderContainerStyle = BuildColumnHeaderStyle(),
            };
            gridView.Columns.Add(new GridViewColumn
            {
                Header = "Path",
                Width = 240,
                DisplayMemberBinding = new Binding(nameof(ProfileImportOptionReport.Path)),
            });
            gridView.Columns.Add(new GridViewColumn
            {
                Header = "Value",
                Width = 120,
                DisplayMemberBinding = new Binding(nameof(ProfileImportOptionReport.Value)),
            });
            gridView.Columns.Add(new GridViewColumn
            {
                Header = "Status",
                Width = 110,
                DisplayMemberBinding = new Binding(nameof(ProfileImportOptionReport.Status)),
            });
            gridView.Columns.Add(new GridViewColumn
            {
                Header = "Reason",
                Width = 220,
                DisplayMemberBinding = new Binding(nameof(ProfileImportOptionReport.Reason)),
            });
            listView.View = gridView;

            return listView;
        }

        /// <summary>
        /// Theme-aware <see cref="GridViewColumnHeader"/> style. The Aero2 default header
        /// template paints its own light gradient chrome and ignores the Background property —
        /// the same dark-theme trap class as the repo's documented ComboBox retemplating fix —
        /// so the header is retemplated to a plain Border that template-binds
        /// Background/BorderBrush/Padding. Trade-off: the default template's resize gripper
        /// (PART_HeaderGripper) is dropped, so columns are fixed-width — acceptable for a
        /// read-only report. Theme tokens flow via <see cref="DynamicResourceExtension"/>
        /// setters (the repo's established Style idiom — see EditorToolbar/ObjectDefinitionPanel).
        /// </summary>
        private static Style BuildColumnHeaderStyle()
        {
            var template = new ControlTemplate(typeof(GridViewColumnHeader));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            border.AppendChild(content);
            template.VisualTree = border;

            var style = new Style(typeof(GridViewColumnHeader));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(ThemeTokens.SurfaceInput)));
            style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(ThemeTokens.TextPrimary)));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension(ThemeTokens.BorderDefault)));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(Spacing.Sm, Spacing.Xs, Spacing.Sm, Spacing.Xs)));
            style.Setters.Add(new Setter(Control.FontFamilyProperty, Typography.UiFont));
            style.Setters.Add(new Setter(Control.FontSizeProperty, Typography.Small));
            style.Setters.Add(new Setter(Control.FontWeightProperty, Typography.WeightSemiBold));
            return style;
        }

        /// <summary>
        /// Theme-aware <see cref="ListViewItem"/> container style — mirrors
        /// <c>HistoryToolWindowControl.CreateQueryItemContainerStyle</c> (the repo's shipped
        /// ListView row pattern): transparent base so the ListView's SurfaceInput shows
        /// through, SurfaceSelection on select, SurfaceHover on hover-not-selected. Foreground
        /// stays TextPrimary in every state (selected-row text must never drop to a muted
        /// token — the repo's documented high-contrast selection rule).
        /// </summary>
        private static Style BuildRowStyle()
        {
            var style = new Style(typeof(ListViewItem));
            style.Setters.Add(new Setter(Control.BackgroundProperty, System.Windows.Media.Brushes.Transparent));
            style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(ThemeTokens.TextPrimary)));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2, 1, 2, 1)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));

            var selected = new Trigger { Property = ListViewItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(ThemeTokens.SurfaceSelection)));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(ThemeTokens.TextPrimary)));
            style.Triggers.Add(selected);

            var hover = new MultiTrigger();
            hover.Conditions.Add(new Condition(UIElement.IsMouseOverProperty, true));
            hover.Conditions.Add(new Condition(ListViewItem.IsSelectedProperty, false));
            hover.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension(ThemeTokens.SurfaceHover)));
            style.Triggers.Add(hover);

            return style;
        }
    }
}
