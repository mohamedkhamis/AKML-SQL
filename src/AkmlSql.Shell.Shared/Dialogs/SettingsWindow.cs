#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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

            // ── SQL Prompt-aligned theme palettes ──
            // Source: doc/SQL-PROMPT/SQL-Prompt-Option/SQL_Prompt_Options_Dialog.md §18.5
            public static readonly ThemeBrushSet Dark = new ThemeBrushSet(
                main:        Color.FromRgb(0x2D, 0x2D, 0x3B), // #2D2D3B  dialog background
                sidebar:     Color.FromRgb(0x1E, 0x1E, 0x2E), // #1E1E2E  tree nav background (SQL Prompt panel)
                panel:       Color.FromRgb(0x1E, 0x1E, 0x2E), // #1E1E2E  content panel background
                input:       Color.FromRgb(0x2D, 0x2D, 0x3B), // #2D2D3B  input bg (matches dialog)
                inputReadOnly: Color.FromRgb(0x25, 0x28, 0x36), // #252836 read-only field
                button:      Color.FromRgb(0x3A, 0x3F, 0x4E), // #3A3F4E  outlined button bg
                buttonHover: Color.FromRgb(0x4A, 0x4F, 0x5E), // #4A4F5E  button hover
                selected:    Color.FromRgb(0x00, 0x78, 0xD4), // #0078D4  accent (selected nav, primary button)
                border:      Color.FromRgb(0x3A, 0x3F, 0x4E), // #3A3F4E  border
                comboBorder: Color.FromRgb(0x3A, 0x3F, 0x4E), // #3A3F4E  input border
                fgPrimary:   Color.FromRgb(0xD4, 0xD4, 0xD4), // #D4D4D4  primary text + section header
                fgSecondary: Color.FromRgb(0x88, 0x92, 0xA8), // #8892A8  unselected nav + setting label
                fgAccent:    Color.FromRgb(0x4F, 0x8C, 0xFF), // #4F8CFF  page title + link (dark variant)
                fgWhite:     Color.FromRgb(0xFF, 0xFF, 0xFF), // #FFFFFF
                selectedText: Color.FromRgb(0xFF, 0xFF, 0xFF),// #FFFFFF  text on accent
                sep:         Color.FromRgb(0x3A, 0x3F, 0x4E), // #3A3F4E  separator
                treeHover:   Color.FromRgb(0x25, 0x28, 0x36), // #252836  nav hover (matches row alt)
                caret:       Color.FromRgb(0xFF, 0xFF, 0xFF)
            );

            public static readonly ThemeBrushSet Light = new ThemeBrushSet(
                main:        Color.FromRgb(0xF0, 0xF0, 0xF0), // #F0F0F0  dialog background
                sidebar:     Color.FromRgb(0xFF, 0xFF, 0xFF), // #FFFFFF  tree nav background (SQL Prompt panel)
                panel:       Color.FromRgb(0xFF, 0xFF, 0xFF), // #FFFFFF  content panel background
                input:       Color.FromRgb(0xFF, 0xFF, 0xFF), // #FFFFFF  input bg
                inputReadOnly: Color.FromRgb(0xF8, 0xF8, 0xF8), // #F8F8F8 read-only / row alt
                button:      Color.FromRgb(0xFF, 0xFF, 0xFF), // #FFFFFF  outlined button bg
                buttonHover: Color.FromRgb(0xE8, 0xE8, 0xE8), // #E8E8E8  button hover
                selected:    Color.FromRgb(0x00, 0x78, 0xD4), // #0078D4  accent (selected nav, primary button)
                border:      Color.FromRgb(0xCC, 0xCC, 0xCC), // #CCCCCC  border
                comboBorder: Color.FromRgb(0xCC, 0xCC, 0xCC), // #CCCCCC  input border
                fgPrimary:   Color.FromRgb(0x33, 0x33, 0x33), // #333333  primary text + section header
                fgSecondary: Color.FromRgb(0x55, 0x55, 0x55), // #555555  unselected nav + setting label
                fgAccent:    Color.FromRgb(0x00, 0x78, 0xD4), // #0078D4  page title + link
                fgWhite:     Color.FromRgb(0x33, 0x33, 0x33), // #333333  headings on light
                selectedText: Color.FromRgb(0xFF, 0xFF, 0xFF),// #FFFFFF  text on accent
                sep:         Color.FromRgb(0xCC, 0xCC, 0xCC), // #CCCCCC  separator
                treeHover:   Color.FromRgb(0xF0, 0xF0, 0xF0), // #F0F0F0  nav hover
                caret:       Color.FromRgb(0x1E, 0x1E, 0x1E)
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

        // ─── Search index (built lazily by Add* helpers) ─────────────────────
        /// <summary>One entry per searchable setting across all pages.</summary>
        private sealed class SearchEntry
        {
            public string Label { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string PageKey { get; set; } = string.Empty;
            public string PageDisplay { get; set; } = string.Empty;
            public string Kind { get; set; } = string.Empty; // "Toggle", "Slider", "Dropdown", "Text", "Info"
            public FrameworkElement? Row { get; set; }       // The row Border to scroll/flash
            public string Haystack { get; set; } = string.Empty; // lowercased combined text for matching
        }

        private readonly List<SearchEntry> _searchIndex = new();
        private string _currentPageKey = string.Empty;
        private string _currentPageDisplay = string.Empty;
        private TextBox? _searchBox;
        private Popup? _searchResultsPopup;
        private ListBox? _searchResultsList;

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
        private ListBox? _lstColoringRules;
        private Button? _btnAddRule;
        private Button? _btnEditRule;
        private Button? _btnRemoveRule;
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
                Title = Constants.ProductName + " Options",
                Width = 880,
                Height = 620,
                MinWidth = 720,
                MinHeight = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.CanResize,
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
                Height = 52,
                Background = _theme.Main,
                BorderBrush = _theme.Sep,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(12, 10, 12, 10)
            };

            var dock = new DockPanel { LastChildFill = false };

            // ─── Right side: Cancel, OK (primary) ───
            var btnCancel = MakeButton("Cancel", 80);
            btnCancel.Click += OnCancelClick;
            DockPanel.SetDock(btnCancel, Dock.Right);
            dock.Children.Add(btnCancel);

            var btnOk = MakePrimaryButton("OK", 80);
            btnOk.Margin = new Thickness(0, 0, 8, 0);
            btnOk.Click += OnOkClick;
            DockPanel.SetDock(btnOk, Dock.Right);
            dock.Children.Add(btnOk);

            // ─── Left side: Restore All Defaults, Import, Export ───
            var btnResetAll = MakeButton("Restore All Defaults", 140);
            btnResetAll.Click += OnResetAllClick;
            DockPanel.SetDock(btnResetAll, Dock.Left);
            dock.Children.Add(btnResetAll);

            var btnImport = MakeButton("Import…", 90);
            btnImport.Margin = new Thickness(8, 0, 0, 0);
            btnImport.Click += OnImportProfileClick;
            DockPanel.SetDock(btnImport, Dock.Left);
            dock.Children.Add(btnImport);

            var btnExport = MakeButton("Export…", 90);
            btnExport.Margin = new Thickness(8, 0, 0, 0);
            btnExport.Click += OnExportProfileClick;
            DockPanel.SetDock(btnExport, Dock.Left);
            dock.Children.Add(btnExport);

            bar.Child = dock;
            return bar;
        }

        // ─── Left sidebar ────────────────────────────────────────────────────

        private Border CreateSidebar()
        {
            var sidebar = new Border
            {
                Width = 220,
                Background = _theme.Sidebar,
                BorderBrush = _theme.Sep,
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(0, 12, 0, 0)
            };

            var panel = new StackPanel();

            // Title label — SQL Prompt style ("AKML SQL Options")
            var title = new TextBlock
            {
                Text = Constants.ProductName + " Options",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = _theme.FgPrimary,
                Margin = new Thickness(16, 0, 16, 14)
            };
            panel.Children.Add(title);

            // Title underline
            panel.Children.Add(new Border
            {
                Height = 1,
                Background = _theme.Sep,
                Margin = new Thickness(12, 0, 12, 10)
            });

            // ── Search box (Visual Studio Options-style, but better) ──
            panel.Children.Add(BuildSearchBox());

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

            // ── SQL Prompt-style hierarchical tree ──
            // Source: doc/SQL-PROMPT/SQL-Prompt-Option/SQL_Prompt_Options_Dialog.md §1.2
            // Parent nodes have no Tag (not selectable as a page); leaves carry the page key.

            AddTreeGroup("Suggestions", expanded: true,
                ("Behavior", "IntelliSense"),
                ("Database", "Schema Cache"));

            AddTreeGroup("Inserted Code", expanded: false,
                ("Refactoring", "Refactoring"));

            AddTreeGroup("Format", expanded: false,
                ("Styles", "Formatting"));

            AddTreeGroup("Queries", expanded: false,
                ("History", "History"),
                ("Execution Warnings", "Safety"),
                ("Query Results", "Grid"),
                ("Execution", "Execution"));

            AddTreeGroup("Tabs", expanded: false,
                ("Color", "Tabs & UI"));

            AddTreeLeaf("Code Analysis", "Code Analysis");
            AddTreeLeaf("Snippets", "Snippets");
            AddTreeLeaf("Prompt AI", "AI Assistance");

            AddTreeGroup("Editor", expanded: false,
                ("Productivity", "Editor"),
                ("Navigation", "Navigation"));

            AddTreeLeaf("Miscellaneous", "General");

            _navTree.SelectedItemChanged += OnNavSelectionChanged;

            panel.Children.Add(_navTree);
            sidebar.Child = panel;
            return sidebar;
        }

        /// <summary>
        /// Adds a non-selectable parent group with one or more leaf children.
        /// Each leaf is a tuple of (display label, page key in <see cref="_pages"/>).
        /// </summary>
        private void AddTreeGroup(string header, bool expanded, params (string Label, string PageKey)[] children)
        {
            var parent = new TreeViewItem
            {
                Header = header,
                Tag = null, // null = not a page, just a group
                IsExpanded = expanded,
                FontWeight = FontWeights.SemiBold
            };

            foreach (var (label, pageKey) in children)
            {
                parent.Items.Add(new TreeViewItem
                {
                    Header = label,
                    Tag = pageKey,
                    FontWeight = FontWeights.Normal
                });
            }

            _navTree!.Items.Add(parent);
        }

        /// <summary>
        /// Adds a top-level leaf (no children, directly selectable).
        /// </summary>
        private void AddTreeLeaf(string header, string pageKey)
        {
            _navTree!.Items.Add(new TreeViewItem
            {
                Header = header,
                Tag = pageKey,
                FontWeight = FontWeights.SemiBold
            });
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Search box & results popup
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Builds the SQL Prompt-style search box at the top of the sidebar.
        /// Includes a magnifying-glass icon, placeholder, and clear button.
        /// </summary>
        private Border BuildSearchBox()
        {
            var container = new Border
            {
                Background = _theme.Input,
                BorderBrush = _theme.ComboBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(12, 0, 12, 12),
                Padding = new Thickness(0)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Magnifying glass icon
            var icon = new TextBlock
            {
                Text = "\uD83D\uDD0D", // 🔍
                FontSize = 11,
                Foreground = _theme.FgSecondary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 6, 0)
            };
            Grid.SetColumn(icon, 0);
            grid.Children.Add(icon);

            // Text input
            _searchBox = new TextBox
            {
                Background = _theme.Transparent,
                Foreground = _theme.FgPrimary,
                CaretBrush = _theme.Caret,
                BorderThickness = new Thickness(0),
                FontSize = 12,
                Height = 26,
                Padding = new Thickness(0, 4, 0, 4),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_searchBox, 1);
            grid.Children.Add(_searchBox);

            // Placeholder overlay (shows when text is empty)
            var placeholder = new TextBlock
            {
                Text = "Search options... (Ctrl+E)",
                Foreground = _theme.FgSecondary,
                FontSize = 12,
                FontStyle = FontStyles.Italic,
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 0, 0)
            };
            Grid.SetColumn(placeholder, 1);
            grid.Children.Add(placeholder);

            // Clear button (visible only when text present)
            var clearBtn = new TextBlock
            {
                Text = "\u2715", // ✕
                FontSize = 12,
                Foreground = _theme.FgSecondary,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 8, 0),
                Visibility = Visibility.Collapsed
            };
            Grid.SetColumn(clearBtn, 2);
            grid.Children.Add(clearBtn);

            // Wire up events
            _searchBox.TextChanged += (s, e) =>
            {
                placeholder.Visibility = string.IsNullOrEmpty(_searchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
                clearBtn.Visibility = string.IsNullOrEmpty(_searchBox.Text) ? Visibility.Collapsed : Visibility.Visible;
                OnSearchTextChanged(_searchBox.Text);
            };

            _searchBox.PreviewKeyDown += OnSearchBoxKeyDown;
            _searchBox.GotFocus += (s, e) => container.BorderBrush = _theme.FgAccent;
            _searchBox.LostFocus += (s, e) =>
            {
                container.BorderBrush = _theme.ComboBorder;
                // Delay close so click on result registers
                _searchBox.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_searchResultsPopup != null && _searchResultsList?.IsKeyboardFocusWithin != true)
                    {
                        _searchResultsPopup.IsOpen = false;
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            };

            clearBtn.MouseLeftButtonUp += (s, e) =>
            {
                _searchBox.Text = string.Empty;
                _searchBox.Focus();
            };

            container.Child = grid;

            // Build the results popup (separate WPF Popup positioned next to the search box)
            BuildSearchResultsPopup(container);

            return container;
        }

        /// <summary>
        /// Builds the WPF Popup that holds the search results list. Positioned to the right
        /// of the sidebar so it doesn't squash the tree.
        /// </summary>
        private void BuildSearchResultsPopup(Border anchor)
        {
            _searchResultsList = new ListBox
            {
                Background = _theme.Panel,
                Foreground = _theme.FgPrimary,
                BorderThickness = new Thickness(0),
                MaxHeight = 420,
                MinWidth = 420,
                MaxWidth = 520,
                FontSize = 12,
                Focusable = true
            };

            // Themed item container — flat rows with hover/selected highlight
            var itemStyle = new Style(typeof(ListBoxItem));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, _theme.Transparent));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, _theme.FgPrimary));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 7, 10, 7)));
            itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            itemStyle.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
            itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, _theme.TreeHover));
            itemStyle.Triggers.Add(hoverTrigger);
            var selTrigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selTrigger.Setters.Add(new Setter(Control.BackgroundProperty, _theme.Selected));
            selTrigger.Setters.Add(new Setter(Control.ForegroundProperty, _theme.SelectedText));
            selTrigger.Setters.Add(new Setter(TextElement.ForegroundProperty, _theme.SelectedText));
            itemStyle.Triggers.Add(selTrigger);
            _searchResultsList.ItemContainerStyle = itemStyle;

            _searchResultsList.MouseLeftButtonUp += (s, e) => CommitSelectedSearchResult();
            _searchResultsList.PreviewKeyDown += OnSearchResultsKeyDown;

            var border = new Border
            {
                Background = _theme.Panel,
                BorderBrush = _theme.FgAccent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 16,
                    ShadowDepth = 4,
                    Opacity = 0.45,
                    Color = Colors.Black
                },
                Child = _searchResultsList
            };

            _searchResultsPopup = new Popup
            {
                Child = border,
                PlacementTarget = anchor,
                Placement = PlacementMode.Right,
                HorizontalOffset = 8,
                VerticalOffset = -2,
                AllowsTransparency = true,
                StaysOpen = false,
                Focusable = false,
                IsOpen = false
            };
        }

        /// <summary>
        /// Filters the search index against the current query and refreshes the results popup.
        /// </summary>
        private void OnSearchTextChanged(string query)
        {
            if (_searchResultsList == null || _searchResultsPopup == null) return;

            query = (query ?? string.Empty).Trim().ToLowerInvariant();
            _searchResultsList.Items.Clear();

            if (query.Length == 0)
            {
                _searchResultsPopup.IsOpen = false;
                return;
            }

            // Score: label-prefix=100, label-substring=60, description=30, page=10
            var matches = new List<(SearchEntry Entry, int Score)>(_searchIndex.Count);
            foreach (var entry in _searchIndex)
            {
                int score = 0;
                var lowerLabel = entry.Label.ToLowerInvariant();
                if (lowerLabel.StartsWith(query, StringComparison.Ordinal)) score += 100;
                else if (lowerLabel.Contains(query, StringComparison.Ordinal)) score += 60;
                if (entry.Description.ToLowerInvariant().Contains(query, StringComparison.Ordinal)) score += 30;
                if (entry.PageDisplay.ToLowerInvariant().Contains(query, StringComparison.Ordinal)) score += 10;
                if (score > 0) matches.Add((entry, score));
            }

            if (matches.Count == 0)
            {
                _searchResultsList.Items.Add(BuildNoResultsItem());
                _searchResultsPopup.IsOpen = true;
                return;
            }

            foreach (var (entry, _) in matches
                         .OrderByDescending(m => m.Score)
                         .ThenBy(m => m.Entry.Label, StringComparer.OrdinalIgnoreCase)
                         .Take(20))
            {
                _searchResultsList.Items.Add(BuildResultItem(entry));
            }

            _searchResultsList.SelectedIndex = 0;
            _searchResultsPopup.IsOpen = true;
        }

        private UIElement BuildResultItem(SearchEntry entry)
        {
            var grid = new Grid { Tag = entry };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Type badge — colored letter that matches setting kind
            var (letter, color) = entry.Kind switch
            {
                "Toggle"   => ("T", Color.FromRgb(0x00, 0x78, 0xD4)),
                "Slider"   => ("S", Color.FromRgb(0xE0, 0x83, 0x00)),
                "Dropdown" => ("D", Color.FromRgb(0x6B, 0x46, 0xC1)),
                "Text"     => ("X", Color.FromRgb(0x16, 0xA3, 0x4A)),
                _           => ("i", Color.FromRgb(0x88, 0x92, 0xA8)),
            };
            var badgeBrush = new SolidColorBrush(color);
            badgeBrush.Freeze();
            var badge = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(3),
                Background = badgeBrush,
                Margin = new Thickness(0, 1, 10, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = letter,
                    Foreground = Brushes.White,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(badge, 0);
            grid.Children.Add(badge);

            // Two-line text: label (bold) + page breadcrumb + description snippet
            var textPanel = new StackPanel();
            textPanel.Children.Add(new TextBlock
            {
                Text = entry.Label,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = _theme.FgPrimary,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            textPanel.Children.Add(new TextBlock
            {
                Text = entry.PageDisplay,
                FontSize = 10,
                Foreground = _theme.FgAccent,
                Margin = new Thickness(0, 1, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            if (!string.IsNullOrEmpty(entry.Description))
            {
                textPanel.Children.Add(new TextBlock
                {
                    Text = entry.Description.Length > 100
                        ? entry.Description.Substring(0, 100) + "…"
                        : entry.Description,
                    FontSize = 11,
                    Foreground = _theme.FgSecondary,
                    Margin = new Thickness(0, 2, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                    MaxHeight = 32,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }
            Grid.SetColumn(textPanel, 1);
            grid.Children.Add(textPanel);

            return grid;
        }

        private UIElement BuildNoResultsItem()
        {
            return new TextBlock
            {
                Text = "No matching settings",
                FontSize = 12,
                FontStyle = FontStyles.Italic,
                Foreground = _theme.FgSecondary,
                Padding = new Thickness(12, 12, 12, 12),
                HorizontalAlignment = HorizontalAlignment.Center
            };
        }

        /// <summary>
        /// Keyboard shortcuts inside the search textbox: Down jumps into results,
        /// Enter commits the selected result, Escape clears.
        /// </summary>
        private void OnSearchBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (_searchResultsPopup == null || _searchResultsList == null) return;

            if (e.Key == Key.Down && _searchResultsPopup.IsOpen)
            {
                _searchResultsList.Focus();
                if (_searchResultsList.Items.Count > 0)
                {
                    _searchResultsList.SelectedIndex = 0;
                    if (_searchResultsList.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem first)
                        first.Focus();
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && _searchResultsPopup.IsOpen)
            {
                CommitSelectedSearchResult();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (_searchBox != null && _searchBox.Text.Length > 0)
                {
                    _searchBox.Text = string.Empty;
                }
                else
                {
                    _searchResultsPopup.IsOpen = false;
                }
                e.Handled = true;
            }
        }

        private void OnSearchResultsKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitSelectedSearchResult();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                _searchResultsPopup!.IsOpen = false;
                _searchBox?.Focus();
                e.Handled = true;
            }
        }

        /// <summary>
        /// Navigates to the page containing the selected search result, scrolls the
        /// matching row into view, and flashes its background to draw attention.
        /// </summary>
        private void CommitSelectedSearchResult()
        {
            if (_searchResultsList?.SelectedItem is not Grid grid || grid.Tag is not SearchEntry entry)
                return;

            // 1. Navigate to the target page by selecting its tree leaf.
            SelectTreeLeafByPageKey(entry.PageKey);

            // 2. Close the popup.
            if (_searchResultsPopup != null) _searchResultsPopup.IsOpen = false;

            // 3. Scroll the target row into view + flash highlight.
            //    Defer to the dispatcher so the page swap completes first.
            var row = entry.Row;
            if (row != null)
            {
                row.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        row.BringIntoView();
                        FlashRow(row);
                    }
                    catch { /* non-fatal */ }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        /// <summary>
        /// Walks the tree (including parent groups) to find the leaf with the given
        /// page key Tag, expands its parent if needed, and selects it.
        /// </summary>
        private void SelectTreeLeafByPageKey(string pageKey)
        {
            if (_navTree == null) return;

            foreach (var obj in _navTree.Items)
            {
                if (obj is not TreeViewItem item) continue;

                if (item.Tag is string topKey && topKey == pageKey)
                {
                    item.IsSelected = true;
                    return;
                }

                foreach (var childObj in item.Items)
                {
                    if (childObj is not TreeViewItem child) continue;
                    if (child.Tag is string childKey && childKey == pageKey)
                    {
                        item.IsExpanded = true;
                        child.IsSelected = true;
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Briefly flashes the row background with the accent color so the user
        /// can spot the setting they jumped to.
        /// </summary>
        private void FlashRow(FrameworkElement row)
        {
            if (row is not Border border) return;

            var originalBrush = border.Background;
            var flashBrush = new SolidColorBrush(((SolidColorBrush)_theme.Selected).Color);
            border.Background = flashBrush;

            var animation = new System.Windows.Media.Animation.ColorAnimation
            {
                From = ((SolidColorBrush)_theme.Selected).Color,
                To = (originalBrush is SolidColorBrush sb) ? sb.Color : Colors.Transparent,
                Duration = TimeSpan.FromMilliseconds(900),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                }
            };
            animation.Completed += (s, e) => border.Background = originalBrush;
            flashBrush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
        }

        // ─── Page building ───────────────────────────────────────────────────

        private void BuildPages()
        {
            // Mapping: page key (used for navigation tag) → SQL Prompt-style display label
            var pages = new (string Key, string Display, Func<UIElement> Builder)[]
            {
                ("General",       "Miscellaneous",       BuildGeneralPage),
                ("IntelliSense",  "Suggestions › Behavior", BuildIntelliSensePage),
                ("Schema Cache",  "Suggestions › Database", BuildSchemaCachePage),
                ("Formatting",    "Format › Styles",     BuildFormattingPage),
                ("Snippets",      "Snippets",            BuildSnippetsPage),
                ("Code Analysis", "Code Analysis",       BuildCodeAnalysisPage),
                ("Refactoring",   "Inserted Code › Refactoring", BuildRefactoringPage),
                ("History",       "Queries › History",   BuildHistoryPage),
                ("Tabs & UI",     "Tabs › Color",        BuildTabsPage),
                ("Safety",        "Queries › Execution Warnings", BuildSafetyPage),
                ("AI Assistance", "Prompt AI",           BuildAiPage),
                ("Grid",          "Queries › Query Results", BuildGridPage),
                ("Editor",        "Editor › Productivity", BuildEditorPage),
                ("Execution",     "Queries › Execution", BuildExecutionPage),
                ("Navigation",    "Editor › Navigation", BuildNavigationPage),
            };

            foreach (var (key, display, builder) in pages)
            {
                _currentPageKey = key;
                _currentPageDisplay = display;
                _pages[key] = builder();
            }

            _currentPageKey = string.Empty;
            _currentPageDisplay = string.Empty;
        }

        /// <summary>
        /// Records a setting in the search index. Called by Add* helpers.
        /// </summary>
        private void RegisterSearchEntry(string label, string description, string kind, FrameworkElement row)
        {
            if (string.IsNullOrEmpty(_currentPageKey)) return;
            _searchIndex.Add(new SearchEntry
            {
                Label = label,
                Description = description ?? string.Empty,
                PageKey = _currentPageKey,
                PageDisplay = _currentPageDisplay,
                Kind = kind,
                Row = row,
                Haystack = ((label ?? "") + " " + (description ?? "") + " " + _currentPageDisplay)
                    .ToLowerInvariant()
            });
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
            AddInfoRow(panel, "Version", Constants.RuntimeVersion + " (" + Constants.BuildDate + ")");

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
            _chkJoinAssist = AddToggle(panel, "JOIN clause assistance",
                "Master switch for FK-assisted JOIN completion. When on: after typing 'JOIN', FK-related tables are suggested first with a full ON clause inserted; inside 'ON', ready-made FK equality predicates are suggested. Orthogonal to Tables Alias. Default: on.");
            _chkAutoAlias = AddToggle(panel, "Tables Alias",
                "When on, completion generates new aliases for inserted tables (e.g. 'Orders o ON o.CustomerId = c.Id'). When off, FK JOIN suggestions still fire but the target table is referenced by its bare name ('Orders ON Orders.CustomerId = c.Id'). Default: off.");
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

            // Environment Rules editor
            var rulesLabel = new TextBlock
            {
                Text = "Environment Rules",
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Margin = new Thickness(20, 16, 20, 4),
            };
            panel.Children.Add(rulesLabel);

            var rulesDesc = new TextBlock
            {
                Text = "Define server name patterns to match environments. Rules are evaluated top-down; first match wins.",
                FontSize = 12,
                Opacity = 0.7,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20, 0, 20, 8),
            };
            panel.Children.Add(rulesDesc);

            _lstColoringRules = new ListBox
            {
                Height = 120,
                Margin = new Thickness(20, 4, 20, 4),
                BorderThickness = new Thickness(1),
                FontSize = 13,
            };
            panel.Children.Add(_lstColoringRules);

            var buttonRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(20, 4, 20, 4),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            _btnAddRule = new Button { Content = "Add...", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
            _btnEditRule = new Button { Content = "Edit...", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
            _btnRemoveRule = new Button { Content = "Remove", Padding = new Thickness(12, 4, 12, 4) };

            _btnAddRule.Click += (s, e) => OnAddColoringRule();
            _btnEditRule.Click += (s, e) => OnEditColoringRule();
            _btnRemoveRule.Click += (s, e) => OnRemoveColoringRule();

            buttonRow.Children.Add(_btnAddRule);
            buttonRow.Children.Add(_btnEditRule);
            buttonRow.Children.Add(_btnRemoveRule);
            panel.Children.Add(buttonRow);

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

        // Tracks the alternating zebra-stripe state per page (reset on each new panel).
        private int _zebraIndex;

        private StackPanel CreatePagePanel()
        {
            _zebraIndex = 0;
            return new StackPanel
            {
                Margin = new Thickness(24, 18, 24, 24),
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
                Background = _theme.Panel,
                Padding = new Thickness(0)
            };
        }

        /// <summary>
        /// SQL Prompt-style page header: blue accent title on the left, "Restore Defaults"
        /// link on the right, and a thin separator underline.
        /// </summary>
        private void AddPageHeader(StackPanel panel, string text)
        {
            var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock
            {
                Text = text,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = _theme.FgAccent
            };
            Grid.SetColumn(title, 0);
            header.Children.Add(title);

            var restoreLink = new TextBlock
            {
                Text = "Restore Defaults",
                FontSize = 11,
                Foreground = _theme.FgAccent,
                TextDecorations = TextDecorations.Underline,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            restoreLink.MouseLeftButtonUp += (s, e) => OnResetThisPageClick(s, new RoutedEventArgs());
            Grid.SetColumn(restoreLink, 1);
            header.Children.Add(restoreLink);

            panel.Children.Add(header);

            // Underline separator
            panel.Children.Add(new Border
            {
                Height = 1,
                Background = _theme.Sep,
                Margin = new Thickness(0, 0, 0, 12)
            });
        }

        /// <summary>
        /// Section header inside a page (bold, foreground primary). SQL Prompt style.
        /// </summary>
        private void AddGroupHeader(StackPanel panel, string text)
        {
            panel.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = _theme.FgPrimary,
                Margin = new Thickness(0, 8, 0, 6)
            });
        }

        private void AddGroupSeparator(StackPanel panel)
        {
            panel.Children.Add(new Border
            {
                Height = 1,
                Background = _theme.Sep,
                Margin = new Thickness(0, 14, 0, 10)
            });
        }

        /// <summary>
        /// Wraps a setting row in a zebra-striped <see cref="Border"/>. Alternates between
        /// transparent and <see cref="ThemeBrushSet.InputReadOnly"/> for readability,
        /// matching the SQL Prompt Options dialog row style.
        /// </summary>
        private Border WrapZebraRow(UIElement content)
        {
            var bg = (_zebraIndex++ % 2 == 0) ? _theme.InputReadOnly : _theme.Transparent;
            return new Border
            {
                Background = bg,
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(-12, 0, -12, 0),
                Child = content
            };
        }

        private CheckBox AddToggle(StackPanel panel, string label, string description)
        {
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
                    Margin = new Thickness(0, 2, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
            }

            cb.Content = contentPanel;
            var row = WrapZebraRow(cb);
            panel.Children.Add(row);
            RegisterSearchEntry(label, description, "Toggle", row);
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
            RegisterSearchEntry(label, description, "Slider", container);
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
            RegisterSearchEntry(label, description, "Dropdown", container);
            return combo;
        }

        /// <summary>
        /// Applies themed styling to a ComboBox so the dropdown popup, items,
        /// hover highlight, selected item, and the toggle button all match the active theme.
        /// Uses system color overrides (reliable with the default Chrome template) plus a
        /// Loaded handler that walks the visual tree to restyle the toggle button background.
        /// </summary>
        private void StyleComboBox(ComboBox combo)
        {
            combo.Background = _theme.Input;
            combo.Foreground = _theme.FgPrimary;
            combo.BorderBrush = _theme.ComboBorder;

            // TextElement.Foreground propagates through the Chrome template's ContentPresenter
            combo.SetValue(TextElement.ForegroundProperty, _theme.FgPrimary);

            // Override system colors used by the default ComboBox Chrome template.
            combo.Resources[SystemColors.WindowBrushKey] = _theme.Input;
            combo.Resources[SystemColors.WindowTextBrushKey] = _theme.FgPrimary;
            combo.Resources[SystemColors.HighlightBrushKey] = _theme.Selected;
            combo.Resources[SystemColors.HighlightTextBrushKey] = _theme.SelectedText;
            combo.Resources[SystemColors.ControlBrushKey] = _theme.Input;
            combo.Resources[SystemColors.ControlTextBrushKey] = _theme.FgPrimary;
            combo.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = _theme.Selected;
            combo.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = _theme.SelectedText;
            // ComboBox dropdown border color
            combo.Resources[SystemColors.ActiveBorderBrushKey] = _theme.ComboBorder;
            combo.Resources[SystemColors.InactiveBorderBrushKey] = _theme.ComboBorder;

            // Walk the visual tree after layout to restyle the toggle button chrome
            var theme = _theme;
            combo.Loaded += (s, e) => ThemeComboBoxVisualTree((ComboBox)s!, theme);

            // Item container style (dropdown rows)
            var itemStyle = new Style(typeof(ComboBoxItem));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, _theme.Input));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, _theme.FgPrimary));
            itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 4, 6, 4)));
            itemStyle.Setters.Add(new Setter(TextElement.ForegroundProperty, _theme.FgPrimary));

            var hoverTrigger = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, _theme.Selected));
            hoverTrigger.Setters.Add(new Setter(Control.ForegroundProperty, _theme.SelectedText));
            hoverTrigger.Setters.Add(new Setter(TextElement.ForegroundProperty, _theme.SelectedText));
            itemStyle.Triggers.Add(hoverTrigger);

            var selectedTrigger = new Trigger { Property = ComboBoxItem.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, _theme.FgAccent));
            selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty,
                Freeze(new SolidColorBrush(Colors.White))));
            selectedTrigger.Setters.Add(new Setter(TextElement.ForegroundProperty,
                Freeze(new SolidColorBrush(Colors.White))));
            itemStyle.Triggers.Add(selectedTrigger);

            combo.ItemContainerStyle = itemStyle;
        }

        /// <summary>
        /// After the ComboBox template is applied, walk the visual tree to restyle the
        /// Chrome toggle button and dropdown border that ignore standard properties.
        /// </summary>
        private static void ThemeComboBoxVisualTree(ComboBox combo, ThemeBrushSet theme)
        {
            try
            {
                // Find the ToggleButton inside the ComboBox template
                var toggleButton = FindChild<ToggleButton>(combo);
                if (toggleButton != null)
                {
                    toggleButton.Background = theme.Input;
                    toggleButton.BorderBrush = theme.ComboBorder;
                    toggleButton.Foreground = theme.FgPrimary;
                    toggleButton.SetValue(TextElement.ForegroundProperty, theme.FgPrimary);

                    // Override system colors inside the toggle button too
                    toggleButton.Resources[SystemColors.ControlBrushKey] = theme.Input;
                    toggleButton.Resources[SystemColors.ControlTextBrushKey] = theme.FgPrimary;
                    toggleButton.Resources[SystemColors.ControlLightBrushKey] = theme.Input;
                    toggleButton.Resources[SystemColors.ControlDarkBrushKey] = theme.ComboBorder;

                    // Theme the arrow Path if present
                    var arrow = FindChild<System.Windows.Shapes.Path>(toggleButton);
                    if (arrow != null)
                    {
                        arrow.Fill = theme.FgSecondary;
                    }
                }

                // Theme the dropdown popup border
                var popup = FindChild<Popup>(combo);
                if (popup?.Child is Border popupBorder)
                {
                    popupBorder.Background = theme.Input;
                    popupBorder.BorderBrush = theme.ComboBorder;
                }
            }
            catch
            {
                // Non-fatal: worst case dropdown keeps system colors
            }
        }

        /// <summary>
        /// Walks the visual tree depth-first to find the first child of type T.
        /// </summary>
        private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T found) return found;
                var result = FindChild<T>(child);
                if (result != null) return result;
            }
            return null;
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
            RegisterSearchEntry(label, description, "Text", container);
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
            RegisterSearchEntry(label, value, "Info", container);
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
            RegisterSearchEntry(label, value, "Info", row);
        }

        private Button MakeButton(string text, double width)
        {
            var btn = new Button
            {
                Content = text,
                Width = width,
                Height = 30,
                FontSize = 12,
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

        /// <summary>
        /// Primary action button — solid blue accent (SQL Prompt style for OK).
        /// </summary>
        private Button MakePrimaryButton(string text, double width)
        {
            var btn = new Button
            {
                Content = text,
                Width = width,
                Height = 30,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = _theme.SelectedText,
                Background = _theme.Selected,
                BorderBrush = _theme.Selected,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 4, 12, 4),
                Cursor = Cursors.Hand
            };

            var theme = _theme;
            // Subtle hover: slightly lighter accent
            var hoverBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x10, 0x88, 0xE4)));
            btn.MouseEnter += (s, e) => { btn.Background = hoverBrush; btn.BorderBrush = hoverBrush; };
            btn.MouseLeave += (s, e) => { btn.Background = theme.Selected; btn.BorderBrush = theme.Selected; };

            return btn;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Navigation
        // ═══════════════════════════════════════════════════════════════════════

        private void OnNavSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_navTree?.SelectedItem is not TreeViewItem item)
            {
                return;
            }

            // Parent group node clicked: expand it and select the first child instead.
            if (item.Tag is null)
            {
                item.IsExpanded = true;
                if (item.Items.Count > 0 && item.Items[0] is TreeViewItem firstChild)
                {
                    firstChild.IsSelected = true;
                }
                return;
            }

            // Leaf node: load its page into the content host.
            if (item.Tag is string pageKey
                && _pages.TryGetValue(pageKey, out var page)
                && _contentHost != null)
            {
                _contentHost.Content = page;
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

                // FR-042: Live re-render tab colors after settings change
                try { Tabs.TabColoringManager.RepaintAllTabs(); } catch { }
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
            // Ctrl+E or Ctrl+F → focus search box (VS Options shortcut convention)
            if ((e.Key == Key.E || e.Key == Key.F)
                && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                _searchBox?.Focus();
                _searchBox?.SelectAll();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                // If the search popup is open, let its own handler clear it instead.
                if (_searchResultsPopup?.IsOpen == true) return;
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
            PopulateColoringRulesList();
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

        // ═══════════════════════════════════════════════════════════════════════
        //  Coloring Rules CRUD
        // ═══════════════════════════════════════════════════════════════════════

        private void PopulateColoringRulesList()
        {
            if (_lstColoringRules == null) return;
            _lstColoringRules.Items.Clear();

            foreach (var rule in _settings.Tabs.ColoringRules)
            {
                _lstColoringRules.Items.Add($"[{rule.Label}]  {rule.Pattern}  \u2192  {rule.Color}");
            }
        }

        private void OnAddColoringRule()
        {
            var rule = new AkmlSql.Core.Config.ColoringRule
            {
                Order = _settings.Tabs.ColoringRules.Count,
                MatchTarget = AkmlSql.Core.Models.Tabs.EnvironmentMatcher.MatchTargetServerName
            };

            if (ShowRuleEditor(rule, "Add Environment Rule"))
            {
                _settings.Tabs.ColoringRules.Add(rule);
                PopulateColoringRulesList();
            }
        }

        private void OnEditColoringRule()
        {
            var index = _lstColoringRules?.SelectedIndex ?? -1;
            if (index < 0 || index >= _settings.Tabs.ColoringRules.Count) return;

            var rule = _settings.Tabs.ColoringRules[index];
            if (ShowRuleEditor(rule, "Edit Environment Rule"))
            {
                PopulateColoringRulesList();
                _lstColoringRules!.SelectedIndex = index;
            }
        }

        private void OnRemoveColoringRule()
        {
            var index = _lstColoringRules?.SelectedIndex ?? -1;
            if (index < 0 || index >= _settings.Tabs.ColoringRules.Count) return;

            _settings.Tabs.ColoringRules.RemoveAt(index);
            PopulateColoringRulesList();
        }

        private bool ShowRuleEditor(AkmlSql.Core.Config.ColoringRule rule, string title)
        {
            var dlg = new System.Windows.Window
            {
                Title = title,
                Width = 420,
                Height = 260,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
            };

            // Try to set owner to the SettingsWindow's dialog
            try { dlg.Owner = _window; } catch { }

            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Label
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Pattern
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Color
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // spacer
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // buttons
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Row 0: Label
            var lblLabel = new TextBlock { Text = "Label:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };
            Grid.SetRow(lblLabel, 0); Grid.SetColumn(lblLabel, 0);
            var txtLabel = new TextBox { Text = rule.Label, Margin = new Thickness(0, 4, 0, 4) };
            Grid.SetRow(txtLabel, 0); Grid.SetColumn(txtLabel, 1);

            // Row 1: Pattern
            var lblPattern = new TextBlock { Text = "Pattern:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };
            Grid.SetRow(lblPattern, 1); Grid.SetColumn(lblPattern, 0);
            var txtPattern = new TextBox { Text = rule.Pattern, Margin = new Thickness(0, 4, 0, 4) };
            Grid.SetRow(txtPattern, 1); Grid.SetColumn(txtPattern, 1);

            // Row 2: Color
            var lblColor = new TextBlock { Text = "Color:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };
            Grid.SetRow(lblColor, 2); Grid.SetColumn(lblColor, 0);
            var colorPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            var txtColor = new TextBox { Text = rule.Color, Width = 100 };
            var colorPreview = new Border
            {
                Width = 24, Height = 24, Margin = new Thickness(8, 0, 0, 0),
                CornerRadius = new CornerRadius(2),
                BorderThickness = new Thickness(1)
            };

            // Live color preview
            Action updatePreview = () =>
            {
                try
                {
                    var hex = txtColor.Text?.Trim() ?? "";
                    if (!hex.StartsWith("#")) hex = "#" + hex;
                    var color = (Color)ColorConverter.ConvertFromString(hex);
                    var brush = new SolidColorBrush(color);
                    brush.Freeze();
                    colorPreview.Background = brush;
                }
                catch { colorPreview.Background = null; }
            };
            updatePreview();
            txtColor.TextChanged += (s, e) => updatePreview();

            colorPanel.Children.Add(txtColor);
            colorPanel.Children.Add(colorPreview);
            Grid.SetRow(colorPanel, 2); Grid.SetColumn(colorPanel, 1);

            // Row 4: Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            var btnOk = new Button { Content = "OK", Width = 75, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var btnCancel = new Button { Content = "Cancel", Width = 75, IsCancel = true };

            bool accepted = false;
            btnOk.Click += (s, e) =>
            {
                rule.Label = txtLabel.Text.Trim();
                rule.Pattern = txtPattern.Text.Trim();
                rule.Color = txtColor.Text.Trim();
                accepted = true;
                dlg.Close();
            };

            buttonPanel.Children.Add(btnOk);
            buttonPanel.Children.Add(btnCancel);
            Grid.SetRow(buttonPanel, 4); Grid.SetColumn(buttonPanel, 0);
            Grid.SetColumnSpan(buttonPanel, 2);

            grid.Children.Add(lblLabel); grid.Children.Add(txtLabel);
            grid.Children.Add(lblPattern); grid.Children.Add(txtPattern);
            grid.Children.Add(lblColor); grid.Children.Add(colorPanel);
            grid.Children.Add(buttonPanel);

            dlg.Content = grid;
            dlg.ShowDialog();

            return accepted;
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
