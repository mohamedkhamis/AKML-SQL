#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
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
    /// Spec 037 review findings — the AI Assistance page's Save semantics: <c>enabled</c> is
    /// derived from the whole working copy (V19 on the write path), never from whichever agent
    /// is selected in the editor; a still-blank Load-time seed (or any untouched Add) is a
    /// placeholder that never persists, with the active id, feature assignments and fallback
    /// order repaired exactly as Remove repairs them; and the write-back deep-copies, so edits
    /// made after an Apply cannot mutate the settings object the host already saved.
    ///
    /// <para>The dialog is built through <see cref="SettingsWindow.TestBuildWindowForRenderTest()"/>
    /// and saved through <see cref="SettingsWindow.GetSettings()"/>, the
    /// <c>AiAgentListPageTests</c> idiom.</para>
    /// </summary>
    public class AiAssistanceSaveTests
    {
        // ── V19 on the write path (finding 1) ────────────────────────────────

        [StaFact]
        public void Save_with_a_usable_agent_present_but_a_provider_less_agent_selected_persists_enabled_true()
        {
            var usable = MakeAgent("Claude (work)");
            // Non-blank (a model was typed) but provider-less — it survives the blank drop
            // while contributing nothing to usability.
            var providerLess = MakeAgent("Draft", provider: "", model: "claude-sonnet-4-6", apiKey: "");
            var (dialog, controls) = BuildPage(SettingsWith(usable, providerLess));

            SelectAgentInList(controls, 1);   // the editor shows the provider-less agent
            var saved = dialog.GetSettings();

            // The old key — "the selected editor row has a provider" — would have written false.
            Assert.True(saved.Ai.Enabled);
        }

        [StaFact]
        public void Save_with_zero_usable_agents_persists_enabled_false()
        {
            var disabled = MakeAgent("Off");
            disabled.Enabled = false;
            var (dialog, controls) = BuildPage(SettingsWith(disabled));

            var saved = dialog.GetSettings();

            Assert.False(saved.Ai.Enabled);
            Assert.Single(saved.Ai.Agents);   // disabled is a configuration, not a blank seed
        }

        // ── The blank seed never persists (finding 2) ────────────────────────

        [StaFact]
        public void Ok_with_an_untouched_seed_persists_zero_agents()
        {
            var (dialog, controls) = BuildPage(new AppSettings());
            Assert.Single(controls.WorkingAgents);   // the Load-time seed

            var saved = dialog.GetSettings();

            Assert.Empty(saved.Ai.Agents);
            Assert.Equal(string.Empty, saved.Ai.ActiveAgentId);
            Assert.False(saved.Ai.Enabled);
            Assert.Empty(controls.WorkingAgents);   // the editor's working copy dropped it too

            // …and the chat empty state afterwards still reads as "no agents" (FR-021) —
            // there is no phantom agent to blame or to deep-link.
            var (text, offender) = AiChatEmptyState.DetectReason(saved.Ai);
            Assert.Equal(AiChatEmptyState.NoAgentsText, text);
            Assert.Equal(string.Empty, offender);
        }

        [StaFact]
        public void A_seed_the_user_typed_a_provider_into_persists()
        {
            var (dialog, controls) = BuildPage(new AppSettings());

            ProviderCombo(controls).SelectedIndex = 1;   // Anthropic — the seed is no longer blank
            var saved = dialog.GetSettings();

            var agent = Assert.Single(saved.Ai.Agents);
            Assert.Equal("anthropic", agent.Provider);
            Assert.False(saved.Ai.Enabled);   // still no key — but the configuration is real
        }

        [StaFact]
        public void Dropping_the_seed_repairs_the_active_id_assignments_and_fallback_order()
        {
            var (dialog, controls) = BuildPage(new AppSettings());
            var seedId = controls.WorkingAgents[0].Id;

            // The user pinned the seed to chat and to the fallback order, then OK'd without
            // ever configuring it.
            controls.AssignmentComboFor(AiFeature.Chat).SelectedIndex = 1;
            controls.FallbackCandidate.SelectedIndex = 0;
            Click(controls.FallbackAddButton);
            Assert.Equal(seedId, controls.ActiveAgentId);   // the seed load made it active

            var saved = dialog.GetSettings();

            Assert.Empty(saved.Ai.Agents);
            Assert.Equal(string.Empty, saved.Ai.FeatureAgents.Chat);      // V16-shaped repair
            Assert.DoesNotContain(seedId, saved.Ai.FallbackOrder);        // V17-shaped repair
            Assert.Equal(string.Empty, saved.Ai.ActiveAgentId);           // V13-shaped repair
        }

        [StaFact]
        public void An_agent_with_only_request_parameters_touched_is_kept()
        {
            // The blank-drop is one notch stricter than validation's blank exemption: moving a
            // slider is a real (if incomplete) configuration and must survive the save.
            var (dialog, controls) = BuildPage(new AppSettings());
            TimeoutSlider(controls).Value = 90;

            var saved = dialog.GetSettings();

            var agent = Assert.Single(saved.Ai.Agents);
            Assert.Equal(90, agent.Timeout);
        }

        // ── No aliasing with the saved settings (finding 5c) ─────────────────

        [StaFact]
        public void Edits_after_a_save_do_not_reach_the_saved_settings_object()
        {
            var (dialog, controls) = BuildPage(SettingsWith(MakeAgent("A")));
            var saved = dialog.GetSettings();

            ModelBox(controls).Text = "claude-mutated";
            Assert.Null(controls.TryAddAgent());   // any commit moment — here, before a CRUD action

            // The page once assigned its working lists into the settings object BY REFERENCE,
            // so this edit would have mutated what the host already persisted (an Apply leaves
            // the dialog open). The write-back now deep-copies.
            Assert.Equal("claude-sonnet-4-6", saved.Ai.Agents[0].Model);
            Assert.Equal("claude-mutated", controls.WorkingAgents[0].Model);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

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

        private static void SelectAgentInList(AiAssistanceControls controls, int index)
        {
            var list = controls.ListView.List;
            Assert.True(index < list.Items.Count,
                $"agent list holds {list.Items.Count} rows; cannot select index {index}");
            list.SelectedIndex = index;
        }

        private static void Click(Button button)
            => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        private static TextBox ModelBox(AiAssistanceControls controls) => GetField<TextBox>(controls, "_model");
        private static ComboBox ProviderCombo(AiAssistanceControls controls) => GetField<ComboBox>(controls, "_provider");
        private static Slider TimeoutSlider(AiAssistanceControls controls) => GetField<Slider>(controls, "_timeout");

        private static T GetField<T>(object instance, string name) where T : class
        {
            var f = instance.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.True(f != null, $"field {name} not found on {instance.GetType().Name}");
            var value = f!.GetValue(instance) as T;
            Assert.NotNull(value);
            return value!;
        }
    }
}
