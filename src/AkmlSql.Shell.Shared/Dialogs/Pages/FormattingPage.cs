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
        public string Help    => "Choose the active formatting style, open the Edit Formatting Styles window to change how styles lay out SQL, and control when Format SQL runs (on paste, save, or delimiter) plus safety options like bulk-format confirmation, backups, --noformat regions, and semantic validation.";

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

            // Spec 033 (T038 / US4) — the SQL Prompt-exact launcher: all layout/casing editing
            // happens in the dedicated window; this page only selects + launches.
            var (rowEdit, btnEdit) = ctx.Rows.AddButton(panel,
                "Formatting styles",
                "Edit formatting styles…",
                "Open the Edit Formatting Styles window (layout, casing, lists, parentheses…)");
            ctx.RegisterSearch("Edit formatting styles", "Open the Edit Formatting Styles window", "Button", rowEdit);

            var (rowShowProfile, chkShowProfile) = ctx.Rows.AddToggle(panel,
                "Show active style in status bar", "Display the active formatting style in the status bar");
            ctx.RegisterSearch("Show active style in status bar", "Display the active formatting style in the status bar", "Toggle", rowShowProfile);

            // Spec 033 (US4) — the trigger toggles live under "Behavior" (SQL Prompt-exact
            // mockup); "Safety & Validation" remains its own group below.
            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Behavior");

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

            var controls = new FormattingControls(cboActive, chkShowProfile, chkEnabled, chkPaste, chkSave, chkDelim,
                chkBulk, chkBackups, chkNoFmt, chkSemantic);

            // Launch() is modal — when it returns, re-read the on-disk active style so a
            // Set-Active done inside the window survives the Options OK/Apply save path
            // (FormattingControls.Save writes the dropdown selection unconditionally).
            btnEdit.Click += (_, _) =>
            {
                try
                {
                    Formatting.FormatStylesEditorWindow.Launch();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "FormattingPage: Format Styles editor launch failed");
                }
                controls.RefreshActiveStyleFromDisk();
            };

            return controls;
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

            SeedActiveStyle(f.ActiveProfile);
        }

        /// <summary>The shipped default when config carries no active style (single source:
        /// the <see cref="FormatterSettings.ActiveProfile"/> initializer).</summary>
        private static readonly string DefaultActiveProfile = new FormatterSettings().ActiveProfile;

        /// <summary>
        /// Seeds the dropdown synchronously with the persisted active style so it is never
        /// empty, then fills the full list from the engine (custom + built-in) asynchronously.
        /// Shared by <see cref="Load"/> and <see cref="RefreshActiveStyleFromDisk"/>.
        /// </summary>
        private void SeedActiveStyle(string? persisted)
        {
            var active = string.IsNullOrWhiteSpace(persisted) ? DefaultActiveProfile : persisted!;
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

        /// <summary>
        /// Spec 033 (T038 / US4 scenario 2+3) — re-seeds the dropdown from the CURRENT on-disk
        /// <c>Formatter.ActiveProfile</c> and repopulates the list. Called after the modal
        /// styles editor closes so its Set-Active / create / rename / delete results are
        /// reflected here — and so the Options save path persists the fresh name instead of
        /// clobbering it with a stale selection.
        /// </summary>
        internal void RefreshActiveStyleFromDisk()
        {
            try
            {
                SeedActiveStyle(ConfigManager.Load().Formatter.ActiveProfile);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "FormattingPage: active-style refresh failed");
            }
        }

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
