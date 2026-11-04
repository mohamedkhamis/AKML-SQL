#nullable enable
using System;
using System.Windows;
using AkmlSql.Core.Config;
using Xunit;
using static AkmlSql.Shell.Shared.Tests.AiPageTestKit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 037 (US2) T036 — the per-agent key rules, as the 2026-11 agent dialog applies them:
    /// the stored key is never shown; an empty key box keeps it (even one this machine cannot
    /// decrypt — PR #251 review finding 2); typing replaces it and clears the decrypt guard and
    /// its notice together; switching provider drops a key that belonged to the old provider.
    /// </summary>
    public class AiAgentKeyProtectionTests
    {
        /// <summary>A syntactically valid dpapi: blob this user cannot decrypt (bad HMAC).</summary>
        private static string UndecryptableKey() => "dpapi:" + Convert.ToBase64String(new byte[64]);

        [StaFact]
        public void An_undecryptable_key_survives_a_save_and_an_edit_that_leaves_the_key_empty()
        {
            var blob = UndecryptableKey();
            var good = MakeAgent("Good", apiKey: "sk-good");
            var bad = MakeAgent("Bad", apiKey: "sk-bad");
            bad.ApiKey = blob;
            var (dialog, controls) = BuildPage(SettingsWith(good, bad));

            Assert.True(controls.WorkingAgents[1].KeyDecryptFailed);
            Assert.Equal(blob, dialog.GetSettings().Ai.Agents[1].ApiKey);

            // Open Bad: empty box + the notice; OK without typing a key keeps the stored value.
            Select(controls, 1);
            AcceptNextEditor(controls, e =>
            {
                Assert.Equal(string.Empty, e.KeyBox.Text);
                Assert.Equal(Visibility.Visible, e.KeyNotice.Visibility);
                e.ModelBox.Text = "claude-opus-4-8";
            });
            controls.TryEditSelectedAgent();

            var saved = dialog.GetSettings();
            Assert.Equal(blob, saved.Ai.Agents[1].ApiKey);
            Assert.Equal("claude-opus-4-8", saved.Ai.Agents[1].Model);
            Assert.Equal("sk-good", ApiKeyProtector.Unprotect(saved.Ai.Agents[0].ApiKey));
        }

        [StaFact]
        public void Typing_a_key_clears_the_guard_and_the_notice_together()
        {
            var bad = MakeAgent("Bad", apiKey: "sk-bad");
            bad.ApiKey = UndecryptableKey();
            var (dialog, controls) = BuildPage(SettingsWith(MakeAgent("Good", apiKey: "sk-good"), bad));

            Select(controls, 1);
            AcceptNextEditor(controls, e =>
            {
                Assert.Equal(Visibility.Visible, e.KeyNotice.Visibility);
                e.KeyBox.Text = "sk-retyped";
                Assert.Equal(Visibility.Collapsed, e.KeyNotice.Visibility);
                Assert.False(e.Draft.KeyDecryptFailed);
            });
            controls.TryEditSelectedAgent();

            Assert.False(controls.WorkingAgents[1].KeyDecryptFailed);
            var saved = dialog.GetSettings();
            Assert.True(ApiKeyProtector.IsProtected(saved.Ai.Agents[1].ApiKey),
                "the re-entered key must be wrapped, not stored plaintext");
            Assert.Equal("sk-retyped", ApiKeyProtector.Unprotect(saved.Ai.Agents[1].ApiKey));
        }

        [StaFact]
        public void The_stored_key_is_never_shown_and_an_empty_box_keeps_it()
        {
            var (dialog, controls) = BuildPage(SettingsWith(MakeAgent("Claude", apiKey: "sk-stored")));

            AcceptNextEditor(controls, e =>
            {
                Assert.Equal(string.Empty, e.KeyBox.Text);
                Assert.Equal(string.Empty, e.KeyMask.Password);
                Assert.Contains("Leave this empty to keep it", e.KeyHint.Text);
                e.NameBox.Text = "Claude (renamed)";
            });
            controls.TryEditSelectedAgent();

            Assert.Equal("sk-stored", ApiKeyProtector.Unprotect(dialog.GetSettings().Ai.Agents[0].ApiKey));
        }

        [StaFact]
        public void Switching_provider_drops_the_old_providers_key()
        {
            var (_, controls) = BuildPage(SettingsWith(MakeAgent("Claude", apiKey: "sk-anthropic")));
            string? refusal = null;
            controls.EditorHost = e =>
            {
                e.ProviderCombo.SelectedIndex = 2;   // Anthropic → OpenAI, no key typed
                Assert.Contains("belongs to the previous provider", e.KeyHint.Text);
                refusal = e.TryAccept();
                return refusal == null;
            };

            controls.TryEditSelectedAgent();

            Assert.NotNull(refusal);   // OpenAI needs a key; the Anthropic one does not carry over
            Assert.Equal("anthropic", controls.WorkingAgents[0].Provider);   // nothing applied
        }

        [StaFact]
        public void A_local_provider_hides_the_key_row()
        {
            var editor = EditorFor(MakeAgent("Local", provider: "ollama", model: "llama3.1:8b", apiKey: ""));

            Assert.Equal(Visibility.Collapsed, editor.KeyRow.Visibility);

            editor.ProviderCombo.SelectedIndex = 1;   // Anthropic
            Assert.Equal(Visibility.Visible, editor.KeyRow.Visibility);
        }

        [StaFact]
        public void The_key_box_is_masked_until_shown_and_mirrors_both_ways()
        {
            var editor = EditorFor(MakeAgent("Claude"));

            Assert.Equal(Visibility.Visible, editor.KeyMask.Visibility);
            Assert.Equal(Visibility.Collapsed, editor.KeyBox.Visibility);
            Click(editor.ShowKeyButton);
            Assert.Equal(Visibility.Collapsed, editor.KeyMask.Visibility);
            Assert.Equal(Visibility.Visible, editor.KeyBox.Visibility);
            Click(editor.ShowKeyButton);
            Assert.Equal(Visibility.Visible, editor.KeyMask.Visibility);

            editor.KeyMask.Password = "sk-typed";
            Assert.Equal("sk-typed", editor.KeyBox.Text);
            editor.KeyBox.Text = "sk-bound";
            Assert.Equal("sk-bound", editor.KeyMask.Password);
        }

        [StaFact]
        public void The_key_help_is_collapsed_until_its_link_is_clicked()
        {
            var editor = EditorFor(MakeAgent("Claude"));
            Assert.Equal(Visibility.Collapsed, editor.KeyHelp.Visibility);

            Click(editor.KeyHelpLink);
            Assert.Equal(Visibility.Visible, editor.KeyHelp.Visibility);

            Click(editor.KeyHelpLink);
            Assert.Equal(Visibility.Collapsed, editor.KeyHelp.Visibility);
        }
    }
}
