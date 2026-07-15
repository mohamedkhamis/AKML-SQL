#nullable enable
using System;
using System.Linq;
using System.Windows.Controls;
using Microsoft.VisualStudio.Shell;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using Serilog;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    internal sealed class FormattingPage : IPageBuilder
    {
        public string Key     => "Formatting";
        public string Display => "Format › Styles";
        public string Title   => "SQL Formatting";
        public string Help    => "Choose the active formatting style and control when Format SQL runs (on paste, save, or delimiter), plus safety options like bulk-format confirmation, backups, --noformat regions, and semantic validation.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            // Spec 030 T021 / FR-006 — see + switch the active formatting style. The page title
            // ("SQL Formatting") already frames this lead section, so no redundant "Active style"
            // group header above the "Active style" dropdown (it read as duplicated text).
            var (rowActive, cboActive) = ctx.Rows.AddDropdown(panel,
                "Active style",
                System.Array.Empty<string>(),
                "The formatting style Format SQL applies. Edit styles in Format Styles editor.");
            ctx.RegisterSearch("Active style", "The formatting style Format SQL applies", "Dropdown", rowActive);

            var (rowShowProfile, chkShowProfile) = ctx.Rows.AddToggle(panel,
                "Show active style in status bar", "Display the active formatting style in the status bar");
            ctx.RegisterSearch("Show active style in status bar", "Display the active formatting style in the status bar", "Toggle", rowShowProfile);

            ctx.Rows.AddGroupSeparator(panel);
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

            return new FormattingControls(cboActive, chkShowProfile, chkEnabled, chkPaste, chkSave, chkDelim,
                chkBulk, chkBackups, chkNoFmt, chkSemantic);
        }
    }

    internal sealed class FormattingControls : IPageControls
    {
        private readonly ComboBox _activeStyle;
        private readonly CheckBox _showProfile;
        private readonly CheckBox _enabled;
        private readonly CheckBox _onPaste;
        private readonly CheckBox _onSave;
        private readonly CheckBox _onDelimiter;
        private readonly CheckBox _confirmBulk;
        private readonly CheckBox _createBackups;
        private readonly CheckBox _respectNoformat;
        private readonly CheckBox _semanticValidation;

        public FormattingControls(ComboBox activeStyle, CheckBox showProfile,
            CheckBox enabled, CheckBox onPaste, CheckBox onSave, CheckBox onDelim,
            CheckBox bulk, CheckBox backups, CheckBox noFmt, CheckBox semantic)
        {
            _activeStyle = activeStyle;
            _showProfile = showProfile;
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
            _showProfile.IsChecked = f.ShowProfileInStatusBar;
            _enabled.IsChecked = f.Enabled;
            _onPaste.IsChecked = f.FormatOnPaste;
            _onSave.IsChecked = f.FormatOnSave;
            _onDelimiter.IsChecked = f.FormatOnDelimiter;
            _confirmBulk.IsChecked = f.ConfirmBulkFormat;
            _createBackups.IsChecked = f.CreateBackups;
            _respectNoformat.IsChecked = f.RespectNoformat;
            _semanticValidation.IsChecked = f.SemanticValidation;

            // Seed the dropdown synchronously with the persisted active style so it is never empty,
            // then fill the full list from the engine (custom + built-in) asynchronously.
            var active = string.IsNullOrWhiteSpace(f.ActiveProfile) ? "Default" : f.ActiveProfile;
            SetItems(new[] { active }, active);
            _ = PopulateProfilesAsync(active);
        }

        public void Save(AppSettings settings)
        {
            var selected = SelectedName();
            if (!string.IsNullOrEmpty(selected))
                settings.Formatter.ActiveProfile = selected!;
            settings.Formatter.ShowProfileInStatusBar = _showProfile.IsChecked == true;
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

        private async System.Threading.Tasks.Task PopulateProfilesAsync(string active)
        {
            try
            {
                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected) return;

                var response = await client.SendRequestAsync<ProfileListResponse, ProfileListRequest>(
                    MessageTypes.ProfileList, new ProfileListRequest(), timeoutMs: 3000);

                var names = (response?.Profiles ?? System.Array.Empty<ProfileInfo>())
                    .Select(p => p.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (names.Count == 0) return;
                if (!names.Any(n => string.Equals(n, active, StringComparison.OrdinalIgnoreCase)))
                    names.Insert(0, active);

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                SetItems(names, active);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Active-style dropdown: profile list IPC failed (seed retained)");
            }
        }

        private void SetItems(System.Collections.Generic.IEnumerable<string> names, string selectName)
        {
            _activeStyle.Items.Clear();
            // Plain string items — RowFactory's ComboBox template/ItemContainerStyle own all
            // theming. Wrapping in ComboBoxItem/TextBlock breaks the closed-face rendering
            // (VisualBrush snapshot) and dark-theme item colors; see RowFactory.StyleComboBox.
            foreach (var name in names)
                _activeStyle.Items.Add(name);
            SelectByName(selectName);
        }

        private void SelectByName(string name)
        {
            for (int i = 0; i < _activeStyle.Items.Count; i++)
            {
                if (_activeStyle.Items[i] is string s
                    && string.Equals(s, name, StringComparison.OrdinalIgnoreCase))
                {
                    _activeStyle.SelectedIndex = i;
                    return;
                }
            }
            if (_activeStyle.Items.Count > 0) _activeStyle.SelectedIndex = 0;
        }

        private string? SelectedName()
        {
            return _activeStyle.SelectedItem as string;
        }
    }
}
