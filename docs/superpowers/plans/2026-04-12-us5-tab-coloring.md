# US5: Environment-Based Tab Coloring — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the tab coloring feature so query tabs are visually marked by environment (Production/Staging/Dev), with a rules editor in Settings, live re-render on changes, and the actual WPF tab-header coloring that is currently a TODO stub.

**Architecture:** The existing infrastructure handles detection (`EnvironmentDetector`), event subscription (`TabColoringManager`), server name extraction, status bar coloring, floating window borders, gradient brush creation, and config persistence. The gaps are: (1) the actual WPF visual tree walk to color document tab headers (`ApplyTabColor`/`ClearTabColor` are TODO stubs), (2) a rules editor UI in Settings so users can add/edit/remove coloring rules without hand-editing JSON, (3) live re-render when settings change (currently requires restart), and (4) tests for the resolution logic.

**Tech Stack:** C# / .NET Framework 4.7.2 (shell), WPF visual tree manipulation, VS SDK `IVsWindowFrame`, xUnit (tests)

---

## Existing Infrastructure (already working)

| Component | File | Status |
|-----------|------|--------|
| `EnvironmentDetector` | `src/AkmlSql.Shell.Shared/Tabs/EnvironmentDetector.cs` | Complete — glob matching, sorted rules |
| `EnvironmentRule` | `src/AkmlSql.Core/Models/Tabs/EnvironmentRule.cs` | Complete — immutable POCO |
| `ColoringRule` | `src/AkmlSql.Core/Config/AppSettings.cs:457-473` | Complete — 4 default rules |
| `TabSettings` | `src/AkmlSql.Core/Config/AppSettings.cs:414-454` | Complete — all toggles |
| `TabColoringManager.Initialize` | `src/AkmlSql.Shell.Shared/Tabs/TabColoringManager.cs:53-87` | Complete — DTE event subscription |
| `OnWindowActivated` | Same file, lines 94-155 | Complete — detection + dispatch |
| `GetActiveServerName` | Same file, lines 552-586 | Partial — caption parsing works, SSMS API TODO |
| `ApplyStatusBarColor` | Same file, lines 166-203 | Complete |
| `ApplyFloatingWindowBorder` | Same file, lines 285-325 | Complete |
| `CreateBrushFromHex` | Same file, lines 698-738 | Complete — gradient + freeze |
| `ParseHexColor` | Same file, lines 521-537 | Complete |
| Visual tree helpers | Same file, lines 417-511 | Complete |
| Settings UI toggles | `SettingsWindow.cs` BuildTabsPage | Complete — enable + gradient toggles |
| `TabManagementInitializer` | `src/AkmlSql.Shell.Shared/Tabs/TabManagementInitializer.cs` | Complete |

## What This Plan Builds

1. **ApplyTabColor / ClearTabColor** — the WPF visual tree walk to find and color document tab headers
2. **Rules editor** in SettingsWindow — add/edit/remove `ColoringRule` entries with color picker
3. **Live re-render** — `EnvironmentDetector.Reload()` + repaint all open tabs on settings save
4. **Tests** — `EnvironmentDetectorTests` for glob matching, priority, edge cases

---

### Task 1: Add EnvironmentDetector unit tests

**Files:**
- Create: `tests/AkmlSql.Core.Tests/Tabs/EnvironmentDetectorTests.cs`

These tests exercise the glob matching and priority resolution logic that already exists in `EnvironmentDetector`. The detector is static with an `Initialize()` that reads config, so we'll test via `Match()` after initializing with known config.

- [ ] **Step 1: Create test file with glob matching tests**

```csharp
using AkmlSql.Core.Config;
using Xunit;

namespace AkmlSql.Core.Tests.Tabs
{
    public class EnvironmentDetectorTests
    {
        /// <summary>
        /// Helper: writes a temporary config.json with the given rules, calls
        /// EnvironmentDetector.Initialize(), then restores the original config.
        /// Since EnvironmentDetector is static and reads from ConfigManager.Load(),
        /// we test through the public Match() API after initialization.
        /// </summary>

        [Theory]
        [InlineData("SQLPROD01", "PRODUCTION")]       // *PROD* matches
        [InlineData("LIVE-SERVER", "PRODUCTION")]      // *LIVE* matches
        [InlineData("STG-SQL", "STAGING")]             // *STG* matches
        [InlineData("UAT-DB", "STAGING")]              // *UAT* matches
        [InlineData("DEV-SQL01", "DEV")]               // *DEV* matches
        [InlineData("localhost", "DEV")]                // exact match
        [InlineData("(local)", "DEV")]                  // exact match
        [InlineData("myserver.database.windows.net", "AZURE")] // suffix match
        public void Match_DefaultRules_ReturnsExpectedLabel(string serverName, string expectedLabel)
        {
            // Default rules are loaded from config.json defaults via ColoringRule list.
            // EnvironmentDetector.Initialize() must have been called.
            // For unit tests, we test the static GlobMatch logic directly.
            // Since EnvironmentDetector is internal to Shell.Shared, we test via
            // the resolution logic extracted into a testable helper (see Step 3).
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("UNKNOWN-SERVER")]
        [InlineData("my-custom-server")]
        public void Match_NoMatchingRule_ReturnsNull(string? serverName)
        {
            // No rule should match these server names.
        }

        [Fact]
        public void Match_PriorityOrder_LowestOrderWins()
        {
            // If a server matches both "*PROD*" (order 0) and "*DEV*" (order 2),
            // the lower order should win.
            // Server "PRODDEV" matches both — order 0 (PRODUCTION) should win.
        }
    }
}
```

Since `EnvironmentDetector` is `internal static` in the Shell.Shared project (not directly testable from Core.Tests), we need to extract the matching logic into a testable Core class.

- [ ] **Step 2: Extract `EnvironmentMatcher` into Core for testability**

Create `src/AkmlSql.Core/Models/Tabs/EnvironmentMatcher.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace AkmlSql.Core.Models.Tabs
{
    /// <summary>
    /// Pure matching logic for environment coloring rules. Extracted from the
    /// shell-side <c>EnvironmentDetector</c> so it can be unit-tested without
    /// VS SDK dependencies.
    /// </summary>
    public static class EnvironmentMatcher
    {
        /// <summary>
        /// Tests <paramref name="serverName"/> against each rule in order.
        /// Returns the first matching rule, or null if none match.
        /// Rules must be pre-sorted by <see cref="EnvironmentRule.Order"/> ascending.
        /// </summary>
        public static EnvironmentRule? Match(IReadOnlyList<EnvironmentRule> rules, string? serverName)
        {
            if (string.IsNullOrWhiteSpace(serverName) || rules == null)
                return null;

            foreach (var rule in rules)
            {
                if (!string.Equals(rule.MatchTarget, "serverName", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (MatchesPattern(rule.Pattern, serverName!))
                    return rule;
            }

            return null;
        }

        /// <summary>
        /// Splits a comma-separated pattern string into sub-patterns and returns true
        /// if any sub-pattern matches the value. Supports * at start/end for glob matching.
        /// </summary>
        public static bool MatchesPattern(string pattern, string value)
        {
            if (string.IsNullOrEmpty(pattern))
                return false;

            var subPatterns = pattern.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var raw in subPatterns)
            {
                var sub = raw.Trim();
                if (sub.Length == 0) continue;
                if (GlobMatch(sub, value))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Simple glob matcher: * at start and/or end. Case-insensitive.
        /// </summary>
        public static bool GlobMatch(string glob, string value)
        {
            bool startsWithWild = glob.StartsWith("*", StringComparison.Ordinal);
            bool endsWithWild = glob.EndsWith("*", StringComparison.Ordinal);

            string core = glob;
            if (startsWithWild) core = core.Substring(1);
            if (endsWithWild && core.Length > 0) core = core.Substring(0, core.Length - 1);

            if (core.Length == 0) return true; // "*" matches everything

            if (startsWithWild && endsWithWild)
                return value.IndexOf(core, StringComparison.OrdinalIgnoreCase) >= 0;
            if (startsWithWild)
                return value.EndsWith(core, StringComparison.OrdinalIgnoreCase);
            if (endsWithWild)
                return value.StartsWith(core, StringComparison.OrdinalIgnoreCase);

            return string.Equals(core, value, StringComparison.OrdinalIgnoreCase);
        }
    }
}
```

- [ ] **Step 3: Update `EnvironmentDetector` to delegate to `EnvironmentMatcher`**

In `src/AkmlSql.Shell.Shared/Tabs/EnvironmentDetector.cs`, replace the private `MatchesPattern`/`GlobMatch` methods with calls to `EnvironmentMatcher`:

```csharp
// In Match():
public static EnvironmentRule? Match(string? serverName)
{
    return EnvironmentMatcher.Match(_rules, serverName);
}

// Remove the private MatchesPattern() and GlobMatch() methods entirely.
```

- [ ] **Step 4: Write complete EnvironmentMatcher tests**

Replace the placeholder test class with real tests in `tests/AkmlSql.Core.Tests/Tabs/EnvironmentDetectorTests.cs`:

```csharp
using System.Collections.Generic;
using AkmlSql.Core.Models.Tabs;
using Xunit;

namespace AkmlSql.Core.Tests.Tabs
{
    public class EnvironmentMatcherTests
    {
        private static readonly List<EnvironmentRule> DefaultRules = new()
        {
            new(0, "*PROD*,*LIVE*", "serverName", "#FF4444", "PRODUCTION"),
            new(1, "*STG*,*UAT*,*STAGING*", "serverName", "#FFB800", "STAGING"),
            new(2, "*DEV*,*LOCAL*,localhost,(local)", "serverName", "#44BB44", "DEV"),
            new(3, "*.database.windows.net", "serverName", "#4488FF", "AZURE"),
        };

        [Theory]
        [InlineData("SQLPROD01", "PRODUCTION")]
        [InlineData("LIVE-SERVER", "PRODUCTION")]
        [InlineData("prod-sql.corp.net", "PRODUCTION")]
        [InlineData("STG-SQL", "STAGING")]
        [InlineData("UAT-DB", "STAGING")]
        [InlineData("DEV-SQL01", "DEV")]
        [InlineData("localhost", "DEV")]
        [InlineData("(local)", "DEV")]
        [InlineData("myserver.database.windows.net", "AZURE")]
        public void Match_DefaultRules_ReturnsExpectedLabel(string server, string expectedLabel)
        {
            var result = EnvironmentMatcher.Match(DefaultRules, server);
            Assert.NotNull(result);
            Assert.Equal(expectedLabel, result!.Label);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("UNKNOWN-SERVER")]
        [InlineData("my-custom-server")]
        public void Match_NoMatchingRule_ReturnsNull(string? server)
        {
            var result = EnvironmentMatcher.Match(DefaultRules, server);
            Assert.Null(result);
        }

        [Fact]
        public void Match_PriorityOrder_LowestOrderWins()
        {
            // "PRODDEV" matches both *PROD* (order 0) and *DEV* (order 2)
            var result = EnvironmentMatcher.Match(DefaultRules, "PRODDEV");
            Assert.NotNull(result);
            Assert.Equal("PRODUCTION", result!.Label);
        }

        [Fact]
        public void Match_EmptyRules_ReturnsNull()
        {
            var result = EnvironmentMatcher.Match(new List<EnvironmentRule>(), "PROD-SQL");
            Assert.Null(result);
        }

        [Theory]
        [InlineData("*PROD*", "SQLPROD01", true)]
        [InlineData("*PROD*", "prod-sql", true)]      // case-insensitive
        [InlineData("*.database.windows.net", "x.database.windows.net", true)]
        [InlineData("DEV*", "DEV-SQL", true)]
        [InlineData("DEV*", "PRODUCTION", false)]
        [InlineData("localhost", "localhost", true)]    // exact match
        [InlineData("localhost", "LOCALHOST", true)]    // case-insensitive exact
        [InlineData("localhost", "localhost2", false)]
        [InlineData("*", "anything", true)]             // wildcard-only
        public void GlobMatch_VariousPatterns(string glob, string value, bool expected)
        {
            Assert.Equal(expected, EnvironmentMatcher.GlobMatch(glob, value));
        }

        [Theory]
        [InlineData("*PROD*,*LIVE*", "SQLPROD01", true)]
        [InlineData("*PROD*,*LIVE*", "LIVE-SERVER", true)]
        [InlineData("*PROD*,*LIVE*", "DEV-SQL", false)]
        [InlineData("localhost,(local)", "(local)", true)]
        public void MatchesPattern_CommaDelimited(string pattern, string value, bool expected)
        {
            Assert.Equal(expected, EnvironmentMatcher.MatchesPattern(pattern, value));
        }
    }
}
```

- [ ] **Step 5: Run tests**

Run: `dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj --filter "FullyQualifiedName~EnvironmentMatcher" -v minimal`
Expected: All tests pass.

---

### Task 2: Implement ApplyTabColor via WPF visual tree walk

**Files:**
- Modify: `src/AkmlSql.Shell.Shared/Tabs/TabColoringManager.cs` (lines 636-691 — the TODO stubs)

This is the core gap. We need to find the WPF `TabItem` (or equivalent) hosting the document window and set its `Background` property.

- [ ] **Step 1: Add tab header discovery helper**

Add a private method `FindDocumentTab` that locates the WPF element for a given DTE `Window`:

```csharp
/// <summary>
/// Finds the WPF tab header element for a DTE document window by walking the
/// main window's visual tree and matching on the window caption.
/// Returns null if the element cannot be found (graceful degradation).
/// </summary>
private static FrameworkElement? FindDocumentTab(Window dteWindow)
{
    try
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var mainWindow = System.Windows.Application.Current?.MainWindow;
        if (mainWindow == null) return null;

        var caption = dteWindow.Caption;
        if (string.IsNullOrEmpty(caption)) return null;

        // VS/SSMS document tabs are typically TabItem elements whose Header
        // (or a child TextBlock) contains the document caption text.
        // We search for elements whose type name contains "TabItem" or
        // "DocumentTabItem" (varies by VS/SSMS version).
        return FindTabByCaption(mainWindow, caption);
    }
    catch (Exception ex)
    {
        Log.Debug(ex, "TabColoringManager: failed to find document tab for '{Caption}'",
            dteWindow.Caption);
        return null;
    }
}

/// <summary>
/// Walks the visual tree looking for a tab-like element whose content text
/// matches <paramref name="caption"/>. Checks both the element's type name
/// (for "TabItem"/"DocumentTabItem") and its descendant TextBlocks.
/// </summary>
private static FrameworkElement? FindTabByCaption(DependencyObject root, string caption)
{
    try
    {
        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is FrameworkElement fe)
            {
                var typeName = fe.GetType().Name;

                // Match VS/SSMS tab item types
                if (typeName.Contains("TabItem") || typeName.Contains("DocumentTab"))
                {
                    // Check if this tab's content/header matches the caption
                    if (TabContainsCaption(fe, caption))
                        return fe;
                }
            }

            // Recurse — but limit depth to avoid performance issues
            var result = FindTabByCaption(child, caption);
            if (result != null)
                return result;
        }
    }
    catch
    {
        // Visual tree walking can fail for disconnected elements
    }

    return null;
}

/// <summary>
/// Checks whether a tab element contains a TextBlock whose Text matches
/// the given caption (prefix match to handle truncated tab titles).
/// </summary>
private static bool TabContainsCaption(FrameworkElement tabElement, string caption)
{
    try
    {
        // Check direct Header property (ContentControl/HeaderedContentControl)
        if (tabElement is HeaderedContentControl hcc)
        {
            var header = hcc.Header?.ToString();
            if (header != null && caption.StartsWith(header, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Search descendant TextBlocks for matching text
        return HasMatchingTextBlock(tabElement, caption);
    }
    catch
    {
        return false;
    }
}

private static bool HasMatchingTextBlock(DependencyObject parent, string caption)
{
    try
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBlock tb && !string.IsNullOrEmpty(tb.Text))
            {
                if (caption.StartsWith(tb.Text, StringComparison.OrdinalIgnoreCase) ||
                    tb.Text.StartsWith(caption, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            if (HasMatchingTextBlock(child, caption))
                return true;
        }
    }
    catch { }
    return false;
}
```

- [ ] **Step 2: Implement ApplyTabColor**

Replace the TODO stub at lines 636-672:

```csharp
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
```

- [ ] **Step 3: Implement ClearTabColor**

Replace the TODO stub at lines 677-690:

```csharp
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
```

- [ ] **Step 4: Build verification**

Run:
```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"
"$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Build -p:Configuration=Release -v:minimal
```
Expected: 0 errors

---

### Task 3: Add live re-render on settings change

**Files:**
- Modify: `src/AkmlSql.Shell.Shared/Tabs/EnvironmentDetector.cs`
- Modify: `src/AkmlSql.Shell.Shared/Tabs/TabColoringManager.cs`

Currently `EnvironmentDetector.Initialize()` loads rules once at startup. We need a `Reload()` method and a way for `TabColoringManager` to repaint all open tabs after settings are saved.

- [ ] **Step 1: Add `Reload()` to EnvironmentDetector**

In `src/AkmlSql.Shell.Shared/Tabs/EnvironmentDetector.cs`, add after `Initialize()`:

```csharp
/// <summary>
/// Reloads coloring rules from the current config. Call after the user saves
/// settings to pick up changes without restarting the IDE.
/// Thread-safe: the new rule array is assigned atomically.
/// </summary>
public static void Reload()
{
    Initialize(); // Same logic — loads from config and replaces _rules
}
```

- [ ] **Step 2: Add `RepaintAllTabs()` to TabColoringManager**

In `src/AkmlSql.Shell.Shared/Tabs/TabColoringManager.cs`, add a public method:

```csharp
/// <summary>
/// Reloads environment rules and repaints all open document tabs.
/// Call from the UI thread after settings are saved (FR-042).
/// </summary>
public static void RepaintAllTabs()
{
    ThreadHelper.ThrowIfNotOnUIThread();

    try
    {
        // Reload rules from updated config
        EnvironmentDetector.Reload();

        if (_dte == null) return;

        var settings = ConfigManager.Load();

        // Re-evaluate every open document window
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

        // Also update the status bar for the currently active window
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

        Log.Information("TabColoringManager: repainted all tabs after settings change");
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "TabColoringManager: failed to repaint all tabs");
    }
}
```

- [ ] **Step 3: Call RepaintAllTabs from SettingsWindow save path**

In `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs`, find the save method (the method that calls `ConfigManager.Save(_settings)`) and add after the save call:

```csharp
// Live re-render tab colors after settings change (FR-042)
try { Tabs.TabColoringManager.RepaintAllTabs(); } catch { }
```

- [ ] **Step 4: Also handle initialization when coloring was disabled at startup but enabled later**

In `TabColoringManager.RepaintAllTabs()`, add at the top before the `if (_dte == null)` check:

```csharp
// If coloring was disabled at startup, _dte won't be set.
// Try to initialize now if the user just enabled it.
if (!_initialized)
{
    var pkg = GetCachedPackage();
    if (pkg != null)
        Initialize(pkg);
}
```

And add a static field to cache the package reference in `Initialize()`:

```csharp
private static WeakReference<AsyncPackage>? _packageRef;

// In Initialize(), after _dte assignment:
_packageRef = new WeakReference<AsyncPackage>(package);

// Helper:
private static AsyncPackage? GetCachedPackage()
{
    AsyncPackage? pkg = null;
    _packageRef?.TryGetTarget(out pkg);
    return pkg;
}
```

- [ ] **Step 5: Build verification**

Run:
```bash
"$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Build -p:Configuration=Release -v:minimal
```
Expected: 0 errors

---

### Task 4: Add coloring rules editor to SettingsWindow

**Files:**
- Modify: `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs`

Add a rules list with Add/Edit/Remove buttons to the existing Tabs page so users can manage `ColoringRule` entries without editing JSON.

- [ ] **Step 1: Add UI fields for the rules editor**

In the field declarations section (around line 248), add:

```csharp
private ListBox? _lstColoringRules;
private Button? _btnAddRule;
private Button? _btnEditRule;
private Button? _btnRemoveRule;
```

- [ ] **Step 2: Extend BuildTabsPage with rules list**

In `BuildTabsPage()` (after the gradient toggle, around line 1441), add:

```csharp
AddGroupSeparator(panel);
AddGroupHeader(panel, "Environment Rules");
AddDescription(panel, "Define server name patterns to match environments. Rules are evaluated top-down; first match wins.");

// Rules ListBox
_lstColoringRules = new ListBox
{
    Height = 120,
    Margin = new Thickness(20, 4, 20, 4),
    Background = Freeze(new SolidColorBrush(ThemeManager.Instance.EditorPanelBackground)),
    Foreground = Freeze(new SolidColorBrush(ThemeManager.Instance.Foreground)),
    BorderBrush = Freeze(new SolidColorBrush(ThemeManager.Instance.Border)),
    BorderThickness = new Thickness(1),
    FontFamily = SegoeUiFont,
    FontSize = 13,
};
panel.Children.Add(_lstColoringRules);

// Button row
var buttonRow = new StackPanel
{
    Orientation = Orientation.Horizontal,
    Margin = new Thickness(20, 4, 20, 4),
    HorizontalAlignment = HorizontalAlignment.Left
};

_btnAddRule = CreateSmallButton("Add...");
_btnEditRule = CreateSmallButton("Edit...");
_btnRemoveRule = CreateSmallButton("Remove");

_btnAddRule.Click += (s, e) => OnAddColoringRule();
_btnEditRule.Click += (s, e) => OnEditColoringRule();
_btnRemoveRule.Click += (s, e) => OnRemoveColoringRule();

buttonRow.Children.Add(_btnAddRule);
buttonRow.Children.Add(_btnEditRule);
buttonRow.Children.Add(_btnRemoveRule);
panel.Children.Add(buttonRow);
```

- [ ] **Step 3: Add rule display and CRUD methods**

```csharp
private void PopulateColoringRulesList()
{
    if (_lstColoringRules == null) return;
    _lstColoringRules.Items.Clear();

    foreach (var rule in _settings.Tabs.ColoringRules)
    {
        _lstColoringRules.Items.Add(FormatRuleDisplay(rule));
    }
}

private static string FormatRuleDisplay(ColoringRule rule)
{
    return $"[{rule.Label}]  {rule.Pattern}  →  {rule.Color}";
}

private void OnAddColoringRule()
{
    var rule = new ColoringRule
    {
        Order = _settings.Tabs.ColoringRules.Count,
        MatchTarget = "serverName"
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
```

- [ ] **Step 4: Create inline rule editor dialog**

Add a private method that shows a simple modal dialog for editing a `ColoringRule`:

```csharp
/// <summary>
/// Shows a small modal dialog to edit one ColoringRule.
/// Returns true if the user clicked OK, false if cancelled.
/// </summary>
private bool ShowRuleEditor(ColoringRule rule, string title)
{
    var dlg = new System.Windows.Window
    {
        Title = title,
        Width = 400,
        Height = 280,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        ResizeMode = ResizeMode.NoResize,
        Background = Freeze(new SolidColorBrush(ThemeManager.Instance.Background)),
    };

    // Try to attach to VS/SSMS main window
    try
    {
        var mainHwnd = (IntPtr)_dte?.MainWindow?.HWnd;
        if (mainHwnd != IntPtr.Zero)
            new WindowInteropHelper(dlg).Owner = mainHwnd;
    }
    catch { }

    var grid = new Grid { Margin = new Thickness(16) };
    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

    var fg = Freeze(new SolidColorBrush(ThemeManager.Instance.Foreground));

    // Label
    var lblLabel = new TextBlock { Text = "Label:", Foreground = fg, VerticalAlignment = VerticalAlignment.Center };
    Grid.SetRow(lblLabel, 0); Grid.SetColumn(lblLabel, 0);
    var txtLabel = new TextBox { Text = rule.Label, Margin = new Thickness(0, 4, 0, 4) };
    Grid.SetRow(txtLabel, 0); Grid.SetColumn(txtLabel, 1);

    // Pattern
    var lblPattern = new TextBlock { Text = "Pattern:", Foreground = fg, VerticalAlignment = VerticalAlignment.Center };
    Grid.SetRow(lblPattern, 1); Grid.SetColumn(lblPattern, 0);
    var txtPattern = new TextBox { Text = rule.Pattern, Margin = new Thickness(0, 4, 0, 4) };
    Grid.SetRow(txtPattern, 1); Grid.SetColumn(txtPattern, 1);

    // Color
    var lblColor = new TextBlock { Text = "Color:", Foreground = fg, VerticalAlignment = VerticalAlignment.Center };
    Grid.SetRow(lblColor, 2); Grid.SetColumn(lblColor, 0);
    var colorPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
    var txtColor = new TextBox { Text = rule.Color, Width = 100 };
    var colorPreview = new Border
    {
        Width = 24, Height = 24, Margin = new Thickness(8, 0, 0, 0),
        CornerRadius = new CornerRadius(2),
        BorderBrush = Freeze(new SolidColorBrush(ThemeManager.Instance.Border)),
        BorderThickness = new Thickness(1)
    };
    UpdateColorPreview(colorPreview, txtColor.Text);
    txtColor.TextChanged += (s, e) => UpdateColorPreview(colorPreview, txtColor.Text);
    colorPanel.Children.Add(txtColor);
    colorPanel.Children.Add(colorPreview);
    Grid.SetRow(colorPanel, 2); Grid.SetColumn(colorPanel, 1);

    // Buttons
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
    Grid.SetRow(buttonPanel, 4); Grid.SetColumn(buttonPanel, 0); Grid.SetColumnSpan(buttonPanel, 2);

    grid.Children.Add(lblLabel); grid.Children.Add(txtLabel);
    grid.Children.Add(lblPattern); grid.Children.Add(txtPattern);
    grid.Children.Add(lblColor); grid.Children.Add(colorPanel);
    grid.Children.Add(buttonPanel);

    dlg.Content = grid;
    dlg.ShowDialog();

    return accepted;
}

private static void UpdateColorPreview(Border preview, string hex)
{
    try
    {
        if (string.IsNullOrWhiteSpace(hex)) { preview.Background = null; return; }
        if (!hex.StartsWith("#")) hex = "#" + hex;
        var color = (Color)ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        preview.Background = brush;
    }
    catch
    {
        preview.Background = null;
    }
}
```

- [ ] **Step 5: Wire PopulateColoringRulesList into the load path**

In the method that calls `SetChecked(_chkTabColoringEnabled, ...)` (around line 2609), add:

```csharp
PopulateColoringRulesList();
```

- [ ] **Step 6: Wire rules list save into the save path**

No extra code needed — the rules are mutated in-place on `_settings.Tabs.ColoringRules`, so `ConfigManager.Save(_settings)` persists them automatically.

- [ ] **Step 7: Add CreateSmallButton helper if not already present**

Check if `CreateSmallButton` exists; if not, add:

```csharp
private static Button CreateSmallButton(string text)
{
    return new Button
    {
        Content = text,
        Padding = new Thickness(12, 4, 12, 4),
        Margin = new Thickness(0, 0, 8, 0),
        FontSize = 12,
    };
}
```

- [ ] **Step 8: Build verification**

Run:
```bash
"$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Build -p:Configuration=Release -v:minimal
```
Expected: 0 errors

---

### Task 5: Run full test suites and build all targets

**Files:** None (verification only)

- [ ] **Step 1: Run Core tests**

Run: `dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj -v minimal`
Expected: All pass (469 baseline + new EnvironmentMatcher tests)

- [ ] **Step 2: Build SSMS 22 (primary target)**

Run:
```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"
"$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Build -p:Configuration=Release -v:minimal
```
Expected: 0 errors

- [ ] **Step 3: Build Engine**

Run: `dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release`
Expected: 0 errors

---

## Scope Decisions

| Included | Excluded (deferred) |
|----------|-------------------|
| Tab header coloring via visual tree walk | Tab context menu right-click (T038) — needs `.vsct` changes across 6 hosts |
| Rules editor in Settings | WCAG-AA auto-contrast (FR-046) — edge case, deferred to polish pass |
| Live re-render on settings save | Registered Server Group inheritance (FR-045) — needs SSMS-specific API |
| Gradient brush support | EnvironmentPaletteWindow as separate window (T042) — rules editor in SettingsWindow is sufficient |
| EnvironmentMatcher unit tests | Database-scope and ServerGroup-scope assignments — requires the Assignment data model |

The excluded items are P3-level polish that can be added incrementally. The core value (tabs colored by environment, editable rules, live update) ships with this plan.
