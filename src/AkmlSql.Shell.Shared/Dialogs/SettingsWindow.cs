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
    internal sealed class SettingsWindow
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
            ["Schema Cache"] = new SchemaCachePage(),
            ["History"] = new HistoryPage(),
            ["AI Assistance"] = new AiAssistancePage(),
            ["Formatting"] = new FormattingPage(),
            ["Tabs & UI"] = new TabsPage(),
            ["IntelliSense"] = new IntelliSensePage(),
            ["SuggestionTypes"] = new SuggestionTypesPage(),
            ["Qualification"] = new QualificationPage(),
        };
        private readonly Dictionary<string, IPageControls> _pageControlsByKey = new();

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
        // General controls migrated to Pages/GeneralPage.cs (Phase 2 B.7).

        // IntelliSense
        // IntelliSense controls migrated to Pages/IntelliSensePage.cs (Phase 2 B.16).

        // Schema Cache
        // Schema Cache controls migrated to Pages/SchemaCachePage.cs (Phase 2 B.11).

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

            // Pick theme based on settings (default: light, like SQL Prompt)
            var themeName = settings.Theme?.ToLowerInvariant() ?? "light";
            _theme = themeName == "dark" ? PageTheme.Dark : PageTheme.Light;
        }

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
        /// Shared initialization: creates the window, populates controls, and selects the first
        /// navigation item. Called by both <see cref="ShowDialog"/> and
        /// <see cref="TestBuildWindowForRenderTest"/>.
        /// </summary>
        private void BuildWindowInner()
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
                Width = 240,  // wider to give long labels like "Execution Warnings" room
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

            // Use implicit style by type so the style cascades to TreeViewItems at every depth.
            // (TreeView.ItemContainerStyle only applies to direct children, breaking nested items.)
            _navTree.Resources[typeof(TreeViewItem)] = itemStyle;

            // Build categories and pages
            BuildPages();

            // ── SQL Prompt-style hierarchical tree ──
            // Source: doc/SQL-PROMPT/SQL-Prompt-Option/SQL_Prompt_Options_Dialog.md §1.2
            // Parent nodes have no Tag (not selectable as a page); leaves carry the page key.

            AddTreeGroup("Suggestions", expanded: true,
                ("Behavior", "IntelliSense"),
                ("Types of suggestion", "SuggestionTypes"),
                ("Database", "Schema Cache"));

            // Inserted Code group introduced in Phase 2 (C.2-C.4).
            AddTreeGroup("Inserted Code", expanded: false,
                ("Qualification & Brackets", "Qualification"));

            AddTreeGroup("Format", expanded: false,
                ("Styles", "Formatting"));

            AddTreeGroup("Editor", expanded: false,
                ("Productivity", "Editor"),
                ("Navigation", "Navigation"),
                ("Refactoring", "Refactoring"));   // moved from "Inserted Code"

            AddTreeGroup("Queries", expanded: false,
                ("History", "History"),
                ("Execution Warnings", "Safety"),
                ("Query Results", "Grid"),
                ("Execution", "Execution"));

            AddTreeGroup("Tabs", expanded: false,
                ("Color", "Tabs & UI"));

            AddTreeLeaf("Code Analysis", "Code Analysis");
            AddTreeLeaf("Snippets", "Snippets");
            AddTreeLeaf("AI Assistance", "AI Assistance");

            AddTreeGroup("Miscellaneous", expanded: false,
                ("Main", "General"));
            // "Labs" sub-leaf is added in Phase 2.

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
                    // Letter sits on a colored badge; SelectedText is "text on accent" (white in both themes).
                    Text = letter,
                    Foreground = _theme.SelectedText,
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
            // (Key, Display) for each page in navigation order. The key matches an
            // IPageBuilder in _pageBuilders; Display is the breadcrumb shown in
            // search results.
            var pages = new (string Key, string Display)[]
            {
                ("General",       "Miscellaneous › Main"),
                ("IntelliSense",  "Suggestions › Behavior"),
                ("SuggestionTypes", "Suggestions › Types of suggestion"),
                ("Schema Cache",  "Suggestions › Database"),
                ("Qualification", "Inserted Code › Qualification & Brackets"),
                ("Formatting",    "Format › Styles"),
                ("Snippets",      "Snippets"),
                ("Code Analysis", "Code Analysis"),
                ("Refactoring",   "Editor › Refactoring"),
                ("History",       "Queries › History"),
                ("Tabs & UI",     "Tabs › Color"),
                ("Safety",        "Queries › Execution Warnings"),
                ("AI Assistance", "AI Assistance"),
                ("Grid",          "Queries › Query Results"),
                ("Editor",        "Editor › Productivity"),
                ("Execution",     "Queries › Execution"),
                ("Navigation",    "Editor › Navigation"),
            };

            foreach (var (key, display) in pages)
            {
                _currentPageKey = key;
                _currentPageDisplay = display;

                if (!_pageBuilders.TryGetValue(key, out var pageBuilder))
                    continue;

                var hostPanel = CreatePagePanel();
                AddPageHeader(hostPanel, pageBuilder.Title);
                var ctx = new PageContext(_theme, _settings, new RowFactory(_theme), RegisterSearchEntry);
                var controls = pageBuilder.Build(hostPanel, ctx);
                _pageControlsByKey[key] = controls;
                _pages[key] = WrapInScrollViewer(hostPanel);

                // Page-specific event hookups the host owns. Theme switching closes
                // the dialog and reopens it under the new theme; coloring-rule CRUD
                // pops a host-owned modal — both stay on SettingsWindow.
                if (controls is GeneralControls gen)
                    gen.Theme.SelectionChanged += OnThemeSelectionChanged;
                if (controls is TabsControls tabs)
                {
                    tabs.AddRuleButton.Click    += (_, _) => OnAddColoringRule();
                    tabs.EditRuleButton.Click   += (_, _) => OnEditColoringRule();
                    tabs.RemoveRuleButton.Click += (_, _) => OnRemoveColoringRule();
                }
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
        // BuildGeneralPage migrated to Pages/GeneralPage.cs (Phase 2 B.7).

        // ═══════════════════════════════════════════════════════════════════════
        //  IntelliSense
        // ═══════════════════════════════════════════════════════════════════════
        // BuildIntelliSensePage migrated to Pages/IntelliSensePage.cs (Phase 2 B.16).

        // ═══════════════════════════════════════════════════════════════════════
        //  Schema Cache
        // ═══════════════════════════════════════════════════════════════════════
        // BuildSchemaCachePage migrated to Pages/SchemaCachePage.cs (Phase 2 B.11).

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

        // Zebra striping moved to RowFactory.WrapZebraRow / ResetZebra (Phase 2 B.1+).

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

        // Add* row helpers + ComboBox theming + zebra striping migrated to
        // RowFactory in Pages/RowFactory.cs (Phase 2 B.1+, cleanup in B.17).

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
                Cursor = Cursors.Hand,
                FocusVisualStyle = FocusVisualStyles.HighStakes // FR-018 / O9
            };

            var theme = _theme; // capture for lambda
            // Explicitly restore Foreground on both enter/leave so that the VS/SSMS host's
            // default button-hover template doesn't override the text color in dark theme.
            btn.MouseEnter += (s, e) => { btn.Background = theme.ButtonHover; btn.Foreground = theme.FgPrimary; };
            btn.MouseLeave += (s, e) => { btn.Background = theme.Button;      btn.Foreground = theme.FgPrimary; };

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
                Cursor = Cursors.Hand,
                FocusVisualStyle = FocusVisualStyles.HighStakes // FR-018 / O9 (primary action)
            };

            var theme = _theme;
            // Subtle hover: slightly lighter accent — pulled from the central palette via AccentPrimaryHover token.
            var hoverBrush = (SolidColorBrush)ThemeRegistry.Instance.Resources[ThemeTokens.AccentPrimaryHover];
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
            if (!_pageControlsByKey.TryGetValue("General", out var c) || c is not GeneralControls gen)
                return;
            var idx = gen.Theme.SelectedIndex;

            // Index 0 = Dark, Index 1 = Light, Index 2 = System (auto-detect from VS/SSMS)
            var requestedTheme = idx switch
            {
                0 => PageTheme.Dark,
                2 => Ui.ThemeManager.DetectFromEnvironment() == Ui.VsThemeKind.Dark
                    ? PageTheme.Dark : PageTheme.Light,
                _ => PageTheme.Light
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
                    case "SuggestionTypes": _settings.IntelliSense.SuggestionTypes = defaults.IntelliSense.SuggestionTypes; break;
                    case "Qualification": _settings.IntelliSense.Qualification = defaults.IntelliSense.Qualification; break;
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
            // ── General (page-split: B.7) ───────────────────────────────
            if (_pageControlsByKey.TryGetValue("General", out var genLoad))
                genLoad.Load(_settings);

            // ── IntelliSense (page-split: B.16) ─────────────────────────
            if (_pageControlsByKey.TryGetValue("IntelliSense", out var isLoad))
                isLoad.Load(_settings);

            // ── Schema Cache ─────────────────────────────────────────────
            // ── Schema Cache (page-split: B.11) ─────────────────────────
            if (_pageControlsByKey.TryGetValue("Schema Cache", out var cacheLoad))
                cacheLoad.Load(_settings);

            // ── Formatting ───────────────────────────────────────────────
            // ── Formatting (page-split: B.14) ───────────────────────────
            if (_pageControlsByKey.TryGetValue("Formatting", out var fmtLoad))
                fmtLoad.Load(_settings);

            // ── Snippets (page-split: B.2) ──────────────────────────────
            if (_pageControlsByKey.TryGetValue("Snippets", out var snippetsLoad))
                snippetsLoad.Load(_settings);

            // ── Code Analysis (page-split: B.3) ─────────────────────────
            if (_pageControlsByKey.TryGetValue("Code Analysis", out var caLoad))
                caLoad.Load(_settings);

            // ── Refactoring (page-split: B.4) ───────────────────────────
            if (_pageControlsByKey.TryGetValue("Refactoring", out var rfLoad))
                rfLoad.Load(_settings);

            // ── History ──────────────────────────────────────────────────
            // ── History (page-split: B.12) ──────────────────────────────
            if (_pageControlsByKey.TryGetValue("History", out var histLoad))
                histLoad.Load(_settings);

            // ── Tabs & UI (page-split: B.15) ────────────────────────────
            if (_pageControlsByKey.TryGetValue("Tabs & UI", out var tabsLoad))
                tabsLoad.Load(_settings);
            // Coloring rules list is rebuilt by the host (CRUD lives on SettingsWindow).
            PopulateColoringRulesList();

            // ── Safety (page-split: B.8) ────────────────────────────────
            if (_pageControlsByKey.TryGetValue("Safety", out var sfLoad))
                sfLoad.Load(_settings);

            // ── AI Assistance ────────────────────────────────────────────
            // ── AI Assistance (page-split: B.13) ────────────────────────
            if (_pageControlsByKey.TryGetValue("AI Assistance", out var aiLoad))
                aiLoad.Load(_settings);

            // ── Grid ─────────────────────────────────────────────────────
            // ── Grid (page-split: B.6) ──────────────────────────────────
            if (_pageControlsByKey.TryGetValue("Grid", out var gridLoad))
                gridLoad.Load(_settings);

            // ── Editor Productivity (page-split: B.10) ──────────────────
            if (_pageControlsByKey.TryGetValue("Editor", out var edLoad))
                edLoad.Load(_settings);

            // ── Execution ────────────────────────────────────────────────
            // ── Execution (page-split: B.9) ─────────────────────────────
            if (_pageControlsByKey.TryGetValue("Execution", out var execLoad))
                execLoad.Load(_settings);

            // ── Navigation ───────────────────────────────────────────────
            // ── Navigation (page-split: B.5) ────────────────────────────
            if (_pageControlsByKey.TryGetValue("Navigation", out var navLoad))
                navLoad.Load(_settings);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Save controls to settings
        // ═══════════════════════════════════════════════════════════════════════

        private void SaveControlsToSettings()
        {
            // ── General (page-split: B.7) ───────────────────────────────
            if (_pageControlsByKey.TryGetValue("General", out var genSave))
                genSave.Save(_settings);

            // ── IntelliSense ─────────────────────────────────────────────
            // ── IntelliSense (page-split: B.16) ─────────────────────────
            if (_pageControlsByKey.TryGetValue("IntelliSense", out var isSave))
                isSave.Save(_settings);

            // ── Schema Cache ─────────────────────────────────────────────
            // ── Schema Cache (page-split: B.11) ─────────────────────────
            if (_pageControlsByKey.TryGetValue("Schema Cache", out var cacheSave))
                cacheSave.Save(_settings);

            // ── Formatting ───────────────────────────────────────────────
            // ── Formatting (page-split: B.14) ───────────────────────────
            if (_pageControlsByKey.TryGetValue("Formatting", out var fmtSave))
                fmtSave.Save(_settings);

            // ── Snippets ─────────────────────────────────────────────────
            // ── Snippets (page-split: B.2) ──────────────────────────────
            if (_pageControlsByKey.TryGetValue("Snippets", out var snippetsSave))
                snippetsSave.Save(_settings);

            // ── Code Analysis ────────────────────────────────────────────
            // ── Code Analysis (page-split: B.3) ─────────────────────────
            if (_pageControlsByKey.TryGetValue("Code Analysis", out var caSave))
                caSave.Save(_settings);

            // ── Refactoring (page-split: B.4) ───────────────────────────
            if (_pageControlsByKey.TryGetValue("Refactoring", out var rfSave))
                rfSave.Save(_settings);

            // ── History ──────────────────────────────────────────────────
            // ── History (page-split: B.12) ──────────────────────────────
            if (_pageControlsByKey.TryGetValue("History", out var histSave))
                histSave.Save(_settings);

            // ── Tabs & UI (page-split: B.15) ────────────────────────────
            if (_pageControlsByKey.TryGetValue("Tabs & UI", out var tabsSave))
                tabsSave.Save(_settings);

            // ── Safety (page-split: B.8) ────────────────────────────────
            if (_pageControlsByKey.TryGetValue("Safety", out var sfSave))
                sfSave.Save(_settings);

            // ── AI Assistance (page-split: B.13) ────────────────────────
            if (_pageControlsByKey.TryGetValue("AI Assistance", out var aiSave))
                aiSave.Save(_settings);

            // ── Grid ─────────────────────────────────────────────────────
            // ── Grid (page-split: B.6) ──────────────────────────────────
            if (_pageControlsByKey.TryGetValue("Grid", out var gridSave))
                gridSave.Save(_settings);

            // ── Editor Productivity ──────────────────────────────────────
            // ── Editor Productivity (page-split: B.10) ──────────────────
            if (_pageControlsByKey.TryGetValue("Editor", out var edSave))
                edSave.Save(_settings);

            // ── Execution ────────────────────────────────────────────────
            // ── Execution (page-split: B.9) ─────────────────────────────
            if (_pageControlsByKey.TryGetValue("Execution", out var execSave))
                execSave.Save(_settings);

            // ── Navigation ───────────────────────────────────────────────
            // ── Navigation (page-split: B.5) ────────────────────────────
            if (_pageControlsByKey.TryGetValue("Navigation", out var navSave))
                navSave.Save(_settings);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Null-safe helpers
        // ═══════════════════════════════════════════════════════════════════════

        // ═══════════════════════════════════════════════════════════════════════
        //  Coloring Rules CRUD
        // ═══════════════════════════════════════════════════════════════════════

        private ListBox? GetColoringRulesList()
            => _pageControlsByKey.TryGetValue("Tabs & UI", out var c) && c is TabsControls tc
                ? tc.ColoringRulesList
                : null;

        private void PopulateColoringRulesList()
        {
            var list = GetColoringRulesList();
            if (list == null) return;
            list.Items.Clear();
            foreach (var rule in _settings.Tabs.ColoringRules)
            {
                list.Items.Add($"[{rule.Label}]  {rule.Pattern}  \u2192  {rule.Color}");
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
            var index = GetColoringRulesList()?.SelectedIndex ?? -1;
            if (index < 0 || index >= _settings.Tabs.ColoringRules.Count) return;

            var rule = _settings.Tabs.ColoringRules[index];
            if (ShowRuleEditor(rule, "Edit Environment Rule"))
            {
                PopulateColoringRulesList();
                GetColoringRulesList()!.SelectedIndex = index;
            }
        }

        private void OnRemoveColoringRule()
        {
            var index = GetColoringRulesList()?.SelectedIndex ?? -1;
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

        // IsChecked / SetChecked / SetSlider / GetSliderInt / SetCombo /
        // GetComboIndex / SetText / GetText helpers were used by the inline
        // LoadSettingsToControls / SaveControlsToSettings blocks that have
        // moved into per-page IPageControls implementations (B.2-B.16).
        // Each page now reads/writes its own controls directly.
    }
}
