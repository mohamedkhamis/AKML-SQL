#nullable enable
using System.Collections.Generic;
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
    /// Design B (2026-10-13): Suggestions › Behavior is two tabs. Completion holds the
    /// IntelliSense rows (with the object definition box, once its own "Tooltips" page); JOIN
    /// assistance holds the JOIN clause, alias and join-condition rows (the former JOIN rows of
    /// IntelliSense and the "Join conditions" page), still greyed by Enable IntelliSense.
    /// </summary>
    public class SuggestionsBehaviorLayoutTests
    {
        [StaFact]
        public void The_page_has_a_completion_tab_and_a_join_assistance_tab()
        {
            var (dialog, _) = Build(new AppSettings());
            var page = Assert.IsAssignableFrom<TabbedPage>(Builders(dialog)["SuggestionsBehavior"]);

            Assert.Equal(new[] { "Completion", "JOIN assistance" }, page.Tabs.Select(t => t.Label));
            Assert.Equal(new[] { "IntelliSense" }, page.Tabs[0].Sections.Select(s => s.Key));
            Assert.Equal(new[] { "JoinOptions" }, page.Tabs[1].Sections.Select(s => s.Key));
        }

        [StaFact]
        public void The_definition_box_toggle_lives_with_completion_and_saves_its_old_setting()
        {
            var settings = new AppSettings();
            settings.CompletionPolish.ShowObjectDefinitionBox = false;
            var (dialog, controls) = Build(settings);
            var intelliSense = (IntelliSenseControls)controls["IntelliSense"];

            Assert.False(intelliSense.ShowDefinitionBox.IsChecked);
            intelliSense.ShowDefinitionBox.IsChecked = true;

            Assert.True(dialog.GetSettings().CompletionPolish.ShowObjectDefinitionBox);
            Assert.False(controls.ContainsKey("CompletionPolish"));
        }

        [StaFact]
        public void The_join_rows_sit_on_the_second_tab_and_round_trip()
        {
            var settings = new AppSettings();
            settings.IntelliSense.JoinAssist = false;
            settings.IntelliSense.AutoAlias = true;
            settings.IntelliSense.JoinOptions.MatchByColumnName = false;
            var (dialog, controls) = Build(settings);
            var page = (TabbedPage)Builders(dialog)["SuggestionsBehavior"];
            var join = (JoinAssistanceControls)controls["JoinOptions"];

            Assert.Contains(join.JoinAssist, LogicalTree.Descendants<CheckBox>(page.Strip!.Panels[1]));
            Assert.False(join.JoinAssist.IsChecked);
            Assert.True(join.AutoAlias.IsChecked);
            Assert.False(join.MatchByColumnName.IsChecked);

            join.JoinAssist.IsChecked = true;
            join.AutoAlias.IsChecked = false;
            join.MatchByColumnName.IsChecked = true;
            var saved = dialog.GetSettings();

            Assert.True(saved.IntelliSense.JoinAssist);
            Assert.False(saved.IntelliSense.AutoAlias);
            Assert.True(saved.IntelliSense.JoinOptions.MatchByColumnName);
        }

        [StaFact]
        public void Enable_IntelliSense_still_greys_the_join_rows_on_the_other_tab()
        {
            var (_, controls) = Build(new AppSettings());
            var intelliSense = (IntelliSenseControls)controls["IntelliSense"];
            var join = (JoinAssistanceControls)controls["JoinOptions"];

            intelliSense.Enabled.IsChecked = false;
            Assert.All(join.Rows, row => Assert.False(row.IsEnabled));

            intelliSense.Enabled.IsChecked = true;
            Assert.All(join.Rows, row => Assert.True(row.IsEnabled));
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static (SettingsWindow Dialog, Dictionary<string, IPageControls> Controls) Build(AppSettings settings)
        {
            var dialog = new SettingsWindow(settings);
            _ = dialog.TestBuildWindowForRenderTest();
            var f = typeof(SettingsWindow).GetField("_pageControlsByKey", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (dialog, (Dictionary<string, IPageControls>)f.GetValue(dialog)!);
        }

        private static Dictionary<string, IPageBuilder> Builders(SettingsWindow dialog) =>
            (Dictionary<string, IPageBuilder>)typeof(SettingsWindow)
                .GetField("_pageBuilders", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
    }
}
