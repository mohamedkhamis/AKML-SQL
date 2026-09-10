#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Ui.Theme;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Spec 037 (US2, T038, research R12) — the agent <see cref="ListBox"/> and its
    /// Add / Duplicate / Remove / Set-active button row for the AI Assistance page, following
    /// the <c>TabsPage</c> coloring-rules precedent (TabsPage.cs:49-71). Row rendering — name,
    /// "provider · model", the health badge, the active marker — lives here so
    /// <c>AiAssistancePage</c> stays reviewable; every CRUD decision is the page's.
    ///
    /// Theme (FR-036, the spec-036 bug class): the item style pairs every hover/selection
    /// background with a foreground from <see cref="PageTheme"/>, and row text carries NO local
    /// <see cref="TextBlock.Foreground"/> — except the health badge (US5, T082), whose semantic
    /// colour (green ready, red failed, amber not-tested/needs-key) is the documented
    /// theme-token exception. The badge's own style carries ancestor-state triggers that flip it
    /// back to the row's hover/selected foreground, so the spec-036 legibility guarantee holds
    /// for it too. The badge also renders the last-checked time beside the status (FR-053).
    /// </summary>
    internal sealed class AiAgentListView
    {
        internal ListBox List { get; }
        internal Button AddButton { get; }
        internal Button DuplicateButton { get; }
        internal Button RemoveButton { get; }
        internal Button SetActiveButton { get; }

        private readonly StackPanel _buttonRow;
        private readonly Brush _hoverForeground;
        private readonly Brush _selectedForeground;
        private readonly bool _dark;

        internal AiAgentListView(PageTheme theme)
        {
            _hoverForeground = theme.FgPrimary;
            _selectedForeground = theme.SelectedText;
            // The palettes are singletons; the semantic badge colours are picked per variant so
            // they read on the list's Input surface in either theme.
            _dark = ReferenceEquals(theme, PageTheme.Dark);

            List = new ListBox
            {
                Height = 140,
                Margin = new Thickness(20, 4, 20, 4),
                BorderThickness = new Thickness(1),
                BorderBrush = theme.ComboBorder,
                Background = theme.Input,
                FontSize = 13,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            AutomationProperties.SetName(List, "AI agents");
            List.ItemContainerStyle = BuildItemStyle(theme);

            AddButton = MakeButton("Add", "Add a new AI agent");
            DuplicateButton = MakeButton("Duplicate", "Duplicate the selected agent, including its key");
            RemoveButton = MakeButton("Remove", "Remove the selected agent");
            SetActiveButton = MakeButton("Set as active", "Make the selected agent the active agent");

            _buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(20, 4, 20, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            _buttonRow.Children.Add(AddButton);
            _buttonRow.Children.Add(DuplicateButton);
            _buttonRow.Children.Add(RemoveButton);
            _buttonRow.Children.Add(SetActiveButton);
        }

        /// <summary>Appends the list and the button row to the page, in tab order (list → buttons).</summary>
        internal void AddTo(StackPanel panel)
        {
            panel.Children.Add(List);
            panel.Children.Add(_buttonRow);
        }

        /// <summary>
        /// Rebuilds the rows from the working copy, marking <paramref name="activeId"/> with ●
        /// and selecting <paramref name="selectedId"/>. Rows carry no state, so a full rebuild
        /// after every commit keeps names, markers and badges honest.
        /// </summary>
        internal void Render(IReadOnlyList<AiAgent> agents, string activeId, string selectedId)
        {
            List.Items.Clear();
            ListBoxItem? toSelect = null;
            foreach (var agent in agents)
            {
                if (agent == null) continue;
                var item = new ListBoxItem
                {
                    Content = BuildRow(agent, activeId),
                    Tag = agent.Id,
                };
                AutomationProperties.SetName(item, "Agent " + (agent.Name ?? string.Empty));
                List.Items.Add(item);
                if (string.Equals(agent.Id, selectedId, StringComparison.Ordinal))
                    toSelect = item;
            }
            List.SelectedItem = toSelect;
        }

        /// <summary>The badge label for one agent — the four FR-053 statuses, glyph + label.</summary>
        internal static string HealthLabelFor(AiAgent agent)
        {
            var status = agent?.Health?.Status ?? AgentHealthStatus.Unknown;
            if (string.Equals(status, AgentHealthStatus.Ready, StringComparison.OrdinalIgnoreCase))
                return "✔ Ready";
            if (string.Equals(status, AgentHealthStatus.NeedsKey, StringComparison.OrdinalIgnoreCase))
                return "⚠ Needs API key";
            if (string.Equals(status, AgentHealthStatus.Failed, StringComparison.OrdinalIgnoreCase))
                return "✖ Failed";
            return "– Not tested";
        }

        /// <summary>
        /// FR-053 (US5, T082): the badge text — the status label with the last-checked time
        /// beside it (local time; today's checks show the time, older ones the date and time).
        /// A missing or unparseable stamp degrades to the bare label.
        /// </summary>
        internal static string HealthBadgeTextFor(AiAgent agent)
        {
            var label = HealthLabelFor(agent);
            var stamp = agent?.Health?.CheckedUtc;
            if (string.IsNullOrEmpty(stamp)) return label;
            if (!DateTime.TryParse(stamp, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var checkedUtc)) return label;
            var local = checkedUtc.ToLocalTime();
            var format = local.Date == DateTime.Today ? "HH:mm" : "yyyy-MM-dd HH:mm";
            return label + " · " + local.ToString(format, CultureInfo.InvariantCulture);
        }

        // Semantic colours — the documented theme-token exception (contracts/options-agents-ui.md
        // § Theme and accessibility): green ready, red failed, amber not-tested / needs-key.
        // Chosen per variant so the badge clears WCAG 4.5:1 on the list's Input surface
        // (#FFFFFF in Light, #1E293B in Dark); the values are pinned by AiAgentHealthTests.
        private static readonly SolidColorBrush ReadyLightBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)));
        private static readonly SolidColorBrush ReadyDarkBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)));
        private static readonly SolidColorBrush FailedLightBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)));
        private static readonly SolidColorBrush FailedDarkBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE5, 0x73, 0x73)));
        private static readonly SolidColorBrush WarnLightBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xA8, 0x64, 0x00)));
        private static readonly SolidColorBrush WarnDarkBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00)));

        private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

        /// <summary>The semantic badge brush for a health status, per theme variant.</summary>
        internal static SolidColorBrush BadgeBrushFor(string? status, bool dark)
        {
            if (string.Equals(status, AgentHealthStatus.Ready, StringComparison.OrdinalIgnoreCase))
                return dark ? ReadyDarkBrush : ReadyLightBrush;
            if (string.Equals(status, AgentHealthStatus.Failed, StringComparison.OrdinalIgnoreCase))
                return dark ? FailedDarkBrush : FailedLightBrush;
            // unknown and needsKey share the amber "not verified" semantics.
            return dark ? WarnDarkBrush : WarnLightBrush;
        }

        // active marker │ name │ provider · model │ health badge — one line per the contract
        // layout. No local Foreground anywhere EXCEPT the badge: the item container's inherited
        // foreground must reach every other run so selection stays legible (spec 036); the
        // badge's style flips its semantic colour on hover/selection to the same effect.
        private UIElement BuildRow(AiAgent agent, string activeId)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var marker = new TextBlock
            {
                Text = string.Equals(agent.Id, activeId, StringComparison.Ordinal) ? "●" : string.Empty,
                FontSize = 11,
                Width = 16,
                VerticalAlignment = VerticalAlignment.Center,
            };
            grid.Children.Add(marker);

            var name = new TextBlock
            {
                Text = agent.Name ?? string.Empty,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);

            var detail = new TextBlock
            {
                Text = ProviderModelLabel(agent),
                FontSize = 11,
                Opacity = 0.72,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 12, 0),
            };
            Grid.SetColumn(detail, 2);
            grid.Children.Add(detail);

            var badge = new TextBlock
            {
                Text = HealthBadgeTextFor(agent),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Style = BuildBadgeStyle(agent),
            };
            Grid.SetColumn(badge, 3);
            grid.Children.Add(badge);

            return grid;
        }

        /// <summary>
        /// The badge's foreground is the semantic status colour in the normal state and the
        /// row's own foreground on hover/selection — declared hover first so a
        /// selected-and-hovered badge keeps the selected pairing, mirroring the item style.
        /// Without these triggers the local semantic brush would stay green/red on the
        /// Selected background (the spec-036 illegibility class).
        /// </summary>
        private Style BuildBadgeStyle(AiAgent agent)
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.ForegroundProperty,
                BadgeBrushFor(agent?.Health?.Status, _dark)));

            var hoverTrigger = new DataTrigger
            {
                Binding = AncestorRowBinding("IsMouseOver"),
                Value = true,
            };
            hoverTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, _hoverForeground));
            style.Triggers.Add(hoverTrigger);

            var selectedTrigger = new DataTrigger
            {
                Binding = AncestorRowBinding("IsSelected"),
                Value = true,
            };
            selectedTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, _selectedForeground));
            style.Triggers.Add(selectedTrigger);

            return style;
        }

        private static Binding AncestorRowBinding(string property)
            => new Binding(property)
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1),
            };

        private static string ProviderModelLabel(AiAgent agent)
        {
            var provider = agent.Provider ?? string.Empty;
            var model = agent.Model ?? string.Empty;
            if (provider.Length == 0 && model.Length == 0) return "not configured";
            if (provider.Length == 0) return model;
            if (model.Length == 0) return provider;
            return provider + " · " + model;
        }

        /// <summary>
        /// The same paired-trigger shape as the dialog's search-results list (spec 036, FR-003):
        /// every background comes with a foreground. Hover is declared before selected, so a
        /// selected-and-hovered row keeps the selected pairing. Shared with the fallback-order
        /// list on the AI page (spec 037 US4).
        /// <para>
        /// The style MUST own the item template: the stock Aero2 <see cref="ListBoxItem"/>
        /// template paints its own ~24%-alpha highlight wash on selection and ignores the item's
        /// <see cref="Control.BackgroundProperty"/> — our SelectedText white then sat on a
        /// near-white wash (invisible selected row, the user-reported bug). The Border template
        /// below paints the trigger-set Background directly, so the selected row is the solid
        /// accent surface the foreground was chosen for.
        /// </para>
        /// </summary>
        internal static Style BuildItemStyle(PageTheme theme)
        {
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.TemplateProperty, BuildItemTemplate()));
            style.Setters.Add(new Setter(Control.BackgroundProperty, theme.Transparent));
            style.Setters.Add(new Setter(Control.ForegroundProperty, theme.FgPrimary));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
            style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, theme.TreeHover));
            hoverTrigger.Setters.Add(new Setter(Control.ForegroundProperty, theme.FgPrimary));
            style.Triggers.Add(hoverTrigger);

            var selectedTrigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, theme.Selected));
            selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, theme.SelectedText));
            selectedTrigger.Setters.Add(new Setter(TextElement.ForegroundProperty, theme.SelectedText));
            style.Triggers.Add(selectedTrigger);

            return style;
        }

        /// <summary>
        /// Minimal Border+ContentPresenter item template: paints the item's (trigger-set)
        /// Background instead of the stock Aero2 selection wash. See <see cref="BuildItemStyle"/>.
        /// Internal so other options lists (e.g. the settings search results) share it.
        /// </summary>
        internal static ControlTemplate BuildItemTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty,
                new TemplateBindingExtension(ItemsControl.HorizontalContentAlignmentProperty));
            content.SetValue(FrameworkElement.VerticalAlignmentProperty,
                new TemplateBindingExtension(ItemsControl.VerticalContentAlignmentProperty));
            border.AppendChild(content);

            return new ControlTemplate(typeof(ListBoxItem)) { VisualTree = border };
        }

        internal static Button MakeButton(string content, string automationName)
        {
            var button = new Button
            {
                Content = content,
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(0, 0, 8, 0),
            };
            AutomationProperties.SetName(button, automationName);
            return button;
        }
    }
}
