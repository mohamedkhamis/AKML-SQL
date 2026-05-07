#nullable enable
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    internal sealed class RefactoringPage : IPageBuilder
    {
        public string Key     => "Refactoring";
        public string Display => "Editor › Refactoring";
        public string Title   => "Refactoring";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Preview & Safety");

            var (rowPreview, chkPreview) = ctx.Rows.AddToggle(panel,
                "Show preview before applying",
                "Display a diff preview dialog before applying refactoring changes");
            ctx.RegisterSearch("Show preview before applying", "Display a diff preview dialog before applying refactoring changes", "Toggle", rowPreview);

            var (rowBackups, chkBackups) = ctx.Rows.AddToggle(panel,
                "Create backups",
                "Save a backup copy before applying refactoring changes");
            ctx.RegisterSearch("Create backups", "Save a backup copy before applying refactoring changes", "Toggle", rowBackups);

            var (rowFormatAfter, chkFormatAfter) = ctx.Rows.AddToggle(panel,
                "Format after refactoring",
                "Apply SQL formatting after a refactoring operation completes");
            ctx.RegisterSearch("Format after refactoring", "Apply SQL formatting after a refactoring operation completes", "Toggle", rowFormatAfter);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Rename Options");

            var (rowComments, chkComments) = ctx.Rows.AddToggle(panel,
                "Include comments in rename scope",
                "Also rename occurrences found inside SQL comments");
            ctx.RegisterSearch("Include comments in rename scope", "Also rename occurrences found inside SQL comments", "Toggle", rowComments);

            var (rowStrings, chkStrings) = ctx.Rows.AddToggle(panel,
                "Include string literals in rename scope",
                "Also rename occurrences found inside string literals");
            ctx.RegisterSearch("Include string literals in rename scope", "Also rename occurrences found inside string literals", "Toggle", rowStrings);

            var (rowScope, cboScope) = ctx.Rows.AddDropdown(panel,
                "Rename scope",
                new[] { "Current Script", "Project Directory" },
                "Scope of the Safe Rename operation");
            ctx.RegisterSearch("Rename scope", "Scope of the Safe Rename operation", "Dropdown", rowScope);

            return new RefactoringControls(chkPreview, chkBackups, chkFormatAfter, chkComments, chkStrings, cboScope);
        }
    }

    internal sealed class RefactoringControls : IPageControls
    {
        private readonly CheckBox _previewBeforeApply;
        private readonly CheckBox _createBackups;
        private readonly CheckBox _formatAfter;
        private readonly CheckBox _includeComments;
        private readonly CheckBox _includeStrings;
        private readonly ComboBox _renameScope;

        public RefactoringControls(CheckBox preview, CheckBox backups, CheckBox formatAfter,
            CheckBox comments, CheckBox strings, ComboBox renameScope)
        {
            _previewBeforeApply = preview;
            _createBackups = backups;
            _formatAfter = formatAfter;
            _includeComments = comments;
            _includeStrings = strings;
            _renameScope = renameScope;
        }

        public void Load(AppSettings settings)
        {
            var rf = settings.Refactoring;
            _previewBeforeApply.IsChecked = rf.PreviewBeforeApply;
            _createBackups.IsChecked = rf.CreateBackups;
            _formatAfter.IsChecked = rf.FormatAfterRefactor;
            _includeComments.IsChecked = rf.IncludeCommentsInRename;
            _includeStrings.IsChecked = rf.IncludeStringLiteralsInRename;
            _renameScope.SelectedIndex = rf.RenameScope == "projectDirectory" ? 1 : 0;
        }

        public void Save(AppSettings settings)
        {
            settings.Refactoring.PreviewBeforeApply = _previewBeforeApply.IsChecked == true;
            settings.Refactoring.CreateBackups = _createBackups.IsChecked == true;
            settings.Refactoring.FormatAfterRefactor = _formatAfter.IsChecked == true;
            settings.Refactoring.IncludeCommentsInRename = _includeComments.IsChecked == true;
            settings.Refactoring.IncludeStringLiteralsInRename = _includeStrings.IsChecked == true;
            settings.Refactoring.RenameScope = _renameScope.SelectedIndex == 1
                ? "projectDirectory" : "currentScript";
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
