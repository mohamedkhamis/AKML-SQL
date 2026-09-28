#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Dialogs;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T008, OPT-03, FR-005/FR-006) — Restore Defaults on a page resets only what that
    /// page shows. Settings no page shows (rule overrides, connection aliases, severities) and
    /// unsaved edits on other pages survive every page reset.
    /// </summary>
    [Collection("AkmlSql ThemeRegistry")]
    public class OptionsPageResetTests
    {
        private static AppSettings Seeded()
        {
            var s = new AppSettings();
            s.CodeAnalysis.RuleOverrides["ST001"] = new RuleOverride { Enabled = false };
            s.Ai.Agents.Add(new AiAgent { Id = "agent-1", Name = "Sentinel", Provider = "anthropic", Model = "m", ApiKey = "k", Enabled = true });
            s.Ai.ActiveAgentId = "agent-1";
            s.Navigation.ConnectionAliases.Add(new ConnectionAliasEntry { ServerName = "srv", Alias = "Live" });
            s.Safety.EnvironmentSeverity["PRODUCTION"] = "block";
            s.Formatter.ActiveProfile = "Collapsed";
            return s;
        }

        private static IEnumerable<string> PageKeys()
        {
            var dialog = new SettingsWindow(new AppSettings());
            var builders = (IDictionary)typeof(SettingsWindow)
                .GetField("_pageBuilders", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(dialog)!;
            return builders.Keys.Cast<string>().ToList();
        }

        [StaFact]
        public void Every_page_reset_keeps_settings_other_pages_own()
        {
            foreach (var key in PageKeys())
            {
                var dialog = new SettingsWindow(Seeded());
                _ = dialog.TestBuildWindowForRenderTest();

                dialog.ResetPageToDefaultsCore(key);
                var s = dialog.GetSettings();

                Assert.False(s.CodeAnalysis.RuleOverrides["ST001"].Enabled);
                Assert.Single(s.Navigation.ConnectionAliases);
                Assert.Equal("block", s.Safety.EnvironmentSeverity["PRODUCTION"]);

                if (key == "Formatting")
                    Assert.Equal(new FormatterSettings().ActiveProfile, s.Formatter.ActiveProfile);
                else
                    Assert.Equal("Collapsed", s.Formatter.ActiveProfile);

                if (key == "AI Assistance")
                    Assert.Empty(s.Ai.Agents);
                else
                    Assert.Equal("Sentinel", Assert.Single(s.Ai.Agents).Name);
            }
        }

        [StaFact]
        public void A_page_reset_keeps_unsaved_edits_on_other_pages()
        {
            var dialog = new SettingsWindow(new AppSettings());
            _ = dialog.TestBuildWindowForRenderTest();

            var controls = (IDictionary)typeof(SettingsWindow)
                .GetField("_pageControlsByKey", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(dialog)!;
            var intelliSense = controls["IntelliSense"]!;
            var box = (CheckBox)intelliSense.GetType()
                .GetField("_showNullability", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(intelliSense)!;
            box.IsChecked = false;

            dialog.ResetPageToDefaultsCore("Grid");

            Assert.False(box.IsChecked);
            Assert.False(dialog.GetSettings().IntelliSense.ShowNullability);
        }

        [StaFact]
        public void A_page_reset_restores_that_pages_defaults()
        {
            var settings = new AppSettings();
            settings.Grid.RowNumbers = !new GridSettings().RowNumbers;
            var dialog = new SettingsWindow(settings);
            _ = dialog.TestBuildWindowForRenderTest();

            dialog.ResetPageToDefaultsCore("Grid");

            Assert.Equal(new GridSettings().RowNumbers, dialog.GetSettings().Grid.RowNumbers);
        }

        [StaFact]
        public void Confirmation_names_the_page_as_the_tree_shows_it()
        {
            var settings = new AppSettings();
            settings.Ai.Agents.Add(new AiAgent { Id = "a1", Name = "One" });
            settings.Ai.Agents.Add(new AiAgent { Id = "a2", Name = "Two" });
            var dialog = new SettingsWindow(settings);
            _ = dialog.TestBuildWindowForRenderTest();

            var tooltips = dialog.ResetConfirmationText("CompletionPolish");
            Assert.StartsWith("Reset the settings on Suggestions › Tooltips?", tooltips);
            Assert.DoesNotContain("CompletionPolish", tooltips);

            Assert.Contains("2 AI agents", dialog.ResetConfirmationText("AI Assistance"));
            Assert.Contains("default environments", dialog.ResetConfirmationText("Tabs & UI"));
        }
    }
}
