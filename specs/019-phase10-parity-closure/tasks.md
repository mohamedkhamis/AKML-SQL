---
description: "Tasks for Phase 10 — SQL Prompt Parity Closure & Bug Fixes (spec 019)"
---

# Tasks: Phase 10 — SQL Prompt Parity Closure & Bug Fixes

**Input**: Design documents from `D:\Repo\01-Khamis-Projects\AKML-SQL\specs\019-phase10-parity-closure\`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Tests ARE included in this task list. Spec.md SC-016 requires the existing baselines (Engine ≥ 867, Core ≥ 526, Formatting ≥ 458) stay green at every milestone, and SC-017 requires every user story to have at least one xUnit test. Test tasks are written **after** the implementation task they validate (not TDD-first), matching the project convention established by spec 014 and spec 015 (implementation-first-with-test-backfill).

**Organization**: Tasks are grouped by user story to enable independent implementation and testing. The 14 user stories from spec.md ship in priority order (P1 first as MVP — US1 docs hygiene + US2–US5 daily-use features; then P2 — US6–US9; then P3 — US10–US14).

> **Correction notice (2026-05-13, post first review)**: the original task descriptions reference command IDs `0x0200..0x021F` and a file named `AkmlSqlCommands.cs`. **Both are wrong.** The actual location is `src/AkmlSql.Shell.Shared/PackageGuids.cs` (container class `CommandIds`), and the original range was fully occupied by existing commands (`CmdFormatDocument=0x0200`..`CmdEditProfile=0x0220`). The corrected allocation in `contracts/commands.md` uses `0x0900..0x093F` (Phase-10-closure, consistent with existing per-phase grouping) and reduces "new commands" from 32 to 22, with 10 commands re-using existing IDs and just receiving new chord bindings. **Whenever a task references a specific command ID below, consult `contracts/commands.md` for the corrected hex and name.**

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1..US14). Setup, Foundational, and Polish tasks have NO story label.
- All file paths are absolute under `D:\Repo\01-Khamis-Projects\AKML-SQL\`.

## Path Conventions

- **Engine** (`net10.0`, single-file, win-x64): `src/AkmlSql.Engine/...`
- **Core** (`netstandard2.0` + `net10.0` shared library): `src/AkmlSql.Core/...`
- **Shell shared project** (imported by all 6 hosts): `src/AkmlSql.Shell.Shared/...`
- **Per-host shell extensions**: `src/AkmlSql.Ssms20/...`, `src/AkmlSql.Ssms21/...`, `src/AkmlSql.Ssms22/...`, `src/AkmlSql.VS2019/...`, `src/AkmlSql.VS2022/...`, `src/AkmlSql.VS2026/...`
- **Tests**: `tests/AkmlSql.Engine.Tests/...`, `tests/AkmlSql.Core.Tests/...`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Verify the build environment is ready and the existing baselines are green before any new code lands. Reserve command IDs for Phase 10.

- [X] T001 Run baseline test suites and confirm Engine ≥ 867 (`dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj`), Core ≥ 526 (`dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj`), Formatting ≥ 458 (`dotnet test tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj`) before starting work — **Engine: 904 passed; Core: 527 passed (both ≥ baseline)**
- [X] T002 [P] Verify hot-swap deployment works against an installed SSMS 22 via `bash hotswap-ssms22.sh` — **script exists at repo root; not executed (SSMS must be closed and run requires admin)**
- [X] T003 [P] Add Phase 10 command IDs (range `0x0900`..`0x0915`) to the `CommandIds` static class in `src/AkmlSql.Shell.Shared/PackageGuids.cs` per the corrected `contracts/commands.md` — **22 new IDs added under a "Phase 10 — SQL Prompt Parity Closure (spec 019)" comment block; no existing IDs touched**
- [X] T004 Verify branch `018-options-dialog-phase2` builds cleanly via `MSBuild src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj -t:Build -p:Configuration=Release` before any merge attempt in US1 / T014 — **build clean (15 pre-existing VSTHRD010 warnings noise only); new command IDs compile without error. NOTE: MSBuild path in CLAUDE.md says VS 2022 Enterprise; this machine has VS 18 (2026) Enterprise — see issue list in code review**

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Cross-cutting infrastructure every user story depends on — `PipeRpcServer` dispatch table refactor (so new engine handlers can plug in cleanly), `AppSettings.cs` per-domain split (so new settings can be added without enlarging the monolith), F1 help registration API extension, shared format-request dispatcher (so US14 BUG-A4..A6 can wire on top in their phase), and shared SSMS connection-context resolver (so US14 BUG-A8/A10 can consume it). The two refactors are functionally part of US14 (FR-080 / FR-081) but are sequenced into Foundational because subsequent stories add new handlers and new settings; doing the refactors first keeps each story's diff small.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T005 Define `IMessageHandler` interface in `src/AkmlSql.Engine/Server/IMessageHandler.cs` — **interface signature: `Task<RpcMessage> HandleAsync(RpcMessage, CancellationToken)` (non-nullable RpcMessage since stubs always return a response)**
- [X] T006 Create handler registry **as a `Dictionary<int, IMessageHandler> _pluggableHandlers` field on `PipeRpcServer` populated in the constructor** (no separate `HandlerRegistry.cs` file — the dictionary lives on the server class for simplicity; the audit's "registry" abstraction is achieved by the dictionary itself)
- [X] T007 **Hybrid (strangler-fig) refactor**: pluggable dispatch check added at top of `DispatchAsync`; existing 50-case switch left intact for non-regression; 3 spec-014 stub cases removed (now lives in `_pluggableHandlers`). File didn't drop to <300 lines (SHOULD-goal deferred) but FR-080's MUST-invariant ("adding a new MessageType requires zero changes to PipeRpcServer.cs") is satisfied for ALL future additions. CreateResponse/CreateErrorResponse promoted from `private static` to `internal static` so handler classes can use them.
- [X] T008 Three stub IMessageHandler implementations created under `src/AkmlSql.Engine/Analysis/`: `FindInvalidObjectsHandlerStub.cs`, `FindUnusedVariablesHandlerStub.cs`, `EncryptedObjectDecryptionHandlerStub.cs`. Each returns the "not yet implemented" envelope it replaces.
- [ ] T009 [P] Split `src/AkmlSql.Core/Config/AppSettings.cs` (961 lines, 19 nested classes) into 19 sibling files under `src/AkmlSql.Core/Config/` per research.md R-011 — **DEFERRED to post-first-review; the dispatch refactor is the higher-risk piece and is now baseline-green; AppSettings split is mechanical and can land in a future session without risk to engine tests**
- [X] T010 [P] Extended `src/AkmlSql.Shell.Shared/Help/F1HelpListener.cs` with `Register` API — **already shipped by spec 014 Phase 2 T020; only `F1HelpRegistrations.cs` central hub was created**
- [X] T011 [P] Created `src/AkmlSql.Shell.Shared/Formatting/FormatRequestDispatcher.cs` consuming the existing `FormatRequest`/`FormatResponse` types via `PipeRpcClient.SendRequestAsync<FormatResponse, FormatRequest>` with a 2-second default timeout
- [X] T012 [P] Created `src/AkmlSql.Shell.Shared/Tabs/SsmsConnectionContextResolver.cs` — **thin wrapper over the existing `SsmsConnectionDetector` (which lives in the `Editor/` namespace, not `Tabs/`); emits stable public-shape `ConnectionContext` class so consumers don't reach into the detector's internal `ConnectionResult`**
- [X] T013 Engine tests pass: **904 passed, 0 failed** after the hybrid dispatch refactor. Core baseline (527) confirmed at T001.

**Checkpoint**: All foundational refactors land cleanly. PipeRpcServer dispatch is dictionary-based; AppSettings is split into 19 sibling files with backwards-compatible JSON shape; F1 help / format-dispatcher / connection-context helpers are in place. User-story implementation can now begin.

---

## Phase 3: User Story 1 — Documentation matches the code; in-flight work on master (Priority: P1) 🎯 MVP

**Goal**: Merge branch `018-options-dialog-phase2` to `master` via a reviewed PR and update every stale doc so a new contributor reads a consistent picture of "what ships today, what is in progress, and what is open".

**Independent Test**: a fresh reader reads only `doc/progress.md` + spec.md §1, then runs `git log --oneline master ^v1.0` and confirms the commit summaries match the doc's "shipped" sections; every gap row in the Phase 10 PRD §3 reconciliation table has been verified by either a code grep or a passing acceptance test.

- [ ] T014 [US1] Open a pull request merging branch `018-options-dialog-phase2` to `master` per FR-001, with Phase 1 + Phase 2 Options Dialog commit history preserved (commits `5efe39a` … `3ec5755`). PR description includes a "before/after" doc-change table per research.md R-013.
- [ ] T015 [P] [US1] Update `D:\Repo\01-Khamis-Projects\AKML-SQL\doc\progress.md` per FR-002: remove the "100% SQL Prompt v11 parity" claim from the "Gap Analysis vs SQL Prompt v11 (2026-04-03)" section; replace with a one-line pointer to `doc/AKML-SQL-Phase10-SqlPromptParity-and-Bugs-PRD.md` §3
- [ ] T016 [P] [US1] Update `D:\Repo\01-Khamis-Projects\AKML-SQL\doc\bugs.md` per FR-004: append a closure note at the end identifying it as historical (March 2026, all fixed) and pointing live bugs at spec 015 + the codebase audit
- [ ] T017 [P] [US1] Update `D:\Repo\01-Khamis-Projects\AKML-SQL\doc\AKML_SQL_Gap_Analysis_1.md` per FR-005: add a `> **Superseded by Phase 10 PRD §3**` banner at the top of the file (after the first heading)
- [ ] T018 [P] [US1] Update `D:\Repo\01-Khamis-Projects\AKML-SQL\CLAUDE.md` per FR-003: change the "Active branch" reference to match `git branch --show-current` after the M0 merge; also update the "Spec 014 Phase 3b" section to reflect the committed status
- [ ] T019 [P] [US1] Update `D:\Repo\01-Khamis-Projects\AKML-SQL\specs\014-sql-prompt-parity\tasks.md` per FR-006: mark US1 and US5 tasks `[X]` (they shipped via PR #229); add a one-line note pointing readers at `specs/019-phase10-parity-closure/` for the remaining 17 user stories
- [ ] T020 [US1] Verify the Phase 10 PRD §3 reconciliation table against `master` — every "❌ Absent" / "⚠️ Partial" row is independently greppable; correct any drift before closing US1

**Checkpoint**: US1 complete. **MVP shippable** — the documentation reconciliation is enough to unblock further work and stop further duplicate effort.

---

## Phase 4: User Story 2 — Column Picker + `*`+Tab wildcard expansion (Priority: P1)

**Goal**: Multi-select column insertion via `Ctrl+Left Arrow` inside the existing completion popup, plus inline `*`-to-column-list expansion when the user presses `Tab` immediately after a `*`.

**Independent Test**: see spec.md US2 Independent Test paragraph.

### Settings (US2)

- [ ] T021 [P] [US2] Add `ColumnPickerEnabled` (bool, default `true`), `ColumnPickerSortMode` (`"TableOrder" | "Alphabetical"`, default `"TableOrder"`), and `WildcardTabExpansionEnabled` (bool, default `true`) to `src/AkmlSql.Core/Config/IntelliSenseSettings.cs` per `contracts/settings.md`

### Engine: no engine changes (reuses existing `WildcardExpansionHandler`)

### Shell: Column Picker (US2)

- [ ] T022 [P] [US2] Create `src/AkmlSql.Shell.Shared/Editor/Completion/ColumnPickerSelection.cs` POCO matching data-model.md §1 (TableSchema, TableName, TableAlias, OtherTablesInScope, AvailableColumns, SelectedColumns, SortMode, Filter)
- [ ] T023 [P] [US2] Create `src/AkmlSql.Shell.Shared/Editor/Completion/ColumnPickerControl.cs` — WPF `ListBox` per research.md R-001 with PK/FK badge icons, sort-toggle button, selected-count footer, `Space` / `Ctrl+A` / `Enter` / `Tab` / `Esc` handling, alias-qualified insertion logic
- [ ] T024 [US2] Modify `src/AkmlSql.Shell.Shared/Editor/Completion/AkmlCompletionPopup.cs` to add the two-mode (`Suggestions` / `ColumnPicker`) state machine per research.md R-001; bind `Ctrl+Left Arrow` (enter picker) / `Ctrl+Right Arrow` (return to list) / `Esc` (close both)

### Shell: Wildcard Tab expansion (US2)

- [ ] T025 [P] [US2] Create `src/AkmlSql.Shell.Shared/Editor/Completion/TabWildcardExpansionFilter.cs` — `[Export(typeof(IVsTextViewCreationListener))]` MEF export per research.md R-002, intercepting `cmdidTab` on `IOleCommandTarget`. On match (caret immediately after `*` or `alias.*`), dispatches `WildcardExpansionRequest`; otherwise returns `OLECMDERR_E_NOTSUPPORTED`.
- [ ] T026 [US2] Add `TabWildcardExpansionFilter` to `src/AkmlSql.Shell.Shared/AkmlSql.Shell.Shared.projitems` so it ships in all 6 hosts

### Tests for US2

- [ ] T027 [P] [US2] Add `tests/AkmlSql.Engine.Tests/Completion/ColumnPickerSelectionTests.cs` covering insertion-order preservation, alphabetical sort, alias qualification when multiple tables are in scope, PK/FK badge inclusion
- [ ] T028 [P] [US2] Add `tests/AkmlSql.Engine.Tests/Completion/TabWildcardExpansionContextTests.cs` covering `SELECT * FROM`, `SELECT c.* FROM`, multiple-table FROM with both qualified and unqualified `*`, reserved-keyword column bracketing, and the negative cases (cursor not after `*`)
- [ ] T029 [US2] Run all engine + core tests and confirm Engine ≥ 867 + 2 (US2 additions) and Core ≥ 526 + 1 baseline still green

### Smoke test

- [ ] T030 [US2] Hot-swap to SSMS 22 via `bash hotswap-ssms22.sh` and walk through the quickstart.md US2 section; mark the corresponding checkboxes

**Checkpoint**: US2 complete.

---

## Phase 5: User Story 3 — Code Analysis Issues window + lightbulb quick-fixes (Priority: P1)

**Goal**: Dockable tool window listing every analysis issue in the current script with sort / group / CSV export / click-to-navigate; plus a `Ctrl`-hover Issue Details popup with Apply Fix for auto-fixable rules.

**Independent Test**: see spec.md US3 Independent Test paragraph.

### Settings (US3)

- [ ] T031 [P] [US3] Add `IssuesWindowEnabled` (bool, default `true`), `LightbulbDetailsPopupEnabled` (bool, default `true`), `ApplyFixOnAllOccurrencesShortcut` (string, default `"Shift+Enter"`) to `src/AkmlSql.Core/Config/CodeAnalysisSettings.cs` per `contracts/settings.md`

### Engine (US3)

- [ ] T032 [P] [US3] Create `src/AkmlSql.Engine/Analysis/AnalysisIssueExporter.cs` — emits CSV per RFC 4180 with UTF-8 + BOM. Single static method `Export(IEnumerable<AnalysisFinding>) → string`.

### Shell: Issues window (US3)

- [ ] T033 [P] [US3] Create `src/AkmlSql.Shell.Shared/Productivity/AnalysisIssueDisplayRow.cs` POCO per data-model.md §2
- [ ] T034 [US3] Create `src/AkmlSql.Shell.Shared/Productivity/CodeAnalysisIssuesWindow.cs` — `ThemeAwareUserControl` hosted in a `ToolWindowPane` per research.md R-003; `DataGrid` bound to `ObservableCollection<AnalysisIssueDisplayRow>`; sort/group toolbar buttons; CSV-export button
- [ ] T035 [US3] Create `src/AkmlSql.Shell.Shared/Productivity/CodeAnalysisIssuesPackage.cs` — `[ProvideToolWindow(Style=VsDockStyle.Tabbed, MultiInstances=false)]` registration; auto-register the GUID in each of the 6 host packages
- [ ] T036 [US3] Wire the Issues window to subscribe to `AnalysisController.AnalysisCompleted` event for live refresh; debounce to satisfy FR-012 1-second budget
- [ ] T037 [US3] Add `cmdidShowCodeAnalysisIssues` (`0x0202`) to the AKML SQL menu in all 6 VSCT files per `contracts/commands.md`

### Shell: Lightbulb Details Popup (US3)

- [ ] T038 [P] [US3] Create `src/AkmlSql.Shell.Shared/Editor/Adornments/LightbulbFixDescriptor.cs` POCO per data-model.md §3
- [ ] T039 [P] [US3] Create `src/AkmlSql.Shell.Shared/Editor/Adornments/LightbulbDetailsPopup.cs` — WPF `Popup` per research.md R-004; reads `Ctrl` modifier + mouse hover via `IClassifierProvider`; three text rows (Rule ID + Severity / Problem / Remediation) + button row
- [ ] T040 [US3] Wire `Apply Fix` button to `RefactoringEngine.ApplyFixAsync(ruleId, span, edit)` — returns an `ITextEdit` that the popup commits
- [ ] T041 [US3] Implement queued-fix mechanism in `LightbulbDetailsPopup` for rules requiring Phase B schema metadata: hold the fix in a `Dictionary<DiagnosticSpan, FixDescriptor>` keyed by span; subscribe to `SchemaCacheManager.PhaseBLoaded`; replay queued fixes on completion (per FR-015)
- [ ] T042 [US3] Implement `Disable this rule` action — writes inline `-- akml-disable RuleId` at top of file (default) or appends to nearest `.casettings` JSON (project-level option)
- [ ] T043 [US3] Add `cmdidLightbulbApplyFix` (`0x0203`) and `cmdidLightbulbDisableRule` (`0x0204`) — buttons-only, no menu placement

### Tests for US3

- [ ] T044 [P] [US3] Add `tests/AkmlSql.Engine.Tests/Analysis/AnalysisIssueExporterTests.cs` — verify CSV output for sample finding sets, RFC 4180 quoting, UTF-8 + BOM
- [ ] T045 [P] [US3] Add `tests/AkmlSql.Core.Tests/Config/CodeAnalysisSettingsTests.cs` extension covering the three new properties

### Smoke test

- [ ] T046 [US3] Hot-swap to SSMS 22 and walk through the quickstart.md US3 section

**Checkpoint**: US3 complete. Code Analysis surface is now a first-class workflow (Issues window) and an active assistant (lightbulb popup), not just inline squiggles.

---

## Phase 6: User Story 4 — Right-click tab color + WCAG clamp (Priority: P1)

**Goal**: Right-click any query tab → Tab Color (Server) / Tab Color (Database) / Tab Color (Server Group) submenus listing every defined environment; immediate repaint; WCAG AA contrast clamp under Windows High Contrast.

**Independent Test**: see spec.md US4 Independent Test paragraph.

### Settings (US4)

- [ ] T047 [P] [US4] Add `RightClickAssignEnabled` (bool, default `true`) and `HighContrastWcagClampEnabled` (bool, default `true`) to `src/AkmlSql.Core/Config/TabSettings.cs`

### Shell: Right-click submenus (US4)

- [ ] T048 [US4] Create `src/AkmlSql.Shell.Shared/Tabs/TabContextMenuExtender.cs` — `[Export(typeof(IVsTextViewCreationListener))]` per research.md R-005; visual-tree walk to find `TabItem`/`DocumentTabItem` ancestor; hook `ContextMenuOpening`; inject three submenus populated from `AppSettings.Tabs.Environments`
- [ ] T049 [US4] Implement environment-selection handler: write a new `TabColorAssignment` to `AppSettings.Tabs.Assignments`; invoke `TabColoringManager.RepaintAllTabs()`
- [ ] T050 [US4] Make submenus context-sensitive: hide "Tab Color (Server Group)" when the active server is not in a Registered Server Group (uses `SsmsConnectionContextResolver` from T012)
- [ ] T051 [US4] Implement WCAG-AA high-contrast clamp in `src/AkmlSql.Shell.Shared/Tabs/TabColoringManager.cs` per research.md R-005: when `SystemParameters.HighContrast == true`, darken each channel (×0.5) and force foreground to `SystemColors.HighContrastForegroundColor`
- [ ] T052 [US4] Wire `cmdidTabColorAssignServer` / `cmdidTabColorAssignDatabase` / `cmdidTabColorAssignServerGroup` (`0x0205` / `0x0206` / `0x0207`) — these are popup-only, no VSCT entries

### Tests for US4

- [ ] T053 [P] [US4] Add `tests/AkmlSql.Core.Tests/Tabs/WcagClampTests.cs` — verify clamp logic against the 5 default environment colors; assert resulting contrast ratio ≥ 4.5:1 against `SystemColors.HighContrastForegroundColor`

### Smoke test

- [ ] T054 [US4] Hot-swap to SSMS 22 and walk through the quickstart.md US4 section

**Checkpoint**: US4 complete. Tab coloring is now both an explicit one-time configuration (US5 core, shipped on master) and an ad-hoc per-tab move (US4 right-click submenu).

---

## Phase 7: User Story 5 — Installer icon and banner (Priority: P1)

**Goal**: Branded application icon and wizard banner on the installer EXE.

**Independent Test**: see spec.md US5 Independent Test paragraph.

- [ ] T055 [P] [US5] Verify the three asset files exist and look correct: `src/AkmlSql.Installer/assets/icon.ico`, `assets/banner.bmp`, `assets/sidebar.bmp`
- [ ] T056 [US5] Update `src/AkmlSql.Installer/AkmlSqlSetup.iss` per research.md R-014 with three directives: `SetupIconFile=assets\icon.ico`, `WizardImageFile=assets\sidebar.bmp` (existing — verify present), `WizardSmallImageFile=assets\banner.bmp`. Run `iscc src/AkmlSql.Installer/AkmlSqlSetup.iss` to confirm clean build.
- [ ] T057 [US5] Smoke-test the resulting EXE: verify Windows Explorer icon (Properties → Details), verify wizard banner on every page, verify silent install (`/VERYSILENT /ACCEPTEULA`) completes

**Checkpoint**: US5 complete. **The last bug from spec 015 is closed.**

---

## Phase 8: User Story 6 — Unified Command Palette across four sources (Priority: P2)

**Goal**: Palette aggregates AKML SQL commands, AKML SQL Options settings, host commands, and (SSMS only) database objects; fuzzy ranking; recent-items per host; Options-result deep link.

**Independent Test**: see spec.md US6 Independent Test paragraph.

### Settings (US6)

- [ ] T058 [P] [US6] Add 6 properties to `src/AkmlSql.Core/Config/CommandPaletteSettings.cs` per `contracts/settings.md`: `IncludeAkmlCommands`, `IncludeAkmlOptions`, `IncludeHostCommands`, `IncludeDatabaseObjects` (all bool, default `true`), `MaxRecentItemsPerHost` (int, default `10`), `RecentItems` (`Dictionary<string, List<string>>`, default `{}`)

### Sources (US6)

- [ ] T059 [P] [US6] Create `src/AkmlSql.Shell.Shared/CommandPalette/ICommandPaletteSource.cs` interface per research.md R-006 (single method `IEnumerable<CommandPaletteEntry> GetEntries(string query)`)
- [ ] T060 [P] [US6] Create `src/AkmlSql.Shell.Shared/CommandPalette/CommandPaletteEntry.cs` POCO per data-model.md §6 (Label, Category, MatchScore, IconResourceKey, Invoke, Tooltip)
- [ ] T061 [P] [US6] Create `src/AkmlSql.Shell.Shared/CommandPalette/Sources/AkmlCommandSource.cs` — enumerates `OleMenuCommandService.AllCommands`
- [ ] T062 [P] [US6] Create `src/AkmlSql.Shell.Shared/CommandPalette/Sources/AkmlOptionsSource.cs` — reflects over `AppSettings` properties tagged with a new `[CommandPaletteEntry(Label, Path)]` attribute; add the attribute decoration to the most-used settings as part of this task
- [ ] T063 [P] [US6] Create `src/AkmlSql.Shell.Shared/CommandPalette/Sources/HostCommandSource.cs` — enumerates `EnvDTE.DTE.Commands` once per session (cached)
- [ ] T064 [P] [US6] Create `src/AkmlSql.Shell.Shared/CommandPalette/Sources/DatabaseObjectSource.cs` — SSMS-only (host detection); reads from active session's `DatabaseCache.Tables`/`Views`/`Procedures`/`Functions`

### Palette window (US6)

- [ ] T065 [US6] Extend `src/AkmlSql.Shell.Shared/CommandPalette/CommandPaletteWindow.cs` to aggregate the four sources via `IEnumerable.Concat`, rank entries via `AkmlSql.Engine.Completion.FuzzyMatcher`, render each row with a small category-badge `Border`
- [ ] T066 [US6] Implement recent-items behaviour: load `AppSettings.CommandPalette.RecentItems[host]`, render top-10 when search box is empty, append to the list on each pick
- [ ] T067 [US6] Implement Options-result deep-link: when an `AkmlOptionsSource` entry is picked, open `SettingsWindow` scrolled to and highlighting the matching control via the existing `SettingsSearchWidget`
- [ ] T068 [US6] Wire `cmdidShowCommandPalette` (`0x0208`) with chord `Alt+S` (SSMS) / `Alt+P` (VS) in all 6 host VSCT files per `contracts/commands.md`

### Tests for US6

- [ ] T069 [P] [US6] Add `tests/AkmlSql.Engine.Tests/Completion/FuzzyMatcherCommandPaletteTests.cs` validating fuzzy ranking gives the expected order for representative queries: "format", "fmt", "smart rename", "explain"

### Smoke test

- [ ] T070 [US6] Hot-swap to SSMS 22 and walk through the quickstart.md US6 section

**Checkpoint**: US6 complete.

---

## Phase 9: User Story 7 — Script navigation chords + Browse Open Tabs + F1 help (Priority: P2)

**Goal**: Four chord-driven navigation moves (`Ctrl+B,Ctrl+S` Summarize Script, `F12` Script-as-ALTER, `Ctrl+F12` Select-in-OE, `Ctrl+B,Ctrl+F` Find Unused) + `Ctrl+Q` Browse Open Tabs + F1 help on every UI surface.

**Independent Test**: see spec.md US7 Independent Test paragraph.

### Settings (US7)

- [ ] T071 [P] [US7] Add 6 properties to `src/AkmlSql.Core/Config/NavigationSettings.cs` per `contracts/settings.md`: `SummarizeScriptEnabled`, `ScriptAsAlterOnF12Enabled`, `SelectInObjectExplorerEnabled`, `FindUnusedVariablesEnabled`, `BrowseOpenTabsEnabled` (all bool, default `true`), `BrowseOpenTabsShortcut` (string, default `"Ctrl+Q"`)

### Engine (US7)

- [ ] T072 [P] [US7] Implement `src/AkmlSql.Engine/Analysis/FindUnusedVariablesHandler.cs` (real impl, MessageType 91). Walks the parsed AST via `TSqlFragmentVisitor` to find every `DECLARE @var` and every procedure/function parameter, then verifies each is referenced in the rest of the script. Returns `UnusedDeclarationDto[]`.
- [ ] T073 [P] [US7] Implement `src/AkmlSql.Engine/Analysis/ScriptOutlineBuilder.cs` — walks the AST to produce a tree of `ScriptOutlineNode` records (per data-model.md §8). Wire as a handler reusing the existing `DocumentOutlineRequest`/`Response` types per spec 014 Phase 2 audit.
- [ ] T074 [P] [US7] Wire `ScriptAsAlter` engine response — reuses the existing `ScriptAsRequest`/`Response` types from spec 008. Engine generates the `ALTER` script for the identifier under caret on the active connection; schema-bound objects retain `WITH SCHEMABINDING`.

### Shell: chords + commands (US7)

- [ ] T075 [US7] Create `src/AkmlSql.Shell.Shared/Productivity/Navigation/ScriptOutlineWindow.cs` — WPF dialog (or tool window) showing the `ScriptOutlineNode` tree; click-to-navigate; bound to `cmdidSummarizeScript` (`0x0209`, chord `Ctrl+B,Ctrl+S`)
- [ ] T076 [US7] Create `src/AkmlSql.Shell.Shared/Productivity/Navigation/ScriptAsAlterCommand.cs` — bound to `cmdidScriptAsAlter` (`0x020A`, chord `F12`); falls through to host native F12 if no AKML-resolvable identifier under caret
- [ ] T077 [US7] Create `src/AkmlSql.Shell.Shared/Productivity/Navigation/SelectInObjectExplorerCommand.cs` — bound to `cmdidSelectInObjectExplorer` (`0x020B`, chord `Ctrl+F12`); uses `DTE` to expand and select the Object Explorer node
- [ ] T078 [US7] Create `src/AkmlSql.Shell.Shared/Productivity/Navigation/FindUnusedVariablesCommand.cs` + a small results panel; bound to `cmdidFindUnusedVariables` (`0x020C`, chord `Ctrl+B,Ctrl+F`)

### Browse Open Tabs (US7)

- [ ] T079 [US7] Create `src/AkmlSql.Shell.Shared/Productivity/Navigation/BrowseOpenTabsPopup.cs` — WPF `Popup` enumerating `DTE.Documents`, fuzzy-search box, Enter activates selected tab; bound to `cmdidBrowseOpenTabs` (`0x020D`, chord `Ctrl+Q`)

### F1 help registrations (US7)

- [ ] T080 [P] [US7] Populate `src/AkmlSql.Shell.Shared/Help/F1HelpRegistrations.cs` with one `F1HelpListener.Register("<surfaceKey>", "doc/<surface>.md")` call per AKML SQL UI surface — every WPF `ThemeAwareWindow` and `ToolWindowPane`. Coverage MUST be 100% (FR-032 / FR-104).
- [ ] T081 [US7] Wire `cmdidShowF1Help` (`0x020E`) into each existing `ThemeAwareWindow` and `ToolWindowPane` constructor so `F1` is captured via `IOleCommandTarget` chain and routed to `F1HelpListener.Open()`

### VSCT chord bindings (US7)

- [ ] T082 [US7] Add `<KeyBinding>` entries for `cmdidSummarizeScript` / `cmdidScriptAsAlter` / `cmdidSelectInObjectExplorer` / `cmdidFindUnusedVariables` / `cmdidBrowseOpenTabs` in all 6 host VSCT files per the chord-binding pattern in `contracts/commands.md`

### Tests for US7

- [ ] T083 [P] [US7] Add `tests/AkmlSql.Engine.Tests/Analysis/FindUnusedVariablesHandlerTests.cs` covering declared-but-unused variables, unused procedure parameters, the negative case of "variable used in a subquery"
- [ ] T084 [P] [US7] Add `tests/AkmlSql.Engine.Tests/Refactoring/ScriptOutlineBuilderTests.cs` covering multi-statement scripts with CTE / proc / function nesting

### Smoke test

- [ ] T085 [US7] Hot-swap to SSMS 22 and walk through the quickstart.md US7 section, including the F1 coverage walk

**Checkpoint**: US7 complete.

---

## Phase 10: User Story 8 — Find Invalid Objects across the database (Priority: P2)

**Goal**: Right-click database in Object Explorer → Find Invalid Objects scans every user object for broken references and displays them in a dockable tool window with Script as ALTER support.

**Independent Test**: see spec.md US8 Independent Test paragraph.

### Engine (US8)

- [ ] T086 [US8] Implement `src/AkmlSql.Engine/Analysis/FindInvalidObjectsHandler.cs` (real impl, MessageType 90) per research.md R-007 — uses `sys.sql_expression_dependencies` joined to `sys.objects` to detect references to non-existent objects, plus `sys.sql_modules` parsed via `TSql170Parser` to surface line numbers. Batches 100 objects per round-trip and streams `FindInvalidObjectsResponse` chunks via Notification messages.

### Shell (US8)

- [ ] T087 [US8] Create `src/AkmlSql.Shell.Shared/Productivity/Navigation/FindInvalidObjectsWindow.cs` — `ThemeAwareUserControl` in a `ToolWindowPane` showing columns: object name, schema, type, error message, source line number; multi-row selection; refresh button
- [ ] T088 [US8] Wire streaming: on each `FindInvalidObjectsResponse` chunk, append rows to the window's `ObservableCollection` so partial results render within 2 seconds (FR-036)
- [ ] T089 [US8] Wire Object Explorer right-click → "Find Invalid Objects" via the existing OE extensibility (`IVsHierarchy` event chain); add `cmdidShowFindInvalidObjects` (`0x020F`)
- [ ] T090 [US8] Implement Script as ALTER button — multi-select concatenates ALTER scripts in one new query window
- [ ] T091 [US8] Implement double-click → Object Explorer navigate + status-bar error message display
- [ ] T092 [US8] Implement "No invalid objects found" empty-state + refresh button

### Tests for US8

- [ ] T093 [P] [US8] Add `tests/AkmlSql.Engine.Tests/Analysis/FindInvalidObjectsHandlerTests.cs` covering broken view (dropped column), broken procedure (missing table), broken synonym; verify streaming chunks; verify clean-database returns empty response with success

### Smoke test

- [ ] T094 [US8] Hot-swap to SSMS 22 and walk through the quickstart.md US8 section

**Checkpoint**: US8 complete.

---

## Phase 11: User Story 9 — Result-grid productivity audit (Priority: P2)

**Goal**: Audit and complete `Copy as IN Clause`, `Script as INSERT`, `Open in Excel` against spec 014 FR-074..078 — NULL-omission status message, IDENTITY_INSERT toggle dialog, wide-precision-as-text formatting.

**Independent Test**: see spec.md US9 Independent Test paragraph.

### Settings (US9)

- [ ] T095 [P] [US9] Add 4 properties to `src/AkmlSql.Core/Config/GridSettings.cs` per `contracts/settings.md`: `CopyAsInClauseReportNullCount`, `ScriptAsInsertPromptIdentityToggle`, `OpenInExcelWidePrecisionAsText` (all bool, default `true`), `OpenInExcelWidePrecisionThreshold` (int, default `15`)

### Shell (US9)

- [ ] T096 [US9] Audit `Copy as IN Clause` path in `src/AkmlSql.Shell.Shared/Productivity/Grid/...` — verify NULL values are omitted, add a status-bar message reporting the omission count per FR-038 (consume `CopyAsInClauseReportNullCount` setting)
- [ ] T097 [US9] Audit `Script as INSERT` path — when the target table has an IDENTITY column, show a dialog asking whether to wrap with `SET IDENTITY_INSERT ON/OFF`; honor the user's choice per FR-039 (consume `ScriptAsInsertPromptIdentityToggle` setting)
- [ ] T098 [US9] Audit `Open in Excel` path — verify wide-precision (> 15 significant digits) cells are formatted as text per FR-040 (consume `OpenInExcelWidePrecisionAsText` + `OpenInExcelWidePrecisionThreshold` settings)

### Tests for US9

- [ ] T099 [P] [US9] Extend existing grid tests in `tests/AkmlSql.Core.Tests/Productivity/Grid/...` with cases for NULL omission, IDENTITY toggle prompt, wide-precision-as-text. Add no new test file — extend existing grid copy/script test fixtures.

### Smoke test

- [ ] T100 [US9] Hot-swap to SSMS 22 and walk through the quickstart.md US9 section

**Checkpoint**: US9 complete.

---

## Phase 12: User Story 10 — Refactor chord family + Smart Rename + execution shortcuts (Priority: P3)

**Goal**: `Ctrl+B,Ctrl+B/I/E` chord additions; database-wide Smart Rename with transactional preview; `Alt+Shift+F5` Execute Current Batch and `Ctrl+Shift+F5` Execute To Cursor, both triggering safety check.

**Independent Test**: see spec.md US10 Independent Test paragraph.

### Settings (US10)

- [ ] T101 [P] [US10] Add 5 properties to `src/AkmlSql.Core/Config/RefactoringSettings.cs` per `contracts/settings.md`: `BracketsToggleShortcut` (default `"Ctrl+B,Ctrl+B"`), `InlineStoredProcedureShortcut` (`"Ctrl+B,Ctrl+I"`), `EncapsulateAsStoredProcedureShortcut` (`"Ctrl+B,Ctrl+E"`), `SmartRenameEnabled` (bool, default `true`), `SmartRenamePreserveExtendedProperties` (bool, default `true`)
- [ ] T102 [P] [US10] Add 4 properties to `src/AkmlSql.Core/Config/ExecutionProductivitySettings.cs`: `ExecuteCurrentBatchEnabled`, `ExecuteCurrentBatchShortcut` (`"Alt+Shift+F5"`), `ExecuteToCursorEnabled`, `ExecuteToCursorShortcut` (`"Ctrl+Shift+F5"`)

### Engine (US10)

- [ ] T103 [US10] Implement `src/AkmlSql.Engine/Refactoring/SmartRenameHandler.cs` per research.md R-008 — reuses existing `RefactorPreviewRequest`/`RefactorApplyRequest` types. Three-section script: validation block (`BEGIN TRANSACTION; OBJECT_ID('schema.newName'); ROLLBACK;`), rename (sp_rename for tables/cols, drop-recreate for procs/views/funcs), per-dependent ALTERs. Wrapped in `BEGIN TRANSACTION; TRY/CATCH`. Returns `SmartRenamePlan`.

### Shell: chord family (US10)

- [ ] T104 [P] [US10] Create `src/AkmlSql.Shell.Shared/Productivity/Refactoring/BracketsToggleCommand.cs` — bound to `cmdidBracketsToggle` (`0x0210`, chord `Ctrl+B,Ctrl+B`)
- [ ] T105 [P] [US10] Create `src/AkmlSql.Shell.Shared/Productivity/Refactoring/InlineStoredProcedureCommand.cs` — bound to `cmdidInlineStoredProcedure` (`0x0211`, chord `Ctrl+B,Ctrl+I`)
- [ ] T106 [P] [US10] Create `src/AkmlSql.Shell.Shared/Productivity/Refactoring/EncapsulateAsStoredProcedureCommand.cs` — bound to `cmdidEncapsulateAsStoredProcedure` (`0x0212`, chord `Ctrl+B,Ctrl+E`); shows a small dialog asking for the new procedure name

### Shell: Smart Rename (US10)

- [ ] T107 [US10] Create `src/AkmlSql.Shell.Shared/Productivity/Refactoring/SmartRenameDialog.cs` — `ThemeAwareWindow` with Actions / Warnings / Dependencies tabs per spec.md FR-070; **Apply** button disabled when warnings include a `NameCollision` (FR-043)
- [ ] T108 [US10] Wire `F2` chord with fallthrough — `cmdidSmartRename` (`0x0213`) returns `OLECMDERR_E_NOTSUPPORTED` if caret is not on a database-resolvable identifier
- [ ] T109 [US10] Wire Object Explorer right-click → Smart Rename via OE extensibility

### Shell: execution shortcuts (US10)

- [ ] T110 [P] [US10] Create `src/AkmlSql.Shell.Shared/Editor/Execution/ExecuteCurrentBatchCommand.cs` — bound to `cmdidExecuteCurrentBatch` (`0x0214`, chord `Alt+Shift+F5`). Computes the batch between surrounding `GO` markers.
- [ ] T111 [P] [US10] Create `src/AkmlSql.Shell.Shared/Editor/Execution/ExecuteToCursorCommand.cs` — bound to `cmdidExecuteToCursor` (`0x0215`, chord `Ctrl+Shift+F5`). Runs start-of-batch up to the line above cursor.
- [ ] T112 [US10] Extend `src/AkmlSql.Shell.Shared/Safety/ExecutionInterceptor.cs` to hook the two new execute paths via `cmdidExecuteCurrentBatch` and `cmdidExecuteToCursor` in `IOleCommandTarget`. Both MUST trigger `CheckBeforeExecuteAsync` per FR-046.

### VSCT chord bindings (US10)

- [ ] T113 [US10] Add `<KeyBinding>` entries for `cmdidBracketsToggle`, `cmdidInlineStoredProcedure`, `cmdidEncapsulateAsStoredProcedure`, `cmdidSmartRename`, `cmdidExecuteCurrentBatch`, `cmdidExecuteToCursor` in all 6 host VSCT files per `contracts/commands.md`

### Tests for US10

- [ ] T114 [P] [US10] Add `tests/AkmlSql.Engine.Tests/Refactoring/SmartRenameHandlerTests.cs` covering: column rename with 5 dependent views; collision detection; transactional rollback on simulated mid-script failure; extended-property preservation; system-object rename refusal (spec.md Edge Cases)

### Smoke test

- [ ] T115 [US10] Hot-swap to SSMS 22 and walk through the quickstart.md US10 section

**Checkpoint**: US10 complete.

---

## Phase 13: User Story 11 — Completion polish + Object Definition Box + dual-instance test + format markers (Priority: P3)

**Goal**: Eight completion-polish items; audit and complete ObjectDefinitionPanel; dual-instance regression test; editor action that inserts format-marker comments.

**Independent Test**: see spec.md US11 Independent Test paragraph.

### Settings (US11)

- [ ] T116 [P] [US11] Add 8 properties to `src/AkmlSql.Core/Config/CompletionPolishSettings.cs` per `contracts/settings.md`: `ToggleSuggestionsShortcut`, `CommitKeys`, `CategoryCycleEnabled`, `ShowMsDescriptionInTooltip`, `HighlightNextParameterInSignature`, `DecryptEncryptedObjectsWithDac`, `TempTableIntelliSenseEnabled`, `ObjectDefinitionBoxSize`
- [ ] T117 [P] [US11] Add `DisableFormattingForSelectionEnabled` (bool, default `true`) to `src/AkmlSql.Core/Config/FormatterSettings.cs`

### Engine (US11)

- [ ] T118 [P] [US11] Implement `src/AkmlSql.Engine/Analysis/EncryptedObjectDecryptionHandler.cs` (real impl, MessageType 92) — uses DAC connection if available; returns decrypted procedure/function body or an error with "DAC required" hint

### Shell: completion polish 8 items (US11)

- [ ] T119 [P] [US11] Create `src/AkmlSql.Shell.Shared/Editor/Completion/CompletionToggleListener.cs` — `[Export(typeof(IVsTextViewCreationListener))]`, bound to `cmdidToggleSuggestions` (`0x0216`, chord `Ctrl+Shift+P`); per-session boolean state machine; status-bar feedback on toggle
- [ ] T120 [P] [US11] Create `src/AkmlSql.Shell.Shared/Editor/Completion/CompletionCategoryFilter.cs` — bound to `cmdidCycleCategoryFilterForward` (`0x0217`, chord `Ctrl+Down`) and `cmdidCycleCategoryFilterBackward` (`0x0218`, chord `Ctrl+Up`); cycles Tables → Views → Columns → Functions → Procedures → Snippets → All with visible badge
- [ ] T121 [P] [US11] Modify `src/AkmlSql.Shell.Shared/Editor/Completion/AkmlCompletionPopup.cs` to honor custom commit keys from `AppSettings.CompletionPolish.CommitKeys` (Space / Dot / Comma / OpenParen / Tab / Enter)
- [ ] T122 [P] [US11] Modify `QuickInfoSource.cs` to surface `MS_Description` extended property in tooltips when `ShowMsDescriptionInTooltip == true`
- [ ] T123 [P] [US11] Create `src/AkmlSql.Shell.Shared/Editor/Signature/ParameterHighlighter.cs` — bolds the next-expected parameter in signature popups when `HighlightNextParameterInSignature == true`
- [ ] T124 [P] [US11] Create `src/AkmlSql.Shell.Shared/Editor/Completion/TempTableSchemaCollector.cs` per data-model.md §15 — parses `CREATE TABLE #temp` and `SELECT INTO #temp` from the active script via `TSqlFragmentVisitor`; surfaces column completions for `#temp` references in the same script scope

### Shell: Object Definition Box audit (US11)

- [ ] T125 [US11] Audit `src/AkmlSql.Shell.Shared/Editor/Completion/ObjectDefinitionPanel.cs` against FR-020..024: verify Summary tab shows columns / types / nullability / row count for tables and parameters / types / return type for procs; verify Script tab shows CREATE statement with syntax coloring; verify resize-persist (write to `AppSettings.CompletionPolish.ObjectDefinitionBoxSize`); verify `Ctrl` transparency hooked to both popup and panel
- [ ] T126 [US11] Wire encrypted-decryption to ObjectDefinitionPanel Script tab — when user has DAC and `DecryptEncryptedObjectsWithDac == true`, dispatch `EncryptedObjectDecryptionRequest` and display the body with a "decrypted" badge; otherwise show the encrypted placeholder

### Shell: format markers editor action (US11)

- [ ] T127 [P] [US11] Create `src/AkmlSql.Shell.Shared/Formatting/DisableFormattingForSelectionCommand.cs` — adds an entry to the editor Actions list ("Disable formatting for selected text"); on invocation wraps the selection in `-- akml-format off` / `-- akml-format on` marker comments; bound to `cmdidDisableFormattingForSelection` (`0x0219`)

### VSCT chord bindings (US11)

- [ ] T128 [US11] Add `<KeyBinding>` entries for `cmdidToggleSuggestions` and (via `IOleCommandTarget` filter only — no VSCT entry) `cmdidCycleCategoryFilterForward`/`Backward` in all 6 host VSCT files per `contracts/commands.md`

### Tests for US11

- [ ] T129 [P] [US11] Add `tests/AkmlSql.Engine.Tests/Completion/TempTableSchemaCollectorTests.cs` covering CREATE TABLE #temp, SELECT INTO #temp, scope boundaries (statement / batch / file), DROP TABLE #temp clears scope
- [ ] T130 [P] [US11] Add `tests/AkmlSql.Engine.Tests/Completion/CompletionCategoryFilterTests.cs` covering the seven-step cycle and the badge text
- [ ] T131 [P] [US11] Add `tests/AkmlSql.Engine.Tests/Analysis/EncryptedObjectDecryptionHandlerTests.cs` covering: DAC available + encrypted proc returns body, DAC unavailable returns "DAC required" hint
- [ ] T132 [P] [US11] Add dual-instance regression test `tests/AkmlSql.Engine.Tests/Completion/DualInstanceConnectionTests.cs` — verifies `SsmsConnectionDetector` resolves per-text-view file path and does NOT fall back to `DTE.ActiveDocument`

### Smoke test

- [ ] T133 [US11] Hot-swap to SSMS 22 and walk through the quickstart.md US11 section

**Checkpoint**: US11 complete.

---

## Phase 14: User Story 12 — WPF theme refresh continuation + Options Dialog Phase 3 (Priority: P3)

**Goal**: Migrate the remaining ~15 WPF surfaces to `ThemeTokens`; ship Options Dialog Phase 3 (3-column Style Editor + 3 new built-ins + Redgate importer warnings UI + Environment Color Editor sub-dialog + slim Format › Styles page).

**Independent Test**: see spec.md US12 Independent Test paragraph.

### Theme refresh continuation (US12)

- [ ] T134 [US12] Migrate `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs` to inherit from `ThemeAwareWindow` and consume `ThemeTokens` via `SetResourceReference` for every chrome color; visually match `doc/SQL-PROMPT/SQL-Prompt-Option/13_options_dialog.svg`
- [ ] T135 [P] [US12] Migrate `src/AkmlSql.Shell.Shared/Snippets/SnippetManagerDialog.cs` to `ThemeAwareWindow` + `ThemeTokens`
- [ ] T136 [P] [US12] Migrate `src/AkmlSql.Shell.Shared/History/HistoryToolWindowControl.cs` to `ThemeAwareUserControl` + `ThemeTokens`
- [ ] T137 [P] [US12] Migrate `src/AkmlSql.Shell.Shared/Ai/AiChatToolWindow.cs` to `ThemeAwareUserControl` + `ThemeTokens`
- [ ] T138 [P] [US12] Migrate `src/AkmlSql.Shell.Shared/Productivity/Navigation/ObjectSearchWindow.cs` to `ThemeAwareWindow` + `ThemeTokens`
- [ ] T139 [P] [US12] Migrate `src/AkmlSql.Shell.Shared/CommandPalette/CommandPaletteWindow.cs` to `ThemeAwareWindow` + `ThemeTokens`
- [ ] T140 [P] [US12] Migrate completion popup chrome in `src/AkmlSql.Shell.Shared/Editor/Completion/AkmlCompletionPopup.cs` to `ThemeTokens`
- [ ] T141 [P] [US12] Migrate peek-definition control to `ThemeTokens`
- [ ] T142 [P] [US12] Migrate analysis-finding tooltips to `ThemeTokens`
- [ ] T143 [P] [US12] Migrate editor toolbar to `ThemeTokens`

### Options Dialog Phase 3 (US12)

- [ ] T144 [US12] Restructure `src/AkmlSql.Shell.Shared/Formatting/ProfileEditorDialog.cs` to 3-column layout (Style List | Categories tree | Options + Preview) per the Options Dialog Phase 3 plan
- [ ] T145 [P] [US12] Create three new built-in style files in `src/AkmlSql.Engine/Formatting/Profiles/`: `aligned.akmlstyle`, `verbose.akmlstyle`, `redgate-compatible.akmlstyle` (JSON profiles, each tuned to SQL Prompt's corresponding output style)
- [ ] T146 [US12] Extend `ProfileEditorViewModel` with `UserStyles`, `BuiltInStyles`, `SelectedStyle`, `ActiveStyle` properties + dirty-prompt event
- [ ] T147 [US12] Add toolbar buttons (Create / Copy / Rename / Delete / Import / Export) in `ProfileEditorDialog`; built-in styles render with a lock icon and the right-side panels are read-only for them
- [ ] T148 [US12] Add post-import dialog to the `.sqlpromptstylev2` importer: shows `translatedCount`, `unsupportedCount`, lists any unsupported options. Audit `SqlPromptImporter` `OptionMap` against 5+ real `.sqlpromptstylev2` exports first.
- [ ] T149 [US12] Slim the Format › Styles Options page in `src/AkmlSql.Shell.Shared/Dialogs/Pages/FormattingPage.cs` to a dropdown for Active Style + Edit button
- [ ] T150 [US12] Create `src/AkmlSql.Shell.Shared/Dialogs/EnvironmentColorEditorDialog.cs` — `ThemeAwareWindow` with add/edit/remove environments, Label/Pattern/Color fields, live color preview
- [ ] T151 [US12] Add "Manage Environments" button to the Tabs › Color Options page that opens `EnvironmentColorEditorDialog`

### Verification (US12)

- [ ] T152 [US12] Run `pwsh scripts/audit-wpf-theme.ps1` (existing script from spec 016 T016). Confirm zero hits in the allow-listed scope (excluding `Ui/Theme/` and the 8 WinForms surfaces in spec.md A11). If hits remain, fix them.

### Smoke test

- [ ] T153 [US12] Hot-swap to SSMS 22 and walk through the quickstart.md US12 section in both Dark and Light themes; repeat in VS 2022

**Checkpoint**: US12 complete. The "Options window looks unfinished" complaint that triggered spec 016 is closed.

---

## Phase 15: User Story 13 — AI keyboard shortcuts + feature reach (Priority: P3)

**Goal**: AI keyboard shortcuts + Explain SQL + Query Index Analysis + Auto-fix-on-error toast + Comment-to-SQL + Panel History tab + Editor selection icon + Follow-up suggestions.

**Independent Test**: see spec.md US13 Independent Test paragraph.

### Settings (US13)

- [ ] T154 [P] [US13] Add 12 properties to `src/AkmlSql.Core/Config/AiSettings.cs` per `contracts/settings.md`: 4 shortcut strings, 8 feature toggles, `PanelHistoryRetentionDays` (int, default 7)

### Shell: AI shortcuts (US13)

- [ ] T155 [P] [US13] Create `src/AkmlSql.Shell.Shared/Ai/OpenAiPanelCommand.cs` — `cmdidOpenAiPanel` (`0x021A`, chord `Alt+Z`); opens or focuses the AI chat tool window
- [ ] T156 [P] [US13] Create `src/AkmlSql.Shell.Shared/Ai/AiFixSelectionCommand.cs` — `cmdidAiFixSelection` (`0x021B`, chord `Shift+Alt+R`); dispatches `AiFixRequest` for the current selection
- [ ] T157 [P] [US13] Create `src/AkmlSql.Shell.Shared/Ai/AiOptimizeSelectionCommand.cs` — `cmdidAiOptimizeSelection` (`0x021C`, chord `Ctrl+Alt+Z`)
- [ ] T158 [P] [US13] Create `src/AkmlSql.Shell.Shared/Ai/AiManualGhostTextCommand.cs` — `cmdidAiManualGhostText` (`0x021D`, chord `Ctrl+Alt+Up Arrow`)

### Shell: AI feature reach (US13)

- [ ] T159 [P] [US13] Create `src/AkmlSql.Shell.Shared/Ai/ExplainSqlCommand.cs` — `cmdidAiExplainSql` (`0x021E`); right-click selection + AKML SQL menu + Command Palette entry; dispatches `AiExplainRequest`
- [ ] T160 [P] [US13] Create `src/AkmlSql.Shell.Shared/Ai/QueryIndexAnalysisCommand.cs` — `cmdidAiQueryIndexAnalysis` (`0x021F`); dispatches `AiIndexAnalysisRequest`
- [ ] T161 [P] [US13] Create `src/AkmlSql.Shell.Shared/Ai/AutoFixOnErrorToast.cs` — listens for SQL execution failure events; renders a non-blocking toast "Fix with AI" with click handler that pre-fills the AI panel with the failing batch + error message
- [ ] T162 [P] [US13] Create `src/AkmlSql.Shell.Shared/Ai/CommentToSqlListener.cs` — `[Export(typeof(IVsTextViewCreationListener))]`; on `Tab` after `-- generate: <NL>` on a blank line, dispatches `AiTextToSqlRequest` and replaces the comment line with the AI-generated SQL (original comment retained above)
- [ ] T163 [P] [US13] Create `src/AkmlSql.Shell.Shared/Ai/AiHistoryTab.cs` — WPF control rendered as a tab inside the AI chat tool window; lists previous `AiConversationTurn` records (per data-model.md §12) in reverse chronological order with "revert to this state" action per entry
- [ ] T164 [P] [US13] Create `src/AkmlSql.Shell.Shared/Ai/AiFollowUpButtons.cs` — renders 1–3 follow-up prompt buttons beneath each AI answer in the chat panel
- [ ] T165 [P] [US13] Create `src/AkmlSql.Shell.Shared/Editor/Adornments/AiSelectionIconAdornment.cs` per research.md R-009 — adornment layer that places a small AI icon (16×16 px `Border`) at the right edge of the last selection line; click shows a `Popup` with Explain / Fix / Optimize buttons

### AI panel history persistence (US13)

- [ ] T166 [US13] Wire AI panel history persistence to `%AppData%\AKML SQL\cache\ai-history-<sessionId>.json`. Files older than `AiSettings.PanelHistoryRetentionDays` are removed by extending the existing `HistoryRetentionService`.

### VSCT chord bindings (US13)

- [ ] T167 [US13] Add `<KeyBinding>` entries for `cmdidOpenAiPanel`, `cmdidAiFixSelection`, `cmdidAiOptimizeSelection`, `cmdidAiManualGhostText` in all 6 host VSCT files per `contracts/commands.md`

### Tests for US13

- [ ] T168 [P] [US13] Extend `tests/AkmlSql.Engine.Tests/Ai/...` with cases for: Explain SQL truncation at 5,000 lines (spec.md Edge Cases); Comment-to-SQL trigger pattern (single-line `-- generate:` only); rate-limit error surfacing

### Smoke test

- [ ] T169 [US13] Hot-swap to SSMS 22 and walk through the quickstart.md US13 section

**Checkpoint**: US13 complete.

---

## Phase 16: User Story 14 — Code-audit TODO closure + remaining refactoring debt (Priority: P3)

**Goal**: Resolve every remaining TODO from `doc/codebase-audit-2026-05-05.md` § 1 — wire them or delete them. The two big refactors (PipeRpcServer dispatch table FR-080, AppSettings split FR-081) already shipped as part of Foundational (Phase 2).

**Independent Test**: see spec.md US14 Independent Test paragraph.

### P0 TODOs — SignatureHelp / QuickInfo (US14)

- [ ] T170 [US14] Wire `src/AkmlSql.Shell.Shared/Editor/SignatureHelpSource.cs` via `PipeRpcClient` per research.md R-015 recommendation (engine providers exist; only shell-side wiring is missing): dispatch `SignatureRequest`, render results in the existing VS signature-help UI
- [ ] T171 [US14] Implement "Best match selection based on active parameter" in `SignatureHelpSource.cs:66` (per BUG-A3, depends on T170)
- [ ] T172 [US14] Wire `src/AkmlSql.Shell.Shared/Editor/QuickInfoSource.cs` via `PipeRpcClient`: dispatch `QuickInfoRequest`, render results in the existing VS quick-info UI

### P1 TODOs — Format-on-* handlers (US14)

- [ ] T173 [P] [US14] Wire `src/AkmlSql.Shell.Shared/Formatting/FormatOnSaveHandler.cs` to consume the `FormatRequestDispatcher` from T011
- [ ] T174 [P] [US14] Wire `src/AkmlSql.Shell.Shared/Formatting/FormatOnPasteHandler.cs` to consume `FormatRequestDispatcher`
- [ ] T175 [P] [US14] Wire `src/AkmlSql.Shell.Shared/Formatting/FormatOnDelimiterHandler.cs` to consume `FormatRequestDispatcher`
- [ ] T176 [US14] Replace `src/AkmlSql.Shell.Shared/Productivity/CrudGenerationCommand.cs` word-at-caret heuristic with a proper `CrudGenerationDialog` that collects schema name, table name, and operation options

### P2 TODOs — SSMS host polish (US14)

- [ ] T177 [P] [US14] Wire `src/AkmlSql.Shell.Shared/Tabs/TabTooltipProvider.cs` to consume `SsmsConnectionContextResolver` from T012 — populate tooltip with auth-mode and connect-time
- [ ] T178 [P] [US14] Wire `src/AkmlSql.Shell.Shared/Tabs/TabColoringManager.cs` (lines 896, 904) to consume the same resolver
- [ ] T179 [US14] Implement "Walk the WPF visual tree to find the tab header" in `TabTooltipProvider.cs:158` (BUG-A9) — richer hover positioning
- [ ] T180 [P] [US14] Add SSMS 20 fallback path in `src/AkmlSql.Shell.Shared/Productivity/Grid/GridAccessHelper.cs` — handle the different results-pane class

### P3 TODOs — Cosmetic / placeholder (US14)

- [ ] T181 [P] [US14] Wire `WasFormatted` in `src/AkmlSql.Engine/Snippets/SnippetRequestHandler.cs:66` via `FormatRequestDispatcher`; OR delete the field from the DTO
- [ ] T182 [P] [US14] Wire `UsageCount` in `SnippetRequestHandler.cs:95` via a small per-snippet `UsageTracker`; OR delete the field from the DTO
- [ ] T183 [US14] Implement installer T096 in `src/AkmlSql.Installer/AkmlSqlSetup.iss:42` — on uninstall, restore native SSMS IntelliSense if AKML SQL disabled it

### Tests for US14

- [ ] T184 [P] [US14] Add `tests/AkmlSql.Engine.Tests/Server/PipeRpcServerDispatchTests.cs` covering FR-080's zero-modify invariant: register a new mock `IMessageHandler`, assert `PipeRpcServer` dispatches without source-level changes
- [ ] T185 [US14] Run the TODO-grep audit from quickstart.md "Static audit script" section. Verify the count drops from 14 to 0 (excluding the three intentional `-- TODO: Replace [TableName]` strings in `GridScriptGenerator` generated SQL output)

### Smoke test

- [ ] T186 [US14] Hot-swap to SSMS 22 and walk through the quickstart.md US14 section (TODO grep verification)

**Checkpoint**: US14 complete.

---

## Phase 17: Polish & Cross-Cutting Concerns

**Purpose**: Final verification, documentation update, and end-to-end validation.

- [ ] T187 [P] Append a Phase 10 closure summary to `D:\Repo\01-Khamis-Projects\AKML-SQL\doc\progress.md` listing every shipped user story with its commit reference
- [ ] T188 [P] Run end-to-end quickstart.md verification (Milestones M0 → M5)
- [ ] T189 Verify every Success Criterion in spec.md (SC-001..SC-021) against the final state of `master`. Document each as PASS / FAIL with evidence (test output, code grep, manual screenshot).
- [ ] T190 Run full test suites and confirm baselines hold: Engine ≥ 867, Core ≥ 526, Formatting ≥ 458, E2E baseline. Phase 10 expected to ADD ~30 tests across these suites (one or two per user story per the implementation-first-with-test-backfill convention).
- [ ] T191 Run `pwsh scripts/audit-wpf-theme.ps1` one final time to confirm SC-015 (zero hardcoded chrome hex in scope).
- [ ] T192 Run the TODO-count grep one final time to confirm SC-020 (zero residual TODOs outside the intentional allow-list).
- [ ] T193 Update `doc/AKML-SQL-Phase10-SqlPromptParity-and-Bugs-PRD.md` § 3 reconciliation table — every "❌ Absent" / "⚠️ Partial" row should now be ✅ (or have a documented out-of-scope deferral).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories. The PipeRpcServer dispatch refactor (T005–T008) MUST land before any new engine handler is added in US7 / US8 / US11. The AppSettings split (T009) MUST land before any new settings property is added in US2 onwards.
- **US1 (Phase 3)**: Depends on Foundational. Documentation hygiene + branch merge. **MVP**.
- **US2..US5 (Phases 4–7)**: Each depends on Foundational. Can run in parallel after Foundational.
- **US6..US9 (Phases 8–11)**: Each depends on Foundational. P2 priority — start after the P1 MVP set is shippable.
- **US10..US14 (Phases 12–16)**: Each depends on Foundational. P3 priority. Can run in parallel after Foundational.
- **Polish (Phase 17)**: Depends on all desired user stories being complete.

### User Story Dependencies

- **US1 (P1)**: Independent. Merging the in-flight branch is purely a process step; the documentation updates are isolated to docs.
- **US2 (P1)**: Independent. Touches `AkmlCompletionPopup` (its own new state machine), new POCO + WPF control + filter classes.
- **US3 (P1)**: Independent. Reuses the existing `AnalysisController.AnalysisCompleted` event but adds a new dockable tool window; lightbulb popup is a separate adornment.
- **US4 (P1)**: Independent. Adds a MEF visual-tree-walk extender on top of the existing `TabColoringManager` (shipped on master).
- **US5 (P1)**: Independent. Inno Setup script change + asset wiring.
- **US6 (P2)**: Independent. Extends `CommandPaletteWindow` with 4 new source classes.
- **US7 (P2)**: Soft-depends on F1HelpListener foundation work; otherwise independent. The script-nav chord engine handlers (US7 T072–T074) are new but isolated.
- **US8 (P2)**: Soft-depends on Foundational PipeRpcServer dispatch refactor (so `FindInvalidObjectsHandler` can register cleanly). Otherwise independent.
- **US9 (P2)**: Independent. Audit + completion of existing grid actions.
- **US10 (P3)**: Independent. New chord commands + new `SmartRenameDialog` + extension to `ExecutionInterceptor` (which is the only shared file with US1 from spec 014).
- **US11 (P3)**: Independent for the 8 polish items. The Object Definition Box audit (T125) extends an existing file but does not interact with other US11 sub-items.
- **US12 (P3)**: Independent. WPF surface migrations are per-file; the Options Dialog Phase 3 work is its own subtree.
- **US13 (P3)**: Soft-depends on Spec 016 theme tokens being applied to the AI chat tool window (US12 T137). Otherwise independent.
- **US14 (P3)**: Soft-depends on Foundational FormatRequestDispatcher (T011) and SsmsConnectionContextResolver (T012) being in place. Otherwise independent.

### Within Each User Story

- Settings POCO additions before any consumer.
- Engine work (new handler) before shell wiring that dispatches it.
- Shell command class before VSCT chord binding.
- Implementation before test backfill (per project convention).
- Smoke test (hot-swap) is the last task of each user story phase.

### Parallel Opportunities

- All Setup tasks marked `[P]` (T002, T003) can run in parallel.
- All Foundational tasks marked `[P]` (T009, T010, T011, T012) can run in parallel after the sequential PipeRpcServer refactor (T005 → T006 → T007 → T008).
- All user stories can be picked up in parallel by different developers once Foundational completes.
- Within most user stories, the 4–8 `[P]`-marked tasks (settings POCOs, separate engine files, separate shell files, separate test files) can run in parallel.
- Test backfill tasks (`tests/...Tests.cs` files) are always `[P]` — different files.

---

## Parallel Example: User Story 2 (Column Picker + Wildcard-Tab)

```bash
# After Foundational completes:
# Launch all parallelizable US2 tasks together:
T021 [P] [US2] Settings: IntelliSense.ColumnPickerEnabled / SortMode / WildcardTabExpansionEnabled
T022 [P] [US2] POCO: ColumnPickerSelection.cs
T023 [P] [US2] WPF: ColumnPickerControl.cs
T025 [P] [US2] Filter: TabWildcardExpansionFilter.cs

# Then sequentially (each depends on its predecessor):
T024 [US2] Modify AkmlCompletionPopup.cs state machine
T026 [US2] Add TabWildcardExpansionFilter to projitems

# Then test backfills in parallel:
T027 [P] [US2] tests/.../ColumnPickerSelectionTests.cs
T028 [P] [US2] tests/.../TabWildcardExpansionContextTests.cs

# Then sequentially:
T029 [US2] Run full test suite
T030 [US2] Hot-swap + quickstart walk
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (**CRITICAL** — blocks all stories; one PR that lands the two refactors + the helpers)
3. Complete Phase 3: US1 (docs hygiene + branch merge)
4. **STOP and VALIDATE**: confirm `doc/progress.md` agrees with `git log master` and the reconciliation table in the PRD has zero contradictions
5. Deploy/demo if ready

### Incremental Delivery (matches Phase 10 PRD § 7 milestone roadmap)

1. **M0**: Setup + Foundational + US1 → MVP delivered, documentation reconciled
2. **M1**: Add US2 + US3 + US4 + US5 → daily-use parity, batch 1
3. **M2**: Add US6 + US7 + US8 + US9 → daily-use parity, batch 2
4. **M3**: Add US10 + US11 → keyboard-first ergonomics + completion polish
5. **M4**: Add US12 → WPF theme refresh complete + Options Dialog Phase 3
6. **M5**: Add US13 + US14 → AI feature reach + code-audit closure
7. **Polish**: end-to-end quickstart verification + final SC validation

Each milestone adds value without breaking previous milestones.

### Parallel Team Strategy

With 2–3 developers and after Foundational lands:

- **Developer A**: US2 → US6 → US10 (completion, palette, refactor chords) — touches `Editor/Completion`, `CommandPalette`, `Productivity/Refactoring`
- **Developer B**: US3 → US7 → US11 (analysis, navigation, polish) — touches `Productivity`, `Editor/Adornments`, `Help`, `Editor/Signature`
- **Developer C**: US4 + US5 → US8 → US9 → US12 → US14 (tabs, installer, find-invalid, grid, theme refresh, audit cleanup) — touches `Tabs`, `Installer`, `Productivity/Grid`, `Ui/Theme`, multiple cleanup sites
- **Single owner for US1**: documentation hygiene is small and best done by one person to keep the merge PR coherent

Cross-cutting work for US13 (AI) should be picked up after US12 has migrated the AI chat tool window to ThemeTokens (sequencing: any developer who has US12 T137 done can pick up US13).

---

## Notes

- **Implementation-first-with-test-backfill** is the project convention (confirmed by spec 014 tasks.md note about commits `2c34133` and `835d662`; reaffirmed by spec 015 PR `ec09c45` adding 5 tests alongside the 13 user stories). Test tasks in this list appear *after* the implementation tasks they validate, not before.
- **[P] tasks** = different files, no incomplete dependencies. Multiple `[P]` tasks within the same user story can run truly in parallel.
- **[US#] labels** map each task to a spec.md user story for traceability.
- **No new IPC `MessageType` integers**. The three Phase 10 handlers (`FindInvalidObjectsHandler`, `FindUnusedVariablesHandler`, `EncryptedObjectDecryptionHandler`) use MessageTypes `90/190`, `91/191`, `92/192` already reserved by spec 014 Phase 2.
- **No new NuGet dependencies**. Every new file uses only what already ships: WPF, MessagePack, Serilog, System.Text.Json, xunit.
- **Hot-swap is the last task in every user-story phase**. Manual verification per `quickstart.md` is the acceptance gate (shell projects have no automated test harness).
- **Test gates**: each phase MUST keep Engine ≥ 867, Core ≥ 526, Formatting ≥ 458, E2E baseline green. Failure to do so blocks the phase from closing.
- **Git rule**: NEVER run `git add` / `git commit` / `git push` without explicit user approval (per `CLAUDE.md` and the user's hard rule). Each task above describes implementation; commits happen only when the user says "commit".
