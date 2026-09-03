using System;
using System.Windows;
using System.Windows.Controls;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Dialogs;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Options › AI Assistance: switching the provider must correct an obviously foreign model
    /// name. The free-text Model box kept "claude-sonnet-5" when the provider was switched to
    /// Gemini, so the engine sent a Claude model to Google's API and chat surfaced a raw 404.
    /// A recognisably foreign (or empty) model is replaced with the new provider's default;
    /// custom/unrecognised names are the user's business and stay untouched.
    /// </summary>
    public class AiProviderModelAutofillTests
    {
        private const int AnthropicIndex = 1;   // page items: (None), Anthropic, OpenAI, Azure OpenAI, Gemini, ...
        private const int GeminiIndex = 4;

        [StaFact]
        public void Switching_provider_replaces_a_foreign_model_with_the_new_default()
        {
            var settings = new AppSettings();
            settings.Ai.Provider = "Anthropic";
            settings.Ai.Model = "claude-sonnet-5";

            var dialog = new SettingsWindow(settings);
            _ = dialog.TestBuildWindowForRenderTest();

            FindProviderCombo(dialog).SelectedIndex = GeminiIndex;

            Assert.Equal("gemini-flash-latest", dialog.GetSettings().Ai.Model);
        }

        [StaFact]
        public void Switching_provider_fills_an_empty_model_with_the_default()
        {
            var settings = new AppSettings();
            settings.Ai.Provider = "";
            settings.Ai.Model = "";

            var dialog = new SettingsWindow(settings);
            _ = dialog.TestBuildWindowForRenderTest();

            FindProviderCombo(dialog).SelectedIndex = AnthropicIndex;

            Assert.Equal("claude-sonnet-4-6", dialog.GetSettings().Ai.Model);
        }

        [StaFact]
        public void Switching_provider_preserves_an_unrecognised_custom_model()
        {
            var settings = new AppSettings();
            settings.Ai.Provider = "Anthropic";
            settings.Ai.Model = "my-fine-tuned-model";

            var dialog = new SettingsWindow(settings);
            _ = dialog.TestBuildWindowForRenderTest();

            FindProviderCombo(dialog).SelectedIndex = GeminiIndex;

            Assert.Equal("my-fine-tuned-model", dialog.GetSettings().Ai.Model);
        }

        [StaFact]
        public void Loading_a_mismatched_config_does_not_silently_rewrite_it()
        {
            // Load() sets the provider combo, which raises SelectionChanged; the stored model —
            // even a mismatched one — must survive the round-trip untouched unless the USER
            // switches provider. (The engine-side factory guard reports the mismatch instead.)
            var settings = new AppSettings();
            settings.Ai.Provider = "Gemini";
            settings.Ai.Model = "claude-sonnet-5";

            var dialog = new SettingsWindow(settings);
            _ = dialog.TestBuildWindowForRenderTest();

            Assert.Equal("claude-sonnet-5", dialog.GetSettings().Ai.Model);
        }

        // Spec 036 (US2, FR-010/FR-013, T028) — every list entry must round-trip select → save →
        // load and Save must write the canonical id the factory accepts. Azure OpenAI and
        // LM Studio fail this today: Save writes "AzureOpenAI"/"LMStudio", which the factory
        // rejects as unknown (research R8). One loop, eight entries — the positional coupling is
        // how the mismatch survived, so the whole list is pinned, not just the new Kimi entry.
        [StaFact]
        public void Every_provider_round_trips_through_save_and_load()
        {
            // (combo index, canonical id) in page-list order; index 0 is (None).
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
                var dialog = new SettingsWindow(new AppSettings());
                _ = dialog.TestBuildWindowForRenderTest();
                FindProviderCombo(dialog).SelectedIndex = index;

                var saved = dialog.GetSettings();
                Assert.True(saved.Ai.Provider == id,
                    $"Save for list index {index} wrote '{saved.Ai.Provider}', expected canonical id '{id}'.");
                Assert.True(saved.Ai.Enabled, $"Selecting index {index} must enable AI.");

                var reopened = new SettingsWindow(saved);
                _ = reopened.TestBuildWindowForRenderTest();

                Assert.True(FindProviderCombo(reopened).SelectedIndex == index,
                    $"Reload of provider '{id}' selected index {FindProviderCombo(reopened).SelectedIndex}, expected {index}.");
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
        public void Load_normalises_legacy_and_display_spellings(string? stored, int expectedIndex)
        {
            var settings = new AppSettings();
            settings.Ai.Provider = stored ?? string.Empty;

            var dialog = new SettingsWindow(settings);
            _ = dialog.TestBuildWindowForRenderTest();

            Assert.Equal(expectedIndex, FindProviderCombo(dialog).SelectedIndex);
        }

        [StaFact]
        public void Switching_to_kimi_preserves_an_unrecognised_custom_model()
        {
            var settings = new AppSettings();
            settings.Ai.Provider = "OpenAI";
            settings.Ai.Model = "my-fine-tuned-model";

            var dialog = new SettingsWindow(settings);
            _ = dialog.TestBuildWindowForRenderTest();

            FindProviderCombo(dialog).SelectedIndex = 5; // Kimi (Moonshot)

            Assert.Equal("my-fine-tuned-model", dialog.GetSettings().Ai.Model);
        }

        [StaFact]
        public void Switching_to_kimi_fills_the_default_model()
        {
            var settings = new AppSettings();
            settings.Ai.Provider = "";
            settings.Ai.Model = "";

            var dialog = new SettingsWindow(settings);
            _ = dialog.TestBuildWindowForRenderTest();

            FindProviderCombo(dialog).SelectedIndex = 5; // Kimi (Moonshot)

            Assert.Equal("kimi-latest", dialog.GetSettings().Ai.Model);
        }

        // ── PR #251 review finding 2: an undecryptable stored key (roamed profile, restored
        // backup, different machine) must be VISIBLE and must never be silently blanked by a
        // Save the user did not touch the key in. ──

        /// <summary>A syntactically valid dpapi: blob this user cannot decrypt (bad HMAC).</summary>
        private static string UndecryptableKey() =>
            "dpapi:" + Convert.ToBase64String(new byte[64]);

        [StaFact]
        public void Undecryptable_stored_key_shows_notice_and_survives_save()
        {
            var settings = new AppSettings();
            settings.Ai.Provider = "openai";
            settings.Ai.Model = "gpt-4o";
            settings.Ai.ApiKey = UndecryptableKey();

            var dialog = new SettingsWindow(settings);
            _ = dialog.TestBuildWindowForRenderTest();

            var controls = GetAiControls(dialog);
            var keyBox = GetField<TextBox>(controls, "_apiKey");
            var notice = GetField<Border>(controls, "_keyNotice");

            // The failure is visible: empty field + the inline notice telling the user to re-enter.
            Assert.Equal(string.Empty, keyBox.Text);
            Assert.Equal(Visibility.Visible, notice.Visibility);
            Assert.Contains("could not be decrypted", ((TextBlock)notice.Child).Text);

            // A Save the user never touched the key in must NOT overwrite the stored value.
            var saved = dialog.GetSettings();
            Assert.Equal(settings.Ai.ApiKey, saved.Ai.ApiKey);
        }

        [StaFact]
        public void After_decrypt_failure_typing_a_new_key_saves_normally()
        {
            var settings = new AppSettings();
            settings.Ai.Provider = "openai";
            settings.Ai.ApiKey = UndecryptableKey();

            var dialog = new SettingsWindow(settings);
            _ = dialog.TestBuildWindowForRenderTest();

            var controls = GetAiControls(dialog);
            var keyBox = GetField<TextBox>(controls, "_apiKey");
            var notice = GetField<Border>(controls, "_keyNotice");
            keyBox.Text = "sk-user-retyped"; // user took control of the field

            // The notice must go with the flag: "the stored value was left untouched" is false
            // the moment the user types, because Save now overwrites it (PR #251 review).
            Assert.Equal(Visibility.Collapsed, notice.Visibility);

            var saved = dialog.GetSettings();
            Assert.True(ApiKeyProtector.IsProtected(saved.Ai.ApiKey),
                "the re-entered key must be wrapped, not stored plaintext");
            Assert.Equal("sk-user-retyped", ApiKeyProtector.Unprotect(saved.Ai.ApiKey));
        }

        /// <summary>The AI Assistance page's controls object from the window's page map.</summary>
        private static object GetAiControls(SettingsWindow dialog)
        {
            var f = typeof(SettingsWindow).GetField("_pageControlsByKey",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(f);
            var pages = (System.Collections.Generic.Dictionary<string, AkmlSql.Shell.Shared.Dialogs.Pages.IPageControls>)f!.GetValue(dialog)!;
            Assert.True(pages.TryGetValue("AI Assistance", out var controls), "AI Assistance controls not found.");
            return controls!;
        }

        private static T GetField<T>(object instance, string name) where T : class
        {
            var f = instance.GetType().GetField(name,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(f);
            return (f!.GetValue(instance) as T)!;
        }

        /// <summary>The AI provider ComboBox: the only combo whose items include "(None)" and "Gemini".
        /// Pages live in the window's private <c>_pages</c> dictionary (only the active page is in
        /// the window's own tree), so search the AI page's element directly.</summary>
        private static ComboBox FindProviderCombo(SettingsWindow dialog)
        {
            var f = typeof(SettingsWindow).GetField("_pages",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(f);
            var pages = (System.Collections.Generic.Dictionary<string, UIElement>)f!.GetValue(dialog)!;
            Assert.True(pages.TryGetValue("AI Assistance", out var aiPage), "AI Assistance page not built.");

            foreach (var combo in Descendants(aiPage!))
            {
                if (combo.Items.Contains("(None)") && combo.Items.Contains("Gemini"))
                    return combo;
            }
            throw new Xunit.Sdk.XunitException("AI provider ComboBox not found on the AI Assistance page.");
        }

        private static System.Collections.Generic.IEnumerable<ComboBox> Descendants(DependencyObject node)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(node))
            {
                if (child is ComboBox cb) yield return cb;
                if (child is DependencyObject d)
                {
                    foreach (var nested in Descendants(d)) yield return nested;
                }
            }
        }
    }
}
