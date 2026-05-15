# Options Dialog Phase 1 — Bug Fix + Tree Restructure + Tests

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the light-theme submenu invisibility bug, restructure the navigation tree to match SQL Prompt parity (B layout), and stand up the first WPF/Shell.Shared test project.

**Architecture:** Surgical changes to `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs` only. One-line cause-fix for the bug (replace `ItemContainerStyle` with implicit type-style in `Resources`). Tree restructure is label-only — `AppSettings` schema unchanged, no migration. New `tests/AkmlSql.Shell.Shared.Tests` project provides STA-thread WPF render tests via `Xunit.StaFact`.

**Tech Stack:** .NET Framework 4.7.2, WPF code-only (no XAML), xunit 2.x, Xunit.StaFact for STA dispatcher tests, Shared Project (.projitems) reference into the test project.

**Spec:** `docs/superpowers/specs/2026-05-06-options-dialog-redgate-parity-design.md` §6.

**Branching:** This plan executes on a fresh branch `017-options-dialog-phase1` off `master` (or off whatever `016-*` lands as). Do NOT execute on the current `016-wpf-theme-refresh` branch — it carries unrelated uncommitted Phase 6 follow-up work.

**Prerequisites the executor must verify before starting:**
1. Working tree is clean (or all uncommitted work is on a separate branch).
2. `dotnet --list-sdks` shows .NET 10 SDK installed (for tests project) — verify with `dotnet --list-sdks`.
3. MSBuild path is `/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe`.
4. The user's Git rule applies: NEVER stage/commit/push without explicit user approval. Each task ends with a "Commit" step that *prepares the commit message* — pause and ask the user for approval before actually running `git commit`.

---

## Pre-flight: Confirm scope is still relevant

Before executing, verify the codebase state matches what this plan assumes. The plan was written 2026-05-06; if executed later, things may have moved.

- [ ] **Step 0.1: Read `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs:513-540`**

Verify the buggy code still exists at those lines — specifically:
- Line 520 has `_navTree.Resources[SystemColors.ControlTextBrushKey] = _theme.SelectedText;`
- Line 540 has `_navTree.ItemContainerStyle = itemStyle;`

If the lines have shifted (because of unrelated edits), grep for the strings instead of trusting the line numbers:

```bash
grep -n "ControlTextBrushKey.*=.*_theme.SelectedText" src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs
grep -n "_navTree.ItemContainerStyle = itemStyle" src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs
```

If either string is missing, **stop and ask the user** — the bug may have been fixed already or the file restructured.

- [ ] **Step 0.2: Verify the per-page polish that the spec lists is already implemented**

These should already exist. If any are missing, this plan needs revising:
- `AddPageHeader` includes a "Restore Defaults" link (search `restoreLink.MouseLeftButtonUp` in `SettingsWindow.cs`)
- `OnResetThisPageClick` exists and switches on page name
- Bottom button bar includes "Restore All Defaults", "Import…", "Export…" (search `btnResetAll`, `btnImport`, `btnExport`)
- `OnImportProfileClick` / `OnExportProfileClick` perform JSON serialization round-trip

If all confirmed, proceed. If any missing, escalate to the user.

---

## Task 1: Apply core bug fix (one-line change)

**Files:**
- Modify: `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs:540`

- [ ] **Step 1.1: Apply the minimal fix**

In `SettingsWindow.cs`, locate line 540:

```csharp
_navTree.ItemContainerStyle = itemStyle;
```

Replace with:

```csharp
// Use implicit style by type so the style cascades to TreeViewItems at every depth.
// (TreeView.ItemContainerStyle only applies to direct children, breaking nested items.)
_navTree.Resources[typeof(TreeViewItem)] = itemStyle;
```

- [ ] **Step 1.2: Build the SSMS 22 extension to validate the change compiles**

Run from repository root:

```bash
"/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe" \
  "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" \
  -t:Build -p:Configuration=Release -v:minimal
```

Expected: Build succeeds with 0 errors. Warnings about obsolete `ThemeManager` properties are pre-existing and OK.

If build fails: re-read the change site, ensure the line replacement is exact (no stray characters).

- [ ] **Step 1.3: Manual verification in light theme**

This step requires running SSMS 22 with the rebuilt extension. The executor must:

1. Deploy the build output to SSMS extension path: `C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\AkmlSql\` (clear MEF cache: delete `%LocalAppData%\Microsoft\SSMS\22.0_*\ComponentModelCache\`).
2. Launch SSMS 22.
3. Tools → AKML SQL → Options.
4. If the dialog opens in dark theme, change Theme dropdown to "Light" and click OK; reopen Options.
5. Verify in light theme: every TreeViewItem (Suggestions/Behavior, Suggestions/Database, Inserted Code/Refactoring, Format/Styles, Queries/History, Queries/Execution Warnings, Queries/Query Results, Queries/Execution, Tabs/Color, Editor/Productivity, Editor/Navigation) shows readable text.

If any item is still invisible: **stop and report**. The fix may be incomplete; consult §6.1 of the spec for the cause chain.

- [ ] **Step 1.4: Manual verification in dark theme (no regression)**

Reopen the Options dialog in dark theme. Verify:
1. Tree text is still readable.
2. Selected item highlighting (blue accent) still works.
3. Hover state still works.
4. Page content still renders correctly (zebra striping, headers, etc.).

- [ ] **Step 1.5: Prepare commit**

Stage files (do NOT actually run `git commit` yet — pause for user approval):

```bash
git add src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs
```

Suggested commit message:

```
Fix light-theme submenu invisibility in Options dialog

Cause: TreeView.ItemContainerStyle applies only to direct children, so
nested TreeViewItems (e.g. Behavior, Database under Suggestions) missed
the style cascade. They fell back to the overridden ControlTextBrushKey
resource, which was set to the white "selected" foreground — invisible
on the white sidebar in light theme.

Fix: replace ItemContainerStyle with an implicit by-type style in
TreeView.Resources, which cascades to every TreeViewItem at any depth.

Refs: docs/superpowers/specs/2026-05-06-options-dialog-redgate-parity-design.md §6.1
```

**Ask the user:** "The bug fix is staged. Approve commit?"

---

## Task 2: Drop redundant resource overrides (defense-in-depth)

**Files:**
- Modify: `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs:519-520`

- [ ] **Step 2.1: Remove the now-unused resource overrides**

In `SettingsWindow.cs`, locate lines 519-520:

```csharp
_navTree.Resources[SystemColors.ControlBrushKey] = _theme.Selected;
_navTree.Resources[SystemColors.ControlTextBrushKey] = _theme.SelectedText;
```

Delete these two lines. With the by-type style cascade in place (Task 1), nothing inside `_navTree` falls back through `ControlBrushKey`/`ControlTextBrushKey`. Keeping the overrides would silently bite any future code that adds an unstyled control inside the tree.

The remaining resource overrides on lines 515-518 (Highlight*) are still needed — they handle focus-loss highlight rendering which the implicit style doesn't cover.

- [ ] **Step 2.2: Build and manually re-verify both themes**

Re-run the build command from Task 1.2 and re-verify both themes per Steps 1.3 and 1.4. Behavior should be identical to Task 1's verification — these overrides were redundant once the cascade works.

- [ ] **Step 2.3: Prepare commit**

```bash
git add src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs
```

Suggested message:

```
Remove redundant ControlBrushKey/ControlTextBrushKey overrides

These resource overrides were forcing the system control-text brush to
white inside the navigation tree. They are no longer needed now that
TreeViewItems are styled via an implicit by-type style; nothing falls
back through these resources. Removing them prevents future unstyled
controls inside the tree from inheriting the selected-state colors.

Refs: docs/superpowers/specs/2026-05-06-options-dialog-redgate-parity-design.md §6.1
```

**Ask the user:** "Defense-in-depth cleanup is staged. Approve commit?"

---

## Task 3: Audit for the same pattern in other dialogs

**Files:**
- Read-only audit across `src/AkmlSql.Shell.Shared/`

- [ ] **Step 3.1: Grep for the same bug pattern**

Run from repository root:

```bash
grep -rn "SystemColors.ControlTextBrushKey" src/AkmlSql.Shell.Shared/
```

Expected occurrences to investigate:
- `src/AkmlSql.Shell.Shared/History/HistoryDiffWindow.cs` — Window with theming
- `src/AkmlSql.Shell.Shared/Safety/SafetyWarningDialog.cs` — Modal dialog with theming
- Any others returned by the grep

For *each* result:

- [ ] **Step 3.2: Inspect the surrounding code**

Read 30 lines around the match. The pattern is:

```csharp
container.Resources[SystemColors.ControlTextBrushKey] = someBrush;
```

If the brush is something like `_theme.SelectedText` (or any non-text foreground brush) AND there are nested controls, you have the same bug. If the brush is the actual text foreground (e.g., `_theme.FgPrimary`), it's fine.

- [ ] **Step 3.3: Apply matching fix to any genuine occurrences**

If found, apply the same Task 1-style fix:
1. Use implicit by-type style in `Resources` for the relevant control type.
2. Remove the `ControlTextBrushKey` override.
3. Manually verify the affected dialog in light theme.

If no genuine occurrences are found, document that in the commit message and proceed.

- [ ] **Step 3.4: Prepare commit (if any fixes applied) or skip**

If you fixed other dialogs:

```bash
git add <each-file-fixed>
```

Suggested message (adjust for actual files):

```
Audit and fix matching ControlTextBrushKey override pattern in <Dialog>

Same root cause as SettingsWindow tree fix: a Resources override of
ControlTextBrushKey forces white foreground on unstyled descendants.
Replaced with implicit by-type style; removed the redundant override.

Refs: docs/superpowers/specs/2026-05-06-options-dialog-redgate-parity-design.md §6.1 (Audit)
```

**Ask the user:** "Audit fixes staged. Approve commit?"

If no fixes were needed, proceed to Task 4.

---

## Task 4: Tree restructure — relabel and move Refactoring

**Files:**
- Modify: `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs:549-576` (tree builder calls)
- Modify: `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs:1098-1115` (`BuildPages` display labels)
- Modify: `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs:2600-2621` (`OnResetThisPageClick` switch)

- [ ] **Step 4.1: Rewrite `BuildPages` display labels**

Locate `BuildPages()` at line 1095. Replace the `pages` array (lines 1098-1115) with the new SQL Prompt parity labels:

```csharp
var pages = new (string Key, string Display, Func<UIElement> Builder)[]
{
    ("General",       "Miscellaneous › Main",         BuildGeneralPage),
    ("IntelliSense",  "Suggestions › Behavior",       BuildIntelliSensePage),
    ("Schema Cache",  "Suggestions › Database",       BuildSchemaCachePage),
    ("Formatting",    "Format › Styles",              BuildFormattingPage),
    ("Snippets",      "Snippets",                     BuildSnippetsPage),
    ("Code Analysis", "Code Analysis",                BuildCodeAnalysisPage),
    ("Refactoring",   "Editor › Refactoring",         BuildRefactoringPage),
    ("History",       "Queries › History",            BuildHistoryPage),
    ("Tabs & UI",     "Tabs › Color",                 BuildTabsPage),
    ("Safety",        "Queries › Execution Warnings", BuildSafetyPage),
    ("AI Assistance", "AI Assistance",                BuildAiPage),
    ("Grid",          "Queries › Query Results",      BuildGridPage),
    ("Editor",        "Editor › Productivity",        BuildEditorPage),
    ("Execution",     "Queries › Execution",          BuildExecutionPage),
    ("Navigation",    "Editor › Navigation",          BuildNavigationPage),
};
```

The page **keys** (first column) are unchanged — `OnResetThisPageClick` still works against them. Only the **display labels** (breadcrumb shown in the page header) and the **tree placement** change.

- [ ] **Step 4.2: Rewrite tree-building calls (lines 549-576)**

Replace the existing `AddTreeGroup` / `AddTreeLeaf` block (lines 549-576) with the new structure:

```csharp
AddTreeGroup("Suggestions", expanded: true,
    ("Behavior", "IntelliSense"),
    ("Database", "Schema Cache"));

// "Inserted Code" group is reserved for Phase 2 (Qualification, INSERT, JOIN).
// Phase 1: empty group is hidden — do not add it yet, to avoid showing an
// empty parent node.

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
```

- [ ] **Step 4.3: Verify `OnResetThisPageClick` switch still covers all keys**

Open `OnResetThisPageClick` at line 2583. The switch statement (lines 2600-2621) reads page keys from the selected TreeViewItem's Tag. The Tags carry the *page key* (e.g. "IntelliSense", "Refactoring"), not the display label. Since we did not rename any keys, no changes are needed here.

To prove this: the switch has cases for `"General"`, `"IntelliSense"`, `"Schema Cache"`, `"Formatting"`, `"Snippets"`, `"Code Analysis"`, `"Refactoring"`, `"History"`, `"Tabs & UI"`, `"Safety"`, `"Grid"`, `"Editor"`, `"Execution"`, `"Navigation"`, `"AI Assistance"`. All 15 page keys. No change needed.

If any case is missing for a key still in `pages` (Step 4.1) — add it. (Currently they all match.)

- [ ] **Step 4.4: Build**

```bash
"/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe" \
  "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" \
  -t:Build -p:Configuration=Release -v:minimal
```

Expected: 0 errors.

- [ ] **Step 4.5: Manual verification of new tree structure**

Deploy and launch. Verify:
1. Tree shows: Suggestions (Behavior, Database) | Format (Styles) | Editor (Productivity, Navigation, Refactoring) | Queries (History, Execution Warnings, Query Results, Execution) | Tabs (Color) | Code Analysis | Snippets | AI Assistance | Miscellaneous (Main).
2. There is NO empty "Inserted Code" group visible (Phase 2 will populate it).
3. There is NO orphan "Labs" leaf (Phase 2 adds it).
4. Click each leaf — page renders, page header breadcrumb matches the new label (e.g. "Editor › Refactoring").
5. Click "Restore Defaults" link on a few pages — confirms the dialog, resets that page only.

- [ ] **Step 4.6: Prepare commit**

```bash
git add src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs
```

Suggested message:

```
Restructure Options dialog tree to SQL Prompt parity (Option B)

- Move Refactoring from "Inserted Code" to "Editor" group (better fit:
  refactoring is a transformation tool, not auto-completion)
- Group Editor sub-pages (Productivity, Navigation, Refactoring) under
  a single Editor parent
- Rename leaf "Tabs (Color)" to "Tabs › Color" via display labels
- Promote "Miscellaneous" to a group with "Main" sub-leaf (Phase 2 adds Labs)
- Drop the empty "Inserted Code" group from the tree until Phase 2
  populates it with Qualification & Brackets / INSERT / JOIN

Page keys are unchanged — config.json schema unaffected, Restore handler
still works.

Refs: docs/superpowers/specs/2026-05-06-options-dialog-redgate-parity-design.md §4, §6.3
```

**Ask the user:** "Tree restructure staged. Approve commit?"

---

## Task 5: Sidebar width tune

**Files:**
- Modify: `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs:473`

- [ ] **Step 5.1: Bump sidebar width**

In `CreateSidebar` (line 469), change line 473:

```csharp
Width = 220,
```

to:

```csharp
Width = 240,  // wider to give long labels like "Execution Warnings" room
```

- [ ] **Step 5.2: Build, deploy, manually verify**

Re-run build (Task 1.2 command). Reopen Options dialog. Verify:
1. Sidebar visibly wider; long labels like "Execution Warnings" no longer truncate or feel cramped.
2. Content panel still has enough room — no horizontal scrollbar.
3. The dialog overall doesn't get wider — only the sidebar/content split shifts.

- [ ] **Step 5.3: Prepare commit**

```bash
git add src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs
```

Suggested message:

```
Widen Options dialog sidebar from 220 to 240px

Long labels like "Execution Warnings" felt cramped at 220px. SQL Prompt
mockup uses ~240px. No effect on the content panel — the dialog total
width is unchanged.

Refs: docs/superpowers/specs/2026-05-06-options-dialog-redgate-parity-design.md §6.2
```

**Ask the user:** "Sidebar width tune staged. Approve commit?"

---

## Task 6: Create AkmlSql.Shell.Shared.Tests project

**Files:**
- Create: `tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj`
- Create: `tests/AkmlSql.Shell.Shared.Tests/Properties/AssemblyInfo.cs`
- Modify: `AKML-SQL.slnx`

This task sets up the WPF test project. It's the largest single chunk of Phase 1; budget ~30-60 minutes including any package-resolution surprises.

- [ ] **Step 6.1: Create the test project directory and csproj file**

Create `tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj` with this content:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <UseWPF>true</UseWPF>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <RootNamespace>AkmlSql.Shell.Shared.Tests</RootNamespace>
    <AssemblyName>AkmlSql.Shell.Shared.Tests</AssemblyName>
    <Platforms>AnyCPU;x64</Platforms>
  </PropertyGroup>

  <ItemGroup>
    <!-- Test framework -->
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="Xunit.StaFact" Version="1.1.11" />
  </ItemGroup>

  <ItemGroup>
    <!-- Compile Shell.Shared sources directly into the test assembly via the shared projitems -->
    <Import Project="..\..\src\AkmlSql.Shell.Shared\AkmlSql.Shell.Shared.projitems" Label="Shared" />
  </ItemGroup>

  <ItemGroup>
    <!-- Reference Core for AppSettings -->
    <ProjectReference Include="..\..\src\AkmlSql.Core\AkmlSql.Core.csproj" />
  </ItemGroup>

</Project>
```

**Note:** the `<Import>` for the .projitems must be inside an `<ItemGroup>` per Visual Studio's shared-project convention. Existing shell extensions (e.g. `src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj`) demonstrate the correct import shape. If this fails to resolve `_theme` or `ThemeBrushSet`, fix by matching the import shape from `AkmlSql.Ssms22.csproj` exactly.

- [ ] **Step 6.2: Verify package versions match the existing test project**

Read `tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj` and check the versions of `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`. Use the same versions in the new csproj for consistency. (If they differ from the values written above, override.)

- [ ] **Step 6.3: Add the new test project to the solution**

Edit `AKML-SQL.slnx`. In the `<Folder Name="/tests/">` section, add:

```xml
<Project Path="tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj" />
```

The full `tests` folder section after the change:

```xml
  <Folder Name="/tests/">
    <Project Path="tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj" />
    <Project Path="tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj" />
    <Project Path="tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj" />
    <Project Path="tests/AkmlSql.E2E.Tests/AkmlSql.E2E.Tests.csproj" />
    <Project Path="tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj" />
  </Folder>
```

- [ ] **Step 6.4: Restore packages and build the empty test project**

```bash
dotnet restore tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj
"/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe" \
  tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj \
  -t:Build -p:Configuration=Debug -v:minimal
```

Expected: build succeeds with 0 errors. There may be many warnings about types in Shell.Shared that reference VS SDK — those are expected because the test project doesn't reference a specific VS SDK version.

If build fails because Shell.Shared sources need a VS SDK reference at compile time:

```xml
<!-- Add this ItemGroup if needed -->
<ItemGroup>
  <PackageReference Include="Microsoft.VisualStudio.Shell.Framework" Version="17.13.40008" />
  <PackageReference Include="Microsoft.VisualStudio.Shell.15.0" Version="17.13.40008" />
</ItemGroup>
```

(Versions: match what `src/AkmlSql.VS2022/AkmlSql.VS2022.csproj` references — these versions can rot, so always cross-check with VS2022 csproj before committing.)

- [ ] **Step 6.5: Add a placeholder smoke test to confirm `[StaFact]` runs**

Create `tests/AkmlSql.Shell.Shared.Tests/SmokeTests.cs`:

```csharp
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    public class SmokeTests
    {
        [StaFact]
        public void StaFact_Runs_OnSTAThread()
        {
            // STAFact ensures this test runs on a single-threaded apartment thread,
            // which WPF UI tests require. If this asserts, the test infrastructure works.
            Assert.Equal(System.Threading.ApartmentState.STA, System.Threading.Thread.CurrentThread.GetApartmentState());
        }
    }
}
```

- [ ] **Step 6.6: Run the smoke test**

```bash
dotnet test tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj --filter "FullyQualifiedName~SmokeTests"
```

Expected: `Passed!  - Failed: 0, Passed: 1, Skipped: 0`.

If the test fails because `Xunit.StaFact` isn't producing an STA thread, package version may be too new/old. Check NuGet for a stable version that matches xunit 2.9.x.

- [ ] **Step 6.7: Prepare commit**

```bash
git add AKML-SQL.slnx tests/AkmlSql.Shell.Shared.Tests/
```

Suggested message:

```
Add AkmlSql.Shell.Shared.Tests project for WPF chrome tests

First test project targeting net472+WPF for AKML. Imports the shared
project sources directly via .projitems and uses Xunit.StaFact for
STA-thread dispatcher tests required by WPF.

A smoke test verifies the STA dispatcher infrastructure works before
real chrome tests are added in subsequent commits.

Refs: docs/superpowers/specs/2026-05-06-options-dialog-redgate-parity-design.md §5.2, §6.4
```

**Ask the user:** "Test project setup staged. Approve commit?"

---

## Task 7: Write `WindowChromeTests.TreeViewItems_AllVisibleInLightTheme`

**Files:**
- Create: `tests/AkmlSql.Shell.Shared.Tests/WindowChromeTests.cs`

This test is the regression safety net for the bug fixed in Task 1. It must FAIL if Task 1's fix is reverted.

- [ ] **Step 7.1: Write the test**

Create `tests/AkmlSql.Shell.Shared.Tests/WindowChromeTests.cs`:

```csharp
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Dialogs;
using AkmlSql.Shell.Shared.Ui.Theme;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    public class WindowChromeTests
    {
        [StaFact]
        public void TreeViewItems_AllVisibleInLightTheme()
        {
            ThemeRegistry.Instance.SetPreference("light");
            var settings = new AppSettings { Theme = "Light" };
            var window = new SettingsWindow(settings);

            // Build the dialog without showing it. Reflection is the simplest path
            // since SettingsWindow doesn't currently expose its internal Window.
            var built = TestAccessor.BuildDialog(window);

            var sidebarBg = ((SolidColorBrush)ThemePalette.Light.Brushes[ThemeTokens.SurfaceSidebar]).Color;

            var treeViewItems = new List<TreeViewItem>();
            CollectTreeViewItems(built, treeViewItems);

            Assert.NotEmpty(treeViewItems);

            foreach (var item in treeViewItems)
            {
                var fg = item.Foreground as SolidColorBrush;
                Assert.NotNull(fg);
                Assert.NotEqual(sidebarBg, fg!.Color);
            }
        }

        private static void CollectTreeViewItems(DependencyObject root, List<TreeViewItem> sink)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is TreeViewItem tvi)
                {
                    sink.Add(tvi);
                }
                CollectTreeViewItems(child, sink);
            }
        }
    }
}
```

**Note:** `SettingsWindow.BuildDialog` (or whatever the constructor calls) is internal. The test needs access to the built window. `TestAccessor` is a helper added in the next step.

- [ ] **Step 7.2: Add `TestAccessor` shim for internals access**

The test needs to instantiate `SettingsWindow` and force visual-tree realization without calling `ShowDialog`. Two options:

**Option A (preferred):** Add `[InternalsVisibleTo]` and a test hook.

In `src/AkmlSql.Shell.Shared/AkmlSql.Shell.Shared.shproj` (or, since it's a Shared Project, in *every* host extension's csproj), `[InternalsVisibleTo]` doesn't work cleanly because the shared sources compile into different assemblies per host.

**Option B (preferred for shared projects):** Add a public test method to `SettingsWindow` itself, gated by `#if DEBUG` or named with a `Test` prefix to make intent clear.

In `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs`, add this method near the bottom of the class (before the closing brace):

```csharp
        /// <summary>
        /// Test-only: builds the dialog's visual tree without showing it. Used by
        /// AkmlSql.Shell.Shared.Tests to render-check chrome.
        /// </summary>
        public Window TestBuildWindowForRenderTest()
        {
            // Reuse whatever the real Show() path uses to build _window, but skip
            // ShowDialog. The exact body depends on the existing Show() implementation —
            // copy the build phase verbatim, omit the modal call.
            BuildPages();
            // ... mirror whatever Show() does to populate _window, minus ShowDialog.
            return _window!;
        }
```

The executor MUST inspect the existing `ShowDialog()` method to copy its build phase faithfully. If `_window` is built lazily inside `ShowDialog`, factor that build phase into a private helper and call it from both `ShowDialog` and `TestBuildWindowForRenderTest`.

Then in the test file, replace `TestAccessor.BuildDialog(window)` with `window.TestBuildWindowForRenderTest()`.

If this turns out to require non-trivial restructuring of `ShowDialog`, **stop and ask the user**. We may want to defer the chrome tests to Phase 2 when the file is being split anyway.

- [ ] **Step 7.3: Run the test**

```bash
dotnet test tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj \
  --filter "FullyQualifiedName~TreeViewItems_AllVisibleInLightTheme"
```

Expected: PASS. If it fails, check:
1. Visual tree was actually realized (the dialog must be Measure/Arrange-ed at minimum).
2. The light palette is being read (verify `ThemeRegistry.Instance.SetPreference("light")` actually flips the palette).
3. The TreeViewItems are being collected (assert `treeViewItems.Count >= 14` — there should be at least 14 leaf nodes in the new tree).

- [ ] **Step 7.4: Manual regression verification — temporarily revert Task 1 to confirm the test catches the bug**

Temporarily change line 540 back to `_navTree.ItemContainerStyle = itemStyle;` and re-run the test. It MUST fail. If it passes, the test is broken — investigate before proceeding.

After confirming, restore the fix.

- [ ] **Step 7.5: Prepare commit**

```bash
git add tests/AkmlSql.Shell.Shared.Tests/WindowChromeTests.cs
git add src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs   # if Step 7.2 modified it
```

Suggested message:

```
Add light-theme TreeViewItem visibility regression test

WindowChromeTests.TreeViewItems_AllVisibleInLightTheme renders the
dialog against the light palette and asserts every TreeViewItem's
Foreground.Color differs from the sidebar background. Catches the
original cause (Foreground falls back to white in the light theme)
and any future style-cascade regressions.

Verified by temporarily reverting the bug fix — the test fails as
expected.

Refs: docs/superpowers/specs/2026-05-06-options-dialog-redgate-parity-design.md §6.4
```

**Ask the user:** "Light-theme regression test staged. Approve commit?"

---

## Task 8: Write `WindowChromeTests.TreeViewItems_AllVisibleInDarkTheme`

**Files:**
- Modify: `tests/AkmlSql.Shell.Shared.Tests/WindowChromeTests.cs`

- [ ] **Step 8.1: Add the dark-theme test**

In `WindowChromeTests.cs`, add a second test method beside the light-theme one:

```csharp
        [StaFact]
        public void TreeViewItems_AllVisibleInDarkTheme()
        {
            ThemeRegistry.Instance.SetPreference("dark");
            var settings = new AppSettings { Theme = "Dark" };
            var window = new SettingsWindow(settings);
            var built = window.TestBuildWindowForRenderTest();

            var sidebarBg = ((SolidColorBrush)ThemePalette.Dark.Brushes[ThemeTokens.SurfaceSidebar]).Color;

            var treeViewItems = new List<TreeViewItem>();
            CollectTreeViewItems(built, treeViewItems);

            Assert.NotEmpty(treeViewItems);
            foreach (var item in treeViewItems)
            {
                var fg = item.Foreground as SolidColorBrush;
                Assert.NotNull(fg);
                Assert.NotEqual(sidebarBg, fg!.Color);
            }
        }
```

- [ ] **Step 8.2: Run both tests**

```bash
dotnet test tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj \
  --filter "FullyQualifiedName~TreeViewItems"
```

Expected: 2 passed.

- [ ] **Step 8.3: Prepare commit**

```bash
git add tests/AkmlSql.Shell.Shared.Tests/WindowChromeTests.cs
```

Suggested message:

```
Add dark-theme TreeViewItem visibility test

Mirror of the light-theme test for the dark palette. Confirms the fix
doesn't regress dark theme either.

Refs: docs/superpowers/specs/2026-05-06-options-dialog-redgate-parity-design.md §6.4
```

**Ask the user:** "Dark-theme test staged. Approve commit?"

---

## Task 9: Write `WindowChromeTests.PageHeader_HasRestoreLink`

**Files:**
- Modify: `tests/AkmlSql.Shell.Shared.Tests/WindowChromeTests.cs`

- [ ] **Step 9.1: Add the page-header test**

This test asserts every page registers a "Restore Defaults" link in its header. Per the spec acceptance criterion §6.5(2).

```csharp
        [StaFact]
        public void PageHeader_HasRestoreLink_ForEveryPage()
        {
            ThemeRegistry.Instance.SetPreference("dark");  // theme doesn't matter for this test
            var settings = new AppSettings();
            var window = new SettingsWindow(settings);
            var built = window.TestBuildWindowForRenderTest();

            // The dialog has 15 pages registered by BuildPages. Each page panel
            // should contain a TextBlock with text "Restore Defaults".
            // Force visual realization for every page by simulating selection.
            var navTree = FindByName<TreeView>(built, "_navTree");
            Assert.NotNull(navTree);

            var leafItems = new List<TreeViewItem>();
            CollectLeafTreeViewItems(navTree!, leafItems);
            Assert.True(leafItems.Count >= 14, $"Expected ≥14 leaf items, found {leafItems.Count}");

            int pagesChecked = 0;
            foreach (var leaf in leafItems)
            {
                leaf.IsSelected = true;
                // Allow WPF to dispatch the selection-changed event and render the page.
                System.Windows.Threading.Dispatcher.CurrentDispatcher
                    .Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);

                var contentRoot = built.Content as DependencyObject;
                Assert.NotNull(contentRoot);

                bool found = false;
                foreach (var tb in EnumerateTextBlocks(contentRoot!))
                {
                    if (tb.Text == "Restore Defaults")
                    {
                        found = true;
                        break;
                    }
                }
                Assert.True(found, $"Page '{leaf.Header}' missing Restore Defaults link");
                pagesChecked++;
            }
            Assert.True(pagesChecked >= 14);
        }

        private static T? FindByName<T>(DependencyObject root, string name) where T : FrameworkElement
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T fe && fe.Name == name) return fe;
                var nested = FindByName<T>(child, name);
                if (nested != null) return nested;
            }
            return null;
        }

        private static void CollectLeafTreeViewItems(DependencyObject root, List<TreeViewItem> sink)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is TreeViewItem tvi && tvi.Items.Count == 0)
                {
                    sink.Add(tvi);
                }
                CollectLeafTreeViewItems(child, sink);
            }
        }

        private static IEnumerable<TextBlock> EnumerateTextBlocks(DependencyObject root)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is TextBlock tb) yield return tb;
                foreach (var nested in EnumerateTextBlocks(child)) yield return nested;
            }
        }
```

**Note:** `_navTree` is a private field — `FindByName` will not find it because WPF `FrameworkElement.Name` is set via `x:Name` or `Name` property assignment, neither of which is set on `_navTree`. The executor must instead either:

1. Add a `public TreeView TestNavTree => _navTree!;` property guarded by a comment that it's test-only, OR
2. Walk the visual tree looking for the *first* `TreeView` (there's only one in this dialog).

Option 2 is preferred (no API changes):

```csharp
private static TreeView? FirstTreeView(DependencyObject root)
{
    int count = VisualTreeHelper.GetChildrenCount(root);
    for (int i = 0; i < count; i++)
    {
        var child = VisualTreeHelper.GetChild(root, i);
        if (child is TreeView tv) return tv;
        var nested = FirstTreeView(child);
        if (nested != null) return nested;
    }
    return null;
}
```

Replace the `FindByName<TreeView>(built, "_navTree")` call with `FirstTreeView(built)`.

- [ ] **Step 9.2: Run all chrome tests**

```bash
dotnet test tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj
```

Expected: 4 passed (1 smoke + 3 chrome).

- [ ] **Step 9.3: Prepare commit**

```bash
git add tests/AkmlSql.Shell.Shared.Tests/WindowChromeTests.cs
```

Suggested message:

```
Add page-header restore-link presence test

Walks every leaf TreeViewItem, simulates selection, and asserts the
content panel contains a "Restore Defaults" TextBlock. Catches future
regressions where someone adds a new page builder but forgets to call
AddPageHeader.

Refs: docs/superpowers/specs/2026-05-06-options-dialog-redgate-parity-design.md §6.4, §6.5
```

**Ask the user:** "Page-header test staged. Approve commit?"

---

## Task 10: Phase 1 acceptance — full verification + screenshots

**Files:**
- None modified; this task is verification only.

- [ ] **Step 10.1: Run the full test suite**

```bash
dotnet test
```

Expected: all existing tests pass + the 4 new chrome tests pass.

- [ ] **Step 10.2: Build the SSMS 22 extension and deploy**

Build:

```bash
"/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe" \
  "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" \
  -t:Build -p:Configuration=Release -v:minimal
```

Deploy: copy build output to `C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\AkmlSql\`.

Clear MEF cache: delete `%LocalAppData%\Microsoft\SSMS\22.0_*\ComponentModelCache\`.

- [ ] **Step 10.3: Capture screenshots for the PR**

Launch SSMS 22, open Tools → AKML SQL → Options.

1. **Light theme — full tree expanded.** Expand all groups (Suggestions, Format, Editor, Queries, Tabs, Miscellaneous). Click "Suggestions › Behavior". Take screenshot.
2. **Dark theme — full tree expanded.** Switch theme via the General page → Theme dropdown. Reopen dialog. Repeat. Take screenshot.
3. **Editor › Refactoring page.** Click into it. Confirm header reads "Editor › Refactoring" and the page renders correctly. Take screenshot.

Save screenshots to `docs/superpowers/plans/2026-05-06-options-dialog-phase1-evidence/` (create the directory). Name them `phase1-light.png`, `phase1-dark.png`, `phase1-refactoring.png`.

- [ ] **Step 10.4: Verify against acceptance criteria from spec §6.5**

Cross-check:
1. ✅ Every TreeViewItem at every depth shows its label clearly in both themes (chrome tests + manual screenshots).
2. ✅ Visual reads at parity with `doc/SQL-PROMPT/SQL-Prompt-Option/13_options_dialog.svg` — title accent + restore link + group caps + zebra rows + sidebar spacing.
3. ✅ Grep audit done (Task 3); any other `ControlTextBrushKey` overrides in `src/AkmlSql.Shell.Shared/` either justified or fixed.
4. ✅ Dark theme: navigate every page, settings load and save correctly (Task 10.2 deploy + manual click-through).

If any criterion is unmet, **stop and report**.

- [ ] **Step 10.5: Final commit (evidence + plan completion marker)**

```bash
git add docs/superpowers/plans/2026-05-06-options-dialog-phase1-evidence/
```

Suggested message:

```
Phase 1 acceptance evidence: light/dark theme screenshots + Refactoring page

All Phase 1 acceptance criteria from spec §6.5 verified:
- TreeViewItems visible at all depths in both themes (regression tests +
  manual screenshots).
- Visual chrome at parity with SQL Prompt mockup.
- ControlTextBrushKey audit clean across Shell.Shared.
- No dark-theme regression.

Phase 1 complete. Phase 2 plan (page split + new pages) to follow.
```

**Ask the user:** "Phase 1 evidence staged. Approve commit and close out Phase 1?"

---

## Self-Review Notes

**Spec coverage check (§6 Phase 1 of the spec):**

- §6.1 Light-theme submenu bug fix — ✅ Task 1 (minimal fix), Task 2 (defense-in-depth), Task 3 (audit)
- §6.2 Visual polish — partially: most rows already implemented in current code. Pre-flight Step 0.2 verifies. Task 5 covers sidebar width. Tree text padding (8,4) deferred — current `Padding = 8,6,8,6` is close enough; not worth the churn for a 2px difference.
- §6.3 Tree restructure — ✅ Task 4
- §6.4 Phase 1 tests — ✅ Tasks 6-9
- §6.5 Phase 1 acceptance criteria — ✅ Task 10

**Type/method consistency:**

- `TestBuildWindowForRenderTest()` — referenced in Tasks 7, 8, 9. Defined in Task 7.2.
- `CollectTreeViewItems` — defined in Task 7, reused in Task 8 (same file).
- `FirstTreeView` / `EnumerateTextBlocks` / `CollectLeafTreeViewItems` — all defined in Task 9.
- `ThemeRegistry.Instance.SetPreference("light"/"dark")` — confirmed exists in `src/AkmlSql.Shell.Shared/Ui/ThemeManager.cs:72`.
- `ThemePalette.Light.Brushes[ThemeTokens.SurfaceSidebar]` — confirmed exists in `src/AkmlSql.Shell.Shared/Ui/Theme/ThemePalette.cs:81`.

**Placeholder scan:**

- No "TBD"s in the plan.
- Step 6.4 says "match what `src/AkmlSql.VS2022/AkmlSql.VS2022.csproj` references" — this is a deliberate cross-reference to existing code, not a placeholder.
- Step 7.2 has a fallback "stop and ask the user" if the build factoring proves harder than expected — this is appropriate scope discipline, not deferred work.

**Risks not in the spec but worth watching during execution:**

- Step 7.2's `TestBuildWindowForRenderTest` may require larger refactoring of `ShowDialog` than expected. If so, defer chrome tests to Phase 2 (when the file is split into per-page files anyway, opening up cleaner test seams) and proceed with manual verification only for Phase 1.
- The `Xunit.StaFact` package version 1.1.x is current; if it has been deprecated by the time this plan executes, search NuGet for the maintained replacement. The smoke test (Task 6.5) verifies whichever package version produces an STA thread.
