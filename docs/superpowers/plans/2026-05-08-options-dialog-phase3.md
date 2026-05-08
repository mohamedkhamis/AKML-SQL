# Options Dialog Phase 3 — Style Editor + Redgate import + Env colors

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the third and final phase of the Options-dialog Redgate-parity work. Upgrade the existing 2-column `ProfileEditorDialog` to the spec's 3-column layout (Style List | Category Tree | Options + Preview), polish the existing Redgate importer with a warnings UI, ship the spec's missing built-in styles, slim the Format › Styles options page, and add the spec's environment color editor sub-dialog reachable from Tabs › Color.

**Spec:** `docs/superpowers/specs/2026-05-06-options-dialog-redgate-parity-design.md` §8.

**Branching:** This plan executes on a fresh branch `019-options-dialog-phase3` cut off `master` **after `018-options-dialog-phase2` merges**. Do NOT execute on the Phase 2 branch directly — that branch is in PR review.

**Tech stack:** .NET Framework 4.7.2 (shell/WPF), .NET 10 (engine + tests), xunit, Xunit.StaFact (chrome tests), `Microsoft.VisualStudio.PlatformUI.DialogWindow` (existing base for `ProfileEditorDialog`).

---

## Pre-flight reconnaissance — what already exists

> **Important:** Phase 3's spec was authored before a full inventory of the existing codebase. This plan corrects the spec's assumptions where they conflict with current code. The mismatches are flagged as **CORRECTION** notes in each section.

The 2026-05-08 codebase already has:

| Area | Spec assumption | Actual current state |
|---|---|---|
| `ProfileEditorDialog` | "Existing 2-col dialog needs upgrade" | ✓ exists at `src/AkmlSql.Shell.Shared/Ui/ProfileEditorDialog.cs` (669 LoC, 1100×750, base class `Microsoft.VisualStudio.PlatformUI.DialogWindow`, programmatic WPF — no XAML). Layout today: left pane = search + TreeView (180px) + dynamic options; right pane = SQL before/after RichTextBoxes with `SqlPreviewRenderer`. |
| `ProfileEditorViewModel` | "Will need new fields: UserStyles, BuiltInStyles, SelectedStyle, ActiveStyle, IsDirty, OnSwitchWhileDirty" | ✓ exists at `src/AkmlSql.Shell.Shared/Ui/ProfileEditorViewModel.cs` (1,044 LoC). Already has: `ProfileName`, `ProfileDescription`, `ProfileAuthor`, `SelectedCategory`, `SearchText`, `IsDirty`, `CanUndo`/`CanRedo`, `FormattedPreview`, `VisibleOptions`. Missing the spec's separated `UserStyles` / `BuiltInStyles` collections and the active-style multi-style switching state. |
| `ProfileManager` | "Need Save/Delete/Export/Import with atomic temp+rename" | ✓ exists at `src/AkmlSql.Formatting/Profiles/ProfileManager.cs`. Surface: `Load`, `Save`, `Delete`, `Duplicate`, `Export`, `Import`, with `IsBuiltIn` metadata flag respected (Save throws on built-ins, Delete refuses built-ins). |
| Built-in styles | "Ship 4 new built-ins: Compact, Aligned, Verbose, Redgate Compatible" | ✓ 5 already exist at `src/AkmlSql.Formatting/Profiles/BuiltIn/`: `compact`, `default`, `expanded`, `leading-commas`, `minimalist`. Schema is `.akmlstyle` JSON with `metadata.isBuiltIn = true`. **CORRECTION:** the spec's lineup overlaps with reality on `Compact` only; `Aligned` / `Verbose` / `Redgate Compatible` are missing. The existing 5 should stay — users may already be on them. |
| Redgate `.sqlpromptstyle` importer | "Need to write `RedgateStyleImporter` from scratch with 1-day spike" | ✓ exists at `src/AkmlSql.Formatting/Profiles/SqlPromptImporter.cs`. **CORRECTION (file format):** Real Redgate exports are `.sqlpromptstylev2` (XML), not the `.sqlpromptstyle` JSON the spec describes. The existing importer correctly handles XML via `XDocument`. Returns `SqlPromptImportResult` with `Profile`, `MappedCount`, `UnmappedCount`, `UnmappedOptions` list. **The 1-day spike may already be effectively done** — verify in Block A and skip the spike if `OptionMap` covers ≥70% of real exports. |
| `FormatterSettings.ActiveProfile` | "Single-style today, needs multi-style work" | ✓ already exists (`AppSettings.cs:337`, default `"Default"`). Multi-style write path is per-style file under `%AppData%\AKML SQL\Profiles\` — already implemented. |
| `EnvironmentRule` model | "Reuses existing TabColoringManager + ColoringRule" | ✓ `src/AkmlSql.Core/Models/Tabs/EnvironmentRule.cs` exists; `EnvironmentMatcher.cs` is the runtime matcher. **CORRECTION:** today's model is a flat list of `ColoringRule` (label + pattern + color + match-target). The spec's environment editor proposes a richer two-tier model (named `Environment` entities + separate `Assignment` rules). Block C will need to decide: keep flat, or migrate to two-tier. Recommended: **keep flat**, match the existing inline-list editor on Tabs › UI but route it through a dedicated sub-dialog with more screen real estate. The richer model is overkill for the current use cases. |
| `FormattingPage.cs` | "Slim down to dropdown + button" | Phase 2 B.14 created this file with 8 toggles (4 trigger + 4 safety). Missing: the "Active style" dropdown and "Edit Formatting Styles…" button. |
| Tabs › UI page | "Replace inline coloring rules list with [Manage Environments…] button" | Phase 2 B.15 page has the inline `ColoringRulesList` ListBox + Add/Edit/Remove buttons that pop the existing `ShowRuleEditor` modal on `SettingsWindow`. Block C will replace those buttons with a single `[Manage Environments…]` button that pops the new sub-dialog. The `ShowRuleEditor` host method becomes dead and gets deleted. |

**Net effect on plan size:** Phase 3 is **smaller than the spec's 4–6 day estimate** — closer to **3–4 days** because the scaffolding is already there. The work is mostly:

1. UI restructure of one existing dialog (3-column layout)
2. Three new built-in style JSON files (lifted from spec's hallmark settings)
3. A warnings dialog after Redgate import (importer itself is done)
4. A 1-window environment color editor (rule model is already shipped)
5. Slim the Format page (~30 lines added to `FormattingPage.cs`)
6. ~10 new tests

---

## Architecture — five logical blocks

| Block | Days | Description | Dependencies |
|---|---|---|---|
| **A — Inventory + ViewModel + missing built-ins** | ~0.5 | Recon validation, audit `OptionMap` coverage, ship `Aligned` / `Verbose` / `Redgate Compatible` JSON, extend `ProfileEditorViewModel` with `UserStyles` / `BuiltInStyles` / `SelectedStyle` / `ActiveStyle` collections + dirty-prompt event | none |
| **B — 3-column editor UI + Style file CRUD UI** | ~1.5 | Add Style List column to `ProfileEditorDialog`. Built-in lock UI + read-only mode toggle. Toolbar: Create / Copy / Rename / Delete / Import / Export. Switching-while-dirty modal | A |
| **C — Redgate importer polish + warnings UI** | ~0.5 | Run a 1-day-cap spike to validate `OptionMap` against 5+ real `.sqlpromptstylev2` exports. Add `ImportWarning` record alongside existing `UnmappedOptions`. After-import dialog showing translated/unsupported counts with details. | B |
| **D — Environment color editor sub-dialog** | ~0.75 | New `EnvironmentColorEditorDialog` reachable from Tabs › UI. Replaces the existing inline list + Add/Edit/Remove on `TabsPage`. Reuses existing `ColoringRule` model. | none |
| **E — Format › Styles slim + Phase 3 tests** | ~0.75 | Add Active Style dropdown + Edit button to `FormattingPage`. Wire through to open `ProfileEditorDialog`. ~10 unit/integration tests for importer + ViewModel + env editor. | A, B, C, D |

Total: **~4 days** of focused work. Each task lands as a separate commit so regressions bisect cleanly.

---

## Pre-flight: confirm scope is still relevant

- [ ] **Step 0.1: Sanity-check the codebase still has the expected shape**

```bash
wc -l src/AkmlSql.Shell.Shared/Ui/ProfileEditorDialog.cs \
      src/AkmlSql.Shell.Shared/Ui/ProfileEditorViewModel.cs \
      src/AkmlSql.Formatting/Profiles/ProfileManager.cs \
      src/AkmlSql.Formatting/Profiles/SqlPromptImporter.cs
ls src/AkmlSql.Formatting/Profiles/BuiltIn/
grep -n "ActiveProfile" src/AkmlSql.Core/Config/AppSettings.cs
```

Expected (within ±10%):
- `ProfileEditorDialog.cs`: ~669 LoC
- `ProfileEditorViewModel.cs`: ~1,044 LoC
- `ProfileManager.cs`: ~343 LoC
- `SqlPromptImporter.cs`: file exists
- 5 `.akmlstyle` files in `BuiltIn/`
- `ActiveProfile` lives on `FormatterSettings` at line ~337

If shapes have shifted dramatically, **stop and report** — this plan was written against the 2026-05-08 codebase.

- [ ] **Step 0.2: Confirm Phase 2 has merged**

```bash
git log --oneline 018-options-dialog-phase2..master | head -3
```

Expected: shows the squash-merge or merge commit of Phase 2 on master. If empty, Phase 2 hasn't merged yet — **do not start Phase 3 on a stale base**.

```bash
git checkout master && git pull
git checkout -b 019-options-dialog-phase3
```

- [ ] **Step 0.3: Confirm no in-flight uncommitted work**

```bash
git status --short
```

Expected: empty.

---

# BLOCK A — Inventory, ViewModel state, missing built-ins

Block A delivers no UI. It validates the existing infrastructure works as recon described and ships the data-side gaps so Block B has something real to wire to.

## Task A.1: Audit `SqlPromptImporter` coverage

**Files:**
- Read: `src/AkmlSql.Formatting/Profiles/SqlPromptImporter.cs`
- Read: any sample `.sqlpromptstylev2` files committed under `tests/` or `doc/SQL-PROMPT/`
- Modify (maybe): `src/AkmlSql.Formatting/Profiles/SqlPromptImporter.cs` to fill gaps

The spec called for a 1-day spike to lock the translation table. The importer already exists, so the spike collapses to a coverage audit.

- [ ] **Step A.1.1: Read `SqlPromptImporter.OptionMap`**

```bash
grep -c "OptionMap.*new" src/AkmlSql.Formatting/Profiles/SqlPromptImporter.cs
grep "static readonly Dictionary<string, Action" src/AkmlSql.Formatting/Profiles/SqlPromptImporter.cs
```

Count the entries — record `<existing-count>`. The spec says ~70% direct + ~15% compatible + ~15% unmapped is a healthy ratio, so a real-world Redgate v10+ export exporting ≥40 options should map ≥28.

- [ ] **Step A.1.2: Find or collect 3-5 real `.sqlpromptstylev2` exports**

Look under `doc/SQL-PROMPT/` and `tests/AkmlSql.Formatting.Tests/Profiles/Fixtures/` (if present) for committed samples. If none exist, ask the user for a representative export from their team's SQL Prompt installation.

```bash
find . -name "*.sqlpromptstylev2" -o -name "*.sqlpromptstyle" 2>/dev/null | head -10
```

If <3 samples available, **stop and ask the user** to provide more. The cap is a 1-day spike — if no samples are reachable in that day, ship A.1 with a TODO and revisit.

- [ ] **Step A.1.3: Run the importer against each sample, log the unmapped list**

Write a one-off diagnostic test (or a console snippet) that calls `SqlPromptImporter.Import(path)` and prints `MappedCount`, `UnmappedCount`, and `UnmappedOptions`. For each repeated unmapped option across multiple samples, decide:

- **Add a Direct mapping** if the AKML profile has a 1:1 equivalent
- **Add a Compatible mapping** if AKML's concept is shaped differently (record this as a future-`ImportWarning`)
- **Leave unmapped** with confidence the option is rare/Redgate-specific

Land coverage improvements as a small commit if any are added. Skip if `OptionMap` already covers everything seen.

- [ ] **Step A.1.4: Prepare commit (if changes were made)**

```bash
git add src/AkmlSql.Formatting/Profiles/SqlPromptImporter.cs
```

Suggested message:

```
Extend SqlPromptImporter coverage from real-world samples (Phase 3 A.1)

Added <N> direct mappings and <M> compatible mappings to OptionMap based
on a coverage audit against <K> real .sqlpromptstylev2 samples. Median
mapped/unmapped ratio across the samples improved from <before>/<total>
to <after>/<total>.

Refs: docs/superpowers/specs/2026-05-06-options-dialog-redgate-parity-design.md §8.6
      docs/superpowers/plans/2026-05-08-options-dialog-phase3.md A.1
```

**Ask the user:** "Importer coverage audit complete. Approve commit?"

If no edits were needed, skip the commit and document the audit result in the next commit's message.

---

## Task A.2: Ship missing built-in styles

**Files:**
- Create: `src/AkmlSql.Formatting/Profiles/BuiltIn/aligned.akmlstyle`
- Create: `src/AkmlSql.Formatting/Profiles/BuiltIn/verbose.akmlstyle`
- Create: `src/AkmlSql.Formatting/Profiles/BuiltIn/redgate-compatible.akmlstyle`
- Modify (maybe): `src/AkmlSql.Engine/AkmlSql.Engine.csproj` (if `.akmlstyle` files need explicit `<EmbeddedResource>` / `<Content CopyToOutputDirectory>` entries)
- Test: `tests/AkmlSql.Formatting.Tests/Profiles/BuiltInStylesTests.cs` (new)

Three new styles to round out the spec's lineup. Existing `compact` / `default` / `expanded` / `leading-commas` / `minimalist` stay — users may already be on them; deletions would be breaking.

- [ ] **Step A.2.1: Read `default.akmlstyle` to learn the schema**

```bash
cat src/AkmlSql.Formatting/Profiles/BuiltIn/default.akmlstyle
```

The schema is `metadata` + `whitespace` + `casing` + `lists` + `parentheses` + `clauses` + `statements` + `expressions` + `other`. Keep schema parity — only the values change between styles.

- [ ] **Step A.2.2: Author `aligned.akmlstyle`**

Hallmarks (from spec §8.4): right-aligned keywords, columns aligned in lists, blank line between statements, max line 100. Set:

- `whitespace.maxLineWidth = 100`
- `whitespace.emptyLineBetweenStatements = 1`
- `lists.alignColumns = true` (or whichever flag exists for column alignment — verify by reading `FormattingProfile.cs`)
- `clauses.alignKeywordsRight = true` (or equivalent — verify)
- `metadata.id` = a fresh GUID
- `metadata.name` = "Aligned"
- `metadata.description` = "Right-aligned keywords with columns aligned in lists. Max line width 100."
- `metadata.author` = "AKML SQL"
- `metadata.version` = "1.0.0"
- `metadata.basedOn` = "Default"
- `metadata.isBuiltIn` = true

- [ ] **Step A.2.3: Author `verbose.akmlstyle`**

Hallmarks: every clause on its own line, max line 80, mandatory parens around CASE, expanded CTE bodies. Set:

- `whitespace.maxLineWidth = 80`
- `whitespace.lineBreakBeforeClause = true` and `lineBreakAfterClause = true`
- `expressions.parenthesizeCase = true`
- `statements.expandCte = true` (verify field name)
- `metadata.name` = "Verbose"
- (rest of metadata as A.2.2)

- [ ] **Step A.2.4: Author `redgate-compatible.akmlstyle`**

Settings tuned to mirror SQL Prompt's factory defaults. The simplest authoring path: import `samples/RedgateDefault.sqlpromptstylev2` (or whatever default sample is available) using `SqlPromptImporter`, save the result via `ProfileSerializer`, hand-edit the output to set `metadata.name = "Redgate Compatible"`, `metadata.id = <fresh GUID>`, `metadata.isBuiltIn = true`, `metadata.basedOn = "(Redgate import)"`. Commit the resulting JSON.

If no Redgate Default sample is available: hand-author against the SQL Prompt online docs (https://documentation.red-gate.com/sp10/styles/default-style) — best-effort, ship as v1.0.0, refine later.

- [ ] **Step A.2.5: Verify `ProfileManager.Load` finds the new files**

The existing `ProfileManager` enumerates `BuiltIn/*.akmlstyle` at construction. If files are picked up via reflection on the embedded resource, may need an `<EmbeddedResource>` entry — verify by tracing `ProfileManager` ctor and the BuiltIn loading code.

```bash
grep -n "BuiltIn\|.akmlstyle\|EmbeddedResource\|GetFiles" src/AkmlSql.Formatting/AkmlSql.Formatting.csproj src/AkmlSql.Formatting/Profiles/ProfileManager.cs | head -20
```

If `.csproj` already has `<None CopyToOutputDirectory>` / `<EmbeddedResource>` for `BuiltIn/*.akmlstyle` with a wildcard, the new files are picked up automatically. If they're listed individually, add the three new entries.

- [ ] **Step A.2.6: Add a test that all built-ins parse**

Create `tests/AkmlSql.Formatting.Tests/Profiles/BuiltInStylesTests.cs`:

```csharp
using System.IO;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Profiles;

public class BuiltInStylesTests
{
    [Fact]
    public void AllBuiltIns_LoadCleanly()
    {
        var manager = new ProfileManager();
        // Adapt to ProfileManager's actual API — likely something like
        // manager.GetAll().Where(p => p.Metadata.IsBuiltIn) or manager.GetBuiltIns().
        var builtIns = manager.GetAll().Where(p => p.Metadata.IsBuiltIn).ToList();

        Assert.True(builtIns.Count >= 8, $"Expected ≥8 built-in styles, found {builtIns.Count}");
        Assert.Contains(builtIns, p => p.Metadata.Name == "Default");
        Assert.Contains(builtIns, p => p.Metadata.Name == "Compact");
        Assert.Contains(builtIns, p => p.Metadata.Name == "Aligned");
        Assert.Contains(builtIns, p => p.Metadata.Name == "Verbose");
        Assert.Contains(builtIns, p => p.Metadata.Name == "Redgate Compatible");
    }
}
```

If the count assertion fails because the actual count is 5 or 6, drop the threshold to match reality plus the three new entries.

- [ ] **Step A.2.7: Build + run the test**

```bash
dotnet build src/AkmlSql.Formatting/AkmlSql.Formatting.csproj -c Release
dotnet test tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj --filter "FullyQualifiedName~BuiltInStylesTests"
```

Expected: 0 errors, 1 passed.

- [ ] **Step A.2.8: Prepare commit**

```bash
git add src/AkmlSql.Formatting/Profiles/BuiltIn/aligned.akmlstyle \
        src/AkmlSql.Formatting/Profiles/BuiltIn/verbose.akmlstyle \
        src/AkmlSql.Formatting/Profiles/BuiltIn/redgate-compatible.akmlstyle \
        tests/AkmlSql.Formatting.Tests/Profiles/BuiltInStylesTests.cs
# plus the .csproj if it needed an explicit Content/EmbeddedResource entry
```

Suggested message:

```
Add Aligned, Verbose, Redgate-Compatible built-in styles (Phase 3 A.2)

Three new built-ins round out the spec §8.4 lineup alongside the existing
five (default, compact, expanded, leading-commas, minimalist).

- aligned.akmlstyle: right-aligned keywords + column alignment + max 100
- verbose.akmlstyle: clause-per-line + parens around CASE + max 80
- redgate-compatible.akmlstyle: mirrors SQL Prompt factory defaults

BuiltInStylesTests.AllBuiltIns_LoadCleanly added — guards against future
schema drift in the BuiltIn/ JSON files.

Refs: docs/superpowers/specs/2026-05-06-options-dialog-redgate-parity-design.md §8.4
      docs/superpowers/plans/2026-05-08-options-dialog-phase3.md A.2
```

**Ask the user:** "Built-in styles ready. Approve commit?"

---

## Task A.3: Extend `ProfileEditorViewModel` with multi-style state

**Files:**
- Modify: `src/AkmlSql.Shell.Shared/Ui/ProfileEditorViewModel.cs`
- Test: `tests/AkmlSql.Formatting.Tests/Profiles/ProfileEditorViewModelTests.cs` (extend existing or create)

Today's view model holds state for ONE profile being edited. The 3-column editor needs to switch between profiles, separating user-owned from built-in.

- [ ] **Step A.3.1: Add new properties**

In `ProfileEditorViewModel`:

```csharp
// New observable collections — order: built-ins first (read-only), user-owned second.
public ObservableCollection<StyleEntry> BuiltInStyles { get; }
public ObservableCollection<StyleEntry> UserStyles { get; }

// Currently being edited. Setting this swaps the dynamic Options panel
// over to the new profile's values. PropertyChanged fires.
public StyleEntry? SelectedStyle { get; set; }

// The style that's currently the active one in FormatterSettings.ActiveProfile.
// Drives the ✓ marker in the UI. Setting this writes config (atomic).
public StyleEntry? ActiveStyle { get; set; }

// Raised when the user attempts to switch SelectedStyle while IsDirty == true.
// Subscribers (the dialog) prompt: Save / Discard / Cancel.
public event EventHandler<DirtySwitchRequest>? OnSwitchWhileDirty;
```

`StyleEntry` is a small wrapper:

```csharp
public sealed record StyleEntry(
    string Name,
    string Description,
    bool IsBuiltIn,
    string FilePath);
```

`DirtySwitchRequest`:

```csharp
public sealed class DirtySwitchRequest : EventArgs
{
    public StyleEntry From { get; init; } = null!;
    public StyleEntry To { get; init; } = null!;
    public DirtySwitchAction Action { get; set; } = DirtySwitchAction.Cancel;
}

public enum DirtySwitchAction { Cancel, Save, Discard }
```

- [ ] **Step A.3.2: Wire ActiveStyle ↔ FormatterSettings.ActiveProfile**

The view model already knows about `ConfigManager`. The setter for `ActiveStyle` should:

```csharp
set
{
    if (_activeStyle == value) return;
    _activeStyle = value;
    if (value != null)
    {
        var settings = ConfigManager.Load();
        settings.Formatter.ActiveProfile = value.Name;
        ConfigManager.Save(settings);  // atomic temp+rename inside ConfigManager
    }
    OnPropertyChanged(nameof(ActiveStyle));
}
```

- [ ] **Step A.3.3: Refresh `BuiltInStyles` and `UserStyles` from ProfileManager**

Add a `RefreshStyleLists()` method called once on construction and after any CRUD operation:

```csharp
private void RefreshStyleLists()
{
    var all = _profileManager.GetAll().ToList(); // sort: name asc
    BuiltInStyles.Clear();
    UserStyles.Clear();
    foreach (var p in all.Where(p => p.Metadata.IsBuiltIn).OrderBy(p => p.Metadata.Name))
        BuiltInStyles.Add(new StyleEntry(p.Metadata.Name, p.Metadata.Description, true, /* path */));
    foreach (var p in all.Where(p => !p.Metadata.IsBuiltIn).OrderBy(p => p.Metadata.Name))
        UserStyles.Add(new StyleEntry(p.Metadata.Name, p.Metadata.Description, false, /* path */));
}
```

Verify the actual ProfileManager API — if there's no `GetAll()`, add one (it's a thin enumeration of the existing on-disk + BuiltIn dirs).

- [ ] **Step A.3.4: Tests**

```csharp
[Fact]
public void RefreshStyleLists_PopulatesBothCollections()
{
    var vm = CreateViewModel();
    Assert.True(vm.BuiltInStyles.Count >= 5);
    Assert.True(vm.UserStyles.Count >= 0); // 0 is fine if user has no custom styles yet
    Assert.All(vm.BuiltInStyles, s => Assert.True(s.IsBuiltIn));
    Assert.All(vm.UserStyles, s => Assert.False(s.IsBuiltIn));
}

[Fact]
public void SwitchingDirty_RaisesOnSwitchWhileDirty()
{
    var vm = CreateViewModel();
    vm.LoadProfile(vm.UserStyles.First()); // populate
    vm.SetOptionForTest("whitespace.tabSize", 8); // mark dirty
    Assert.True(vm.IsDirty);

    DirtySwitchRequest? captured = null;
    vm.OnSwitchWhileDirty += (_, e) => { captured = e; e.Action = DirtySwitchAction.Discard; };
    vm.SelectedStyle = vm.BuiltInStyles.First();

    Assert.NotNull(captured);
    Assert.False(vm.IsDirty); // discard reset it
}

[Fact]
public void SetActiveStyle_WritesAppSettings()
{
    var vm = CreateViewModel();
    var target = vm.BuiltInStyles.First(s => s.Name == "Compact");
    vm.ActiveStyle = target;
    var reread = ConfigManager.Load();
    Assert.Equal("Compact", reread.Formatter.ActiveProfile);
}
```

The test helpers (`SetOptionForTest`, `LoadProfile`) probably exist already on the view model — adapt to what's there.

- [ ] **Step A.3.5: Build + run tests**

```bash
dotnet test tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj --filter "FullyQualifiedName~ProfileEditorViewModelTests"
```

Expected: all green.

- [ ] **Step A.3.6: Prepare commit**

```bash
git add src/AkmlSql.Shell.Shared/Ui/ProfileEditorViewModel.cs \
        tests/AkmlSql.Formatting.Tests/Profiles/ProfileEditorViewModelTests.cs
```

Suggested message:

```
Extend ProfileEditorViewModel for multi-style switching (Phase 3 A.3)

Adds the state the 3-column editor (B.1) needs:
- BuiltInStyles + UserStyles ObservableCollections (sorted by name)
- SelectedStyle (the one being edited)
- ActiveStyle (the one in FormatterSettings.ActiveProfile)
- OnSwitchWhileDirty event for dirty-prompt routing

ActiveStyle setter atomically writes ConfigManager. RefreshStyleLists()
re-pulls from ProfileManager.GetAll() and is called on init + after any
file CRUD.

Three tests cover: list population, dirty-prompt routing, ActiveStyle
write-through.

Refs: docs/superpowers/specs/2026-05-06-options-dialog-redgate-parity-design.md §8.3
      docs/superpowers/plans/2026-05-08-options-dialog-phase3.md A.3
```

**Ask the user:** "ViewModel extensions ready. Approve commit?"

**Block A complete.** Data plumbing is in place. UI work begins in Block B.

---

# BLOCK B — 3-column editor UI + style file CRUD UI

Block B is the visual heart of Phase 3. The existing 2-col `ProfileEditorDialog` becomes 3-col. The new left column shows the Style List with a toolbar; the existing left becomes middle; the existing right (preview) stays right.

## Task B.1: Add Style List column to `ProfileEditorDialog`

**Files:**
- Modify: `src/AkmlSql.Shell.Shared/Ui/ProfileEditorDialog.cs`

- [ ] **Step B.1.1: Read the existing layout to understand the column structure**

```bash
grep -n "ColumnDefinition\|RowDefinition\|leftPanel\|rightPanel\|GridSplitter" src/AkmlSql.Shell.Shared/Ui/ProfileEditorDialog.cs | head -30
```

Note the current dimensions: 1100×750, 3 columns (`*`, 5px splitter, `*`). Spec §8.2 says target 1280×750. Width bump goes here.

- [ ] **Step B.1.2: Restructure to 5-col Grid (4 content + 2 splitters)**

Replace:

```csharp
mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) }); // Splitter
mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
```

With:

```csharp
// Style List (250px) | Splitter (5px) | Categories+Options (1*) | Splitter (5px) | Preview (1*)
mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
```

Adjust `Width = 1280` and remap existing children to `Grid.SetColumn(child, 2)` (middle) and `Grid.SetColumn(child, 4)` (right).

- [ ] **Step B.1.3: Build the Style List column body**

The new column body (target column 0):

```
┌─ Style List (250px) ─┐
│ Built-in Styles      │  ← header
│   ✓ Default      🔒 │  ← active marker (✓), built-in lock (🔒)
│     Compact     🔒  │
│     Aligned     🔒  │
│     ...              │
│ ─────────────────    │  ← separator
│ Your Styles          │
│     My Style         │
│     Team Style       │
├──────────────────────┤
│ [+ Create]           │  ← toolbar (vertical, sticky to bottom)
│ [⎘ Copy]             │
│ [✎ Rename]           │
│ [✕ Delete]           │
│ [↑ Import]           │
│ [↓ Export]           │
└──────────────────────┘
```

Implement as a `DockPanel` with:
- `DockPanel.Dock = Top`: header + ListView bound to `vm.BuiltInStyles`
- `DockPanel.Dock = Top`: separator (`Border` height=1)
- `DockPanel.Dock = Top`: header + ListView bound to `vm.UserStyles`
- `DockPanel.Dock = Bottom`: 6-button toolbar (`StackPanel Orientation=Vertical`)

Use `ItemTemplate` with a `Grid` per row: column 0 = active marker (✓ TextBlock visible only when `Name == ActiveStyle.Name`), column 1 = `Name`, column 2 = lock icon (`🔒` visible only when `IsBuiltIn`).

- [ ] **Step B.1.4: Hook ListView selection to ViewModel.SelectedStyle**

```csharp
builtInListView.SelectionChanged += (s, e) => {
    if (builtInListView.SelectedItem is StyleEntry entry) {
        userListView.SelectedItem = null; // clear other list
        TrySwitchSelected(entry);
    }
};
userListView.SelectionChanged += (s, e) => {
    if (userListView.SelectedItem is StyleEntry entry) {
        builtInListView.SelectedItem = null;
        TrySwitchSelected(entry);
    }
};

private void TrySwitchSelected(StyleEntry target)
{
    if (_viewModel.IsDirty)
    {
        // Subscribe to OnSwitchWhileDirty before setting; the handler picks Save/Discard/Cancel.
        // The view model's setter will respect Cancel and revert SelectedStyle.
    }
    _viewModel.SelectedStyle = target;
}
```

The dirty prompt is implemented in B.3.

- [ ] **Step B.1.5: Show / hide read-only mode based on `SelectedStyle.IsBuiltIn`**

When `SelectedStyle.IsBuiltIn`:
- Disable all option controls in the middle column (loop the `_optionsPanel.Children`, set `IsEnabled = false`)
- Hide / disable Rename and Delete buttons in the toolbar
- Show a "🔒 Built-in style — Copy to edit" banner above the options panel

When `!SelectedStyle.IsBuiltIn`:
- Enable all option controls
- Show all toolbar buttons

Wire as a method `ApplyReadOnlyState(bool readOnly)` called from `OnViewModelPropertyChanged` when `SelectedStyle` changes.

- [ ] **Step B.1.6: Build and visually verify**

```bash
"$MSBUILD" src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj -t:Restore,Build -p:Configuration=Release -v:minimal
```

Manual smoke test (deferred to user): open the Profile Editor (currently from Format › Styles → Edit Profiles button or wherever `EditProfileCommand` is wired), confirm:
- Three columns render at 1280×750
- Style List shows the 8 built-ins under "Built-in Styles" + any user styles under "Your Styles"
- Selecting a built-in disables option controls and shows the lock banner
- Selecting a user style enables them

- [ ] **Step B.1.7: Prepare commit**

```bash
git add src/AkmlSql.Shell.Shared/Ui/ProfileEditorDialog.cs
```

Suggested message:

```
Add Style List column to ProfileEditorDialog (Phase 3 B.1)

3-column 1280×750 layout per spec §8.2: Style List | Categories+Options
| Live Preview. Style List shows Built-in Styles (read-only) above
Your Styles, with the active style marked by a ✓ and built-ins by a 🔒.

Selecting a built-in puts the middle column in read-only mode (controls
disabled, toolbar hides Rename/Delete, banner explains "Copy to edit").

Toolbar (B.2) lives at the bottom of the new column with 6 buttons:
Create / Copy / Rename / Delete / Import / Export. The buttons are
present in this commit but their click handlers throw NotImplemented —
B.2 wires them.

Refs: docs/superpowers/specs/2026-05-06-options-dialog-redgate-parity-design.md §8.2
      docs/superpowers/plans/2026-05-08-options-dialog-phase3.md B.1
```

**Ask the user:** "Style List column ready. Approve commit?"

---

## Task B.2: Wire Create / Copy / Rename / Delete buttons

**Files:**
- Modify: `src/AkmlSql.Shell.Shared/Ui/ProfileEditorDialog.cs`
- Modify: `src/AkmlSql.Shell.Shared/Ui/ProfileEditorViewModel.cs` (add CRUD methods)
- Test: `tests/AkmlSql.Formatting.Tests/Profiles/ProfileEditorViewModelTests.cs`

`ProfileManager` already has the file-side primitives (`Save`, `Delete`, `Duplicate`). The view model wraps each in a method that also refreshes the lists and selects the new entry.

- [ ] **Step B.2.1: Create modal**

Pop a small modal (`Window` size 360×180) with:
- Label "Style name:"
- TextBox (initial focus, validates on TextChanged: non-empty, no path separators, no reserved chars `< > : " / \ | ? *`, doesn't already exist)
- Label "Base on:"
- Dropdown listing all existing styles (default to "Default")
- OK / Cancel

OK calls `viewModel.CreateStyle(name, baseOn)` which:
1. `var newProfile = profileManager.Duplicate(baseOn, name);`
2. `newProfile.Metadata.IsBuiltIn = false;` (Duplicate already does this — verify)
3. `profileManager.Save(newProfile);` (writes to user dir)
4. `RefreshStyleLists();`
5. `SelectedStyle = UserStyles.FirstOrDefault(s => s.Name == name);`

- [ ] **Step B.2.2: Copy modal**

Similar to Create but pre-fills name as `"Copy of " + SelectedStyle.Name`. Allows copying built-ins (the spec calls this the canonical "create custom style" entry point). Wire to `viewModel.CopyStyle(source, newName)`.

- [ ] **Step B.2.3: Rename — inline edit + handler**

Either:
- F2 / right-click → renamable cell in the ListView (more polished, more code)
- Modal "Rename Style" with new-name TextBox (simpler)

Recommend the modal for now; inline edit can land in a follow-up. Wire to `viewModel.RenameStyle(oldName, newName)` which:

```csharp
public void RenameStyle(string oldName, string newName)
{
    var profile = _profileManager.Load(oldName);
    var oldFilePath = /* path of oldName */;
    profile.Metadata.Name = newName;
    _profileManager.Save(profile);  // writes new file
    File.Delete(oldFilePath);  // removes the old one
    if (_settings.Formatter.ActiveProfile == oldName)
    {
        _settings.Formatter.ActiveProfile = newName;
        ConfigManager.Save(_settings);  // atomic — preserves active style
    }
    RefreshStyleLists();
    SelectedStyle = UserStyles.First(s => s.Name == newName);
}
```

Disable the Rename button when `SelectedStyle.IsBuiltIn`.

- [ ] **Step B.2.4: Delete — confirmation + handler**

Show MessageBox "Delete style 'X'? This cannot be undone." Yes/No.

```csharp
public void DeleteStyle(string name)
{
    if (_settings.Formatter.ActiveProfile == name)
        throw new InvalidOperationException("Cannot delete the active style. Switch to another style first.");
    _profileManager.Delete(name);
    RefreshStyleLists();
    SelectedStyle = UserStyles.FirstOrDefault() ?? BuiltInStyles.First(s => s.Name == "Default");
}
```

The "active style cannot be deleted" check — surface as a friendly MessageBox, not an exception.

Disable the Delete button when `SelectedStyle.IsBuiltIn` OR `SelectedStyle.Name == ActiveStyle.Name`.

- [ ] **Step B.2.5: Tests**

Three tests in `ProfileEditorViewModelTests.cs`:

```csharp
[Fact]
public void CreateStyle_AddsToUserStyles()
{
    var vm = CreateViewModel();
    var before = vm.UserStyles.Count;
    vm.CreateStyle("MyTest", baseOn: "Default");
    Assert.Equal(before + 1, vm.UserStyles.Count);
    Assert.Equal("MyTest", vm.SelectedStyle?.Name);
    Assert.False(vm.SelectedStyle!.IsBuiltIn);
}

[Fact]
public void RenameActive_UpdatesAppSettings()
{
    var vm = CreateViewModel();
    vm.CreateStyle("RenameMe", "Default");
    vm.ActiveStyle = vm.UserStyles.First(s => s.Name == "RenameMe");
    vm.RenameStyle("RenameMe", "Renamed");
    Assert.Equal("Renamed", ConfigManager.Load().Formatter.ActiveProfile);
}

[Fact]
public void DeleteActive_IsRejected()
{
    var vm = CreateViewModel();
    vm.CreateStyle("Pinned", "Default");
    vm.ActiveStyle = vm.UserStyles.First(s => s.Name == "Pinned");
    Assert.Throws<InvalidOperationException>(() => vm.DeleteStyle("Pinned"));
}
```

- [ ] **Step B.2.6: Build + run tests**

```bash
dotnet test tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj
"$MSBUILD" src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj -t:Restore,Build -p:Configuration=Release -v:minimal
```

Expected: all green, 0 errors.

- [ ] **Step B.2.7: Prepare commit**

```bash
git add src/AkmlSql.Shell.Shared/Ui/ProfileEditorDialog.cs \
        src/AkmlSql.Shell.Shared/Ui/ProfileEditorViewModel.cs \
        tests/AkmlSql.Formatting.Tests/Profiles/ProfileEditorViewModelTests.cs
```

Suggested message:

```
Wire Create/Copy/Rename/Delete buttons in Style List toolbar (Phase 3 B.2)

Six toolbar buttons land their click handlers:
- Create: pop name + base-on modal, call vm.CreateStyle
- Copy: pop name modal pre-filled "Copy of <selected>", call vm.CopyStyle
- Rename: name modal, call vm.RenameStyle which atomically updates
  ConfigManager when renaming the active style
- Delete: confirmation MessageBox, call vm.DeleteStyle which refuses
  to delete the active style with a friendly error message
- Import / Export: defer to B.4 (Redgate import) and existing
  ProfileManager.Export

Three tests added: CreateStyle, RenameActive_UpdatesAppSettings,
DeleteActive_IsRejected.

Refs: docs/superpowers/plans/2026-05-08-options-dialog-phase3.md B.2
```

**Ask the user:** "Style CRUD buttons ready. Approve commit?"

---

## Task B.3: Switching-while-dirty prompt

**Files:**
- Modify: `src/AkmlSql.Shell.Shared/Ui/ProfileEditorDialog.cs`

When the user clicks a different style in the Style List while there are unsaved changes, prompt: "Save changes to '<from>' before switching to '<to>'?" with Save / Discard / Cancel.

- [ ] **Step B.3.1: Subscribe to `OnSwitchWhileDirty` in the dialog ctor**

```csharp
_viewModel.OnSwitchWhileDirty += (s, e) => {
    var msg = $"You have unsaved changes to '{e.From.Name}'. Save before switching to '{e.To.Name}'?";
    var result = MessageBox.Show(msg, "Unsaved Changes",
        MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
    e.Action = result switch
    {
        MessageBoxResult.Yes    => DirtySwitchAction.Save,
        MessageBoxResult.No     => DirtySwitchAction.Discard,
        _                        => DirtySwitchAction.Cancel,
    };
};
```

The view model's `SelectedStyle` setter is responsible for honoring `Action`:

```csharp
set
{
    if (_selectedStyle == value) return;
    if (IsDirty && _selectedStyle != null)
    {
        var req = new DirtySwitchRequest { From = _selectedStyle, To = value };
        OnSwitchWhileDirty?.Invoke(this, req);
        switch (req.Action)
        {
            case DirtySwitchAction.Save: SaveCurrent(); break;
            case DirtySwitchAction.Discard: ResetDirty(); break;
            case DirtySwitchAction.Cancel: return; // don't change
        }
    }
    _selectedStyle = value;
    LoadProfile(value); // populates options panel
    OnPropertyChanged(nameof(SelectedStyle));
}
```

`SaveCurrent` and `ResetDirty` may already exist on the view model — check.

- [ ] **Step B.3.2: Test**

```csharp
[Fact]
public void SwitchingDirty_Save_PersistsBeforeSwitching()
{
    var vm = CreateViewModel();
    var first = vm.BuiltInStyles.First(s => s.Name == "Default");
    var second = vm.BuiltInStyles.First(s => s.Name == "Compact");
    vm.SelectedStyle = first;
    // Note: built-ins are read-only — use a user style for this test
    vm.CreateStyle("DirtyTest", "Default");
    vm.SelectedStyle = vm.UserStyles.First(s => s.Name == "DirtyTest");
    vm.SetOptionForTest("whitespace.tabSize", 8);
    Assert.True(vm.IsDirty);

    vm.OnSwitchWhileDirty += (_, e) => e.Action = DirtySwitchAction.Save;
    vm.SelectedStyle = second;

    var reread = _profileManager.Load("DirtyTest");
    Assert.Equal(8, reread.Whitespace.TabSize);
}
```

- [ ] **Step B.3.3: Build + tests + commit**

```bash
"$MSBUILD" src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj -t:Restore,Build -p:Configuration=Release -v:minimal
dotnet test tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj
```

```bash
git add src/AkmlSql.Shell.Shared/Ui/ProfileEditorDialog.cs \
        src/AkmlSql.Shell.Shared/Ui/ProfileEditorViewModel.cs \
        tests/AkmlSql.Formatting.Tests/Profiles/ProfileEditorViewModelTests.cs
```

Message:

```
Add dirty-switch prompt to ProfileEditorDialog (Phase 3 B.3)

Switching SelectedStyle while IsDirty raises OnSwitchWhileDirty. The
dialog handler shows YesNoCancel "Save changes to '<from>' before
switching to '<to>'?". Save persists, Discard resets, Cancel reverts.

SwitchingDirty_Save_PersistsBeforeSwitching test added.

Refs: docs/superpowers/plans/2026-05-08-options-dialog-phase3.md B.3
```

**Ask the user:** "Dirty-switch prompt ready. Approve commit?"

---

# BLOCK C — Redgate import polish + warnings UI

The importer exists; this block adds the post-import warnings dialog called for in spec §8.6.

## Task C.1: Add `ImportWarning` record + extend `SqlPromptImportResult`

**Files:**
- Modify: `src/AkmlSql.Formatting/Profiles/SqlPromptImporter.cs`

Today's `SqlPromptImportResult` has `MappedCount` / `UnmappedCount` / `UnmappedOptions: List<string>`. The spec asks for richer warnings (Direct/Compatible/Unmapped tiers + per-warning detail).

- [ ] **Step C.1.1: Add `ImportWarning` record**

```csharp
public sealed record ImportWarning(
    string RedgateKey,
    ImportWarningKind Kind,
    string Reason,
    string DefaultedTo);

public enum ImportWarningKind { Direct, Compatible, Unmapped }
```

- [ ] **Step C.1.2: Extend `SqlPromptImportResult`**

Keep existing `MappedCount` / `UnmappedCount` / `UnmappedOptions` for back-compat. Add:

```csharp
public List<ImportWarning> Warnings { get; set; } = [];
```

Compatible mappings (lossy) write an entry with `Kind = Compatible`. Unmapped writes `Kind = Unmapped`. Direct writes nothing (silent success).

- [ ] **Step C.1.3: Update internal mapping pipeline to emit warnings**

The current `OptionMap` is `Dictionary<string, Action<FormattingProfile, string>>`. To emit Compatible warnings, the mapping function needs to know it's lossy. Two options:

(a) Change to `Dictionary<string, IRedgateMapping>` where `IRedgateMapping` is a polymorphic interface with `Apply(profile, value, warnings)`. Three impls: `DirectMapping`, `CompatibleMapping(string reason)`, `UnmappedMapping(string reason)`.

(b) Stay with `Action` but have each Compatible mapping take a `List<ImportWarning>` ref. Less polymorphic but smaller change.

Recommend (a) — cleaner long-term. ~30 LoC of mapping infrastructure for a much clearer extension story.

- [ ] **Step C.1.4: Migrate existing entries to the new shape**

Walk every existing `OptionMap` entry and re-classify as Direct / Compatible / Unmapped. Anything that does a 1:1 enum or bool flip stays Direct. Anything that maps a Redgate option to a different-shape AKML option becomes Compatible with a one-line reason. Existing `UnmappedOptions` → all Unmapped warnings.

- [ ] **Step C.1.5: Tests**

```csharp
[Fact]
public void Import_DirectMapping_ProducesNoWarning()
{
    var xml = "<SqlPromptStyle><Casing.ReservedKeywords>UPPERCASE</Casing.ReservedKeywords></SqlPromptStyle>";
    var path = WriteTemp(xml);
    var result = SqlPromptImporter.Import(path);
    Assert.Equal(1, result.MappedCount);
    Assert.Empty(result.Warnings);
    Assert.Equal("UPPERCASE", result.Profile.Casing.ReservedKeywords);
}

[Fact]
public void Import_CompatibleMapping_EmitsWarning()
{
    // Use a Redgate option that has a different shape in AKML — pick one from C.1.4
    var xml = "<SqlPromptStyle><SomeRedgateOnly>true</SomeRedgateOnly></SqlPromptStyle>";
    var path = WriteTemp(xml);
    var result = SqlPromptImporter.Import(path);
    Assert.Single(result.Warnings, w => w.Kind == ImportWarningKind.Compatible);
}

[Fact]
public void Import_UnmappedOption_AppearsInWarnings()
{
    var xml = "<SqlPromptStyle><InventedRedgateOption>x</InventedRedgateOption></SqlPromptStyle>";
    var path = WriteTemp(xml);
    var result = SqlPromptImporter.Import(path);
    Assert.Single(result.Warnings, w => w.Kind == ImportWarningKind.Unmapped);
    Assert.Equal("InventedRedgateOption", result.Warnings[0].RedgateKey);
}
```

- [ ] **Step C.1.6: Commit**

```bash
git add src/AkmlSql.Formatting/Profiles/SqlPromptImporter.cs \
        tests/AkmlSql.Formatting.Tests/Profiles/SqlPromptImporterTests.cs
```

Message:

```
Extend SqlPromptImporter with ImportWarning tier classification (Phase 3 C.1)

Direct / Compatible / Unmapped tiers per spec §8.6. The internal OptionMap
is migrated from Dictionary<string, Action<...>> to Dictionary<string,
IRedgateMapping>. Each mapping classifies its translation:

- Direct: 1:1, no warning
- Compatible: lossy translation (concept exists with different shape)
  → ImportWarning with Kind = Compatible + reason
- Unmapped: option exists in Redgate but not in AKML → ImportWarning
  with Kind = Unmapped + DefaultedTo

SqlPromptImportResult gains a Warnings: List<ImportWarning> property.
Existing MappedCount / UnmappedCount / UnmappedOptions kept for
back-compat with any existing callers.

Three new tests cover each tier.

Refs: docs/superpowers/specs/2026-05-06-options-dialog-redgate-parity-design.md §8.6
      docs/superpowers/plans/2026-05-08-options-dialog-phase3.md C.1
```

**Ask the user:** "Importer warning tiers ready. Approve commit?"

---

## Task C.2: Post-import warnings dialog

**Files:**
- Create: `src/AkmlSql.Shell.Shared/Ui/RedgateImportResultDialog.cs`
- Modify: `src/AkmlSql.Shell.Shared/Ui/ProfileEditorDialog.cs` (Import button now opens this)
- Modify: `src/AkmlSql.Shell.Shared/AkmlSql.Shell.Shared.projitems`

Per spec §8.6:

```
Imported "MyTeamStyle.sqlpromptstylev2" as "MyTeamStyle"
✓ 47 settings translated
⚠ 8 settings not yet supported by AKML — see details
   • InsertStatements.AlignAssignmentOperators (using AKML default)
   • CTE.AlignCommas (using AKML default)
   ...
   [ Show all ]   [ Open in Editor ]   [ OK ]
```

- [ ] **Step C.2.1: Create the dialog**

`RedgateImportResultDialog : Window` (themed via `PageTheme` from Phase 2 if possible, or `ThemeManager` directly). Layout:

- Header: "Imported '<source filename>' as '<style name>'"
- Stats line: "✓ <N> settings translated   ⚠ <M> settings not yet supported"
- Scrollable warnings list (only show first 5; if more, "Show all" button expands)
- Bottom buttons: "Open in Editor" (returns DialogResult and selects new style in editor), "OK" (just closes)

Receives `SqlPromptImportResult` + source filename + new style name as constructor args.

- [ ] **Step C.2.2: Wire the Import button in `ProfileEditorDialog`**

```csharp
private void OnImportClick(object? sender, RoutedEventArgs e)
{
    var dlg = new Microsoft.Win32.OpenFileDialog
    {
        Title = "Import Style",
        Filter = "AKML Style (*.akmlstyle)|*.akmlstyle|" +
                 "Redgate Style v2 (*.sqlpromptstylev2)|*.sqlpromptstylev2|" +
                 "All Supported (*.akmlstyle;*.sqlpromptstylev2)|*.akmlstyle;*.sqlpromptstylev2"
    };
    if (dlg.ShowDialog() != true) return;

    var path = dlg.FileName;
    if (path.EndsWith(".sqlpromptstylev2", StringComparison.OrdinalIgnoreCase))
    {
        var result = SqlPromptImporter.Import(path);
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        result.Profile.Metadata.Name = name;
        result.Profile.Metadata.IsBuiltIn = false;
        _profileManager.Save(result.Profile);
        _viewModel.RefreshStyleLists();
        _viewModel.SelectedStyle = _viewModel.UserStyles.First(s => s.Name == name);

        var resultDlg = new RedgateImportResultDialog(result, System.IO.Path.GetFileName(path), name) { Owner = this };
        resultDlg.ShowDialog();
    }
    else
    {
        // Native .akmlstyle — let ProfileManager.Import handle it directly
        var imported = _profileManager.Import(path);
        _viewModel.RefreshStyleLists();
        _viewModel.SelectedStyle = _viewModel.UserStyles.First(s => s.Name == imported.Metadata.Name);
    }
}
```

- [ ] **Step C.2.3: Register the new file in `.projitems`**

```xml
<Compile Include="$(MSBuildThisFileDirectory)Ui\RedgateImportResultDialog.cs" />
```

- [ ] **Step C.2.4: Test (no automated UI test — chrome tests are too coarse)**

Manual smoke test deferred to user. Add a unit test for the Import button's integration logic if you can extract the file-handling part:

```csharp
[Fact]
public void ImportRedgateFile_WritesProfileAndReportsWarnings()
{
    // Use a fixture .sqlpromptstylev2 file from tests/.../Fixtures/
    var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(tempDir);
    var manager = new ProfileManager(tempDir);
    var (profile, warnings) = ImportRedgateForTest("Fixtures/RedgateDefault.sqlpromptstylev2", manager);
    Assert.True(warnings.MappedCount > 0);
    Assert.True(File.Exists(Path.Combine(tempDir, "RedgateDefault.akmlstyle")));
}
```

- [ ] **Step C.2.5: Build + commit**

```bash
"$MSBUILD" src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj -t:Restore,Build -p:Configuration=Release -v:minimal
dotnet test tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj
```

```bash
git add src/AkmlSql.Shell.Shared/Ui/RedgateImportResultDialog.cs \
        src/AkmlSql.Shell.Shared/Ui/ProfileEditorDialog.cs \
        src/AkmlSql.Shell.Shared/AkmlSql.Shell.Shared.projitems
```

Message:

```
Add post-import warnings dialog for Redgate styles (Phase 3 C.2)

After importing a .sqlpromptstylev2 from the Style List toolbar, the
RedgateImportResultDialog summarises:
- Source filename → new style name
- N settings translated (no warning each)
- M settings unsupported / lossy with one row per warning
- "Open in Editor" / "OK" buttons

Native .akmlstyle imports skip the dialog (no translation losses).

Refs: docs/superpowers/specs/2026-05-06-options-dialog-redgate-parity-design.md §8.6
      docs/superpowers/plans/2026-05-08-options-dialog-phase3.md C.2
```

**Ask the user:** "Redgate import dialog ready. Approve commit?"

---

# BLOCK D — Environment color editor sub-dialog

The existing inline ColoringRules ListBox + Add/Edit/Remove on `TabsPage` (Phase 2 B.15) is functional but cramped. Spec §8.7 wants a dedicated sub-dialog with more room.

## Task D.1: Build the `EnvironmentColorEditorDialog`

**Files:**
- Create: `src/AkmlSql.Shell.Shared/Dialogs/EnvironmentColorEditorDialog.cs`
- Modify: `src/AkmlSql.Shell.Shared/AkmlSql.Shell.Shared.projitems`

> **CORRECTION from spec §8.7:** The spec describes a two-tier model (named `Environment` entities + separate `Assignment` rules referring to them). Today's `EnvironmentRule` is a flat list (label + pattern + color + match-target). Migrating to two-tier is a significant data model change with `config.json` migration implications. **Recommendation: keep flat for Phase 3.** The dialog can present a polished single-table view of the existing `ColoringRules` collection without forcing a model change. If the two-tier model is wanted, raise it as a Phase 4 follow-up.

- [ ] **Step D.1.1: Build the dialog**

Layout (single-tier flat list, ~680×520):

```
┌─ Environment Tab Colors ────────────────────────[680×520]┐
│  Coloring Rules                                          │
│  ┌──────────────┬──────────────┬──────────┬──────────┐   │
│  │ Label        │ Match On     │ Pattern  │ Color    │   │
│  ├──────────────┼──────────────┼──────────┼──────────┤   │
│  │ Production   │ Server name  │ *prod*   │ #E74C3C  │   │
│  │ Staging      │ Database     │ *_stg    │ #F39C12  │   │
│  │ ...          │              │          │          │   │
│  └──────────────┴──────────────┴──────────┴──────────┘   │
│  [+ Add]  [✎ Edit]  [↑ Up]  [↓ Down]  [✕ Remove]         │
│                                                          │
│  ☑ Use gradient colors on tabs                          │
│  Reminder: rules are evaluated top-down; first match wins│
│                                                          │
│                              [Apply]  [OK]  [Cancel]     │
└──────────────────────────────────────────────────────────┘
```

Use a `DataGrid` bound to `ObservableCollection<ColoringRule>`. Reorder buttons (Up/Down) update the `Order` field on each rule.

- [ ] **Step D.1.2: Reuse the existing rule-editor sub-dialog**

`SettingsWindow.ShowRuleEditor(ColoringRule rule, string title)` already exists (still kept around even after Phase 2 migration). Move it OUT of `SettingsWindow` into a new `EnvironmentRuleEditDialog` so the new env-editor can call it directly without depending on `SettingsWindow`.

```bash
grep -n "ShowRuleEditor" src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs
```

Lift to: `src/AkmlSql.Shell.Shared/Dialogs/EnvironmentRuleEditDialog.cs`. Preserve the body verbatim. `SettingsWindow.OnEditColoringRule` etc. will now construct `new EnvironmentRuleEditDialog(rule, title) { Owner = _window }` instead.

- [ ] **Step D.1.3: Replace inline editor on `TabsPage`**

In `TabsPage.cs` Build:
- Delete the inline ListBox + Add/Edit/Remove buttons
- Replace with a single `[Manage Environment Colors…]` button
- The button click handler (wired in `SettingsWindow.BuildPages` like the existing General theme dropdown) opens the new `EnvironmentColorEditorDialog`

`TabsControls.ColoringRulesList` becomes obsolete — `SettingsWindow.GetColoringRulesList()` and `PopulateColoringRulesList()` can be deleted along with the ListBox-based Add/Edit/Remove handlers (they're no longer reachable).

The new flow:
- Click `[Manage Environment Colors…]` → opens `EnvironmentColorEditorDialog` modal with a copy of `_settings.Tabs.ColoringRules`
- User edits, clicks Apply or OK → dialog returns `DialogResult.true` and the updated list
- `SettingsWindow` writes the updated list back to `_settings.Tabs.ColoringRules`

- [ ] **Step D.1.4: Register file + build**

```xml
<Compile Include="$(MSBuildThisFileDirectory)Dialogs\EnvironmentColorEditorDialog.cs" />
<Compile Include="$(MSBuildThisFileDirectory)Dialogs\EnvironmentRuleEditDialog.cs" />
```

```bash
"$MSBUILD" src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj -t:Restore,Build -p:Configuration=Release -v:minimal
dotnet test tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj
```

The chrome tests should still pass — Tabs page now has fewer inline controls but the `Restore Defaults` link still exists (from `AddPageHeader`).

- [ ] **Step D.1.5: Test (round-trip persistence)**

```csharp
[Fact]
public void EnvironmentColorEditor_AddingRule_PersistsToSettings()
{
    var settings = new AppSettings();
    var initialCount = settings.Tabs.ColoringRules.Count;
    var dlg = new EnvironmentColorEditorDialog(settings.Tabs.ColoringRules);
    // Programmatically click Add, fill in, click OK — drive via reflection or
    // expose an internal method like dlg.AddRuleForTest(...) that mirrors the
    // user click path.
    dlg.AddRuleForTest(new ColoringRule { Label = "Test", Pattern = "test*", Color = "#FF0000" });
    dlg.AcceptForTest();
    Assert.Equal(initialCount + 1, settings.Tabs.ColoringRules.Count);
}
```

- [ ] **Step D.1.6: Commit**

```bash
git add src/AkmlSql.Shell.Shared/Dialogs/EnvironmentColorEditorDialog.cs \
        src/AkmlSql.Shell.Shared/Dialogs/EnvironmentRuleEditDialog.cs \
        src/AkmlSql.Shell.Shared/Dialogs/Pages/TabsPage.cs \
        src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs \
        src/AkmlSql.Shell.Shared/AkmlSql.Shell.Shared.projitems \
        tests/...
```

Message:

```
Add Environment Color Editor sub-dialog (Phase 3 D.1)

The inline coloring rules list on Tabs › UI is replaced by a single
[Manage Environment Colors…] button that opens a dedicated 680×520
sub-dialog with a DataGrid + Up/Down reorder buttons.

ShowRuleEditor (the existing per-rule editor) is lifted out of
SettingsWindow into Dialogs/EnvironmentRuleEditDialog.cs so the new
sub-dialog can use it without depending on SettingsWindow.

Tabs.ColoringRules data model unchanged — single-tier flat list. The
spec's two-tier Environment / Assignment split is deferred to a future
phase.

SSMS22 builds clean. Chrome tests pass. Round-trip persistence test
added.

Refs: docs/superpowers/specs/2026-05-06-options-dialog-redgate-parity-design.md §8.7
      docs/superpowers/plans/2026-05-08-options-dialog-phase3.md D.1
```

**Ask the user:** "Environment color editor ready. Approve commit?"

---

# BLOCK E — Format › Styles slim + final polish

## Task E.1: Slim Format › Styles page + add Active Style controls

**Files:**
- Modify: `src/AkmlSql.Shell.Shared/Dialogs/Pages/FormattingPage.cs`
- Modify: `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs` (post-Build event hookup for the new Edit button)

The current `FormattingPage` (Phase 2 B.14) has 4 trigger toggles + 4 safety/validation toggles. Spec §8.1 wants an "Active style" dropdown + "Edit Formatting Styles…" button at the top.

- [ ] **Step E.1.1: Add the Active Style group**

In `FormattingPage.Build`, BEFORE the "Triggers" group:

```csharp
ctx.Rows.AddGroupHeader(panel, "Active Style");

var (rowActive, cboActive) = ctx.Rows.AddDropdown(panel,
    "Active style",
    new[] { /* placeholder; populated by FormattingControls.Load from ProfileManager */ },
    "The formatting style currently in use. Built-in styles are read-only — pick Edit to copy and customize.");
ctx.RegisterSearch("Active style", "The formatting style currently in use", "Dropdown", rowActive);

var editButton = new Button {
    Content = "Edit Formatting Styles…",
    /* ... themed via ctx.Theme ... */
};
panel.Children.Add(editButton);
// editButton.Click is wired by the host via FormattingControls.EditButton

ctx.Rows.AddGroupSeparator(panel);
ctx.Rows.AddGroupHeader(panel, "Triggers");
// ... existing 4 trigger toggles ...
```

- [ ] **Step E.1.2: Update `FormattingControls`**

Expose `ActiveStyle: ComboBox` and `EditButton: Button` publicly (similar to `GeneralControls.Theme` from Phase 2 B.7). Load populates the dropdown items from `ProfileManager.GetAll()` and selects the current `Formatter.ActiveProfile`. Save writes the selected item back.

```csharp
public ComboBox ActiveStyle { get; }
public Button EditButton { get; }

public void Load(AppSettings settings)
{
    var pm = new ProfileManager();
    var all = pm.GetAll().Select(p => p.Metadata.Name).OrderBy(n => n).ToList();
    ActiveStyle.Items.Clear();
    foreach (var n in all) ActiveStyle.Items.Add(n);
    ActiveStyle.SelectedItem = settings.Formatter.ActiveProfile;
    // ... existing 8 toggle Loads ...
}

public void Save(AppSettings settings)
{
    settings.Formatter.ActiveProfile = ActiveStyle.SelectedItem as string ?? "Default";
    // ... existing 8 toggle Saves ...
}
```

- [ ] **Step E.1.3: Wire EditButton in `SettingsWindow.BuildPages`**

```csharp
if (controls is FormattingControls fmt)
{
    fmt.EditButton.Click += (_, _) => OpenStyleEditor();
}

private void OpenStyleEditor()
{
    var vm = new ProfileEditorViewModel(/* ... */);
    var dlg = new ProfileEditorDialog(vm) { Owner = _window };
    dlg.ShowDialog();
    // Re-load FormattingControls after the editor closes (the active style may have changed)
    if (_pageControlsByKey.TryGetValue("Formatting", out var c)) c.Load(_settings);
}
```

- [ ] **Step E.1.4: Build + chrome tests**

```bash
"$MSBUILD" src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj -t:Restore,Build -p:Configuration=Release -v:minimal
dotnet test tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj
```

Expected: 0 errors, 5/5 chrome tests pass (the existing Phase 2 set still applies — no new pages added so the reset-coverage test is unchanged).

- [ ] **Step E.1.5: Commit**

```bash
git add src/AkmlSql.Shell.Shared/Dialogs/Pages/FormattingPage.cs \
        src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs
```

Message:

```
Slim Format › Styles page + add Active Style controls (Phase 3 E.1)

Per spec §8.1, the page now opens with an "Active Style" group:
- Dropdown listing every style from ProfileManager (built-ins + user)
- "Edit Formatting Styles…" button that opens ProfileEditorDialog

The existing Triggers + Safety/Validation groups (8 toggles) stay below
— those are workflow settings, not formatting rules, so they belong on
the page rather than in the editor.

FormattingControls exposes ActiveStyle (ComboBox) and EditButton (Button)
publicly so SettingsWindow can wire the Edit click handler post-Build,
matching the pattern established for GeneralControls.Theme in Phase 2 B.7.

SSMS22 builds clean. 5/5 chrome tests pass.

Refs: docs/superpowers/specs/2026-05-06-options-dialog-redgate-parity-design.md §8.1
      docs/superpowers/plans/2026-05-08-options-dialog-phase3.md E.1
```

**Ask the user:** "Format › Styles slim ready. Approve commit?"

---

## Task E.2: Final test pass + plan close-out

- [ ] **Step E.2.1: Run the full test pipeline**

```bash
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj
dotnet test tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj
dotnet test tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj
```

Expected: all green. Engine.Tests should be 904 (from Phase 2 close), Formatting.Tests should have grown by ~7-10 (built-in tests, viewmodel CRUD, importer warnings).

If anything fails: stop, fix, recommit before declaring Phase 3 done.

- [ ] **Step E.2.2: Manual smoke test (deferred to user)**

Phase 3 has the highest visual surface change of the three phases. Recommend the user smoke-test:

1. **Open Format › Styles** — confirm Active Style dropdown lists all built-ins + user styles, Edit button opens the editor
2. **Click "Edit Formatting Styles…"** — confirm 3-column layout renders at 1280×750
3. **Click a built-in (e.g. Aligned)** — confirm middle column controls go disabled, lock banner appears
4. **Click Copy → enter "MyTest" → OK** — confirm new entry appears under Your Styles, becomes selected, controls enabled
5. **Edit a setting (e.g. tabSize)** — confirm Save & Apply persists; reopen the editor, confirm setting kept
6. **Click another style** while dirty — confirm save/discard/cancel prompt fires
7. **Import a `.sqlpromptstylev2` file** — confirm the new RedgateImportResultDialog opens with translated/unsupported counts
8. **Open Tabs › UI → Manage Environment Colors…** — confirm sub-dialog opens with existing rules
9. **Add a coloring rule, click OK** — reopen the dialog, confirm the rule persists
10. **Active Style dropdown on Format › Styles** — switch to Compact, click OK on Options, reopen → confirm formatter now uses Compact

If anything's off, the bug is most likely in: ProfileEditorViewModel's switch logic (B.3), the Style List ItemTemplate bindings (B.1), or the new env editor's data round-trip (D.1).

- [ ] **Step E.2.3: Tag the close**

If smoke test passes:

```bash
git tag -a phase3-complete -m "Phase 3 complete: 3-col Style Editor, Redgate warnings UI, env color editor, Format page slim"
```

Optional. Helps locate the close-of-phase commit later.

**Block E complete. Phase 3 complete.**

---

## Phase 3 final test summary

| Test | Block | Coverage |
|---|---|---|
| `BuiltInStylesTests.AllBuiltIns_LoadCleanly` | A | Schema validity for 8 built-ins |
| `ProfileEditorViewModelTests.RefreshStyleLists_PopulatesBothCollections` | A | Built-in/user split |
| `ProfileEditorViewModelTests.SwitchingDirty_RaisesOnSwitchWhileDirty` | A | Dirty event |
| `ProfileEditorViewModelTests.SetActiveStyle_WritesAppSettings` | A | Active style write-through |
| `ProfileEditorViewModelTests.CreateStyle_AddsToUserStyles` | B | Create CRUD |
| `ProfileEditorViewModelTests.RenameActive_UpdatesAppSettings` | B | Rename atomic w/ active |
| `ProfileEditorViewModelTests.DeleteActive_IsRejected` | B | Delete protection |
| `ProfileEditorViewModelTests.SwitchingDirty_Save_PersistsBeforeSwitching` | B | Dirty Save path |
| `SqlPromptImporterTests.Import_DirectMapping_ProducesNoWarning` | C | Direct tier silent |
| `SqlPromptImporterTests.Import_CompatibleMapping_EmitsWarning` | C | Compatible tier emits |
| `SqlPromptImporterTests.Import_UnmappedOption_AppearsInWarnings` | C | Unmapped tier emits |
| `EnvironmentColorEditorTests.AddingRule_PersistsToSettings` | D | Round-trip |

**Net: ~12 new tests** in `tests/AkmlSql.Formatting.Tests/Profiles/` and `tests/AkmlSql.Shell.Shared.Tests/`.

Pre-existing test counts (Phase 2 close):
- Engine.Tests: 904 → still 904 (no engine changes)
- Core.Tests: 9 → still 9
- Shell.Shared.Tests: 5 chrome tests → still 5

---

## Phase 3 risks (carried forward from spec §9)

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| **R1.** Real-world `.sqlpromptstylev2` schema drift between Redgate v9/v10/v11 | Medium | Medium | A.1 spike validates against samples spanning versions. Cap at v10+ if v9 differs. The existing importer already handles v2 XML; tracker for v3/v4 if Redgate ships them. |
| **R3.** Built-in style content tuning (Aligned/Verbose/Redgate Compatible) doesn't match users' expectations | Medium | Low | A.2.4 says "best-effort" for Redgate Compatible — users can refine via the editor. For Aligned/Verbose, ship a 30-line canonical SQL fixture in the test suite (`AllBuiltInsFormatSampleSql`) and visual-review in PR. |
| **R-new.** ProfileEditorDialog 3-col layout breaks at small window sizes | Low | Low | Use `MinWidth = 1100` so the user can shrink but not below the original 2-col size. The Splitter columns let users re-balance. |
| **R-new.** Two-tier Environment/Assignment migration (D.1 deferral) becomes a regret later | Low | Low | Documented as a Phase 4 follow-up. The flat model has shipped successfully through Phase 2; users haven't asked for the two-tier shape. |

---

## How to execute this plan

For each Block:
1. Read the Block's tasks top to bottom before starting any code
2. Execute one task at a time
3. After each task's commit step, **stop and ask the user for approval** — Phase 2 established that this user is firm on the no-auto-commit rule
4. Run build + tests as each step says — don't batch them

For Phase 3 specifically:
- Block A is mostly recon and small JSON authoring — fast
- Block B is the largest (3-column UI restructure + 6 toolbar buttons + dirty prompt) — pace yourself
- Block C depends on Block A's `OptionMap` audit findings — don't skip the spike
- Block D is independent of A/B/C — can ship whenever
- Block E ties everything together; do it last so the editor and env-editor are both real before the Format page is slimmed

---

**End of Phase 3 plan.** Estimated total: 4 days focused work, 13 commits across 5 blocks, ~12 new tests.
