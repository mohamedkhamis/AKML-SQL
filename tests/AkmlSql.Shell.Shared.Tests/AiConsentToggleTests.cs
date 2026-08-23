using System.Collections;
using System.Reflection;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Dialogs;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Pins the cloud-AI consent toggle on the Options › AI Assistance page. The engine refuses to
    /// send prompts/schema to a non-local provider until <see cref="AiSettings.PrivacyConsentRequired"/>
    /// is cleared (it throws "CONSENT_REQUIRED: Data will be sent to your AI provider. Please confirm
    /// in settings."). Before this control there was NO UI that wrote the flag, so the message pointed
    /// at a nonexistent setting. These tests prove the control exists on the page and round-trips the
    /// flag (checkbox shows "consent granted", i.e. the inverse of "consent required").
    /// </summary>
    public class AiConsentToggleTests
    {
        [StaFact]
        public void ConsentToggle_IsRegisteredOnTheAiAssistancePage()
        {
            var dialog = new SettingsWindow(new AppSettings());
            _ = dialog.TestBuildWindowForRenderTest();

            // A vacuous value round-trip would pass even if no control owned the setting; asserting
            // the search index attributes the row to the AI page proves the checkbox was really built.
            Assert.Equal("AI Assistance",
                PageKeyForSearchLabel(dialog, "Consent to cloud AI data sharing"));
        }

        [StaFact]
        public void ConsentGranted_RoundTripsAsNotRequired()
        {
            // Consent granted in config (PrivacyConsentRequired = false) → checkbox checked on Load →
            // Save writes it straight back.
            var settings = new AppSettings();
            settings.Ai.PrivacyConsentRequired = false;

            var dialog = new SettingsWindow(settings);
            _ = dialog.TestBuildWindowForRenderTest();

            Assert.False(dialog.GetSettings().Ai.PrivacyConsentRequired);
        }

        [StaFact]
        public void DefaultConsentWithheld_RoundTripsAsRequired()
        {
            // Privacy-first default: a fresh install requires consent (PrivacyConsentRequired = true),
            // the checkbox loads unchecked, and Save preserves the gate.
            var settings = new AppSettings();
            Assert.True(settings.Ai.PrivacyConsentRequired); // guard the default

            var dialog = new SettingsWindow(settings);
            _ = dialog.TestBuildWindowForRenderTest();

            Assert.True(dialog.GetSettings().Ai.PrivacyConsentRequired);
        }

        private static string? PageKeyForSearchLabel(SettingsWindow dialog, string label)
        {
            var f = typeof(SettingsWindow).GetField("_searchIndex", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(f);
            var index = (IEnumerable)f!.GetValue(dialog)!;

            foreach (var entry in index)
            {
                var t = entry.GetType();
                var entryLabel = (string)t.GetProperty("Label")!.GetValue(entry)!;
                if (entryLabel == label)
                    return (string)t.GetProperty("PageKey")!.GetValue(entry)!;
            }
            return null;
        }
    }
}
