#nullable enable
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    internal sealed class EditorPage : IPageBuilder
    {
        public string Key     => "Editor";
        public string Display => "Editor › Productivity";
        public string Title   => "Editor Productivity";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            var (rowHl, chkHl) = ctx.Rows.AddToggle(panel,
                "Highlight occurrences", "Highlight all occurrences of selected identifier");
            ctx.RegisterSearch("Highlight occurrences", "Highlight all occurrences of selected identifier", "Toggle", rowHl);

            var (rowBracket, chkBracket) = ctx.Rows.AddToggle(panel,
                "Bracket matching", "Highlight matching BEGIN/END and parenthesis pairs");
            ctx.RegisterSearch("Bracket matching", "Highlight matching BEGIN/END and parenthesis pairs", "Toggle", rowBracket);

            var (rowRegions, chkRegions) = ctx.Rows.AddToggle(panel,
                "Named regions", "Show named region markers in editor");
            ctx.RegisterSearch("Named regions", "Show named region markers in editor", "Toggle", rowRegions);

            var (rowSticky, chkSticky) = ctx.Rows.AddToggle(panel,
                "Sticky scroll", "Pin parent scope headers while scrolling");
            ctx.RegisterSearch("Sticky scroll", "Pin parent scope headers while scrolling", "Toggle", rowSticky);

            var (rowMinimap, chkMinimap) = ctx.Rows.AddToggle(panel,
                "Code minimap", "Show code minimap in editor margin");
            ctx.RegisterSearch("Code minimap", "Show code minimap in editor margin", "Toggle", rowMinimap);

            var (rowOutline, chkOutline) = ctx.Rows.AddToggle(panel,
                "Document Outline", "Enable Document Outline panel");
            ctx.RegisterSearch("Document Outline", "Enable Document Outline panel", "Toggle", rowOutline);

            return new EditorControls(chkHl, chkBracket, chkRegions, chkSticky, chkMinimap, chkOutline);
        }
    }

    internal sealed class EditorControls : IPageControls
    {
        private readonly CheckBox _highlightOccurrences;
        private readonly CheckBox _bracketMatching;
        private readonly CheckBox _namedRegions;
        private readonly CheckBox _stickyScroll;
        private readonly CheckBox _minimap;
        private readonly CheckBox _documentOutline;

        public EditorControls(CheckBox hl, CheckBox bracket, CheckBox regions, CheckBox sticky, CheckBox minimap, CheckBox outline)
        {
            _highlightOccurrences = hl;
            _bracketMatching = bracket;
            _namedRegions = regions;
            _stickyScroll = sticky;
            _minimap = minimap;
            _documentOutline = outline;
        }

        public void Load(AppSettings settings)
        {
            var ep = settings.EditorProductivity;
            _highlightOccurrences.IsChecked = ep.HighlightOccurrences;
            _bracketMatching.IsChecked = ep.BracketMatching;
            _namedRegions.IsChecked = ep.NamedRegions;
            _stickyScroll.IsChecked = ep.StickyScroll;
            _minimap.IsChecked = ep.Minimap;
            _documentOutline.IsChecked = ep.DocumentOutline;
        }

        public void Save(AppSettings settings)
        {
            settings.EditorProductivity.HighlightOccurrences = _highlightOccurrences.IsChecked == true;
            settings.EditorProductivity.BracketMatching = _bracketMatching.IsChecked == true;
            settings.EditorProductivity.NamedRegions = _namedRegions.IsChecked == true;
            settings.EditorProductivity.StickyScroll = _stickyScroll.IsChecked == true;
            settings.EditorProductivity.Minimap = _minimap.IsChecked == true;
            settings.EditorProductivity.DocumentOutline = _documentOutline.IsChecked == true;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
