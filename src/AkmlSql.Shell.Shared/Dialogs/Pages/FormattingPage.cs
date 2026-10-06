#nullable enable
using System;
using System.Linq;
using System.Windows;
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
        public string Display => "Format";
        public string Title   => "Format";
        public string HelpTopic => "topics/options#format";
        public string Help    => "Choose the active formatting style, open the Edit Formatting Styles window to change how styles lay out SQL, turn the formatter on or off, and choose whether Bulk Format makes backups.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            // Spec 040 (OPT-05): the master switch leads the page; every other row is its child,
            // greyed while it is off.
            var (rowEnabled, chkEnabled) = ctx.Rows.AddToggle(panel,
                "Enable SQL formatter", "Master switch for all formatting features");
            ctx.RegisterSearch("Enable SQL formatter", "Master switch for all formatting features", "Toggle", rowEnabled);

            // Spec 030 T021 / FR-006 — see + switch the active formatting style.
            var (rowActive, cboActive) = ctx.Rows.AddDropdown(panel,
                "Active style",
                System.Array.Empty<string>(),
                "The formatting style Format SQL applies. Edit styles in Format Styles editor.", chkEnabled);
            ctx.RegisterSearch("Active style", "The formatting style Format SQL applies", "Dropdown", rowActive);

            // Spec 033 (T038 / US4) — the SQL Prompt-exact launcher: all layout/casing editing
            // happens in the dedicated window; this page only selects + launches.
            var (rowEdit, btnEdit) = ctx.Rows.AddButton(panel,
                "Formatting styles",
                "Edit formatting styles…",
                "Open the Edit Formatting Styles window (layout, casing, lists, parentheses…)", chkEnabled);
            ctx.RegisterSearch("Formatting styles", "Open the Edit Formatting Styles window", "Button", rowEdit);

            var (rowShowProfile, chkShowProfile) = ctx.Rows.AddToggle(panel,
                "Show active style in status bar", "Display the active formatting style in the status bar", chkEnabled);
            ctx.RegisterSearch("Show active style in status bar", "Display the active formatting style in the status bar", "Toggle", rowShowProfile);

            // Spec 040 (T187, T190 — STY-10, STY-11): the team style folder row and the
            // "When you run Format SQL, AKML SQL will:" group (self-contained: FormatSqlRows below).
            var formatSqlRows = FormatSqlRows.Add(panel, ctx, chkEnabled);

            // Spec 040 (OPT-01): Format on paste / save / delimiter, Confirm before bulk format,
            // Respect --noformat regions (Bulk Format always respects them) and Validate formatting
            // preserves semantics changed nothing and are hidden; their saved values are kept.

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Bulk format");

            var (rowBackups, chkBackups) = ctx.Rows.AddToggle(panel,
                "Create backups before formatting",
                "Save a backup copy of files before applying format changes", chkEnabled);
            ctx.RegisterSearch("Create backups before formatting", "Save a backup copy of files before applying format changes", "Toggle", rowBackups);

            var controls = new FormattingControls(cboActive, chkShowProfile, chkEnabled, chkBackups);
            controls.Attach(formatSqlRows);

            // Launch() is modal — when it returns, re-read the on-disk active style so a
            // Set-Active done inside the window survives the Options OK/Apply save path
            // (FormattingControls.Save writes the dropdown selection unconditionally).
            btnEdit.Click += (_, _) =>
            {
                // The shared open path: reports a launch failure and refreshes the Active Style menu.
                Commands.FormatStylesCommand.Open();
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
        private readonly CheckBox _createBackups;

        public FormattingControls(ComboBox activeStyle, CheckBox showProfile, CheckBox enabled, CheckBox backups)
        {
            _activeStyle = activeStyle;
            _showProfile = showProfile;
            _enabled = enabled;
            _createBackups = backups;
        }

        /// <summary>Spec 040 (T187, T190) — row groups that load and save with this page.</summary>
        private readonly System.Collections.Generic.List<IPageControls> _attached = new();

        /// <summary>Spec 040 — the page's team style folder and Format SQL actions rows.</summary>
        internal FormatSqlRows? AttachedFormatSqlRows => _attached.OfType<FormatSqlRows>().FirstOrDefault();

        internal void Attach(IPageControls rows) => _attached.Add(rows);

        public void Load(AppSettings settings)
        {
            var f = settings.Formatter;
            _showProfile.IsChecked = f.ShowProfileInStatusBar;
            _enabled.IsChecked = f.Enabled;
            _createBackups.IsChecked = f.CreateBackups;
            foreach (var rows in _attached) rows.Load(settings);

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
            settings.Formatter.CreateBackups = _createBackups.IsChecked == true;
            foreach (var rows in _attached) rows.Save(settings);
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

    /// <summary>
    /// Spec 040 (T187, STY-10 / T190, STY-11) — Options › Format › Styles: the <b>Team style
    /// folder</b> row (a full local or UNC path, with a <b>…</b> folder picker, checked by
    /// <see cref="TeamStyleFolderValidator"/>) and the <b>When you run Format SQL, AKML SQL
    /// will:</b> group, which lists only the actions the formatter can perform (FR-065). Every row
    /// is a child of "Enable SQL formatter".
    /// </summary>
    internal sealed class FormatSqlRows : IPageControls
    {
        internal const string TeamFolderLabel = "Team style folder";
        internal const string ActionsHeader = "When you run Format SQL, AKML SQL will:";

        /// <summary>Dropdown order; index ↔ the config word.</summary>
        // "As the style says" first and the default: a style that inserts semicolons or adds
        // brackets keeps doing so unless the user picks otherwise here.
        private static readonly string[] SemicolonWords = { FormatSqlActions.Style, FormatSqlActions.Insert, FormatSqlActions.Remove, FormatSqlActions.Leave };
        private static readonly string[] SemicolonItems = { "As the style says", "Insert", "Remove", "Leave as written" };
        private static readonly string[] BracketWords = { FormatSqlActions.Style, FormatSqlActions.Add, FormatSqlActions.Remove, FormatSqlActions.Leave };
        private static readonly string[] BracketItems = { "As the style says", "Add", "Remove", "Leave as written" };

        /// <summary>Semantic "invalid" red, the same in every theme (RowFactory's number fields use it too).</summary>
        private static readonly System.Windows.Media.SolidColorBrush InvalidBrush =
            Ui.Theme.PageTheme.Freeze(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE8, 0x11, 0x23)));

        private readonly TextBox _teamFolder;
        private readonly TextBlock _teamFolderError;
        private readonly System.Windows.Media.Brush? _teamFolderBorder;
        private readonly CheckBox _applyLayout;
        private readonly CheckBox _applyCasing;
        private readonly ComboBox _semicolons;
        private readonly ComboBox _squareBrackets;
        private readonly CheckBox _expandWildcards;
        private readonly CheckBox _qualifyObjectNames;

        /// <summary>The saved folder, kept when the box holds something that isn't a full path.</summary>
        private string _lastValidTeamFolder = string.Empty;

        private FormatSqlRows(TextBox teamFolder, TextBlock teamFolderError, CheckBox applyLayout, CheckBox applyCasing,
            ComboBox semicolons, ComboBox squareBrackets, CheckBox expandWildcards, CheckBox qualifyObjectNames)
        {
            _teamFolder = teamFolder;
            _teamFolderError = teamFolderError;
            _teamFolderBorder = teamFolder.BorderBrush;
            _applyLayout = applyLayout;
            _applyCasing = applyCasing;
            _semicolons = semicolons;
            _squareBrackets = squareBrackets;
            _expandWildcards = expandWildcards;
            _qualifyObjectNames = qualifyObjectNames;
            _teamFolder.LostFocus += (_, _) => ValidateTeamFolder();
        }

        // Test seams.
        internal TextBox TeamFolderBox => _teamFolder;
        internal TextBlock TeamFolderError => _teamFolderError;
        internal CheckBox ApplyLayout => _applyLayout;
        internal CheckBox ApplyCasing => _applyCasing;
        internal ComboBox Semicolons => _semicolons;
        internal ComboBox SquareBrackets => _squareBrackets;
        internal CheckBox ExpandWildcards => _expandWildcards;
        internal CheckBox QualifyObjectNames => _qualifyObjectNames;

        public static FormatSqlRows Add(StackPanel panel, PageContext ctx, CheckBox parent)
        {
            // ── Team style folder ────────────────────────────────────────────────────────────
            const string teamDescription =
                "A shared folder of styles. They are listed under TEAM STYLES for everyone who points here, " +
                "and are read-only unless the folder can be written to. Leave empty to turn team styles off.";
            var (teamRow, teamBox) = ctx.Rows.AddTextInput(panel, TeamFolderLabel, teamDescription, parent: parent);
            ctx.RegisterSearch(TeamFolderLabel, "A shared folder of formatting styles for your team", "Text", teamRow);

            // The box gets a "…" folder picker beside it, and an error line under it.
            var error = new TextBlock
            {
                Foreground = InvalidBrush,
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
            };
            var index = teamRow.Children.IndexOf(teamBox);
            teamRow.Children.Remove(teamBox);
            var browse = new Button
            {
                Content = "…",
                Width = 32,
                Height = 28,
                FontSize = 12,
                Margin = new Thickness(6, 0, 0, 0),
                ToolTip = "Choose the team style folder",
            };
            System.Windows.Automation.AutomationProperties.SetName(browse, "Choose the team style folder");
            Ui.Theme.ThemedButton.ApplySecondary(browse, ctx.Theme);
            var line = new DockPanel { LastChildFill = false, HorizontalAlignment = HorizontalAlignment.Left };
            line.Children.Add(teamBox);
            line.Children.Add(browse);
            teamRow.Children.Insert(index, line);
            teamRow.Children.Insert(index + 1, error);

            // ── When you run Format SQL, AKML SQL will: ─────────────────────────────────────
            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, ActionsHeader);

            var (layoutRow, layout) = ctx.Rows.AddToggle(panel, "Apply layout",
                "Lay out line breaks and indentation with the active style. Off keeps your own spacing.", parent);
            ctx.RegisterSearch("Apply layout", "Lay out line breaks and indentation when you run Format SQL", "Toggle", layoutRow);

            var (casingRow, casing) = ctx.Rows.AddToggle(panel, "Apply casing",
                "Change keywords, functions and data types to the active style's casing.", parent);
            ctx.RegisterSearch("Apply casing", "Change keyword, function and data type case when you run Format SQL", "Toggle", casingRow);

            var (semicolonsRow, semicolons) = ctx.Rows.AddDropdown(panel, "Semicolons", SemicolonItems,
                "Insert a semicolon after each statement, remove them, or leave them as written.", parent);
            ctx.RegisterSearch("Semicolons", "Insert or remove statement-terminating semicolons when you run Format SQL", "Dropdown", semicolonsRow);

            var (bracketsRow, brackets) = ctx.Rows.AddDropdown(panel, "Square brackets", BracketItems,
                "Put square brackets around names, remove the brackets names don't need, or leave them as written.", parent);
            ctx.RegisterSearch("Square brackets", "Add or remove square brackets around names when you run Format SQL", "Dropdown", bracketsRow);

            var (expandRow, expand) = ctx.Rows.AddToggle(panel, "Expand wildcards",
                "Replace SELECT * with the table's columns. Needs a query window connected to the database.", parent);
            ctx.RegisterSearch("Expand wildcards", "Replace SELECT * with the column list when you run Format SQL", "Toggle", expandRow);

            var (qualifyRow, qualify) = ctx.Rows.AddToggle(panel, "Qualify object names",
                "Add the schema to table names (Products → dbo.Products). Needs a query window connected to the database.", parent);
            ctx.RegisterSearch("Qualify object names", "Add the schema to table names when you run Format SQL", "Toggle", qualifyRow);

            var rows = new FormatSqlRows(teamBox, error, layout, casing, semicolons, brackets, expand, qualify);
            browse.Click += (_, _) => rows.BrowseForTeamFolder();
            return rows;
        }

        public void Load(AppSettings settings)
        {
            var f = settings.Formatter;
            _lastValidTeamFolder = f.TeamStyleFolder ?? string.Empty;
            _teamFolder.Text = _lastValidTeamFolder;
            ShowTeamFolderError(null);

            var a = f.FormatSqlActions ?? new FormatSqlActions();
            _applyLayout.IsChecked = a.ApplyLayout;
            _applyCasing.IsChecked = a.ApplyCasing;
            _semicolons.SelectedIndex = FormatSqlActionsMapper.SemicolonsCode(a.Semicolons) switch
            {
                1 => IndexOf(SemicolonWords, FormatSqlActions.Insert),
                2 => IndexOf(SemicolonWords, FormatSqlActions.Remove),
                0 => IndexOf(SemicolonWords, FormatSqlActions.Leave),
                _ => IndexOf(SemicolonWords, FormatSqlActions.Style),
            };
            _squareBrackets.SelectedIndex = FormatSqlActionsMapper.SquareBracketsCode(a.SquareBrackets) switch
            {
                1 => IndexOf(BracketWords, FormatSqlActions.Add),
                2 => IndexOf(BracketWords, FormatSqlActions.Remove),
                0 => IndexOf(BracketWords, FormatSqlActions.Leave),
                _ => IndexOf(BracketWords, FormatSqlActions.Style),
            };
            _expandWildcards.IsChecked = a.ExpandWildcards;
            _qualifyObjectNames.IsChecked = a.QualifyObjectNames;
        }

        public void Save(AppSettings settings)
        {
            // A path that isn't a full path is never saved: the last good one stays, and the row says why.
            settings.Formatter.TeamStyleFolder = ValidateTeamFolder() ?? _lastValidTeamFolder;

            if (settings.Formatter.FormatSqlActions == null) settings.Formatter.FormatSqlActions = new FormatSqlActions();
            var a = settings.Formatter.FormatSqlActions;
            a.ApplyLayout = _applyLayout.IsChecked == true;
            a.ApplyCasing = _applyCasing.IsChecked == true;
            a.Semicolons = WordAt(SemicolonWords, _semicolons.SelectedIndex);
            a.SquareBrackets = WordAt(BracketWords, _squareBrackets.SelectedIndex);
            a.ExpandWildcards = _expandWildcards.IsChecked == true;
            a.QualifyObjectNames = _qualifyObjectNames.IsChecked == true;
        }

        public void Reset(AppSettings defaults) => Load(defaults);

        /// <summary>
        /// Checks the box: a full path (or empty) becomes the value to save, in its canonical form;
        /// anything else turns the border red, shows why, and returns null.
        /// </summary>
        internal string? ValidateTeamFolder()
        {
            var (ok, fullPath, error) = TeamStyleFolderValidator.Normalize(_teamFolder.Text);
            if (!ok)
            {
                ShowTeamFolderError(error);
                return null;
            }

            ShowTeamFolderError(null);
            _lastValidTeamFolder = fullPath ?? string.Empty;
            return _lastValidTeamFolder;
        }

        private void ShowTeamFolderError(string? error)
        {
            _teamFolderError.Text = error ?? string.Empty;
            _teamFolderError.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
            _teamFolder.BorderBrush = error == null ? _teamFolderBorder : InvalidBrush;
        }

        private void BrowseForTeamFolder()
        {
            try
            {
                using var dialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Choose the team style folder",
                    ShowNewFolderButton = true,
                };
                var current = TeamStyleFolderValidator.Normalize(_teamFolder.Text).FullPath;
                if (!string.IsNullOrEmpty(current)) dialog.SelectedPath = current;

                var owner = Window.GetWindow(_teamFolder);
                var result = owner == null
                    ? dialog.ShowDialog()
                    : dialog.ShowDialog(new Win32Owner(new System.Windows.Interop.WindowInteropHelper(owner).Handle));
                if (result != System.Windows.Forms.DialogResult.OK) return;

                _teamFolder.Text = dialog.SelectedPath;
                ValidateTeamFolder();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "FormattingPage: team style folder picker failed");
            }
        }

        private static int IndexOf(string[] words, string word) => Math.Max(0, Array.IndexOf(words, word));

        private static string WordAt(string[] words, int index) =>
            index >= 0 && index < words.Length ? words[index] : FormatSqlActions.Style;

        private sealed class Win32Owner : System.Windows.Forms.IWin32Window
        {
            public Win32Owner(IntPtr handle) => Handle = handle;
            public IntPtr Handle { get; }
        }
    }
}
