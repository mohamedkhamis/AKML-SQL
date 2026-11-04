#nullable enable
using System;
using System.Linq;
using System.Windows;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Dialogs.Pages;
using Xunit;
using static AkmlSql.Shell.Shared.Tests.AiPageTestKit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 037 (US2) T033–T035, reshaped by the 2026-11 master–detail redesign (SSMS layout B):
    /// the agent grid's CRUD — Add… and Edit… through the agent dialog (driven here through
    /// <see cref="AiAssistanceControls.EditorHost"/>), Duplicate, Delete with its confirmation,
    /// Set as active — the working-copy contract (a cancelled dialog and a cancelled Options
    /// window both discard), whole-copy validation (FR-032) and rename stability (FR-005).
    /// </summary>
    public class AiAgentListPageTests
    {
        // ── Add… ─────────────────────────────────────────────────────────────

        [StaFact]
        public void Add_opens_the_dialog_with_a_unique_name_and_adds_the_accepted_agent()
        {
            var (_, controls) = BuildPage(SettingsWith(MakeAgent("Agent 1")));
            AiAgentEditor? seen = null;
            AcceptNextEditor(controls, e => FillUsable(e), e => seen = e);

            Click(controls.ListView.AddButton);

            Assert.NotNull(seen);
            Assert.True(seen!.IsNew);
            Assert.Equal("Add AI agent", seen.Title);
            Assert.Equal(2, controls.WorkingAgents.Count);
            var added = controls.WorkingAgents[1];
            Assert.Equal("Agent 2", added.Name);
            Assert.Equal(32, added.Id.Length); // Guid "N" — R14
            Assert.Equal("anthropic", added.Provider);
            Assert.Equal("sk-new", ApiKeyProtector.Unprotect(added.ApiKey));
            Assert.Equal(added.Id, controls.SelectedAgentId);
        }

        [StaFact]
        public void A_cancelled_add_adds_nothing()
        {
            var (_, controls) = BuildPage(SettingsWith(MakeAgent("Agent 1")));
            CancelNextEditor(controls, e => FillUsable(e));

            Assert.Null(controls.TryAddAgent());

            Assert.Single(controls.WorkingAgents);
        }

        [StaFact]
        public void The_dialog_refuses_an_agent_without_a_provider()
        {
            var (_, controls) = BuildPage(SettingsWith(MakeAgent("Agent 1")));
            string? refusal = null;
            controls.EditorHost = editor =>
            {
                refusal = editor.TryAccept();   // nothing filled in
                return refusal == null;
            };

            controls.TryAddAgent();

            Assert.Equal("Choose a provider.", refusal);
            Assert.Single(controls.WorkingAgents);
        }

        [StaFact]
        public void The_first_usable_agent_of_an_empty_list_becomes_active()
        {
            var (dialog, controls) = BuildPage(new AppSettings());
            Assert.Empty(controls.WorkingAgents);
            AcceptNextEditor(controls, e => FillUsable(e, "Claude"));

            Click(controls.ListView.AddButton);

            var added = Assert.Single(controls.WorkingAgents);
            Assert.Equal(added.Id, controls.ActiveAgentId);
            var saved = dialog.GetSettings();
            Assert.Equal(added.Id, saved.Ai.ActiveAgentId);
            Assert.True(saved.Ai.Enabled);
        }

        [StaTheory]
        [InlineData(true)]
        [InlineData(false)]
        public void The_21st_agent_is_refused_naming_the_limit(bool viaDuplicate)
        {
            var agents = new AiAgent[AiAgentResolver.MaxAgents];
            for (var i = 0; i < agents.Length; i++) agents[i] = MakeAgent("Agent " + (i + 1));
            var (_, controls) = BuildPage(SettingsWith(agents));
            var opened = false;
            controls.EditorHost = _ => { opened = true; return true; };

            var refusal = viaDuplicate ? controls.TryDuplicateSelectedAgent() : controls.TryAddAgent();

            Assert.NotNull(refusal);
            Assert.Contains("20", refusal);
            Assert.False(opened);   // refused before the dialog
            Assert.Equal(AiAgentResolver.MaxAgents, controls.WorkingAgents.Count);
        }

        // ── Edit… ────────────────────────────────────────────────────────────

        [StaFact]
        public void Edit_opens_the_selected_agent_and_applies_the_accepted_changes()
        {
            var a = MakeAgent("A");
            var b = MakeAgent("B", provider: "kimi", model: "kimi-latest");
            var (dialog, controls) = BuildPage(SettingsWith(a, b));
            Select(controls, 1);
            AiAgentEditor? seen = null;
            AcceptNextEditor(controls, e => e.ModelBox.Text = "kimi-k2-thinking", e => seen = e);

            Click(controls.ListView.EditButton);

            Assert.Equal("Edit AI agent: B", seen!.Title);
            Assert.Equal("kimi-k2-thinking", controls.WorkingAgents[1].Model);
            Assert.Equal(b.Id, controls.WorkingAgents[1].Id);
            var saved = dialog.GetSettings();
            Assert.Equal("kimi-k2-thinking", saved.Ai.Agents[1].Model);
            Assert.Equal("claude-sonnet-4-6", saved.Ai.Agents[0].Model);   // A untouched
        }

        [StaFact]
        public void The_summary_edit_link_opens_the_same_dialog()
        {
            var (_, controls) = BuildPage(SettingsWith(MakeAgent("A")));
            var opened = 0;
            controls.EditorHost = _ => { opened++; return false; };

            Click(controls.EditLink);

            Assert.Equal(1, opened);
        }

        [StaFact]
        public void A_cancelled_edit_changes_nothing()
        {
            var (_, controls) = BuildPage(SettingsWith(MakeAgent("A")));
            CancelNextEditor(controls, e =>
            {
                e.ModelBox.Text = "claude-opus-edited";
                e.NameBox.Text = "Renamed";
            });

            controls.TryEditSelectedAgent();

            Assert.Equal("claude-sonnet-4-6", controls.WorkingAgents[0].Model);
            Assert.Equal("A", controls.WorkingAgents[0].Name);
        }

        [StaFact]
        public void Provider_switch_autofill_corrects_a_foreign_model_in_the_dialog()
        {
            // FR-034 (T047): the model auto-correction follows the provider switch of the agent
            // being edited.
            var (_, controls) = BuildPage(SettingsWith(MakeAgent("A")));
            AcceptNextEditor(controls, e =>
            {
                e.ProviderCombo.SelectedIndex = 4;   // Anthropic → Gemini
                Assert.Equal("gemini-flash-latest", e.ModelBox.Text);
                e.KeyBox.Text = "gm-key";            // a key belongs to its provider: the old one is dropped
            });

            controls.TryEditSelectedAgent();

            Assert.Equal("gemini", controls.WorkingAgents[0].Provider);
            Assert.Equal("gemini-flash-latest", controls.WorkingAgents[0].Model);
        }

        [StaFact]
        public void A_disabled_agent_can_be_re_enabled_and_saved()
        {
            var on = MakeAgent("On", provider: "kimi", model: "kimi-latest");
            var off = MakeAgent("Off", provider: "kimi", model: "kimi-latest");
            off.Enabled = false;
            var (dialog, controls) = BuildPage(SettingsWith(on, off));

            Select(controls, 1);
            AcceptNextEditor(controls, e =>
            {
                Assert.True(e.EnabledToggle.IsChecked == false);   // bound from the disabled agent
                e.EnabledToggle.IsChecked = true;
            });
            controls.TryEditSelectedAgent();
            var saved = dialog.GetSettings();

            Assert.True(saved.Ai.Agents[1].Enabled);
            Assert.True(saved.Ai.Enabled);   // V19 on the write path
        }

        [StaFact]
        public void Disabling_an_agent_commits_through_save()
        {
            var (dialog, controls) = BuildPage(SettingsWith(MakeAgent("On", provider: "kimi", model: "kimi-latest")));
            AcceptNextEditor(controls, e => e.EnabledToggle.IsChecked = false);

            controls.TryEditSelectedAgent();
            var saved = dialog.GetSettings();

            Assert.False(saved.Ai.Agents[0].Enabled);
            Assert.False(saved.Ai.Enabled);   // nothing usable left
            Assert.Contains("Off", RowTexts(controls.ListView.List, 0));
        }

        [StaFact]
        public void Toggling_enabled_does_not_reset_health()
        {
            var agent = MakeAgent("Ready");
            agent.Health = new AgentHealth { Status = AgentHealthStatus.Ready, LatencyMs = 812 };
            var (_, controls) = BuildPage(SettingsWith(agent));
            AcceptNextEditor(controls, e => e.EnabledToggle.IsChecked = false);

            controls.TryEditSelectedAgent();

            // Only provider/model/key/endpoint edits invalidate a recorded check (T086).
            Assert.Equal(AgentHealthStatus.Ready, controls.WorkingAgents[0].Health?.Status);
        }

        // ── Duplicate, Delete, Set as active ─────────────────────────────────

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
        public void Delete_asks_for_confirmation_naming_the_agent_and_its_features()
        {
            var claude = MakeAgent("Claude (work)");
            var kimi = MakeAgent("Kimi", provider: "kimi", model: "kimi-latest");
            var settings = SettingsWith(claude, kimi);
            settings.Ai.FeatureAgents.Fix = kimi.Id;
            var (_, controls) = BuildPage(settings);
            Select(controls, 1);

            string? asked = null;
            controls.DeleteConfirmation = text => { asked = text; return false; };
            Click(controls.ListView.DeleteButton);

            Assert.NotNull(asked);
            Assert.Contains("Kimi", asked);
            Assert.Contains("Fix errors", asked);
            Assert.Equal(2, controls.WorkingAgents.Count); // declined — nothing deleted
        }

        [StaFact]
        public void Delete_repairs_the_active_id_assignments_and_fallback_order()
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

            Select(controls, 1);
            controls.DeleteConfirmation = _ => true;
            Click(controls.ListView.DeleteButton);

            Assert.Equal(2, controls.WorkingAgents.Count);
            Assert.Equal(a.Id, controls.ActiveAgentId);   // repaired to the first usable survivor

            var saved = dialog.GetSettings();
            Assert.Equal(string.Empty, saved.Ai.FeatureAgents.Chat);  // V16 — assignment cleared
            Assert.Equal(c.Id, saved.Ai.FeatureAgents.Fix);           // …but only the deleted one
            Assert.DoesNotContain(b.Id, saved.Ai.FallbackOrder);      // V17 — fallback entry dropped
            Assert.Contains(c.Id, saved.Ai.FallbackOrder);
        }

        [StaFact]
        public void Delete_selects_the_nearest_surviving_agent()
        {
            var a = MakeAgent("A");
            var b = MakeAgent("B");
            var c = MakeAgent("C");
            var (_, controls) = BuildPage(SettingsWith(a, b, c));
            controls.DeleteConfirmation = _ => true;

            Select(controls, 1);
            Click(controls.ListView.DeleteButton);
            Assert.Equal(c.Id, controls.SelectedAgentId);

            Click(controls.ListView.DeleteButton);
            Assert.Equal(a.Id, controls.SelectedAgentId);
        }

        [StaFact]
        public void Deleting_the_last_agent_shows_the_empty_state()
        {
            var (dialog, controls) = BuildPage(SettingsWith(MakeAgent("Only")));
            controls.DeleteConfirmation = _ => true;

            Click(controls.ListView.DeleteButton);

            Assert.Empty(controls.WorkingAgents);
            Assert.Equal(Visibility.Visible, controls.ListView.EmptyText.Visibility);
            Assert.False(controls.ListView.EditButton.IsEnabled);
            Assert.Equal("No AI agent yet", controls.SummaryTitle.Text);
            var saved = dialog.GetSettings();
            Assert.Empty(saved.Ai.Agents);
            Assert.Equal(string.Empty, saved.Ai.Provider);   // the flat mirror cannot resurrect it
        }

        [StaFact]
        public void Set_active_marks_the_agent_and_persists_it()
        {
            var a = MakeAgent("A");
            var b = MakeAgent("B", provider: "kimi", model: "kimi-latest");
            var (dialog, controls) = BuildPage(SettingsWith(a, b));

            Select(controls, 1);
            Assert.Null(controls.TrySetActiveSelectedAgent());

            Assert.Equal(b.Id, controls.ActiveAgentId);
            var saved = dialog.GetSettings();
            Assert.Equal(b.Id, saved.Ai.ActiveAgentId);
            Assert.Equal("kimi", saved.Ai.Provider);      // the flat mirror follows the active agent (V18)
            Assert.Equal("kimi-latest", saved.Ai.Model);
            Assert.Contains(RowTexts(controls.ListView.List, 1), t => t == "●");
            Assert.DoesNotContain(RowTexts(controls.ListView.List, 0), t => t == "●");
            Assert.False(controls.ListView.SetActiveButton.IsEnabled);   // already active
        }

        [StaFact]
        public void Set_active_is_refused_for_an_unusable_agent_with_a_reason()
        {
            var a = MakeAgent("A");
            var bad = MakeAgent("Kimi", provider: "kimi", model: "kimi-latest", apiKey: "");
            var (_, controls) = BuildPage(SettingsWith(a, bad));

            Select(controls, 1);
            var refusal = controls.TrySetActiveSelectedAgent();

            Assert.NotNull(refusal);
            Assert.Contains("Kimi", refusal);
            Assert.Contains("API key", refusal);
            Assert.Equal(a.Id, controls.ActiveAgentId);
        }

        [StaFact]
        public void Set_active_is_refused_while_the_agent_is_disabled()
        {
            var a = MakeAgent("A");
            var off = MakeAgent("Off", provider: "kimi", model: "kimi-latest");
            off.Enabled = false;
            var (_, controls) = BuildPage(SettingsWith(a, off));

            Select(controls, 1);
            var refusal = controls.TrySetActiveSelectedAgent();

            Assert.NotNull(refusal);
            Assert.Contains("Off", refusal);
            Assert.Contains("disabled", refusal);
            Assert.Equal(a.Id, controls.ActiveAgentId);
        }

        // ── The grid and the summary ─────────────────────────────────────────

        [StaFact]
        public void Rows_show_name_provider_model_health_and_the_active_marker()
        {
            var a = MakeAgent("Claude (work)");
            a.Health = new AgentHealth { Status = AgentHealthStatus.Ready, LatencyMs = 812 };
            var b = MakeAgent("Kimi", provider: "kimi", model: "kimi-latest");
            var (_, controls) = BuildPage(SettingsWith(a, b));

            var list = controls.ListView.List;
            Assert.Equal(2, list.Items.Count);
            Assert.Equal("2 of 20 agents", controls.ListView.CountText.Text);

            var rowA = RowTexts(list, 0);
            Assert.Contains(rowA, t => t == "●");
            Assert.Contains(rowA, t => t == "Claude (work)");
            Assert.Contains(rowA, t => t == "Anthropic");
            Assert.Contains(rowA, t => t == "claude-sonnet-4-6");
            Assert.Contains(rowA, t => t == "✔ Ready");

            var rowB = RowTexts(list, 1);
            Assert.DoesNotContain(rowB, t => t == "●");
            Assert.Contains(rowB, t => t == "Kimi");
            Assert.Contains(rowB, t => t == "Kimi (Moonshot)");
            Assert.Contains(rowB, t => t == "kimi-latest");
            Assert.Contains(rowB, t => t == "– Not tested");
        }

        [StaFact]
        public void The_summary_describes_the_selected_agent_and_what_it_answers()
        {
            var a = MakeAgent("Claude (work)");
            var b = MakeAgent("Local", provider: "ollama", model: "llama3.1:8b", apiKey: "", endpoint: "http://localhost:11434");
            var settings = SettingsWith(a, b);
            settings.Ai.FeatureAgents.GhostText = b.Id;
            settings.Ai.FallbackOrder.Add(b.Id);
            var (_, controls) = BuildPage(settings);

            Assert.Equal("Claude (work)", controls.SummaryTitle.Text);
            Assert.Contains("Anthropic", controls.SummaryDetail.Text);
            Assert.Contains("API key stored", controls.SummaryDetail.Text);
            Assert.Contains("Active agent", controls.SummaryUse.Text);

            Select(controls, 1);
            Assert.Equal("Local", controls.SummaryTitle.Text);
            Assert.Contains("http://localhost:11434", controls.SummaryDetail.Text);
            Assert.Contains("no key needed", controls.SummaryDetail.Text);
            Assert.Contains("Inline ghost text", controls.SummaryUse.Text);
            Assert.Contains("Fallback #1", controls.SummaryUse.Text);
        }

        [StaFact]
        public void An_empty_config_shows_the_empty_state_and_seeds_no_agent()
        {
            var (dialog, controls) = BuildPage(new AppSettings());

            Assert.Empty(controls.WorkingAgents);
            Assert.Equal(Visibility.Visible, controls.ListView.EmptyText.Visibility);
            Assert.Equal("0 of 20 agents", controls.ListView.CountText.Text);
            Assert.True(controls.ListView.AddButton.IsEnabled);
            Assert.False(controls.ListView.DeleteButton.IsEnabled);
            Assert.Null(controls.ValidateWorkingCopy());
            Assert.Empty(dialog.GetSettings().Ai.Agents);
        }

        // ── The deep link (FR-017) ───────────────────────────────────────────

        [StaFact]
        public void A_deep_link_to_an_agent_opens_its_editor()
        {
            var a = MakeAgent("A");
            var b = MakeAgent("B", provider: "kimi", model: "kimi-latest", apiKey: "");
            var (_, controls) = BuildPage(SettingsWith(a, b));
            AiAgentEditor? seen = null;
            controls.EditorHost = e => { seen = e; return false; };

            controls.InitialAgentId = b.Id;
            controls.OpenDeepLinkedEditor();

            Assert.NotNull(seen);
            Assert.False(seen!.IsNew);
            Assert.Equal(b.Id, seen.Draft.Id);
            Assert.Equal(b.Id, controls.SelectedAgentId);
            Assert.Null(controls.InitialAgentId);   // consumed once
        }

        [StaFact]
        public void An_empty_deep_link_with_no_agents_opens_add()
        {
            var (_, controls) = BuildPage(new AppSettings());
            AiAgentEditor? seen = null;
            controls.EditorHost = e => { seen = e; return false; };

            controls.InitialAgentId = string.Empty;
            controls.OpenDeepLinkedEditor();

            Assert.NotNull(seen);
            Assert.True(seen!.IsNew);
        }

        // ── The working-copy contract (FR-031) ───────────────────────────────

        [StaFact]
        public void Options_cancel_discards_every_accepted_edit()
        {
            var a = MakeAgent("A");
            var b = MakeAgent("B", provider: "kimi", model: "kimi-latest");
            var settings = SettingsWith(a, b);
            var (_, controls) = BuildPage(settings);

            AcceptNextEditor(controls, e => e.ModelBox.Text = "claude-opus-edited");
            controls.TryEditSelectedAgent();
            AcceptNextEditor(controls, e => FillUsable(e));
            controls.TryAddAgent();
            Assert.Equal(3, controls.WorkingAgents.Count);

            // Cancel = the host never calls Save. The ORIGINAL settings object is untouched:
            // the page edited a deep copy (research R13).
            Assert.Equal(2, settings.Ai.Agents.Count);
            Assert.Equal("claude-sonnet-4-6", settings.Ai.Agents[0].Model);
            Assert.Equal("kimi-latest", settings.Ai.Agents[1].Model);
        }

        // ── Validation (FR-032) ──────────────────────────────────────────────

        [StaTheory]
        [InlineData("Claude (work)")]
        [InlineData("claude (work)")]      // case-insensitive
        [InlineData("  Claude (work)  ")]  // trim-insensitive
        public void The_dialog_refuses_a_duplicate_name_naming_it(string duplicate)
        {
            var kimi = MakeAgent("Kimi");
            var claude = MakeAgent("Claude (work)");
            var (_, controls) = BuildPage(SettingsWith(kimi, claude));
            string? refusal = null;
            controls.EditorHost = e =>
            {
                e.NameBox.Text = duplicate;   // renaming Kimi onto Claude
                refusal = e.TryAccept();
                return refusal == null;
            };

            controls.TryEditSelectedAgent();

            Assert.NotNull(refusal);
            Assert.Contains("Another agent is already named", refusal);
            Assert.Contains(duplicate.Trim(), refusal);
            Assert.Equal("Kimi", controls.WorkingAgents[0].Name);   // not applied
        }

        [StaFact]
        public void Validation_covers_the_whole_working_copy_not_just_the_selection()
        {
            var good = MakeAgent("Good");
            var bad = MakeAgent("Bad", provider: "anthropic", model: "");
            var (_, controls) = BuildPage(SettingsWith(good, bad));

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

            Select(controls, 1);
            AcceptNextEditor(controls, e => e.NameBox.Text = "Kimi K2");
            controls.TryEditSelectedAgent();
            var saved = dialog.GetSettings();

            Assert.Equal("Kimi K2", saved.Ai.Agents[1].Name);
            Assert.Equal(b.Id, saved.Ai.Agents[1].Id);              // FR-005 — references are by id
            Assert.Equal(a.Id, saved.Ai.ActiveAgentId);
            Assert.Equal(b.Id, saved.Ai.FeatureAgents.Chat);
            Assert.Contains(b.Id, saved.Ai.FallbackOrder);
            Assert.Contains("Kimi K2", controls.AssignmentComboFor(AiFeature.Chat).Items.Cast<object>().Select(o => o.ToString()));
        }

        [StaFact]
        public void Name_validation_fires_on_focus_loss_in_the_dialog()
        {
            var kimi = MakeAgent("Kimi");
            var claude = MakeAgent("Claude (work)");
            var editor = EditorFor(kimi, claude);

            editor.NameBox.Text = "Claude (work)";
            editor.NameBox.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
            Assert.Equal(Visibility.Visible, editor.NameError.Visibility);
            Assert.Contains("Claude (work)", editor.NameError.Text);

            editor.NameBox.Text = "Kimi K2";
            editor.NameBox.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
            Assert.Equal(Visibility.Collapsed, editor.NameError.Visibility);
        }

        [StaFact]
        public void An_empty_name_is_refused()
        {
            var editor = EditorFor(MakeAgent("Kimi"));

            editor.NameBox.Text = "  ";

            Assert.Equal("Name is required.", editor.TryAccept());
            Assert.Equal(Visibility.Visible, editor.ErrorLine.Visibility);
        }
    }
}
