#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Linq;
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
    /// Spec 033 (T037 / US4) — the Options Format › Styles page is the SQL Prompt-exact
    /// launcher: it carries the "Edit formatting styles…" button and a "Behavior" group, and
    /// <c>RefreshActiveStyleFromDisk</c> re-seeds the dropdown from config so the Options OK
    /// path persists a Set-Active done inside the styles window instead of clobbering it.
    /// Runs under an isolated AKML_APP_DATA_ROOT (shared serialization collection).
    /// </summary>
    [Collection("AkmlSql AppData isolation")]
    public sealed class FormattingPageTests : AppDataIsolatedTest
    {
        public FormattingPageTests() : base("akmlsql-formattingpage-test-") { }

        private static (SettingsWindow Dialog, UIElement Page, FormattingControls Controls) BuildFormattingPage(AppSettings settings)
        {
            var dialog = new SettingsWindow(settings);
            _ = dialog.TestBuildWindowForRenderTest();

            var pages = (IDictionary)typeof(SettingsWindow)
                .GetField("_pages", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(dialog)!;
            var page = (UIElement)pages["Formatting"]!;

            var controlsByKey = (IDictionary)typeof(SettingsWindow)
                .GetField("_pageControlsByKey", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(dialog)!;
            var controls = (FormattingControls)controlsByKey["Formatting"]!;

            return (dialog, page, controls);
        }

        [StaFact]
        public void Page_contains_edit_styles_button_and_behavior_header()
        {
            var (_, page, _) = BuildFormattingPage(new AppSettings());

            var buttons = LogicalTree.Descendants<Button>(page).ToList();
            Assert.Contains(buttons, b => b.Content as string == "Edit formatting styles…");

            var headers = LogicalTree.Descendants<TextBlock>(page).Select(t => t.Text).ToList();
            Assert.Contains("Behavior", headers);
        }

        [StaFact]
        public void RefreshActiveStyleFromDisk_reseeds_and_save_persists_the_fresh_name()
        {
            // Options opened while "Old Choice" was active…
            var settings = new AppSettings();
            settings.Formatter.ActiveProfile = "Old Choice";
            var (dialog, _, controls) = BuildFormattingPage(settings);

            // …then the styles window (modal) set a different active style on disk…
            var onDisk = ConfigManager.Load();
            onDisk.Formatter.ActiveProfile = "Window Choice";
            ConfigManager.Save(onDisk);

            // …the post-close refresh re-seeds from disk (engine disconnected → seed only)…
            controls.RefreshActiveStyleFromDisk();

            // …and OK'ing Options persists the fresh name — no clobber (US4 scenario 3).
            var saved = dialog.GetSettings();
            Assert.Equal("Window Choice", saved.Formatter.ActiveProfile);
        }
    }
}
