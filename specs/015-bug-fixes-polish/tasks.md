# Tasks: Multi-Area Bug Fixes and UI Polish (015)

**Input**: Design documents from `specs/015-bug-fixes-polish/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ipc-changes.md, quickstart.md

**Organization**: Tasks are grouped by user story (P1→P13) to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no incomplete task dependencies)
- **[Story]**: User story this task belongs to (US1–US13)

---

## Phase 1: Setup

**Purpose**: Baseline verification before any changes

- [X] T001 Verify test suite passes on branch baseline — run `dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj`; all tests must pass before proceeding
- [X] T002 Smoke-build SSMS22 shell to confirm clean baseline — run `MSBuild src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj -t:Restore,Build -p:Configuration=Release -v:minimal`; must complete with zero errors

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ NOTE**: No true cross-cutting foundational prerequisites exist for this feature. All 13 user story phases are independent of each other and can begin immediately after Phase 1. Phases 3–15 may be worked in any order or in parallel (see Dependencies section).

---

## Phase 3: User Story 1 — IntelliSense for UPDATE SET and ALTER TABLE (Priority: P1) 🎯 MVP

**Goal**: Column names appear in the completion list after `UPDATE <table> SET ` and `ALTER TABLE <table> ALTER COLUMN `.

**Independent Test**: In a connected SSMS22 query window, type `UPDATE Users SET ` → completion list shows Users column names; type `ALTER TABLE Users ALTER COLUMN ` → completion list shows Users column names.

- [X] T003 [US1] Add `AlterTableColumn` variant to the `ClauseType` enum in `src/AkmlSql.Engine/Completion/CursorContextAnalyzer.cs` (after the existing `Alter` variant at ~line 21)
- [X] T004 [US1] In `CursorContextAnalyzer.DetermineClauseType()` in `src/AkmlSql.Engine/Completion/CursorContextAnalyzer.cs`: add a backward token scan case that detects the pattern `COLUMN ← ALTER ← <table_token> ← TABLE ← ALTER`; return `ClauseType.AlterTableColumn` and store the extracted `<table_token>` in `CursorContext` for provider use
- [X] T005 [US1] In `ColumnProvider.CanHandle()` in `src/AkmlSql.Engine/Completion/Providers/ColumnProvider.cs`: handle `ClauseType.AlterTableColumn` — extract the table name from context (set in T004), call `cache.FindObject(schema, tableName)`, and return `true` when the table is found in the schema cache
- [X] T006 [US1] In the token-based alias resolution fallback in `src/AkmlSql.Engine/Completion/CompletionEngine.cs` (~lines 101-109): after the alias scan completes with an empty map AND `context.ClauseType == ClauseType.UpdateSet`, scan backward for the `UPDATE <schema.table> SET` token pattern and inject `<table>` as an implicit alias entry so `ColumnProvider` can retrieve its columns
- [X] T007 [US1] Add two unit test cases to `tests/AkmlSql.Core.Tests/` (in the nearest completion test file): (1) `UPDATE Users SET ` → expect completion items of type `Column` from `Users`; (2) `ALTER TABLE Users ALTER COLUMN ` → expect completion items of type `Column` from `Users` — run tests and confirm both pass
- [ ] T008 [US1] Build engine (`dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj`) and manually verify in SSMS22: connect to a database with a `Users` table, type `UPDATE Users SET ` and `ALTER TABLE Users ALTER COLUMN ` — both must show column completions

**Checkpoint**: US1 fully functional — UPDATE SET and ALTER TABLE column completions work.

---

## Phase 4: User Story 2 — Analysis Button Produces Visible Results (Priority: P2)

**Goal**: Clicking the "Analysis" toolbar button produces findings or a "No issues found" state within 5 seconds; the log records every analysis attempt.

**Independent Test**: Open a query containing `SELECT *` → click Analysis toolbar button → Error List or results panel shows at least one finding within 5 seconds; log file contains an analysis entry.

- [X] T009 [US2] Identify the `CmdId` bound to the "Analysis" toolbar button by reading `src/AkmlSql.Shell.Shared/AkmlSql.Shell.Shared.vsct` (search for "Analysis"); trace to its command handler class in `src/AkmlSql.Shell.Shared/Commands/` — document the handler file path
- [X] T010 [US2] In the Analysis command handler identified in T009: ensure the handler (a) sends the correct IPC request (`MessageTypes.RequestAnalyze` or `MessageTypes.AiIndexAnalysis`) and (b) opens the Error List or shows a results panel after receiving the response — add a `ShowToolWindow` or `IVsOutputWindow` write call if missing
- [X] T011 [US2] Add DEBUG-level log entries to `src/AkmlSql.Shell.Shared/Editor/Analysis/AnalysisController.cs`: on trigger log `"Analysis triggered for session {sessionId}"` and on result receipt log `"Analysis complete: {count} findings in {ms}ms"`
- [ ] T012 [US2] Verify in SSMS22: open a query with `SELECT * FROM Users`, click the Analysis toolbar button → findings appear in Error List within 5 seconds; check log file (`%AppData%/AKML SQL/logs/`) → analysis entries present

**Checkpoint**: US2 fully functional — clicking Analysis produces visible output and log entries.

---

## Phase 5: User Story 3 — Search Uses Active Connection (Priority: P3)

**Goal**: Object Search returns results when a database connection is active; "No active database connection" appears only when truly disconnected.

**Independent Test**: Connect to a DB → open Object Search → type a table name → results appear (no false "no connection" error).

- [X] T013 [US3] In `src/AkmlSql.Engine/Navigation/NavigationRequestHandler.cs` at line ~166: change the guard from `if (string.IsNullOrEmpty(databaseName))` to `if (string.IsNullOrEmpty(connectionString) || string.IsNullOrEmpty(databaseName))` — one-line bug fix
- [X] T014 [US3] Immediately before the fixed guard in `src/AkmlSql.Engine/Navigation/NavigationRequestHandler.cs`: add `Log.Debug("ObjectSearch: session={id} connectionString={hasConn} database={db}", request.SessionId, !string.IsNullOrEmpty(connectionString), databaseName)` for future diagnostics
- [ ] T015 [US3] Verify in SSMS22: (1) connect to AdventureWorks → open Object Search → type "Person" → expect results; (2) disconnect → open Object Search → type anything → expect correct "No active database connection for this session" message

**Checkpoint**: US3 fully functional — Search works for connected sessions and shows correct error when disconnected.

---

## Phase 6: User Story 4 — DROP TABLE Safety Warning (Priority: P4)

**Goal**: Executing `DROP TABLE` always triggers the safety confirmation dialog by default; suppression events are logged.

**Independent Test**: Execute `DROP TABLE dbo.TestTable` with default config → SafetyWarningDialog appears; cancel → no execution occurs.

- [X] T016 [US4] In `src/AkmlSql.Core/Config/AppSettings.cs`, locate the `SafetySettings` class: confirm `DropConfirmation` property default is `true`; if it is `false` or missing, change the initializer to `public bool DropConfirmation { get; set; } = true;`
- [X] T017 [US4] In `src/AkmlSql.Shell.Shared/Safety/ExecutionInterceptor.cs` at the config-suppression path (~lines 314-317) and the environment-suppression path (~lines 200-204): add `Log.Warning("Safety check suppressed: statement={type} reason={reason}", statementType, reason)` before each bypass return — ensures suppression is always auditable in the log
- [ ] T018 [US4] Verify in SSMS22: (1) default config → execute `DROP TABLE dbo.NonExistent` → dialog appears → cancel → no execution; (2) set `dropConfirmation: false` in `config.json` → execute → log shows WARNING "Safety check suppressed", dialog does NOT appear

**Checkpoint**: US4 fully functional — DROP TABLE triggers safety dialog by default; suppression is logged.

---

## Phase 7: User Story 5 — Star Badge Count in SQL History (Priority: P5)

**Goal**: A numeric badge on the Starred filter button shows the live count of starred queries, updating immediately on star/un-star.

**Independent Test**: Star 3 queries → badge shows "3"; un-star 1 → badge shows "2"; close and reopen History panel → badge still shows "2".

- [X] T019 [US5] Add `StarredCount` computed property to `src/AkmlSql.Shell.Shared/History/HistoryViewModel.cs`: `public int StarredCount => Entries.Count(e => e.IsFavorite);` — in the `IsFavorite` toggle handler, add `OnPropertyChanged(nameof(StarredCount))` so the binding updates immediately on every star/un-star action
- [X] T020 [US5] In `src/AkmlSql.Shell.Shared/History/HistoryToolWindowControl.cs` at the Starred filter button (~line 225): add a `TextBlock` badge overlay bound to `{Binding StarredCount}` with a `Visibility` converter that hides it when `StarredCount == 0`; style with `Background = Freeze(new SolidColorBrush(ThemeManager.Instance.AccentColor))` and `Foreground = Freeze(new SolidColorBrush(ThemeManager.Instance.HighlightForeground))`, rounded corner `Border` (CornerRadius=8, Padding=2,0)
- [ ] T021 [US5] Verify in SSMS22: star three queries → badge shows "3"; un-star one → badge updates to "2" immediately; close History panel → reopen → badge shows "2" (persisted)

**Checkpoint**: US5 fully functional — star badge shows live accurate count.

---

## Phase 8: User Story 6 — Advanced Search in SQL History (Priority: P6)

**Goal**: Advanced search filters by keyword, date range, and database name return accurate matching history entries.

**Independent Test**: Execute `SELECT * FROM Users`, then search History with keyword "Users" → the query appears in results.

- [X] T022 [US6] Trace `CamelCaseTokens` from `src/AkmlSql.Shell.Shared/History/HistorySearchParser.cs` (~lines 102-138) through the engine history handler — find where `CamelCaseTokens` is (or is not) consumed in the FTS5 query builder; add `Log.Debug("AdvancedSearch: ftsQuery={q} camelTokens={n}", ftsQuery, camelCaseTokens.Count)` at the consumption point
- [X] T023 [US6] If `CamelCaseTokens` is not applied in the engine handler (identified in T022): wire it into the FTS5 post-filter — for each `CamelCaseToken`, filter results to those whose SQL text contains any token matching the camelCase pattern; update the relevant engine handler file
- [X] T024 [US6] Add one integration test in `tests/AkmlSql.Core.Tests/` for advanced search: input `"database:AdventureWorks starred:true"` through `HistorySearchParser` → verify the parser produces the correct `DatabaseFilter = "AdventureWorks"` and `FavoritesOnly = true` fields; run test and confirm it passes

**Checkpoint**: US6 fully functional — advanced search filters return accurate results.

---

## Phase 9: User Story 7 — Schema Progress Notification Box (Priority: P7)

**Goal**: Schema-loading progress shows as a bottom-right notification box, not a top-of-editor strip.

**Independent Test**: Connect to a large database → schema loading shows a ~280×56px notification box with spinner in the bottom-right corner of the editor. No strip or spinner appears at line 1. Notification fades out on load completion.

- [X] T025 [US7] In `src/AkmlSql.Shell.Shared/Editor/SchemaProgress/SchemaProgressMargin.cs`: remove the `IWpfTextViewMargin` interface declaration and the `[Export(typeof(IWpfTextViewMarginProvider))]` export attribute; preserve the spinner `Ellipse` (with `RotateTransform` + `DoubleAnimation`), `TextBlock` status, and `FadeOut()` animation method — these are reused
- [X] T026 [US7] On the same class in `src/AkmlSql.Shell.Shared/Editor/SchemaProgress/SchemaProgressMargin.cs`: add `[Export(typeof(IWpfTextViewCreationListener))]`, `[ContentType("tsql")]`, and `[TextViewRole(PredefinedTextViewRoles.Document)]` export attributes; implement `IWpfTextViewCreationListener.TextViewCreated(IWpfTextView textView)` — store `textView` reference, subscribe to `textView.ViewportWidthChanged` and `textView.ViewportHeightChanged` for repositioning
- [X] T027 [US7] In `src/AkmlSql.Shell.Shared/Editor/SchemaProgress/SchemaProgressMargin.cs`: add `[Export(typeof(AdornmentLayerDefinition))] [Name("AkmlSchemaProgress")] [Order(After = PredefinedAdornmentLayers.CurrentLineHighlighter)]` class-level field; in `TextViewCreated()`, obtain `_adornmentLayer = textView.GetAdornmentLayer("AkmlSchemaProgress")`
- [X] T028 [US7] In `src/AkmlSql.Shell.Shared/Editor/SchemaProgress/SchemaProgressMargin.cs`: build the notification `Border` (Width=280, Height=56, CornerRadius=4, Background=`Freeze(new SolidColorBrush(ThemeManager.Instance.EditorPanelBackground))`); place the existing spinner `Ellipse` and `TextBlock` inside it; add to `_adornmentLayer` canvas; set position via `Canvas.SetRight(_border, 12)` and `Canvas.SetBottom(_border, 12)`
- [X] T029 [US7] In `src/AkmlSql.Shell.Shared/Editor/SchemaProgress/SchemaProgressMargin.cs`: in the `ViewportWidthChanged` and `ViewportHeightChanged` handlers, update `Canvas.SetRight` and `Canvas.SetBottom` to reposition the notification box at the new viewport bottom-right corner (use `textView.ViewportWidth`, `textView.ViewportHeight`, and adornment canvas offsets)
- [ ] T030 [US7] Build SSMS22 shell: `MSBuild src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj -t:Restore,Build -p:Configuration=Release -v:minimal`; clear SSMS22 MEF cache at `%LocalAppData%/Microsoft/SSMS/22.0_*/ComponentModelCache/` before testing
- [ ] T031 [US7] Verify in SSMS22: connect to a database with large schema (100+ tables) → notification box appears in bottom-right corner of editor (not at top) → spinner animates → fades out on load complete; resize SSMS window → notification stays in bottom-right corner

**Checkpoint**: US7 fully functional — schema progress notification is at bottom-right and does not block editing.

---

## Phase 10: User Story 8 — Options Dark Theme Readable Text (Priority: P8)

**Goal**: All dropdown labels and button text remain fully legible in Dark theme, in all interaction states.

**Independent Test**: Open SQL Options with Dark theme active → hover OK, Cancel, Import, Export → all labels remain clearly legible; open any dropdown → all option labels are high-contrast.

- [X] T032 [US8] In `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs` in `MakeButton()` (~line 2258-2279): update `MouseEnter` handler to also set `btn.Foreground = Freeze(new SolidColorBrush(_theme.FgPrimary.Color))` (ensuring text stays readable on the lighter hover background); update `MouseLeave` to restore `btn.Foreground = Freeze(new SolidColorBrush(_theme.FgPrimary.Color))` and `btn.Background = Freeze(new SolidColorBrush(_theme.ButtonBackground.Color))`
- [X] T033 [US8] In `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs` in `ThemeComboBoxVisualTree()` (~line 2028): set `TextElement.ForegroundProperty` on the `ComboBox` wrapper element itself (in addition to `ComboBoxItem` level) to prevent VS host theme inheritance from overriding dropdown text color in dark mode
- [ ] T034 [US8] Verify in SSMS22 with Dark theme active: open SQL Options → hover OK, Cancel, Import, Export buttons — text must be fully legible (no fading); open the Theme dropdown → all option labels ("Dark", "Light", "Blue") must be high-contrast; switch to Light theme → repeat — both themes pass

**Checkpoint**: US8 fully functional — Options dialog text is legible in all themes and interaction states.

---

## Phase 11: User Story 9 — Query Rename Discoverability (Priority: P9)

**Goal**: Unnamed queries show a muted placeholder label; rename is surfaced via a clearly labelled context menu item with a descriptive tooltip.

**Independent Test**: Open History → an unnamed entry shows "(rename me)" in muted text → right-click → "Rename" with tooltip → enter name → name persists after panel close/reopen.

- [X] T035 [US9] In `src/AkmlSql.Shell.Shared/History/HistoryToolWindowControl.cs` in `QueryNameConverter` (~lines 1797-1819): when `TabTitle` is null or empty, return a `Run` (or style) with text `"(rename me)"` and foreground `Freeze(new SolidColorBrush(ThemeManager.Instance.PlaceholderText))` — visually distinguishes unnamed queries from named ones
- [X] T036 [US9] In `src/AkmlSql.Shell.Shared/History/HistoryToolWindowControl.cs` at the rename `MenuItem` creation (~line 648): add `renameItem.ToolTip = "Give this query a descriptive name to find it easily later";`
- [ ] T037 [US9] Verify in SSMS22: run a new query → history entry shows "(rename me)" in muted color; right-click → "Rename" (with tooltip) → type "My Test Query" → confirm; close History panel → reopen → entry shows "My Test Query" (no placeholder)

**Checkpoint**: US9 fully functional — query rename is discoverable and persists correctly.

---

## Phase 12: User Story 10 — Document Outline Shows SQL Structure (Priority: P10)

**Goal**: Document Outline panel displays a navigable tree of CTEs, procedures, functions, and other named SQL elements; includes a Refresh button; shows an empty-state message for blank documents.

**Independent Test**: Open a `.sql` file with a CTE and a stored procedure → open Document Outline → both appear as clickable nodes → click a node → editor scrolls to and selects that definition.

- [X] T038 [US10] In `src/AkmlSql.Shell.Shared/Productivity/DocumentOutline/DocumentOutlineViewModel.cs` (~line 112): add a null/empty guard that logs `Log.Debug("DocumentOutline: buffer empty at attach time — deferring until first edit")` and subscribes to `ITextBuffer.Changed` once (unsubscribing after first fire) to call `RequestOutlineUpdate()` when content first appears
- [X] T039 [US10] Verify `[ContentType("tsql")]` and `[TextViewRole(PredefinedTextViewRoles.Document)]` export attributes are present on the `IWpfTextViewCreationListener` export in `src/AkmlSql.Shell.Shared/Commands/DocumentOutlineCommand.cs` (or the relevant MEF listener class); add them if missing so the listener only attaches to SQL editor buffers
- [X] T040 [US10] Add a "↻ Refresh" `Button` to `src/AkmlSql.Shell.Shared/Productivity/DocumentOutline/DocumentOutlineControl.xaml` (or its code-behind): themed with `ThemeManager.Instance` foreground and background colors, frozen brushes; wire its `Click` event to `_viewModel.RequestOutlineUpdate()` — implements FR-019a
- [X] T041 [US10] In the Document Outline tree view (XAML or code-behind): when the returned outline node list is empty after a successful IPC response, show a `TextBlock` with `"No SQL structure found — add CTEs, stored procedures, or functions to see them listed here"` in `PlaceholderText` color — prevents the blank-window appearance
- [ ] T042 [US10] Verify in SSMS22: open SQL file with `WITH MyCTE AS (SELECT 1) SELECT * FROM MyCTE` and `CREATE PROCEDURE dbo.TestProc AS SELECT 1` → open Document Outline → "MyCTE" and "dbo.TestProc" appear as nodes → click each → editor scrolls; edit to add another CTE → click Refresh → new node appears; open blank file → Outline shows empty-state message (not blank)

**Checkpoint**: US10 fully functional — Document Outline shows SQL structure with Refresh button.

---

## Phase 13: User Story 11 — Installer: Remove Desktop Shortcut (Priority: P11)

**Goal**: The installer presents no "Create desktop shortcut" checkbox and creates no desktop shortcut.

**Independent Test**: Run installer — no desktop shortcut option appears on any page; complete install — no desktop shortcut on desktop.

- [X] T043 [US11] In `src/AkmlSql.Installer/AkmlSqlSetup.iss`: delete the `[Icons]` entry that has `Tasks: desktopicon` (~line 146), and delete the `[Tasks]` section entry named `desktopicon` (~lines 148-149) — two lines removed, nothing else touches these names
- [ ] T044 [US11] Build installer: `"/c/Program Files/Inno Setup 7/ISCC.exe" src/AkmlSql.Installer/AkmlSqlSetup.iss`; run the output EXE and walk through all pages — confirm no desktop shortcut checkbox appears; run with `/VERYSILENT /ACCEPTEULA` — confirm no desktop shortcut is created

**Checkpoint**: US11 fully functional — installer has no desktop shortcut option.

---

## Phase 14: User Story 12 — Version Scheme: Major.YY.MMDDHHmm (Priority: P12)

**Goal**: All build outputs (About dialog, VSIX manifests, installer) display version in `1.YY.MMDDHHmm` format (e.g., `1.26.04140511` for 2026-04-14 05:11 UTC+2).

**Independent Test**: Build on 2026-04-14 → About dialog shows `1.26.0414HHMM`; VSIX manifest inside the `.vsix` archive matches; installer AppVersion matches.

- [X] T045 [US12] Update `src/Directory.Build.props` (~lines 10-12): add `<_BuildYear>$([System.DateTime]::UtcNow.AddHours(2).ToString("yy"))</_BuildYear>` and change the `<Version>` property from `1.$(GitCommitCount).$(_BuildStamp)` to `1.$(_BuildYear).$(_BuildStamp)` — produces `1.26.MMDDHHmm` format
- [X] T046 [US12] Update `build.ps1`: (1) compute `$Year = (Get-Date).ToUniversalTime().AddHours(2).ToString("yy")` and `$Stamp = (Get-Date).ToUniversalTime().AddHours(2).ToString("MMddHHmm")`; (2) set `$Version = "1.$Year.$Stamp"`; (3) pass `/DMyAppVersion=$Version` to ISCC; (4) in `src/AkmlSql.Installer/AkmlSqlSetup.iss` change line 51 from hardcoded `#define MyAppVersion "1.0.0"` to `#ifndef MyAppVersion` / `#define MyAppVersion "1.0.0"` / `#endif` so the CLI `/D` override takes precedence
- [X] T047 [P] [US12] Update all 7 VSIX manifest files — replace hardcoded `Version="1.0.0"` (Schema 2011) or `<Version>1.0.0</Version>` (Schema 2010) with the MSBuild `$(Version)` property token so `build.ps1`'s MSBuild invocation injects the computed version at build time — files: `src/AkmlSql.Ssms20/source.extension.vsixmanifest`, `src/AkmlSql.Ssms21/source.extension.vsixmanifest`, `src/AkmlSql.Ssms22/extension.vsixmanifest`, `src/AkmlSql.VS2019/source.extension.vsixmanifest`, `src/AkmlSql.VS2022/extension.vsixmanifest`, `src/AkmlSql.VS2022/source.extension.vsixmanifest`, `src/AkmlSql.VS2026/extension.vsixmanifest`
- [ ] T048 [US12] Run `./build.ps1 -Configuration Release`; verify: (1) About dialog version = `1.26.MMDDHHmm`; (2) open `.vsix` as ZIP → `extension.vsixmanifest` version matches; (3) run installer → AppVersion in installer header matches

**Checkpoint**: US12 fully functional — all build outputs show consistent date-stamped version.

---

## Phase 15: User Story 13 — AI Assistance Inline Help (Priority: P13)

**Goal**: The AI Assistance settings panel shows inline guidance for Claude and Gemini directly below each API key field.

**Independent Test**: Open SQL Options → AI Assistance tab → inline help text visible below each provider's API key field, mentioning API key source and example model name; text is legible in both Light and Dark themes.

- [X] T049 [P] [US13] In `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs` in the AI Assistance settings section (~line 1659): below the Claude API Key `TextBox`, add a `TextBlock` with text `"Get your API key at console.anthropic.com → API Keys. Example model: claude-sonnet-4-6"` — style: `FontSize=11`, `TextWrapping=Wrap`, `Foreground=Freeze(new SolidColorBrush(_theme.PlaceholderText.Color))`
- [X] T050 [P] [US13] In `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs`: below the Gemini API Key `TextBox`, add a `TextBlock` with text `"Get your API key at aistudio.google.com → Get API key. Example model: gemini-2.0-flash"` — same style as T049
- [ ] T051 [US13] Verify in SSMS22 in both Light and Dark themes: open SQL Options → AI Assistance → both provider help texts are visible, correctly styled in muted color, and do not overflow the panel width

**Checkpoint**: US13 fully functional — AI Assistance panel shows inline help for Claude and Gemini.

---

## Phase 16: Polish & Cross-Cutting Concerns

**Purpose**: Final validation and integration check across all groups

- [X] T052 [P] Run full test suite to confirm no regressions introduced: `dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj` — all tests must pass
- [X] T053 [P] Build all modified shell extensions: `MSBuild src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj -t:Restore,Build -p:Configuration=Release -v:minimal` — zero errors, zero warnings on code touched by this branch
- [X] T054 Confirm US14 (installer icon/banner) is deferred — verify `src/AkmlSql.Installer/assets/` contains `icon.ico`, `sidebar.bmp`, and `banner.bmp`; add a comment in `AkmlSqlSetup.iss` above lines 80-82 noting "Assets to be replaced with branded versions from design team"; no other code change needed

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **User Stories (Phases 3–15)**: All independent — can begin after Phase 1 OR in parallel with each other (see exception below)
- **Polish (Phase 16)**: Run after all desired user story phases complete

### User Story Dependencies

All 13 user stories are independently implementable. Only one serialization constraint exists:

| Story | File(s) Modified | Parallel With |
|---|---|---|
| US1 | Engine: Completion/ | US2, US3, US4, US6, US7 |
| US2 | Shell: Commands/, Engine: Analysis | US1, US3, US4 |
| US3 | Engine: Navigation/ | US1, US2, US4 |
| US4 | Core: AppSettings.cs, Shell: Safety/ | US1, US2, US3 |
| US5 | Shell: History/ (ViewModel + Control) | US6, US7, US8, US9, US10, US11, US12, US13 |
| US6 | Shell: History/, Engine: history handler | US5, US9 (different History files) |
| US7 | Shell: Editor/SchemaProgress/ | All others |
| US8 | Shell: Dialogs/SettingsWindow.cs | ⚠️ Serialize with US13 |
| US9 | Shell: History/ (Control only) | US5, US6 |
| US10 | Shell: Productivity/DocumentOutline/ | All others |
| US11 | Installer: AkmlSqlSetup.iss | US12 (different sections, can parallel) |
| US12 | build.ps1, Directory.Build.props, 7× vsixmanifest | US11 |
| US13 | Shell: Dialogs/SettingsWindow.cs | ⚠️ Serialize with US8 |

**⚠️ US8 and US13 both modify `SettingsWindow.cs` — implement sequentially (complete US8 before starting US13) or assign to the same developer.**

### Parallel Execution Examples

```bash
# Batch 1 — engine fixes (entirely different files, zero conflict)
T003-T008  [US1] Completion: CursorContextAnalyzer, CompletionEngine, ColumnProvider
T013-T015  [US3] Navigation: NavigationRequestHandler (one-line fix)
T016-T018  [US4] Config: AppSettings, Safety: ExecutionInterceptor

# Batch 2 — shell History panel (different files within History/)
T019-T021  [US5] HistoryViewModel + HistoryToolWindowControl (badge)
T022-T024  [US6] HistorySearchParser trace (read-only) + engine handler
T035-T037  [US9] HistoryToolWindowControl (rename placeholder + tooltip)

# Batch 3 — installer + build infra (different files)
T043-T044  [US11] AkmlSqlSetup.iss (remove 2 lines)
T045-T048  [US12] Directory.Build.props, build.ps1, 7× vsixmanifest

# Sequential — same file (SettingsWindow.cs)
T032-T034  [US8]  Dark theme fix  →  then  →  T049-T051  [US13] AI inline help

# Independent — own files
T025-T031  [US7]  SchemaProgressMargin (adornment layer refactor)
T009-T012  [US2]  Analysis command + logging
T038-T042  [US10] DocumentOutlineViewModel + Control
```

---

## Implementation Strategy

### MVP (US1 Only — IntelliSense)

1. Phase 1: Setup (T001–T002)
2. Phase 3: US1 (T003–T008)
3. **STOP and VALIDATE**: Confirm UPDATE SET + ALTER TABLE column completions work in SSMS22
4. Demo / deploy

### Recommended Incremental Delivery

Ordered by effort (smallest → largest), maximising parallel work:

| Step | Tasks | Rationale |
|---|---|---|
| 1 | T001–T002 | Baseline verified |
| 2 | T013–T015 (US3) + T016–T018 (US4) + T043–T044 (US11) | 1-liners and tiny fixes — fastest wins |
| 3 | T003–T008 (US1) | Core IntelliSense — primary value |
| 4 | T032–T034 (US8) → T049–T051 (US13) | Same file — do together |
| 5 | T019–T021 (US5) + T035–T037 (US9) + T022–T024 (US6) | History panel — group by file |
| 6 | T009–T012 (US2) | Investigation-first |
| 7 | T025–T031 (US7) | Structural refactor |
| 8 | T038–T042 (US10) | Document Outline |
| 9 | T045–T048 (US12) | Build infra |
| 10 | T052–T054 | Polish + validation |

---

## Notes

- `[P]` tasks = different files, no incomplete task dependencies — safe to parallelize
- `[Story]` maps each task to its user story for traceability and independent delivery
- All new `SolidColorBrush` instances MUST use `Freeze()` per CLAUDE.md
- Shell extensions MUST be built with MSBuild (not `dotnet build`) per CLAUDE.md
- Clear SSMS MEF cache (`%LocalAppData%/Microsoft/SSMS/22.0_*/ComponentModelCache/`) after any shell extension change
- US14 (installer icon/banner) is excluded — design deliverable, no code changes required
- Tests are not explicitly requested in the spec — T007 and T024 are included as lightweight regression guards for the two highest-risk code changes (IntelliSense and advanced search)
