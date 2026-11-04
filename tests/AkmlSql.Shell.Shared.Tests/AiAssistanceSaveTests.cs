#nullable enable
using System;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Ai;
using Xunit;
using static AkmlSql.Shell.Shared.Tests.AiPageTestKit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 037 review findings — the AI Assistance page's Save semantics: <c>enabled</c> is
    /// derived from the whole working copy (V19 on the write path), never from the selected
    /// agent; an empty configuration persists no agent (the 2026-11 grid seeds no placeholder —
    /// it shows an empty state); flat fields with no agent list are kept as an agent; a blank
    /// agent from a hand-edited config is dropped with its references repaired; and the
    /// write-back deep-copies, so edits made after an Apply cannot mutate the saved object.
    /// </summary>
    public class AiAssistanceSaveTests
    {
        [StaFact]
        public void Save_with_a_usable_agent_present_but_a_provider_less_agent_selected_persists_enabled_true()
        {
            var usable = MakeAgent("Claude (work)");
            var providerLess = MakeAgent("Draft", provider: "", model: "claude-sonnet-4-6", apiKey: "");
            var (dialog, controls) = BuildPage(SettingsWith(usable, providerLess));

            Select(controls, 1);
            var saved = dialog.GetSettings();

            Assert.True(saved.Ai.Enabled);
        }

        [StaFact]
        public void Save_with_zero_usable_agents_persists_enabled_false()
        {
            var disabled = MakeAgent("Off");
            disabled.Enabled = false;
            var (dialog, _) = BuildPage(SettingsWith(disabled));

            var saved = dialog.GetSettings();

            Assert.False(saved.Ai.Enabled);
            Assert.Single(saved.Ai.Agents);   // disabled is a configuration, not a blank
        }

        [StaFact]
        public void Ok_on_an_empty_configuration_persists_zero_agents()
        {
            var (dialog, controls) = BuildPage(new AppSettings());
            Assert.Empty(controls.WorkingAgents);

            var saved = dialog.GetSettings();

            Assert.Empty(saved.Ai.Agents);
            Assert.Equal(string.Empty, saved.Ai.ActiveAgentId);
            Assert.False(saved.Ai.Enabled);

            // …and the chat empty state still reads as "no agents" (FR-021).
            var (text, offender) = AiChatEmptyState.DetectReason(saved.Ai);
            Assert.Equal(AiChatEmptyState.NoAgentsText, text);
            Assert.Equal(string.Empty, offender);
        }

        [StaFact]
        public void Flat_fields_without_an_agent_list_are_kept_as_an_agent()
        {
            var settings = new AppSettings();
            settings.Ai.Provider = "kimi";
            settings.Ai.Model = "kimi-latest";
            settings.Ai.ApiKey = ApiKeyProtector.Protect("sk-flat");
            var (dialog, controls) = BuildPage(settings);

            var seed = Assert.Single(controls.WorkingAgents);
            Assert.Equal("kimi", seed.Provider);
            Assert.Equal(seed.Id, controls.ActiveAgentId);

            var saved = dialog.GetSettings();
            Assert.Equal("kimi-latest", Assert.Single(saved.Ai.Agents).Model);
            Assert.True(saved.Ai.Enabled);
        }

        [StaFact]
        public void A_blank_agent_from_the_config_is_dropped_with_its_references_repaired()
        {
            var good = MakeAgent("Good");
            var blank = MakeAgent("Blank", provider: "", model: "", apiKey: "");
            var settings = SettingsWith(good, blank);
            settings.Ai.FeatureAgents.Chat = blank.Id;
            settings.Ai.FallbackOrder.Add(blank.Id);
            var (dialog, _) = BuildPage(settings);

            var saved = dialog.GetSettings();

            Assert.Single(saved.Ai.Agents);
            Assert.Equal(good.Id, saved.Ai.Agents[0].Id);
            Assert.Equal(string.Empty, saved.Ai.FeatureAgents.Chat);   // V16-shaped repair
            Assert.DoesNotContain(blank.Id, saved.Ai.FallbackOrder);    // V17-shaped repair
            Assert.Equal(good.Id, saved.Ai.ActiveAgentId);
        }

        [StaFact]
        public void The_shared_request_parameters_reach_an_agent_added_in_the_dialog()
        {
            var (dialog, controls) = BuildPage(new AppSettings());
            GetField<System.Windows.Controls.TextBox>(controls, "_timeout").Text = "90";
            AcceptNextEditor(controls, e => FillUsable(e, "Claude"));

            controls.TryAddAgent();
            var saved = dialog.GetSettings();

            Assert.Equal(90, Assert.Single(saved.Ai.Agents).Timeout);
        }

        [StaFact]
        public void Edits_after_a_save_do_not_reach_the_saved_settings_object()
        {
            var (dialog, controls) = BuildPage(SettingsWith(MakeAgent("A")));
            var saved = dialog.GetSettings();

            AcceptNextEditor(controls, e => e.ModelBox.Text = "claude-mutated");
            controls.TryEditSelectedAgent();

            // The page once assigned its working lists into the settings object BY REFERENCE,
            // so this edit would have mutated what the host already persisted (an Apply leaves
            // the dialog open). The write-back deep-copies.
            Assert.Equal("claude-sonnet-4-6", saved.Ai.Agents[0].Model);
            Assert.Equal("claude-mutated", controls.WorkingAgents[0].Model);
        }
    }
}
