#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using AkmlSql.Core.Config;
using Serilog;
using Constants = AkmlSql.Core.Constants;

// ReSharper disable MemberCanBePrivate.Local

namespace AkmlSql.Shell.Shared.Dialogs
{
    /// <summary>
    /// Professional themed WPF Settings window inspired by Redgate SQL Prompt.
    /// Supports Dark and Light themes. Code-only (no XAML) — compatible with
    /// SharedProject (.projitems) across all 6 host targets.
    /// </summary>
    internal sealed class SettingsWindow
    {
        // ─── Theme brush set ────────────────────────────────────────────────
        /// <summary>
        /// Holds all frozen brushes for a single theme variant (Dark or Light).
        /// </summary>
        private sealed class ThemeBrushSet
        {
            public SolidColorBrush Main { get; }
            public SolidColorBrush Sidebar { get; }
            public SolidColorBrush Panel { get; }
            public SolidColorBrush Input { get; }
            public SolidColorBrush InputReadOnly { get; }
            public SolidColorBrush Button { get; }
            public SolidColorBrush ButtonHover { get; }
            public SolidColorBrush Selected { get; }
            public SolidColorBrush Border { get; }
            public SolidColorBrush ComboBorder { get; }
            public SolidColorBrush FgPrimary { get; }
            public SolidColorBrush FgSecondary { get; }
            public SolidColorBrush FgAccent { get; }
            public SolidColorBrush FgWhite { get; }
            public SolidColorBrush SelectedText { get; }
            public SolidColorBrush Sep { get; }
            public SolidColorBrush Transparent { get; }
            public SolidColorBrush TreeHover { get; }
            public SolidColorBrush Caret { get; }

            private ThemeBrushSet(
                Color main, Color sidebar, Color panel, Color input, Color inputReadOnly,
                Color button, Color buttonHover, Color selected,
                Color border, Color comboBorder,
                Color fgPrimary, Color fgSecondary, Color fgAccent, Color fgWhite,
                Color selectedText, Color sep, Color treeHover, Color caret)
            {
                Main = Freeze(new SolidColorBrush(main));
                Sidebar = Freeze(new SolidColorBrush(sidebar));
                Panel = Freeze(new SolidColorBrush(panel));
                Input = Freeze(new SolidColorBrush(input));
                InputReadOnly = Freeze(new SolidColorBrush(inputReadOnly));
                Button = Freeze(new SolidColorBrush(button));
                ButtonHover = Freeze(new SolidColorBrush(buttonHover));
                Selected = Freeze(new SolidColorBrush(selected));
                Border = Freeze(new SolidColorBrush(border));
                ComboBorder = Freeze(new SolidColorBrush(comboBorder));
                FgPrimary = Freeze(new SolidColorBrush(fgPrimary));
                FgSecondary = Freeze(new SolidColorBrush(fgSecondary));
                FgAccent = Freeze(new SolidColorBrush(fgAccent));
                FgWhite = Freeze(new SolidColorBrush(fgWhite));
                SelectedText = Freeze(new SolidColorBrush(selectedText));
                Sep = Freeze(new SolidColorBrush(sep));
                Transparent = Freeze(new SolidColorBrush(Colors.Transparent));
                TreeHover = Freeze(new SolidColorBrush(treeHover));
                Caret = Freeze(new SolidColorBrush(caret));
            }

            public static readonly ThemeBrushSet Dark = new ThemeBrushSet(
                main:        Color.FromRgb(0x1E, 0x1E, 0x1E), // #1E1E1E
                sidebar:     Color.FromRgb(0x25, 0x25, 0x26), // #252526
                panel:       Color.FromRgb(0x2D, 0x2D, 0x30), // #2D2D30
                input:       Color.FromRgb(0x3C, 0x3C, 0x3C), // #3C3C3C
                inputReadOnly: Color.FromRgb(0x1E, 0x1E, 0x1E), // #1E1E1E
                button:      Color.FromRgb(0x3C, 0x3C, 0x3C), // #3C3C3C
                buttonHover: Color.FromRgb(0x50, 0x50, 0x54), // #505054
                selected:    Color.FromRgb(0x09, 0x47, 0x71), // #094771
                border:      Color.FromRgb(0x3C, 0x3C, 0x3C), // #3C3C3C
                comboBorder: Color.FromRgb(0x55, 0x55, 0x55), // #555555
                fgPrimary:   Color.FromRgb(0xD4, 0xD4, 0xD4), // #D4D4D4
                fgSecondary: Color.FromRgb(0x88, 0x88, 0x88), // #888888
                fgAccent:    Color.FromRgb(0x00, 0x7A, 0xCC), // #007ACC
                fgWhite:     Color.FromRgb(0xFF, 0xFF, 0xFF), // #FFFFFF
                selectedText: Color.FromRgb(0xFF, 0xFF, 0xFF), // #FFFFFF
                sep:         Color.FromRgb(0x3C, 0x3C, 0x3C), // #3C3C3C
                treeHover:   Color.FromRgb(0x2A, 0x2D, 0x2E), // #2A2D2E
                caret:       Color.FromRgb(0xFF, 0xFF, 0xFF)  // #FFFFFF
            );

            public static readonly ThemeBrushSet Light = new ThemeBrushSet(
                main:        Color.FromRgb(0xF5, 0xF5, 0xF5), // #F5F5F5 — window background
                sidebar:     Color.FromRgb(0xF0, 0xF0, 0xF0), // #F0F0F0 — sidebar background
                panel:       Color.FromRgb(0xFF, 0xFF, 0xFF), // #FFFFFF — content background
                input:       Color.FromRgb(0xFF, 0xFF, 0xFF), // #FFFFFF — input background
                inputReadOnly: Color.FromRgb(0xF0, 0xF0, 0xF0), // #F0F0F0
                button:      Color.FromRgb(0xE0, 0xE0, 0xE0), // #E0E0E0 — button background
                buttonHover: Color.FromRgb(0xD0, 0xD0, 0xD0), // #D0D0D0 — button hover
                selected:    Color.FromRgb(0xCC, 0xE8, 0xFF), // #CCE8FF — selected/highlight
                border:      Color.FromRgb(0xE0, 0xE0, 0xE0), // #E0E0E0 — border
                comboBorder: Color.FromRgb(0xCC, 0xCC, 0xCC), // #CCCCCC — input border
                fgPrimary:   Color.FromRgb(0x1E, 0x1E, 0x1E), // #1E1E1E — text primary
                fgSecondary: Color.FromRgb(0x66, 0x66, 0x66), // #666666 — text secondary
                fgAccent:    Color.FromRgb(0x00, 0x7A, 0xCC), // #007ACC — accent blue
                fgWhite:     Color.FromRgb(0x1E, 0x1E, 0x1E), // #1E1E1E — headings (dark on light)
                selectedText: Color.FromRgb(0x1E, 0x1E, 0x1E), // #1E1E1E — selected text (dark on light blue)
                sep:         Color.FromRgb(0xE0, 0xE0, 0xE0), // #E0E0E0 — separator
                treeHover:   Color.FromRgb(0xE8, 0xE8, 0xE8), // #E8E8E8 — hover
                caret:       Color.FromRgb(0x1E, 0x1E, 0x1E)  // #1E1E1E
            );
        }

        private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

        // ─── Active theme ───────────────────────────────────────────────────
        private readonly ThemeBrushSet _theme;

        // ─── State ───────────────────────────────────────────────────────────
        private Window? _window;
        private AppSettings _settings;
        private ContentControl? _contentHost;
        private TreeView? _navTree;
        private readonly Dictionary<string, UIElement> _pages = new();

        // Track whether user confirmed via OK
        private bool _dialogResult;

        /// <summary>
        /// When set to true by the theme-changed handler, the caller should
        /// reopen the settings window to apply the new theme.
        /// </summary>
        public bool ThemeChangeRequested { get; private set; }

        // ─── Control references (for Load / Save) ───────────────────────────

        // General
        private CheckBox? _chkAutoUpdate;
        private CheckBox? _chkTelemetry;
        private ComboBox? _cboTheme;

        // IntelliSense
        private CheckBox? _chkIsEnabled;
        private CheckBox? _chkAutoTrigger;
        private CheckBox? _chkAfterDot;
        private CheckBox? _chkFuzzyMatch;
        private CheckBox? _chkShowDataTypes;
        private CheckBox? _chkShowNullability;
        private CheckBox? _chkShowPkFk;
        private CheckBox? _chkAutoAlias;
        private CheckBox? _chkJoinAssist;
        private CheckBox? _chkDisableNativeIs;
        private Slider? _sldTriggerDelay;
        private TextBlock? _lblTriggerDelayValue;
        private Slider? _sldMaxSuggestions;
        private TextBlock? _lblMaxSuggestionsValue;
        private ComboBox? _cboKeywordCase;

        // Schema Cache
        private CheckBox? _chkCacheAutoRefresh;
        private CheckBox? _chkDetectDdl;
        private CheckBox? _chkLazyLoadColumns;
        private CheckBox? _chkPersistToDisk;
        private Slider? _sldRefreshInterval;
        private TextBlock? _lblRefreshIntervalValue;
        private Slider? _sldMaxDatabases;
        private TextBlock? _lblMaxDatabasesValue;

        // Formatting
        private CheckBox? _chkFmtEnabled;
        private CheckBox? _chkFormatOnPaste;
        private CheckBox? _chkFormatOnSave;
        private CheckBox? _chkFormatOnDelimiter;
        private CheckBox? _chkConfirmBulk;
        private CheckBox? _chkCreateBackups;
        private CheckBox? _chkRespectNoformat;
        private CheckBox? _chkSemanticValidation;

        // Snippets
        private CheckBox? _chkSnipEnabled;
        private CheckBox? _chkSnipShowInCompletion;
        private CheckBox? _chkSnipFormatOnExpand;
        private CheckBox? _chkSnipContextFilter;
        private CheckBox? _chkSnipTrackUsage;
        private TextBox? _txtPersonalFolder;
        private TextBox? _txtTeamFolder;

        // Code Analysis
        private CheckBox? _chkAnalysisEnabled;
        private CheckBox? _chkAnalysisRunOnType;
        private CheckBox? _chkAnalysisRunOnSave;
        private CheckBox? _chkAnalysisShowInErrorList;

        // Refactoring
        private CheckBox? _chkRefPreviewBeforeApply;
        private CheckBox? _chkRefCreateBackups;
        private CheckBox? _chkRefFormatAfterRefactor;
        private CheckBox? _chkRefIncludeCommentsInRename;
        private CheckBox? _chkRefIncludeStringLiteralsInRename;
        private ComboBox? _cboRefRenameScope;

        // History
        private CheckBox? _chkHistEnabled;
        private CheckBox? _chkHistEncryptAtRest;
        private CheckBox? _chkHistRecordFailures;
        private CheckBox? _chkHistDeduplication;
        private Slider? _sldHistRetentionDays;
        private TextBlock? _lblHistRetentionValue;
        private Slider? _sldHistMaxEntries;
        private TextBlock? _lblHistMaxEntriesValue;

        // Tabs
        private CheckBox? _chkTabColoringEnabled;
        private CheckBox? _chkTabGradientColors;
        private CheckBox? _chkTabSessionRecovery;
        private Slider? _sldTabAutoSaveInterval;
        private TextBlock? _lblTabAutoSaveValue;
        private Slider? _sldTabMaxClosedTabs;
        private TextBlock? _lblTabMaxClosedTabsValue;
        private TextBox? _txtTabCustomWindowTitle;
        private ComboBox? _cboTabRestoreOnStartup;

        // Safety
        private CheckBox? _chkSafetyProductionWarning;
        private CheckBox? _chkSafetyDeleteWithoutWhere;
        private CheckBox? _chkSafetyUpdateWithoutWhere;
        private CheckBox? _chkSafetyDropConfirmation;
        private CheckBox? _chkSafetyTruncateConfirmation;
        private CheckBox? _chkSafetyTransactionReminder;
        private Slider? _sldSafetyTransReminderInterval;
        private TextBlock? _lblSafetyTransReminderValue;

        // AI
        private CheckBox? _chkAiTextToSql;
        private CheckBox? _chkAiExplain;
        private CheckBox? _chkAiFix;
        private CheckBox? _chkAiOptimize;
        private CheckBox? _chkAiIndexSuggestions;
        private CheckBox? _chkAiChatPanel;
        private CheckBox? _chkAiInlineCompletion;
        private CheckBox? _chkAiAutoFixOnError;
        private ComboBox? _cboAiProvider;
        private TextBox? _txtAiModel;
        private TextBox? _txtAiApiKey;
        private TextBox? _txtAiEndpoint;
        private ComboBox? _cboAiPrivacyMode;
        private Slider? _sldAiMaxTokens;
        private TextBlock? _lblAiMaxTokensValue;
        private Slider? _sldAiTemperature;
        private TextBlock? _lblAiTemperatureValue;
        private Slider? _sldAiTimeout;
        private TextBlock? _lblAiTimeoutValue;
        private Slider? _sldAiRetries;
        private TextBlock? _lblAiRetriesValue;

        // Grid
        private CheckBox? _chkGridAggregates;
        private CheckBox? _chkGridNullHighlight;
        private CheckBox? _chkGridRowNumbers;
        private CheckBox? _chkGridFreezeHeaders;
        private CheckBox? _chkGridExcelLargeNumberAsText;

        // Editor Productivity
        private CheckBox? _chkEdHighlightOccurrences;
        private CheckBox? _chkEdBracketMatching;
        private CheckBox? _chkEdNamedRegions;
        private CheckBox? _chkEdStickyScroll;
        private CheckBox? _chkEdMinimap;
        private CheckBox? _chkEdDocumentOutline;

        // Execution
        private CheckBox? _chkExecShowTimer;
        private CheckBox? _chkExecMultiDatabase;
        private Slider? _sldExecNotificationThreshold;
        private TextBlock? _lblExecNotificationValue;

        // Navigation
        private CheckBox? _chkNavGoToDefinition;
        private CheckBox? _chkNavPeekDefinition;
        private CheckBox? _chkNavFindReferences;
        private CheckBox? _chkNavObjectSearch;

        // ─── Public API ──────────────────────────────────────────────────────

        public SettingsWindow(AppSettings settings)
        {
            _settings = settings;

            // Pick theme based on settings (default: light, like SQL Prompt)
            var themeName = settings.Theme?.ToLowerInvariant() ?? "light";
            _theme = themeName == "dark" ? ThemeBrushSet.Dark : ThemeBrushSet.Light;
        }

        /// <summary>
        /// Shows the settings window as a modal dialog.
        /// Returns true if the user clicked OK/Apply, false if Cancel.
        /// </summary>
        public bool ShowDialog()
        {
            _window = CreateWindow();
            LoadSettingsToControls();

            // Select the first category
            if (_navTree?.Items.Count > 0)
            {
                var firstItem = _navTree.Items[0] as TreeViewItem;
                if (firstItem != null)
                    firstItem.IsSelected = true;
            }

            _window.ShowDialog();
            return _dialogResult;
        }

        /// <summary>Returns the (potentially modified) settings.</summary>
        public AppSettings GetSettings()
        {
            SaveControlsToSettings();
            return _settings;
        }

        // ─── Window construction ─────────────────────────────────────────────

        private Window CreateWindow()
        {
            var window = new Window
            {
                Title = Constants.ProductName + " Settings",
                Width = 820,
                Height = 620,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = _theme.Main,
                Foreground = _theme.FgPrimary,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.SingleBorderWindow,
            };

            // Try to set owner to IDE main window
            try
            {
                var mainWindow = Application.Current?.MainWindow;
                if (mainWindow != null)
                    window.Owner = mainWindow;
            }
            catch
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            // Root layout: DockPanel
            var root = new DockPanel { Background = _theme.Main };

            // ─── Bottom bar ──────────────────────────────────────────────
            var bottomBar = CreateBottomBar();
            DockPanel.SetDock(bottomBar, Dock.Bottom);
            root.Children.Add(bottomBar);

            // ─── Separator above bottom bar ──────────────────────────────
            var sep = new Border { Height = 1, Background = _theme.Sep };
            DockPanel.SetDock(sep, Dock.Bottom);
            root.Children.Add(sep);

            // ─── Left sidebar ────────────────────────────────────────────
            var sidebar = CreateSidebar();
            DockPanel.SetDock(sidebar, Dock.Left);
            root.Children.Add(sidebar);

            // ─── Vertical separator ──────────────────────────────────────
            var vertSep = new Border { Width = 1, Background = _theme.Sep };
            DockPanel.SetDock(vertSep, Dock.Left);
            root.Children.Add(vertSep);

            // ─── Right content area ──────────────────────────────────────
            _contentHost = new ContentControl
            {
                Background = _theme.Panel,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            root.Children.Add(_contentHost);

            window.Content = root;
            window.KeyDown += OnWindowKeyDown;

            return window;
        }

        // ─── Bottom bar ──────────────────────────────────────────────────────

        private Border CreateBottomBar()
        {
            var bar = new Border
            {
                Height = 50,
                Background = _theme.Sidebar,
                Padding = new Thickness(12, 8, 12, 8)
            };

            var dock = new DockPanel { LastChildFill = false };

            // Right side: OK, Cancel, Apply
            var btnApply = MakeButton("Apply", 80);
            btnApply.Click += OnApplyClick;
            DockPanel.SetDock(btnApply, Dock.Right);
            dock.Children.Add(btnApply);

            var btnCancel = MakeButton("Cancel", 80);
            btnCancel.Margin = new Thickness(0, 0, 8, 0);
            btnCancel.Click += OnCancelClick;
            DockPanel.SetDock(btnCancel, Dock.Right);
            dock.Children.Add(btnCancel);

            var btnOk = MakeButton("OK", 80);
            btnOk.Margin = new Thickness(0, 0, 8, 0);
            btnOk.FontWeight = FontWeights.SemiBold;
            btnOk.Click += OnOkClick;
            DockPanel.SetDock(btnOk, Dock.Right);
            dock.Children.Add(btnOk);

            // Left side: Import/Export
            var btnImport = MakeButton("Import Profile...", 120);
            btnImport.Margin = new Thickness(0, 0, 8, 0);
            btnImport.Click += OnImportProfileClick;
            DockPanel.SetDock(btnImport, Dock.Left);
            dock.Children.Add(btnImport);

            var btnExport = MakeButton("Export Profile...", 120);
            btnExport.Click += OnExportProfileClick;
            DockPanel.SetDock(btnExport, Dock.Left);
            dock.Children.Add(btnExport);

            var btnResetPage = MakeButton("Reset This Page", 120);
            btnResetPage.Margin = new Thickness(8, 0, 0, 0);
            btnResetPage.Click += OnResetThisPageClick;
            DockPanel.SetDock(btnResetPage, Dock.Left);
            dock.Children.Add(btnResetPage);

            var btnResetAll = MakeButton("Reset All", 80);
            btnResetAll.Margin = new Thickness(8, 0, 0, 0);
            btnResetAll.Click += OnResetAllClick;
            DockPanel.SetDock(btnResetAll, Dock.Left);
            dock.Children.Add(btnResetAll);

            bar.Child = dock;
            return bar;
        }

        // ─── Left sidebar ────────────────────────────────────────────────────

        private Border CreateSidebar()
        {
            var sidebar = new Border
            {
                Width = 200,
                Background = _theme.Sidebar,
                Padding = new Thickness(0, 8, 0, 0)
            };

            var panel = new StackPanel();

            // Title label
            var title = new TextBlock
            {
                Text = "Settings",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = _theme.FgWhite,
                Margin = new Thickness(16, 4, 0, 12)
            };
            panel.Children.Add(title);

            // TreeView for navigation
            _navTree = new TreeView
            {
                Background = _theme.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = _theme.FgPrimary,
                Padding = new Thickness(0)
            };

            // Override system highlight colors so TreeView items stay themed
            // even when focus moves between tree and content panel
            _navTree.Resources[SystemColors.HighlightBrushKey] = _theme.Selected;
            _navTree.Resources[SystemColors.HighlightTextBrushKey] = _theme.SelectedText;
            _navTree.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = _theme.Selected;
            _navTree.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = _theme.SelectedText;
            _navTree.Resources[SystemColors.ControlBrushKey] = _theme.Selected;
            _navTree.Resources[SystemColors.ControlTextBrushKey] = _theme.SelectedText;

            // Apply themed style to TreeViewItems
            var itemStyle = new Style(typeof(TreeViewItem));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, _theme.FgPrimary));
            itemStyle.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
            itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, _theme.Transparent));
            itemStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));

            var selectedTrigger = new Trigger { Property = TreeViewItem.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, _theme.Selected));
            selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, _theme.SelectedText));
            itemStyle.Triggers.Add(selectedTrigger);

            var mouseOverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            mouseOverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, _theme.TreeHover));
            itemStyle.Triggers.Add(mouseOverTrigger);

            _navTree.ItemContainerStyle = itemStyle;

            // Build categories and pages
            BuildPages();

            string[] categories =
            {
                "General", "IntelliSense", "Schema Cache", "Formatting", "Snippets",
                "Code Analysis", "Refactoring", "History", "Tabs & UI", "Safety",
                "Grid", "Editor", "Execution", "Navigation", "AI Assistance"
            };

            foreach (var cat in categories)
            {
                var item = new TreeViewItem
                {
                    Header = cat,
                    Tag = cat,
                    IsExpanded = false
                };
                _navTree.Items.Add(item);
            }

            _navTree.SelectedItemChanged += OnNavSelectionChanged;

            panel.Children.Add(_navTree);
            sidebar.Child = panel;
            return sidebar;
        }

        // ─── Page building ───────────────────────────────────────────────────

        private void BuildPages()
        {
            _pages["General"] = BuildGeneralPage();
            _pages["IntelliSense"] = BuildIntelliSensePage();
            _pages["Schema Cache"] = BuildSchemaCachePage();
            _pages["Formatting"] = BuildFormattingPage();
            _pages["Snippets"] = BuildSnippetsPage();
            _pages["Code Analysis"] = BuildCodeAnalysisPage();
            _pages["Refactoring"] = BuildRefactoringPage();
            _pages["History"] = BuildHistoryPage();
            _pages["Tabs & UI"] = BuildTabsPage();
            _pages["Safety"] = BuildSafetyPage();
            _pages["AI Assistance"] = BuildAiPage();
            _pages["Grid"] = BuildGridPage();
            _pages["Editor"] = BuildEditorPage();
            _pages["Execution"] = BuildExecutionPage();
            _pages["Navigation"] = BuildNavigationPage();
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  General
        // ═══════════════════════════════════════════════════════════════════════
        private UIElement BuildGeneralPage()
        {
            var panel = CreatePagePanel();

            AddPageHeader(panel, "General Settings");

            AddGroupHeader(panel, "Appearance");
            _cboTheme = AddDropdown(panel, "Theme",
                new[] { "Dark", "Light", "System" },
                "UI color theme for AKML SQL dialogs");
            _cboTheme.SelectionChanged += OnThemeSelectionChanged;

            AddGroupSeparator(panel);
            AddGroupHeader(panel, "Updates & Telemetry");
            _chkAutoUpdate = AddToggle(panel, "Check for updates automatically",
                "Checks for new versions every 24 hours on startup");
            _chkTelemetry = AddToggle(panel, "Send anonymous usage telemetry",
                "No personally identifiable information is collected");

            AddGroupSeparator(panel);
            AddGroupHeader(panel, "Paths");
            AddReadOnlyField(panel, "Configuration file", Constants.ConfigFilePath);
            AddReadOnlyField(panel, "Log directory", Constants.LogsPath);

            AddGroupSeparator(panel);
            AddGroupHeader(panel, "About");
            AddInfoRow(panel, "Version", Constants.Version + " (" + Constants.BuildDate + ")");

            return WrapInScrollViewer(panel);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  IntelliSense
        // ═══════════════════════════════════════════════════════════════════════
        private UIElement BuildIntelliSensePage()
        {
            var panel = CreatePagePanel();

            AddPageHeader(panel, "IntelliSense");

            AddGroupHeader(panel, "Core");
            _chkIsEnabled = AddToggle(panel, "Enable IntelliSense",
                "Master switch for all IntelliSense features");
            _chkAutoTrigger = AddToggle(panel, "Auto-trigger completions while typing",
                "Show completion list automatically without Ctrl+Space");
            _chkAfterDot = AddToggle(panel, "Trigger after dot",
                "Auto-complete after typing '.' for table.column references");
            _chkFuzzyMatch = AddToggle(panel, "Enable fuzzy matching",
                "Substring and approximate matching in addition to prefix");

            AddGroupSeparator(panel);
            AddGroupHeader(panel, "Display");

            (_sldMaxSuggestions, _lblMaxSuggestionsValue) = AddSlider(panel,
                "Maximum suggestions", 5, 200, 50,
                "Maximum number of items shown in the completion list");
            (_sldTriggerDelay, _lblTriggerDelayValue) = AddSlider(panel,
                "Trigger delay (ms)", 0, 2000, 100,
                "Debounce delay before showing completions");

            _cboKeywordCase = AddDropdown(panel, "Keyword casing",
                new[] { "UPPER", "lower", "PascalCase", "As-Is" },
                "Casing applied to SQL keywords inserted by IntelliSense");

            _chkShowDataTypes = AddToggle(panel, "Show column data types",
                "Display data type information in completion details");
            _chkShowNullability = AddToggle(panel, "Show nullability info",
                "Show NOT NULL / NULL status in completion details");
            _chkShowPkFk = AddToggle(panel, "Show PK/FK indicators",
                "Show primary key and foreign key badges");

            AddGroupSeparator(panel);
            AddGroupHeader(panel, "Assistance");
            _chkAutoAlias = AddToggle(panel, "Auto-generate table aliases",
                "Suggest aliases when completing table names in FROM clauses");
            _chkJoinAssist = AddToggle(panel, "JOIN clause assistance",
                "Suggest JOIN conditions based on foreign key relationships");
            _chkDisableNativeIs = AddToggle(panel, "Disable native SSMS IntelliSense",
                "Recommended to avoid conflicts with AKML SQL IntelliSense");

            return WrapInScrollViewer(panel);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Schema Cache
        // ═══════════════════════════════════════════════════════════════════════
        private UIElement BuildSchemaCachePage()
        {
            var panel = CreatePagePanel();

            AddPageHeader(panel, "Schema Cache");

            AddGroupHeader(panel, "Refresh Behavior");
            _chkCacheAutoRefresh = AddToggle(panel, "Auto-refresh schema cache",
                "Periodically check for schema changes in the background");

            (_sldRefreshInterval, _lblRefreshIntervalValue) = AddSlider(panel,
                "Refresh interval (seconds)", 30, 3600, 300,
                "Time between background change-detection queries");

            _chkDetectDdl = AddToggle(panel, "Detect DDL changes",
                "Trigger immediate cache refresh when DDL statements are executed");

            AddGroupSeparator(panel);
            AddGroupHeader(panel, "Storage");

            (_sldMaxDatabases, _lblMaxDatabasesValue) = AddSlider(panel,
                "Max cached databases", 1, 50, 10,
                "Number of database caches kept in memory before LRU eviction");

            _chkLazyLoadColumns = AddToggle(panel, "Lazy-load column metadata",
                "Load columns and foreign keys in background (Phase B)");
            _chkPersistToDisk = AddToggle(panel, "Persist cache to disk",
                "Save schema cache to disk for faster startup on reconnect");

            return WrapInScrollViewer(panel);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Formatting
        // ═══════════════════════════════════════════════════════════════════════
        private UIElement BuildFormattingPage()
        {
            var panel = CreatePagePanel();

            AddPageHeader(panel, "SQL Formatting");

            AddGroupHeader(panel, "Triggers");
            _chkFmtEnabled = AddToggle(panel, "Enable SQL formatter",
                "Master switch for all formatting features");
            _chkFormatOnPaste = AddToggle(panel, "Format on paste",
                "Automatically format SQL when pasting from clipboard");
            _chkFormatOnSave = AddToggle(panel, "Format on save",
                "Automatically format the document when saving");
            _chkFormatOnDelimiter = AddToggle(panel, "Format on delimiter",
                "Format when typing GO or semicolon");

            AddGroupSeparator(panel);
            AddGroupHeader(panel, "Safety & Validation");
            _chkConfirmBulk = AddToggle(panel, "Confirm before bulk format",
                "Show a confirmation dialog before formatting multiple files");
            _chkCreateBackups = AddToggle(panel, "Create backups before formatting",
                "Save a backup copy of files before applying format changes");
            _chkRespectNoformat = AddToggle(panel, "Respect --noformat regions",
                "Skip formatting inside --noformat / --endnoformat blocks");
            _chkSemanticValidation = AddToggle(panel, "Validate formatting preserves semantics",
                "Re-parse formatted SQL to verify it is semantically equivalent");

            return WrapInScrollViewer(panel);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Snippets
        // ═══════════════════════════════════════════════════════════════════════
        private UIElement BuildSnippetsPage()
        {
            var panel = CreatePagePanel();

            AddPageHeader(panel, "Snippets");

            AddGroupHeader(panel, "Snippet Manager");
            _chkSnipEnabled = AddToggle(panel, "Enable snippets",
                "Master switch for the snippet engine");
            _chkSnipShowInCompletion = AddToggle(panel, "Show in IntelliSense completions",
                "Include snippets in the main completion list");
            _chkSnipFormatOnExpand = AddToggle(panel, "Format after expansion",
                "Apply SQL formatting after expanding a snippet");
            _chkSnipContextFilter = AddToggle(panel, "Filter by SQL context",
                "Only show snippets valid for the current SQL position");
            _chkSnipTrackUsage = AddToggle(panel, "Track usage for ranking",
                "Boost frequently-used snippets to the top of the list");

            AddGroupSeparator(panel);
            AddGroupHeader(panel, "Snippet Folders");
            _txtPersonalFolder = AddTextInput(panel, "Personal folder",
                "Path to personal .akmlsnippet files (leave empty for default)");
            _txtTeamFolder = AddTextInput(panel, "Team folder",
                "Shared folder for team snippet distribution");

            return WrapInScrollViewer(panel);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Code Analysis
        // ═══════════════════════════════════════════════════════════════════════
        private UIElement BuildCodeAnalysisPage()
        {
            var panel = CreatePagePanel();

            AddPageHeader(panel, "Code Analysis");

            AddGroupHeader(panel, "Analysis Engine");
            _chkAnalysisEnabled = AddToggle(panel, "Enable code analysis",
                "Master switch for all 120+ analysis rules");
            _chkAnalysisRunOnType = AddToggle(panel, "Analyze while typing",
                "Run analysis rules in real-time as you type");
            _chkAnalysisRunOnSave = AddToggle(panel, "Analyze on save",
                "Run full analysis when the document is saved");
            _chkAnalysisShowInErrorList = AddToggle(panel, "Show in Error List",
                "Report analysis issues in the VS/SSMS Error List window");

            AddGroupSeparator(panel);
            AddInfoRow(panel, "Rules", "120+ rules across 8 categories (PE, BP, SE, ST, DE, DEP, EX, NM)");
            AddInfoRow(panel, "Per-project config", ".casettings JSON file searched upward from file");
            AddInfoRow(panel, "Inline suppression", "-- akml-disable RuleId / -- akml-enable RuleId");

            return WrapInScrollViewer(panel);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Refactoring
        // ═══════════════════════════════════════════════════════════════════════
        private UIElement BuildRefactoringPage()
        {
            var panel = CreatePagePanel();

            AddPageHeader(panel, "Refactoring");

            AddGroupHeader(panel, "Preview & Safety");
            _chkRefPreviewBeforeApply = AddToggle(panel, "Show preview before applying",
                "Display a diff preview dialog before applying refactoring changes");
            _chkRefCreateBackups = AddToggle(panel, "Create backups",
                "Save a backup copy before applying refactoring changes");
            _chkRefFormatAfterRefactor = AddToggle(panel, "Format after refactoring",
                "Apply SQL formatting after a refactoring operation completes");

            AddGroupSeparator(panel);
            AddGroupHeader(panel, "Rename Options");
            _chkRefIncludeCommentsInRename = AddToggle(panel, "Include comments in rename scope",
                "Also rename occurrences found inside SQL comments");
            _chkRefIncludeStringLiteralsInRename = AddToggle(panel, "Include string literals in rename scope",
                "Also rename occurrences found inside string literals");
            _cboRefRenameScope = AddDropdown(panel, "Rename scope",
                new[] { "Current Script", "Project Directory" },
                "Scope of the Safe Rename operation");

            return WrapInScrollViewer(panel);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  History
        // ═══════════════════════════════════════════════════════════════════════
        private UIElement BuildHistoryPage()
        {
            var panel = CreatePagePanel();

            AddPageHeader(panel, "SQL History");

            AddGroupHeader(panel, "Recording");
            _chkHistEnabled = AddToggle(panel, "Enable SQL history recording",
                "Record all executed SQL statements to a local database");
            _chkHistRecordFailures = AddToggle(panel, "Record failed executions",
                "Also record statements that resulted in errors");
            _chkHistDeduplication = AddToggle(panel, "Enable deduplication",
                "Avoid storing duplicate statements in quick succession");

            AddGroupSeparator(panel);
            AddGroupHeader(panel, "Storage");

            (_sldHistRetentionDays, _lblHistRetentionValue) = AddSlider(panel,
                "Retention (days)", 1, 3650, 90,
                "Number of days to keep history entries before pruning");
            (_sldHistMaxEntries, _lblHistMaxEntriesValue) = AddSlider(panel,
                "Max entries", 1000, 1_000_000, 100_000,
                "Maximum number of history entries stored", true);

            _chkHistEncryptAtRest = AddToggle(panel, "Encrypt at rest",
                "Encrypt stored SQL history using DPAPI + AES-256");

            return WrapInScrollViewer(panel);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Tabs & UI
        // ═══════════════════════════════════════════════════════════════════════
        private UIElement BuildTabsPage()
        {
            var panel = CreatePagePanel();

            AddPageHeader(panel, "Tabs & UI");

            AddGroupHeader(panel, "Tab Coloring");
            _chkTabColoringEnabled = AddToggle(panel, "Enable environment-based tab coloring",
                "Color tabs based on server name patterns (e.g. PROD=red, DEV=green)");
            _chkTabGradientColors = AddToggle(panel, "Use gradient colors",
                "Apply a vertical gradient to tab color bars (lighter at top, base color at bottom)");

            AddGroupSeparator(panel);
            AddGroupHeader(panel, "Session Recovery");
            _chkTabSessionRecovery = AddToggle(panel, "Enable session recovery",
                "Save open documents and restore them on next startup");

            (_sldTabAutoSaveInterval, _lblTabAutoSaveValue) = AddSlider(panel,
                "Auto-save interval (seconds)", 30, 300, 60,
                "How often to save document state for recovery");

            _cboTabRestoreOnStartup = AddDropdown(panel, "Restore on startup",
                new[] { "Prompt", "Always", "Never" },
                "Behavior when opening the IDE after a previous session");

            (_sldTabMaxClosedTabs, _lblTabMaxClosedTabsValue) = AddSlider(panel,
                "Max closed tabs to remember", 1, 100, 20,
                "Number of recently closed tabs available for Ctrl+Shift+T restore");

            AddGroupSeparator(panel);
            AddGroupHeader(panel, "Window Title");
            _txtTabCustomWindowTitle = AddTextInput(panel, "Custom window title template",
                "Use {server}, {database}, and other placeholders");

            return WrapInScrollViewer(panel);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Safety
        // ═══════════════════════════════════════════════════════════════════════
        private UIElement BuildSafetyPage()
        {
            var panel = CreatePagePanel();

            AddPageHeader(panel, "Execution Safety");

            AddGroupHeader(panel, "Warnings");
            _chkSafetyProductionWarning = AddToggle(panel, "Production server warning",
                "Show a warning banner when connected to production environments");
            _chkSafetyDeleteWithoutWhere = AddToggle(panel, "DELETE without WHERE",
                "Warn before executing DELETE statements with no WHERE clause");
            _chkSafetyUpdateWithoutWhere = AddToggle(panel, "UPDATE without WHERE",
                "Warn before executing UPDATE statements with no WHERE clause");
            _chkSafetyDropConfirmation = AddToggle(panel, "DROP confirmation",
                "Require confirmation before executing DROP statements");
            _chkSafetyTruncateConfirmation = AddToggle(panel, "TRUNCATE confirmation",
                "Require confirmation before executing TRUNCATE statements");

            AddGroupSeparator(panel);
            AddGroupHeader(panel, "Transaction Reminder");
            _chkSafetyTransactionReminder = AddToggle(panel, "Enable transaction reminder",
                "Periodically remind about open transactions on production servers");

            (_sldSafetyTransReminderInterval, _lblSafetyTransReminderValue) = AddSlider(panel,
                "Reminder interval (seconds)", 30, 3600, 300,
                "Time between transaction reminder notifications");

            return WrapInScrollViewer(panel);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Grid
        // ═══════════════════════════════════════════════════════════════════════
        private UIElement BuildGridPage()
        {
            var panel = CreatePagePanel();
            AddPageHeader(panel, "Results Grid");

            _chkGridAggregates = AddToggle(panel, "Aggregate statistics",
                "Show Sum, Avg, Count, Min, Max for selected cells");
            _chkGridNullHighlight = AddToggle(panel, "Highlight NULL cells",
                "Highlight NULL cells in results grid");
            _chkGridRowNumbers = AddToggle(panel, "Row numbers",
                "Show row numbers column");
            _chkGridFreezeHeaders = AddToggle(panel, "Freeze headers",
                "Freeze column headers while scrolling");

            AddGroupSeparator(panel);
            AddGroupHeader(panel, "Excel Export");
            _chkGridExcelLargeNumberAsText = AddToggle(panel, "Save 15+ digit numbers as text",
                "Numbers with 15 or more digits are saved as text to prevent Excel from rounding them");

            return WrapInScrollViewer(panel);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Editor Productivity
        // ═══════════════════════════════════════════════════════════════════════
        private UIElement BuildEditorPage()
        {
            var panel = CreatePagePanel();
            AddPageHeader(panel, "Editor Productivity");

            _chkEdHighlightOccurrences = AddToggle(panel, "Highlight occurrences",
                "Highlight all occurrences of selected identifier");
            _chkEdBracketMatching = AddToggle(panel, "Bracket matching",
                "Highlight matching BEGIN/END and parenthesis pairs");
            _chkEdNamedRegions = AddToggle(panel, "Named regions",
                "Show named region markers in editor");
            _chkEdStickyScroll = AddToggle(panel, "Sticky scroll",
                "Pin parent scope headers while scrolling");
            _chkEdMinimap = AddToggle(panel, "Code minimap",
                "Show code minimap in editor margin");
            _chkEdDocumentOutline = AddToggle(panel, "Document Outline",
                "Enable Document Outline panel");

            return WrapInScrollViewer(panel);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Execution Productivity
        // ═══════════════════════════════════════════════════════════════════════
        private UIElement BuildExecutionPage()
        {
            var panel = CreatePagePanel();
            AddPageHeader(panel, "Execution");

            _chkExecShowTimer = AddToggle(panel, "Execution timer",
                "Show execution timer in status bar");
            _chkExecMultiDatabase = AddToggle(panel, "Multi-database execution",
                "Enable multi-database execution mode");

            AddGroupHeader(panel, "Notifications");
            (_sldExecNotificationThreshold, _lblExecNotificationValue) = AddSlider(panel,
                "Notification threshold", 5, 300, 30,
                "Seconds before showing long-running query notification");

            return WrapInScrollViewer(panel);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Navigation
        // ═══════════════════════════════════════════════════════════════════════
        private UIElement BuildNavigationPage()
        {
            var panel = CreatePagePanel();
            AddPageHeader(panel, "Navigation");

            _chkNavGoToDefinition = AddToggle(panel, "Go to Definition",
                "Enable Go to Definition (F12)");
            _chkNavPeekDefinition = AddToggle(panel, "Peek Definition",
                "Enable Peek Definition (Alt+F12)");
            _chkNavFindReferences = AddToggle(panel, "Find All References",
                "Enable Find All References (Shift+F12)");
            _chkNavObjectSearch = AddToggle(panel, "Object Search",
                "Enable Object Search (Ctrl+T)");

            return WrapInScrollViewer(panel);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  AI Assistance
        // ═══════════════════════════════════════════════════════════════════════
        private UIElement BuildAiPage()
        {
            var panel = CreatePagePanel();

            AddPageHeader(panel, "AI Assistance");

            AddGroupHeader(panel, "Provider Configuration");
            _cboAiProvider = AddDropdown(panel, "AI Provider",
                new[] { "(None)", "Anthropic", "OpenAI", "Azure OpenAI", "Gemini", "Ollama", "LM Studio", "Custom" },
                "Select the AI provider for SQL assistance features");
            _txtAiModel = AddTextInput(panel, "Model",
                "e.g. gpt-4o, claude-sonnet-4-20250514, gemini-pro");
            _txtAiApiKey = AddTextInput(panel, "API Key",
                "Your API key for the selected provider", true);
            _txtAiEndpoint = AddTextInput(panel, "Endpoint URL",
                "Custom endpoint (required for Azure OpenAI and custom providers)");

            AddGroupSeparator(panel);
            AddGroupHeader(panel, "Privacy & Data");
            _cboAiPrivacyMode = AddDropdown(panel, "Privacy mode",
                new[] { "Schema Only", "Full", "Anonymous", "Offline", "Disabled" },
                "Controls what data is sent to the AI provider");

            AddGroupSeparator(panel);
            AddGroupHeader(panel, "Parameters");
            (_sldAiMaxTokens, _lblAiMaxTokensValue) = AddSlider(panel,
                "Max response tokens", 128, 128000, 4096,
                "Maximum number of tokens in the AI response", true);
            (_sldAiTemperature, _lblAiTemperatureValue) = AddSlider(panel,
                "Temperature (x10)", 0, 20, 2,
                "Sampling temperature: 0 = deterministic, 20 = creative");
            (_sldAiTimeout, _lblAiTimeoutValue) = AddSlider(panel,
                "Timeout (seconds)", 5, 300, 30,
                "Request timeout for AI API calls");
            (_sldAiRetries, _lblAiRetriesValue) = AddSlider(panel,
                "Retries", 0, 10, 2,
                "Number of automatic retries on transient failures");

            AddGroupSeparator(panel);
            AddGroupHeader(panel, "Features");
            _chkAiTextToSql = AddToggle(panel, "Natural language to SQL",
                "Generate SQL from plain English descriptions");
            _chkAiExplain = AddToggle(panel, "Explain SQL",
                "Get AI-powered explanations of SQL queries");
            _chkAiFix = AddToggle(panel, "Fix errors",
                "Suggest fixes when queries fail with errors");
            _chkAiOptimize = AddToggle(panel, "Optimize queries",
                "Get AI-powered query optimization suggestions");
            _chkAiIndexSuggestions = AddToggle(panel, "Index suggestions",
                "AI-powered index analysis and recommendations");
            _chkAiChatPanel = AddToggle(panel, "Chat panel",
                "Enable the AI chat side panel for interactive assistance");
            _chkAiInlineCompletion = AddToggle(panel, "Inline ghost text",
                "Show AI-powered inline completion suggestions as ghost text");
            _chkAiAutoFixOnError = AddToggle(panel, "Auto-fix on error",
                "Automatically suggest fixes when query execution fails");

            return WrapInScrollViewer(panel);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  UI Builder Helpers
        // ═══════════════════════════════════════════════════════════════════════

        private StackPanel CreatePagePanel()
        {
            return new StackPanel
            {
                Margin = new Thickness(24, 16, 24, 24),
                Background = _theme.Transparent
            };
        }

        private ScrollViewer WrapInScrollViewer(UIElement content)
        {
            return new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = _theme.Panel
            };
        }

        private void AddPageHeader(StackPanel panel, string text)
        {
            panel.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = _theme.FgWhite,
                Margin = new Thickness(0, 0, 0, 16)
            });
        }

        private void AddGroupHeader(StackPanel panel, string text)
        {
            panel.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = _theme.FgAccent,
                Margin = new Thickness(0, 4, 0, 10)
            });
        }

        private void AddGroupSeparator(StackPanel panel)
        {
            panel.Children.Add(new Border
            {
                Height = 1,
                Background = _theme.Sep,
                Margin = new Thickness(0, 16, 0, 12)
            });
        }

        private CheckBox AddToggle(StackPanel panel, string label, string description)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

            var cb = new CheckBox
            {
                Foreground = _theme.FgPrimary,
                FontSize = 13,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            // Build the content with label and description
            var contentPanel = new StackPanel();
            contentPanel.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = _theme.FgPrimary,
                FontSize = 13
            });
            if (!string.IsNullOrEmpty(description))
            {
                contentPanel.Children.Add(new TextBlock
                {
                    Text = description,
                    Foreground = _theme.FgSecondary,
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            cb.Content = contentPanel;
            row.Children.Add(cb);
            panel.Children.Add(row);
            return cb;
        }

        private (Slider slider, TextBlock valueLabel) AddSlider(
            StackPanel panel, string label, double min, double max, double defaultValue,
            string description, bool largeRange = false)
        {
            var container = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

            // Label row with value display
            var headerRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };

            var valueLabel = new TextBlock
            {
                Text = defaultValue.ToString(CultureInfo.InvariantCulture),
                Foreground = _theme.FgAccent,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                MinWidth = 60,
                TextAlignment = TextAlignment.Right
            };
            DockPanel.SetDock(valueLabel, Dock.Right);
            headerRow.Children.Add(valueLabel);

            headerRow.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = _theme.FgPrimary,
                FontSize = 13
            });

            container.Children.Add(headerRow);

            // Slider
            var slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = defaultValue,
                IsSnapToTickEnabled = true,
                TickFrequency = largeRange ? Math.Max(1, (max - min) / 100) : 1,
                Height = 22,
                Foreground = _theme.FgAccent
            };

            // Update value label on change
            var valueLabelRef = valueLabel;
            slider.ValueChanged += (s, e) =>
            {
                valueLabelRef.Text = ((int)e.NewValue).ToString(CultureInfo.InvariantCulture);
            };

            container.Children.Add(slider);

            // Description
            if (!string.IsNullOrEmpty(description))
            {
                container.Children.Add(new TextBlock
                {
                    Text = description,
                    Foreground = _theme.FgSecondary,
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            panel.Children.Add(container);
            return (slider, valueLabel);
        }

        private ComboBox AddDropdown(StackPanel panel, string label, string[] items, string description)
        {
            var container = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

            container.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = _theme.FgPrimary,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var combo = new ComboBox
            {
                Background = _theme.Input,
                Foreground = _theme.FgPrimary,
                BorderBrush = _theme.ComboBorder,
                BorderThickness = new Thickness(1),
                FontSize = 13,
                Height = 28,
                Padding = new Thickness(6, 4, 6, 4),
                MaxWidth = 300,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            // Apply themed styling to dropdown items
            StyleComboBox(combo);

            foreach (var item in items)
            {
                // Use TextBlock as content so foreground propagates correctly
                // through the Chrome template's ContentPresenter
                combo.Items.Add(new ComboBoxItem
                {
                    Content = new TextBlock { Text = item, Foreground = _theme.FgPrimary },
                    Foreground = _theme.FgPrimary
                });
            }

            if (combo.Items.Count > 0)
                combo.SelectedIndex = 0;

            container.Children.Add(combo);

            if (!string.IsNullOrEmpty(description))
            {
                container.Children.Add(new TextBlock
                {
                    Text = description,
                    Foreground = _theme.FgSecondary,
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            panel.Children.Add(container);
            return combo;
        }

        /// <summary>
        /// Applies themed styling to a ComboBox so the dropdown popup, items,
        /// hover highlight, and selected item all match the active theme.
        /// </summary>
        private void StyleComboBox(ComboBox combo)
        {
            combo.Background = _theme.Input;
            combo.Foreground = _theme.FgPrimary;
            combo.BorderBrush = _theme.ComboBorder;

            // Override system colors used by the default ComboBox Chrome template.
            // This is the ONLY reliable way to theme dropdown popups without
            // rewriting the entire ControlTemplate.
            combo.Resources[SystemColors.WindowBrushKey] = _theme.Input;
            combo.Resources[SystemColors.WindowTextBrushKey] = _theme.FgPrimary;
            combo.Resources[SystemColors.HighlightBrushKey] = _theme.Selected;
            combo.Resources[SystemColors.HighlightTextBrushKey] = _theme.SelectedText;
            combo.Resources[SystemColors.ControlBrushKey] = _theme.Input;
            combo.Resources[SystemColors.ControlTextBrushKey] = _theme.FgPrimary;
            combo.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = _theme.Selected;
            combo.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = _theme.SelectedText;

            // The default Chrome template's ContentPresenter inherits TextElement.Foreground
            // from its template parent (the toggle button), not from the ComboBox.Foreground
            // property. Setting TextElement.Foreground as an attached property on the ComboBox
            // ensures it propagates through the visual tree to the ContentPresenter, fixing
            // the bad text color/shadow in dark mode.
            combo.SetValue(TextElement.ForegroundProperty, _theme.FgPrimary);

            var itemStyle = new Style(typeof(ComboBoxItem));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, _theme.Input));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, _theme.FgPrimary));
            itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 4, 6, 4)));

            var hoverTrigger = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, _theme.Selected));
            hoverTrigger.Setters.Add(new Setter(Control.ForegroundProperty, _theme.SelectedText));
            itemStyle.Triggers.Add(hoverTrigger);

            var selectedTrigger = new Trigger { Property = ComboBoxItem.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, _theme.FgAccent));
            selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty,
                Freeze(new SolidColorBrush(Colors.White))));
            itemStyle.Triggers.Add(selectedTrigger);

            combo.ItemContainerStyle = itemStyle;
        }

        private TextBox AddTextInput(StackPanel panel, string label, string description,
            bool isPassword = false)
        {
            var container = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

            container.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = _theme.FgPrimary,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var textBox = new TextBox
            {
                Background = _theme.Input,
                Foreground = _theme.FgPrimary,
                BorderBrush = _theme.ComboBorder,
                BorderThickness = new Thickness(1),
                CaretBrush = _theme.Caret,
                FontSize = 13,
                Height = 28,
                Padding = new Thickness(6, 4, 6, 4),
                MaxWidth = 500,
                HorizontalAlignment = HorizontalAlignment.Left,
                MinWidth = 300
            };

            // For password fields, we obscure by using a special character replacement
            // (WPF PasswordBox cannot bind Text easily, so we use TextBox with masking hint)
            if (isPassword)
            {
                textBox.Tag = "password";
            }

            container.Children.Add(textBox);

            if (!string.IsNullOrEmpty(description))
            {
                container.Children.Add(new TextBlock
                {
                    Text = description,
                    Foreground = _theme.FgSecondary,
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            panel.Children.Add(container);
            return textBox;
        }

        private void AddReadOnlyField(StackPanel panel, string label, string value)
        {
            var container = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

            container.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = _theme.FgSecondary,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 2)
            });

            var textBox = new TextBox
            {
                Text = value,
                Background = _theme.InputReadOnly,
                Foreground = _theme.FgSecondary,
                BorderBrush = _theme.Border,
                BorderThickness = new Thickness(1),
                FontSize = 12,
                IsReadOnly = true,
                Height = 26,
                Padding = new Thickness(6, 3, 6, 3),
                MaxWidth = 500,
                HorizontalAlignment = HorizontalAlignment.Left,
                MinWidth = 300
            };

            container.Children.Add(textBox);
            panel.Children.Add(container);
        }

        private void AddInfoRow(StackPanel panel, string label, string value)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 6) };

            row.Children.Add(new TextBlock
            {
                Text = label + ":",
                Foreground = _theme.FgSecondary,
                FontSize = 12,
                MinWidth = 120,
                Margin = new Thickness(0, 0, 8, 0)
            });

            row.Children.Add(new TextBlock
            {
                Text = value,
                Foreground = _theme.FgPrimary,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });

            panel.Children.Add(row);
        }

        private Button MakeButton(string text, double width)
        {
            var btn = new Button
            {
                Content = text,
                Width = width,
                Height = 30,
                FontSize = 13,
                Foreground = _theme.FgPrimary,
                Background = _theme.Button,
                BorderBrush = _theme.Border,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 4, 12, 4),
                Cursor = Cursors.Hand
            };

            var theme = _theme; // capture for lambda
            btn.MouseEnter += (s, e) => btn.Background = theme.ButtonHover;
            btn.MouseLeave += (s, e) => btn.Background = theme.Button;

            return btn;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Navigation
        // ═══════════════════════════════════════════════════════════════════════

        private void OnNavSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_navTree?.SelectedItem is TreeViewItem item && item.Tag is string category)
            {
                if (_pages.TryGetValue(category, out var page) && _contentHost != null)
                {
                    _contentHost.Content = page;
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Button handlers
        // ═══════════════════════════════════════════════════════════════════════

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            SaveControlsToSettings();
            _dialogResult = true;
            _window?.Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            _dialogResult = false;
            _window?.Close();
        }

        private void OnApplyClick(object sender, RoutedEventArgs e)
        {
            SaveControlsToSettings();
            try
            {
                ConfigManager.Save(_settings);
                _dialogResult = true;
                Log.Information("Settings applied via SettingsWindow");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SettingsWindow: Apply failed");
                MessageBox.Show(
                    "Failed to apply settings: " + ex.Message,
                    Constants.ProductName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                _dialogResult = false;
                _window?.Close();
                e.Handled = true;
            }
        }

        private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_cboTheme == null) return;
            var idx = _cboTheme.SelectedIndex;

            // Index 0 = Dark, Index 1 = Light, Index 2 = System (auto-detect from VS/SSMS)
            var requestedTheme = idx switch
            {
                0 => ThemeBrushSet.Dark,
                2 => Ui.ThemeManager.DetectFromEnvironment() == Ui.VsThemeKind.Dark
                    ? ThemeBrushSet.Dark : ThemeBrushSet.Light,
                _ => ThemeBrushSet.Light
            };

            if (_theme == requestedTheme)
                return; // no change needed

            // Save the new theme preference immediately so the reopened window uses it
            SaveControlsToSettings();
            try
            {
                ConfigManager.Save(_settings);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SettingsWindow: Failed to save theme change");
            }

            // Update ThemeManager so all AKML SQL UI picks up the new theme
            Ui.ThemeManager.Instance.SetUserTheme(_settings.Theme);

            // Signal that the window should be reopened with the new theme
            ThemeChangeRequested = true;
            _dialogResult = true;
            _window?.Close();
        }

        // ─── Export / Import ─────────────────────────────────────────────────

        private static readonly JsonSerializerOptions ExportSerializerOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private void OnExportProfileClick(object sender, RoutedEventArgs e)
        {
            try
            {
                // Capture current UI state into settings before exporting
                SaveControlsToSettings();

                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Export AKML SQL Settings",
                    Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                    FileName = "akml-settings.json",
                    DefaultExt = ".json",
                    OverwritePrompt = true
                };

                if (dlg.ShowDialog(_window) == true)
                {
                    var json = JsonSerializer.Serialize(_settings, ExportSerializerOptions);
                    File.WriteAllText(dlg.FileName, json);
                    Log.Information("Settings exported to {Path}", dlg.FileName);
                    MessageBox.Show(
                        "Settings exported successfully.",
                        Constants.ProductName,
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SettingsWindow: Export failed");
                MessageBox.Show(
                    "Failed to export settings: " + ex.Message,
                    Constants.ProductName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void OnImportProfileClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Import AKML SQL Settings",
                    Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                    DefaultExt = ".json",
                    CheckFileExists = true
                };

                if (dlg.ShowDialog(_window) != true)
                    return;

                var content = File.ReadAllText(dlg.FileName);

                var imported = JsonSerializer.Deserialize<AppSettings>(content, ExportSerializerOptions);
                if (imported == null)
                {
                    MessageBox.Show(
                        "The selected file does not contain valid AKML SQL settings.",
                        Constants.ProductName,
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // Preserve the current install-specific fields that should not be overwritten
                imported.InstallId = _settings.InstallId;
                imported.InstalledTargets = _settings.InstalledTargets;
                imported.LastUpdateCheck = _settings.LastUpdateCheck;

                _settings = imported;
                LoadSettingsToControls();
                Log.Information("Settings imported from {Path}", dlg.FileName);
                MessageBox.Show(
                    "Settings imported successfully.\nClick OK or Apply to save.",
                    Constants.ProductName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (JsonException ex)
            {
                Log.Warning(ex, "SettingsWindow: Import parse failed");
                MessageBox.Show(
                    "The selected file is not valid JSON:\n" + ex.Message,
                    Constants.ProductName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SettingsWindow: Import failed");
                MessageBox.Show(
                    "Failed to import settings: " + ex.Message,
                    Constants.ProductName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void OnResetThisPageClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedItem = _navTree?.SelectedItem as TreeViewItem;
                var pageName = selectedItem?.Tag as string;
                if (string.IsNullOrEmpty(pageName))
                {
                    MessageBox.Show("Select a settings page first.", Constants.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (MessageBox.Show($"Reset all settings on the '{pageName}' page to defaults?",
                    Constants.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;

                var defaults = new AppSettings();
                switch (pageName)
                {
                    case "General":
                        _settings.AutoUpdateEnabled = defaults.AutoUpdateEnabled;
                        _settings.TelemetryEnabled = defaults.TelemetryEnabled;
                        _settings.Theme = defaults.Theme;
                        break;
                    case "IntelliSense": _settings.IntelliSense = defaults.IntelliSense; break;
                    case "Schema Cache": _settings.Cache = defaults.Cache; break;
                    case "Formatting": _settings.Formatter = defaults.Formatter; break;
                    case "Snippets": _settings.Snippets = defaults.Snippets; break;
                    case "Code Analysis": _settings.CodeAnalysis = defaults.CodeAnalysis; break;
                    case "Refactoring": _settings.Refactoring = defaults.Refactoring; break;
                    case "History": _settings.History = defaults.History; break;
                    case "Tabs & UI": _settings.Tabs = defaults.Tabs; break;
                    case "Safety": _settings.Safety = defaults.Safety; break;
                    case "Grid": _settings.Grid = defaults.Grid; break;
                    case "Editor": _settings.EditorProductivity = defaults.EditorProductivity; break;
                    case "Execution": _settings.ExecutionProductivity = defaults.ExecutionProductivity; break;
                    case "Navigation": _settings.Navigation = defaults.Navigation; break;
                    case "AI Assistance": _settings.Ai = defaults.Ai; break;
                }
                LoadSettingsToControls();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SettingsWindow: Reset page failed");
            }
        }

        private void OnResetAllClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MessageBox.Show("Reset ALL settings to defaults? This cannot be undone.",
                    Constants.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;

                _settings = new AppSettings();
                LoadSettingsToControls();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SettingsWindow: Reset all failed");
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Load settings into controls
        // ═══════════════════════════════════════════════════════════════════════

        private void LoadSettingsToControls()
        {
            // ── General ──────────────────────────────────────────────────
            SetChecked(_chkAutoUpdate, _settings.AutoUpdateEnabled);
            SetChecked(_chkTelemetry, _settings.TelemetryEnabled);
            var themeIdx = (_settings.Theme?.ToLowerInvariant()) switch
            {
                "light" => 1,
                "system" => 2,
                _ => 0 // "dark" or unset
            };
            SetCombo(_cboTheme, themeIdx);

            // ── IntelliSense ─────────────────────────────────────────────
            var i = _settings.IntelliSense;
            SetChecked(_chkIsEnabled, i.Enabled);
            SetChecked(_chkAutoTrigger, i.AutoTrigger);
            SetChecked(_chkAfterDot, i.AfterDot);
            SetChecked(_chkFuzzyMatch, i.FuzzyMatch);
            SetChecked(_chkShowDataTypes, i.ShowDataTypes);
            SetChecked(_chkShowNullability, i.ShowNullability);
            SetChecked(_chkShowPkFk, i.ShowPkFk);
            SetChecked(_chkAutoAlias, i.AutoAlias);
            SetChecked(_chkJoinAssist, i.JoinAssist);
            SetChecked(_chkDisableNativeIs, i.DisableNativeIntelliSense);
            SetSlider(_sldTriggerDelay, _lblTriggerDelayValue, i.TriggerDelayMs);
            SetSlider(_sldMaxSuggestions, _lblMaxSuggestionsValue, i.MaxSuggestions);
            SetCombo(_cboKeywordCase, (int)i.KeywordCase);

            // ── Schema Cache ─────────────────────────────────────────────
            var c = _settings.Cache;
            SetChecked(_chkCacheAutoRefresh, c.AutoRefresh);
            SetChecked(_chkDetectDdl, c.DetectDdl);
            SetChecked(_chkLazyLoadColumns, c.LazyLoadColumns);
            SetChecked(_chkPersistToDisk, c.PersistToDisk);
            SetSlider(_sldRefreshInterval, _lblRefreshIntervalValue, c.RefreshIntervalSeconds);
            SetSlider(_sldMaxDatabases, _lblMaxDatabasesValue, c.MaxDatabases);

            // ── Formatting ───────────────────────────────────────────────
            var f = _settings.Formatter;
            SetChecked(_chkFmtEnabled, f.Enabled);
            SetChecked(_chkFormatOnPaste, f.FormatOnPaste);
            SetChecked(_chkFormatOnSave, f.FormatOnSave);
            SetChecked(_chkFormatOnDelimiter, f.FormatOnDelimiter);
            SetChecked(_chkConfirmBulk, f.ConfirmBulkFormat);
            SetChecked(_chkCreateBackups, f.CreateBackups);
            SetChecked(_chkRespectNoformat, f.RespectNoformat);
            SetChecked(_chkSemanticValidation, f.SemanticValidation);

            // ── Snippets ─────────────────────────────────────────────────
            var s = _settings.Snippets;
            SetChecked(_chkSnipEnabled, s.Enabled);
            SetChecked(_chkSnipShowInCompletion, s.ShowInCompletion);
            SetChecked(_chkSnipFormatOnExpand, s.FormatOnExpand);
            SetChecked(_chkSnipContextFilter, s.ContextFilter);
            SetChecked(_chkSnipTrackUsage, s.TrackUsage);
            SetText(_txtPersonalFolder, s.PersonalFolder);
            SetText(_txtTeamFolder, s.TeamFolder);

            // ── Code Analysis ────────────────────────────────────────────
            var ca = _settings.CodeAnalysis;
            SetChecked(_chkAnalysisEnabled, ca.Enabled);
            SetChecked(_chkAnalysisRunOnType, ca.RunOnType);
            SetChecked(_chkAnalysisRunOnSave, ca.RunOnSave);
            SetChecked(_chkAnalysisShowInErrorList, ca.ShowInErrorList);

            // ── Refactoring ──────────────────────────────────────────────
            var rf = _settings.Refactoring;
            SetChecked(_chkRefPreviewBeforeApply, rf.PreviewBeforeApply);
            SetChecked(_chkRefCreateBackups, rf.CreateBackups);
            SetChecked(_chkRefFormatAfterRefactor, rf.FormatAfterRefactor);
            SetChecked(_chkRefIncludeCommentsInRename, rf.IncludeCommentsInRename);
            SetChecked(_chkRefIncludeStringLiteralsInRename, rf.IncludeStringLiteralsInRename);
            SetCombo(_cboRefRenameScope, rf.RenameScope == "projectDirectory" ? 1 : 0);

            // ── History ──────────────────────────────────────────────────
            var h = _settings.History;
            SetChecked(_chkHistEnabled, h.Enabled);
            SetChecked(_chkHistRecordFailures, h.RecordFailures);
            SetChecked(_chkHistDeduplication, h.Deduplication);
            SetChecked(_chkHistEncryptAtRest, h.EncryptAtRest);
            SetSlider(_sldHistRetentionDays, _lblHistRetentionValue, h.RetentionDays);
            SetSlider(_sldHistMaxEntries, _lblHistMaxEntriesValue, h.MaxEntries);

            // ── Tabs & UI ────────────────────────────────────────────────
            var t = _settings.Tabs;
            SetChecked(_chkTabColoringEnabled, t.ColoringEnabled);
            SetChecked(_chkTabGradientColors, t.GradientColors);
            SetChecked(_chkTabSessionRecovery, t.SessionRecovery);
            SetSlider(_sldTabAutoSaveInterval, _lblTabAutoSaveValue, t.AutoSaveInterval);
            SetSlider(_sldTabMaxClosedTabs, _lblTabMaxClosedTabsValue, t.MaxClosedTabs);
            SetText(_txtTabCustomWindowTitle, t.CustomWindowTitle);
            var restoreIdx = t.RestoreOnStartup?.ToLowerInvariant() switch
            {
                "always" => 1,
                "never" => 2,
                _ => 0
            };
            SetCombo(_cboTabRestoreOnStartup, restoreIdx);

            // ── Safety ───────────────────────────────────────────────────
            var sf = _settings.Safety;
            SetChecked(_chkSafetyProductionWarning, sf.ProductionWarning);
            SetChecked(_chkSafetyDeleteWithoutWhere, sf.DeleteWithoutWhere);
            SetChecked(_chkSafetyUpdateWithoutWhere, sf.UpdateWithoutWhere);
            SetChecked(_chkSafetyDropConfirmation, sf.DropConfirmation);
            SetChecked(_chkSafetyTruncateConfirmation, sf.TruncateConfirmation);
            SetChecked(_chkSafetyTransactionReminder, sf.TransactionReminder);
            SetSlider(_sldSafetyTransReminderInterval, _lblSafetyTransReminderValue, sf.TransactionReminderInterval);

            // ── AI Assistance ────────────────────────────────────────────
            var ai = _settings.Ai;
            var providerIdx = (ai.Provider?.ToLowerInvariant()) switch
            {
                "anthropic" => 1,
                "openai" => 2,
                "azureopenai" => 3,
                "gemini" => 4,
                "ollama" => 5,
                "lmstudio" => 6,
                "custom" => 7,
                _ => 0
            };
            SetCombo(_cboAiProvider, providerIdx);
            SetText(_txtAiModel, ai.Model);
            SetText(_txtAiApiKey, ai.ApiKey);
            SetText(_txtAiEndpoint, ai.Endpoint);

            var privacyIdx = (ai.PrivacyMode?.ToLowerInvariant()) switch
            {
                "full" => 1,
                "anonymous" => 2,
                "offline" => 3,
                "disabled" => 4,
                _ => 0 // schemaOnly
            };
            SetCombo(_cboAiPrivacyMode, privacyIdx);

            SetSlider(_sldAiMaxTokens, _lblAiMaxTokensValue, ai.MaxTokens);
            SetSlider(_sldAiTemperature, _lblAiTemperatureValue, (int)(ai.Temperature * 10));
            SetSlider(_sldAiTimeout, _lblAiTimeoutValue, ai.Timeout);
            SetSlider(_sldAiRetries, _lblAiRetriesValue, ai.Retries);

            SetChecked(_chkAiTextToSql, ai.TextToSql);
            SetChecked(_chkAiExplain, ai.Explain);
            SetChecked(_chkAiFix, ai.Fix);
            SetChecked(_chkAiOptimize, ai.Optimize);
            SetChecked(_chkAiIndexSuggestions, ai.IndexSuggestions);
            SetChecked(_chkAiChatPanel, ai.ChatPanel);
            SetChecked(_chkAiInlineCompletion, ai.InlineCompletion);
            SetChecked(_chkAiAutoFixOnError, ai.AutoFixOnError);

            // ── Grid ─────────────────────────────────────────────────────
            var gr = _settings.Grid;
            SetChecked(_chkGridAggregates, gr.Aggregates);
            SetChecked(_chkGridNullHighlight, gr.NullHighlight);
            SetChecked(_chkGridRowNumbers, gr.RowNumbers);
            SetChecked(_chkGridFreezeHeaders, gr.FreezeHeaders);
            SetChecked(_chkGridExcelLargeNumberAsText, gr.ExcelLargeNumberAsText);

            // ── Editor Productivity ──────────────────────────────────────
            var ep = _settings.EditorProductivity;
            SetChecked(_chkEdHighlightOccurrences, ep.HighlightOccurrences);
            SetChecked(_chkEdBracketMatching, ep.BracketMatching);
            SetChecked(_chkEdNamedRegions, ep.NamedRegions);
            SetChecked(_chkEdStickyScroll, ep.StickyScroll);
            SetChecked(_chkEdMinimap, ep.Minimap);
            SetChecked(_chkEdDocumentOutline, ep.DocumentOutline);

            // ── Execution ────────────────────────────────────────────────
            var ex = _settings.ExecutionProductivity;
            SetChecked(_chkExecShowTimer, ex.ShowExecutionTimer);
            SetChecked(_chkExecMultiDatabase, ex.MultiDatabase);
            SetSlider(_sldExecNotificationThreshold, _lblExecNotificationValue, ex.NotificationThreshold);

            // ── Navigation ───────────────────────────────────────────────
            var nav = _settings.Navigation;
            SetChecked(_chkNavGoToDefinition, nav.GoToDefinition);
            SetChecked(_chkNavPeekDefinition, nav.PeekDefinition);
            SetChecked(_chkNavFindReferences, nav.FindReferences);
            SetChecked(_chkNavObjectSearch, nav.ObjectSearch);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Save controls to settings
        // ═══════════════════════════════════════════════════════════════════════

        private void SaveControlsToSettings()
        {
            // ── General ──────────────────────────────────────────────────
            _settings.AutoUpdateEnabled = IsChecked(_chkAutoUpdate);
            _settings.TelemetryEnabled = IsChecked(_chkTelemetry);
            _settings.Theme = GetComboIndex(_cboTheme) switch
            {
                1 => "light",
                2 => "system",
                _ => "dark"
            };

            // ── IntelliSense ─────────────────────────────────────────────
            _settings.IntelliSense.Enabled = IsChecked(_chkIsEnabled);
            _settings.IntelliSense.AutoTrigger = IsChecked(_chkAutoTrigger);
            _settings.IntelliSense.AfterDot = IsChecked(_chkAfterDot);
            _settings.IntelliSense.FuzzyMatch = IsChecked(_chkFuzzyMatch);
            _settings.IntelliSense.ShowDataTypes = IsChecked(_chkShowDataTypes);
            _settings.IntelliSense.ShowNullability = IsChecked(_chkShowNullability);
            _settings.IntelliSense.ShowPkFk = IsChecked(_chkShowPkFk);
            _settings.IntelliSense.AutoAlias = IsChecked(_chkAutoAlias);
            _settings.IntelliSense.JoinAssist = IsChecked(_chkJoinAssist);
            _settings.IntelliSense.DisableNativeIntelliSense = IsChecked(_chkDisableNativeIs);
            _settings.IntelliSense.TriggerDelayMs = GetSliderInt(_sldTriggerDelay);
            _settings.IntelliSense.MaxSuggestions = GetSliderInt(_sldMaxSuggestions);
            _settings.IntelliSense.KeywordCase = (KeywordCaseOption)GetComboIndex(_cboKeywordCase);

            // ── Schema Cache ─────────────────────────────────────────────
            _settings.Cache.AutoRefresh = IsChecked(_chkCacheAutoRefresh);
            _settings.Cache.DetectDdl = IsChecked(_chkDetectDdl);
            _settings.Cache.LazyLoadColumns = IsChecked(_chkLazyLoadColumns);
            _settings.Cache.PersistToDisk = IsChecked(_chkPersistToDisk);
            _settings.Cache.RefreshIntervalSeconds = GetSliderInt(_sldRefreshInterval);
            _settings.Cache.MaxDatabases = GetSliderInt(_sldMaxDatabases);

            // ── Formatting ───────────────────────────────────────────────
            _settings.Formatter.Enabled = IsChecked(_chkFmtEnabled);
            _settings.Formatter.FormatOnPaste = IsChecked(_chkFormatOnPaste);
            _settings.Formatter.FormatOnSave = IsChecked(_chkFormatOnSave);
            _settings.Formatter.FormatOnDelimiter = IsChecked(_chkFormatOnDelimiter);
            _settings.Formatter.ConfirmBulkFormat = IsChecked(_chkConfirmBulk);
            _settings.Formatter.CreateBackups = IsChecked(_chkCreateBackups);
            _settings.Formatter.RespectNoformat = IsChecked(_chkRespectNoformat);
            _settings.Formatter.SemanticValidation = IsChecked(_chkSemanticValidation);

            // ── Snippets ─────────────────────────────────────────────────
            _settings.Snippets.Enabled = IsChecked(_chkSnipEnabled);
            _settings.Snippets.ShowInCompletion = IsChecked(_chkSnipShowInCompletion);
            _settings.Snippets.FormatOnExpand = IsChecked(_chkSnipFormatOnExpand);
            _settings.Snippets.ContextFilter = IsChecked(_chkSnipContextFilter);
            _settings.Snippets.TrackUsage = IsChecked(_chkSnipTrackUsage);
            _settings.Snippets.PersonalFolder = GetText(_txtPersonalFolder);
            _settings.Snippets.TeamFolder = GetText(_txtTeamFolder);

            // ── Code Analysis ────────────────────────────────────────────
            _settings.CodeAnalysis.Enabled = IsChecked(_chkAnalysisEnabled);
            _settings.CodeAnalysis.RunOnType = IsChecked(_chkAnalysisRunOnType);
            _settings.CodeAnalysis.RunOnSave = IsChecked(_chkAnalysisRunOnSave);
            _settings.CodeAnalysis.ShowInErrorList = IsChecked(_chkAnalysisShowInErrorList);

            // ── Refactoring ──────────────────────────────────────────────
            _settings.Refactoring.PreviewBeforeApply = IsChecked(_chkRefPreviewBeforeApply);
            _settings.Refactoring.CreateBackups = IsChecked(_chkRefCreateBackups);
            _settings.Refactoring.FormatAfterRefactor = IsChecked(_chkRefFormatAfterRefactor);
            _settings.Refactoring.IncludeCommentsInRename = IsChecked(_chkRefIncludeCommentsInRename);
            _settings.Refactoring.IncludeStringLiteralsInRename = IsChecked(_chkRefIncludeStringLiteralsInRename);
            _settings.Refactoring.RenameScope = GetComboIndex(_cboRefRenameScope) == 1
                ? "projectDirectory" : "currentScript";

            // ── History ──────────────────────────────────────────────────
            _settings.History.Enabled = IsChecked(_chkHistEnabled);
            _settings.History.RecordFailures = IsChecked(_chkHistRecordFailures);
            _settings.History.Deduplication = IsChecked(_chkHistDeduplication);
            _settings.History.EncryptAtRest = IsChecked(_chkHistEncryptAtRest);
            _settings.History.RetentionDays = GetSliderInt(_sldHistRetentionDays);
            _settings.History.MaxEntries = GetSliderInt(_sldHistMaxEntries);

            // ── Tabs & UI ────────────────────────────────────────────────
            _settings.Tabs.ColoringEnabled = IsChecked(_chkTabColoringEnabled);
            _settings.Tabs.GradientColors = IsChecked(_chkTabGradientColors);
            _settings.Tabs.SessionRecovery = IsChecked(_chkTabSessionRecovery);
            _settings.Tabs.AutoSaveInterval = GetSliderInt(_sldTabAutoSaveInterval);
            _settings.Tabs.MaxClosedTabs = GetSliderInt(_sldTabMaxClosedTabs);
            _settings.Tabs.CustomWindowTitle = GetText(_txtTabCustomWindowTitle);
            _settings.Tabs.RestoreOnStartup = GetComboIndex(_cboTabRestoreOnStartup) switch
            {
                1 => "always",
                2 => "never",
                _ => "prompt"
            };

            // ── Safety ───────────────────────────────────────────────────
            _settings.Safety.ProductionWarning = IsChecked(_chkSafetyProductionWarning);
            _settings.Safety.DeleteWithoutWhere = IsChecked(_chkSafetyDeleteWithoutWhere);
            _settings.Safety.UpdateWithoutWhere = IsChecked(_chkSafetyUpdateWithoutWhere);
            _settings.Safety.DropConfirmation = IsChecked(_chkSafetyDropConfirmation);
            _settings.Safety.TruncateConfirmation = IsChecked(_chkSafetyTruncateConfirmation);
            _settings.Safety.TransactionReminder = IsChecked(_chkSafetyTransactionReminder);
            _settings.Safety.TransactionReminderInterval = GetSliderInt(_sldSafetyTransReminderInterval);

            // ── AI Assistance ────────────────────────────────────────────
            _settings.Ai.Provider = GetComboIndex(_cboAiProvider) switch
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
            _settings.Ai.Model = GetText(_txtAiModel);
            _settings.Ai.ApiKey = GetText(_txtAiApiKey);
            _settings.Ai.Endpoint = GetText(_txtAiEndpoint);
            _settings.Ai.PrivacyMode = GetComboIndex(_cboAiPrivacyMode) switch
            {
                1 => "full",
                2 => "anonymous",
                3 => "offline",
                4 => "disabled",
                _ => "schemaOnly"
            };
            _settings.Ai.MaxTokens = GetSliderInt(_sldAiMaxTokens);
            _settings.Ai.Temperature = GetSliderInt(_sldAiTemperature) / 10.0;
            _settings.Ai.Timeout = GetSliderInt(_sldAiTimeout);
            _settings.Ai.Retries = GetSliderInt(_sldAiRetries);

            _settings.Ai.Enabled = GetComboIndex(_cboAiProvider) > 0;
            _settings.Ai.TextToSql = IsChecked(_chkAiTextToSql);
            _settings.Ai.Explain = IsChecked(_chkAiExplain);
            _settings.Ai.Fix = IsChecked(_chkAiFix);
            _settings.Ai.Optimize = IsChecked(_chkAiOptimize);
            _settings.Ai.IndexSuggestions = IsChecked(_chkAiIndexSuggestions);
            _settings.Ai.ChatPanel = IsChecked(_chkAiChatPanel);
            _settings.Ai.InlineCompletion = IsChecked(_chkAiInlineCompletion);
            _settings.Ai.AutoFixOnError = IsChecked(_chkAiAutoFixOnError);

            // ── Grid ─────────────────────────────────────────────────────
            _settings.Grid.Aggregates = IsChecked(_chkGridAggregates);
            _settings.Grid.NullHighlight = IsChecked(_chkGridNullHighlight);
            _settings.Grid.RowNumbers = IsChecked(_chkGridRowNumbers);
            _settings.Grid.FreezeHeaders = IsChecked(_chkGridFreezeHeaders);
            _settings.Grid.ExcelLargeNumberAsText = IsChecked(_chkGridExcelLargeNumberAsText);

            // ── Editor Productivity ──────────────────────────────────────
            _settings.EditorProductivity.HighlightOccurrences = IsChecked(_chkEdHighlightOccurrences);
            _settings.EditorProductivity.BracketMatching = IsChecked(_chkEdBracketMatching);
            _settings.EditorProductivity.NamedRegions = IsChecked(_chkEdNamedRegions);
            _settings.EditorProductivity.StickyScroll = IsChecked(_chkEdStickyScroll);
            _settings.EditorProductivity.Minimap = IsChecked(_chkEdMinimap);
            _settings.EditorProductivity.DocumentOutline = IsChecked(_chkEdDocumentOutline);

            // ── Execution ────────────────────────────────────────────────
            _settings.ExecutionProductivity.ShowExecutionTimer = IsChecked(_chkExecShowTimer);
            _settings.ExecutionProductivity.MultiDatabase = IsChecked(_chkExecMultiDatabase);
            _settings.ExecutionProductivity.NotificationThreshold = GetSliderInt(_sldExecNotificationThreshold);

            // ── Navigation ───────────────────────────────────────────────
            _settings.Navigation.GoToDefinition = IsChecked(_chkNavGoToDefinition);
            _settings.Navigation.PeekDefinition = IsChecked(_chkNavPeekDefinition);
            _settings.Navigation.FindReferences = IsChecked(_chkNavFindReferences);
            _settings.Navigation.ObjectSearch = IsChecked(_chkNavObjectSearch);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Null-safe helpers
        // ═══════════════════════════════════════════════════════════════════════

        private static void SetChecked(CheckBox? cb, bool value)
        {
            if (cb != null) cb.IsChecked = value;
        }

        private static bool IsChecked(CheckBox? cb) => cb?.IsChecked == true;

        private static void SetSlider(Slider? slider, TextBlock? label, double value)
        {
            if (slider == null) return;
            var clamped = Math.Max(slider.Minimum, Math.Min(slider.Maximum, value));
            slider.Value = clamped;
            if (label != null) label.Text = ((int)clamped).ToString(CultureInfo.InvariantCulture);
        }

        private static int GetSliderInt(Slider? slider) => slider != null ? (int)slider.Value : 0;

        private static void SetCombo(ComboBox? combo, int index)
        {
            if (combo != null && index >= 0 && index < combo.Items.Count)
                combo.SelectedIndex = index;
        }

        private static int GetComboIndex(ComboBox? combo) => combo?.SelectedIndex ?? 0;

        private static void SetText(TextBox? textBox, string? value)
        {
            if (textBox != null) textBox.Text = value ?? string.Empty;
        }

        private static string GetText(TextBox? textBox) => textBox?.Text?.Trim() ?? string.Empty;
    }
}
