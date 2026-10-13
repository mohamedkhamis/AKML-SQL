#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Dialogs;
using AkmlSql.Shell.Shared.Dialogs.Pages;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Design B (2026-10-13): Queries › Execution &amp; warnings is two tabs — Results &amp;
    /// execution (the grid and execution sections) and Warnings (the five execution warnings and
    /// the transaction reminder, which moved here from Suggestions › Warnings &amp; highlighting).
    /// </summary>
    public class QueriesLayoutTests
    {
        [StaFact]
        public void Execution_and_warnings_has_two_tabs_with_the_warnings_section_second()
        {
            var (dialog, _) = Build(new AppSettings());
            var page = Assert.IsAssignableFrom<TabbedPage>(Builders(dialog)["ResultsExecution"]);

            Assert.Equal("Queries › Execution & warnings", page.Display);
            Assert.Equal(new[] { "Results & execution", "Warnings" }, page.Tabs.Select(t => t.Label));
            Assert.Equal(new[] { "Grid", "Execution" }, page.Tabs[0].Sections.Select(s => s.Key));
            Assert.Equal(new[] { "Safety" }, page.Tabs[1].Sections.Select(s => s.Key));
        }

        [StaFact]
        public void A_deep_link_to_the_warnings_section_opens_the_queries_page()
        {
            var dialog = new SettingsWindow(new AppSettings());
            _ = dialog.TestBuildWindowForRenderTest("Safety");

            Assert.Equal("ResultsExecution", dialog.ShownPageKey("Safety"));
            Assert.Equal("ResultsExecution", dialog.CurrentPageKey);
        }

        [StaFact]
        public void The_warnings_still_round_trip_from_their_new_tab()
        {
            var settings = new AppSettings();
            settings.Safety.DeleteWithoutWhere = false;
            settings.Safety.TransactionReminderInterval = 120;
            var (dialog, controls) = Build(settings);
            var page = (TabbedPage)Builders(dialog)["ResultsExecution"];

            var rows = LogicalTree.Descendants<CheckBox>(page.Strip!.Panels[1]).ToList();
            Assert.NotEmpty(rows);
            Assert.True(controls.ContainsKey("Safety"));

            var saved = dialog.GetSettings();
            Assert.False(saved.Safety.DeleteWithoutWhere);
            Assert.Equal(120, saved.Safety.TransactionReminderInterval);
        }

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
