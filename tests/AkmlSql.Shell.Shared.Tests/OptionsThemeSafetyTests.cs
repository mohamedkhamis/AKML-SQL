#nullable enable
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Dialogs;
using AkmlSql.Shell.Shared.Dialogs.Pages;
using AkmlSql.Shell.Shared.Ui.Theme;
using Xunit;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T007, OPT-02, FR-004) — the Theme drop-down never saves anything by itself.
    /// Before the fix, opening Options with "System" on a dark host, Restore all defaults, or an
    /// Import all raised the drop-down's SelectionChanged, which saved half-loaded settings to
    /// disk and reopened the window.
    /// </summary>
    [Collection("AkmlSql ThemeRegistry")]
    public class OptionsThemeSafetyTests : AppDataIsolatedTest
    {
        private readonly System.Collections.Generic.List<string> _themeCalls = new System.Collections.Generic.List<string>();

        public OptionsThemeSafetyTests() : base("akml-theme-safety-")
        {
            Commands.OptionsCommand.ThemePreferenceOverride = _themeCalls.Add;
        }

        public override void Dispose()
        {
            HostThemeWatcher.VariantOverrideForTests = null;
            Commands.OptionsCommand.ThemePreferenceOverride = null;
            base.Dispose();
        }

        private static byte[] SaveAndRead(AppSettings settings)
        {
            ConfigManager.Save(settings);
            return File.ReadAllBytes(Constants.ConfigFilePath);
        }

        private static byte[] ConfigBytes() => File.ReadAllBytes(Constants.ConfigFilePath);

        private static IDictionary PageControls(SettingsWindow dialog) =>
            (IDictionary)typeof(SettingsWindow)
                .GetField("_pageControlsByKey", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(dialog)!;

        [StaFact]
        public void System_theme_on_a_dark_host_opens_without_saving_or_reopening()
        {
            HostThemeWatcher.VariantOverrideForTests = () => ThemeVariant.Dark;
            var before = SaveAndRead(new AppSettings { Theme = "system" });

            var dialog = new SettingsWindow(ConfigManager.Load());
            _ = dialog.TestBuildWindowForRenderTest();

            Assert.False(dialog.ThemeChangeRequested);
            Assert.Same(PageTheme.Dark, SettingsWindow.ResolvePageTheme("system"));
            Assert.Equal(before, ConfigBytes());
        }

        [StaFact]
        public void Restore_all_then_discard_leaves_the_config_unchanged()
        {
            var before = SaveAndRead(new AppSettings { Theme = "dark" });

            var dialog = new SettingsWindow(ConfigManager.Load());
            _ = dialog.TestBuildWindowForRenderTest();
            dialog.ResetAllToDefaultsCore();

            Assert.False(dialog.ThemeChangeRequested);
            Assert.Equal(before, ConfigBytes());
        }

        [StaFact]
        public void Importing_a_different_theme_writes_nothing()
        {
            var before = SaveAndRead(new AppSettings { Theme = "dark" });

            var dialog = new SettingsWindow(ConfigManager.Load());
            _ = dialog.TestBuildWindowForRenderTest();
            dialog.ImportSettings(new AppSettings { Theme = "light" });

            Assert.False(dialog.ThemeChangeRequested);
            Assert.Equal("light", dialog.WorkingCopy.Theme);
            Assert.Equal(before, ConfigBytes());
        }

        [StaFact]
        public void Picking_light_keeps_unsaved_edits_in_the_working_copy_and_writes_nothing()
        {
            var before = SaveAndRead(new AppSettings { Theme = "dark" });

            var dialog = new SettingsWindow(ConfigManager.Load());
            var window = dialog.TestBuildWindowForRenderTest("IntelliSense");

            // An unsaved edit on another page.
            var nullability = PageControls(dialog)["IntelliSense"]!;
            var box = (CheckBox)nullability.GetType()
                .GetField("_showNullability", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(nullability)!;
            box.IsChecked = false;

            var general = (GeneralControls)PageControls(dialog)["General"]!;
            general.Theme.SelectedIndex = 1; // Light

            Assert.True(dialog.ThemeChangeRequested);
            Assert.False(dialog.WorkingCopy.IntelliSense.ShowNullability);
            Assert.Equal("light", dialog.WorkingCopy.Theme);
            Assert.Equal("SuggestionsBehavior", dialog.CurrentPageKey); // the page showing IntelliSense
            Assert.Equal(new[] { "light" }, _themeCalls); // live preview only; nothing saved
            Assert.Equal(before, ConfigBytes());
        }
    }
}
