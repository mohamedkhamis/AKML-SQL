#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using AkmlSql.Core.Config;
using AkmlSql.Core.Models.Tabs;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Serilog;
using Window = EnvDTE.Window;

namespace AkmlSql.Shell.Shared.Tabs
{
    /// <summary>
    /// Monitors document window activations and applies background colour, environment
    /// labels, status bar tinting, and floating window borders based on the connected
    /// server environment.
    /// <para>
    /// Colour mapping is driven by <see cref="EnvironmentDetector"/> which evaluates
    /// <see cref="ColoringRule"/> entries from <c>config.json</c>.
    /// </para>
    /// <para>
    /// Status bar coloring (T027) and floating window border (T028) are applied on
    /// every window activation (T029) so the visual cue follows focus changes.
    /// </para>
    /// </summary>
    internal static class TabColoringManager
    {
        private static DTE2? _dte;
        private static WindowEvents? _windowEvents;
        private static bool _initialized;

        /// <summary>
        /// Weak reference to the package, allowing <see cref="RepaintAllTabs"/> to
        /// late-initialize when coloring was disabled at startup but enabled later.
        /// </summary>
        private static WeakReference<AsyncPackage>? _packageRef;

        /// <summary>
        /// Tracks the last environment color applied to the status bar so we can avoid
        /// redundant visual tree walks when the color hasn't changed.
        /// </summary>
        private static Color? _lastStatusBarColor;

        /// <summary>
        /// Tracks the WPF element we last used as the status bar, to allow clearing
        /// it when the environment changes or becomes unresolved.
        /// </summary>
        private static WeakReference<FrameworkElement>? _statusBarElement;

        /// <summary>
        /// Subscribes to DTE window activation events. Must be called on the UI thread
        /// during package initialization.
        /// </summary>
        /// <param name="package">The async package instance (used to retrieve DTE).</param>
        public static void Initialize(AsyncPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Always store the package reference so RepaintAllTabs can late-initialize
            // even when coloring is currently disabled (FR-042).
            _packageRef = new WeakReference<AsyncPackage>(package);

            if (_initialized) return;

            try
            {
                // Guard: ensure coloring is enabled before subscribing to events
                var settings = ConfigManager.Load();
                if (!settings.Tabs.ColoringEnabled)
                {
                    Log.Information("TabColoringManager: tab coloring is disabled, skipping initialization");
                    return;
                }

                _dte = (DTE2?)Package.GetGlobalService(typeof(DTE));
                if (_dte == null)
                {
                    Log.Warning("TabColoringManager: DTE service not available");
                    return;
                }

                // Keep a strong reference to WindowEvents so it is not garbage-collected.
                _windowEvents = _dte.Events.WindowEvents;
                _windowEvents.WindowActivated += OnWindowActivated;

                _initialized = true;
                Log.Information("TabColoringManager: initialized and listening for window activations");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "TabColoringManager: failed to initialize");
            }
        }

        /// <summary>
        /// Reloads environment rules and repaints all open document tabs.
        /// Call from the UI thread after settings are saved (FR-042).
        /// </summary>
        public static void RepaintAllTabs()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // Late-initialize if coloring was disabled at startup but enabled later.
                if (!_initialized)
                {
                    AsyncPackage? pkg = null;
                    _packageRef?.TryGetTarget(out pkg);
                    if (pkg != null)
                        Initialize(pkg);
                    if (_dte == null) return;
                }

                EnvironmentDetector.Reload();

                if (_dte == null) return;

                var settings = ConfigManager.Load();

                foreach (Window window in _dte.Windows)
                {
                    try
                    {
                        if (window.Kind != "Document") continue;

                        if (!settings.Tabs.ColoringEnabled)
                        {
                            ClearTabColor(window);
                            continue;
                        }

                        var serverName = GetActiveServerName(window);
                        if (string.IsNullOrWhiteSpace(serverName))
                        {
                            ClearTabColor(window);
                            continue;
                        }

                        var rule = EnvironmentDetector.Match(serverName);
                        if (rule != null)
                            ApplyTabColor(window, rule);
                        else
                            ClearTabColor(window);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "TabColoringManager: error repainting tab '{Caption}'", window.Caption);
                    }
                }

                // Refresh status bar for the currently active window.
                try
                {
                    var activeWindow = _dte.ActiveWindow;
                    if (activeWindow?.Kind == "Document")
                    {
                        var server = GetActiveServerName(activeWindow);
                        var rule = !string.IsNullOrWhiteSpace(server) ? EnvironmentDetector.Match(server) : null;
                        if (rule != null)
                        {
                            var color = ParseHexColor(rule.Color);
                            if (color.HasValue && settings.Tabs.StatusBarColorEnabled)
                            {
                                _lastStatusBarColor = null; // Force refresh
                                ApplyStatusBarColor(color.Value, rule.Label);
                            }
                        }
                        else
                        {
                            ClearStatusBarColor();
                        }
                    }
                    else
                    {
                        ClearStatusBarColor();
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "TabColoringManager: error refreshing status bar during repaint");
                }

                Log.Information("TabColoringManager: repainted all tabs after settings change");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "TabColoringManager: failed to repaint all tabs");
            }
        }

        /// <summary>
        /// Fired whenever a VS/SSMS window receives focus. If the window is a document
        /// window, we detect the connected server and apply tab colouring, status bar
        /// tinting (T027), and floating window border (T028).
        /// </summary>
        private static void OnWindowActivated(Window gotFocus, Window lostFocus)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // Only process document windows (Kind == "Document").
                if (gotFocus == null || gotFocus.Kind != "Document")
                    return;

                // Guard: check if coloring is enabled.
                var settings = ConfigManager.Load();
                if (!settings.Tabs.ColoringEnabled)
                {
                    ClearTabColor(gotFocus);
                    ClearStatusBarColor();
                    return;
                }

                // Detect the connected server name from the active connection context.
                var serverName = GetActiveServerName(gotFocus);
                if (string.IsNullOrWhiteSpace(serverName))
                {
                    ClearTabColor(gotFocus);
                    ClearStatusBarColor();
                    return;
                }

                // Match against environment rules.
                var rule = EnvironmentDetector.Match(serverName);
                if (rule != null)
                {
                    ApplyTabColor(gotFocus, rule);

                    // Parse the rule color once for status bar and floating window.
                    var envColor = ParseHexColor(rule.Color);
                    if (envColor.HasValue)
                    {
                        // T027: status bar color propagation
                        if (settings.Tabs.StatusBarColorEnabled)
                        {
                            ApplyStatusBarColor(envColor.Value, rule.Label);
                        }

                        // T028: floating window border
                        if (settings.Tabs.FloatingWindowBorderEnabled)
                        {
                            ApplyFloatingWindowBorder(gotFocus, envColor.Value);
                        }
                    }
                }
                else
                {
                    ClearTabColor(gotFocus);
                    ClearStatusBarColor();
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "TabColoringManager: error processing window activation");
            }
        }

        // ────────────────────────────────────────────────────────────────────────────
        //  T027 — Status bar environment color
        // ────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Applies a semi-transparent environment color to the SSMS/VS main window status bar.
        /// Uses WPF visual tree walking to locate the status bar element.
        /// All operations are wrapped in try/catch for graceful degradation across SSMS versions.
        /// </summary>
        private static void ApplyStatusBarColor(Color envColor, string label)
        {
            try
            {
                // Skip if the color hasn't changed since last application.
                if (_lastStatusBarColor.HasValue && _lastStatusBarColor.Value == envColor)
                    return;

                var mainWindow = System.Windows.Application.Current?.MainWindow;
                if (mainWindow == null) return;

                var statusBarElement = FindStatusBar(mainWindow);
                if (statusBarElement == null)
                {
                    Log.Debug("TabColoringManager: status bar element not found in visual tree");
                    return;
                }

                // 60% opacity (alpha = 153) for status bar so underlying theme remains visible.
                var semiTransparent = Color.FromArgb(153, envColor.R, envColor.G, envColor.B);
                var brush = new SolidColorBrush(semiTransparent);
                brush.Freeze();

                statusBarElement.SetValue(Control.BackgroundProperty, brush);

                // Cache the element and color to avoid redundant walks.
                _statusBarElement = new WeakReference<FrameworkElement>(statusBarElement);
                _lastStatusBarColor = envColor;

                Log.Debug("TabColoringManager: applied status bar color {Color} for environment '{Label}'",
                    envColor, label);
            }
            catch (Exception ex)
            {
                // Graceful degradation — different SSMS/VS version may have different visual tree.
                Log.Debug(ex, "TabColoringManager: failed to apply status bar color");
            }
        }

        /// <summary>
        /// Clears any previously applied environment color from the status bar,
        /// restoring the default theme background.
        /// </summary>
        private static void ClearStatusBarColor()
        {
            try
            {
                if (_lastStatusBarColor == null)
                    return;

                FrameworkElement? element = null;
                if (_statusBarElement != null && _statusBarElement.TryGetTarget(out element) && element != null)
                {
                    // Reset to the default value, which causes the theme brush to take over.
                    element.ClearValue(Control.BackgroundProperty);
                }

                _lastStatusBarColor = null;
                _statusBarElement = null;

                Log.Debug("TabColoringManager: cleared status bar color");
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "TabColoringManager: failed to clear status bar color");
            }
        }

        /// <summary>
        /// Searches the WPF visual tree of the main window to locate the status bar.
        /// Tries multiple strategies in priority order to handle differences between
        /// SSMS 20, SSMS 21/22, VS 2019, VS 2022, and VS 2026.
        /// </summary>
        private static FrameworkElement? FindStatusBar(DependencyObject root)
        {
            try
            {
                // Strategy 1: Look for the standard WPF StatusBar control.
                var statusBar = FindDescendantByType<System.Windows.Controls.Primitives.StatusBar>(root);
                if (statusBar != null)
                    return statusBar;

                // Strategy 2: Search by common element names used in VS/SSMS shells.
                string[] knownNames =
                {
                    "StatusBarContainer",
                    "PART_StatusBar",
                    "StatusBar",
                    "StatusBarPanel",
                    "MainStatusBar"
                };
                foreach (var name in knownNames)
                {
                    var element = FindDescendantByName(root, name);
                    if (element != null)
                        return element;
                }

                // Strategy 3: Look for a DockPanel child docked at the bottom — common status bar pattern.
                var bottomDocked = FindBottomDockedElement(root);
                if (bottomDocked != null)
                    return bottomDocked;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "TabColoringManager: error during status bar search");
            }

            return null;
        }

        // ────────────────────────────────────────────────────────────────────────────
        //  T028 — Floating window environment border
        // ────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// If the active document window is undocked (floating), finds the hosting WPF
        /// <see cref="System.Windows.Window"/> and applies a solid 3px environment-colored border.
        /// </summary>
        private static void ApplyFloatingWindowBorder(Window dteWindow, Color envColor)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // A floating window in DTE has Linkable == false or is in its own frame.
                // The most reliable check: the window is not "linked" (i.e., it's floating).
                bool isFloating;
                try
                {
                    // In some SSMS versions, accessing Linkable can throw for certain window types.
                    isFloating = !dteWindow.Linkable;
                }
                catch
                {
                    // If Linkable is not accessible, try an alternative check.
                    isFloating = IsWindowFloating(dteWindow);
                }

                if (!isFloating)
                    return;

                // Find the WPF Window hosting this floating document.
                var wpfWindow = FindFloatingWpfWindow(dteWindow);
                if (wpfWindow == null) return;

                // Apply solid 3px border — no transparency for clear visual distinction.
                var borderBrush = new SolidColorBrush(envColor);
                borderBrush.Freeze();

                wpfWindow.BorderBrush = borderBrush;
                wpfWindow.BorderThickness = new Thickness(3);

                Log.Debug("TabColoringManager: applied floating window border for '{Caption}'", dteWindow.Caption);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "TabColoringManager: failed to apply floating window border");
            }
        }

        /// <summary>
        /// Attempts to determine whether a DTE window is floating using its <c>Left</c>/<c>Top</c>
        /// properties compared to the main window bounds. This is a fallback when <c>Linkable</c>
        /// is not accessible.
        /// </summary>
        private static bool IsWindowFloating(Window dteWindow)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                // A floating window typically has its own Top/Left outside the main client area,
                // or is not contained within the main window's document well.
                // The safest heuristic: if the window's LinkedWindowFrame is null or
                // the window is not in the main document group.
                var frame = dteWindow.LinkedWindowFrame;
                return frame != null && frame.LinkedWindows.Count <= 1;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Locates the WPF <see cref="System.Windows.Window"/> hosting a floating DTE document window.
        /// Enumerates all open WPF windows and matches by HWND or title.
        /// </summary>
        private static System.Windows.Window? FindFloatingWpfWindow(Window dteWindow)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                // Approach 1: Get the HWND from the DTE window and find the owning WPF Window.
                var hwnd = (IntPtr)dteWindow.HWnd;
                if (hwnd != IntPtr.Zero)
                {
                    var hwndSource = HwndSource.FromHwnd(hwnd);
                    if (hwndSource?.RootVisual is System.Windows.Window directWindow)
                        return directWindow;

                    // The DTE HWnd might be a child; walk up to find the top-level WPF Window.
                    if (hwndSource?.RootVisual is DependencyObject rootVisual)
                    {
                        var parentWindow = System.Windows.Window.GetWindow(rootVisual);
                        if (parentWindow != null && parentWindow != System.Windows.Application.Current?.MainWindow)
                            return parentWindow;
                    }
                }

                // Approach 2: Enumerate all WPF windows and find one whose title matches.
                var caption = dteWindow.Caption;
                if (!string.IsNullOrEmpty(caption))
                {
                    foreach (System.Windows.Window wpfWin in System.Windows.Application.Current.Windows)
                    {
                        try
                        {
                            if (wpfWin == System.Windows.Application.Current.MainWindow)
                                continue;

                            if (wpfWin.Title != null &&
                                wpfWin.Title.IndexOf(caption, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                return wpfWin;
                            }
                        }
                        catch
                        {
                            // Some windows may not be accessible — skip.
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "TabColoringManager: failed to find floating WPF window");
            }

            return null;
        }

        // ────────────────────────────────────────────────────────────────────────────
        //  WPF visual tree helpers
        // ────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Recursively searches the WPF visual tree for the first descendant of type <typeparamref name="T"/>.
        /// Returns <c>null</c> if not found. Wrapped in try/catch for safety.
        /// </summary>
        private static T? FindDescendantByType<T>(DependencyObject parent) where T : DependencyObject
        {
            try
            {
                int childCount = VisualTreeHelper.GetChildrenCount(parent);
                for (int i = 0; i < childCount; i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i);
                    if (child is T match)
                        return match;

                    var descendant = FindDescendantByType<T>(child);
                    if (descendant != null)
                        return descendant;
                }
            }
            catch
            {
                // Visual tree walking can fail for disconnected elements.
            }

            return null;
        }

        /// <summary>
        /// Recursively searches the WPF visual tree for a <see cref="FrameworkElement"/>
        /// whose <c>Name</c> matches <paramref name="name"/> (case-insensitive).
        /// Returns <c>null</c> if not found.
        /// </summary>
        private static FrameworkElement? FindDescendantByName(DependencyObject parent, string name)
        {
            try
            {
                int childCount = VisualTreeHelper.GetChildrenCount(parent);
                for (int i = 0; i < childCount; i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i);
                    if (child is FrameworkElement fe &&
                        string.Equals(fe.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return fe;
                    }

                    var descendant = FindDescendantByName(child, name);
                    if (descendant != null)
                        return descendant;
                }
            }
            catch
            {
                // Visual tree walking can fail for disconnected elements.
            }

            return null;
        }

        /// <summary>
        /// Searches for the first <see cref="FrameworkElement"/> that is docked at the bottom
        /// of a <see cref="DockPanel"/> — a common layout pattern for status bars.
        /// Only returns elements that look like status bars (height &lt; 40px).
        /// </summary>
        private static FrameworkElement? FindBottomDockedElement(DependencyObject parent)
        {
            try
            {
                int childCount = VisualTreeHelper.GetChildrenCount(parent);
                for (int i = 0; i < childCount; i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i);

                    if (child is FrameworkElement fe)
                    {
                        var dock = DockPanel.GetDock(fe);
                        if (dock == Dock.Bottom && fe.ActualHeight > 0 && fe.ActualHeight < 40)
                        {
                            return fe;
                        }
                    }

                    // Only recurse one level into DockPanels for performance.
                    if (child is DockPanel)
                    {
                        var result = FindBottomDockedElement(child);
                        if (result != null)
                            return result;
                    }
                }
            }
            catch
            {
                // Visual tree walking can fail for disconnected elements.
            }

            return null;
        }

        // ────────────────────────────────────────────────────────────────────────────
        //  Document tab discovery
        // ────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Maximum recursion depth for tab-discovery visual tree walks.
        /// Document tabs are typically within 15-20 levels of the root; capping at 30
        /// avoids runaway recursion in exotic host layouts.
        /// </summary>
        private const int MaxTabSearchDepth = 30;

        /// <summary>
        /// Finds the WPF <see cref="FrameworkElement"/> representing the document tab header
        /// for the given DTE <paramref name="dteWindow"/>. Walks the main window's visual tree
        /// looking for tab-like elements whose caption matches the window title.
        /// Returns <c>null</c> if no matching tab is found (graceful degradation).
        /// </summary>
        private static FrameworkElement? FindDocumentTab(Window dteWindow)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                var caption = dteWindow.Caption;
                if (string.IsNullOrEmpty(caption))
                    return null;

                var mainWindow = System.Windows.Application.Current?.MainWindow;
                if (mainWindow == null)
                    return null;

                return FindTabByCaption(mainWindow, caption, 0);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "TabColoringManager: error finding document tab");
                return null;
            }
        }

        /// <summary>
        /// Recursively walks the WPF visual tree starting from <paramref name="root"/>,
        /// looking for elements whose type name contains "TabItem" or "DocumentTab" and
        /// whose content matches <paramref name="caption"/>.
        /// <para>
        /// Uses runtime <see cref="Type.Name"/> checks instead of compile-time type references
        /// because the exact tab control types differ between VS 2019/2022/2026 and SSMS 20/21/22:
        /// <list type="bullet">
        ///   <item><c>DocumentTabItem</c> (VS 2022+, SSMS 21+)</item>
        ///   <item><c>TabItem</c> (VS 2019, SSMS 20)</item>
        /// </list>
        /// </para>
        /// </summary>
        private static FrameworkElement? FindTabByCaption(DependencyObject root, string caption, int depth)
        {
            if (depth > MaxTabSearchDepth)
                return null;

            try
            {
                int childCount = VisualTreeHelper.GetChildrenCount(root);
                for (int i = 0; i < childCount; i++)
                {
                    var child = VisualTreeHelper.GetChild(root, i);

                    if (child is FrameworkElement fe)
                    {
                        var typeName = fe.GetType().Name;

                        // Check for known VS/SSMS document tab type names.
                        bool isTabLike = typeName.IndexOf("TabItem", StringComparison.OrdinalIgnoreCase) >= 0
                                      || typeName.IndexOf("DocumentTab", StringComparison.OrdinalIgnoreCase) >= 0;

                        if (isTabLike && TabContainsCaption(fe, caption))
                        {
                            return fe;
                        }
                    }

                    // Recurse into children.
                    var result = FindTabByCaption(child, caption, depth + 1);
                    if (result != null)
                        return result;
                }
            }
            catch
            {
                // Visual tree walking can fail for disconnected or unloaded elements.
            }

            return null;
        }

        /// <summary>
        /// Determines whether a tab-like <paramref name="tabElement"/> displays text matching
        /// <paramref name="caption"/>. Checks the element's <c>Header</c> property (if it is
        /// a <see cref="HeaderedContentControl"/> or exposes a <c>Header</c> property via
        /// reflection) and falls back to searching descendant <see cref="TextBlock"/>s.
        /// <para>
        /// Uses <see cref="string.StartsWith(string, StringComparison)"/> because SSMS may
        /// truncate long tab titles with an ellipsis or suffix.
        /// </para>
        /// </summary>
        private static bool TabContainsCaption(FrameworkElement tabElement, string caption)
        {
            try
            {
                // Strategy 1: HeaderedContentControl.Header (TabItem inherits this).
                if (tabElement is HeaderedContentControl hcc && hcc.Header != null)
                {
                    var headerText = hcc.Header as string;
                    if (headerText == null && hcc.Header is FrameworkElement headerFe)
                    {
                        // The header might be a visual element containing a TextBlock.
                        if (HasMatchingTextBlock(headerFe, caption))
                            return true;
                    }
                    else if (headerText != null)
                    {
                        if (CaptionMatches(headerText, caption))
                            return true;
                    }

                    // Header.ToString() fallback for non-string, non-visual headers.
                    if (headerText == null)
                    {
                        var toString = hcc.Header.ToString();
                        if (toString != null && CaptionMatches(toString, caption))
                            return true;
                    }
                }

                // Strategy 2: Reflection-based Header property for non-standard tab controls
                // (e.g. VS PlatformUI DocumentTabItem that may not inherit HeaderedContentControl).
                var headerProp = tabElement.GetType().GetProperty("Header");
                if (headerProp != null)
                {
                    var headerVal = headerProp.GetValue(tabElement);
                    if (headerVal is string hs && CaptionMatches(hs, caption))
                        return true;
                    if (headerVal is FrameworkElement hfe && HasMatchingTextBlock(hfe, caption))
                        return true;
                    if (headerVal != null)
                    {
                        var ts = headerVal.ToString();
                        if (ts != null && CaptionMatches(ts, caption))
                            return true;
                    }
                }

                // Strategy 3: Search descendant TextBlocks within the tab element itself.
                if (HasMatchingTextBlock(tabElement, caption))
                    return true;
            }
            catch
            {
                // Graceful degradation for different SSMS/VS visual tree layouts.
            }

            return false;
        }

        /// <summary>
        /// Recursively searches for <see cref="TextBlock"/> children of <paramref name="parent"/>
        /// whose <see cref="TextBlock.Text"/> matches <paramref name="caption"/>.
        /// Limited to 10 levels of depth to avoid excessive recursion within a single tab element.
        /// </summary>
        private static bool HasMatchingTextBlock(DependencyObject parent, string caption, int depth = 0)
        {
            if (depth > 10)
                return false;

            try
            {
                int childCount = VisualTreeHelper.GetChildrenCount(parent);
                for (int i = 0; i < childCount; i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i);

                    if (child is TextBlock tb && tb.Text != null && CaptionMatches(tb.Text, caption))
                        return true;

                    if (HasMatchingTextBlock(child, caption, depth + 1))
                        return true;
                }
            }
            catch
            {
                // Visual tree walking can fail for disconnected elements.
            }

            return false;
        }

        /// <summary>
        /// Compares a tab's display text against the expected DTE window caption.
        /// Uses <see cref="string.StartsWith(string, StringComparison)"/> because SSMS
        /// may truncate long captions (e.g. appending "..." or omitting suffixes), and
        /// also checks whether the caption starts with the tab text for the reverse case.
        /// </summary>
        private static bool CaptionMatches(string tabText, string caption)
        {
            return tabText.StartsWith(caption, StringComparison.OrdinalIgnoreCase)
                || caption.StartsWith(tabText, StringComparison.OrdinalIgnoreCase);
        }

        // ────────────────────────────────────────────────────────────────────────────
        //  Color parsing
        // ────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Parses a hex colour string (e.g. <c>"#FF4444"</c>) into a WPF <see cref="Color"/>.
        /// Returns <c>null</c> if parsing fails.
        /// </summary>
        private static Color? ParseHexColor(string? hex)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex))
                    return null;

                if (!hex!.StartsWith("#", StringComparison.Ordinal))
                    hex = "#" + hex;

                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch
            {
                return null;
            }
        }

        // ────────────────────────────────────────────────────────────────────────────
        //  Original tab coloring (existing functionality)
        // ────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Attempts to retrieve the connected SQL Server instance name from the active
        /// document's connection context.
        /// </summary>
        /// <remarks>
        /// In SSMS, the connection context is typically available through the document's
        /// <c>IVsWindowFrame</c> properties or via SSMS-specific service interfaces.
        /// This is a best-effort extraction that works across VS and SSMS hosts.
        /// </remarks>
        private static string? GetActiveServerName(Window window)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // Approach 1: Try to get the server name from the window caption.
                // SSMS window captions are typically in the format: "Query - ServerName.DatabaseName"
                // or "SQLQuery1.sql - ServerName.DatabaseName - UserName"
                var caption = window.Caption;
                if (!string.IsNullOrEmpty(caption))
                {
                    var serverName = ParseServerNameFromCaption(caption);
                    if (!string.IsNullOrEmpty(serverName))
                        return serverName;
                }

                // TODO: Approach 2: SSMS-specific connection context retrieval.
                // In SSMS, connection info can be obtained through:
                //   - IVsWindowFrame -> GetProperty(WindowFrameProperties) for SSMS connection data
                //   - ScriptFactory.GetCurrentlyActiveFrameInfo() (SSMS internal API)
                //   - UIConnectionInfo from the SSMS ServiceProvider
                // These APIs vary between SSMS 20/21/22 and require SSMS-specific assembly references.
                // Implementation will be completed when SSMS connection service integration is available.

                // TODO: Approach 3: For VS (non-SSMS), check SQL Server Object Explorer connection
                // context via IVsDataConnection or similar VS data services.
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "TabColoringManager: could not extract server name from window");
            }

            return null;
        }

        /// <summary>
        /// Parses a server name from an SSMS-style window caption.
        /// Expected formats:
        /// <list type="bullet">
        ///   <item><c>"SQLQuery1.sql - SERVERNAME.master - sa"</c></item>
        ///   <item><c>"Query - SERVERNAME.DatabaseName"</c></item>
        ///   <item><c>"SERVERNAME.DatabaseName - SQLQuery1.sql"</c></item>
        /// </list>
        /// </summary>
        private static string? ParseServerNameFromCaption(string caption)
        {
            if (string.IsNullOrEmpty(caption))
                return null;

            // SSMS typically uses " - " as a delimiter in captions.
            var parts = caption.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                var trimmed = part.Trim();

                // Look for segments containing a dot that look like "ServerName.DatabaseName".
                // Skip segments that end with common file extensions.
                if (trimmed.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    continue;

                var dotIndex = trimmed.IndexOf('.');
                if (dotIndex > 0 && dotIndex < trimmed.Length - 1)
                {
                    // Extract the part before the first dot as the server name.
                    // For "SERVER.database.windows.net.master", we want "SERVER.database.windows.net"
                    // For "SERVERNAME.master", we want "SERVERNAME"
                    // Heuristic: if the segment after the last dot looks like a database name
                    // (doesn't contain dots itself after splitting), take everything before the last dot.
                    var lastDot = trimmed.LastIndexOf('.');
                    var possibleServer = trimmed.Substring(0, lastDot);
                    if (!string.IsNullOrWhiteSpace(possibleServer))
                        return possibleServer;
                }
            }

            return null;
        }

        /// <summary>
        /// Applies the environment rule's colour to the document tab header via WPF visual tree walking.
        /// </summary>
        private static void ApplyTabColor(Window window, EnvironmentRule rule)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var settings = ConfigManager.Load();
                var brush = CreateBrushFromHex(rule.Color, settings.Tabs.GradientColors);
                if (brush == null) return;

                var tabElement = FindDocumentTab(window);
                if (tabElement == null)
                {
                    Log.Debug("TabColoringManager: tab element not found for '{Caption}'", window.Caption);
                    return;
                }

                tabElement.SetValue(Control.BackgroundProperty, brush);
                Log.Debug("TabColoringManager: applied color {Color} ({Label}) to tab for '{Caption}'",
                    rule.Color, rule.Label, window.Caption);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "TabColoringManager: failed to apply tab color");
            }
        }

        /// <summary>
        /// Removes any previously applied environment colour from a document tab.
        /// </summary>
        private static void ClearTabColor(Window window)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var tabElement = FindDocumentTab(window);
                if (tabElement == null) return;

                tabElement.ClearValue(Control.BackgroundProperty);
                Log.Debug("TabColoringManager: cleared tab color for '{Caption}'", window.Caption);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "TabColoringManager: failed to clear tab color");
            }
        }

        /// <summary>
        /// Converts a hex colour string (e.g. <c>"#FF4444"</c>) to a frozen <see cref="Brush"/>.
        /// When <paramref name="gradient"/> is true, returns a vertical <see cref="LinearGradientBrush"/>
        /// from a lighter tint at the top to the base colour at the bottom.
        /// Returns <c>null</c> if parsing fails.
        /// </summary>
        private static Brush? CreateBrushFromHex(string hex, bool gradient = false)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex))
                    return null;

                // Ensure the hex string starts with '#'.
                if (!hex.StartsWith("#", StringComparison.Ordinal))
                    hex = "#" + hex;

                var color = (Color)ColorConverter.ConvertFromString(hex);

                // Use semi-transparent for tab backgrounds so text remains readable.
                var baseColor = Color.FromArgb(60, color.R, color.G, color.B);

                Brush brush;
                if (gradient)
                {
                    // Lighter tint at top (20% toward white), base color at bottom
                    var lighter = Color.FromArgb(
                        baseColor.A,
                        (byte)Math.Min(baseColor.R + (255 - baseColor.R) / 5, 255),
                        (byte)Math.Min(baseColor.G + (255 - baseColor.G) / 5, 255),
                        (byte)Math.Min(baseColor.B + (255 - baseColor.B) / 5, 255));
                    brush = new LinearGradientBrush(lighter, baseColor, 90.0); // top to bottom
                }
                else
                {
                    brush = new SolidColorBrush(baseColor);
                }

                brush.Freeze(); // Thread-safe after freezing.
                return brush;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "TabColoringManager: invalid hex color '{Hex}'", hex);
                return null;
            }
        }
    }
}
