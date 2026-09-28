#nullable enable
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Editor › Productivity. Spec 040 (OPT-01): "Named regions" and "Document Outline" changed
    /// nothing and are hidden; their saved values are kept.
    /// </summary>
    internal sealed class EditorPage : IPageBuilder
    {
        public string Key     => "Editor";
        public string Display => "Editor › Productivity";
        public string Title   => "Editor Productivity";
        public string Help    => "Toggle editor productivity aids: occurrence highlighting, bracket matching, sticky scroll and the code minimap.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            var (rowHl, chkHl) = ctx.Rows.AddToggle(panel,
                "Highlight occurrences", "Highlight all occurrences of selected identifier");
            ctx.RegisterSearch("Highlight occurrences", "Highlight all occurrences of selected identifier", "Toggle", rowHl);

            var (rowBracket, chkBracket) = ctx.Rows.AddToggle(panel,
                "Bracket matching", "Highlight matching BEGIN/END and parenthesis pairs");
            ctx.RegisterSearch("Bracket matching", "Highlight matching BEGIN/END and parenthesis pairs", "Toggle", rowBracket);

            var (rowSticky, chkSticky) = ctx.Rows.AddToggle(panel,
                "Sticky scroll", "Pin parent scope headers while scrolling");
            ctx.RegisterSearch("Sticky scroll", "Pin parent scope headers while scrolling", "Toggle", rowSticky);

            var (rowMinimap, chkMinimap) = ctx.Rows.AddToggle(panel,
                "Code minimap", "Show code minimap in editor margin");
            ctx.RegisterSearch("Code minimap", "Show code minimap in editor margin", "Toggle", rowMinimap);

            return new EditorControls(chkHl, chkBracket, chkSticky, chkMinimap);
        }
    }

    internal sealed class EditorControls : IPageControls
    {
        private readonly CheckBox _highlightOccurrences;
        private readonly CheckBox _bracketMatching;
        private readonly CheckBox _stickyScroll;
        private readonly CheckBox _minimap;

        public EditorControls(CheckBox hl, CheckBox bracket, CheckBox sticky, CheckBox minimap)
        {
            _highlightOccurrences = hl;
            _bracketMatching = bracket;
            _stickyScroll = sticky;
            _minimap = minimap;
        }

        public void Load(AppSettings settings)
        {
            var ep = settings.EditorProductivity;
            _highlightOccurrences.IsChecked = ep.HighlightOccurrences;
            _bracketMatching.IsChecked = ep.BracketMatching;
            _stickyScroll.IsChecked = ep.StickyScroll;
            _minimap.IsChecked = ep.Minimap;
        }

        public void Save(AppSettings settings)
        {
            settings.EditorProductivity.HighlightOccurrences = _highlightOccurrences.IsChecked == true;
            settings.EditorProductivity.BracketMatching = _bracketMatching.IsChecked == true;
            settings.EditorProductivity.StickyScroll = _stickyScroll.IsChecked == true;
            settings.EditorProductivity.Minimap = _minimap.IsChecked == true;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
