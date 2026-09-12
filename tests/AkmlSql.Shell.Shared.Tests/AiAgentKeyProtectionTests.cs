using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Dialogs;
using AkmlSql.Shell.Shared.Dialogs.Pages;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 037 (US2) T036 — the per-agent decrypt-failure guard (FR-009, V23, research R11).
    /// The PR #251 contract generalises to every agent: an undecryptable stored key survives any
    /// save the user did not retype it in, typing clears the guard and the notice together, and
    /// switching between a decryptable and an undecryptable agent keeps both states correct
    /// (which is why the flag lives on the working-copy agent, not in one page-level field).
    /// </summary>
    public class AiAgentKeyProtectionTests
    {
        /// <summary>A syntactically valid dpapi: blob this user cannot decrypt (bad HMAC).</summary>
        private static string UndecryptableKey() =>
            "dpapi:" + Convert.ToBase64String(new byte[64]);

        [StaFact]
        public void An_undecryptable_key_survives_a_save_per_agent()
        {
            var blob = UndecryptableKey();
            var good = MakeAgent("Good", "sk-good");
            var bad = MakeAgent("Bad", "sk-bad");
            bad.ApiKey = blob;
            var (dialog, controls) = BuildPage(SettingsWith(good, bad));

            // Bad was never selected: its stored key must come through Save byte-identical.
            var saved = dialog.GetSettings();
            Assert.Equal(blob, saved.Ai.Agents[1].ApiKey);

            // Select Bad: empty box + the notice — and a save the user did not retype the key in
            // must STILL not overwrite the stored value.
            SelectAgent(controls, 1);
            var keyBox = GetField<TextBox>(controls, "_apiKey");
            var notice = GetField<Border>(controls, "_keyNotice");
            Assert.Equal(string.Empty, keyBox.Text);
            Assert.Equal(Visibility.Visible, notice.Visibility);

            saved = dialog.GetSettings();
            Assert.Equal(blob, saved.Ai.Agents[1].ApiKey);
            // Good's key is unaffected by any of this.
            Assert.Equal("sk-good", ApiKeyProtector.Unprotect(saved.Ai.Agents[0].ApiKey));
        }

        [StaFact]
        public void Typing_clears_the_guard_and_the_notice_together()
        {
            var bad = MakeAgent("Bad", "sk-bad");
            bad.ApiKey = UndecryptableKey();
            var (dialog, controls) = BuildPage(SettingsWith(MakeAgent("Good", "sk-good"), bad));

            SelectAgent(controls, 1);
            var notice = GetField<Border>(controls, "_keyNotice");
            Assert.Equal(Visibility.Visible, notice.Visibility);
            Assert.True(controls.WorkingAgents[1].KeyDecryptFailed);

            GetField<TextBox>(controls, "_apiKey").Text = "sk-retyped";

            // Both, together (PR #251 follow-up, per agent): the notice's claim stops being true
            // the moment the user takes control of the field.
            Assert.Equal(Visibility.Collapsed, notice.Visibility);
            Assert.False(controls.WorkingAgents[1].KeyDecryptFailed);

            var saved = dialog.GetSettings();
            Assert.True(ApiKeyProtector.IsProtected(saved.Ai.Agents[1].ApiKey),
                "the re-entered key must be wrapped, not stored plaintext");
            Assert.Equal("sk-retyped", ApiKeyProtector.Unprotect(saved.Ai.Agents[1].ApiKey));
        }

        [StaFact]
        public void Switching_between_decryptable_and_undecryptable_agents_keeps_both_correct()
        {
            var good = MakeAgent("Good", "sk-good");
            var bad = MakeAgent("Bad", "sk-bad");
            bad.ApiKey = UndecryptableKey();
            var (_, controls) = BuildPage(SettingsWith(good, bad));

            var keyBox = GetField<TextBox>(controls, "_apiKey");
            var notice = GetField<Border>(controls, "_keyNotice");

            // Initial selection is the active (good) agent.
            Assert.Equal("sk-good", keyBox.Text);
            Assert.Equal(Visibility.Collapsed, notice.Visibility);
            Assert.False(controls.WorkingAgents[0].KeyDecryptFailed);

            SelectAgent(controls, 1);
            Assert.Equal(string.Empty, keyBox.Text);
            Assert.Equal(Visibility.Visible, notice.Visibility);
            Assert.True(controls.WorkingAgents[1].KeyDecryptFailed);
            Assert.False(controls.WorkingAgents[0].KeyDecryptFailed); // Good's state untouched

            SelectAgent(controls, 0);
            Assert.Equal("sk-good", keyBox.Text);
            Assert.Equal(Visibility.Collapsed, notice.Visibility);
            Assert.False(controls.WorkingAgents[0].KeyDecryptFailed);
            Assert.True(controls.WorkingAgents[1].KeyDecryptFailed); // Bad's guard survives the switch

            SelectAgent(controls, 1);
            Assert.Equal(string.Empty, keyBox.Text);
            Assert.Equal(Visibility.Visible, notice.Visibility);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static AiAgent MakeAgent(string name, string plainKey)
        {
            return new AiAgent
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Provider = "anthropic",
                Model = "claude-sonnet-4-6",
                ApiKey = ApiKeyProtector.Protect(plainKey),
                Enabled = true,
                CreatedUtc = DateTime.UtcNow.ToString("O"),
            };
        }

        private static AppSettings SettingsWith(params AiAgent[] agents)
        {
            var settings = new AppSettings();
            foreach (var agent in agents) settings.Ai.Agents.Add(agent);
            settings.Ai.ActiveAgentId = agents[0].Id;
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

        private static void SelectAgent(AiAssistanceControls controls, int index)
        {
            var list = controls.ListView.List;
            Assert.True(index < list.Items.Count,
                $"agent list holds {list.Items.Count} rows; cannot select index {index}");
            list.SelectedIndex = index;
        }

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
