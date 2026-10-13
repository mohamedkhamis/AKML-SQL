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
    /// Design B (2026-10-13): General gains the SQL Server connection rows (once under
    /// Suggestions › Lists &amp; connections), Snippets is its own tree leaf, and Suggestions ›
    /// Lists &amp; scope keeps what the suggestions box lists and where it looks.
    /// </summary>
    public class GeneralLayoutTests
    {
        [StaFact]
        public void General_is_a_combined_page_of_general_and_sql_server_connections()
        {
            var (dialog, controls) = Build(new AppSettings());
            var page = Assert.IsAssignableFrom<CombinedPage>(Builders(dialog)["GeneralAll"]);

            Assert.Equal("General", page.Display);
            Assert.Equal(new[] { "General", "ConnectionsMemory" }, page.Sections.Select(s => s.Key));
            Assert.Equal("General › SQL Server connections", Builders(dialog)["ConnectionsMemory"].Display);
            Assert.IsType<GeneralControls>(controls["General"]);
        }

        // The theme dropdown's reopen wiring on the (now combined) General page is covered by
        // OptionsThemeSafetyTests, which drives it through the host's preview hook.

        [StaFact]
        public void Sql_auth_credentials_round_trip_from_general()
        {
            var settings = new AppSettings();
            settings.IntelliSense.EnableSqlAuthCredentials = false;
            var (dialog, controls) = Build(settings);
            var connections = (ConnectionsMemoryControls)controls["ConnectionsMemory"];

            Assert.False(connections.EnableSqlAuthCredentials.IsChecked);
            connections.EnableSqlAuthCredentials.IsChecked = true;

            Assert.True(dialog.GetSettings().IntelliSense.EnableSqlAuthCredentials);
        }

        [StaFact]
        public void Snippets_is_its_own_page_and_lists_and_scope_holds_the_rest()
        {
            var (dialog, _) = Build(new AppSettings());
            var builders = Builders(dialog);

            Assert.Equal("Snippets", builders["Snippets"].Display);
            var lists = Assert.IsAssignableFrom<CombinedPage>(builders["SuggestionsLists"]);
            Assert.Equal("Suggestions › Lists & scope", lists.Display);
            Assert.Equal(new[] { "SuggestionTypes", "ConnectionScope" }, lists.Sections.Select(s => s.Key));
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
