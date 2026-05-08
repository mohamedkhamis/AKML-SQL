#nullable enable
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    internal sealed class FormattingPage : IPageBuilder
    {
        public string Key     => "Formatting";
        public string Display => "Format › Styles";
        public string Title   => "SQL Formatting";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Triggers");

            var (rowEnabled, chkEnabled) = ctx.Rows.AddToggle(panel,
                "Enable SQL formatter", "Master switch for all formatting features");
            ctx.RegisterSearch("Enable SQL formatter", "Master switch for all formatting features", "Toggle", rowEnabled);

            var (rowPaste, chkPaste) = ctx.Rows.AddToggle(panel,
                "Format on paste", "Automatically format SQL when pasting from clipboard");
            ctx.RegisterSearch("Format on paste", "Automatically format SQL when pasting from clipboard", "Toggle", rowPaste);

            var (rowSave, chkSave) = ctx.Rows.AddToggle(panel,
                "Format on save", "Automatically format the document when saving");
            ctx.RegisterSearch("Format on save", "Automatically format the document when saving", "Toggle", rowSave);

            var (rowDelim, chkDelim) = ctx.Rows.AddToggle(panel,
                "Format on delimiter", "Format when typing GO or semicolon");
            ctx.RegisterSearch("Format on delimiter", "Format when typing GO or semicolon", "Toggle", rowDelim);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Safety & Validation");

            var (rowBulk, chkBulk) = ctx.Rows.AddToggle(panel,
                "Confirm before bulk format",
                "Show a confirmation dialog before formatting multiple files");
            ctx.RegisterSearch("Confirm before bulk format", "Show a confirmation dialog before formatting multiple files", "Toggle", rowBulk);

            var (rowBackups, chkBackups) = ctx.Rows.AddToggle(panel,
                "Create backups before formatting",
                "Save a backup copy of files before applying format changes");
            ctx.RegisterSearch("Create backups before formatting", "Save a backup copy of files before applying format changes", "Toggle", rowBackups);

            var (rowNoFmt, chkNoFmt) = ctx.Rows.AddToggle(panel,
                "Respect --noformat regions",
                "Skip formatting inside --noformat / --endnoformat blocks");
            ctx.RegisterSearch("Respect --noformat regions", "Skip formatting inside --noformat / --endnoformat blocks", "Toggle", rowNoFmt);

            var (rowSemantic, chkSemantic) = ctx.Rows.AddToggle(panel,
                "Validate formatting preserves semantics",
                "Re-parse formatted SQL to verify it is semantically equivalent");
            ctx.RegisterSearch("Validate formatting preserves semantics", "Re-parse formatted SQL to verify it is semantically equivalent", "Toggle", rowSemantic);

            return new FormattingControls(chkEnabled, chkPaste, chkSave, chkDelim,
                chkBulk, chkBackups, chkNoFmt, chkSemantic);
        }
    }

    internal sealed class FormattingControls : IPageControls
    {
        private readonly CheckBox _enabled;
        private readonly CheckBox _onPaste;
        private readonly CheckBox _onSave;
        private readonly CheckBox _onDelimiter;
        private readonly CheckBox _confirmBulk;
        private readonly CheckBox _createBackups;
        private readonly CheckBox _respectNoformat;
        private readonly CheckBox _semanticValidation;

        public FormattingControls(CheckBox enabled, CheckBox onPaste, CheckBox onSave, CheckBox onDelim,
            CheckBox bulk, CheckBox backups, CheckBox noFmt, CheckBox semantic)
        {
            _enabled = enabled;
            _onPaste = onPaste;
            _onSave = onSave;
            _onDelimiter = onDelim;
            _confirmBulk = bulk;
            _createBackups = backups;
            _respectNoformat = noFmt;
            _semanticValidation = semantic;
        }

        public void Load(AppSettings settings)
        {
            var f = settings.Formatter;
            _enabled.IsChecked = f.Enabled;
            _onPaste.IsChecked = f.FormatOnPaste;
            _onSave.IsChecked = f.FormatOnSave;
            _onDelimiter.IsChecked = f.FormatOnDelimiter;
            _confirmBulk.IsChecked = f.ConfirmBulkFormat;
            _createBackups.IsChecked = f.CreateBackups;
            _respectNoformat.IsChecked = f.RespectNoformat;
            _semanticValidation.IsChecked = f.SemanticValidation;
        }

        public void Save(AppSettings settings)
        {
            settings.Formatter.Enabled = _enabled.IsChecked == true;
            settings.Formatter.FormatOnPaste = _onPaste.IsChecked == true;
            settings.Formatter.FormatOnSave = _onSave.IsChecked == true;
            settings.Formatter.FormatOnDelimiter = _onDelimiter.IsChecked == true;
            settings.Formatter.ConfirmBulkFormat = _confirmBulk.IsChecked == true;
            settings.Formatter.CreateBackups = _createBackups.IsChecked == true;
            settings.Formatter.RespectNoformat = _respectNoformat.IsChecked == true;
            settings.Formatter.SemanticValidation = _semanticValidation.IsChecked == true;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
