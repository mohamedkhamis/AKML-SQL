#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Dialogs;
using AkmlSql.Shell.Shared.Dialogs.Pages;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 037 (US4) T068 — the "Feature assignments" group on the AI Assistance page: seven
    /// dropdowns (FR-046/FR-047), each offering "Use active agent" plus every agent by name,
    /// round-tripping through save and load — plus the fallback-order editor (T075) persisted
    /// into the working copy. Built through
    /// <see cref="SettingsWindow.TestBuildWindowForRenderTest()"/>, the same idiom as
    /// <c>AiAgentListPageTests</c>.
    /// </summary>
    public class AiFeatureAssignmentPageTests
    {
        private static readonly AiFeature[] AllFeatures =
        {
            AiFeature.Chat, AiFeature.TextToSql, AiFeature.Explain, AiFeature.Fix,
            AiFeature.Optimize, AiFeature.IndexSuggestions, AiFeature.GhostText,
        };

        // ── The seven dropdowns ────────────────────────────────────────────

        [StaFact]
        public void Seven_dropdowns_offer_use_active_agent_plus_every_agent_by_name()
        {
            var (_, controls) = BuildPage(SettingsWith(MakeAgent("Claude (work)"), MakeAgent("Kimi")));

            foreach (var feature in AllFeatures)
            {
                var combo = controls.AssignmentComboFor(feature);
                Assert.Equal(
                    new[] { "Use active agent", "Claude (work)", "Kimi" },
                    Items(combo));
            }
        }

        [StaFact]
        public void Use_active_agent_is_the_default_for_every_feature()
        {
            var (_, controls) = BuildPage(SettingsWith(MakeAgent("Claude (work)"), MakeAgent("Kimi")));

            foreach (var feature in AllFeatures)
            {
                Assert.Equal("Use active agent",
                    controls.AssignmentComboFor(feature).SelectedItem);
            }
        }

        [StaFact]
        public void Assignments_round_trip_through_save_and_load()
        {
            var claude = MakeAgent("Claude (work)");
            var kimi = MakeAgent("Kimi");
            var (dialog, controls) = BuildPage(SettingsWith(claude, kimi));

            controls.AssignmentComboFor(AiFeature.Chat).SelectedItem = "Kimi";
            controls.AssignmentComboFor(AiFeature.GhostText).SelectedItem = "Claude (work)";

            var saved = dialog.GetSettings();
            Assert.Equal(kimi.Id, saved.Ai.FeatureAgents.Chat);          // by ID, never by name (V5)
            Assert.Equal(claude.Id, saved.Ai.FeatureAgents.GhostText);
            Assert.Equal(string.Empty, saved.Ai.FeatureAgents.Explain);  // untouched stays "follow active"

            // …and back: a fresh dialog on the saved settings shows the same selections.
            var (_, reloaded) = BuildPage(saved);
            Assert.Equal("Kimi", reloaded.AssignmentComboFor(AiFeature.Chat).SelectedItem);
            Assert.Equal("Claude (work)", reloaded.AssignmentComboFor(AiFeature.GhostText).SelectedItem);
            Assert.Equal("Use active agent", reloaded.AssignmentComboFor(AiFeature.Explain).SelectedItem);
        }

        [StaFact]
        public void Selecting_use_active_agent_clears_the_assignment_back_to_empty()
        {
            var kimi = MakeAgent("Kimi");
            var (dialog, controls) = BuildPage(SettingsWith(MakeAgent("Claude (work)"), kimi));

            controls.AssignmentComboFor(AiFeature.Fix).SelectedItem = "Kimi";
            controls.AssignmentComboFor(AiFeature.Fix).SelectedItem = "Use active agent";

            Assert.Equal(string.Empty, dialog.GetSettings().Ai.FeatureAgents.Fix);
        }

        [StaFact]
        public void Removing_an_agent_returns_its_assignments_to_use_active_agent()
        {
            var claude = MakeAgent("Claude (work)");
            var kimi = MakeAgent("Kimi");
            var (dialog, controls) = BuildPage(SettingsWith(claude, kimi));
            controls.AssignmentComboFor(AiFeature.Optimize).SelectedItem = "Kimi";

            // Remove Kimi through the real button path (V16 repair already lives in the page).
            controls.ListView.List.SelectedIndex = 1;
            controls.RemoveConfirmation = _ => true;
            Click(controls.ListView.RemoveButton);

            Assert.Equal("Use active agent",
                controls.AssignmentComboFor(AiFeature.Optimize).SelectedItem);
            Assert.Equal(string.Empty, dialog.GetSettings().Ai.FeatureAgents.Optimize);
        }

        // ── The fallback-order editor (T075) ───────────────────────────────

        [StaFact]
        public void Fallback_order_round_trips_through_save_and_load()
        {
            var claude = MakeAgent("Claude (work)");
            var kimi = MakeAgent("Kimi");
            var local = MakeAgent("Local", provider: "ollama", model: "llama3.1");
            var (dialog, controls) = BuildPage(SettingsWith(claude, kimi, local));

            AddFallback(controls, "Kimi");
            AddFallback(controls, "Local");

            var saved = dialog.GetSettings();
            Assert.Equal(new[] { kimi.Id, local.Id }, saved.Ai.FallbackOrder);   // order is the user's

            var (_, reloaded) = BuildPage(saved);
            Assert.Equal(new[] { "Kimi", "Local" }, FallbackNames(reloaded));
        }

        [StaFact]
        public void Fallback_editor_moves_and_removes_entries()
        {
            var (dialog, controls) = BuildPage(SettingsWith(
                MakeAgent("Claude (work)"), MakeAgent("Kimi"), MakeAgent("Local", provider: "ollama", model: "llama3.1")));
            AddFallback(controls, "Claude (work)");
            AddFallback(controls, "Kimi");
            AddFallback(controls, "Local");

            // Move "Kimi" above "Claude (work)".
            controls.FallbackList.SelectedIndex = 1;
            Click(controls.FallbackMoveUpButton);
            Assert.Equal(new[] { "Kimi", "Claude (work)", "Local" }, FallbackNames(controls));
            Assert.Equal("Kimi", ((ListBoxItem)controls.FallbackList.SelectedItem).Content);

            // Remove "Kimi".
            controls.FallbackList.SelectedIndex = 0;
            Click(controls.FallbackRemoveButton);
            Assert.Equal(new[] { "Claude (work)", "Local" }, FallbackNames(controls));

            var saved = dialog.GetSettings();
            Assert.Equal(2, saved.Ai.FallbackOrder.Count);
        }

        [StaFact]
        public void The_candidate_picker_offers_only_agents_not_already_in_the_chain()
        {
            var (_, controls) = BuildPage(SettingsWith(MakeAgent("Claude (work)"), MakeAgent("Kimi")));

            AddFallback(controls, "Kimi");

            Assert.Equal(new[] { "Claude (work)" }, Items(controls.FallbackCandidate));
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static AiAgent MakeAgent(string name, string provider = "anthropic",
            string model = "claude-sonnet-4-6")
        {
            return new AiAgent
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Provider = provider,
                Model = model,
                ApiKey = provider == "anthropic" ? ApiKeyProtector.Protect("sk-test") : string.Empty,
                Enabled = true,
                CreatedUtc = DateTime.UtcNow.ToString("O"),
            };
        }

        private static AppSettings SettingsWith(params AiAgent[] agents)
        {
            var settings = new AppSettings();
            foreach (var agent in agents) settings.Ai.Agents.Add(agent);
            settings.Ai.ActiveAgentId = agents.Length > 0 ? agents[0].Id : string.Empty;
            return settings;
        }

        private static (SettingsWindow Dialog, AiAssistanceControls Controls) BuildPage(AppSettings settings)
        {
            var dialog = new SettingsWindow(settings);
            _ = dialog.TestBuildWindowForRenderTest();
            var f = typeof(SettingsWindow).GetField("_pageControlsByKey",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(f);
            var pages = (Dictionary<string, IPageControls>)f!.GetValue(dialog)!;
            Assert.True(pages.TryGetValue("AI Assistance", out var controls), "AI Assistance controls not found.");
            return (dialog, (AiAssistanceControls)controls!);
        }

        private static void AddFallback(AiAssistanceControls controls, string agentName)
        {
            controls.FallbackCandidate.SelectedItem = agentName;
            Click(controls.FallbackAddButton);
        }

        private static List<string> FallbackNames(AiAssistanceControls controls)
        {
            var names = new List<string>();
            foreach (ListBoxItem item in controls.FallbackList.Items)
                names.Add((string)item.Content);
            return names;
        }

        private static List<string> Items(ComboBox combo)
        {
            var items = new List<string>();
            foreach (var item in combo.Items) items.Add((string)item);
            return items;
        }

        private static void Click(Button button)
            => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }
}
