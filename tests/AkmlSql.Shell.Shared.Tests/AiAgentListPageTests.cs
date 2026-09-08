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
    /// Spec 037 (US2) T033–T035 — the AI Assistance agent list: CRUD (FR-025 – FR-029), the
    /// working-copy contract (FR-030/FR-031, Cancel discards), and whole-copy validation
    /// (FR-032, V3, V12) plus rename stability (FR-005).
    ///
    /// The dialog is built through <see cref="SettingsWindow.TestBuildWindowForRenderTest()"/> and
    /// the page's controls are pulled from the host's page map, the same idiom as
    /// <c>AiProviderModelAutofillTests</c>; CRUD is driven through the page's internal seams (and
    /// the real buttons where no modal confirmation is involved).
    /// </summary>
    public class AiAgentListPageTests
    {
        // ── T033: CRUD ───────────────────────────────────────────────────────

        [StaFact]
        public void Add_creates_a_uniquely_named_agent_and_selects_it()
        {
            var (_, controls) = BuildPage(SettingsWith(MakeAgent("Agent 1")));

            Click(controls.ListView.AddButton);

            Assert.Equal(2, controls.WorkingAgents.Count);
            var added = controls.WorkingAgents[1];
            Assert.Equal("Agent 2", added.Name);
            Assert.Equal(32, added.Id.Length); // Guid "N" — R14
            Assert.Equal(added.Id, controls.SelectedAgentId);
            // The editor is bound to the new agent (FR-026).
            Assert.Equal("Agent 2", NameBox(controls).Text);
        }

        [StaTheory]
        [InlineData(true)]
        [InlineData(false)]
        public void The_21st_agent_is_refused_naming_the_limit(bool viaDuplicate)
        {
            var agents = new AiAgent[AiAgentResolver.MaxAgents];
            for (var i = 0; i < agents.Length; i++) agents[i] = MakeAgent("Agent " + (i + 1));
            var (_, controls) = BuildPage(SettingsWith(agents));

            var refusal = viaDuplicate ? controls.TryDuplicateSelectedAgent() : controls.TryAddAgent();

            Assert.NotNull(refusal);
            Assert.Contains("20", refusal);
            Assert.Equal(AiAgentResolver.MaxAgents, controls.WorkingAgents.Count);
        }

        [StaFact]
        public void Duplicate_copies_every_setting_including_the_key_and_renames()
        {
            var source = MakeAgent("Claude (work)", provider: "anthropic", model: "claude-opus-4-8", apiKey: "sk-work");
            source.MaxTokens = 8192;
            source.Temperature = 0.7;
            source.Timeout = 45;
            source.Retries = 4;
            source.Health = new AgentHealth { Status = AgentHealthStatus.Ready, LatencyMs = 812 };
            var (_, controls) = BuildPage(SettingsWith(source, MakeAgent("Kimi")));

            Assert.Null(controls.TryDuplicateSelectedAgent());

            Assert.Equal(3, controls.WorkingAgents.Count);
            var copy = controls.WorkingAgents[1]; // inserted directly after the source
            Assert.NotEqual(source.Id, copy.Id);
            Assert.Equal("Claude (work) (copy)", copy.Name);
            Assert.Equal(source.Provider, copy.Provider);
            Assert.Equal(source.Model, copy.Model);
            // The key is copied: verbatim-equal to the working source's blob, and it unwraps to
            // the same key. (Blob-for-blob equality with the ORIGINAL settings object is not the
            // contract — committing re-wraps the displayed plaintext, and DPAPI output varies.)
            Assert.Equal(controls.WorkingAgents[0].ApiKey, copy.ApiKey);
            Assert.Equal("sk-work", ApiKeyProtector.Unprotect(copy.ApiKey));
            Assert.Equal(source.Endpoint, copy.Endpoint);
            Assert.Equal(source.MaxTokens, copy.MaxTokens);
            Assert.Equal(source.Temperature, copy.Temperature);
            Assert.Equal(source.Timeout, copy.Timeout);
            Assert.Equal(source.Retries, copy.Retries);
            Assert.Null(copy.Health); // health does not transfer to an unverified copy
            Assert.Equal(copy.Id, controls.SelectedAgentId);
        }

        [StaFact]
        public void Remove_asks_for_confirmation_naming_the_agent()
        {
            var (_, controls) = BuildPage(SettingsWith(MakeAgent("Claude (work)"), MakeAgent("Kimi")));
            SelectAgentInList(controls, 1);

            string? asked = null;
            controls.RemoveConfirmation = text => { asked = text; return false; };
            Click(controls.ListView.RemoveButton);

            Assert.NotNull(asked);
            Assert.Contains("Kimi", asked);
            Assert.Equal(2, controls.WorkingAgents.Count); // declined — nothing removed
        }

        [StaFact]
        public void Remove_repairs_the_active_id_assignments_and_fallback_order()
        {
            var a = MakeAgent("A");
            var b = MakeAgent("B");
            var c = MakeAgent("C");
            var settings = SettingsWith(a, b, c);
            settings.Ai.ActiveAgentId = b.Id;
            settings.Ai.FeatureAgents.Chat = b.Id;
            settings.Ai.FeatureAgents.Fix = c.Id;
            settings.Ai.FallbackOrder.Add(b.Id);
            settings.Ai.FallbackOrder.Add(c.Id);
            var (dialog, controls) = BuildPage(settings);

            SelectAgentInList(controls, 1);
            controls.RemoveConfirmation = _ => true;
            Click(controls.ListView.RemoveButton);

            Assert.Equal(2, controls.WorkingAgents.Count);
            // The active id pointed at the removed agent: repaired to the first usable survivor.
            Assert.Equal(a.Id, controls.ActiveAgentId);

            var saved = dialog.GetSettings();
            Assert.Equal(string.Empty, saved.Ai.FeatureAgents.Chat);  // V16 — assignment cleared
            Assert.Equal(c.Id, saved.Ai.FeatureAgents.Fix);           // …but only the removed one
            Assert.DoesNotContain(b.Id, saved.Ai.FallbackOrder);      // V17 — fallback entry dropped
            Assert.Contains(c.Id, saved.Ai.FallbackOrder);
        }

        [StaFact]
        public void Remove_selects_the_nearest_surviving_agent()
        {
            var a = MakeAgent("A");
            var b = MakeAgent("B");
            var c = MakeAgent("C");
            var (_, controls) = BuildPage(SettingsWith(a, b, c));
            controls.RemoveConfirmation = _ => true;

            // Middle removal: the agent that slid into the vacated slot is selected.
            SelectAgentInList(controls, 1);
            Click(controls.ListView.RemoveButton);
            Assert.Equal(c.Id, controls.SelectedAgentId);

            // Tail removal: the previous agent is selected.
            Click(controls.ListView.RemoveButton);
            Assert.Equal(a.Id, controls.SelectedAgentId);
        }

        [StaFact]
        public void Set_active_marks_the_agent_and_persists_it()
        {
            var a = MakeAgent("A");
            var b = MakeAgent("B", provider: "kimi", model: "kimi-latest");
            var (dialog, controls) = BuildPage(SettingsWith(a, b));

            SelectAgentInList(controls, 1);
            Assert.Null(controls.TrySetActiveSelectedAgent());

            Assert.Equal(b.Id, controls.ActiveAgentId);
            var saved = dialog.GetSettings();
            Assert.Equal(b.Id, saved.Ai.ActiveAgentId);
            // The flat mirror follows the active agent (V18), so older builds read B too.
            Assert.Equal("kimi", saved.Ai.Provider);
            Assert.Equal("kimi-latest", saved.Ai.Model);
            // The active marker moved to B's row.
            Assert.Contains(RowTexts(controls.ListView.List, 1), t => t == "●");
            Assert.DoesNotContain(RowTexts(controls.ListView.List, 0), t => t == "●");
        }

        [StaFact]
        public void Set_active_is_refused_for_an_unusable_agent_with_a_reason()
        {
            var a = MakeAgent("A");
            var bad = MakeAgent("Kimi", provider: "kimi", model: "kimi-latest", apiKey: "");
            var (_, controls) = BuildPage(SettingsWith(a, bad));

            SelectAgentInList(controls, 1);
            var refusal = controls.TrySetActiveSelectedAgent();

            Assert.NotNull(refusal);
            Assert.Contains("Kimi", refusal);
            Assert.Contains("API key", refusal);
            Assert.Equal(a.Id, controls.ActiveAgentId); // unchanged
        }

        // ── The per-agent Enabled toggle (review finding) ────────────────────

        [StaFact]
        public void A_disabled_agent_can_be_re_enabled_and_saved()
        {
            var on = MakeAgent("On", provider: "kimi", model: "kimi-latest");
            var off = MakeAgent("Off", provider: "kimi", model: "kimi-latest");
            off.Enabled = false;
            var (dialog, controls) = BuildPage(SettingsWith(on, off));

            var toggle = EnabledToggle(controls);
            Assert.True(toggle.IsChecked == true);    // bound from the active (enabled) agent

            SelectAgentInList(controls, 1);
            Assert.True(toggle.IsChecked == false);   // bound from the disabled agent

            toggle.IsChecked = true;
            var saved = dialog.GetSettings();

            Assert.True(saved.Ai.Agents[1].Enabled);
            Assert.True(saved.Ai.Enabled);   // V19 on the write path — the agent is usable again
        }

        [StaFact]
        public void Disabling_an_agent_commits_through_save()
        {
            var agent = MakeAgent("On", provider: "kimi", model: "kimi-latest");
            var (dialog, controls) = BuildPage(SettingsWith(agent));

            EnabledToggle(controls).IsChecked = false;
            var saved = dialog.GetSettings();

            Assert.False(saved.Ai.Agents[0].Enabled);
            Assert.False(saved.Ai.Enabled);   // nothing usable left
        }

        [StaFact]
        public void Toggling_enabled_does_not_reset_health()
        {
            var agent = MakeAgent("Ready");
            agent.Health = new AgentHealth { Status = AgentHealthStatus.Ready, LatencyMs = 812 };
            var (_, controls) = BuildPage(SettingsWith(agent));

            EnabledToggle(controls).IsChecked = false;

            // Only provider/model/key/endpoint edits invalidate a recorded check (T086).
            Assert.Equal(AgentHealthStatus.Ready, controls.WorkingAgents[0].Health?.Status);
        }

        [StaFact]
        public void Set_active_is_refused_while_the_agent_is_disabled()
        {
            var a = MakeAgent("A");
            var off = MakeAgent("Off", provider: "kimi", model: "kimi-latest");
            off.Enabled = false;
            var (_, controls) = BuildPage(SettingsWith(a, off));

            SelectAgentInList(controls, 1);
            var refusal = controls.TrySetActiveSelectedAgent();

            Assert.NotNull(refusal);
            Assert.Contains("Off", refusal);
            Assert.Contains("disabled", refusal);
            Assert.Equal(a.Id, controls.ActiveAgentId); // unchanged
        }

        [StaFact]
        public void List_rows_show_name_provider_model_health_and_the_active_marker()
        {
            var a = MakeAgent("Claude (work)");
            a.Health = new AgentHealth { Status = AgentHealthStatus.Ready, LatencyMs = 812 };
            var b = MakeAgent("Kimi", provider: "kimi", model: "kimi-latest");
            var (_, controls) = BuildPage(SettingsWith(a, b));

            var list = controls.ListView.List;
            Assert.Equal(2, list.Items.Count);

            var rowA = RowTexts(list, 0);
            Assert.Contains(rowA, t => t == "●");
            Assert.Contains(rowA, t => t == "Claude (work)");
            Assert.Contains(rowA, t => t == "anthropic · claude-sonnet-4-6");
            Assert.Contains(rowA, t => t == "✔ Ready");

            var rowB = RowTexts(list, 1);
            Assert.DoesNotContain(rowB, t => t == "●");
            Assert.Contains(rowB, t => t == "Kimi");
            Assert.Contains(rowB, t => t == "kimi · kimi-latest");
            Assert.Contains(rowB, t => t == "– Not tested");
        }

        // ── T034: the working-copy contract (FR-031) ─────────────────────────

        [StaFact]
        public void Switching_selection_retains_unsaved_edits()
        {
            var a = MakeAgent("A");
            var b = MakeAgent("B", provider: "kimi", model: "kimi-latest");
            var (_, controls) = BuildPage(SettingsWith(a, b));

            ModelBox(controls).Text = "claude-opus-edited";
            SelectAgentInList(controls, 1);  // leaving A commits the edit to the working copy
            SelectAgentInList(controls, 0);  // …and it is still there on return

            Assert.Equal("claude-opus-edited", ModelBox(controls).Text);
            Assert.Equal("claude-opus-edited", controls.WorkingAgents[0].Model);
            Assert.Equal("kimi-latest", controls.WorkingAgents[1].Model);
        }

        [StaFact]
        public void Editing_one_agent_does_not_alter_another()
        {
            var a = MakeAgent("A");
            var b = MakeAgent("B", provider: "kimi", model: "kimi-latest");
            var (dialog, controls) = BuildPage(SettingsWith(a, b));

            ModelBox(controls).Text = "claude-opus-edited";
            EndpointBox(controls).Text = "https://edited.example.com";
            SelectAgentInList(controls, 1);

            Assert.Equal("kimi-latest", ModelBox(controls).Text);
            Assert.Equal(string.Empty, EndpointBox(controls).Text);

            var saved = dialog.GetSettings();
            Assert.Equal("claude-opus-edited", saved.Ai.Agents[0].Model);
            Assert.Equal("kimi-latest", saved.Ai.Agents[1].Model);
            Assert.Equal(string.Empty, saved.Ai.Agents[1].Endpoint);
        }

        [StaFact]
        public void Provider_switch_autofill_applies_to_the_selected_agent_only()
        {
            // FR-034 (T047): the model auto-correction follows the provider switch of the agent
            // being edited and leaves every other agent's model alone.
            var a = MakeAgent("A");
            var b = MakeAgent("B", provider: "kimi", model: "kimi-latest");
            var (_, controls) = BuildPage(SettingsWith(a, b));

            ProviderCombo(controls).SelectedIndex = 4; // A: Anthropic → Gemini
            Assert.Equal("gemini-flash-latest", ModelBox(controls).Text);

            SelectAgentInList(controls, 1);
            Assert.Equal("kimi-latest", ModelBox(controls).Text);           // B untouched
            Assert.Equal("kimi-latest", controls.WorkingAgents[1].Model);

            SelectAgentInList(controls, 0);
            Assert.Equal("gemini-flash-latest", ModelBox(controls).Text);   // A's correction retained
        }

        [StaFact]
        public void Cancel_discards_every_edit()
        {
            var a = MakeAgent("A");
            var b = MakeAgent("B", provider: "kimi", model: "kimi-latest");
            var settings = SettingsWith(a, b);
            var originalModel = a.Model;
            var (_, controls) = BuildPage(settings);

            ModelBox(controls).Text = "claude-opus-edited";
            Assert.Null(controls.TryAddAgent());
            Assert.Equal(3, controls.WorkingAgents.Count);

            // Cancel = the host never calls Save. The ORIGINAL settings object must be untouched:
            // the page edited a deep copy (research R13).
            Assert.Equal(2, settings.Ai.Agents.Count);
            Assert.Equal(originalModel, settings.Ai.Agents[0].Model);
            Assert.Equal("kimi-latest", settings.Ai.Agents[1].Model);
        }

        // ── T035: validation (FR-032) ────────────────────────────────────────

        [StaTheory]
        [InlineData("Claude (work)")]
        [InlineData("claude (work)")]      // case-insensitive
        [InlineData("  Claude (work)  ")]  // trim-insensitive
        public void A_duplicate_name_is_refused_naming_the_agent(string duplicate)
        {
            var kimi = MakeAgent("Kimi");
            var claude = MakeAgent("Claude (work)");
            var (_, controls) = BuildPage(SettingsWith(kimi, claude));

            NameBox(controls).Text = duplicate; // renaming Kimi onto Claude
            var error = controls.ValidateWorkingCopy();

            Assert.NotNull(error);
            Assert.Contains("Another agent is already named", error);
            Assert.Contains(duplicate.Trim(), error); // names the name at fault
            // The offending agent is selected before the message is shown.
            Assert.Equal(kimi.Id, controls.SelectedAgentId);
        }

        [StaFact]
        public void Validation_covers_the_whole_working_copy_not_just_the_selection()
        {
            var good = MakeAgent("Good");
            var bad = MakeAgent("Bad", provider: "anthropic", model: "");
            var (_, controls) = BuildPage(SettingsWith(good, bad));

            // "Good" is selected and valid; the invalid agent is not the selected one.
            Assert.Equal(good.Id, controls.SelectedAgentId);
            var error = controls.ValidateWorkingCopy();

            Assert.NotNull(error);
            Assert.Contains("Bad", error);   // the agent …
            Assert.Contains("Model", error); // … and the field
            Assert.Equal(bad.Id, controls.SelectedAgentId);
        }

        [StaFact]
        public void Rename_preserves_the_active_id_assignments_and_fallback_order()
        {
            var a = MakeAgent("A");
            var b = MakeAgent("Kimi", provider: "kimi", model: "kimi-latest");
            var settings = SettingsWith(a, b);
            settings.Ai.FeatureAgents.Chat = b.Id;
            settings.Ai.FallbackOrder.Add(b.Id);
            var (dialog, controls) = BuildPage(settings);

            SelectAgentInList(controls, 1);
            NameBox(controls).Text = "Kimi K2";
            var saved = dialog.GetSettings();

            Assert.Equal("Kimi K2", saved.Ai.Agents[1].Name);
            Assert.Equal(b.Id, saved.Ai.Agents[1].Id);              // FR-005 — references are by id
            Assert.Equal(a.Id, saved.Ai.ActiveAgentId);
            Assert.Equal(b.Id, saved.Ai.FeatureAgents.Chat);
            Assert.Contains(b.Id, saved.Ai.FallbackOrder);
        }

        [StaFact]
        public void Name_validation_fires_on_focus_loss()
        {
            var (_, controls) = BuildPage(SettingsWith(MakeAgent("Kimi"), MakeAgent("Claude (work)")));

            var nameBox = NameBox(controls);
            nameBox.Text = "Claude (work)";
            nameBox.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));

            var nameError = GetField<TextBlock>(controls, "_nameError");
            Assert.Equal(Visibility.Visible, nameError.Visibility);
            Assert.Contains("Claude (work)", nameError.Text);

            nameBox.Text = "Kimi K2";
            nameBox.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
            Assert.Equal(Visibility.Collapsed, nameError.Visibility);
        }

        [StaFact]
        public void A_completely_blank_agent_does_not_block_ok()
        {
            // The page seeds one blank agent whenever the list is empty, so an unconfigured user
            // must still be able to OK the dialog — the connection rules bite only once ANY
            // connection field is filled.
            var (_, controls) = BuildPage(new AppSettings());

            Assert.Single(controls.WorkingAgents); // the seed
            Assert.Null(controls.ValidateWorkingCopy());
        }

        [StaFact]
        public void A_blank_agent_with_an_empty_name_is_still_refused()
        {
            var (_, controls) = BuildPage(new AppSettings());

            NameBox(controls).Text = "  ";
            var error = controls.ValidateWorkingCopy();

            Assert.NotNull(error);
            Assert.Contains("Name is required.", error);
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
            return (dialog, GetAiControls(dialog));
        }

        private static AiAssistanceControls GetAiControls(SettingsWindow dialog)
        {
            var f = typeof(SettingsWindow).GetField("_pageControlsByKey",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(f);
            var pages = (Dictionary<string, IPageControls>)f!.GetValue(dialog)!;
            Assert.True(pages.TryGetValue("AI Assistance", out var controls), "AI Assistance controls not found.");
            return (AiAssistanceControls)controls!;
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

        /// <summary>The visible text runs of one rendered agent row (marker, name, detail, badge).</summary>
        private static List<string> RowTexts(ListBox list, int index)
        {
            var item = (ListBoxItem)list.Items[index];
            var texts = new List<string>();
            foreach (var block in LogicalTree.Descendants<TextBlock>((DependencyObject)item.Content))
                texts.Add(block.Text);
            return texts;
        }

        private static TextBox NameBox(AiAssistanceControls controls) => GetField<TextBox>(controls, "_name");
        private static TextBox ModelBox(AiAssistanceControls controls) => GetField<TextBox>(controls, "_model");
        private static TextBox EndpointBox(AiAssistanceControls controls) => GetField<TextBox>(controls, "_endpoint");
        private static ComboBox ProviderCombo(AiAssistanceControls controls) => GetField<ComboBox>(controls, "_provider");
        private static CheckBox EnabledToggle(AiAssistanceControls controls) => GetField<CheckBox>(controls, "_agentEnabled");

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
