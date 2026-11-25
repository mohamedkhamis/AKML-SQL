#nullable enable
using System.Collections;
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
    /// Spec 047 (SSMS-001, contracts/analysis-display.md §2) — Options › Code analysis gets an "Advisory findings" row in
    /// the "Analysis engine" group: "Margin marker only" (<c>marker</c>) or "Underline in the text" (<c>underline</c>),
    /// bound to <c>codeAnalysis.squiggleStyle</c> and found by Options search. Runs under an isolated
    /// AKML_APP_DATA_ROOT (shared serialization collection).
    /// </summary>
    [Collection("AkmlSql AppData isolation")]
    public sealed class CodeAnalysisPageAdvisoryRowTests : AppDataIsolatedTest
    {
        public CodeAnalysisPageAdvisoryRowTests() : base("akmlsql-codeanalysis-advisory-test-") { }

        private const string PageKey = "Code Analysis";

        private static (SettingsWindow Dialog, UIElement Page, CodeAnalysisControls Controls) Build(AppSettings settings)
        {
            var dialog = new SettingsWindow(settings);
            _ = dialog.TestBuildWindowForRenderTest();

            var pages = (IDictionary)typeof(SettingsWindow)
                .GetField("_pages", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(dialog)!;
            var controlsByKey = (IDictionary)typeof(SettingsWindow)
                .GetField("_pageControlsByKey", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(dialog)!;
            return (dialog, (UIElement)pages[PageKey]!, (CodeAnalysisControls)controlsByKey[PageKey]!);
        }

        private static ComboBox AdvisoryCombo(UIElement page) => Assert.Single(LogicalTree.Descendants<ComboBox>(page));

        private static string? ItemText(object item) => item is ComboBoxItem c ? c.Content as string : item as string;

        [StaFact]
        public void The_analysis_engine_group_has_an_advisory_findings_dropdown()
        {
            var (_, page, _) = Build(new AppSettings());

            var texts = LogicalTree.Descendants<TextBlock>(page).Select(t => t.Text).ToList();
            Assert.Contains("Advisory findings", texts);
            Assert.True(texts.IndexOf("Advisory findings") < texts.IndexOf("Suppressing rules"),
                "the row belongs to the Analysis engine group");

            var items = AdvisoryCombo(page).Items.Cast<object>().Select(ItemText).ToArray();
            Assert.Equal(new[] { "Margin marker only", "Underline in the text" }, items);
        }

        [StaTheory]
        [InlineData("marker", 0)]
        [InlineData("underline", 1)]
        public void Load_shows_the_saved_choice(string style, int index)
        {
            var settings = new AppSettings();
            settings.CodeAnalysis.SquiggleStyle = style;

            var (_, page, _) = Build(settings);

            Assert.Equal(index, AdvisoryCombo(page).SelectedIndex);
        }

        [StaFact]
        public void Save_writes_the_choice_back()
        {
            var (dialog, page, _) = Build(new AppSettings());

            AdvisoryCombo(page).SelectedIndex = 1;

            Assert.Equal("underline", dialog.GetSettings().CodeAnalysis.SquiggleStyle);
        }

        [StaFact]
        public void Options_search_finds_the_row()
        {
            var (dialog, _, _) = Build(new AppSettings());

            var index = (IEnumerable)typeof(SettingsWindow)
                .GetField("_searchIndex", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(dialog)!;
            var pageKeys = index.Cast<object>()
                .Where(e => (string)e.GetType().GetProperty("Label")!.GetValue(e)! == "Advisory findings")
                .Select(e => (string)e.GetType().GetProperty("PageKey")!.GetValue(e)!)
                .ToList();
            Assert.Equal(new[] { PageKey }, pageKeys);
        }
    }
}
