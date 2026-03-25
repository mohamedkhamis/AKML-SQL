using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using AkmlSql.Core.Config;
using AkmlSql.Core.Models.Analysis;
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

        // Code Analysis
        private CheckBox      _chkAnalysisEnabled;
        private CheckBox      _chkAnalysisRunOnType;
        private CheckBox      _chkAnalysisRunOnSave;
        private CheckBox      _chkAnalysisShowInErrorList;
        private DataGridView  _gridRules;

        // Refactoring
        private CheckBox  _chkRefPreviewBeforeApply;
        private CheckBox  _chkRefCreateBackups;
        private CheckBox  _chkRefFormatAfterRefactor;
        private CheckBox  _chkRefIncludeCommentsInRename;
        private CheckBox  _chkRefIncludeStringLiteralsInRename;
        private ComboBox  _cboRefRenameScope;

        // History
        private CheckBox _chkHistEnabled;
        private NumericUpDown _nudHistRetentionDays;
        private NumericUpDown _nudHistMaxEntries;
        private CheckBox _chkHistEncryptAtRest;
        private CheckBox _chkHistRecordFailures;
        private CheckBox _chkHistDeduplication;

        // Tabs
        private CheckBox _chkTabColoringEnabled;
        private DataGridView _gridColoringRules;
        private CheckBox _chkTabSessionRecovery;
        private NumericUpDown _nudTabAutoSaveInterval;
        private ComboBox _cboTabRestoreOnStartup;
        private NumericUpDown _nudTabMaxClosedTabs;
        private TextBox _txtTabCustomWindowTitle;

        // Safety
        private CheckBox _chkSafetyProductionWarning;
        private CheckBox _chkSafetyDeleteWithoutWhere;
        private CheckBox _chkSafetyUpdateWithoutWhere;
        private CheckBox _chkSafetyDropConfirmation;
        private CheckBox _chkSafetyTruncateConfirmation;
        private CheckBox _chkSafetyTransactionReminder;
        private NumericUpDown _nudSafetyTransactionReminderInterval;

        // Phase 8 — Editor Productivity
        private CheckBox _chkHighlightOccurrences;
        private CheckBox _chkBracketMatching;
        private CheckBox _chkNamedRegions;
        private CheckBox _chkStickyScroll;
        private CheckBox _chkMinimap;
        private CheckBox _chkDocumentOutline;

        // Phase 8 — Execution Productivity
        private NumericUpDown _nudNotificationThreshold;
        private CheckBox _chkShowExecutionTimer;
        private CheckBox _chkMultiDatabase;

        // Phase 8 — Navigation
        private CheckBox _chkGoToDefinition;
        private CheckBox _chkPeekDefinition;
        private CheckBox _chkFindReferences;
        private CheckBox _chkObjectSearch;
        private DataGridView _gridConnectionAliases;

        // Phase 8 — Grid
        private CheckBox _chkGridAggregates;
        private CheckBox _chkGridNullHighlight;
        private CheckBox _chkGridRowNumbers;
        private CheckBox _chkGridFreezeHeaders;

        // Phase 9 — AI Assistant
        private ComboBox  _cboAiProvider;
        private TextBox   _txtAiModel;
        private TextBox   _txtAiApiKey;
        private TextBox   _txtAiEndpoint;
        private RadioButton _rbPrivacyFull;
        private RadioButton _rbPrivacySchemaOnly;
        private RadioButton _rbPrivacyAnonymous;
        private RadioButton _rbPrivacyOffline;
        private RadioButton _rbPrivacyDisabled;
        private CheckBox  _chkAiTextToSql;
        private CheckBox  _chkAiExplain;
        private CheckBox  _chkAiFix;
        private CheckBox  _chkAiOptimize;
        private CheckBox  _chkAiIndexSuggestions;
        private CheckBox  _chkAiChatPanel;
        private CheckBox  _chkAiInlineCompletion;
        private CheckBox  _chkAiAutoFixOnError;
        private NumericUpDown _nudAiMaxTokens;
        private NumericUpDown _nudAiTemperature;
        private NumericUpDown _nudAiTimeout;
        private NumericUpDown _nudAiRetries;
        private Button    _btnAiTestConnection;
        private Label     _lblAiTestResult;
        private ComboBox  _cboAiOfflineProvider;
        private TextBox   _txtAiOfflineModel;
        private TextBox   _txtAiOfflineEndpoint;

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
            Text            = Constants.ProductName + " Options";
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox     = false;
            MinimizeBox     = false;
            StartPosition   = FormStartPosition.CenterParent;
            Size            = new Size(620, 600);
            MinimumSize     = new Size(540, 500);
            ShowInTaskbar   = false;
            BackColor       = Color.FromArgb(250, 250, 252);

            var tabControl = new TabControl { Dock = DockStyle.Fill };
            tabControl.TabPages.Add(CreateGeneralTab());
            tabControl.TabPages.Add(CreateIntelliSenseTab());
            tabControl.TabPages.Add(CreateCacheTab());
            tabControl.TabPages.Add(CreateFormatterTab());
            tabControl.TabPages.Add(CreateSnippetsTab());
            tabControl.TabPages.Add(CreateCodeAnalysisTab());
            tabControl.TabPages.Add(CreateRefactoringTab());
            tabControl.TabPages.Add(CreateHistoryTab());
            tabControl.TabPages.Add(CreateTabsTab());
            tabControl.TabPages.Add(CreateSafetyTab());
            tabControl.TabPages.Add(CreateEditorProductivityTab());
            tabControl.TabPages.Add(CreateExecutionProductivityTab());
            tabControl.TabPages.Add(CreateNavigationTab());
            tabControl.TabPages.Add(CreateGridTab());
            tabControl.TabPages.Add(CreateAiAssistantTab());

            // ─── Button panel ─────────────────────────────────────────────────
            var buttonPanel = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 54,
                BackColor = Color.FromArgb(242, 242, 245)
            };

            var separator = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 1,
                BackColor = Color.FromArgb(210, 210, 215)
            };

            var applyButton = new Button
            {
                Text      = "Apply",
                Size      = new Size(90, 30),
                FlatStyle = FlatStyle.System
            };
            var saveButton = new Button
            {
                Text         = "Save All",
                Size         = new Size(90, 30),
                FlatStyle    = FlatStyle.System,
                DialogResult = DialogResult.OK,
                Font         = new Font(DefaultFont, FontStyle.Bold)
            };
            var cancelButton = new Button
            {
                Text         = "Cancel",
                Size         = new Size(90, 30),
                FlatStyle    = FlatStyle.System,
                DialogResult = DialogResult.Cancel
            };

            // Apply saves settings without closing the dialog
            applyButton.Click += (_, __) =>
            {
                SaveControlsToSettings();
                try   { ConfigManager.Save(_settings); }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "SettingsDialog: Apply failed");
                    MessageBox.Show("Failed to apply settings: " + ex.Message,
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            // Position all buttons flush-right using a Layout event
            buttonPanel.Layout += (_, __) =>
            {
                int top = (buttonPanel.Height - 30) / 2 + 1;
                cancelButton.Location = new Point(buttonPanel.Width - 100, top);
                saveButton.Location   = new Point(buttonPanel.Width - 198, top);
                applyButton.Location  = new Point(buttonPanel.Width - 296, top);
            };

            AcceptButton = saveButton;
            CancelButton = cancelButton;
            buttonPanel.Controls.Add(separator);
            buttonPanel.Controls.Add(applyButton);
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

        // ─── Static rule catalog (shell side — no Engine DLL required) ───────────────
        private static readonly (string Id, string Category, string Description, string DefaultSeverity)[] KnownRules =
        [
            ("PE001", "Performance",  "Avoid SELECT * in stored procedures",                    "Warning"),
            ("PE002", "Performance",  "Unqualified object name — add schema prefix",             "Warning"),
            ("PE003", "Performance",  "DELETE/UPDATE without WHERE clause",                      "Error"),
            ("PE004", "Performance",  "LIKE pattern with leading wildcard — forces table scan",  "Warning"),
            ("PE009", "Performance",  "Stored procedure missing SET NOCOUNT ON",                 "Warning"),
            ("PE010", "Performance",  "EXISTS (SELECT *) — replace with SELECT 1",              "Warning"),
            ("BP001", "BestPractice", "Use SCOPE_IDENTITY() instead of @@IDENTITY",             "Warning"),
            ("BP004", "BestPractice", "Use IS NULL / IS NOT NULL instead of = NULL",            "Error"),
            ("SE001", "Security",     "Dynamic SQL string concatenation — SQL injection risk",   "Error"),
            ("DEP001","Deprecated",   "Deprecated data type: text / ntext / image",              "Warning"),
        ];

        private TabPage CreateCodeAnalysisTab()
        {
            var tab = new TabPage("Code Analysis") { AutoScroll = true };
            var y = 20;

            AddSectionLabel(tab, "General", ref y);
            _chkAnalysisEnabled         = AddCheckBox(tab, "Enable code analysis",         ref y);
            _chkAnalysisRunOnType       = AddCheckBox(tab, "Analyze while typing",         ref y);
            _chkAnalysisRunOnSave       = AddCheckBox(tab, "Analyze on save",              ref y);
            _chkAnalysisShowInErrorList = AddCheckBox(tab, "Show issues in Error List",    ref y);

            y += 10;
            AddSectionLabel(tab, "Rules", ref y);

            // DataGridView -------------------------------------------------------
            var colEnabled  = new DataGridViewCheckBoxColumn { HeaderText = "Enabled",  Width = 65,  ReadOnly = false, Name = "Enabled" };
            var colId       = new DataGridViewTextBoxColumn  { HeaderText = "Rule ID",  Width = 75,  ReadOnly = true,  Name = "RuleId" };
            var colCategory = new DataGridViewTextBoxColumn  { HeaderText = "Category", Width = 100, ReadOnly = true,  Name = "Category" };
            var colDesc     = new DataGridViewTextBoxColumn  { HeaderText = "Description", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true, Name = "Description" };
            var colSeverity = new DataGridViewComboBoxColumn
            {
                HeaderText = "Severity", Width = 100, Name = "Severity",
                DataSource = new[] { "Error", "Warning", "Information", "Hint" }
            };

            _gridRules = new DataGridView
            {
                Location           = new Point(15, y),
                Size               = new Size(tab.ClientSize.Width > 0 ? tab.ClientSize.Width - 30 : 530, 200),
                Anchor             = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                AllowUserToAddRows    = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible     = false,
                AutoSizeRowsMode      = DataGridViewAutoSizeRowsMode.AllCells,
                SelectionMode         = DataGridViewSelectionMode.FullRowSelect,
            };
            _gridRules.Columns.AddRange(colEnabled, colId, colCategory, colDesc, colSeverity);

            foreach (var (id, cat, desc, sev) in KnownRules)
                _gridRules.Rows.Add(true, id, cat, desc, sev);

            tab.Controls.Add(_gridRules);
            y += 210;

            // Import / Export buttons --------------------------------------------
            var btnImport = new Button { Text = "Import CAsettings...", Location = new Point(15, y), Size = new Size(160, 28) };
            var btnExport = new Button { Text = "Export CAsettings...", Location = new Point(185, y), Size = new Size(160, 28) };
            btnImport.Click += OnImportCaSettings;
            btnExport.Click += OnExportCaSettings;
            tab.Controls.Add(btnImport);
            tab.Controls.Add(btnExport);

            return tab;
        }

        private TabPage CreateRefactoringTab()
        {
            var tab = new TabPage("Refactoring");
            tab.AutoScroll = true;
            var y = 20;

            AddSectionLabel(tab, "Preview & Safety", ref y);
            _chkRefPreviewBeforeApply  = AddCheckBox(tab, "Show preview dialog before applying changes", ref y);
            _chkRefCreateBackups       = AddCheckBox(tab, "Create backups before applying refactoring", ref y);
            _chkRefFormatAfterRefactor = AddCheckBox(tab, "Format SQL after refactoring", ref y);

            y += 10;
            AddSectionLabel(tab, "Rename Options", ref y);
            _chkRefIncludeCommentsInRename      = AddCheckBox(tab, "Include comment text in rename scope", ref y);
            _chkRefIncludeStringLiteralsInRename = AddCheckBox(tab, "Include string literals in rename scope", ref y);
            _cboRefRenameScope = AddComboField(tab, "Rename scope:",
                ["Current Script", "Project Directory"], ref y);

            return tab;
        }

        private TabPage CreateHistoryTab()
        {
            var tab = new TabPage("History") { AutoScroll = true };
            var y = 20;

            AddSectionLabel(tab, "SQL History Recording", ref y);
            _chkHistEnabled = AddCheckBox(tab, "Enable SQL history recording", ref y);
            _nudHistRetentionDays = AddNumericField(tab, "Retention days:", 1, 3650, ref y);
            _nudHistMaxEntries = AddNumericField(tab, "Max entries:", 1000, 10_000_000, ref y);
            _chkHistEncryptAtRest = AddCheckBox(tab, "Encrypt history at rest (DPAPI + AES-256)", ref y);
            _chkHistRecordFailures = AddCheckBox(tab, "Record failed executions", ref y);
            _chkHistDeduplication = AddCheckBox(tab, "Enable deduplication in search", ref y);

            return tab;
        }

        private TabPage CreateTabsTab()
        {
            var tab = new TabPage("Tabs") { AutoScroll = true };
            var y = 20;

            AddSectionLabel(tab, "Tab Coloring", ref y);
            _chkTabColoringEnabled = AddCheckBox(tab, "Enable environment-based tab coloring", ref y);

            // DataGridView for coloring rules
            var colOrder   = new DataGridViewTextBoxColumn  { HeaderText = "Order",   Width = 55,  Name = "Order" };
            var colPattern = new DataGridViewTextBoxColumn  { HeaderText = "Pattern", Width = 180, Name = "Pattern" };
            var colColor   = new DataGridViewTextBoxColumn  { HeaderText = "Color",   Width = 80,  Name = "Color" };
            var colLabel   = new DataGridViewTextBoxColumn  { HeaderText = "Label",   AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, Name = "Label" };

            _gridColoringRules = new DataGridView
            {
                Location           = new Point(15, y),
                Size               = new Size(tab.ClientSize.Width > 0 ? tab.ClientSize.Width - 30 : 530, 140),
                Anchor             = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                AllowUserToAddRows    = true,
                AllowUserToDeleteRows = true,
                RowHeadersVisible     = true,
                AutoSizeRowsMode      = DataGridViewAutoSizeRowsMode.AllCells,
                SelectionMode         = DataGridViewSelectionMode.FullRowSelect,
            };
            _gridColoringRules.Columns.AddRange(colOrder, colPattern, colColor, colLabel);

            tab.Controls.Add(_gridColoringRules);
            y += 150;

            y += 10;
            AddSectionLabel(tab, "Session Recovery", ref y);
            _chkTabSessionRecovery = AddCheckBox(tab, "Enable session auto-save and crash recovery", ref y);
            _nudTabAutoSaveInterval = AddNumericField(tab, "Auto-save interval (seconds):", 30, 300, ref y);
            _cboTabRestoreOnStartup = AddComboField(tab, "Restore on startup:",
                ["prompt", "always", "never"], ref y);
            _nudTabMaxClosedTabs = AddNumericField(tab, "Max closed tabs to remember:", 1, 100, ref y);

            y += 10;
            AddSectionLabel(tab, "Window Title", ref y);
            _txtTabCustomWindowTitle = AddTextField(tab, "Title template:", ref y);
            AddHintLabel(tab, "Tokens: {server}, {database}, {user}, {file}", ref y);

            return tab;
        }

        private TabPage CreateSafetyTab()
        {
            var tab = new TabPage("Safety") { AutoScroll = true };
            var y = 20;

            AddSectionLabel(tab, "Execution Safety Warnings", ref y);
            _chkSafetyProductionWarning = AddCheckBox(tab, "Warn before executing DML/DDL on production servers", ref y);
            _chkSafetyDeleteWithoutWhere = AddCheckBox(tab, "Warn on DELETE without WHERE clause", ref y);
            _chkSafetyUpdateWithoutWhere = AddCheckBox(tab, "Warn on UPDATE without WHERE clause", ref y);
            _chkSafetyDropConfirmation = AddCheckBox(tab, "Confirm DROP TABLE / DROP DATABASE", ref y);
            _chkSafetyTruncateConfirmation = AddCheckBox(tab, "Confirm TRUNCATE TABLE", ref y);

            y += 10;
            AddSectionLabel(tab, "Transaction Monitoring", ref y);
            _chkSafetyTransactionReminder = AddCheckBox(tab, "Remind about uncommitted transactions", ref y);
            _nudSafetyTransactionReminderInterval = AddNumericField(tab, "Reminder interval (seconds):", 30, 3600, ref y);

            return tab;
        }

        private TabPage CreateEditorProductivityTab()
        {
            var tab = new TabPage("Editor") { AutoScroll = true };
            var y = 20;

            AddSectionLabel(tab, "Editor Productivity (Phase 8)", ref y);
            _chkHighlightOccurrences = AddCheckBox(tab, "Highlight occurrences of selected identifier", ref y);
            _chkBracketMatching      = AddCheckBox(tab, "Bracket / BEGIN-END matching", ref y);
            _chkNamedRegions         = AddCheckBox(tab, "Support named regions (--region / --endregion)", ref y);
            _chkStickyScroll         = AddCheckBox(tab, "Sticky scroll (pin scope context at top)", ref y);
            _chkMinimap              = AddCheckBox(tab, "Show minimap overview in right margin", ref y);
            _chkDocumentOutline      = AddCheckBox(tab, "Enable document outline panel", ref y);

            return tab;
        }

        private TabPage CreateExecutionProductivityTab()
        {
            var tab = new TabPage("Execution") { AutoScroll = true };
            var y = 20;

            AddSectionLabel(tab, "Execution Productivity (Phase 8)", ref y);
            _chkShowExecutionTimer  = AddCheckBox(tab, "Show execution timer in status bar", ref y);
            _nudNotificationThreshold = AddNumericField(tab, "Notification threshold (seconds):", 0, 3600, ref y);
            AddHintLabel(tab, "Toast notification when query runs longer than threshold. 0 = disabled.", ref y);
            _chkMultiDatabase       = AddCheckBox(tab, "Enable multi-database execution", ref y);

            return tab;
        }

        private TabPage CreateNavigationTab()
        {
            var tab = new TabPage("Navigation") { AutoScroll = true };
            var y = 20;

            AddSectionLabel(tab, "Navigation Features (Phase 8)", ref y);
            _chkGoToDefinition  = AddCheckBox(tab, "Enable Go To Definition (F12)", ref y);
            _chkPeekDefinition  = AddCheckBox(tab, "Enable Peek Definition (Alt+F12)", ref y);
            _chkFindReferences  = AddCheckBox(tab, "Enable Find All References (Shift+F12)", ref y);
            _chkObjectSearch    = AddCheckBox(tab, "Enable Object Search (Ctrl+T)", ref y);

            y += 10;
            _gridConnectionAliases = Productivity.ConnectionAliasEditor.CreateAliasGrid(tab, ref y);

            return tab;
        }

        private TabPage CreateGridTab()
        {
            var tab = new TabPage("Grid") { AutoScroll = true };
            var y = 20;

            AddSectionLabel(tab, "Results Grid Enhancements (Phase 8)", ref y);
            _chkGridAggregates    = AddCheckBox(tab, "Show aggregates (SUM, AVG, COUNT) in status bar for selected cells", ref y);
            _chkGridNullHighlight = AddCheckBox(tab, "Highlight NULL values with distinct background", ref y);
            _chkGridRowNumbers    = AddCheckBox(tab, "Show row numbers", ref y);
            _chkGridFreezeHeaders = AddCheckBox(tab, "Freeze column headers on scroll", ref y);

            return tab;
        }

        private TabPage CreateAiAssistantTab()
        {
            var tab = new TabPage("AI Assistant") { AutoScroll = true };
            var y = 20;

            // ─── Provider Configuration ──────────────────────────────────────
            AddSectionLabel(tab, "Provider Configuration", ref y);
            _cboAiProvider = AddComboField(tab, "Provider:",
                ["(None)", "Anthropic Claude", "OpenAI GPT", "Azure OpenAI",
                 "Google Gemini", "Ollama (Local)", "LM Studio (Local)", "Custom Endpoint"], ref y);
            _txtAiModel = AddTextField(tab, "Model:", ref y);
            AddHintLabel(tab, "e.g. claude-sonnet-4-20250514, gpt-4o, gemini-pro", ref y);

            // API Key (password-masked)
            var lblKey = new Label { Text = "API Key:", Location = new Point(30, y + 2), AutoSize = true };
            _txtAiApiKey = new TextBox
            {
                Location = new Point(110, y),
                Size = new Size(380, 22),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                UseSystemPasswordChar = true
            };
            tab.Controls.Add(lblKey);
            tab.Controls.Add(_txtAiApiKey);
            y += 30;

            _txtAiEndpoint = AddTextField(tab, "Endpoint:", ref y);
            AddHintLabel(tab, "Required for Azure OpenAI, Ollama, LM Studio, Custom Endpoint", ref y);

            // ─── Privacy Mode ────────────────────────────────────────────────
            y += 10;
            AddSectionLabel(tab, "Privacy Mode", ref y);
            _rbPrivacyFull       = AddRadioButton(tab, "Full — send query text and schema metadata", ref y);
            _rbPrivacySchemaOnly = AddRadioButton(tab, "Schema Only — send metadata only, no query text (default)", ref y);
            _rbPrivacyAnonymous  = AddRadioButton(tab, "Anonymous — strip identifiers before sending", ref y);
            _rbPrivacyOffline    = AddRadioButton(tab, "Offline — use only local/offline provider", ref y);
            _rbPrivacyDisabled   = AddRadioButton(tab, "Disabled — no data sent to any AI provider", ref y);

            // ─── Feature Toggles ─────────────────────────────────────────────
            y += 10;
            AddSectionLabel(tab, "Feature Toggles", ref y);
            _chkAiTextToSql        = AddCheckBox(tab, "Text-to-SQL (natural language query generation)", ref y);
            _chkAiExplain          = AddCheckBox(tab, "Explain (AI-powered SQL explanation)", ref y);
            _chkAiFix              = AddCheckBox(tab, "Fix (AI-powered error fix suggestions)", ref y);
            _chkAiOptimize         = AddCheckBox(tab, "Optimize (AI-powered query optimization)", ref y);
            _chkAiIndexSuggestions = AddCheckBox(tab, "Index Suggestions", ref y);
            _chkAiChatPanel        = AddCheckBox(tab, "Chat Panel (AI side panel)", ref y);
            _chkAiInlineCompletion = AddCheckBox(tab, "Inline Completion (ghost-text suggestions)", ref y);
            _chkAiAutoFixOnError   = AddCheckBox(tab, "Auto-Fix on Error (suggest fixes when queries fail)", ref y);

            // ─── Configuration ───────────────────────────────────────────────
            y += 10;
            AddSectionLabel(tab, "Configuration", ref y);
            _nudAiMaxTokens   = AddNumericField(tab, "Max tokens:", 128, 128000, ref y);
            _nudAiTemperature = AddNumericField(tab, "Temperature (x10):", 0, 20, ref y);
            AddHintLabel(tab, "Value is divided by 10 (e.g. 2 = 0.2). Range: 0.0 - 2.0", ref y);
            _nudAiTimeout     = AddNumericField(tab, "Timeout (seconds):", 5, 300, ref y);
            _nudAiRetries     = AddNumericField(tab, "Retries:", 0, 10, ref y);

            // ─── Test Connection ─────────────────────────────────────────────
            y += 10;
            AddSectionLabel(tab, "Connection Test", ref y);
            _btnAiTestConnection = new Button
            {
                Text = "Test Connection",
                Location = new Point(30, y),
                Size = new Size(130, 28)
            };
            _lblAiTestResult = new Label
            {
                Text = string.Empty,
                Location = new Point(170, y + 4),
                AutoSize = true,
                ForeColor = Color.Gray
            };
            _btnAiTestConnection.Click += OnAiTestConnection;
            tab.Controls.Add(_btnAiTestConnection);
            tab.Controls.Add(_lblAiTestResult);
            y += 36;

            // ─── Offline Fallback ────────────────────────────────────────────
            y += 10;
            AddSectionLabel(tab, "Offline Fallback Provider", ref y);
            AddHintLabel(tab, "Used when the primary provider is unreachable or privacy mode is Offline.", ref y);
            _cboAiOfflineProvider = AddComboField(tab, "Provider:",
                ["(None)", "Ollama (Local)", "LM Studio (Local)", "Custom Endpoint"], ref y);
            _txtAiOfflineModel    = AddTextField(tab, "Model:", ref y);
            _txtAiOfflineEndpoint = AddTextField(tab, "Endpoint:", ref y);

            return tab;
        }

        private static RadioButton AddRadioButton(TabPage tab, string text, ref int y)
        {
            var rb = new RadioButton
            {
                Text = text,
                Location = new Point(30, y),
                AutoSize = true
            };
            tab.Controls.Add(rb);
            y += 24;
            return rb;
        }

        private async void OnAiTestConnection(object sender, EventArgs e)
        {
            _lblAiTestResult.ForeColor = Color.Gray;
            _lblAiTestResult.Text = "Testing...";
            _btnAiTestConnection.Enabled = false;

            try
            {
                var manager = Ipc.EngineLifecycle.Manager;
                if (manager?.Client == null || !manager.Client.IsConnected)
                {
                    _lblAiTestResult.ForeColor = Color.Red;
                    _lblAiTestResult.Text = "Engine not connected";
                    return;
                }

                var providerMap = new Dictionary<int, string>
                {
                    { 1, "Anthropic" }, { 2, "OpenAI" }, { 3, "AzureOpenAI" },
                    { 4, "Gemini" }, { 5, "Ollama" }, { 6, "LMStudio" }, { 7, "Custom" }
                };

                var providerIndex = _cboAiProvider.SelectedIndex;
                if (providerIndex < 1 || !providerMap.TryGetValue(providerIndex, out var providerName))
                {
                    _lblAiTestResult.ForeColor = Color.Red;
                    _lblAiTestResult.Text = "Select a provider first";
                    return;
                }

                var request = new Core.Ipc.Messages.AiProviderTestRequest
                {
                    Provider = providerName,
                    ApiKey   = _txtAiApiKey.Text.Trim(),
                    Model    = _txtAiModel.Text.Trim(),
                    Endpoint = _txtAiEndpoint.Text.Trim()
                };

                var response = await manager.Client
                    .SendRequestAsync<Core.Ipc.Messages.AiProviderTestResponse, Core.Ipc.Messages.AiProviderTestRequest>(
                        Core.Ipc.MessageTypes.AiProviderTest, request, timeoutMs: 30000);

                if (response.Success)
                {
                    _lblAiTestResult.ForeColor = Color.Green;
                    _lblAiTestResult.Text = $"Connected ({response.LatencyMs} ms)";
                    if (!string.IsNullOrEmpty(response.ModelName))
                        _lblAiTestResult.Text += $" — {response.ModelName}";
                }
                else
                {
                    _lblAiTestResult.ForeColor = Color.Red;
                    _lblAiTestResult.Text = response.ErrorMessage ?? "Test failed";
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "SettingsDialog: AI provider test failed");
                _lblAiTestResult.ForeColor = Color.Red;
                _lblAiTestResult.Text = "Error: " + ex.Message;
            }
            finally
            {
                _btnAiTestConnection.Enabled = true;
            }
        }

        private void OnImportCaSettings(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Title  = "Import CAsettings",
                Filter = "CAsettings files (*.casettings*;*.json)|*.casettings;*.casettings.json;*.json|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            try
            {
                var json = File.ReadAllText(dlg.FileName);
                var ca   = JsonSerializer.Deserialize<CaSettings>(json,
                               new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (ca?.Rules == null) return;

                foreach (DataGridViewRow row in _gridRules.Rows)
                {
                    var ruleId = row.Cells["RuleId"].Value as string;
                    if (ruleId != null && ca.Rules.TryGetValue(ruleId, out var cfg))
                    {
                        row.Cells["Enabled"].Value  = cfg.Enabled;
                        row.Cells["Severity"].Value = cfg.Severity ?? "Warning";
                    }
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "SettingsDialog: import CAsettings failed");
                MessageBox.Show("Failed to import: " + ex.Message, Constants.ProductName,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnExportCaSettings(object sender, EventArgs e)
        {
            using var dlg = new SaveFileDialog
            {
                Title      = "Export CAsettings",
                Filter     = "CAsettings JSON (*.casettings)|*.casettings|JSON file (*.json)|*.json",
                FileName   = ".casettings",
                DefaultExt = "casettings"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            try
            {
                var rules = new Dictionary<string, RuleConfig>();
                foreach (DataGridViewRow row in _gridRules.Rows)
                {
                    var ruleId = row.Cells["RuleId"].Value as string;
                    if (string.IsNullOrEmpty(ruleId)) continue;
                    rules[ruleId] = new RuleConfig
                    {
                        Enabled  = row.Cells["Enabled"].Value is true,
                        Severity = row.Cells["Severity"].Value as string ?? "Warning"
                    };
                }
                var ca  = new CaSettings { Rules = rules };
                var opt = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(ca, opt));
                MessageBox.Show("Settings exported to " + dlg.FileName, Constants.ProductName,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "SettingsDialog: export CAsettings failed");
                MessageBox.Show("Failed to export: " + ex.Message, Constants.ProductName,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
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

            // Code Analysis
            var ca = _settings.CodeAnalysis;
            _chkAnalysisEnabled.Checked         = ca.Enabled;
            _chkAnalysisRunOnType.Checked        = ca.RunOnType;
            _chkAnalysisRunOnSave.Checked        = ca.RunOnSave;
            _chkAnalysisShowInErrorList.Checked  = ca.ShowInErrorList;

            // Refactoring
            var rf = _settings.Refactoring;
            _chkRefPreviewBeforeApply.Checked            = rf.PreviewBeforeApply;
            _chkRefCreateBackups.Checked                 = rf.CreateBackups;
            _chkRefFormatAfterRefactor.Checked           = rf.FormatAfterRefactor;
            _chkRefIncludeCommentsInRename.Checked       = rf.IncludeCommentsInRename;
            _chkRefIncludeStringLiteralsInRename.Checked = rf.IncludeStringLiteralsInRename;
            _cboRefRenameScope.SelectedIndex = rf.RenameScope == "projectDirectory" ? 1 : 0;

            // History
            var h = _settings.History;
            _chkHistEnabled.Checked = h.Enabled;
            _nudHistRetentionDays.Value = Math.Min(Math.Max(h.RetentionDays, 1), 3650);
            _nudHistMaxEntries.Value = Math.Min(Math.Max(h.MaxEntries, 1000), 10_000_000);
            _chkHistEncryptAtRest.Checked = h.EncryptAtRest;
            _chkHistRecordFailures.Checked = h.RecordFailures;
            _chkHistDeduplication.Checked = h.Deduplication;

            // Tabs
            var t = _settings.Tabs;
            _chkTabColoringEnabled.Checked = t.ColoringEnabled;
            _gridColoringRules.Rows.Clear();
            foreach (var rule in t.ColoringRules)
            {
                _gridColoringRules.Rows.Add(rule.Order.ToString(), rule.Pattern, rule.Color, rule.Label);
            }
            _chkTabSessionRecovery.Checked = t.SessionRecovery;
            _nudTabAutoSaveInterval.Value = Math.Min(Math.Max(t.AutoSaveInterval, 30), 300);
            _cboTabRestoreOnStartup.SelectedIndex = t.RestoreOnStartup?.ToLowerInvariant() switch
            {
                "always" => 1,
                "never"  => 2,
                _        => 0 // "prompt"
            };
            _nudTabMaxClosedTabs.Value = Math.Min(Math.Max(t.MaxClosedTabs, 1), 100);
            _txtTabCustomWindowTitle.Text = t.CustomWindowTitle ?? string.Empty;

            // Safety
            var sf = _settings.Safety;
            _chkSafetyProductionWarning.Checked = sf.ProductionWarning;
            _chkSafetyDeleteWithoutWhere.Checked = sf.DeleteWithoutWhere;
            _chkSafetyUpdateWithoutWhere.Checked = sf.UpdateWithoutWhere;
            _chkSafetyDropConfirmation.Checked = sf.DropConfirmation;
            _chkSafetyTruncateConfirmation.Checked = sf.TruncateConfirmation;
            _chkSafetyTransactionReminder.Checked = sf.TransactionReminder;
            _nudSafetyTransactionReminderInterval.Value = Math.Min(Math.Max(sf.TransactionReminderInterval, 30), 3600);

            // Phase 8 — Editor Productivity
            var ep = _settings.EditorProductivity;
            _chkHighlightOccurrences.Checked = ep.HighlightOccurrences;
            _chkBracketMatching.Checked      = ep.BracketMatching;
            _chkNamedRegions.Checked         = ep.NamedRegions;
            _chkStickyScroll.Checked         = ep.StickyScroll;
            _chkMinimap.Checked              = ep.Minimap;
            _chkDocumentOutline.Checked      = ep.DocumentOutline;

            // Phase 8 — Execution Productivity
            var xp = _settings.ExecutionProductivity;
            _chkShowExecutionTimer.Checked    = xp.ShowExecutionTimer;
            _nudNotificationThreshold.Value   = Math.Min(Math.Max(xp.NotificationThreshold, 0), 3600);
            _chkMultiDatabase.Checked         = xp.MultiDatabase;

            // Phase 8 — Navigation
            var nav = _settings.Navigation;
            _chkGoToDefinition.Checked  = nav.GoToDefinition;
            _chkPeekDefinition.Checked  = nav.PeekDefinition;
            _chkFindReferences.Checked  = nav.FindReferences;
            _chkObjectSearch.Checked    = nav.ObjectSearch;
            Productivity.ConnectionAliasEditor.LoadAliases(_gridConnectionAliases, nav);

            // Phase 8 — Grid
            var gr = _settings.Grid;
            _chkGridAggregates.Checked    = gr.Aggregates;
            _chkGridNullHighlight.Checked = gr.NullHighlight;
            _chkGridRowNumbers.Checked    = gr.RowNumbers;
            _chkGridFreezeHeaders.Checked = gr.FreezeHeaders;

            // Phase 9 — AI Assistant
            var ai = _settings.Ai;
            _cboAiProvider.SelectedIndex = (ai.Provider?.ToLowerInvariant()) switch
            {
                "anthropic"  => 1,
                "openai"     => 2,
                "azureopenai"=> 3,
                "gemini"     => 4,
                "ollama"     => 5,
                "lmstudio"   => 6,
                "custom"     => 7,
                _            => 0  // "(None)"
            };
            _txtAiModel.Text    = ai.Model ?? string.Empty;
            _txtAiApiKey.Text   = ai.ApiKey ?? string.Empty;
            _txtAiEndpoint.Text = ai.Endpoint ?? string.Empty;

            switch (ai.PrivacyMode?.ToLowerInvariant())
            {
                case "full":       _rbPrivacyFull.Checked = true; break;
                case "anonymous":  _rbPrivacyAnonymous.Checked = true; break;
                case "offline":    _rbPrivacyOffline.Checked = true; break;
                case "disabled":   _rbPrivacyDisabled.Checked = true; break;
                default:           _rbPrivacySchemaOnly.Checked = true; break;
            }

            _chkAiTextToSql.Checked        = ai.TextToSql;
            _chkAiExplain.Checked          = ai.Explain;
            _chkAiFix.Checked              = ai.Fix;
            _chkAiOptimize.Checked         = ai.Optimize;
            _chkAiIndexSuggestions.Checked = ai.IndexSuggestions;
            _chkAiChatPanel.Checked        = ai.ChatPanel;
            _chkAiInlineCompletion.Checked = ai.InlineCompletion;
            _chkAiAutoFixOnError.Checked   = ai.AutoFixOnError;

            _nudAiMaxTokens.Value   = Math.Min(Math.Max(ai.MaxTokens, 128), 128000);
            _nudAiTemperature.Value = Math.Min(Math.Max((int)(ai.Temperature * 10), 0), 20);
            _nudAiTimeout.Value     = Math.Min(Math.Max(ai.Timeout, 5), 300);
            _nudAiRetries.Value     = Math.Min(Math.Max(ai.Retries, 0), 10);

            _cboAiOfflineProvider.SelectedIndex = (ai.OfflineProvider?.ToLowerInvariant()) switch
            {
                "ollama"   => 1,
                "lmstudio" => 2,
                "custom"   => 3,
                _          => 0
            };
            _txtAiOfflineModel.Text    = ai.OfflineModel ?? string.Empty;
            _txtAiOfflineEndpoint.Text = ai.OfflineEndpoint ?? string.Empty;

            // Populate rule grid from user-level .casettings file
            try
            {
                var settingsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AKML SQL", ".casettings");
                if (File.Exists(settingsPath))
                {
                    var saved = JsonSerializer.Deserialize<CaSettings>(
                        File.ReadAllText(settingsPath),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (saved?.Rules != null)
                    {
                        foreach (DataGridViewRow row in _gridRules.Rows)
                        {
                            var ruleId = row.Cells["RuleId"].Value as string;
                            if (ruleId != null && saved.Rules.TryGetValue(ruleId, out var cfg))
                            {
                                row.Cells["Enabled"].Value  = cfg.Enabled;
                                row.Cells["Severity"].Value = cfg.Severity ?? "Warning";
                            }
                        }
                    }
                }
            }
            catch { /* ignore; defaults are already populated */ }
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

            // Code Analysis
            _settings.CodeAnalysis.Enabled         = _chkAnalysisEnabled.Checked;
            _settings.CodeAnalysis.RunOnType        = _chkAnalysisRunOnType.Checked;
            _settings.CodeAnalysis.RunOnSave        = _chkAnalysisRunOnSave.Checked;
            _settings.CodeAnalysis.ShowInErrorList  = _chkAnalysisShowInErrorList.Checked;

            // Refactoring
            _settings.Refactoring.PreviewBeforeApply            = _chkRefPreviewBeforeApply.Checked;
            _settings.Refactoring.CreateBackups                 = _chkRefCreateBackups.Checked;
            _settings.Refactoring.FormatAfterRefactor           = _chkRefFormatAfterRefactor.Checked;
            _settings.Refactoring.IncludeCommentsInRename       = _chkRefIncludeCommentsInRename.Checked;
            _settings.Refactoring.IncludeStringLiteralsInRename = _chkRefIncludeStringLiteralsInRename.Checked;
            _settings.Refactoring.RenameScope = _cboRefRenameScope.SelectedIndex == 1
                ? "projectDirectory"
                : "currentScript";

            // History
            _settings.History.Enabled = _chkHistEnabled.Checked;
            _settings.History.RetentionDays = (int)_nudHistRetentionDays.Value;
            _settings.History.MaxEntries = (int)_nudHistMaxEntries.Value;
            _settings.History.EncryptAtRest = _chkHistEncryptAtRest.Checked;
            _settings.History.RecordFailures = _chkHistRecordFailures.Checked;
            _settings.History.Deduplication = _chkHistDeduplication.Checked;

            // Tabs
            _settings.Tabs.ColoringEnabled = _chkTabColoringEnabled.Checked;
            _settings.Tabs.ColoringRules.Clear();
            foreach (DataGridViewRow row in _gridColoringRules.Rows)
            {
                if (row.IsNewRow) continue;
                var pattern = row.Cells["Pattern"].Value as string;
                if (string.IsNullOrWhiteSpace(pattern)) continue;
                int.TryParse(row.Cells["Order"].Value?.ToString(), out var order);
                _settings.Tabs.ColoringRules.Add(new ColoringRule
                {
                    Order = order,
                    Pattern = pattern,
                    Color = row.Cells["Color"].Value as string ?? "#808080",
                    Label = row.Cells["Label"].Value as string ?? string.Empty
                });
            }
            _settings.Tabs.SessionRecovery = _chkTabSessionRecovery.Checked;
            _settings.Tabs.AutoSaveInterval = (int)_nudTabAutoSaveInterval.Value;
            _settings.Tabs.RestoreOnStartup = _cboTabRestoreOnStartup.SelectedIndex switch
            {
                1 => "always",
                2 => "never",
                _ => "prompt"
            };
            _settings.Tabs.MaxClosedTabs = (int)_nudTabMaxClosedTabs.Value;
            _settings.Tabs.CustomWindowTitle = _txtTabCustomWindowTitle.Text.Trim();

            // Safety
            _settings.Safety.ProductionWarning = _chkSafetyProductionWarning.Checked;
            _settings.Safety.DeleteWithoutWhere = _chkSafetyDeleteWithoutWhere.Checked;
            _settings.Safety.UpdateWithoutWhere = _chkSafetyUpdateWithoutWhere.Checked;
            _settings.Safety.DropConfirmation = _chkSafetyDropConfirmation.Checked;
            _settings.Safety.TruncateConfirmation = _chkSafetyTruncateConfirmation.Checked;
            _settings.Safety.TransactionReminder = _chkSafetyTransactionReminder.Checked;
            _settings.Safety.TransactionReminderInterval = (int)_nudSafetyTransactionReminderInterval.Value;

            // Phase 8 — Editor Productivity
            _settings.EditorProductivity.HighlightOccurrences = _chkHighlightOccurrences.Checked;
            _settings.EditorProductivity.BracketMatching      = _chkBracketMatching.Checked;
            _settings.EditorProductivity.NamedRegions         = _chkNamedRegions.Checked;
            _settings.EditorProductivity.StickyScroll         = _chkStickyScroll.Checked;
            _settings.EditorProductivity.Minimap              = _chkMinimap.Checked;
            _settings.EditorProductivity.DocumentOutline      = _chkDocumentOutline.Checked;

            // Phase 8 — Execution Productivity
            _settings.ExecutionProductivity.ShowExecutionTimer    = _chkShowExecutionTimer.Checked;
            _settings.ExecutionProductivity.NotificationThreshold = (int)_nudNotificationThreshold.Value;
            _settings.ExecutionProductivity.MultiDatabase         = _chkMultiDatabase.Checked;

            // Phase 8 — Navigation
            _settings.Navigation.GoToDefinition = _chkGoToDefinition.Checked;
            _settings.Navigation.PeekDefinition = _chkPeekDefinition.Checked;
            _settings.Navigation.FindReferences = _chkFindReferences.Checked;
            _settings.Navigation.ObjectSearch   = _chkObjectSearch.Checked;
            Productivity.ConnectionAliasEditor.SaveAliases(_gridConnectionAliases, _settings.Navigation);

            // Phase 8 — Grid
            _settings.Grid.Aggregates    = _chkGridAggregates.Checked;
            _settings.Grid.NullHighlight = _chkGridNullHighlight.Checked;
            _settings.Grid.RowNumbers    = _chkGridRowNumbers.Checked;
            _settings.Grid.FreezeHeaders = _chkGridFreezeHeaders.Checked;

            // Phase 9 — AI Assistant
            _settings.Ai.Provider = _cboAiProvider.SelectedIndex switch
            {
                1 => "Anthropic",
                2 => "OpenAI",
                3 => "AzureOpenAI",
                4 => "Gemini",
                5 => "Ollama",
                6 => "LMStudio",
                7 => "Custom",
                _ => ""
            };
            _settings.Ai.Model    = _txtAiModel.Text.Trim();
            _settings.Ai.ApiKey   = _txtAiApiKey.Text.Trim();
            _settings.Ai.Endpoint = _txtAiEndpoint.Text.Trim();

            _settings.Ai.PrivacyMode =
                _rbPrivacyFull.Checked      ? "full" :
                _rbPrivacyAnonymous.Checked ? "anonymous" :
                _rbPrivacyOffline.Checked   ? "offline" :
                _rbPrivacyDisabled.Checked  ? "disabled" :
                                              "schemaOnly";

            _settings.Ai.TextToSql        = _chkAiTextToSql.Checked;
            _settings.Ai.Explain          = _chkAiExplain.Checked;
            _settings.Ai.Fix              = _chkAiFix.Checked;
            _settings.Ai.Optimize         = _chkAiOptimize.Checked;
            _settings.Ai.IndexSuggestions = _chkAiIndexSuggestions.Checked;
            _settings.Ai.ChatPanel        = _chkAiChatPanel.Checked;
            _settings.Ai.InlineCompletion = _chkAiInlineCompletion.Checked;
            _settings.Ai.AutoFixOnError   = _chkAiAutoFixOnError.Checked;

            _settings.Ai.MaxTokens   = (int)_nudAiMaxTokens.Value;
            _settings.Ai.Temperature = (double)_nudAiTemperature.Value / 10.0;
            _settings.Ai.Timeout     = (int)_nudAiTimeout.Value;
            _settings.Ai.Retries     = (int)_nudAiRetries.Value;

            _settings.Ai.Enabled = _cboAiProvider.SelectedIndex > 0;

            _settings.Ai.OfflineProvider = _cboAiOfflineProvider.SelectedIndex switch
            {
                1 => "Ollama",
                2 => "LMStudio",
                3 => "Custom",
                _ => ""
            };
            _settings.Ai.OfflineModel    = _txtAiOfflineModel.Text.Trim();
            _settings.Ai.OfflineEndpoint = _txtAiOfflineEndpoint.Text.Trim();

            // Persist rule enable/severity overrides to the user-level .casettings file
            SaveRuleGridToCaSettings();
        }

        private void SaveRuleGridToCaSettings()
        {
            try
            {
                var userDir      = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AKML SQL");
                Directory.CreateDirectory(userDir);
                var settingsPath = Path.Combine(userDir, ".casettings");

                // Load existing file to preserve any rules not shown in the grid
                var existing = new Dictionary<string, RuleConfig>();
                if (File.Exists(settingsPath))
                {
                    try
                    {
                        var ca = JsonSerializer.Deserialize<CaSettings>(
                            File.ReadAllText(settingsPath),
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (ca?.Rules != null)
                            foreach (var kvp in ca.Rules)
                                existing[kvp.Key] = kvp.Value;
                    }
                    catch { /* ignore corrupt file */ }
                }

                // Merge grid state (grid rows take precedence)
                foreach (DataGridViewRow row in _gridRules.Rows)
                {
                    var ruleId = row.Cells["RuleId"].Value as string;
                    if (string.IsNullOrEmpty(ruleId)) continue;
                    existing[ruleId] = new RuleConfig
                    {
                        Enabled  = row.Cells["Enabled"].Value is true,
                        Severity = row.Cells["Severity"].Value as string ?? "Warning"
                    };
                }

                var output = new CaSettings { Rules = existing };
                File.WriteAllText(settingsPath, JsonSerializer.Serialize(output,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "SettingsDialog: failed to save rule grid to .casettings");
            }
        }

        // --- Layout helpers ---

        private static void AddSectionLabel(TabPage tab, string text, ref int y)
        {
            // Horizontal separator (skipped for the very first section on the tab)
            if (y > 20)
            {
                y += 4;
                var sep = new Panel
                {
                    Location  = new Point(12, y),
                    Height    = 1,
                    BackColor = Color.FromArgb(210, 215, 225)
                };
                sep.Width = tab.ClientSize.Width > 0 ? tab.ClientSize.Width - 24 : 520;
                tab.Resize += (_, __) => sep.Width = tab.ClientSize.Width - 24;
                tab.Controls.Add(sep);
                y += 8;
            }

            var label = new Label
            {
                Text      = text,
                Font      = new Font(DefaultFont, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 84, 166),
                Location  = new Point(15, y),
                AutoSize  = true
            };
            tab.Controls.Add(label);
            y += 26;
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

        private static void AddHintLabel(TabPage tab, string text, ref int y)
        {
            var lbl = new Label
            {
                Text = text,
                ForeColor = Color.FromArgb(120, 120, 130),
                Location = new Point(45, y),
                AutoSize = true,
                Font = new Font(DefaultFont.FontFamily, DefaultFont.Size - 1f, FontStyle.Italic)
            };
            tab.Controls.Add(lbl);
            y += 20;
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
