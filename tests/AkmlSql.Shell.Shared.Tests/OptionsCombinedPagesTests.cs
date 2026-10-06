#nullable enable
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Dialogs;
using AkmlSql.Shell.Shared.Dialogs.Pages;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Options pages that held one to three settings are sections of combined pages
    /// (Pages/CombinedPages.cs). A section keeps its own key, controls and search entries: a link
    /// to it opens the page that shows it, Restore Defaults on that page resets every section, and
    /// the command palette still finds and changes its settings.
    /// </summary>
    [Collection("AkmlSql ThemeRegistry")]
    public class OptionsCombinedPagesTests
    {
        [StaTheory]
        [InlineData("IntelliSense", "SuggestionsBehavior")]
        [InlineData("CompletionPolish", "SuggestionsBehavior")]
        [InlineData("JoinOptions", "SuggestionsBehavior")]
        [InlineData("SuggestionTypes", "SuggestionsLists")]
        [InlineData("ConnectionScope", "SuggestionsLists")]
        [InlineData("ConnectionsMemory", "SuggestionsLists")]
        [InlineData("Snippets", "SuggestionsLists")]
        [InlineData("InsertOptions", "InsertedCode")]
        [InlineData("Qualification", "InsertedCode")]
        [InlineData("Aliases", "InsertedCode")]
        [InlineData("SpecialCharacters", "InsertedCode")]
        [InlineData("Grid", "ResultsExecution")]
        [InlineData("Execution", "ResultsExecution")]
        [InlineData("Editor", "EditorAll")]
        [InlineData("Refactoring", "EditorAll")]
        [InlineData("Navigation", "EditorAll")]
        public void A_link_to_a_section_opens_the_page_that_shows_it(string section, string page)
        {
            var dialog = new SettingsWindow(new AppSettings { Theme = "Light" });
            var window = dialog.TestBuildWindowForRenderTest(section);
            try
            {
                Assert.Equal(page, dialog.ShownPageKey(section));
                Assert.Equal(page, dialog.CurrentPageKey);
            }
            finally
            {
                window.Close();
            }
        }

        [StaFact]
        public void A_combined_page_shows_every_sections_settings()
        {
            var dialog = new SettingsWindow(new AppSettings { Theme = "Light" });
            var window = dialog.TestBuildWindowForRenderTest("InsertedCode");
            try
            {
                var texts = LogicalTree.Descendants<TextBlock>(window).Select(t => t.Text).ToList();
                Assert.Contains("Inserted code", texts);                 // the page band
                Assert.Contains("Insert column names", texts);           // Objects & statements
                Assert.Contains("Schema qualification", texts);          // Qualification
                Assert.Contains("Include the AS keyword", texts);        // Aliases
                Assert.Contains("Closing characters", texts);            // Special characters
            }
            finally
            {
                window.Close();
            }
        }

        [StaFact]
        public void Restore_defaults_on_a_combined_page_resets_every_section()
        {
            var settings = new AppSettings();
            settings.Snippets.Enabled = false;                                     // Snippets section
            settings.IntelliSense.ConnectionScope.IncludeLinkedServers = true;     // Connections section
            settings.Formatter.ActiveProfile = "Collapsed";                        // another page
            var dialog = new SettingsWindow(settings);
            var window = dialog.TestBuildWindowForRenderTest();
            try
            {
                dialog.ResetPageToDefaultsCore("SuggestionsLists");
                var s = dialog.GetSettings();

                Assert.True(s.Snippets.Enabled);
                Assert.False(s.IntelliSense.ConnectionScope.IncludeLinkedServers);
                Assert.Equal("Collapsed", s.Formatter.ActiveProfile);
                Assert.StartsWith("Reset the settings on Suggestions › Lists & connections?", dialog.ResetConfirmationText("SuggestionsLists"));
            }
            finally
            {
                window.Close();
            }
        }

        [StaFact]
        public void The_palette_finds_a_sections_setting_with_its_controls_and_page()
        {
            var catalog = SettingsWindow.BuildOptionsCatalog(new AppSettings());
            try
            {
                var snippets = Assert.Single(catalog, e => e.Label == "Enable snippets");
                Assert.Equal("Snippets", snippets.PageKey);
                Assert.Equal("Suggestions › Lists & connections", snippets.PageDisplay);
                Assert.NotNull(snippets.Controls);
                Assert.NotNull(snippets.Toggle);
            }
            finally
            {
                SettingsWindow.InvalidateOptionsCatalog();
            }
        }

        [StaFact]
        public void Every_section_is_shown_on_exactly_one_page()
        {
            var dialog = new SettingsWindow(new AppSettings { Theme = "Light" });
            var builders = (System.Collections.Generic.Dictionary<string, IPageBuilder>)typeof(SettingsWindow)
                .GetField("_pageBuilders", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(dialog)!;

            var sections = builders.Values.OfType<CombinedPage>().SelectMany(p => p.Sections.Select(s => s.Key)).ToList();
            Assert.Equal(sections.Count, sections.Distinct().Count());
            Assert.DoesNotContain(sections, s => builders[s] is CombinedPage);
        }

        /// <summary>Logical-tree walk (pages enter the tree once their leaf is selected).</summary>
        private static class LogicalTree
        {
            public static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
            {
                foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
                {
                    if (child is T t) yield return t;
                    foreach (var d in Descendants<T>(child)) yield return d;
                }
            }
        }
    }
}
