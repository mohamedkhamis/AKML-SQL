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
