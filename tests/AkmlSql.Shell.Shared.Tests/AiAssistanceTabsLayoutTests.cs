#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Ai;
using AkmlSql.Shell.Shared.Dialogs;
using AkmlSql.Shell.Shared.Dialogs.Pages;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// The 2026-10-13 AI assistance page layout (design B of the local demo): three in-page tabs
    /// — Agents, Features, Privacy &amp; data — a fitted Selected agent form whose Test connection
    /// box sits AFTER the four fields it tests, a masked API key, and request parameters shared
    /// by every agent on the Privacy &amp; data tab. Built through
    /// <see cref="SettingsWindow.TestBuildWindowForRenderTest()"/> like the other AI page tests.
    /// </summary>
    public class AiAssistanceTabsLayoutTests
    {
        // ── Tabs ─────────────────────────────────────────────────────────────

        [StaFact]
        public void Page_opens_on_the_agents_tab_with_the_others_collapsed()
        {
            var (_, controls) = BuildPage(SettingsWith(MakeAgent("Claude")));

            Assert.Equal(3, controls.Tabs.Panels.Count);
            Assert.Equal(0, controls.Tabs.SelectedIndex);
            Assert.Equal(Visibility.Visible, controls.Tabs.Panels[0].Visibility);
            Assert.Equal(Visibility.Collapsed, controls.Tabs.Panels[1].Visibility);
            Assert.Equal(Visibility.Collapsed, controls.Tabs.Panels[2].Visibility);
        }

        [StaFact]
        public void Selecting_a_tab_shows_only_that_tab()
        {
            var (_, controls) = BuildPage(SettingsWith(MakeAgent("Claude")));

            controls.Tabs.Select(1);
            Assert.Equal(1, controls.Tabs.SelectedIndex);
            Assert.Equal(Visibility.Collapsed, controls.Tabs.Panels[0].Visibility);
            Assert.Equal(Visibility.Visible, controls.Tabs.Panels[1].Visibility);
            Assert.Equal(Visibility.Collapsed, controls.Tabs.Panels[2].Visibility);

            Click(controls.Tabs.Buttons[2]);   // the strip's own buttons drive the same switch
            Assert.Equal(2, controls.Tabs.SelectedIndex);
            Assert.Equal(Visibility.Visible, controls.Tabs.Panels[2].Visibility);
            Assert.Equal(Visibility.Collapsed, controls.Tabs.Panels[1].Visibility);
        }

        [StaFact]
        public void Bringing_a_row_on_a_hidden_tab_into_view_switches_to_its_tab()
        {
            // The Options search box and the Command Palette reveal a row with BringIntoView; a
            // row on a collapsed tab must first make its tab the selected one.
            var (_, controls) = BuildPage(SettingsWith(MakeAgent("Claude")), "AI Assistance");
            Assert.Equal(0, controls.Tabs.SelectedIndex);

            GetField<ComboBox>(controls, "_privacy").BringIntoView();

            Assert.Equal(2, controls.Tabs.SelectedIndex);
            Assert.Equal(Visibility.Visible, controls.Tabs.Panels[2].Visibility);
        }

        // ── The Agents tab ───────────────────────────────────────────────────

        [StaFact]
        public void The_agents_tab_holds_list_fields_and_test_connection_in_reading_order()
        {
            var (_, controls) = BuildPage(SettingsWith(MakeAgent("Claude")));
            var order = LogicalTree.Descendants<FrameworkElement>(controls.Tabs.Panels[0]).ToList();
            int At(FrameworkElement e)
            {
                var i = order.IndexOf(e);
                Assert.True(i >= 0, "element is not on the Agents tab");
                return i;
            }

            Assert.True(At(controls.ListView.List) < At(GetField<TextBox>(controls, "_name")));
            Assert.True(At(GetField<TextBox>(controls, "_apiKey")) < At(GetField<TextBox>(controls, "_endpoint")));
            // Test connection comes AFTER the endpoint it tests — the point of the redesign.
            Assert.True(At(GetField<TextBox>(controls, "_endpoint")) < At(GetField<Button>(controls, "_testButton")));
        }

        [StaFact]
        public void The_agent_and_fallback_lists_never_scroll_sideways()
        {
            // A ListBox measures its rows at infinite width by default, so at the dialog's 720 px
            // minimum the agent row grew past the list and a horizontal scrollbar cut the badge
            // off. The rows must compress to the list's width instead.
            var (_, controls) = BuildPage(SettingsWith(MakeAgent("Claude")));

            Assert.Equal(ScrollBarVisibility.Disabled, ScrollViewer.GetHorizontalScrollBarVisibility(controls.ListView.List));
            Assert.Equal(ScrollBarVisibility.Disabled, ScrollViewer.GetHorizontalScrollBarVisibility(controls.FallbackList));
        }

        [StaFact]
        public void The_key_help_is_collapsed_until_its_link_is_clicked()
        {
            var (_, controls) = BuildPage(SettingsWith(MakeAgent("Claude")));
            Assert.Equal(Visibility.Collapsed, controls.KeyHelp.Visibility);

            Click(controls.KeyHelpLink);
            Assert.Equal(Visibility.Visible, controls.KeyHelp.Visibility);

            Click(controls.KeyHelpLink);
            Assert.Equal(Visibility.Collapsed, controls.KeyHelp.Visibility);
        }

        [StaFact]
        public void The_api_key_is_masked_until_shown()
        {
            var (_, controls) = BuildPage(SettingsWith(MakeAgent("Claude")));
            var apiKey = GetField<TextBox>(controls, "_apiKey");

            Assert.Equal(Visibility.Visible, controls.KeyMask.Visibility);
            Assert.Equal(Visibility.Collapsed, apiKey.Visibility);

            Click(controls.ShowKeyButton);
            Assert.Equal(Visibility.Collapsed, controls.KeyMask.Visibility);
            Assert.Equal(Visibility.Visible, apiKey.Visibility);

            Click(controls.ShowKeyButton);
            Assert.Equal(Visibility.Visible, controls.KeyMask.Visibility);
            Assert.Equal(Visibility.Collapsed, apiKey.Visibility);
        }

        [StaFact]
        public void The_masked_box_and_the_text_box_hold_the_same_key()
        {
            // Every existing flow (Load, Save, Test connection, the key-protection rules) reads
            // and writes the TextBox; the PasswordBox is only its masked face.
            var (_, controls) = BuildPage(SettingsWith(MakeAgent("Claude", apiKey: "sk-test")));
            var apiKey = GetField<TextBox>(controls, "_apiKey");
            Assert.Equal("sk-test", apiKey.Text);
            Assert.Equal("sk-test", controls.KeyMask.Password);

            controls.KeyMask.Password = "sk-typed";
            Assert.Equal("sk-typed", apiKey.Text);

            apiKey.Text = "sk-bound";
            Assert.Equal("sk-bound", controls.KeyMask.Password);
        }

        [StaFact]
        public async Task The_test_result_colour_follows_the_theme()
        {
            // The list badges already pick their semantic brush per theme; the test outcome line
            // must do the same (the light-theme green read at ~2.4:1 on the dark panel).
            var settings = SettingsWith(MakeAgent("Azure", provider: "azure", model: "gpt-4o", endpoint: ""));
            settings.Theme = "dark";
            var (_, controls) = BuildPage(settings);

            await controls.RunProviderTestAsync();   // refused locally: Azure needs an endpoint

            Assert.Contains("Endpoint is required", GetField<TextBlock>(controls, "_testResult").Text);
            Assert.Same(AiAgentListView.BadgeBrushFor(AgentHealthStatus.Failed, dark: true),
                GetField<TextBlock>(controls, "_testResult").Foreground);
        }

        // ── The Features tab ─────────────────────────────────────────────────

        [StaFact]
        public void A_feature_toggle_and_its_agent_dropdown_share_one_row()
        {
            var (_, controls) = BuildPage(SettingsWith(MakeAgent("Claude")));
            var toggle = GetField<CheckBox>(controls, "_textToSql");
            var combo = controls.AssignmentComboFor(AiFeature.TextToSql);

            Assert.Contains(toggle, LogicalTree.Descendants<CheckBox>(controls.Tabs.Panels[1]));
            Assert.Same(RowOf(toggle), RowOf(combo));

            // The label column is what gives way at the window's 720 px minimum: the feature
            // name trims with an ellipsis and keeps its full text as a tooltip.
            var label = Assert.Single(LogicalTree.Descendants<TextBlock>(RowOf(toggle)), t => t.Text == "Natural language to SQL");
            Assert.Equal(TextTrimming.CharacterEllipsis, label.TextTrimming);
            Assert.Equal("Natural language to SQL", label.ToolTip);
        }

        // ── The Privacy & data tab ───────────────────────────────────────────

        [StaFact]
        public void Request_parameters_live_on_the_privacy_tab()
        {
            var (_, controls) = BuildPage(SettingsWith(MakeAgent("Claude")));
            var timeout = GetField<TextBox>(controls, "_timeout");

            Assert.Contains(timeout, LogicalTree.Descendants<TextBox>(controls.Tabs.Panels[2]));
            Assert.DoesNotContain(timeout, LogicalTree.Descendants<TextBox>(controls.Tabs.Panels[0]));
        }

        [StaFact]
        public void Request_parameters_are_shared_by_every_agent_on_save()
        {
            var a = MakeAgent("A");
            a.Timeout = 30;
            var b = MakeAgent("B");
            b.Timeout = 60;
            var settings = SettingsWith(a, b);
            settings.Ai.Timeout = 30;   // the flat mirror follows the active agent (a)
            var (dialog, controls) = BuildPage(settings);
            Assert.Equal("30", GetField<TextBox>(controls, "_timeout").Text);

            GetField<TextBox>(controls, "_timeout").Text = "90";
            var saved = dialog.GetSettings();

            Assert.Equal(2, saved.Ai.Agents.Count);
            Assert.All(saved.Ai.Agents, agent => Assert.Equal(90, agent.Timeout));
            Assert.Equal(90, saved.Ai.Timeout);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static Border RowOf(DependencyObject element)
        {
            var node = LogicalTreeHelper.GetParent(element);
            while (node != null && node is not Border)
                node = LogicalTreeHelper.GetParent(node);
            Assert.True(node != null, "no Border row wraps the element");
            return (Border)node!;
        }

        private static AiAgent MakeAgent(string name, string provider = "anthropic",
            string model = "claude-sonnet-4-6", string apiKey = "sk-test", string endpoint = "")
        {
            return new AiAgent
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Provider = provider,
                Model = model,
                ApiKey = apiKey.Length == 0 ? string.Empty : ApiKeyProtector.Protect(apiKey),
                Endpoint = endpoint,
                Enabled = true,
                CreatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            };
        }

        private static AppSettings SettingsWith(params AiAgent[] agents)
        {
            var settings = new AppSettings();
            foreach (var agent in agents) settings.Ai.Agents.Add(agent);
            settings.Ai.ActiveAgentId = agents.Length > 0 ? agents[0].Id : string.Empty;
            return settings;
        }

        private static (SettingsWindow Dialog, AiAssistanceControls Controls) BuildPage(AppSettings settings, string? initialPageKey = null)
        {
            var dialog = new SettingsWindow(settings);
            _ = initialPageKey == null
                ? dialog.TestBuildWindowForRenderTest()
                : dialog.TestBuildWindowForRenderTest(initialPageKey);
            var f = typeof(SettingsWindow).GetField("_pageControlsByKey",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(f);
            var pages = (Dictionary<string, IPageControls>)f!.GetValue(dialog)!;
            Assert.True(pages.TryGetValue("AI Assistance", out var controls), "AI Assistance controls not found.");
            return (dialog, (AiAssistanceControls)controls!);
        }

        private static void Click(Button button)
            => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        private static T GetField<T>(object instance, string name) where T : class
        {
            var f = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.True(f != null, $"field {name} not found on {instance.GetType().Name}");
            var value = f!.GetValue(instance) as T;
            Assert.True(value != null, $"field {name} is null or not a {typeof(T).Name}");
            return value!;
        }
    }
}
