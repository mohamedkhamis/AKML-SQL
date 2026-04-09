---

description: "Tasks for SQL Prompt Parity (spec 014)"
---

# Tasks: SQL Prompt Parity — Close the Gap

**Input**: Design documents from `D:\Repo\01-Khamis-Projects\AKML-SQL\specs\014-sql-prompt-parity\`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Tests ARE included in this task list. SC-009 mandates the existing 867 Engine + 459 Core test baseline must stay green for every milestone, and CLAUDE.md test conventions require xUnit coverage for every new engine routine. Test tasks are written **after** the implementation task they validate (not TDD-first) because the project's existing convention is implementation-first with test backfill — confirmed by the recent commits `2c34133` and `835d662` which added tests alongside (not before) the code.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing. The 20 user stories from spec.md ship in priority order (P1 first as MVP; P2 next; P3 last).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (e.g., US1..US20). Setup, Foundational, and Polish tasks have NO story label.
- All file paths are absolute under `D:\Repo\01-Khamis-Projects\AKML-SQL\`.

## Path Conventions

- **Engine** (`net10.0`, single-file, win-x64): `src/AkmlSql.Engine/...`
- **Core** (`netstandard2.0` + `net10.0` shared library): `src/AkmlSql.Core/...`
- **Shell shared project** (imported by all 6 hosts): `src/AkmlSql.Shell.Shared/...`
- **Per-host shell extensions**: `src/AkmlSql.Ssms20/...`, `src/AkmlSql.Ssms21/...`, `src/AkmlSql.Ssms22/...`, `src/AkmlSql.VS2019/...`, `src/AkmlSql.VS2022/...`, `src/AkmlSql.VS2026/...`
- **Tests**: `tests/AkmlSql.Engine.Tests/...`, `tests/AkmlSql.Core.Tests/...`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Verify the build environment is ready and the 867+459 baseline is green before any new code lands.

- [X] T001 Run baseline tests `dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj` and `dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj` and confirm Engine ≥ 867 / Core ≥ 459 pass before starting work
- [X] T002 [P] Verify all 6 host SDKs are installed by running `MSBuild -t:Restore` against each shell extension project under `src/AkmlSql.Ssms20`, `src/AkmlSql.Ssms21`, `src/AkmlSql.Ssms22`, `src/AkmlSql.VS2019`, `src/AkmlSql.VS2022`, `src/AkmlSql.VS2026`
- [X] T003 [P] Verify `bash hotswap-ssms22.sh` works against an installed SSMS 22 and the engine starts cleanly (sanity check before adding new IPC types)
- [X] T004 [P] Add a top-of-spec marker comment in `src/AkmlSql.Core/Ipc/RpcMessage.cs` reserving MessageType ranges 90–99 (requests) and 190–199 (responses) for spec 014 — also allocated the only three genuinely new ids: `FindInvalidObjects=90/190`, `FindUnusedVariables=91/191`, `EncryptedObjectDecryption=92/192`
- [X] T005 Create the planning index file `doc/spec-014-progress.md` with phase status table, audit findings on reused infrastructure, and update protocol

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add the cross-cutting infrastructure every user story depends on — `AppSettings` sections, IPC message scaffolding, F1 help context base, Options page navigation. **No user story work can begin until this phase is complete.**

- [X] T006 Extend `src/AkmlSql.Core/Config/AppSettings.cs` with the spec-014 properties on existing sections: `SafetySettings` (US1: `MergeNoFilter`, `InsideJoin`, `InsideProcOrTrigger`, `DefaultButton`, `ShowEnvironmentColorInHeader`), `GridSettings` (US16: `EnableCopyAsInClause`, `EnableScriptAsInsert`, `EnableOpenInExcel`, `ScriptAsInsertIncludesIdentity`), `CodeAnalysisSettings` (US17: `LightbulbsEnabled`, `ShowAdvisoryHints`, `ApplyFixOnAllOccurrencesShortcut`), `NavigationSettings` (US13/US20: `EnableF12ScriptAsAlter`, `EnableCtrlF12SelectInOe`, `EnableSummarizeScript`, `EnableFindUnused`, `EnableExecuteCurrentBatch`, `EnableExecuteToCursor`, `EnableBrowseOpenTabs`, `BrowseOpenTabsShortcut`), `CommandPaletteSettings` (US4: `Enabled`, `IncludeAkmlCommands`, `IncludeAkmlOptions`, `IncludeHostCommands`, `IncludeDbObjects`, `MaxRecentItems`, `RecentItems`), `AiSettings` (US10/US18: shortcuts + editor icon + follow-up + comment trigger). Add new `CompletionPolishSettings` class (US19/US2/US8) and wire `AppSettings.CompletionPolish`. **Tab coloring already covered by existing `TabSettings` — no new section needed (audit finding).**
- [X] T007 Skipped — existing `AppSettings.cs` uses `[JsonPropertyName]` not `[Description]`. New properties follow the same XML-doc convention. Settings search will reflect over property names + JSON names per the existing pattern (no behaviour gap).
- [X] T008 Extend `tests/AkmlSql.Core.Tests/Config/AppSettingsTests.cs` with 10 new test methods covering defaults and mutations for every spec-014 addition (`SafetySettings_Spec014_*`, `GridSettings_Spec014_*`, `CodeAnalysisSettings_Spec014_*`, `NavigationSettings_Spec014_*`, `CommandPaletteSettings_Spec014_*`, `AiSettings_Spec014_*`, `CompletionPolishSettings_*`, `AppSettings_HasCompletionPolish`). Result: 23/23 in scope, 469/469 full Core suite (was 459).
- [X] T009 Add 3 new `MessageType` constants to `src/AkmlSql.Core/Ipc/RpcMessage.cs`: `FindInvalidObjects=90`, `FindUnusedVariables=91`, `EncryptedObjectDecryption=92`, plus matching responses 190/191/192. **Audit finding**: every other planned MessageType (Explain SQL, Index Analysis, Comment-to-SQL, DocumentOutline, ScriptAs, GridExport, GetObjectDefinition, RefactorPreview/Apply, AiFix, AiOptimize, AiChat, AiGhostText, SafetyCheck, etc.) already exists from previous specs (010–013). No additional MessageType ints needed.
- [X] T010 Skipped — `SafetyCheckResponse` already uses `SafetyWarningDto[]` (existing file). The "SafetyFinding" name in the contracts doc was a redraft; the existing `SafetyWarningDto` covers FR-002/003/004 once `SafetyCheckHandler` is extended in US1.
- [X] T011 Skipped — `AiExplainRequest/Response`, `AiIndexAnalysisRequest/Response`, `AiTextToSqlRequest/Response` (used as comment-to-SQL transport) all already exist under `src/AkmlSql.Core/Ipc/Messages/` from spec 009 (AI Assistance).
- [X] T012 Create `FindInvalidObjectsRequest.cs`, `FindInvalidObjectsResponse.cs`, `InvalidObjectRecord.cs` under `src/AkmlSql.Core/Ipc/Messages/` — these are genuinely new (no previous spec covered DB-wide invalid-reference scanning).
- [X] T013 Skipped — `RefactorPreviewRequest.cs` / `RefactorPreviewResponse.cs` / `RefactorApplyRequest.cs` / `RefactorApplyResponse.cs` / `RefactorChangeInfo.cs` already exist with a `RefactorOperationType.SafeRename = 0` enum that the spec-015 Smart Rename engine can target. No new transport DTOs needed.
- [X] T014 Create `FindUnusedVariablesRequest.cs`, `FindUnusedVariablesResponse.cs`, `UnusedDeclarationDto.cs` under `src/AkmlSql.Core/Ipc/Messages/`. **`SummarizeScript` and `ScriptObjectAsAlter` already exist** as `DocumentOutlineRequest/Response`+`OutlineNodeDto` and `ScriptAsRequest/Response` from spec 008 — reused as-is.
- [X] T015 Skipped — `CodeAnalysisRequest.cs`, `CodeAnalysisResponse.cs`, `CodeIssueInfo.cs`, `FixActionInfo.cs`, `CodeActionDto.cs` already exist from spec 005 (Static Code Analysis). The `FixActionInfo` payload covers everything spec 014 US17 needs for the lightbulb wiring.
- [X] T016 Skipped — `GridExportRequest.cs` / `GridExportResponse.cs` already exist (spec 011, with `ExcelLargeNumberAsText` property!). Reused as-is for US16 Result Grid productivity.
- [X] T017 Create `EncryptedObjectDecryptionRequest.cs`, `EncryptedObjectDecryptionResponse.cs` under `src/AkmlSql.Core/Ipc/Messages/`. **`RefreshRequest.cs` already exists** for the existing schema-cache refresh — US19 `Ctrl+Shift+D` will reuse it (no new transport).
- [X] T018 Extend `tests/AkmlSql.Core.Tests/Ipc/Messages/IpcMessagesTests.cs` with 8 new test methods covering MessagePack defaults/mutations for every new DTO (FindInvalidObjects ×3, FindUnusedVariables ×3, EncryptedObjectDecryption ×2) + a `Spec014_NewMessageTypes_AreAllocated` cross-check that asserts the 3 new MessageType integers live at 90/91/92 and 190/191/192. Result: full Core suite at 478/478 stable (was 459).
- [X] T019 Add 3 new dispatch cases to `src/AkmlSql.Engine/Server/PipeRpcServer.cs DispatchAsync` for `FindInvalidObjects`, `FindUnusedVariables`, `EncryptedObjectDecryption`, each returning a "not yet implemented" response with the corresponding error status and ErrorMessage citing the user story it lands in. Engine builds 0 errors. Real handlers land in the per-US phases.
- [X] T020 Create `src/AkmlSql.Shell.Shared/Help/F1HelpListener.cs` (singleton with thread-safe `Register`/`TryResolve`/`Open`/`Count` API), wire it into `src/AkmlSql.Shell.Shared/AkmlSql.Shell.Shared.projitems` so all 6 host extensions inherit it. SSMS 22 host extension builds clean (0 warnings, 0 errors) confirming cross-host integration.

**Checkpoint**: All 8 settings sections compile and round-trip; all 14 IPC types are defined and serialize; engine dispatch returns "not implemented" for each; F1 help base is in place. User-story implementation can now begin.

---

## Phase 3: User Story 1 — Pre-execution safety warnings (Priority: P1) 🎯 MVP

**Goal**: Block accidental DELETE / UPDATE without WHERE (and the same patterns inside INNER JOIN, MERGE without WHEN MATCHED, and procedure/trigger bodies) with a confirmation dialog before SSMS sends the SQL to the server.

**Independent Test**: Type `DELETE FROM TestTable;` in SSMS 22, press F5, see the warning dialog naming the statement, server, database, and (if Production-tagged) environment color. Cancel = no execution. Execute = the statement runs.

### Engine: extend SafetyCheckHandler (US1)

- [ ] T021 [US1] Extend `src/AkmlSql.Engine/Refactoring/SafetyCheckHandler.cs` to detect DELETE without WHERE, UPDATE without WHERE, MERGE without WHEN MATCHED, INNER JOIN without WHERE, and the same patterns inside CREATE/ALTER PROCEDURE and CREATE/ALTER TRIGGER bodies via a single `TSqlFragmentVisitor` pass
- [ ] T022 [US1] Update `SafetyCheckRequest` and `SafetyCheckResponse` in `src/AkmlSql.Core/Ipc/Messages/SafetyCheckRequest.cs` and `SafetyCheckResponse.cs` to include the new toggles (`IncludeMergeWithoutFilter`, `IncludeInsideJoin`, `IncludeProcedureBodies`) and the `Findings` array per `contracts/ipc-messages.md`
- [ ] T023 [US1] Wire the augmented handler into `src/AkmlSql.Engine/Server/PipeRpcServer.cs` `DispatchAsync` for `MessageTypes.SafetyCheckRequest`, replacing the foundational stub
- [ ] T024 [P] [US1] Add `tests/AkmlSql.Engine.Tests/Refactoring/SafetyCheckHandlerTests.cs` covering all 5 detection patterns plus the negative cases from spec edge cases (DELETE with subquery WHERE, MERGE with WHEN MATCHED, dynamic SQL, schema-bound objects)

### Shell: ExecutionInterceptor (US1)

- [ ] T025 [US1] Create `src/AkmlSql.Shell.Shared/Editor/Execution/ExecutionInterceptor.cs` MEF export implementing `IOleCommandTarget` chain interception for `cmdidF5`, `cmdidShiftF5`, `cmdidAltShiftF5`, `cmdidCtrlShiftF5` per research R-001
- [ ] T026 [US1] Create `src/AkmlSql.Shell.Shared/Dialogs/SafetyWarningDialog.cs` and `SafetyWarningDialog.xaml` rendering the rule id, statement text, server, database, environment color (read from `TabColoringManager`), with **Cancel** as default focus (FR-005)
- [ ] T027 [US1] Add a per-session opt-out memory dictionary (`Dictionary<string, HashSet<string>>` keyed by `sessionId` → `SuppressedRuleIds`) in `ExecutionInterceptor.cs`, cleared on text-view-closed (FR-006)
- [ ] T028 [US1] Wire `ExecutionInterceptor` to dispatch `SafetyCheckRequest` to the engine via `EngineLifecycle.Manager.Client.SendRequestAsync` with a 500ms timeout (FR-009); on timeout, log a warning and let execution proceed
- [ ] T029 [US1] Register `SafetyWarningDialog` in the F1 help context map with key `akmlsql.dialog.safety` and the corresponding documentation URL in `src/AkmlSql.Shell.Shared/Help/F1HelpListener.cs`

### Settings UI for US1

- [ ] T030 [P] [US1] Add an "Execution Warnings" page to `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs` with toggles for the master switch, each rule id, default-button choice, and the environment-color-in-header option, bound to `AppSettings.ExecutionWarnings`
- [ ] T031 [P] [US1] Add the Execution Warnings page to the search index in `SettingsWindow.cs` so the search box (FR-059) finds the rules by name

### Tests for US1

- [ ] T032 [P] [US1] Add `tests/AkmlSql.Engine.Tests/Refactoring/SafetyCheckPatternTests.cs` with 30 representative SQL statements covering all 5 rules and their edge cases (parameterised xUnit theory)
- [ ] T033 [P] [US1] Add `tests/AkmlSql.Core.Tests/Config/ExecutionWarningsSettingsTests.cs` to verify default rule list and round-trip behaviour
- [ ] T034 [US1] Run all Engine + Core tests and confirm 867+459 baseline plus US1 additions all pass
- [ ] T035 [US1] Hot-swap to SSMS 22 (`bash hotswap-ssms22.sh`) and walk through the US1 section of `quickstart.md`; mark the corresponding boxes

**Checkpoint**: US1 complete. AKML SQL now blocks unsafe DELETE/UPDATE/MERGE before they reach the server. **MVP shippable.**

---

## Phase 4: User Story 5 — Environment-based tab coloring (Priority: P2)

**Goal**: Color query tabs by environment (Production red, Staging orange, Development green) so users see at a glance which environment they're about to run code against. Pairs with US1 for safety.

**Independent Test**: Right-click a query tab → Tab Color (Server) → Production. The tab turns red. Open a second query against a different server tagged Development; that tab is green. Edit the Production color in Options; all tabs update without restart.

### Engine: no engine work (settings-only feature)

### Shell: TabColoringManager wiring (US5)

- [ ] T036 [US5] Update `src/AkmlSql.Shell.Shared/TabColoring/TabColoringManager.cs` to read `Environment[]` and `TabColorAssignment[]` from `AppSettings.TabColoring` instead of any hard-coded defaults
- [ ] T037 [US5] Update `src/AkmlSql.Shell.Shared/TabColoring/EnvironmentDetector.cs` to resolve a `TabColorAssignment` for a given (server, database, group) tuple using the priority order in FR-045 (server > database > group)
- [ ] T038 [P] [US5] Create `src/AkmlSql.Shell.Shared/TabColoring/TabContextMenuExtender.cs` MEF export adding "Tab Color (Server)", "Tab Color (Database)", and "Tab Color (Server Group)" submenus to the query-tab right-click menu (FR-041)
- [ ] T039 [US5] Add a live re-render handler in `TabColoringManager.cs` that subscribes to `ConfigManager.SettingsChanged` and re-paints all open tabs without restart (FR-042)
- [ ] T040 [US5] Implement the gradient brush logic in `TabColoringManager.cs` (lighter at top, darker at bottom) gated on the `GradientEnabled` setting (FR-044)
- [ ] T041 [US5] Add WCAG-AA contrast adjustment to `TabColoringManager.cs` for high-contrast Windows themes (FR-046, edge case)

### Options page (US5)

- [ ] T042 [P] [US5] Create `src/AkmlSql.Shell.Shared/Dialogs/EnvironmentPaletteWindow.cs` and `.xaml` allowing the user to add/edit/remove environments and assignments (FR-043), bound to `AppSettings.TabColoring`
- [ ] T043 [P] [US5] Add the Tabs > Color page to `SettingsWindow.cs` linking to `EnvironmentPaletteWindow` and bound to the `TabColoring` settings section

### Tests for US5

- [ ] T044 [P] [US5] Add `tests/AkmlSql.Core.Tests/Config/TabColoringSettingsTests.cs` covering default environments (Production, Staging, Development), color hex validation, and assignment-priority resolution

**Checkpoint**: US5 complete. The US1 safety dialog can now render its header in the environment color (cross-story polish).

---

## Phase 5: User Story 2 — Column Picker inside the completion popup (Priority: P2)

**Goal**: Multi-select columns from a table in the completion popup via `Ctrl+Left`, with PK/FK badges and table-order vs alphabetical toggle.

**Independent Test**: Type `SELECT  FROM dbo.LargeTable`, press `Ctrl+Left`, see the Column Picker. `Space` to multi-select, `Enter` to insert the comma-separated list at the cursor.

- [ ] T045 [P] [US2] Create `src/AkmlSql.Shell.Shared/Editor/Completion/ColumnPickerControl.cs` and `.xaml` with a `ListBox` of columns, PK/FK badge columns, sort toggle button, and selected-count footer
- [ ] T046 [P] [US2] Create `src/AkmlSql.Shell.Shared/Editor/Completion/ColumnPickerSelection.cs` POCO matching the data-model entity (parent table/alias, selected list, filter, sort mode)
- [ ] T047 [US2] Extend `src/AkmlSql.Shell.Shared/Editor/Completion/AkmlCompletionPopup.cs` with a second `ContentPresenter` for `ColumnPickerControl`, plus state machine to switch between suggestion list and picker via `Ctrl+Left`/`Ctrl+Right` (FR-010, R-002)
- [ ] T048 [US2] Wire `Space` to toggle row selection and `Ctrl+A` to select all in `ColumnPickerControl.cs` (FR-013)
- [ ] T049 [US2] Wire `Enter` and `Tab` in `ColumnPickerControl.cs` to insert the selected columns comma-separated at the caret position via `ITextEdit` on the parent text view, qualified with the table alias when multiple tables are in scope (FR-014, FR-015)
- [ ] T050 [US2] Wire `Esc` in `ColumnPickerControl.cs` to close the picker without inserting (FR-016)
- [ ] T051 [P] [US2] Add `tests/AkmlSql.Engine.Tests/Completion/ColumnPickerSelectionTests.cs` covering insertion-order preservation, alphabetical sort, and qualification when multiple tables are in scope (engine logic only — picker UI is integration-tested manually)
- [ ] T052 [US2] Hot-swap to SSMS 22 and walk through the US2 section of `quickstart.md`

**Checkpoint**: US2 complete.

---

## Phase 6: User Story 3 — Wildcard expansion `*`+Tab (Priority: P2)

**Goal**: Pressing `Tab` immediately after `*` (or `alias.*`) expands the wildcard into the explicit column list.

**Independent Test**: Type `SELECT * FROM Customers c`, position cursor right after `*`, press `Tab`, see the column list inserted in place of `*`.

- [ ] T053 [US3] Create `src/AkmlSql.Shell.Shared/Editor/Completion/TabWildcardExpansionFilter.cs` MEF export implementing `IOleCommandTarget` for `cmdidTab`, checking whether the immediately preceding non-whitespace character is `*` or `alias.*`, and dispatching `WildcardExpansionRequest` to the engine when matched (R-003)
- [ ] T054 [US3] Ensure the filter returns `OLECMDERR_E_NOTSUPPORTED` for non-matching contexts so normal Tab (indent / completion commit) still works
- [ ] T055 [P] [US3] Add `tests/AkmlSql.Engine.Tests/Refactoring/WildcardExpansionContextTests.cs` covering `SELECT * FROM`, `SELECT c.* FROM`, multiple-table FROM, and the negative cases (cursor not after `*`)
- [ ] T056 [US3] Hot-swap to SSMS 22 and walk through the US3 section of `quickstart.md`

**Checkpoint**: US3 complete.

---

## Phase 7: User Story 4 — Command Palette (Priority: P2)

**Goal**: Unified `Alt+S` (SSMS) / `Alt+P` (VS) palette that fuzzy-searches AKML SQL commands, AKML SQL options, host commands, and (SSMS only) database objects.

**Independent Test**: Press `Alt+S`, type `format`, see results across all four categories. Pick a result, see the corresponding action run.

### Sources (US4)

- [ ] T057 [P] [US4] Create `src/AkmlSql.Shell.Shared/CommandPalette/ICommandPaletteSource.cs` interface with `IEnumerable<CommandPaletteEntry> GetEntries()`
- [ ] T058 [P] [US4] Create `src/AkmlSql.Shell.Shared/CommandPalette/CommandPaletteEntry.cs` POCO matching the data-model entity (display label, category, fuzzy score, invoke action, optional icon)
- [ ] T059 [P] [US4] Create `src/AkmlSql.Shell.Shared/CommandPalette/Sources/AkmlCommandSource.cs` enumerating every registered `OleMenuCommand` in the AKML SQL command set
- [ ] T060 [P] [US4] Create `src/AkmlSql.Shell.Shared/CommandPalette/Sources/AkmlOptionsSource.cs` reflecting over `AppSettings` properties tagged with `[Description]`
- [ ] T061 [P] [US4] Create `src/AkmlSql.Shell.Shared/CommandPalette/Sources/HostCommandSource.cs` enumerating SSMS / VS built-in commands via `EnvDTE.DTE.Commands`
- [ ] T062 [P] [US4] Create `src/AkmlSql.Shell.Shared/CommandPalette/Sources/DatabaseObjectSource.cs` reading from the active session's `DatabaseCache` (SSMS only — gated on host detection)

### Palette window (US4)

- [ ] T063 [US4] Extend `src/AkmlSql.Shell.Shared/CommandPalette/CommandPaletteWindow.cs` to aggregate the four sources, rank entries via the existing `AkmlSql.Engine.Completion.FuzzyMatcher`, and render a category badge per row
- [ ] T064 [US4] Wire the `Alt+S` chord (SSMS) and `Alt+P` chord (VS) in each host's `.vsct` file (`src/AkmlSql.Ssms20/AkmlSqlSsms20.vsct`, `src/AkmlSql.Ssms21/AkmlSqlSsms21.vsct`, `src/AkmlSql.Ssms22/AkmlSqlSsms22.vsct`, `src/AkmlSql.VS2019/AkmlSqlVS2019.vsct`, `src/AkmlSql.VS2022/AkmlSqlVS2022.vsct`, `src/AkmlSql.VS2026/AkmlSqlVS2026.vsct`) to the new `cmdidShowCommandPalette` (`0x0164`)
- [ ] T065 [US4] Add Most-Recent-Items behaviour to `CommandPaletteWindow.cs` reading and writing `AppSettings.CommandPalette.RecentItems` (FR-052)
- [ ] T066 [P] [US4] Add `tests/AkmlSql.Engine.Tests/Completion/FuzzyMatcherCommandPaletteTests.cs` validating that fuzzy ranking gives expected order for "format", "fmt", "smart rename", "explain"
- [ ] T067 [US4] Hot-swap to SSMS 22 and walk through the US4 section of `quickstart.md`

**Checkpoint**: US4 complete.

---

## Phase 8: User Story 6 — Code Analysis Issues window (Priority: P2)

**Goal**: Dockable tool window listing every analysis issue in the active script with click-to-navigate, sort, group, CSV export, and persistent docked position.

**Independent Test**: Open a script with ≥10 known issues, open the Issues window, click a row → editor jumps to the line. Edit the script → list refreshes within 1s. Click Export → CSV file is saved.

### Engine push notification (US6)

- [ ] T068 [US6] Modify `src/AkmlSql.Engine/Analysis/AnalysisEngine.cs` to publish an `AnalysisIssuesPushed` notification (MessageType `300`) after every analysis run, sending the full issue list for the active document
- [ ] T069 [US6] Modify `src/AkmlSql.Engine/Server/PipeRpcServer.cs` to expose a `SendNotification(MessageType, payload)` method that fires-and-forgets a frame to the connected client without blocking the dispatch loop

### Shell tool window (US6)

- [ ] T070 [US6] Create `src/AkmlSql.Shell.Shared/ToolWindows/CodeAnalysisIssuesToolWindow.cs` and `.xaml` hosting a `DataGrid` bound to a `CollectionView` over `AnalysisIssue[]`
- [ ] T071 [US6] Wire `EngineLifecycle.Manager.Client.NotificationReceived` in `CodeAnalysisIssuesToolWindow.cs` to refresh the grid on every `AnalysisIssuesPushed` notification (FR-039 — within 1s of typing pause)
- [ ] T072 [US6] Add column-header sorting, group-by-rule toggle, and CSV export button in `CodeAnalysisIssuesToolWindow.cs` (FR-038)
- [ ] T073 [US6] Wire row click in `CodeAnalysisIssuesToolWindow.cs` to scroll the editor to the offending span and highlight it (FR-037)
- [ ] T074 [US6] Persist docked position via `[ProvideToolWindowVisibility]` attribute and standard `ToolWindowPane.SaveUIState` in `CodeAnalysisIssuesToolWindow.cs` (FR-040)
- [ ] T075 [US6] Hot-swap to SSMS 22 and walk through the US6 section of `quickstart.md`

**Checkpoint**: US6 complete.

---

## Phase 9: User Story 13 — Script navigation chords (Priority: P2)

**Goal**: Four navigation chords — Summarize Script (`Ctrl+B,Ctrl+S`), Script Object as ALTER (`F12`), Select in Object Explorer (`Ctrl+F12`), Find Unused Variables (`Ctrl+B,Ctrl+F`).

**Independent Test**: Open a 500-line script. Press `Ctrl+B,Ctrl+S` → outline appears. Click an entry → editor jumps. Place caret on `dbo.MyProc`, press `F12` → `ALTER` script opens in a new window.

### Summarize Script (US13)

- [ ] T076 [US13] Create `src/AkmlSql.Engine/Refactoring/SummarizeScriptEngine.cs` implementing a `TSqlFragmentVisitor` walk that produces `ScriptOutlineNode[]` for every top-level statement (USE/CREATE/ALTER/SELECT/INSERT/UPDATE/DELETE/EXEC/EXEC AS/REVERT/DROP/TRUNCATE/MERGE)
- [ ] T077 [US13] Wire `SummarizeScriptRequest` (MessageType `96`) into `src/AkmlSql.Engine/Server/PipeRpcServer.cs` dispatching to `SummarizeScriptEngine`
- [ ] T078 [P] [US13] Add `tests/AkmlSql.Engine.Tests/Refactoring/SummarizeScriptEngineTests.cs` with at least 8 representative scripts (single-statement, multi-statement, nested CTE, EXEC AS REVERT pair, ≥1000-line stress)
- [ ] T079 [P] [US13] Create `src/AkmlSql.Shell.Shared/Dialogs/SummarizeScriptDialog.cs` and `.xaml` rendering the outline in a `TreeView` with click-to-navigate

### Script Object as ALTER (US13)

- [ ] T080 [US13] Add `GetObjectAsAlterAsync(connectionString, schema, name, ct)` to `src/AkmlSql.Engine/Schema/SchemaMetadataService.cs` using `OBJECT_DEFINITION` and a regex rewrite of `CREATE` → `ALTER`
- [ ] T081 [US13] Wire `ScriptObjectAsAlterRequest` (MessageType `97`) into `PipeRpcServer.cs`
- [ ] T082 [US13] Create `src/AkmlSql.Shell.Shared/Refactoring/ScriptObjectAsAlterCommand.cs` reading the identifier under the caret, dispatching the request, and opening the result in a new query window via `EnvDTE.DTE.ItemOperations.NewFile`

### Select in Object Explorer (US13)

- [ ] T083 [US13] Create `src/AkmlSql.Shell.Shared/Refactoring/SelectInObjectExplorerCommand.cs` using `IObjectExplorerService` to expand the OE tree to the object under the caret (SSMS only — gracefully no-ops in VS)

### Find Unused Variables (US13)

- [ ] T084 [US13] Create `src/AkmlSql.Engine/Refactoring/FindUnusedEngine.cs` with a single AST walk that records every `DECLARE @x` and procedure parameter, then a second pass to mark each as read; emits the unread set as `UnusedDeclaration[]`
- [ ] T085 [US13] Wire `FindUnusedVariablesRequest` (MessageType `98`) into `PipeRpcServer.cs`
- [ ] T086 [P] [US13] Add `tests/AkmlSql.Engine.Tests/Refactoring/FindUnusedEngineTests.cs` covering: unused variable, unused parameter, used-but-only-assigned, conditional usage in IF
- [ ] T087 [US13] Create `src/AkmlSql.Shell.Shared/ToolWindows/FindUnusedVariablesToolWindow.cs` displaying the result with click-to-navigate

### Wiring + chord registration (US13)

- [ ] T088 [US13] Add `cmdidSummarizeScript` (`0x0154`), `cmdidScriptObjectAsAlter` (`0x0152`), `cmdidSelectInObjectExplorer` (`0x0153`), `cmdidFindUnusedDeclarations` (`0x0155`) to all 6 host `.vsct` files with their chord bindings per `contracts/command-bindings.md`

**Checkpoint**: US13 complete.

---

## Phase 10: User Story 14 — Find Invalid Objects (Priority: P2)

**Goal**: Object Explorer right-click → Find Invalid Objects scans for broken-reference objects and lists them in a dockable tool window with Script-as-ALTER actions.

**Independent Test**: Right-click a database with known invalid views, see them listed. Click Script as ALTER → new query window opens with the ALTER scripts.

- [ ] T089 [US14] Add `ScanInvalidObjectsAsync(connectionString, ct)` to `src/AkmlSql.Engine/Schema/SchemaMetadataService.cs` querying `sys.sql_expression_dependencies` joined to `sys.sql_modules`, yielding `IAsyncEnumerable<InvalidObjectRecord>` in chunks of 50 per R-014
- [ ] T090 [US14] Wire `FindInvalidObjectsRequest` (MessageType `93`) into `PipeRpcServer.cs` and emit `InvalidObjectsScanProgress` notifications (MessageType `301`) for each chunk
- [ ] T091 [P] [US14] Add `tests/AkmlSql.Engine.Tests/Schema/InvalidObjectScanTests.cs` with a stub `IDbConnection` returning canned `sys.sql_expression_dependencies` rows for: missing table, missing column, missing schema, valid object (negative case)
- [ ] T092 [P] [US14] Create `src/AkmlSql.Shell.Shared/ToolWindows/InvalidObjectsToolWindow.cs` and `.xaml` rendering streaming results in a `DataGrid` with columns Schema/Name/Type/ErrorMessage/SourceLine
- [ ] T093 [US14] Add multi-row select + Script as ALTER button in `InvalidObjectsToolWindow.cs` that dispatches one `ScriptObjectAsAlterRequest` per selected row and concatenates the results into a single new query window
- [ ] T094 [US14] Add double-click handler in `InvalidObjectsToolWindow.cs` that expands Object Explorer to the clicked node and shows the error message in the status bar
- [ ] T095 [US14] Add a "Find Invalid Objects" entry to the Object Explorer database right-click menu in `src/AkmlSql.Shell.Shared/ObjectExplorer/ObjectExplorerContextMenuExtender.cs` (NEW file) — SSMS only, gracefully no-ops in VS
- [ ] T096 [US14] Register `InvalidObjectsToolWindow` in F1 help context map with key `akmlsql.window.invalid-objects`
- [ ] T097 [US14] Hot-swap to SSMS 22 and walk through the US14 section of `quickstart.md`

**Checkpoint**: US14 complete.

---

## Phase 11: User Story 17 — Code Analysis lightbulb quick-fixes (Priority: P2)

**Goal**: Lightbulb gutter icon (orange = fixable, blue = advisory) plus an Issue Details popup with Apply Fix and Disable Rule buttons. Auto-fixes for the ~27 known fixable rules.

**Independent Test**: Type `WHERE x != 1` (triggers BP002). See orange lightbulb. Hold `Ctrl` over the squiggle, click Apply Fix → `!=` becomes `<>`.

- [ ] T098 [US17] Create `src/AkmlSql.Engine/Analysis/AnalysisFixDispatcher.cs` registering one fix routine per known auto-fixable rule id (~27 rules per A17), each calling into the existing `RefactoringEngine`
- [ ] T099 [US17] Wire `AnalysisFixRequest` (MessageType `99`) into `PipeRpcServer.cs` dispatching to `AnalysisFixDispatcher`
- [ ] T100 [US17] Extend `AnalysisIssue` DTO in `src/AkmlSql.Core/Ipc/Messages/AnalysisIssue.cs` with `ProblemText`, `RemediationText`, `IsAutoFixable` properties; populate them from the rule registry in `src/AkmlSql.Engine/Analysis/RuleRegistry.cs`
- [ ] T101 [P] [US17] Add `tests/AkmlSql.Engine.Tests/Analysis/AnalysisFixDispatcherTests.cs` covering: known fixable rule applies fix, advisory rule returns `NoFixAvailable`, schema-dependent fix returns `WaitingForSchema`
- [ ] T102 [P] [US17] Create `src/AkmlSql.Shell.Shared/Editor/Lightbulbs/LightbulbAdornment.cs` MEF export rendering an orange or blue lightbulb in the editor gutter for each `AnalysisIssue` based on `IsAutoFixable`
- [ ] T103 [P] [US17] Create `src/AkmlSql.Shell.Shared/Editor/Lightbulbs/IssueDetailsPopup.cs` and `.xaml` triggered by `Ctrl+hover` on a squiggle, showing rule id / severity / problem / remediation and Apply Fix + Disable Rule buttons
- [ ] T104 [US17] Wire Apply Fix in `IssueDetailsPopup.cs` to dispatch `AnalysisFixRequest` and apply the returned `NewDocumentText` via `ITextEdit` (FR-081)
- [ ] T105 [US17] Wire Disable Rule in `IssueDetailsPopup.cs` to insert the `-- akml-disable RuleId` comment at the top of the file or update `.casettings` (FR-082)
- [ ] T106 [US17] Hot-swap to SSMS 22 and walk through the US17 section of `quickstart.md`

**Checkpoint**: US17 complete. **All P2 stories shipped.**

---

## Phase 12: User Story 7 — Full `Ctrl+B` refactoring chord family (Priority: P3)

**Goal**: Bind the seven `Ctrl+B,Ctrl+*` chords from SQL Prompt: Apply Casing, Qualify Object Names, Expand Wildcards, Insert Semicolons, Add/Remove Brackets, Inline Procedure, Encapsulate as Procedure.

**Independent Test**: Select code, press `Ctrl+B,Ctrl+U` → keyword casing normalised. Repeat for the other six chords; each performs its respective action.

- [ ] T107 [US7] Add `cmdidApplyCasing` (`0x0156`), `cmdidQualifyObjectNames` (`0x0157`), `cmdidExpandWildcards` (`0x0158`), `cmdidInsertSemicolons` (`0x0159`), `cmdidToggleBrackets` (`0x015A`), `cmdidInlineProcedure` (`0x015B`), `cmdidEncapsulateAsProcedure` (`0x015C`) to all 6 host `.vsct` files with their chord bindings per `contracts/command-bindings.md`
- [ ] T108 [US7] Create `src/AkmlSql.Shell.Shared/Refactoring/CtrlBChordHandler.cs` registering one `OleMenuCommand` per chord, each dispatching to the corresponding existing engine refactoring routine
- [ ] T109 [P] [US7] Add `ApplyCasingAsync` to `src/AkmlSql.Engine/Refactoring/RefactoringEngine.cs` if not already present, calling into the existing `FormatterPipeline.CasingEngine`
- [ ] T110 [P] [US7] Add `QualifyObjectNamesAsync` to `RefactoringEngine.cs` if not already present
- [ ] T111 [P] [US7] Add `InsertSemicolonsAsync` to `RefactoringEngine.cs` walking the AST and inserting `;` after every statement that needs one
- [ ] T112 [P] [US7] Add `ToggleBracketsAsync` to `RefactoringEngine.cs` adding `[ ]` to identifiers without brackets and removing them from those with
- [ ] T113 [P] [US7] Add `InlineProcedureAsync` to `RefactoringEngine.cs` resolving the `EXEC procName` reference, fetching the procedure body, and inlining it (gracefully refusing for procs with parameters / dynamic SQL)
- [ ] T114 [P] [US7] Add `EncapsulateAsProcedureAsync` to `RefactoringEngine.cs` wrapping the selection in a `CREATE PROCEDURE` skeleton, auto-detecting parameters from variable references
- [ ] T115 [P] [US7] Add `tests/AkmlSql.Engine.Tests/Refactoring/CtrlBChordTests.cs` covering all 7 routines with at least 3 representative inputs each
- [ ] T116 [US7] Add the 7 chord commands to the **AKML SQL → Refactoring** menu in each host `.vsct` per the menu structure in `contracts/command-bindings.md`
- [ ] T117 [US7] Add the 7 commands to the Command Palette source list in `src/AkmlSql.Shell.Shared/CommandPalette/Sources/AkmlCommandSource.cs` (already auto-discovered via `OleMenuCommandService`, but add metadata for better fuzzy ranking)
- [ ] T118 [US7] Hot-swap to SSMS 22 and walk through the US7 section of `quickstart.md`

**Checkpoint**: US7 complete.

---

## Phase 13: User Story 8 — Object Definition Box (Priority: P3)

**Goal**: Side panel next to the completion popup with Summary + Script tabs, resizable, persistent size, semi-transparent on `Ctrl`-hold.

**Independent Test**: Type `SELECT * FROM Cust`, select `Customers` in popup. Side panel shows columns/types on Summary tab. Click Script → `CREATE TABLE` shown. Drag corner to resize → new size persists across SSMS restart.

- [ ] T119 [US8] Create `src/AkmlSql.Shell.Shared/Editor/Completion/ObjectDefinitionBox.cs` and `.xaml` with two tabs: Summary (`DataGrid` of column metadata) and Script (read-only `SyntaxColoringTextBox`), plus a corner-drag resize grip
- [ ] T120 [US8] Add `ObjectDefinition` POCO in `src/AkmlSql.Core/Ipc/Messages/ObjectDefinition.cs` matching the data-model entity
- [ ] T121 [US8] Add `GetObjectDefinitionAsync(connectionString, db, schema, name, ct)` to `src/AkmlSql.Engine/Schema/SchemaMetadataService.cs` returning column / parameter / row-count metadata plus the CREATE script
- [ ] T122 [US8] Wire `ObjectDefinitionBox` to dispatch `GetObjectDefinitionAsync` whenever the highlighted suggestion in `AkmlCompletionPopup` changes (debounced 200ms to avoid flooding)
- [ ] T123 [US8] Persist `Width` / `Height` in `AppSettings.CompletionPolish.ObjectDefinitionBoxSize` after corner drag completes (FR-023)
- [ ] T124 [US8] Add a global `KeyDown`/`KeyUp` listener in `AkmlCompletionPopup.cs` that sets `Opacity = 0.4` on both popup and definition box while `Ctrl` is held (FR-024, R-008)
- [ ] T125 [P] [US8] Add `tests/AkmlSql.Engine.Tests/Schema/GetObjectDefinitionTests.cs` covering tables, views, procedures (with parameters), functions, and the encrypted-fallback path
- [ ] T126 [US8] Hot-swap to SSMS 22 and walk through the US8 section of `quickstart.md`

**Checkpoint**: US8 complete.

---

## Phase 14: User Story 9 — Inline `-- akml-format off / on` markers (Priority: P3)

**Goal**: Action list entry "Disable formatting for selected text" wraps the selection in marker comments. The existing `NoformatScanner` already honours them.

**Independent Test**: Select a UNION block, hold `Ctrl`, pick "Disable formatting for selected text". The selection is wrapped. Run Format Document → wrapped block preserved verbatim, rest reformatted.

- [ ] T127 [US9] Create `src/AkmlSql.Shell.Shared/Editor/Formatting/DisableFormattingActionProvider.cs` MEF export implementing `ITextActionListProvider` to contribute the action when there is a non-empty selection
- [ ] T128 [US9] Implement the action body in `DisableFormattingActionProvider.cs` using `ITextEdit` to wrap the selection with `\n-- akml-format off\n` and `\n-- akml-format on\n` (FR-031)
- [ ] T129 [P] [US9] Add `tests/AkmlSql.Engine.Tests/Formatting/NoformatScannerEdgeCaseTests.cs` covering nested off/off, unmatched off, markers inside string literals (edge cases per spec)
- [ ] T130 [US9] Hot-swap to SSMS 22 and walk through the US9 section of `quickstart.md`

**Checkpoint**: US9 complete.

---

## Phase 15: User Story 10 — AI keyboard shortcuts (Priority: P3)

**Goal**: Bind `Alt+Z` (open chat), `Shift+Alt+R` (fix selection), `Ctrl+Alt+Z` (optimize selection), `Ctrl+Alt+Up` (manual ghost text). When AI is disabled, show a status-bar message.

**Independent Test**: With AI enabled, press each shortcut → corresponding flow runs. With AI disabled, see a status-bar message and no action.

- [ ] T131 [US10] Add `cmdidAiOpenChat` (`0x015D`), `cmdidAiFixSelection` (`0x015E`), `cmdidAiOptimizeSelection` (`0x015F`), `cmdidAiManualGhostText` (`0x0160`) to all 6 host `.vsct` files with their chord bindings per `contracts/command-bindings.md`
- [ ] T132 [US10] Create `src/AkmlSql.Shell.Shared/Ai/AiShortcutHandlers.cs` registering one `OleMenuCommand` per chord, dispatching to the existing `AiChatPanelService`
- [ ] T133 [US10] Add the AI-disabled status-bar message in `AiShortcutHandlers.cs` reading `AppSettings.Ai.Enabled` (FR-057)
- [ ] T134 [US10] Add the four shortcuts to the **AKML SQL → AI** menu in each host `.vsct` per the menu structure in `contracts/command-bindings.md`
- [ ] T135 [P] [US10] Add `tests/AkmlSql.Core.Tests/Config/AiShortcutSettingsTests.cs` covering default shortcut strings and round-trip
- [ ] T136 [US10] Register all AI dialogs in F1 help context map with appropriate URLs
- [ ] T137 [US10] Hot-swap to SSMS 22 and walk through the US10 section of `quickstart.md`

**Checkpoint**: US10 complete.

---

## Phase 16: User Story 11 — Dual-instance awareness regression guard (Priority: P3)

**Goal**: Ensure the cross-server leak fixed in commit `2c34133` cannot regress. Pure regression-test work.

**Independent Test**: Two query windows on two different servers; type `USE ` in each — only the matching server's databases appear. 50 sequential runs, zero leaks.

- [ ] T138 [US11] Add `tests/AkmlSql.Engine.Tests/Connection/SsmsConnectionDetectorRegressionTests.cs` stubbing `EnvDTE.Documents` with two documents (different file paths, different captions) and asserting `TryDetectConnection(textView)` returns the correct one for each, never falling back to `ActiveDocument`
- [ ] T139 [US11] Add `tests/AkmlSql.Engine.Tests/Completion/DatabaseProviderCacheIsolationTests.cs` exercising session A's connection, switching to session B, and asserting the cache is invalidated and the new server's databases are returned
- [ ] T140 [US11] Hot-swap to SSMS 22 and run the 50-iteration manual test from the US11 section of `quickstart.md`, capturing the log to confirm zero `ActiveDocument` fallbacks

**Checkpoint**: US11 complete.

---

## Phase 17: User Story 12 — Settings surface for every new feature (Priority: P3)

**Goal**: Verify every spec-014 feature has an Options entry, that the search box finds each by name, and that toggling any feature off takes effect within 1s without restart.

**Independent Test**: Open Options, search for each new feature by name, toggle off, verify the feature stops within 1s.

- [ ] T141 [US12] Audit `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs` to verify every `AppSettings` section added in T006 has a corresponding Options page; add any missing pages
- [ ] T142 [US12] Verify the Options search box implementation in `SettingsWindow.cs` uses reflection over `AppSettings` `[Description]` attributes (added in T007) and returns matches for the spec-014 feature names
- [ ] T143 [US12] Wire `ConfigManager.SettingsChanged` event in every shell-side feature added by US1..US20 so toggling settings live takes effect within 1s (FR-060) — this is a cross-cutting audit task
- [ ] T144 [P] [US12] Add `tests/AkmlSql.Core.Tests/Config/AppSettingsSearchIndexTests.cs` reflecting over `AppSettings` and asserting every feature in spec 014 is found by its display label
- [ ] T145 [US12] Register every Options page in F1 help context map with appropriate URLs
- [ ] T146 [US12] Walk through the US12 section of `quickstart.md` against the running SSMS 22 instance

**Checkpoint**: US12 complete.

---

## Phase 18: User Story 15 — Smart Rename with dependency preview (Priority: P3)

**Goal**: Database-wide rename of object/column/parameter with dependency-resolution preview dialog and transactional apply.

**Independent Test**: Rename a column referenced by 3 views, 2 procedures, 1 trigger via `F2`. Preview dialog shows all 6 dependents. Click Apply → all 6 still parse cleanly after the rename.

### Engine Smart Rename (US15)

- [ ] T147 [US15] Create `src/AkmlSql.Engine/Refactoring/SmartRenameEngine.cs` with `BuildPlanAsync(connectionString, target, newName, ct)` that resolves all dependent objects via `sys.sql_expression_dependencies` and generates the full `BEGIN TRAN ... sp_rename + ALTER ... COMMIT` script
- [ ] T148 [US15] Add `ApplyAsync(plan, ct)` to `SmartRenameEngine.cs` running the generated script inside a single transaction with rollback on any failure (R-013, FR-071)
- [ ] T149 [US15] Wire `SmartRenamePreviewRequest` (MessageType `94`) and `SmartRenameApplyRequest` (MessageType `95`) into `PipeRpcServer.cs` dispatching to `SmartRenameEngine`
- [ ] T150 [US15] Emit `SmartRenameApplyProgress` notifications (MessageType `302`) at each stage (parsing, executing rename, rewriting dependents, committing)
- [ ] T151 [US15] Add collision detection in `BuildPlanAsync` that sets `HasUnresolvedCollision = true` when the target name already exists in the same scope (FR-073)
- [ ] T152 [US15] Add extended-property and permission preservation logic in `SmartRenameEngine` that captures `sys.fn_listextendedproperty` and `sys.database_permissions` rows for the target before rename and reapplies them after (FR-072)
- [ ] T153 [P] [US15] Add `tests/AkmlSql.Engine.Tests/Refactoring/SmartRenameEngineTests.cs` covering: simple column rename, column with FK target, column with extended property, name collision, transactional rollback on failure, system-table refusal

### Shell Smart Rename Dialog (US15)

- [ ] T154 [US15] Create `src/AkmlSql.Shell.Shared/Dialogs/SmartRenameDialog.cs` and `.xaml` with old-name display, new-name input, Preview button, and three tabs (Actions / Warnings / Dependencies)
- [ ] T155 [US15] Wire the Preview button in `SmartRenameDialog.cs` to dispatch `SmartRenamePreviewRequest` and populate the three tabs from the response
- [ ] T156 [US15] Wire the Apply button to dispatch `SmartRenameApplyRequest`, show progress from `SmartRenameApplyProgress` notifications, and disable the button when `HasUnresolvedCollision` is true
- [ ] T157 [US15] Add `cmdidShowSmartRenameDialog` (`0x0166`) to all 6 host `.vsct` files bound to `F2` with editor scope per `contracts/command-bindings.md`
- [ ] T158 [US15] Add Object Explorer right-click "Smart Rename..." menu item via the `ObjectExplorerContextMenuExtender.cs` from T095
- [ ] T159 [US15] Register `SmartRenameDialog` in F1 help context map with key `akmlsql.dialog.smartrename`
- [ ] T160 [US15] Hot-swap to SSMS 22 and walk through the US15 section of `quickstart.md` against a real test database

**Checkpoint**: US15 complete.

---

## Phase 19: User Story 16 — Result-grid productivity (Priority: P3)

**Goal**: Right-click result grid → Copy as IN Clause / Script as INSERT / Open in Excel with full precision.

**Independent Test**: Run `SELECT TOP 10 Id FROM Customers`, right-click the grid → each of the three actions produces the expected output.

- [ ] T161 [US16] Create `src/AkmlSql.Engine/Refactoring/ResultGridScriptEngine.cs` with three methods: `BuildInClause`, `BuildInsertStatement`, `BuildExcelExport`, each accepting a `ResultGridContext` and returning a `ResultGridScript`
- [ ] T162 [US16] Wire `ResultGridScriptRequest` (MessageType `100`) into `PipeRpcServer.cs`
- [ ] T163 [US16] Implement IDENTITY-aware INSERT scripting with `SET IDENTITY_INSERT` opt-in (FR-076 + edge case)
- [ ] T164 [US16] Implement NULL omission with warning count for Copy as IN Clause (edge case)
- [ ] T165 [P] [US16] Add `tests/AkmlSql.Engine.Tests/Refactoring/ResultGridScriptEngineTests.cs` covering all three modes plus the edge cases (NULLs, IDENTITY, > 15-digit precision, binary columns)
- [ ] T166 [US16] Create `src/AkmlSql.Shell.Shared/Editor/ResultGridHook.cs` adding the three menu items to the SSMS 21+ result grid via `IVsTextViewFilter`
- [ ] T167 [US16] Add a fallback path in `ResultGridHook.cs` for SSMS 20's older `Microsoft.SqlServer.Management.UI.Grid.GridControl` per R-018
- [ ] T168 [US16] Add `cmdidResultGridCopyAsInClause` (`0x016F`), `cmdidResultGridScriptAsInsert` (`0x0170`), `cmdidResultGridOpenInExcel` (`0x0171`) to all 6 host `.vsct` files
- [ ] T169 [US16] Hot-swap to SSMS 22 and walk through the US16 section of `quickstart.md`

**Checkpoint**: US16 complete.

---

## Phase 20: User Story 18 — AI Explain, Index Analysis, fix-on-error, comment-to-SQL (Priority: P3)

**Goal**: Five new AI capabilities — Explain SQL, Query Index Analysis, auto-fix-on-error toast, comment-to-SQL, AI panel history + follow-ups + editor selection icon.

**Independent Test**: Each capability returns an answer (or a clear status message if AI is disabled / rate-limited).

### Engine handlers (US18)

- [ ] T170 [US18] Create `src/AkmlSql.Engine/Ai/ExplainSqlHandler.cs` building an explain prompt with the selected SQL and database context, calling the existing `AiClient`, and returning `ExplainSqlResponse`
- [ ] T171 [US18] Create `src/AkmlSql.Engine/Ai/QueryIndexAnalysisHandler.cs` calling the AI/ML index recommendation service, returning existing-vs-hinted plan summaries plus `CREATE INDEX` script
- [ ] T172 [US18] Create `src/AkmlSql.Engine/Ai/CommentToSqlHandler.cs` parsing `-- generate: <text>` and asking the AI to produce matching SQL
- [ ] T173 [US18] Add `OnExecutionFailedAsync` method to `AiRequestHandler.cs` that builds a fix prompt from the failing batch and the SQL Server error, used by the shell-side fix-on-error toast
- [ ] T174 [US18] Wire all four new request types (`ExplainSqlRequest` 90, `QueryIndexAnalysisRequest` 91, `CommentToSqlRequest` 92) into `PipeRpcServer.cs`
- [ ] T175 [US18] Add `Confidence: Low` flag in `QueryIndexAnalysisHandler` when the table has missing column statistics (edge case)
- [ ] T176 [US18] Add `> 5000 lines truncation warning` in `ExplainSqlHandler` when the input selection exceeds the limit (edge case)
- [ ] T177 [P] [US18] Add `tests/AkmlSql.Engine.Tests/Ai/ExplainSqlHandlerTests.cs`, `QueryIndexAnalysisHandlerTests.cs`, `CommentToSqlHandlerTests.cs` mocking `AiClient` and asserting the prompt-building logic and the response handling

### Shell UI (US18)

- [ ] T178 [US18] Extend `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs` with a History tab listing previous prompts and answers with revert-to-state actions (FR-088)
- [ ] T179 [US18] Add 1–3 follow-up suggestion buttons after every AI answer in `AiChatPanel.cs` (FR-090)
- [ ] T180 [US18] Create `src/AkmlSql.Shell.Shared/Ai/EditorSelectionAiIcon.cs` MEF export rendering an orange AI icon at the right edge of any non-empty selection with Explain / Fix / Optimize hover actions (FR-089)
- [ ] T181 [US18] Add the fix-on-error toast logic to the existing `ExecutionInterceptor.cs` (from T025 — same class hooks query-completed events per R-015), surfacing an `IInfoBarUIElement` on failure
- [ ] T182 [US18] Add comment-to-SQL detection in `src/AkmlSql.Shell.Shared/Editor/Completion/TabWildcardExpansionFilter.cs` (extending the same Tab filter from T053 — only act on `-- generate:` comment lines)
- [ ] T183 [US18] Register `cmdidExplainSql` (`0x0172`), `cmdidQueryIndexAnalysis` (`0x0173`), `cmdidCommentToSql` (`0x0174`) to all 6 host `.vsct` files; add corresponding right-click menu entries for Explain SQL on selection
- [ ] T184 [US18] Add toggles in `AppSettings.Ai` for `EnableExplainSql`, `EnableQueryIndexAnalysis`, `EnableCommentToSql`, `EnableFixOnError`, `ShowEditorIcon`, `ShowFollowupSuggestions` (already added in T006 but now wired to the actual features)
- [ ] T185 [US18] Hot-swap to SSMS 22 and walk through the US18 section of `quickstart.md`

**Checkpoint**: US18 complete.

---

## Phase 21: User Story 19 — Completion suggestion polish (Priority: P3)

**Goal**: 10 polish items — toggle on/off (`Ctrl+Shift+P`), refresh cache (`Ctrl+Shift+D`), custom commit keys, category filter, MS_Description tooltips, parameter highlighting, encrypted decryption, customizable templates, temp-table IntelliSense.

**Independent Test**: Each polish item works as described in the spec.

### Toggle and refresh (US19)

- [ ] T186 [US19] Create `src/AkmlSql.Shell.Shared/Editor/Completion/CompletionToggleService.cs` MEF singleton with a `bool Suppressed` runtime-only flag, exposing `Toggle()` and a `SuppressedChanged` event
- [ ] T187 [US19] Wire `cmdidToggleSuggestions` (`0x0161`) bound to `Ctrl+Shift+P` in all 6 host `.vsct` files; the command toggles `CompletionToggleService.Suppressed` and posts a status-bar message
- [ ] T188 [US19] Modify `src/AkmlSql.Shell.Shared/Editor/Completion/CompletionController.cs` to consult `CompletionToggleService.Suppressed` before showing the popup
- [ ] T189 [US19] Wire `cmdidRefreshSchemaCache` (`0x0162`) bound to `Ctrl+Shift+D` in all 6 host `.vsct` files; the command dispatches `RefreshSchemaCacheRequest` (MessageType `102`) to the engine
- [ ] T190 [US19] Add `ForceRefreshAsync(sessionId, ct)` to `src/AkmlSql.Engine/Schema/SchemaCacheManager.cs` and wire `RefreshSchemaCacheRequest` into `PipeRpcServer.cs`; the method coalesces concurrent requests for the same session (edge case)

### Commit keys + category filter (US19)

- [ ] T191 [US19] Modify `src/AkmlSql.Shell.Shared/Editor/Completion/CompletionController.cs` to read `AppSettings.CompletionPolish.CommitKeys` and commit the highlighted suggestion on any of the configured keys (FR-094)
- [ ] T192 [US19] Add `Ctrl+Up` / `Ctrl+Down` handling in `AkmlCompletionPopup.cs` cycling the category badge through Tables → Views → Columns → Functions → Procedures → Snippets → All (FR-095)

### Tooltips and parameter highlighting (US19)

- [ ] T193 [US19] Extend `src/AkmlSql.Engine/Schema/SchemaMetadataService.cs` to read `MS_Description` extended properties via `sys.fn_listextendedproperty`
- [ ] T194 [US19] Add `MS_Description` rendering plus clickable identifier links to the existing tooltip rendering in `src/AkmlSql.Shell.Shared/Editor/QuickInfo/AkmlQuickInfoSource.cs`
- [ ] T195 [US19] Add bold-rendering of the next-expected parameter in `AkmlQuickInfoSource.cs` parameter signature popup (FR-097)

### Encrypted decryption (US19)

- [ ] T196 [US19] Create `src/AkmlSql.Engine/Schema/EncryptedObjectDecryptor.cs` implementing the documented XOR + RC4 decryption algorithm against the DAC connection (R-017)
- [ ] T197 [US19] Wire `EncryptedObjectDecryptionRequest` (MessageType `101`) into `PipeRpcServer.cs` dispatching to `EncryptedObjectDecryptor`
- [ ] T198 [P] [US19] Add `tests/AkmlSql.Engine.Tests/Schema/EncryptedObjectDecryptorTests.cs` against a stub DAC connection returning known-encrypted bytes; assert the decrypted output matches expected plaintext

### Temp-table IntelliSense and templates (US19)

- [ ] T199 [US19] Create `src/AkmlSql.Engine/Completion/Providers/TempTableProvider.cs` registered in `CompletionEngine`, parsing `CREATE TABLE #x ...` and `SELECT ... INTO #x ...` from the current script's token stream and contributing column completions when the cursor references the temp table (R-016)
- [ ] T200 [US19] Add ALTER and INSERT statement template configuration to `AkmlSql.Engine/Completion/Templates/StatementTemplateRenderer.cs` (NEW file), reading `AppSettings.CompletionPolish.AlterTableTemplate` / `InsertIntoTemplate`
- [ ] T201 [US19] Hot-swap to SSMS 22 and walk through the US19 section of `quickstart.md` against a database with an encrypted procedure and a temp-table-using script

**Checkpoint**: US19 complete.

---

## Phase 22: User Story 20 — Execution shortcuts and Browse Open Tabs (Priority: P3)

**Goal**: Two new execute chords (`Alt+Shift+F5` Execute Current Batch, `Ctrl+Shift+F5` Execute To Cursor) plus `Ctrl+Q` Browse Open Tabs and F1 contextual help.

**Independent Test**: Each chord performs its action; both new execute chords trigger the US1 safety dialog when applicable.

- [ ] T202 [US20] Add `cmdidExecuteCurrentBatch` (`0x0150`) and `cmdidExecuteToCursor` (`0x0151`) to all 6 host `.vsct` files with their chord bindings per `contracts/command-bindings.md`
- [ ] T203 [US20] Create `src/AkmlSql.Shell.Shared/Editor/Execution/ExecuteCurrentBatchCommand.cs` parsing the active text view to find the surrounding `GO` markers, then dispatching the resulting batch text to SSMS's standard execute path
- [ ] T204 [US20] Create `src/AkmlSql.Shell.Shared/Editor/Execution/ExecuteToCursorCommand.cs` extracting the text from start-of-batch up to the line above the cursor and dispatching to the same execute path
- [ ] T205 [US20] Verify the `ExecutionInterceptor` from T025 also intercepts the two new chord commands so US1's safety dialog fires (FR-103)
- [ ] T206 [US20] Add `cmdidBrowseOpenTabs` (`0x0163`) bound to `Ctrl+Q` in all 6 host `.vsct` files; gated to SSMS only by default (VS uses `Ctrl+Q` for Quick Launch)
- [ ] T207 [US20] Create `src/AkmlSql.Shell.Shared/Dialogs/BrowseOpenTabsDialog.cs` and `.xaml` enumerating `EnvDTE.DTE.Documents`, fuzzy-ranking via `FuzzyMatcher`, with Enter to activate the selected entry (R-020)
- [ ] T208 [US20] Add `cmdidF1Help` (`0x0175`) to all 6 host `.vsct` files; the command reads the focused element's `HelpContextValues` and opens the matching URL via `System.Diagnostics.Process.Start` (R-019)
- [ ] T209 [P] [US20] Add `tests/AkmlSql.Engine.Tests/Refactoring/ExecuteCurrentBatchTests.cs` covering: cursor in middle batch, cursor in first batch (no leading `GO`), cursor in last batch (no trailing `GO`), cursor at empty line above first batch (Execute To Cursor returns empty span)
- [ ] T210 [US20] Hot-swap to SSMS 22 and walk through the US20 section of `quickstart.md`

**Checkpoint**: US20 complete. **All 20 user stories shipped.**

---

## Phase 23: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, regression sweeps, full quickstart walk-through across all 6 hosts.

- [ ] T211 [P] Update `doc/progress.md` with the spec 014 milestone log: every user story completed, every commit, every regression caught
- [ ] T212 [P] Update `CLAUDE.md` "Engine Components" table to add the new handlers (`SmartRenameEngine`, `SummarizeScriptEngine`, `FindUnusedEngine`, `AnalysisFixDispatcher`, `ResultGridScriptEngine`, `EncryptedObjectDecryptor`, `ExplainSqlHandler`, `QueryIndexAnalysisHandler`, `CommentToSqlHandler`) and the new completion provider (`TempTableProvider`)
- [ ] T213 [P] Update `CLAUDE.md` "Code Conventions" with the new IPC dispatch pattern decisions from research.md
- [ ] T214 Run the full Engine test suite `dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj` and confirm pass count is `>= 867 + new tests added in spec 014`
- [ ] T215 Run the full Core test suite `dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj` and confirm pass count is `>= 459 + new tests added in spec 014`
- [ ] T216 Build all 6 shell extensions individually (Ssms20, Ssms21, Ssms22, VS2019, VS2022, VS2026) per CLAUDE.md "Build Commands" — each with `MSBuild -t:Restore` then `-t:Build` — and confirm 0 errors
- [ ] T217 Publish the Engine `dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64` and confirm the single-file output is `< 60 MB`
- [ ] T218 Run the **complete** `quickstart.md` end-to-end against a freshly-installed SSMS 22 with two SQL Server connections, ticking every box across all 20 user stories
- [ ] T219 Execute a smoke test in SSMS 20 (the harder host — VS 2017 IsolatedShell, x86, Schema 2010) running US1, US2, US3, US7, US19 to confirm cross-host parity per gate G1
- [ ] T220 Build the installer `"/c/Program Files/Inno Setup 7/ISCC.exe" src/AkmlSql.Installer/AkmlSqlSetup.iss` and confirm `Output/AKMLSQLSetup.exe` is created without errors

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: T001 → T002, T003, T004, T005 (T001 must run first to confirm baseline)
- **Foundational (Phase 2)**: depends on Setup completion. T006 → T007, T008. T009 → T010..T017 (parallel within DTO file group). T018 depends on T010..T017. T019 depends on T009 + T010..T017. T020 has no dependencies inside Phase 2.
- **Phase 3 (US1, P1)**: depends on Foundational. **MVP — STOP HERE for first deployable.**
- **Phases 4–11 (P2 stories)**: each depends on Foundational only. They are independent and can run in parallel.
- **Phases 12–22 (P3 stories)**: each depends on Foundational only. They are independent and can run in parallel.
- **Phase 23 (Polish)**: depends on every desired user story being complete.

### User Story Dependencies (cross-story integration)

- **US1**: independent.
- **US2**: independent.
- **US3**: independent.
- **US4**: independent.
- **US5**: independent. *Soft dependency*: US1's safety dialog renders the environment color from US5 if both are shipped, but US1 functions correctly without US5.
- **US6**: depends on the existing `AnalysisEngine`. Independent of other stories.
- **US7**: independent.
- **US8**: extends `AkmlCompletionPopup` (US2 also extends it — these two share the file `AkmlCompletionPopup.cs`, so US2 and US8 cannot run truly in parallel; US2 should land first).
- **US9**: independent.
- **US10**: depends on the existing `AiChatPanelService`.
- **US11**: independent (regression-test only).
- **US12**: depends on every other story being defined in `AppSettings` (T006 in Foundational satisfies this).
- **US13**: independent.
- **US14**: depends on T080 (`GetObjectAsAlterAsync` from US13). If US14 ships before US13, T080 must move into Foundational instead.
- **US15**: depends on `SchemaMetadataService` and `RefactoringEngine`. Independent of other US.
- **US16**: independent.
- **US17**: depends on the existing `AnalysisEngine` and `RefactoringEngine`. Independent of other US.
- **US18**: depends on the existing `AiChatPanelService`. T181 (fix-on-error toast) extends `ExecutionInterceptor.cs` from US1 — US1 must land first.
- **US19**: T188 modifies `CompletionController.cs`; T191 also modifies it; T192 modifies `AkmlCompletionPopup.cs` (which US2 and US8 also modify). US19 should land **after** US2 and US8 to avoid serial-merge conflicts on those files.
- **US20**: T205 references `ExecutionInterceptor.cs` from US1 — US1 must land first.

### Within Each User Story

- Engine handlers (and their tests) before shell-side wiring that calls them.
- New IPC DTOs before handlers that use them.
- VSCT chord additions can run parallel to handler implementation but must land before manual quickstart verification.
- Each story ends with a hot-swap + quickstart walk-through (the last task in every phase).

### Parallel Opportunities

- All Phase 1 setup tasks marked [P] can run in parallel.
- Phase 2 DTO creation tasks T010..T017 are all [P] (different files, no dependencies).
- Once Foundational completes, **all 20 user-story phases can run in parallel by different developers**, with the cross-story file conflicts noted above (US2/US8/US19 share `AkmlCompletionPopup.cs`; US18/US20 share `ExecutionInterceptor.cs`).
- Within each user story, [P]-marked tasks (typically tests) can run alongside the implementation tasks.

---

## Parallel Example: Foundational Phase

```bash
# After T001 (baseline), T006, and T009 land, the rest of Phase 2 can fan out:
Task: "T010 [P] Create SafetyFinding DTO in src/AkmlSql.Core/Ipc/Messages/SafetyFinding.cs"
Task: "T011 [P] Create AI request/response DTOs in src/AkmlSql.Core/Ipc/Messages/"
Task: "T012 [P] Create FindInvalidObjects DTOs"
Task: "T013 [P] Create SmartRename DTOs"
Task: "T014 [P] Create script-navigation DTOs"
Task: "T015 [P] Create analysis-fix DTOs"
Task: "T016 [P] Create result-grid DTOs"
Task: "T017 [P] Create encrypted/refresh DTOs"
```

## Parallel Example: Phase 3 (US1)

```bash
# Engine and tests can fan out after T021 (extending SafetyCheckHandler) lands:
Task: "T024 [P] [US1] SafetyCheckHandlerTests"
Task: "T030 [P] [US1] Add Execution Warnings page to SettingsWindow"
Task: "T031 [P] [US1] Add Execution Warnings page to search index"
Task: "T032 [P] [US1] SafetyCheckPatternTests with 30 statements"
Task: "T033 [P] [US1] ExecutionWarningsSettingsTests"
```

## Parallel Example: P3 stories (after Foundational)

```bash
# 11 P3 stories can run on 11 parallel branches if staffed:
Branch A: US7  (Ctrl+B chords)         — Phase 12
Branch B: US8  (Object Definition Box) — Phase 13   [serialise after US2]
Branch C: US9  (Format markers)        — Phase 14
Branch D: US10 (AI shortcuts)          — Phase 15
Branch E: US11 (Dual-instance test)    — Phase 16
Branch F: US12 (Settings audit)        — Phase 17
Branch G: US15 (Smart Rename)          — Phase 18
Branch H: US16 (Result Grid)           — Phase 19
Branch I: US18 (AI features)           — Phase 20   [serialise after US1]
Branch J: US19 (Completion polish)     — Phase 21   [serialise after US2 + US8]
Branch K: US20 (Execute shortcuts)     — Phase 22   [serialise after US1]
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1: Setup (T001..T005)
2. Phase 2: Foundational (T006..T020) — **CRITICAL — blocks all stories**
3. Phase 3: US1 (T021..T035) — Pre-execution safety dialog
4. **STOP and VALIDATE**: walk through the US1 section of `quickstart.md`. If green, ship.

### Incremental Delivery (recommended)

1. Setup + Foundational → foundation ready
2. Add US1 → ship MVP
3. Add P2 stories one at a time in this order: US5 (tab coloring) → US6 (analysis window) → US14 (find invalid objects) → US17 (lightbulbs) → US13 (script navigation) → US2 (column picker) → US3 (wildcard tab) → US4 (command palette)
4. Add P3 stories one at a time, prioritising user value: US7 (chords) → US8 (object def box) → US15 (smart rename) → US19 (completion polish) → US18 (AI features) → US20 (execute shortcuts) → US10 (AI shortcuts) → US16 (result grid) → US9 (format markers) → US11 (regression test) → US12 (settings audit)
5. Phase 23 (Polish) after every story is in

### Parallel Team Strategy

With 4–6 developers:

1. The team completes Phases 1 + 2 together
2. Once Foundational is done:
   - Developer A: US1 (P1, MVP) — highest priority
   - Developer B: US5 + US14 (Tab coloring + Find Invalid Objects, both pure shell-side work after engine APIs)
   - Developer C: US2 → US8 → US19 (sequential — they share `AkmlCompletionPopup.cs`)
   - Developer D: US15 (Smart Rename — large self-contained engine work)
   - Developer E: US18 + US10 (AI suite)
   - Developer F: US7 + US13 + US20 (chord-binding heavy)
3. Stories complete and integrate independently
4. Final integration sweep in Phase 23 across all branches

---

## Notes

- Every task includes a concrete file path and is independently completable by an LLM with no further context.
- `[P]` markers identify true parallelism (different files, no upstream dependency).
- Story labels enable per-developer assignment without ambiguity.
- The cross-story file-sharing constraints in the Dependencies section are the only places where parallelism is restricted.
- After every task or logical group, commit and re-run the Engine + Core test baseline to catch regressions early.
- Hot-swap to SSMS 22 (`bash hotswap-ssms22.sh`) for fast iteration. The full installer is only needed at Phase 23 for cross-host smoke testing.
- The 220 tasks above implement spec 014 in full. Tracking progress via `doc/spec-014-progress.md` (created in T005) is recommended.
