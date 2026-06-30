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
        public string Display => "Suggestions › Types of suggestion";
        public string Title   => "Types of suggestion";
        public string Help    => "Controls which kinds of items appear in the completion list, including system objects, all database columns after SELECT, and SQL keywords, and whether column suggestions are scoped to referenced tables only or every table in the database.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "What appears in the suggestion list");

            var (rowSysObjs, chkSysObjs) = ctx.Rows.AddToggle(panel,
                "List system objects",
                "Include system stored procs and functions (sp_*, sys.*) in suggestions.");
            ctx.RegisterSearch("List system objects", "Include system stored procs and functions in suggestions", "Toggle", rowSysObjs);

            var (rowAllCols, chkAllCols) = ctx.Rows.AddToggle(panel,
                "List all database columns after SELECT",
                "Show every column from every table immediately after SELECT.");
            ctx.RegisterSearch("List all database columns after SELECT", "Show every column from every table immediately after SELECT", "Toggle", rowAllCols);

            var (rowKeywords, chkKeywords) = ctx.Rows.AddToggle(panel,
                "Show keywords in suggestions",
                "Include SQL keywords (SELECT, FROM, etc.) in the list.");
            ctx.RegisterSearch("Show keywords in suggestions", "Include SQL keywords in the completion list", "Toggle", rowKeywords);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Column suggestions");

            var (rowScope, cboScope) = ctx.Rows.AddDropdown(panel,
                "Suggest columns from",
                new[] { "Referenced tables only", "All tables" },
                "Whether typing in WHERE/SELECT shows columns from only the FROM-clause tables, or every table in the database.");
            ctx.RegisterSearch("Suggest columns from", "Scope of column suggestions", "Dropdown", rowScope);

            return new SuggestionTypesControls(chkSysObjs, chkAllCols, chkKeywords, cboScope);
        }
    }

    internal sealed class SuggestionTypesControls : IPageControls
    {
        private readonly CheckBox _systemObjects;
        private readonly CheckBox _allColumnsAfterSelect;
        private readonly CheckBox _keywords;
        private readonly ComboBox _columnScope;

        public SuggestionTypesControls(CheckBox sysObjs, CheckBox allCols, CheckBox keywords, ComboBox scope)
        {
            _systemObjects = sysObjs;
            _allColumnsAfterSelect = allCols;
            _keywords = keywords;
            _columnScope = scope;
        }

        public void Load(AppSettings settings)
        {
            var s = settings.IntelliSense.SuggestionTypes;
            _systemObjects.IsChecked = s.IncludeSystemObjects;
            _allColumnsAfterSelect.IsChecked = s.SuggestAllColumnsAfterSelect;
            _keywords.IsChecked = s.IncludeKeywords;
            // ColumnScope: ReferencedOnly = 0, All = 1.
            // Map enum value to dropdown index (the dropdown lists Referenced first).
            _columnScope.SelectedIndex = s.ColumnScope == ColumnSuggestionScope.All ? 1 : 0;
        }

        public void Save(AppSettings settings)
        {
            var s = settings.IntelliSense.SuggestionTypes;
            s.IncludeSystemObjects = _systemObjects.IsChecked == true;
            s.SuggestAllColumnsAfterSelect = _allColumnsAfterSelect.IsChecked == true;
            s.IncludeKeywords = _keywords.IsChecked == true;
            s.ColumnScope = _columnScope.SelectedIndex == 1
                ? ColumnSuggestionScope.All
                : ColumnSuggestionScope.ReferencedOnly;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
