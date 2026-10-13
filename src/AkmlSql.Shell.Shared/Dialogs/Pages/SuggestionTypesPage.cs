#nullable enable
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Suggestions › Types of suggestion (Phase 2 C.1). Surfaces the
    /// <see cref="SuggestionTypesSettings"/> sub-object added in A.1 and
    /// wired into <c>CompletionEngine</c> + <c>ObjectProvider</c> in A.2.
    /// </summary>
    internal sealed class SuggestionTypesPage : IPageBuilder
    {
        public string Key     => "SuggestionTypes";
        public string Display => "Suggestions › Lists & scope › Types of suggestion";
        public string Title   => "Types of suggestion";
        public string HelpTopic => "topics/options#types-of-suggestion";
        public string Help    => "Controls which kinds of items appear in the completion list, including system objects and SQL keywords, and whether column suggestions are scoped to referenced tables only or every table in the database.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            // Design B (2026-10): one group, fitted rows.
            ctx.Rows.AddGroupHeader(panel, "What appears in the suggestion list");

            var (left, right) = ctx.Rows.AddColumns(panel);

            var (rowSysObjs, chkSysObjs) = ctx.Rows.AddToggle(left,
                "List system objects", "sp_* and sys.* objects");
            ctx.Rows.Tip(rowSysObjs, "Include system stored procs and functions (sp_*, sys.*) in suggestions.");
            ctx.RegisterSearch("List system objects", "Include system stored procs and functions in suggestions", "Toggle", rowSysObjs);

            // Spec 040 (OPT-01): "List all database columns after SELECT" is hidden — nothing reads
            // it (use "Suggest columns from: All tables"); the saved value is kept.

            var (rowKeywords, chkKeywords) = ctx.Rows.AddToggle(right,
                "Show keywords in suggestions", "");
            ctx.Rows.Tip(rowKeywords, "Include SQL keywords (SELECT, FROM, etc.) in the list.");
            ctx.RegisterSearch("Show keywords in suggestions", "Include SQL keywords in the completion list", "Toggle", rowKeywords);

            var (rowScope, cboScope) = ctx.Rows.AddDropdown(panel,
                "Suggest columns from",
                new[] { "Referenced tables only", "All tables" },
                "In WHERE and SELECT: the FROM-clause tables only, or every table");
            ctx.Rows.Tip(rowScope, "Whether typing in WHERE/SELECT shows columns from only the FROM-clause tables, or every table in the database.");
            ctx.RegisterSearch("Suggest columns from", "Scope of column suggestions", "Dropdown", rowScope);

            return new SuggestionTypesControls(chkSysObjs, chkKeywords, cboScope);
        }
    }

    internal sealed class SuggestionTypesControls : IPageControls
    {
        private readonly CheckBox _systemObjects;
        private readonly CheckBox _keywords;
        private readonly ComboBox _columnScope;

        public SuggestionTypesControls(CheckBox sysObjs, CheckBox keywords, ComboBox scope)
        {
            _systemObjects = sysObjs;
            _keywords = keywords;
            _columnScope = scope;
        }

        public void Load(AppSettings settings)
        {
            var s = settings.IntelliSense.SuggestionTypes;
            _systemObjects.IsChecked = s.IncludeSystemObjects;
            _keywords.IsChecked = s.IncludeKeywords;
            // ColumnScope: ReferencedOnly = 0, All = 1.
            // Map enum value to dropdown index (the dropdown lists Referenced first).
            _columnScope.SelectedIndex = s.ColumnScope == ColumnSuggestionScope.All ? 1 : 0;
        }

        public void Save(AppSettings settings)
        {
            var s = settings.IntelliSense.SuggestionTypes;
            s.IncludeSystemObjects = _systemObjects.IsChecked == true;
            s.IncludeKeywords = _keywords.IsChecked == true;
            s.ColumnScope = _columnScope.SelectedIndex == 1
                ? ColumnSuggestionScope.All
                : ColumnSuggestionScope.ReferencedOnly;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
