#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Ui.Theme;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Spec 037 (US2, T038, research R12), reshaped by the 2026-11 master–detail redesign (SSMS
    /// layout B): the AI assistance page's agent grid — a toolbar (Add…, Edit…, Duplicate,
    /// Delete, Set as active, Test and the agent count), column captions (Name, Provider, Model,
    /// Status) and one <see cref="ListBox"/> row per agent: active marker │ name │ provider │
    /// model │ health badge. Double-click or Enter asks for Edit, the Delete key for Delete; an
    /// empty list shows how to start. Every CRUD decision stays the page's.
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
        // Column widths shared by the captions and every row, so they line up.
        private const double MarkerWidth = 18;
        private const double ProviderWidth = 110;
        private const double StatusWidth = 150;

        internal ListBox List { get; }
        internal Button AddButton { get; }
        internal Button EditButton { get; }
        internal Button DuplicateButton { get; }
        internal Button DeleteButton { get; }
        internal Button SetActiveButton { get; }
        internal Button TestButton { get; }
        internal TextBlock CountText { get; }
        internal TextBlock EmptyText { get; }

        /// <summary>The toolbar row, for the host to place.</summary>
        internal FrameworkElement ButtonRow => _buttonRow;

        /// <summary>The captions, the list and the empty-state line, as one block.</summary>
        internal FrameworkElement GridBlock => _grid;

        /// <summary>Double-click or Enter on a row.</summary>
        internal event EventHandler? EditRequested;

        /// <summary>The Delete key on a row.</summary>
        internal event EventHandler? DeleteRequested;

        private readonly DockPanel _buttonRow;
        private readonly Grid _header;
        private readonly Grid _grid;
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
                Height = 196,   // seven rows without a scrollbar
                BorderThickness = new Thickness(1),
                BorderBrush = theme.ComboBorder,
                Background = theme.Input,
                FontSize = 13,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            AutomationProperties.SetName(List, "AI agents");
            // Rows compress to the list's width (name and model trim) instead of growing past it
            // behind a horizontal scrollbar — at the dialog's 720 px minimum the badge was cut off.
            ScrollViewer.SetHorizontalScrollBarVisibility(List, ScrollBarVisibility.Disabled);
            List.ItemContainerStyle = BuildItemStyle(theme);
            List.MouseDoubleClick += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject source && FindRow(source) != null && List.SelectedItem != null)
                {
                    EditRequested?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                }
            };
            List.KeyDown += (_, e) =>
            {
                if (List.SelectedItem == null) return;
                if (e.Key == Key.Enter)
                {
                    EditRequested?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                }
                else if (e.Key == Key.Delete)
                {
                    DeleteRequested?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                }
            };

            AddButton = MakeButton("Add…", "Add an AI agent", theme);
            EditButton = MakeButton("Edit…", "Edit the selected AI agent", theme);
            DuplicateButton = MakeButton("Duplicate", "Duplicate the selected agent, including its key", theme);
            DeleteButton = MakeButton("Delete", "Delete the selected AI agent", theme);
            SetActiveButton = MakeButton("Set as active", "Make the selected agent the active agent", theme);
            TestButton = MakeButton("Test", "Test the selected agent's connection", theme);

            CountText = new TextBlock
            {
                FontSize = 11.5,
                Foreground = theme.FgSecondary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(AddButton);
            actions.Children.Add(EditButton);
            actions.Children.Add(DuplicateButton);
            actions.Children.Add(DeleteButton);
            actions.Children.Add(new Border
            {
                Width = 1,
                Margin = new Thickness(2, 3, 10, 3),
                Background = theme.Sep,
            });
            actions.Children.Add(SetActiveButton);
            actions.Children.Add(TestButton);

            _buttonRow = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(actions, Dock.Left);
            _buttonRow.Children.Add(actions);
            DockPanel.SetDock(CountText, Dock.Right);
            _buttonRow.Children.Add(CountText);

            // Column captions over a hairline, padded like a row (border 1 + item padding 8).
            _header = new Grid { Margin = new Thickness(9, 0, 9, 0) };
            AddColumns(_header);
            var captions = new[] { "", "Name", "Provider", "Model", "Status" };
            for (var i = 0; i < captions.Length; i++)
            {
                var caption = new TextBlock { Text = captions[i], FontSize = 11, Foreground = theme.FgSecondary };
                Grid.SetColumn(caption, i);
                _header.Children.Add(caption);
            }
            var headerBorder = new Border
            {
                BorderBrush = theme.Sep,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 0, 0, 5),
                Margin = new Thickness(0, 0, 0, 3),
                Child = _header,
            };
            // A vertical scrollbar narrows the rows: the captions follow so the columns stay aligned.
            List.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) =>
            {
                if (e.OriginalSource is ScrollViewer sv)
                {
                    var bar = sv.ComputedVerticalScrollBarVisibility == Visibility.Visible ? SystemParameters.VerticalScrollBarWidth : 0;
                    _header.Margin = new Thickness(9, 0, 9 + bar, 0);
                }
            }));

            EmptyText = new TextBlock
            {
                Text = "No AI agents yet. Click Add… to connect Anthropic, OpenAI, Gemini, Kimi, Ollama, LM Studio or a custom endpoint.",
                FontSize = 12,
                Foreground = theme.FgSecondary,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(12, 10, 12, 10),
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
            };

            _grid = new Grid();
            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _grid.Children.Add(headerBorder);
            Grid.SetRow(List, 1);
            _grid.Children.Add(List);
            Grid.SetRow(EmptyText, 1);
            _grid.Children.Add(EmptyText);
        }

        private static void AddColumns(Grid grid)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MarkerWidth) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ProviderWidth) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(StatusWidth) });
        }

        private static ListBoxItem? FindRow(DependencyObject source)
        {
            var node = source;
            while (node != null && node is not ListBoxItem)
                node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
            return node as ListBoxItem;
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
                AutomationProperties.SetName(item, "Agent " + (agent.Name ?? string.Empty) + (agent.Enabled ? string.Empty : " (off)"));
                if (!agent.Enabled) item.Opacity = 0.6;
                List.Items.Add(item);
                if (string.Equals(agent.Id, selectedId, StringComparison.Ordinal))
                    toSelect = item;
            }
            List.SelectedItem = toSelect;
            var count = List.Items.Count;
            CountText.Text = count.ToString(CultureInfo.InvariantCulture) + " of " +
                             AiAgentResolver.MaxAgents.ToString(CultureInfo.InvariantCulture) + " agents";
            EmptyText.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
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

        // active marker │ name │ provider │ model │ health badge — the caption columns. No local
        // Foreground anywhere EXCEPT the badge: the item container's inherited foreground must
        // reach every other run so selection stays legible (spec 036); the badge's style flips
        // its semantic colour on hover/selection to the same effect.
        private UIElement BuildRow(AiAgent agent, string activeId)
        {
            var grid = new Grid();
            AddColumns(grid);
            var isActive = string.Equals(agent.Id, activeId, StringComparison.Ordinal);

            var marker = new TextBlock
            {
                Text = isActive ? "●" : string.Empty,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = isActive ? "The active agent" : null,
            };
            grid.Children.Add(marker);

            var nameCell = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };
            nameCell.Children.Add(new TextBlock
            {
                Text = agent.Name ?? string.Empty,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (!agent.Enabled)
            {
                nameCell.Children.Add(new TextBlock
                {
                    Text = "Off",
                    FontSize = 10.5,
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "This agent is turned off",
                });
            }
            Grid.SetColumn(nameCell, 1);
            grid.Children.Add(nameCell);

            var provider = new TextBlock
            {
                Text = ProviderLabel(agent),
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            Grid.SetColumn(provider, 2);
            grid.Children.Add(provider);

            var model = new TextBlock
            {
                Text = string.IsNullOrEmpty(agent.Model) ? "—" : agent.Model,
                FontSize = 12,
                FontFamily = MonoFont,
                Opacity = 0.85,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = string.IsNullOrEmpty(agent.Model) ? null : agent.Model,
            };
            Grid.SetColumn(model, 3);
            grid.Children.Add(model);

            var badge = new TextBlock
            {
                Text = HealthBadgeTextFor(agent),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Style = BuildBadgeStyle(agent),
                ToolTip = string.IsNullOrEmpty(agent.Health?.Message) ? null : agent.Health!.Message,
            };
            Grid.SetColumn(badge, 4);
            grid.Children.Add(badge);

            return grid;
        }

        private static readonly FontFamily MonoFont = new FontFamily("Consolas");

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

        /// <summary>The provider's display name ("Anthropic", "Kimi (Moonshot)"), or "Not set".</summary>
        internal static string ProviderLabel(AiAgent agent)
        {
            var id = AiProviderIds.Normalize(agent.Provider);
            if (id.Length == 0) return string.IsNullOrEmpty(agent.Provider) ? "Not set" : agent.Provider;
            foreach (var (display, providerId) in AiAssistancePage.Providers)
            {
                if (providerId == id) return display;
            }
            return agent.Provider ?? id;
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

        /// <summary>
        /// A list-action button. Spec 040 (OPT-09): themed from the Options window's
        /// <paramref name="theme"/> — the stock button was light chrome on the dark page.
        /// </summary>
        internal static Button MakeButton(string content, string automationName, PageTheme theme)
        {
            var button = new Button
            {
                Content = content,
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(0, 0, 8, 0),
            };
            ThemedButton.ApplySecondary(button, theme);
            AutomationProperties.SetName(button, automationName);
            return button;
        }
    }
}
