using System.Collections;
using System.Reflection;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Dialogs;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Drives the consolidated "Inserted Code › Special characters" Options pane
    /// (report §4 rec #1). SQL Prompt keeps bracket-identifiers, add-parentheses and
    /// auto-close-characters together on ONE pane; AKML had them scattered across the
    /// IntelliSense (Behavior) and Qualification pages. These tests pin that (a) a
    /// "SpecialCharacters" page is registered and produces controls, (b) its three
    /// settings round-trip through the dialog, and (c) they are relocated OFF the old
    /// pages (the search index now attributes them to SpecialCharacters).
    /// </summary>
    public class SpecialCharactersPageTests
    {
        [StaFact]
        public void SpecialCharactersPage_IsRegistered_AndRoundTripsItsThreeSettings()
        {
            var settings = new AppSettings();
            // Flip all three away from their defaults (true / true / WhenRequired).
            settings.IntelliSense.SpecialCharOptions.AddParentheses = false;
            settings.IntelliSense.SpecialCharOptions.AutoCloseCharacters = false;
            settings.IntelliSense.Qualification.BracketMode = BracketMode.Always;

            var dialog = new SettingsWindow(settings);
            _ = dialog.TestBuildWindowForRenderTest(); // builds pages + Load()s controls

            // (a) The consolidated page exists and produced an IPageControls.
            var controls = GetPrivateDictionary(dialog, "_pageControlsByKey");
            Assert.True(controls.Contains("SpecialCharacters"),
                "Expected a consolidated 'SpecialCharacters' page in _pageControlsByKey.");

            // (b) Saving the dialog writes the three values back unchanged.
            var saved = dialog.GetSettings(); // calls SaveControlsToSettings
            Assert.False(saved.IntelliSense.SpecialCharOptions.AddParentheses);
            Assert.False(saved.IntelliSense.SpecialCharOptions.AutoCloseCharacters);
            Assert.Equal(BracketMode.Always, saved.IntelliSense.Qualification.BracketMode);
        }

        [StaFact]
        public void SpecialCharacterSettings_AreRelocatedOffTheirOldPages()
        {
            var dialog = new SettingsWindow(new AppSettings());
            _ = dialog.TestBuildWindowForRenderTest();

            // The three consolidated settings must now be attributed to the
            // SpecialCharacters page in the search index, not their old homes.
            Assert.Equal("SpecialCharacters", PageKeyForSearchLabel(dialog, "Add parentheses after functions"));
            Assert.Equal("SpecialCharacters", PageKeyForSearchLabel(dialog, "Auto-close matching characters"));
            Assert.Equal("SpecialCharacters", PageKeyForSearchLabel(dialog, "Bracket identifiers"));
        }

        // ─── reflection helpers ──────────────────────────────────────────────

        private static IDictionary GetPrivateDictionary(SettingsWindow dialog, string field)
        {
            var f = typeof(SettingsWindow).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(f);
            return (IDictionary)f!.GetValue(dialog)!;
        }

        /// <summary>
        /// Returns the PageKey the search index attributes to the setting whose label
        /// matches <paramref name="label"/>, or null if that label is not indexed.
        /// </summary>
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
