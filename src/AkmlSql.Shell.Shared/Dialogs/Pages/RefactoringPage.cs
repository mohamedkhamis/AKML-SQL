#nullable enable
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Editor › Refactoring. Spec 040 (OPT-01): Show preview before applying, Create backups,
    /// Format after refactoring, Include string literals in rename scope and Rename scope changed
    /// nothing and are hidden; their saved values are kept.
    /// </summary>
    internal sealed class RefactoringPage : IPageBuilder
    {
        public string Key     => "Refactoring";
        public string Display => "Editor › Refactoring";
        public string Title   => "Refactoring";
        public string Help    => "Controls Safe Rename: whether occurrences inside comments are renamed too.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Rename Options");

            var (rowComments, chkComments) = ctx.Rows.AddToggle(panel,
                "Include comments in rename scope",
                "Also rename occurrences found inside SQL comments");
            ctx.RegisterSearch("Include comments in rename scope", "Also rename occurrences found inside SQL comments", "Toggle", rowComments);

            return new RefactoringControls(chkComments);
        }
    }

    internal sealed class RefactoringControls : IPageControls
    {
        private readonly CheckBox _includeComments;

        public RefactoringControls(CheckBox comments)
        {
            _includeComments = comments;
        }

        public void Load(AppSettings settings)
        {
            _includeComments.IsChecked = settings.Refactoring.IncludeCommentsInRename;
        }

        public void Save(AppSettings settings)
        {
            settings.Refactoring.IncludeCommentsInRename = _includeComments.IsChecked == true;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
