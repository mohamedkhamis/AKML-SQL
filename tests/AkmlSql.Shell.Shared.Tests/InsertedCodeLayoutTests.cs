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
    /// Design B (2026-10-13): Inserted code is three tabs — Statements (INSERT/EXEC expansion
    /// with schema qualification, bracket policy and function parentheses), Aliases, and Closing
    /// characters. Bracket identifiers and Add parentheses moved from the Special characters
    /// section to Qualification; they keep saving to the same settings.
    /// </summary>
    public class InsertedCodeLayoutTests
    {
        [StaFact]
        public void The_page_has_statements_aliases_and_closing_characters_tabs()
        {
            var (dialog, _) = Build(new AppSettings());
            var page = Assert.IsAssignableFrom<TabbedPage>(Builders(dialog)["InsertedCode"]);

            Assert.Equal(new[] { "Statements", "Aliases", "Closing characters" }, page.Tabs.Select(t => t.Label));
            Assert.Equal(new[] { "InsertOptions", "Qualification" }, page.Tabs[0].Sections.Select(s => s.Key));
            Assert.Equal(new[] { "Aliases" }, page.Tabs[1].Sections.Select(s => s.Key));
            Assert.Equal(new[] { "SpecialCharacters" }, page.Tabs[2].Sections.Select(s => s.Key));
        }

        [StaFact]
        public void Bracket_policy_and_function_parentheses_save_through_qualification()
        {
            var settings = new AppSettings();
            settings.IntelliSense.Qualification.BracketMode = BracketMode.Never;
            settings.IntelliSense.SpecialCharOptions.AddParentheses = false;
            var (dialog, controls) = Build(settings);
            var qualification = (QualificationControls)controls["Qualification"];

            Assert.Equal(2, qualification.BracketMode.SelectedIndex);
            Assert.False(qualification.AddParentheses.IsChecked);

            qualification.BracketMode.SelectedIndex = 0;
            qualification.AddParentheses.IsChecked = true;
            var saved = dialog.GetSettings();

            Assert.Equal(BracketMode.Always, saved.IntelliSense.Qualification.BracketMode);
            Assert.True(saved.IntelliSense.SpecialCharOptions.AddParentheses);
        }

        [StaFact]
        public void Closing_characters_round_trip_on_the_third_tab()
        {
            var settings = new AppSettings();
            settings.IntelliSense.SpecialCharOptions.CloseDoubleQuote = false;
            var (dialog, controls) = Build(settings);
            var page = (TabbedPage)Builders(dialog)["InsertedCode"];
            var closing = (SpecialCharactersControls)controls["SpecialCharacters"];

            Assert.Contains(closing.CloseDoubleQuote, LogicalTree.Descendants<CheckBox>(page.Strip!.Panels[2]));
            Assert.False(closing.CloseDoubleQuote.IsChecked);

            closing.CloseDoubleQuote.IsChecked = true;
            Assert.True(dialog.GetSettings().IntelliSense.SpecialCharOptions.CloseDoubleQuote);
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
