#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Dialogs.Pages;
using AkmlSql.Shell.Shared.Ui.Theme;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Design B (2026-10-13): a <see cref="TabbedPage"/> is a <see cref="CombinedPage"/> whose
    /// sections are grouped into <see cref="OptionsTabStrip"/> tabs. Sections keep their keys,
    /// controls and search entries; only where their rows land changes.
    /// </summary>
    public class TabbedPageTests
    {
        private sealed class StubSection : IPageBuilder
        {
            public StubSection(string key) { Key = key; }
            public string Key { get; }
            public string Display => Key;
            public string Title => Key;
            public string HelpTopic => "topics/options#stub";
            public string Help => "";
            public CheckBox? Built { get; private set; }
            public IPageControls Build(StackPanel panel, PageContext ctx)
            {
                var (_, chk) = ctx.Rows.AddToggle(panel, Key + " toggle", "");
                Built = chk;
                return new StubControls();
            }
        }

        private sealed class StubControls : IPageControls
        {
            public void Load(AppSettings settings) { }
            public void Save(AppSettings settings) { }
            public void Reset(AppSettings defaults) { }
        }

        private sealed class TwoTabPage : TabbedPage
        {
            public TwoTabPage(IPageBuilder a, IPageBuilder b, IPageBuilder c)
                : base(("First", new[] { a, b }), ("Second", new[] { c })) { }
            public override string Key => "TwoTab";
            public override string Display => "Two tab";
            public override string Title => "Two tab";
            public override string HelpTopic => "topics/options#stub";
            public override string Help => "";
        }

        private static PageContext Context() =>
            new PageContext(PageTheme.Light, new AppSettings(), new RowFactory(PageTheme.Light), (_, _, _, _) => { });

        [StaFact]
        public void Sections_build_into_their_tabs_panels_in_order()
        {
            var a = new StubSection("A"); var b = new StubSection("B"); var c = new StubSection("C");
            var page = new TwoTabPage(a, b, c);
            var panel = new StackPanel();

            var built = page.BuildSections(panel, Context());

            Assert.Equal(new[] { "A", "B", "C" }, built.Select(s => s.Key));
            Assert.Equal(new[] { "A", "B", "C" }, page.Sections.Select(s => s.Key));
            Assert.NotNull(page.Strip);
            Assert.Equal(2, page.Strip!.Panels.Count);
            Assert.Contains(a.Built!, LogicalTree.Descendants<CheckBox>(page.Strip.Panels[0]));
            Assert.Contains(b.Built!, LogicalTree.Descendants<CheckBox>(page.Strip.Panels[0]));
            Assert.Contains(c.Built!, LogicalTree.Descendants<CheckBox>(page.Strip.Panels[1]));
            Assert.Equal("First tab", System.Windows.Automation.AutomationProperties.GetName(page.Strip.Buttons[0]));
        }

        [StaFact]
        public void The_strip_opens_on_the_first_tab_and_hides_the_rest()
        {
            var page = new TwoTabPage(new StubSection("A"), new StubSection("B"), new StubSection("C"));
            page.BuildSections(new StackPanel(), Context());

            Assert.Equal(0, page.Strip!.SelectedIndex);
            Assert.Equal(Visibility.Visible, page.Strip.Panels[0].Visibility);
            Assert.Equal(Visibility.Collapsed, page.Strip.Panels[1].Visibility);
        }
    }
}
