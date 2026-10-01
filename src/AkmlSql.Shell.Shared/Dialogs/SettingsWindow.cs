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
using AkmlSql.Shell.Shared.Dialogs.Pages;
using AkmlSql.Shell.Shared.Ui.Theme;
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
    internal sealed class SettingsWindow : Commands.IOptionsDialog
    {
        // ─── Theme brush set ────────────────────────────────────────────────
        // PageTheme was lifted to Pages/PageTheme.cs (Phase 2 B.1) so per-page
        // builders can consume it without depending on SettingsWindow internals.

        private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

        // ─── Active theme ───────────────────────────────────────────────────
        private readonly PageTheme _theme;

        // ─── State ───────────────────────────────────────────────────────────
        private Window? _window;
        private AppSettings _settings;
        private ContentControl? _contentHost;
        private TreeView? _navTree;
        private readonly Dictionary<string, UIElement> _pages = new();

        // ─── Page-split builders (Phase 2 B.2+) ──────────────────────────────
        // Pages migrated to per-file IPageBuilder implementations. Keys not present
        // here fall back to the legacy inline Build*Page method via the BuildPages
        // dispatch loop. Cleanup of the legacy methods happens in B.17 once all
        // 15 pages have moved.
        private readonly Dictionary<string, IPageBuilder> _pageBuilders = new()
        {
            ["Snippets"] = new SnippetsPage(),
            ["Code Analysis"] = new CodeAnalysisPage(),
            ["Refactoring"] = new RefactoringPage(),
            ["Navigation"] = new NavigationPage(),
            ["Grid"] = new GridPage(),
            ["General"] = new GeneralPage(),
            ["Safety"] = new SafetyPage(),
            ["Execution"] = new ExecutionPage(),
            ["Editor"] = new EditorPage(),
            ["History"] = new HistoryPage(),
            ["AI Assistance"] = new AiAssistancePage(),
            ["Formatting"] = new FormattingPage(),
            ["Tabs & UI"] = new TabsPage(),
            ["IntelliSense"] = new IntelliSensePage(),
            ["SuggestionTypes"] = new SuggestionTypesPage(),
            ["CompletionPolish"] = new CompletionPolishPage(),
            ["Aliases"] = new AliasesPage(),
            ["ConnectionScope"] = new ConnectionScopePage(),
            ["ConnectionsMemory"] = new ConnectionsMemoryPage(),
            ["Qualification"] = new QualificationPage(),
            ["SpecialCharacters"] = new SpecialCharactersPage(),
            ["InsertOptions"] = new InsertStatementsPage(),
            ["JoinOptions"] = new JoinCompletionPage(),
        };
        private readonly Dictionary<string, IPageControls> _pageControlsByKey = new();

        // Track whether user confirmed via OK
        private bool _dialogResult;

        // Spec 040 (OPT-02): true while controls are being filled from settings, so the Theme
        // drop-down's SelectionChanged (raised by Load, Reset or Import) is not mistaken for a pick.
        private bool _loadingControls;

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

        /// <summary>
        /// Spec 037 (US1, FR-017): deep-link target for the AI Assistance page. Set before
        /// <see cref="ShowDialog(string?)"/>: an agent id selects that agent; <c>""</c> performs
        /// the page's implicit Add when its agent list is empty; <c>null</c> (the default) is
        /// the ordinary open with no agent pre-selection.
        /// </summary>
        public string? InitialAgentId { get; set; }

        /// <summary>
        /// Spec 040 (OPT-07, FR-053, T163): the label of an option to scroll to, flash and focus
        /// once the window has loaded — set by the Command Palette for options it can't toggle in
        /// place. <c>null</c> (the default) opens the page as usual.
        /// </summary>
        public string? InitialFocusLabel { get; set; }

        /// <summary>The row <see cref="ApplyInitialFocus"/> jumped to (test seam).</summary>
        internal FrameworkElement? FocusedRow { get; private set; }

        /// <summary>
        /// Test seam (spec 037 review): when set, the OK/Apply agent-validation refusal is
        /// reported through this action instead of a modal <see cref="MessageBox"/>, so the
        /// refusal path is exercisable without a pump-blocking dialog. Production code never
        /// sets it.
        /// </summary>
        internal Action<string>? ValidationRefusalReporter { get; set; }

        // ─── Control references (for Load / Save) ───────────────────────────

        // General
        // General controls migrated to Pages/GeneralPage.cs (Phase 2 B.7).

        // IntelliSense
        // IntelliSense controls migrated to Pages/IntelliSensePage.cs (Phase 2 B.16).

        // Formatting
        // Formatting controls migrated to Pages/FormattingPage.cs (Phase 2 B.14).

        // Snippets
        // Snippets controls migrated to Pages/SnippetsPage.cs (Phase 2 B.2);
        // owned by the SnippetsControls record stored in _pageControlsByKey["Snippets"].

        // Code Analysis
        // Code Analysis controls migrated to Pages/CodeAnalysisPage.cs (Phase 2 B.3).

        // Refactoring
        // Refactoring controls migrated to Pages/RefactoringPage.cs (Phase 2 B.4).

        // History
        // History controls migrated to Pages/HistoryPage.cs (Phase 2 B.12).

        // Tabs
        // Tabs & UI controls migrated to Pages/TabsPage.cs (Phase 2 B.15).

        // Safety
        // Safety controls migrated to Pages/SafetyPage.cs (Phase 2 B.8).

        // AI
        // AI Assistance controls migrated to Pages/AiAssistancePage.cs (Phase 2 B.13).

        // Grid
        // Grid controls migrated to Pages/GridPage.cs (Phase 2 B.6).

        // Editor Productivity
        // Editor controls migrated to Pages/EditorPage.cs (Phase 2 B.10).

        // Execution
        // Execution controls migrated to Pages/ExecutionPage.cs (Phase 2 B.9).

        // Navigation
        // Navigation controls migrated to Pages/NavigationPage.cs (Phase 2 B.5).

        // ─── Public API ──────────────────────────────────────────────────────

        public SettingsWindow(AppSettings settings)
        {
            _settings = settings;
            _theme = ResolvePageTheme(settings.Theme);
        }

        /// <summary>
        /// Spec 040 (OPT-02) — the window's brush set for a theme preference: "dark" → Dark,
        /// "system" → the host's current theme, anything else → Light. Spec 040 (OPT-09, T169):
        /// under Windows high contrast (or a high-contrast host) the window is always
        /// <see cref="PageTheme.HighContrast"/>, whatever the saved preference — fixed Light or Dark
        /// brushes would paint over the colours the user chose for legibility.
        /// </summary>
        internal static PageTheme ResolvePageTheme(string? preference)
        {
            if (Ui.Theme.HostThemeWatcher.CurrentHighContrast
                || Ui.Theme.HostThemeWatcher.CurrentHostVariant == Ui.Theme.ThemeVariant.HighContrast)
                return PageTheme.HighContrast;

            switch ((preference ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "dark":
                    return PageTheme.Dark;
                case "system":
                    return Ui.Theme.HostThemeWatcher.CurrentHostVariant == Ui.Theme.ThemeVariant.Dark
                        ? PageTheme.Dark
                        : PageTheme.Light;
                default:
                    return PageTheme.Light;
            }
        }

        /// <summary>
        /// Spec 040 (OPT-02) — the settings as edited so far, including unsaved edits on every page.
        /// After a theme change the reopened window starts from this, so nothing is lost and nothing
        /// is written to disk before OK.
        /// </summary>
        public AppSettings WorkingCopy => _settings;

        /// <summary>The page key of the selected tree leaf, so a reopened window lands on the same page.</summary>
        public string? CurrentPageKey => (_navTree?.SelectedItem as TreeViewItem)?.Tag as string;

        /// <summary>
        /// Spec 040 (X-03, FR-062): what F1 opens — the selected page's
        /// <see cref="IPageBuilder.HelpTopic"/>, or the Options topic when no page is selected.
        /// </summary>
        internal string? CurrentHelpTopic =>
            CurrentPageKey is string key && _pageBuilders.TryGetValue(key, out var page)
                ? page.HelpTopic
                : global::AkmlSql.Shell.Shared.Help.F1HelpRegistrations.OptionsTopic;

        /// <summary>
        /// Shows the settings window as a modal dialog.
        /// Returns true if the user clicked OK/Apply, false if Cancel.
        /// </summary>
        public bool ShowDialog()
        {
            BuildWindowInner();
            _window!.ShowDialog();
            return _dialogResult;
        }

        /// <summary>
        /// Spec 037 (US1, FR-017): as <see cref="ShowDialog()"/>, but pre-selects the page whose
        /// key is <paramref name="initialPageKey"/> (routing through the same
        /// <see cref="SelectTreeLeafByPageKey"/> the settings search box drives) instead of the
        /// first nav item. A null or unknown key degrades to the ordinary first-item selection.
        /// </summary>
        public bool ShowDialog(string? initialPageKey)
        {
            BuildWindowInner(initialPageKey);
            _window!.ShowDialog();
            return _dialogResult;
        }

        /// <summary>
        /// Test-only: build the dialog's visual tree without showing it. Used by
        /// AkmlSql.Shell.Shared.Tests for chrome regression checks. Must NOT be
        /// called from production code paths — this method exists solely to expose
        /// the rendering seam to the test project.
        /// No items are pre-selected so every TreeViewItem reflects its base (non-selected) style.
        /// </summary>
        public Window TestBuildWindowForRenderTest()
        {
            _window = CreateWindow();
            LoadSettingsToControls();
            // Intentionally do NOT select the first item — we want base (non-selected) style
            // applied to all items so the chrome test can assert the unselected foreground.
            _window.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            _window.Arrange(new Rect(0, 0, _window.DesiredSize.Width, _window.DesiredSize.Height));
            _window.UpdateLayout();
            return _window!;
        }

        /// <summary>
        /// Test-only deep-link seam (spec 037 T019): as <see cref="TestBuildWindowForRenderTest()"/>,
        /// but performs the same pre-selection <see cref="ShowDialog(string?)"/> performs, so the
        /// deep-link routing is assertable without showing a modal dialog.
        /// </summary>
        public Window TestBuildWindowForRenderTest(string? initialPageKey)
        {
            _window = CreateWindow();
            LoadSettingsToControls();
            if (!string.IsNullOrEmpty(initialPageKey))
                SelectTreeLeafByPageKey(initialPageKey!);
            _window.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            _window.Arrange(new Rect(0, 0, _window.DesiredSize.Width, _window.DesiredSize.Height));
            _window.UpdateLayout();
            return _window!;
        }

        /// <summary>
        /// Shared initialization: creates the window, populates controls, and selects the first
        /// navigation item — or the page named by <paramref name="initialPageKey"/> when one is
        /// given. Called by both <see cref="ShowDialog"/> and
        /// <see cref="TestBuildWindowForRenderTest()"/>.
        /// </summary>
        private void BuildWindowInner(string? initialPageKey = null)
        {
            _window = CreateWindow();
            LoadSettingsToControls();

            if (!string.IsNullOrEmpty(initialPageKey) && SelectTreeLeafByPageKey(initialPageKey!))
                return;

            // Select the first category
            if (_navTree?.Items.Count > 0)
            {
                var firstItem = _navTree.Items[0] as TreeViewItem;
                if (firstItem != null)
                    firstItem.IsSelected = true;
            }
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
                Title = WindowTitles.For("Options"),
                Icon = Ui.WindowIcon.Source,
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
            // Spec 040 (T163): the Command Palette's jump to one option, once the page is laid out.
            window.Loaded += (_, _) => ApplyInitialFocus();

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
            var btnResetAll = MakeButton("Restore all defaults", 140);
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
                Width = 240,  // wider to give long labels like "Execution Warnings" room
                Background = _theme.Sidebar,
                BorderBrush = _theme.Sep,
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(0, 12, 0, 0)
            };

            // DockPanel so the title/underline/search stay pinned and the nav tree fills the rest
            // inside a ScrollViewer — otherwise a plain StackPanel gives the tree unbounded height and
            // the lower groups get clipped (no scrollbar) once several are expanded.
            var panel = new DockPanel { LastChildFill = true };

            // Title label — SQL Prompt style ("AKML SQL Options")
            var title = new TextBlock
            {
                Text = Constants.ProductName + " Options",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = _theme.FgPrimary,
                Margin = new Thickness(16, 0, 16, 14)
            };
            DockPanel.SetDock(title, Dock.Top);
            panel.Children.Add(title);

            // Title underline
            var titleRule = new Border
            {
                Height = 1,
                Background = _theme.Sep,
                Margin = new Thickness(12, 0, 12, 10)
            };
            DockPanel.SetDock(titleRule, Dock.Top);
            panel.Children.Add(titleRule);

            // ── Search box (Visual Studio Options-style, but better) ──
            var searchBox = BuildSearchBox();
            DockPanel.SetDock(searchBox, Dock.Top);
            panel.Children.Add(searchBox);

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

            // Spec 036 (US4, FR-001/FR-002, research R7): every hover background must carry its own
            // paired foreground — the trigger used to set only Background, so a selected+hovered item
            // painted TreeHover (near-white in Light) with SelectedText (white): the reported
            // white-on-white bug. FgPrimary is the token paired with SurfaceHover in every palette.
            var mouseOverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            mouseOverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, _theme.TreeHover));
            mouseOverTrigger.Setters.Add(new Setter(Control.ForegroundProperty, _theme.FgPrimary));
            itemStyle.Triggers.Add(mouseOverTrigger);

            // Selected ∧ hovered keeps the selected pair intact (readable by design). Placed last so
            // it wins over the single triggers above for the properties they share.
            var selectedMouseOverTrigger = new MultiTrigger();
            selectedMouseOverTrigger.Conditions.Add(new Condition(TreeViewItem.IsSelectedProperty, true));
            selectedMouseOverTrigger.Conditions.Add(new Condition(UIElement.IsMouseOverProperty, true));
            selectedMouseOverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, _theme.Selected));
            selectedMouseOverTrigger.Setters.Add(new Setter(Control.ForegroundProperty, _theme.SelectedText));
            itemStyle.Triggers.Add(selectedMouseOverTrigger);

            // SQL Prompt's nav has no expander chevrons — a flat, permanently-expanded list with
            // bold groups and indented leaves. Replace the default TreeViewItem template with
            // header + always-visible children (groups stay expanded; leaves have no children so
            // their ItemsPresenter renders nothing). The hover/selected triggers above flow into
            // the header via TemplateBinding. NOTE: the style's explicit Foreground setter must
            // stay — WindowChromeTests reflects it to prove nav text is visible per theme.
            itemStyle.Setters.Add(new Setter(Control.TemplateProperty, BuildNavItemTemplate()));

            // Use implicit style by type so the style cascades to TreeViewItems at every depth.
            // (TreeView.ItemContainerStyle only applies to direct children, breaking nested items.)
            _navTree.Resources[typeof(TreeViewItem)] = itemStyle;

            // Build categories and pages
            BuildPages();

            // ── SQL Prompt-style hierarchical tree ──
            // Source: doc/SQL-PROMPT/SQL-Prompt-Option/SQL_Prompt_Options_Dialog.md §1.2
            // Parent nodes have no Tag (not selectable as a page); leaves carry the page key.
            //
            // ── Spec 020 T044 audit (vs SQL Prompt §1.2) ──
            // No missing pages — AKML's tree is a superset of SQL Prompt's documented hierarchy:
            //   Suggestions ▸ Behavior / Types of suggestion / Database   → present (IntelliSense, SuggestionTypes, Schema Cache)
            //   Inserted Code ▸ Qualification / INSERT / JOIN             → present (Qualification, InsertOptions, JoinOptions)
            //   Format ▸ Styles                                            → present (Formatting)
            //   Queries ▸ History / Execution Warnings / Query Results     → present (History, Safety, Grid)
            //   Tabs ▸ Color                                               → present (Tabs & UI)
            //   Code Analysis, Snippets                                    → present (leaves)
            //   Prompt AI                                                  → present as "AI Assistance" (naming deviation; same scope)
            //   Miscellaneous ▸ Main / Labs                                → present (General, Labs)
            // AKML-only additions (deviations, not gaps):
            //   Editor group ▸ Productivity / Navigation / Refactoring     → AKML-specific editor surfaces
            //   Queries ▸ Execution                                        → AKML-specific execution-environment settings
            // Note: T045 ("add missing Options pages") is superseded — the pages it lists
            // (Queries ▸ Execution Warnings, Query Results, Miscellaneous ▸ Labs) were authored
            // in earlier specs and are already wired to GridPage / SafetyPage / LabsPage / GeneralPage.

            // General is the landing page (first node) so the theme / dark-mode selector is the first
            // thing shown when Options opens. It also carries updates, paths and version info. This
            // replaces the old "Miscellaneous ▸ Application" placement (removed below).
            AddTreeLeaf("General", "General");

            // Spec 040 (OPT-04, contracts/ui.md §1): SQL Prompt's arrangement and names, in sentence
            // case. Only labels move — the page keys (Tags) stay, so deep links and tests still work.
            AddTreeGroup("Suggestions",
                ("Behavior", "IntelliSense"),
                ("Types of suggestion", "SuggestionTypes"),
                ("Tooltips", "CompletionPolish"),
                ("Connections", "ConnectionScope"),
                ("Join conditions", "JoinOptions"),
                ("Snippets", "Snippets"),
                ("Warnings & highlighting", "Safety"));

            AddTreeGroup("Inserted code",
                ("Objects & statements", "InsertOptions"),
                ("Qualification", "Qualification"),
                ("Aliases", "Aliases"),
                ("Special characters", "SpecialCharacters"));

            AddTreeGroup("Format",
                ("Styles", "Formatting"));

            AddTreeLeaf("Navigation", "Navigation");

            AddTreeGroup("Queries",
                ("Query results", "Grid"),
                ("History", "History"),
                ("Color", "Tabs & UI"),
                ("Execution", "Execution"));

            AddTreeGroup("Editor",
                ("Productivity", "Editor"),
                ("Refactoring", "Refactoring"));

            AddTreeLeaf("Code analysis", "Code Analysis");
            AddTreeLeaf("Connections & memory", "ConnectionsMemory");
            AddTreeLeaf("AI assistance", "AI Assistance");

            // Spec 040 (OPT-01): Suggestions › Database and Miscellaneous › Labs are gone — every
            // row on both changed nothing. "Application" moved to the top-level "General" leaf.

            _navTree.SelectedItemChanged += OnNavSelectionChanged;

            // Fill child: the tree scrolls when expanded groups overflow the sidebar height.
            // Fill child: the tree scrolls ITSELF — TreeView's control template already hosts a
            // ScrollViewer, and the DockPanel fill slot constrains its height. Wrapping it in a
            // second ScrollViewer (the previous layout) gave the tree unbounded height, so the
            // inner ScrollViewer never scrolled yet still swallowed every mouse-wheel event —
            // with all groups permanently expanded the nav was wheel-dead.
            ScrollViewer.SetHorizontalScrollBarVisibility(_navTree, ScrollBarVisibility.Disabled);
            _navTree.Margin = new Thickness(0, 0, 0, 8);
            panel.Children.Add(_navTree);
            sidebar.Child = panel;
            return sidebar;
        }

        /// <summary>
        /// Chevron-free nav item template (SQL Prompt style): a header Border that TemplateBinds
        /// Background/Padding (so the style's hover/selected triggers still render) above an
        /// always-visible, indented ItemsPresenter. Groups can no longer collapse — neither can
        /// SQL Prompt's.
        /// </summary>
        private static ControlTemplate BuildNavItemTemplate()
        {
            var headerBorder = new FrameworkElementFactory(typeof(Border), "headerBorder");
            headerBorder.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            headerBorder.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

            var headerContent = new FrameworkElementFactory(typeof(ContentPresenter));
            headerContent.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            headerBorder.AppendChild(headerContent);

            var childrenHost = new FrameworkElementFactory(typeof(ItemsPresenter));
            childrenHost.SetValue(FrameworkElement.MarginProperty, new Thickness(16, 0, 0, 0));

            var root = new FrameworkElementFactory(typeof(StackPanel));
            root.AppendChild(headerBorder);
            root.AppendChild(childrenHost);

            var template = new ControlTemplate(typeof(TreeViewItem)) { VisualTree = root };
            template.Seal();
            return template;
        }

        /// <summary>
        /// Adds a non-selectable parent group with one or more leaf children.
        /// Each leaf is a tuple of (display label, page key in <see cref="_pages"/>).
        /// </summary>
        private void AddTreeGroup(string header, params (string Label, string PageKey)[] children)
        {
            var parent = new TreeViewItem
            {
                Header = header,
                Tag = null, // null = not a page, just a group
                IsExpanded = true, // always-expanded contract — the chevron-free nav template renders children unconditionally
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
                VerticalContentAlignment = VerticalAlignment.Center,
                FocusVisualStyle = FocusVisualStyles.HighStakes // FR-018 / O9 (search input)
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
            // Own the template: the stock Aero2 template paints a ~24% wash on selection and
            // ignores the Background set below — white SelectedText on a near-white wash is
            // invisible (same bug class as the agent list; shared template).
            itemStyle.Setters.Add(new Setter(Control.TemplateProperty, Pages.AiAgentListView.BuildItemTemplate()));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, _theme.Transparent));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, _theme.FgPrimary));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 7, 10, 7)));
            itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            itemStyle.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
            itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            // Spec 036 (US4, FR-003, research R7): pair a foreground with the hover background here
            // too — this list escapes the bug today only through trigger ordering, and one palette
            // change would break it. FgPrimary is the token paired with SurfaceHover.
            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, _theme.TreeHover));
            hoverTrigger.Setters.Add(new Setter(Control.ForegroundProperty, _theme.FgPrimary));
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

            // Type badge — a letter for the setting kind. Spec 040 (OPT-09): theme brushes, not fixed
            // colours — a setting's badge is the accent pair (Selected / SelectedText, the pair built
            // to be read together, system Highlight colours under high contrast); an information row
            // gets the quiet raised face.
            var (letter, isSetting) = entry.Kind switch
            {
                "Toggle"   => ("T", true),
                "Slider"   => ("S", true),
                "Number"   => ("N", true),
                "Dropdown" => ("D", true),
                "Text"     => ("X", true),
                "List"     => ("L", true),
                "Grid"     => ("G", true),
                _           => ("i", false),
            };
            var badge = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(3),
                Background = isSetting ? _theme.Selected : _theme.Button,
                Margin = new Thickness(0, 1, 10, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = letter,
                    Foreground = isSetting ? _theme.SelectedText : _theme.FgPrimary,
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
            if (entry.Row != null)
                ScrollToAndFlash(entry.Row, focusControl: false);
        }

        /// <summary>
        /// Scrolls <paramref name="row"/> into view and flashes it — deferred to the dispatcher so a
        /// page swap made just before completes first. With <paramref name="focusControl"/> the
        /// row's first focusable control also takes keyboard focus (the Command Palette jump).
        /// </summary>
        private void ScrollToAndFlash(FrameworkElement row, bool focusControl)
        {
            row.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    row.BringIntoView();
                    FlashRow(row);
                    if (focusControl)
                        FirstFocusableControl(row)?.Focus();
                }
                catch { /* non-fatal */ }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>The first enabled, focusable control inside an option row, depth first.</summary>
        private static Control? FirstFocusableControl(DependencyObject root)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is not DependencyObject d) continue;
                if (d is Control c && c.Focusable && c.IsEnabled && c.IsTabStop) return c;
                var nested = FirstFocusableControl(d);
                if (nested != null) return nested;
            }
            return null;
        }

        /// <summary>
        /// Spec 040 (OPT-07, FR-053, T163) — scrolls to, flashes and focuses the row labelled
        /// <see cref="InitialFocusLabel"/>: on the selected page when it has one, else on the first
        /// page that does (selecting that page). Runs when the window loads. Returns false when no
        /// row has that label (the window then simply opens on its page).
        /// </summary>
        internal bool ApplyInitialFocus()
        {
            var label = InitialFocusLabel;
            if (string.IsNullOrEmpty(label)) return false;

            var pageKey = CurrentPageKey;
            var entry = _searchIndex.FirstOrDefault(e => e.PageKey == pageKey && string.Equals(e.Label, label, StringComparison.Ordinal))
                        ?? _searchIndex.FirstOrDefault(e => string.Equals(e.Label, label, StringComparison.Ordinal))
                        ?? _searchIndex.FirstOrDefault(e => string.Equals(e.Label, label, StringComparison.OrdinalIgnoreCase));
            if (entry?.Row == null)
            {
                Log.Debug("SettingsWindow: no option labelled {Label} to focus", label);
                return false;
            }

            if (entry.PageKey != pageKey)
                SelectTreeLeafByPageKey(entry.PageKey);
            FocusedRow = entry.Row;
            ScrollToAndFlash(entry.Row, focusControl: true);
            return true;
        }

        /// <summary>
        /// Walks the tree (including parent groups) to find the leaf with the given
        /// page key Tag, expands its parent if needed, and selects it.
        /// Returns false when no leaf carries that key.
        /// </summary>
        private bool SelectTreeLeafByPageKey(string pageKey)
        {
            if (_navTree == null) return false;

            foreach (var obj in _navTree.Items)
            {
                if (obj is not TreeViewItem item) continue;

                if (item.Tag is string topKey && topKey == pageKey)
                {
                    item.IsSelected = true;
                    return true;
                }

                foreach (var childObj in item.Items)
                {
                    if (childObj is not TreeViewItem child) continue;
                    if (child.Tag is string childKey && childKey == pageKey)
                    {
                        item.IsExpanded = true;
                        child.IsSelected = true;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Briefly flashes the row background with the accent color so the user
        /// can spot the setting they jumped to.
        /// </summary>
        private void FlashRow(FrameworkElement row)
        {
            // Spec 040 (T161): toggle rows are Borders; number, dropdown and text rows are panels.
            DependencyProperty? background = row switch
            {
                Border _ => Border.BackgroundProperty,
                Panel _ => Panel.BackgroundProperty,
                _ => null
            };
            if (background == null) return;

            var originalBrush = (Brush?)row.GetValue(background);
            var flashBrush = new SolidColorBrush(((SolidColorBrush)_theme.Selected).Color);
            row.SetValue(background, flashBrush);

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
            animation.Completed += (s, e) => row.SetValue(background, originalBrush);
            flashBrush.BeginAnimation(SolidColorBrush.ColorProperty, animation);
        }

        // ─── Page building ───────────────────────────────────────────────────

        private void BuildPages()
        {
            // Page keys in navigation order; each matches an IPageBuilder in _pageBuilders.
            // The breadcrumb (search results + page header band) comes from the page's own
            // IPageBuilder.Display — the single source, no parallel display list to maintain.
            var pages = new[]
            {
                "General",
                "IntelliSense",
                "SuggestionTypes",
                "CompletionPolish",
                "ConnectionScope",
                "JoinOptions",
                "Snippets",
                "Safety",
                "InsertOptions",
                "Qualification",
                "Aliases",
                "SpecialCharacters",
                "Formatting",
                "Navigation",
                "Grid",
                "History",
                "Tabs & UI",
                "Execution",
                "Editor",
                "Refactoring",
                "Code Analysis",
                "ConnectionsMemory",
                "AI Assistance",
            };

            foreach (var key in pages)
            {
                if (!_pageBuilders.TryGetValue(key, out var pageBuilder))
                    continue;

                _currentPageKey = key;
                _currentPageDisplay = pageBuilder.Display;

                var hostPanel = CreatePagePanel();
                // The header shows the page's breadcrumb ("Inserted Code › Special characters"),
                // not the short Title — SQL Prompt's band names the full location.
                AddPageHeader(hostPanel, pageBuilder.Display, pageBuilder.Help);
                var ctx = new PageContext(_theme, _settings, new RowFactory(_theme), RegisterSearchEntry);
                var controls = pageBuilder.Build(hostPanel, ctx);
                _pageControlsByKey[key] = controls;
                _pages[key] = WrapInScrollViewer(hostPanel);

                // Page-specific event hookup the host owns: theme switching closes the dialog
                // and reopens it under the new theme. (Spec 040, OPT-08: the Color page now owns
                // its rules grid and environments, so no coloring-rule CRUD lives here.)
                if (controls is GeneralControls gen)
                    gen.Theme.SelectionChanged += OnThemeSelectionChanged;
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
        //  Options catalog (Command Palette)
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>A built catalog and the dispatcher its WPF controls belong to.</summary>
        private sealed class OptionsCatalogCache
        {
            public OptionsCatalogCache(System.Windows.Threading.Dispatcher dispatcher, IReadOnlyList<OptionsCatalogEntry> entries)
            {
                Dispatcher = dispatcher;
                Entries = entries;
            }

            public System.Windows.Threading.Dispatcher Dispatcher { get; }
            public IReadOnlyList<OptionsCatalogEntry> Entries { get; }
        }

        private static volatile OptionsCatalogCache? _optionsCatalog;

        /// <summary>
        /// Spec 040 (OPT-07, FR-053, research R7) — every option the Command Palette can offer, read
        /// from the Options pages themselves: the pages are built on a throwaway, never-shown
        /// instance and their search index is the list, so a renamed label, a new row or a moved page
        /// reaches the palette with no second table to keep in step. Each entry keeps its row, its
        /// page's controls and — for a Toggle — its CheckBox, which is how the palette reads and flips
        /// the setting through the page's own Load and Save.
        ///
        /// <para>Left out: the AI assistance page (its rows edit whichever agent is selected) and
        /// Info and Button rows (nothing to set). Built once per UI thread and cached for the session;
        /// <paramref name="settings"/> only seeds the build. Must run on the UI thread.</para>
        /// </summary>
        internal static IReadOnlyList<OptionsCatalogEntry> BuildOptionsCatalog(AppSettings settings)
        {
            var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            var cached = _optionsCatalog;
            if (cached != null && cached.Dispatcher == dispatcher)
                return cached.Entries;

            var host = new SettingsWindow(settings);
            host.BuildPages();
            // The throwaway host never shows a window. Keeping it "loading" for good turns its Theme
            // drop-down handler into a no-op, so loading the General page for the palette can never
            // preview a theme or ask for a reopen.
            host._loadingControls = true;

            var entries = new List<OptionsCatalogEntry>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in host._searchIndex)
            {
                if (e.Row == null || e.PageKey == "AI Assistance" || e.Kind == "Info" || e.Kind == "Button")
                    continue;
                if (!host._pageControlsByKey.TryGetValue(e.PageKey, out var controls))
                    continue;

                CheckBox? toggle = null;
                if (e.Kind == "Toggle")
                {
                    toggle = e.Row as CheckBox ?? FindCheckBox(e.Row);
                    if (toggle == null) continue; // a "Toggle" row the palette couldn't flip
                }

                // The palette id is page + label, so a label repeated on one page is listed once.
                if (!seen.Add(e.PageKey + "\u001F" + e.Label))
                    continue;

                entries.Add(new OptionsCatalogEntry(
                    e.PageKey, e.PageDisplay, e.Label, e.Description, e.Kind, e.Row, controls, toggle));
            }

            _optionsCatalog = new OptionsCatalogCache(dispatcher, entries);
            return entries;
        }

        /// <summary>Drops the cached catalog, so the next <see cref="BuildOptionsCatalog"/> rebuilds it.</summary>
        internal static void InvalidateOptionsCatalog() => _optionsCatalog = null;

        /// <summary>The first CheckBox in an option row (a toggle row's Border wraps it directly).</summary>
        private static CheckBox? FindCheckBox(DependencyObject root)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is CheckBox cb) return cb;
                if (child is DependencyObject d && FindCheckBox(d) is CheckBox nested) return nested;
            }
            return null;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  General
        // ═══════════════════════════════════════════════════════════════════════
        // BuildGeneralPage migrated to Pages/GeneralPage.cs (Phase 2 B.7).

        // ═══════════════════════════════════════════════════════════════════════
        //  IntelliSense
        // ═══════════════════════════════════════════════════════════════════════
        // BuildIntelliSensePage migrated to Pages/IntelliSensePage.cs (Phase 2 B.16).

        // ═══════════════════════════════════════════════════════════════════════
        //  Formatting
        // ═══════════════════════════════════════════════════════════════════════
        // BuildFormattingPage migrated to Pages/FormattingPage.cs (Phase 2 B.14).

        // ═══════════════════════════════════════════════════════════════════════
        //  Snippets
        // ═══════════════════════════════════════════════════════════════════════
        // BuildSnippetsPage migrated to Pages/SnippetsPage.cs (Phase 2 B.2).

        // ═══════════════════════════════════════════════════════════════════════
        //  Code Analysis
        // ═══════════════════════════════════════════════════════════════════════
        // BuildCodeAnalysisPage migrated to Pages/CodeAnalysisPage.cs (Phase 2 B.3).

        // ═══════════════════════════════════════════════════════════════════════
        //  Refactoring
        // ═══════════════════════════════════════════════════════════════════════
        // BuildRefactoringPage migrated to Pages/RefactoringPage.cs (Phase 2 B.4).

        // ═══════════════════════════════════════════════════════════════════════
        //  History
        // ═══════════════════════════════════════════════════════════════════════
        // BuildHistoryPage migrated to Pages/HistoryPage.cs (Phase 2 B.12).

        // ═══════════════════════════════════════════════════════════════════════
        //  Tabs & UI
        // ═══════════════════════════════════════════════════════════════════════
        // BuildTabsPage migrated to Pages/TabsPage.cs (Phase 2 B.15).

        // ═══════════════════════════════════════════════════════════════════════
        //  Safety
        // ═══════════════════════════════════════════════════════════════════════
        // BuildSafetyPage migrated to Pages/SafetyPage.cs (Phase 2 B.8).

        // ═══════════════════════════════════════════════════════════════════════
        //  Grid
        // ═══════════════════════════════════════════════════════════════════════
        // BuildGridPage migrated to Pages/GridPage.cs (Phase 2 B.6).

        // ═══════════════════════════════════════════════════════════════════════
        //  Editor Productivity
        // ═══════════════════════════════════════════════════════════════════════
        // BuildEditorPage migrated to Pages/EditorPage.cs (Phase 2 B.10).

        // ═══════════════════════════════════════════════════════════════════════
        //  Execution Productivity
        // ═══════════════════════════════════════════════════════════════════════
        // BuildExecutionPage migrated to Pages/ExecutionPage.cs (Phase 2 B.9).

        // ═══════════════════════════════════════════════════════════════════════
        //  Navigation
        // ═══════════════════════════════════════════════════════════════════════
        // BuildNavigationPage migrated to Pages/NavigationPage.cs (Phase 2 B.5).

        // ═══════════════════════════════════════════════════════════════════════
        //  AI Assistance
        // ═══════════════════════════════════════════════════════════════════════
        // BuildAiPage migrated to Pages/AiAssistancePage.cs (Phase 2 B.13).

        // ═══════════════════════════════════════════════════════════════════════
        //  UI Builder Helpers
        // ═══════════════════════════════════════════════════════════════════════

        // Row construction moved to RowFactory.WrapZebraRow (Phase 2 B.1+).

        private StackPanel CreatePagePanel()
        {
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
        /// SQL Prompt-style page header: a full-width band (SurfaceElevated with a 1px bottom
        /// border) carrying the bold "Group › Page" breadcrumb on the left and the "?" help
        /// button + "Restore Defaults" push button on the right — matching the Redgate reference
        /// (doc/_Prompt-Gap/SQL Prompt Options - Special characters Redgate.png).
        /// </summary>
        private void AddPageHeader(StackPanel panel, string text, string help)
        {
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // "?" help button
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Restore Defaults button

            var title = new TextBlock
            {
                Text = text,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = _theme.FgPrimary,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(title, 0);
            header.Children.Add(title);

            // Spec 030 T083 (FR-044) + SQL Prompt "?" parity — the page help renders as a
            // collapsed accent-bordered block that this circular "?" button toggles, instead of
            // an always-visible paragraph. Uniform across pages because IPageBuilder.Help is a
            // required member.
            Border? helpBlock = null;
            if (!string.IsNullOrEmpty(help))
            {
                helpBlock = new Border
                {
                    Name = "PageHelpBlock",
                    BorderBrush = _theme.FgAccent,
                    BorderThickness = new Thickness(2, 0, 0, 0),
                    Background = _theme.Panel,
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 0, 0, 12),
                    Visibility = Visibility.Collapsed,
                    Child = new TextBlock
                    {
                        Text = help,
                        Foreground = _theme.FgSecondary,
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        LineHeight = 18
                    }
                };

                // A real Button (not a Border) so the help affordance is keyboard-focusable,
                // Space/Enter-invokable, and visible to assistive tech (PR #248 review finding #8).
                var helpButton = new Button
                {
                    Width = 18,
                    Height = 18,
                    Background = _theme.Transparent,
                    BorderBrush = _theme.FgAccent,
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "What's on this page?",
                    FocusVisualStyle = FocusVisualStyles.HighStakes,
                    Template = BuildHelpButtonTemplate(),
                    Content = new TextBlock
                    {
                        Text = "?",
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Foreground = _theme.FgAccent,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                System.Windows.Automation.AutomationProperties.SetName(helpButton, "Page help");
                var capturedHelp = helpBlock;
                helpButton.Click += (_, _) =>
                    capturedHelp.Visibility = capturedHelp.Visibility == Visibility.Visible
                        ? Visibility.Collapsed
                        : Visibility.Visible;
                Grid.SetColumn(helpButton, 1);
                header.Children.Add(helpButton);
            }

            // Push button, not a link — the reference shows a standard button in the band.
            // Chrome and hover behavior come from MakeButton; only the band's size deltas are
            // overridden (auto width — the 11px label plus padding exceeds MakeButton's fixed 90).
            // Content stays an explicit TextBlock: WindowChromeTests enumerates the logical tree
            // for a TextBlock whose Text is exactly "Restore Defaults".
            var restoreButton = MakeButton("Restore Defaults", 90);
            restoreButton.Content = new TextBlock { Text = "Restore Defaults" };
            restoreButton.Height = 22;
            restoreButton.FontSize = 11;
            restoreButton.Padding = new Thickness(10, 0, 10, 0);
            restoreButton.Width = double.NaN;
            restoreButton.MinWidth = 90;
            restoreButton.VerticalAlignment = VerticalAlignment.Center;
            restoreButton.Click += OnResetThisPageClick;
            Grid.SetColumn(restoreButton, 2);
            header.Children.Add(restoreButton);

            // The band stretches edge-to-edge over CreatePagePanel's 24/18 page margin (same
            // negative-margin technique as WrapZebraRow); its bottom border doubles as the old
            // standalone underline separator.
            panel.Children.Add(new Border
            {
                Background = _theme.Button,
                BorderBrush = _theme.Sep,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(24, 8, 12, 8),
                Margin = new Thickness(-24, -18, -24, 14),
                Child = header
            });

            if (helpBlock != null)
            {
                panel.Children.Add(helpBlock);
            }
        }

        // Add* row helpers + ComboBox theming + zebra striping migrated to
        // RowFactory in Pages/RowFactory.cs (Phase 2 B.1+, cleanup in B.17).

        /// <summary>
        /// Circular chromeless template for the page-header "?" help button: a 9px-radius Border
        /// (TemplateBinding background/border) wrapping the centered content.
        /// </summary>
        private static ControlTemplate BuildHelpButtonTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);

            return new ControlTemplate(typeof(Button)) { VisualTree = border };
        }

        /// <summary>
        /// Secondary push button. Spec 040 (OPT-09, research R9): painted by the themed template
        /// (<see cref="ThemedButton.ApplySecondary(Button, PageTheme)"/>) — the stock Aero chrome,
        /// which mouse-enter/leave handlers used to fight, repainted the face near-white on hover in
        /// Dark and ignored the theme while pressed.
        /// </summary>
        private Button MakeButton(string text, double width)
        {
            var btn = new Button
            {
                Content = text,
                Width = width,
                Height = 30,
                FontSize = 12,
                Padding = new Thickness(12, 4, 12, 4),
                Cursor = Cursors.Hand,
                FocusVisualStyle = FocusVisualStyles.HighStakes // FR-018 / O9
            };
            ThemedButton.ApplySecondary(btn, _theme);
            return btn;
        }

        /// <summary>
        /// Primary action button — solid accent (SQL Prompt style for OK). Spec 040 (OPT-09): hover
        /// and pressed come from this window's <see cref="PageTheme"/>, not from
        /// <see cref="ThemeRegistry"/>'s current palette, which can be the other theme.
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
                Padding = new Thickness(12, 4, 12, 4),
                Cursor = Cursors.Hand,
                FocusVisualStyle = FocusVisualStyles.HighStakes // FR-018 / O9 (primary action)
            };
            ThemedButton.ApplyPrimary(btn, _theme);
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
            if (!ValidateAiWorkingCopyBeforeSave()) return;

            SaveControlsToSettings();
            _dialogResult = true;
            _window?.Close();
        }

        /// <summary>
        /// Spec 037 (US2, FR-032): the validate-first half of OK and Apply — refused while any
        /// agent the user EDITED here fails V1–V12 (a user can leave an invalid agent, select
        /// another, and press OK), with the offending agent selected before the message is
        /// shown, so the user lands where the problem is; the dialog stays open.
        ///
        /// <para>Defect 9 fix: the scope is the agents this dialog session touched, not the
        /// whole working copy. Every page loads eagerly, so the AI page always holds every
        /// stored agent — gating the WHOLE dialog on all of them let one stale half-configured
        /// agent (a key typed weeks ago, no model) refuse a save the user came here to make on
        /// an unrelated page, yank the nav to a page they never opened, and leave no way out but
        /// repairing that agent. See <c>AiAssistanceControls.ValidateTouchedAgents</c>.</para>
        /// </summary>
        private bool ValidateAiWorkingCopyBeforeSave()
        {
            if (_pageControlsByKey.TryGetValue("AI Assistance", out var aiPageControls) &&
                aiPageControls is AiAssistanceControls aiControls)
            {
                var validationError = aiControls.ValidateTouchedAgents();
                if (validationError != null)
                {
                    SelectTreeLeafByPageKey("AI Assistance");
                    if (ValidationRefusalReporter != null)
                    {
                        ValidationRefusalReporter(validationError);
                    }
                    else
                    {
                        MessageBox.Show(
                            validationError,
                            Constants.ProductName,
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                    return false;
                }
            }
            return true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            _dialogResult = false;
            _window?.Close();
        }

        private void OnApplyClick(object sender, RoutedEventArgs e)
        {
            // Apply is OK without the close: the same validate-first gate (FR-032), then the
            // same save-and-notify path (the AnalysisSettingsChanged notification is what keeps
            // the engine from serving stale settings after an Apply).
            if (!ValidateAiWorkingCopyBeforeSave()) return;

            SaveControlsToSettings();
            try
            {
                Commands.OptionsCommand.SaveAndNotify(_settings);
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
            // Ctrl+E or Ctrl+F → focus search box (VS Options shortcut convention)
            if ((e.Key == Key.E || e.Key == Key.F)
                && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                _searchBox?.Focus();
                _searchBox?.SelectAll();
                e.Handled = true;
                return;
            }

            // Spec 040 (X-03, FR-062): F1 opens the selected page's topic on the docs site.
            if (e.Key == Key.F1 && Keyboard.Modifiers == ModifierKeys.None)
            {
                var topic = CurrentHelpTopic;
                if (!string.IsNullOrEmpty(topic))
                    global::AkmlSql.Shell.Shared.Help.F1HelpListener.Default.Open(topic!);
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

        /// <summary>
        /// Spec 040 (OPT-02, FR-004, research R2) — a real Theme pick closes the window so it can
        /// reopen under the new brushes. Nothing is written to disk: every page's unsaved edits go
        /// into the working copy the reopened window starts from, and OK or Cancel in that window
        /// decides. Selection changes raised while controls load (window open, Reset, Import) are
        /// ignored — they used to save half-loaded settings and reopen the window by themselves.
        /// </summary>
        private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingControls) return;
            if (!_pageControlsByKey.TryGetValue("General", out var c) || c is not GeneralControls gen)
                return;

            // Index 0 = Dark, 1 = Light, 2 = System (follow VS/SSMS)
            var pick = gen.Theme.SelectedIndex switch
            {
                0 => "dark",
                2 => "system",
                _ => "light",
            };
            if (ResolvePageTheme(pick) == _theme)
                return; // same brushes — nothing to reopen

            SaveControlsToSettings();
            // Other AKML surfaces preview the pick at once; Cancel in the reopened window restores it.
            Commands.OptionsCommand.ApplyThemePreference(pick);

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

                ImportSettings(imported);
                Log.Information("Settings imported from {Path}", dlg.FileName);
                MessageBox.Show(
                    "Settings imported. Click OK to save them, or Cancel to discard.",
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

        /// <summary>
        /// Spec 040 (OPT-03, FR-007) — replaces the working copy with <paramref name="imported"/>,
        /// keeping this installation's identity and first-run state. Nothing is saved until OK.
        /// </summary>
        internal void ImportSettings(AppSettings imported)
        {
            ConfigManager.PreserveInstallState(_settings, imported);
            _settings = imported;
            LoadSettingsToControls();
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

                if (MessageBox.Show(ResetConfirmationText(pageName!),
                    Constants.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;

                // Only this page's controls change; unsaved edits on other pages stay as they are.
                ResetPageToDefaultsCore(pageName!);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SettingsWindow: Reset page failed");
            }
        }

        /// <summary>
        /// Spec 040 (OPT-03, FR-005) — resets exactly the settings the page shows: the page's own
        /// controls load the defaults and write them back, so settings hidden from every page
        /// (rule overrides, connection aliases, severities …) and the other pages' unsaved edits
        /// survive. Throws for an unknown page key. Needs the window to be built.
        /// </summary>
        internal void ResetPageToDefaultsCore(string pageKey)
        {
            if (!_pageControlsByKey.TryGetValue(pageKey, out var controls))
                throw new InvalidOperationException($"Page '{pageKey}' is not registered in the Options window.");

            SaveControlsToSettings();
            _loadingControls = true;
            try
            {
                controls.Reset(new AppSettings());
                controls.Save(_settings);
                controls.Load(_settings);
            }
            finally
            {
                _loadingControls = false;
            }
            // (Spec 040, OPT-08: the Color page's Reset brings back the default rules and
            // environments itself — they are part of its Load/Save.)
        }

        /// <summary>
        /// Spec 040 (OPT-03, FR-006) — the page reset confirmation, naming the page as the tree
        /// shows it (never the raw page key) and warning about what else goes with it.
        /// </summary>
        internal string ResetConfirmationText(string pageKey)
        {
            var display = _pageBuilders.TryGetValue(pageKey, out var builder) ? builder.Display : pageKey;
            var text = $"Reset the settings on {display}?";
            if (pageKey == "AI Assistance")
            {
                var agents = _settings.Ai.Agents?.Count ?? 0;
                if (agents > 0)
                    text += Environment.NewLine + $"This also removes your {agents} AI agent{(agents == 1 ? "" : "s")} and their API keys.";
            }
            else if (pageKey == "Tabs & UI")
            {
                text += Environment.NewLine + "This also restores the default environments and rules.";
            }
            return text;
        }

        private void OnResetAllClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MessageBox.Show("Reset ALL settings to defaults? Click OK afterwards to save, or Cancel to keep your settings.",
                    Constants.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;

                ResetAllToDefaultsCore();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SettingsWindow: Reset all failed");
            }
        }

        /// <summary>
        /// Spec 040 (OPT-03, FR-007) — every setting back to its default in the working copy,
        /// keeping this installation's identity and first-run state. Saved only on OK.
        /// </summary>
        internal void ResetAllToDefaultsCore()
        {
            var fresh = new AppSettings();
            ConfigManager.PreserveInstallState(_settings, fresh);
            _settings = fresh;
            LoadSettingsToControls();
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Load settings into controls
        // ═══════════════════════════════════════════════════════════════════════

        private void LoadSettingsToControls()
        {
            _loadingControls = true;
            try
            {
                // Spec 037 (US1, FR-017): hand the deep-link agent id to the AI Assistance page
                // before it loads — it selects that agent, or performs its implicit Add when the
                // id is "" and the agent list is empty.
                if (_pageControlsByKey.TryGetValue("AI Assistance", out var aiPageControls) &&
                    aiPageControls is AiAssistanceControls aiControls)
                {
                    aiControls.InitialAgentId = InitialAgentId;
                }

                // Single dispatch loop covers every registered IPageBuilder. Adding
                // a new page to _pageBuilders automatically picks up Load coverage —
                // there is no second list to keep in sync, which was the root cause
                // of the C.1-C.5 silent-discard bug fixed in this commit.
                foreach (var controls in _pageControlsByKey.Values)
                    controls.Load(_settings);
            }
            finally
            {
                _loadingControls = false;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Save controls to settings
        // ═══════════════════════════════════════════════════════════════════════

        private void SaveControlsToSettings()
        {
            // Symmetrical with LoadSettingsToControls — single dispatch loop
            // covers every registered IPageBuilder.
            foreach (var controls in _pageControlsByKey.Values)
                controls.Save(_settings);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Null-safe helpers
        // ═══════════════════════════════════════════════════════════════════════

        // IsChecked / SetChecked / SetSlider / GetSliderInt / SetCombo /
        // GetComboIndex / SetText / GetText helpers were used by the inline
        // LoadSettingsToControls / SaveControlsToSettings blocks that have
        // moved into per-page IPageControls implementations (B.2-B.16).
        // Each page now reads/writes its own controls directly.
    }

    /// <summary>
    /// Spec 040 (OPT-07, FR-053) — one option as the Command Palette lists it, from
    /// <see cref="SettingsWindow.BuildOptionsCatalog"/>. The public members describe it; the
    /// internal ones are the row on the page it came from, that page's controls and, for a toggle,
    /// its CheckBox.
    /// </summary>
    internal sealed class OptionsCatalogEntry
    {
        internal OptionsCatalogEntry(
            string pageKey, string pageDisplay, string label, string description, string kind,
            FrameworkElement row, IPageControls controls, CheckBox? toggle)
        {
            PageKey = pageKey;
            PageDisplay = pageDisplay;
            Label = label;
            Description = description;
            Kind = kind;
            Row = row;
            Controls = controls;
            Toggle = toggle;
        }

        /// <summary>The page's key (the tree leaf's Tag), e.g. <c>IntelliSense</c>.</summary>
        public string PageKey { get; }

        /// <summary>The page's breadcrumb, e.g. <c>Suggestions › Behavior</c>.</summary>
        public string PageDisplay { get; }

        /// <summary>The option's label, exactly as the page shows it.</summary>
        public string Label { get; }

        public string Description { get; }

        /// <summary>The search kind the page registered: Toggle, Slider, Number, Dropdown, Text, List or Grid.</summary>
        public string Kind { get; }

        /// <summary>True for an on/off option the palette flips in place.</summary>
        public bool IsToggle => Toggle != null;

        internal FrameworkElement Row { get; }

        internal IPageControls Controls { get; }

        internal CheckBox? Toggle { get; }
    }
}
