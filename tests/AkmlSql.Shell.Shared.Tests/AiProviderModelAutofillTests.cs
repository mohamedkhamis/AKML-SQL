#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Dialogs.Pages;
using Xunit;
using static AkmlSql.Shell.Shared.Tests.AiPageTestKit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// The agent dialog (Options › AI assistance › Add… / Edit…): switching the provider must
    /// correct an obviously foreign model name. The free-text Model box kept "claude-sonnet-5"
    /// when the provider was switched to Gemini, so the engine sent a Claude model to Google's
    /// API and chat surfaced a raw 404. A recognisably foreign (or empty) model is replaced with
    /// the new provider's default; custom/unrecognised names are the user's business. Also
    /// pinned: every provider round-trips by canonical id (spec 036, FR-013), legacy spellings
    /// load into the right entry, and an undecryptable flat key survives (PR #251 finding 2).
    /// </summary>
    public class AiProviderModelAutofillTests
    {
        private const int AnthropicIndex = 1;   // (None), Anthropic, OpenAI, Azure OpenAI, Gemini, Kimi, Ollama, LM Studio, Custom
        private const int GeminiIndex = 4;
        private const int KimiIndex = 5;

        private static AiAgent Agent(string provider, string model) => new AiAgent
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Agent 1",
            Provider = provider,
            Model = model,
        };

        [StaFact]
        public void Switching_provider_replaces_a_foreign_model_with_the_new_default()
        {
            var editor = EditorFor(Agent("anthropic", "claude-sonnet-5"));

            editor.ProviderCombo.SelectedIndex = GeminiIndex;

            Assert.Equal("gemini-flash-latest", editor.ModelBox.Text);
        }

        [StaFact]
        public void Switching_provider_fills_an_empty_model_with_the_default()
        {
            var editor = EditorFor(Agent("", ""));

            editor.ProviderCombo.SelectedIndex = AnthropicIndex;

            Assert.Equal("claude-sonnet-4-6", editor.ModelBox.Text);
        }

        [StaFact]
        public void Switching_provider_preserves_an_unrecognised_custom_model()
        {
            var editor = EditorFor(Agent("anthropic", "my-fine-tuned-model"));

            editor.ProviderCombo.SelectedIndex = GeminiIndex;

            Assert.Equal("my-fine-tuned-model", editor.ModelBox.Text);
        }

        [StaFact]
        public void Opening_a_mismatched_agent_does_not_silently_rewrite_it()
        {
            // Binding the draft sets the provider combo, which raises SelectionChanged; the stored
            // model — even a mismatched one — must survive unless the USER switches provider.
            var editor = EditorFor(Agent("gemini", "claude-sonnet-5"));
            Assert.Equal("claude-sonnet-5", editor.ModelBox.Text);

            var settings = new AppSettings();
            settings.Ai.Provider = "Gemini";
            settings.Ai.Model = "claude-sonnet-5";
            var (dialog, _) = BuildPage(settings);
            Assert.Equal("claude-sonnet-5", dialog.GetSettings().Ai.Model);
        }

        // Spec 036 (US2, FR-010/FR-013, T028) — every list entry must round-trip select → save →
        // load, and Save must write the canonical id the factory accepts.
        [StaFact]
        public void Every_provider_round_trips_through_save_and_load()
        {
            var cases = new (int Index, string Id)[]
            {
                (1, "anthropic"),
                (2, "openai"),
                (3, "azure"),
                (4, "gemini"),
                (5, "kimi"),
                (6, "ollama"),
                (7, "lmstudio"),
                (8, "custom"),
            };

            foreach (var (index, id) in cases)
            {
                var (dialog, controls) = BuildPage(new AppSettings());
                AcceptNextEditor(controls, e =>
                {
                    e.ProviderCombo.SelectedIndex = index;
                    if (e.ModelBox.Text.Length == 0) e.ModelBox.Text = "test-model";
                    e.KeyBox.Text = "sk-test";
                    e.EndpointBox.Text = "https://ai.example.com";
                });
                controls.TryAddAgent();

                var saved = dialog.GetSettings();
                Assert.True(saved.Ai.Provider == id,
                    $"Save for list index {index} wrote '{saved.Ai.Provider}', expected canonical id '{id}'.");
                Assert.True(saved.Ai.Enabled, $"Adding a usable agent for index {index} must enable AI.");

                var (reopened, reopenedControls) = BuildPage(saved);
                var shownIndex = -1;
                reopenedControls.EditorHost = e => { shownIndex = e.ProviderCombo.SelectedIndex; return false; };
                reopenedControls.TryEditSelectedAgent();
                Assert.True(shownIndex == index, $"Reload of provider '{id}' selected index {shownIndex}, expected {index}.");
                Assert.Equal(id, reopened.GetSettings().Ai.Provider);
            }
        }

        [StaTheory]
        [InlineData("AzureOpenAI", 3)]   // written by builds before FR-013
        [InlineData("azure", 3)]
        [InlineData("LMStudio", 7)]      // written by builds before FR-013
        [InlineData("lmstudio", 7)]
        [InlineData("Kimi (Moonshot)", 5)]
        [InlineData("moonshot", 5)]
        [InlineData("", 0)]
        [InlineData(null, 0)]
        public void Opening_an_agent_normalises_legacy_and_display_spellings(string? stored, int expectedIndex)
        {
            var editor = EditorFor(Agent(stored ?? string.Empty, "m"));

            Assert.Equal(expectedIndex, editor.ProviderCombo.SelectedIndex);
        }

        [StaFact]
        public void A_legacy_flat_provider_loads_as_a_canonical_agent()
        {
            var settings = new AppSettings();
            settings.Ai.Provider = "AzureOpenAI";
            settings.Ai.Model = "gpt-4o";
            var (_, controls) = BuildPage(settings);

            Assert.Equal("azure", Assert.Single(controls.WorkingAgents).Provider);
        }

        [StaFact]
        public void Switching_to_kimi_preserves_an_unrecognised_custom_model()
        {
            var editor = EditorFor(Agent("openai", "my-fine-tuned-model"));

            editor.ProviderCombo.SelectedIndex = KimiIndex;

            Assert.Equal("my-fine-tuned-model", editor.ModelBox.Text);
        }

        [StaFact]
        public void Switching_to_kimi_fills_the_default_model()
        {
            var editor = EditorFor(Agent("", ""));

            editor.ProviderCombo.SelectedIndex = KimiIndex;

            Assert.Equal("kimi-latest", editor.ModelBox.Text);
        }

        // ── PR #251 review finding 2: an undecryptable stored key must be VISIBLE and must never
        // be silently blanked by a save the user did not touch the key in. ──

        private static string UndecryptableKey() => "dpapi:" + Convert.ToBase64String(new byte[64]);

        [StaFact]
        public void Undecryptable_stored_key_shows_notice_and_survives_save()
        {
            var settings = new AppSettings();
            settings.Ai.Provider = "openai";
            settings.Ai.Model = "gpt-4o";
            settings.Ai.ApiKey = UndecryptableKey();
            var (dialog, controls) = BuildPage(settings);

            Border? notice = null;
            controls.EditorHost = e => { notice = e.KeyNotice; return false; };
            controls.TryEditSelectedAgent();

            Assert.Equal(Visibility.Visible, notice!.Visibility);
            Assert.Contains("could not be decrypted", ((TextBlock)notice.Child).Text);
            Assert.Equal(settings.Ai.ApiKey, dialog.GetSettings().Ai.ApiKey);
        }

        [StaFact]
        public void After_decrypt_failure_typing_a_new_key_saves_normally()
        {
            var settings = new AppSettings();
            settings.Ai.Provider = "openai";
            settings.Ai.Model = "gpt-4o";
            settings.Ai.ApiKey = UndecryptableKey();
            var (dialog, controls) = BuildPage(settings);

            AcceptNextEditor(controls, e =>
            {
                e.KeyBox.Text = "sk-user-retyped";
                Assert.Equal(Visibility.Collapsed, e.KeyNotice.Visibility);
            });
            controls.TryEditSelectedAgent();

            var saved = dialog.GetSettings();
            Assert.True(ApiKeyProtector.IsProtected(saved.Ai.ApiKey), "the re-entered key must be wrapped, not stored plaintext");
            Assert.Equal("sk-user-retyped", ApiKeyProtector.Unprotect(saved.Ai.ApiKey));
        }
    }
}
