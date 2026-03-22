using System;
using System.Drawing;
using System.Windows.Forms;
using AkmlSql.Core.Config;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Dialogs
{
    internal class SettingsDialog : Form
    {
        private AppSettings _settings;

        // General
        private CheckBox _chkAutoUpdate;
        private CheckBox _chkTelemetry;

        // IntelliSense
        private CheckBox _chkIsEnabled;
        private CheckBox _chkAutoTrigger;
        private NumericUpDown _nudTriggerDelay;
        private CheckBox _chkAfterDot;
        private NumericUpDown _nudMaxSuggestions;
        private CheckBox _chkFuzzyMatch;
        private CheckBox _chkShowDataTypes;
        private CheckBox _chkShowNullability;
        private CheckBox _chkShowPkFk;
        private CheckBox _chkAutoAlias;
        private CheckBox _chkJoinAssist;
        private ComboBox _cboKeywordCase;
        private CheckBox _chkDisableNativeIs;

        // Cache
        private CheckBox _chkCacheAutoRefresh;
        private NumericUpDown _nudRefreshInterval;
        private CheckBox _chkDetectDdl;
        private NumericUpDown _nudMaxDatabases;
        private CheckBox _chkLazyLoadColumns;
        private CheckBox _chkPersistToDisk;

        // Formatter
        private CheckBox _chkFmtEnabled;
        private CheckBox _chkFormatOnPaste;
        private CheckBox _chkFormatOnSave;
        private CheckBox _chkFormatOnDelimiter;
        private CheckBox _chkConfirmBulk;
        private CheckBox _chkCreateBackups;
        private CheckBox _chkRespectNoformat;
        private CheckBox _chkSemanticValidation;

        // Snippets
        private CheckBox _chkSnipEnabled;
        private CheckBox _chkSnipShowInCompletion;
        private CheckBox _chkSnipFormatOnExpand;
        private CheckBox _chkSnipContextFilter;
        private CheckBox _chkSnipTrackUsage;
        private TextBox _txtPersonalFolder;
        private TextBox _txtTeamFolder;

        public SettingsDialog(AppSettings settings)
        {
            _settings = settings;
            InitializeComponents();
            LoadSettingsToControls();
        }

        public AppSettings GetSettings()
        {
            SaveControlsToSettings();
            return _settings;
        }

        private void InitializeComponents()
        {
            Text = Constants.ProductName + " Options";
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(580, 560);
            MinimumSize = new Size(520, 480);
            ShowInTaskbar = false;

            var tabControl = new TabControl
            {
                Dock = DockStyle.Fill
            };

            tabControl.TabPages.Add(CreateGeneralTab());
            tabControl.TabPages.Add(CreateIntelliSenseTab());
            tabControl.TabPages.Add(CreateCacheTab());
            tabControl.TabPages.Add(CreateFormatterTab());
            tabControl.TabPages.Add(CreateSnippetsTab());

            var buttonPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 50
            };

            var saveButton = new Button
            {
                Text = "Save",
                Size = new Size(90, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                DialogResult = DialogResult.OK
            };
            saveButton.Location = new Point(buttonPanel.Width - 210, 10);
            // Recompute on resize
            buttonPanel.Resize += (_, _2) =>
            {
                saveButton.Location = new Point(buttonPanel.Width - 210, 10);
            };

            var cancelButton = new Button
            {
                Text = "Cancel",
                Size = new Size(90, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                DialogResult = DialogResult.Cancel
            };
            cancelButton.Location = new Point(buttonPanel.Width - 105, 10);
            buttonPanel.Resize += (_, _2) =>
            {
                cancelButton.Location = new Point(buttonPanel.Width - 105, 10);
            };

            AcceptButton = saveButton;
            CancelButton = cancelButton;

            buttonPanel.Controls.Add(saveButton);
            buttonPanel.Controls.Add(cancelButton);

            Controls.Add(tabControl);
            Controls.Add(buttonPanel);
        }

        private TabPage CreateGeneralTab()
        {
            var tab = new TabPage("General");
            var y = 20;

            AddSectionLabel(tab, "Updates & Telemetry", ref y);
            _chkAutoUpdate = AddCheckBox(tab, "Check for updates automatically (every 24h)", ref y);
            _chkTelemetry = AddCheckBox(tab, "Send anonymous usage telemetry (no PII)", ref y);

            y += 10;
            AddSectionLabel(tab, "Paths", ref y);
            AddReadOnlyField(tab, "Config:", Constants.ConfigFilePath, ref y);
            AddReadOnlyField(tab, "Logs:", Constants.LogsPath, ref y);

            return tab;
        }

        private TabPage CreateIntelliSenseTab()
        {
            var tab = new TabPage("IntelliSense");
            tab.AutoScroll = true;
            var y = 20;

            AddSectionLabel(tab, "Core", ref y);
            _chkIsEnabled = AddCheckBox(tab, "Enable IntelliSense", ref y);
            _chkAutoTrigger = AddCheckBox(tab, "Auto-trigger completions while typing", ref y);
            _chkAfterDot = AddCheckBox(tab, "Trigger after dot (e.g. dbo.)", ref y);
            _chkFuzzyMatch = AddCheckBox(tab, "Enable fuzzy matching", ref y);

            y += 10;
            AddSectionLabel(tab, "Display", ref y);
            _nudMaxSuggestions = AddNumericField(tab, "Max suggestions:", 5, 200, ref y);
            _nudTriggerDelay = AddNumericField(tab, "Trigger delay (ms):", 0, 2000, ref y);
            _cboKeywordCase = AddComboField(tab, "Keyword case:",
                ["UPPER", "lower", "PascalCase", "As-Is"], ref y);
            _chkShowDataTypes = AddCheckBox(tab, "Show data types in suggestions", ref y);
            _chkShowNullability = AddCheckBox(tab, "Show nullability info", ref y);
            _chkShowPkFk = AddCheckBox(tab, "Show PK/FK indicators", ref y);

            y += 10;
            AddSectionLabel(tab, "Assistance", ref y);
            _chkAutoAlias = AddCheckBox(tab, "Auto-generate table aliases", ref y);
            _chkJoinAssist = AddCheckBox(tab, "JOIN clause assistance", ref y);
            _chkDisableNativeIs = AddCheckBox(tab, "Disable native SSMS IntelliSense (recommended)", ref y);

            return tab;
        }

        private TabPage CreateCacheTab()
        {
            var tab = new TabPage("Cache");
            var y = 20;

            AddSectionLabel(tab, "Schema Cache", ref y);
            _chkCacheAutoRefresh = AddCheckBox(tab, "Auto-refresh schema cache", ref y);
            _nudRefreshInterval = AddNumericField(tab, "Refresh interval (seconds):", 30, 3600, ref y);
            _chkDetectDdl = AddCheckBox(tab, "Detect DDL changes and refresh automatically", ref y);
            _nudMaxDatabases = AddNumericField(tab, "Max cached databases:", 1, 50, ref y);
            _chkLazyLoadColumns = AddCheckBox(tab, "Lazy-load column metadata", ref y);
            _chkPersistToDisk = AddCheckBox(tab, "Persist cache to disk", ref y);

            return tab;
        }

        private TabPage CreateFormatterTab()
        {
            var tab = new TabPage("Formatter");
            var y = 20;

            AddSectionLabel(tab, "Formatting", ref y);
            _chkFmtEnabled = AddCheckBox(tab, "Enable SQL formatter", ref y);
            _chkFormatOnPaste = AddCheckBox(tab, "Format on paste", ref y);
            _chkFormatOnSave = AddCheckBox(tab, "Format on save", ref y);
            _chkFormatOnDelimiter = AddCheckBox(tab, "Format on delimiter (GO, semicolon)", ref y);

            y += 10;
            AddSectionLabel(tab, "Safety", ref y);
            _chkConfirmBulk = AddCheckBox(tab, "Confirm before bulk format", ref y);
            _chkCreateBackups = AddCheckBox(tab, "Create backups before formatting files", ref y);
            _chkRespectNoformat = AddCheckBox(tab, "Respect --noformat regions", ref y);
            _chkSemanticValidation = AddCheckBox(tab, "Validate formatting preserves semantics", ref y);

            return tab;
        }

        private TabPage CreateSnippetsTab()
        {
            var tab = new TabPage("Snippets");
            tab.AutoScroll = true;
            var y = 20;

            AddSectionLabel(tab, "Snippet Manager", ref y);
            _chkSnipEnabled = AddCheckBox(tab, "Enable snippets", ref y);
            _chkSnipShowInCompletion = AddCheckBox(tab, "Show snippets in IntelliSense completions", ref y);
            _chkSnipFormatOnExpand = AddCheckBox(tab, "Format SQL after snippet expansion", ref y);
            _chkSnipContextFilter = AddCheckBox(tab, "Filter snippets by SQL context", ref y);
            _chkSnipTrackUsage = AddCheckBox(tab, "Track snippet usage for ranking", ref y);

            y += 10;
            AddSectionLabel(tab, "Snippet Folders", ref y);
            _txtPersonalFolder = AddTextField(tab, "Personal:", ref y);
            _txtTeamFolder = AddTextField(tab, "Team:", ref y);

            return tab;
        }

        private void LoadSettingsToControls()
        {
            // General
            _chkAutoUpdate.Checked = _settings.AutoUpdateEnabled;
            _chkTelemetry.Checked = _settings.TelemetryEnabled;

            // IntelliSense
            var i = _settings.IntelliSense;
            _chkIsEnabled.Checked = i.Enabled;
            _chkAutoTrigger.Checked = i.AutoTrigger;
            _nudTriggerDelay.Value = Math.Min(Math.Max(i.TriggerDelayMs, 0), 2000);
            _chkAfterDot.Checked = i.AfterDot;
            _nudMaxSuggestions.Value = Math.Min(Math.Max(i.MaxSuggestions, 5), 200);
            _chkFuzzyMatch.Checked = i.FuzzyMatch;
            _chkShowDataTypes.Checked = i.ShowDataTypes;
            _chkShowNullability.Checked = i.ShowNullability;
            _chkShowPkFk.Checked = i.ShowPkFk;
            _chkAutoAlias.Checked = i.AutoAlias;
            _chkJoinAssist.Checked = i.JoinAssist;
            _cboKeywordCase.SelectedIndex = (int)i.KeywordCase;
            _chkDisableNativeIs.Checked = i.DisableNativeIntelliSense;

            // Cache
            var c = _settings.Cache;
            _chkCacheAutoRefresh.Checked = c.AutoRefresh;
            _nudRefreshInterval.Value = Math.Min(Math.Max(c.RefreshIntervalSeconds, 30), 3600);
            _chkDetectDdl.Checked = c.DetectDdl;
            _nudMaxDatabases.Value = Math.Min(Math.Max(c.MaxDatabases, 1), 50);
            _chkLazyLoadColumns.Checked = c.LazyLoadColumns;
            _chkPersistToDisk.Checked = c.PersistToDisk;

            // Formatter
            var f = _settings.Formatter;
            _chkFmtEnabled.Checked = f.Enabled;
            _chkFormatOnPaste.Checked = f.FormatOnPaste;
            _chkFormatOnSave.Checked = f.FormatOnSave;
            _chkFormatOnDelimiter.Checked = f.FormatOnDelimiter;
            _chkConfirmBulk.Checked = f.ConfirmBulkFormat;
            _chkCreateBackups.Checked = f.CreateBackups;
            _chkRespectNoformat.Checked = f.RespectNoformat;
            _chkSemanticValidation.Checked = f.SemanticValidation;

            // Snippets
            var s = _settings.Snippets;
            _chkSnipEnabled.Checked = s.Enabled;
            _chkSnipShowInCompletion.Checked = s.ShowInCompletion;
            _chkSnipFormatOnExpand.Checked = s.FormatOnExpand;
            _chkSnipContextFilter.Checked = s.ContextFilter;
            _chkSnipTrackUsage.Checked = s.TrackUsage;
            _txtPersonalFolder.Text = s.PersonalFolder;
            _txtTeamFolder.Text = s.TeamFolder;
        }

        private void SaveControlsToSettings()
        {
            // General
            _settings.AutoUpdateEnabled = _chkAutoUpdate.Checked;
            _settings.TelemetryEnabled = _chkTelemetry.Checked;

            // IntelliSense
            _settings.IntelliSense.Enabled = _chkIsEnabled.Checked;
            _settings.IntelliSense.AutoTrigger = _chkAutoTrigger.Checked;
            _settings.IntelliSense.TriggerDelayMs = (int)_nudTriggerDelay.Value;
            _settings.IntelliSense.AfterDot = _chkAfterDot.Checked;
            _settings.IntelliSense.MaxSuggestions = (int)_nudMaxSuggestions.Value;
            _settings.IntelliSense.FuzzyMatch = _chkFuzzyMatch.Checked;
            _settings.IntelliSense.ShowDataTypes = _chkShowDataTypes.Checked;
            _settings.IntelliSense.ShowNullability = _chkShowNullability.Checked;
            _settings.IntelliSense.ShowPkFk = _chkShowPkFk.Checked;
            _settings.IntelliSense.AutoAlias = _chkAutoAlias.Checked;
            _settings.IntelliSense.JoinAssist = _chkJoinAssist.Checked;
            _settings.IntelliSense.KeywordCase = (KeywordCaseOption)_cboKeywordCase.SelectedIndex;
            _settings.IntelliSense.DisableNativeIntelliSense = _chkDisableNativeIs.Checked;

            // Cache
            _settings.Cache.AutoRefresh = _chkCacheAutoRefresh.Checked;
            _settings.Cache.RefreshIntervalSeconds = (int)_nudRefreshInterval.Value;
            _settings.Cache.DetectDdl = _chkDetectDdl.Checked;
            _settings.Cache.MaxDatabases = (int)_nudMaxDatabases.Value;
            _settings.Cache.LazyLoadColumns = _chkLazyLoadColumns.Checked;
            _settings.Cache.PersistToDisk = _chkPersistToDisk.Checked;

            // Formatter
            _settings.Formatter.Enabled = _chkFmtEnabled.Checked;
            _settings.Formatter.FormatOnPaste = _chkFormatOnPaste.Checked;
            _settings.Formatter.FormatOnSave = _chkFormatOnSave.Checked;
            _settings.Formatter.FormatOnDelimiter = _chkFormatOnDelimiter.Checked;
            _settings.Formatter.ConfirmBulkFormat = _chkConfirmBulk.Checked;
            _settings.Formatter.CreateBackups = _chkCreateBackups.Checked;
            _settings.Formatter.RespectNoformat = _chkRespectNoformat.Checked;
            _settings.Formatter.SemanticValidation = _chkSemanticValidation.Checked;

            // Snippets
            _settings.Snippets.Enabled = _chkSnipEnabled.Checked;
            _settings.Snippets.ShowInCompletion = _chkSnipShowInCompletion.Checked;
            _settings.Snippets.FormatOnExpand = _chkSnipFormatOnExpand.Checked;
            _settings.Snippets.ContextFilter = _chkSnipContextFilter.Checked;
            _settings.Snippets.TrackUsage = _chkSnipTrackUsage.Checked;
            _settings.Snippets.PersonalFolder = _txtPersonalFolder.Text.Trim();
            _settings.Snippets.TeamFolder = _txtTeamFolder.Text.Trim();
        }

        // --- Layout helpers ---

        private static void AddSectionLabel(TabPage tab, string text, ref int y)
        {
            var label = new Label
            {
                Text = text,
                Font = new Font(Control.DefaultFont, FontStyle.Bold),
                Location = new Point(15, y),
                AutoSize = true
            };
            tab.Controls.Add(label);
            y += 25;
        }

        private static CheckBox AddCheckBox(TabPage tab, string text, ref int y)
        {
            var cb = new CheckBox
            {
                Text = text,
                Location = new Point(30, y),
                AutoSize = true
            };
            tab.Controls.Add(cb);
            y += 26;
            return cb;
        }

        private static NumericUpDown AddNumericField(TabPage tab, string label, int min, int max, ref int y)
        {
            var lbl = new Label
            {
                Text = label,
                Location = new Point(30, y + 2),
                AutoSize = true
            };
            var nud = new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Location = new Point(250, y),
                Size = new Size(80, 22)
            };
            tab.Controls.Add(lbl);
            tab.Controls.Add(nud);
            y += 30;
            return nud;
        }

        private static ComboBox AddComboField(TabPage tab, string label, string[] items, ref int y)
        {
            var lbl = new Label
            {
                Text = label,
                Location = new Point(30, y + 2),
                AutoSize = true
            };
            var cbo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(250, y),
                Size = new Size(130, 22)
            };
            cbo.Items.AddRange(items);
            tab.Controls.Add(lbl);
            tab.Controls.Add(cbo);
            y += 30;
            return cbo;
        }

        private static TextBox AddTextField(TabPage tab, string label, ref int y)
        {
            var lbl = new Label
            {
                Text = label,
                Location = new Point(30, y + 2),
                AutoSize = true
            };
            var txt = new TextBox
            {
                Location = new Point(110, y),
                Size = new Size(380, 22),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            tab.Controls.Add(lbl);
            tab.Controls.Add(txt);
            y += 30;
            return txt;
        }

        private static void AddReadOnlyField(TabPage tab, string label, string value, ref int y)
        {
            var lbl = new Label
            {
                Text = label,
                Location = new Point(30, y + 2),
                AutoSize = true
            };
            var txt = new TextBox
            {
                Text = value,
                Location = new Point(110, y),
                Size = new Size(380, 22),
                ReadOnly = true,
                BackColor = SystemColors.Control,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            tab.Controls.Add(lbl);
            tab.Controls.Add(txt);
            y += 30;
        }
    }
}
