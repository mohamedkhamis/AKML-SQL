# Tasks: SQL Prompt Core Feature Parity

**Input**: Design documents from `/specs/010-sql-prompt-core-parity/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ipc-messages.md, quickstart.md

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1-US8)
- Includes exact file paths in descriptions

## Path Conventions

- Shell code: `src/AkmlSql.Shell.Shared/`
- Engine code: `src/AkmlSql.Engine/`
- Core models/IPC: `src/AkmlSql.Core/`
- Tests: `tests/AkmlSql.Core.Tests/`
- All shell projects compile from Shell.Shared via .projitems import

---

## Phase 1: Setup

**Purpose**: No new projects needed. Verify existing infrastructure compiles and tests pass before modifying files.

- [x] T001 Verify clean build of AkmlSql.Ssms22 with MSBuild and run existing tests via `dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj`
- [x] T002 Add new menu command GUIDs and IDs for Snippet Manager and Bookmark commands in src/AkmlSql.Shell.Shared/Commands/AkmlSqlPackage.vsct

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add shared SafetySettings enhancement and audit logging infrastructure needed by multiple user stories.

**CRITICAL**: No user story work can begin until this phase is complete.

- [x] T003 Add `EnvironmentSeverity` dictionary property to `SafetySettings` class in src/AkmlSql.Core/Config/AppSettings.cs with default mapping (PRODUCTION=TypeServerName, STAGING=SimpleConfirm, DEV=Disabled)
- [x] T004 [P] Add `SafetyAuditEntry` structured logging helper method to src/AkmlSql.Shell.Shared/Safety/ExecutionInterceptor.cs that writes Warning-level Serilog entries with server, database, environment, statement type, SQL text (first 500 chars), and outcome fields

**Checkpoint**: Foundation ready -- user story implementation can now begin

---

## Phase 3: User Story 1 - Execution Guard (Priority: P1) MVP

**Goal**: Intercept destructive queries (DELETE/UPDATE without WHERE, DROP, TRUNCATE) before execution and show a confirmation dialog with environment-aware severity.

**Independent Test**: Connect to any server with an environment rule assigned, execute `DELETE FROM dbo.Orders` (no WHERE), verify confirmation dialog appears before execution proceeds. Verify log entry written.

### Implementation for User Story 1

- [x] T005 [US1] Implement IOleCommandTarget command filter in `ExecutionInterceptor.Initialize()` to intercept ECMD_EXECUTE and ECMD_EXECUTESQL before SSMS processes them in src/AkmlSql.Shell.Shared/Safety/ExecutionInterceptor.cs
- [x] T006 [US1] Extract SQL text from active editor buffer (IVsTextView.GetBuffer) and server name from active connection info within the command filter in src/AkmlSql.Shell.Shared/Safety/ExecutionInterceptor.cs
- [x] T007 [US1] Wire the command filter to call existing `OnBeforeExecute(sqlText, serverName)` -- if it returns false, suppress the command (return OLECMDERR_E_CANCELED); if true, pass through to next handler in src/AkmlSql.Shell.Shared/Safety/ExecutionInterceptor.cs
- [x] T008 [US1] Update `SafetyWarningDialog` to read `EnvironmentSeverity` from settings and show TypeToConfirm mode (type server name) for Production, SimpleConfirm (Yes/No) for non-Production, per clarification in src/AkmlSql.Shell.Shared/Safety/SafetyWarningDialog.cs
- [x] T009 [US1] Add audit logging calls after dialog result in `OnBeforeExecute()` -- log Blocked, Confirmed, or Bypassed outcomes using the SafetyAuditEntry helper from T004 in src/AkmlSql.Shell.Shared/Safety/ExecutionInterceptor.cs
- [x] T010 [US1] Add unit tests for SafetyCheckHandler covering DELETE without WHERE, UPDATE without WHERE, DROP TABLE, TRUNCATE TABLE, DELETE with WHERE (should not warn), and safe SELECT in tests/AkmlSql.Core.Tests/Safety/SafetyCheckHandlerTests.cs

**Checkpoint**: Execution Guard fully functional -- destructive queries intercepted with environment-aware dialogs and audit logging

---

## Phase 4: User Story 2 - Snippet Manager Dialog (Priority: P1)

**Goal**: WPF dialog for creating, editing, searching, importing, and exporting code snippets.

**Independent Test**: Open Snippet Manager from AKML SQL menu, create snippet with abbreviation "test", body `SELECT $CURSOR$ FROM $DBNAME$`, save it, type "test" + Tab in editor, verify expansion.

### Implementation for User Story 2

- [x] T011 [P] [US2] Create `SnippetManagerViewModel.cs` with INotifyPropertyChanged, snippet list binding, search/filter, CRUD operations via IPC (SnippetList/SnippetSave/SnippetDelete messages) in src/AkmlSql.Shell.Shared/Snippets/SnippetManagerViewModel.cs
- [x] T012 [P] [US2] Implement `SnippetRequestHandler.HandleImport()` to parse .akmlsnippet JSON files and SQL Prompt XML format, save imported snippets to personal folder in src/AkmlSql.Engine/Snippets/SnippetRequestHandler.cs
- [x] T013 [US2] Create `SnippetManagerDialog.cs` as WPF DialogWindow with split-pane layout (left: SearchBox + TreeView grouped by source/category; right: editor panel with shortcode/name/description/tags/body fields; bottom: New/Duplicate/Delete/Import/Export/Close buttons) using ProfileEditorDialog pattern and ThemeManager colors in src/AkmlSql.Shell.Shared/Snippets/SnippetManagerDialog.cs
- [x] T014 [US2] Create `SnippetManagerCommand.cs` to register a VS menu command that opens the SnippetManagerDialog, add CommandPlacement in .vsct under AKML SQL menu in src/AkmlSql.Shell.Shared/Snippets/SnippetManagerCommand.cs
- [x] T015 [US2] Add unit tests for SnippetRequestHandler.HandleImport() covering .akmlsnippet JSON import, duplicate shortcode handling, and invalid file rejection in tests/AkmlSql.Core.Tests/Snippets/SnippetImportTests.cs

**Checkpoint**: Snippet Manager fully functional -- users can create, edit, search, import, and export snippets via the dialog

---

## Phase 5: User Story 3 - Settings UI Completeness (Priority: P2)

**Goal**: All 50+ settings in AppSettings configurable through a well-organized WPF Settings dialog with category tree navigation.

**Independent Test**: Open Settings dialog, navigate to each category page, change a setting, click OK, verify change persists in config.json and affects feature behavior.

### Implementation for User Story 3

- [x] T016 [US3] Refactor `SettingsWindow.cs` to use a category TreeView on the left panel (repurpose OptionCategoryTreeBuilder pattern) with 15 category nodes matching AppSettings sections in src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs
- [x] T017 [US3] Implement IntelliSense settings page (12 settings: enabled, auto-trigger, delay, fuzzy match, show types/nullability/PK-FK, auto-alias, join assist, keyword case, disable native) in src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs
- [x] T018 [P] [US3] Implement Cache settings page (6 settings: auto-refresh, interval, DDL detection, max databases, lazy load, persist) in src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs
- [x] T019 [P] [US3] Implement Formatter settings page with active profile dropdown, real-time SQL preview panel, and "Edit Style" button linking to ProfileEditorDialog in src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs
- [x] T020 [P] [US3] Implement Code Analysis settings page with rule list showing ID, description, severity dropdown (Error/Warning/Info/Ignore), and per-rule toggle in src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs
- [x] T021 [P] [US3] Implement Tabs & History settings page with coloring toggle, environment rules editor (add/edit/remove with color picker), history max entries, and cleanup interval in src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs
- [x] T022 [P] [US3] Implement Safety settings page with execution guard toggles (DELETE, UPDATE, DROP, TRUNCATE), environment severity map editor, and transaction reminder interval in src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs
- [x] T023 [P] [US3] Implement remaining settings pages (Snippets, Refactoring, Grid, Editor Productivity, Navigation, Command Palette) with appropriate controls in src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs
- [x] T024 [US3] Add Reset This Page, Reset All, Export All Settings (JSON), and Import Settings buttons to SettingsWindow footer in src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs

**Checkpoint**: All AppSettings sections configurable via UI -- no manual JSON editing required

---

## Phase 6: User Story 4 - Safe Rename Refactoring (Priority: P2)

**Goal**: Rename tables, columns, procedures across database references with preview and script generation (no direct DB execution).

**Independent Test**: Create a table with a column referenced by a view and a stored procedure, invoke Safe Rename on the column, verify preview shows both dependent objects, click "Generate Script" to get a complete ALTER script in a new tab.

### Implementation for User Story 4

- [x] T025 [P] [US4] Create `RenameScriptGenerator.cs` that converts `RefactorChangeInfo[]` array into a complete SQL script with ALTER TABLE/ALTER PROCEDURE statements, comments explaining each change, and a transaction wrapper in src/AkmlSql.Shell.Shared/Refactoring/RenameScriptGenerator.cs
- [x] T026 [P] [US4] Complete `RefactoringPreviewDialog.cs` stub with left TreeView (file-level nodes + per-reference checkboxes), right RichTextBox diff view (- old / + new color-coded), and bottom buttons ("Generate Script" + "Cancel") using ThemeManager colors in src/AkmlSql.Shell.Shared/Refactoring/RefactoringPreviewDialog.cs
- [x] T027 [US4] Complete `SafeRenameCommand.cs` stub: show input dialog for new name, send RefactorPreviewRequest (type 30, OperationType=SafeRename) to engine, open RefactoringPreviewDialog with results, on "Generate Script" call RenameScriptGenerator and open output in new editor tab via DTE in src/AkmlSql.Shell.Shared/Refactoring/SafeRenameCommand.cs
- [x] T028 [US4] Add unit tests for RenameScriptGenerator covering column rename with FK dependency, procedure rename, and table rename scenarios in tests/AkmlSql.Core.Tests/Refactoring/SafeRenameOperationTests.cs

**Checkpoint**: Safe Rename generates reviewed, commented ALTER scripts in new editor tabs

---

## Phase 7: User Story 5 - Actions List / Lightbulb Menu (Priority: P3)

**Goal**: Unified lightbulb quick-actions popup showing both code analysis fixes and refactoring actions contextually.

**Independent Test**: Place cursor on `SELECT *`, verify lightbulb appears, click it, see "Expand Wildcards" action. Select code, verify "Surround with BEGIN/END" appears.

### Implementation for User Story 5

- [x] T029 [P] [US5] Create `RefactoringAction.cs` implementing ISuggestedAction for: Qualify Object Names, Expand Wildcards, Surround with BEGIN/END, Surround with TRY/CATCH, Comment/Uncomment selection, Create Snippet from Selection -- each dispatches to existing FormatAction IPC, lightweight operations, or opens SnippetManagerDialog with pre-filled body in src/AkmlSql.Shell.Shared/Analysis/RefactoringAction.cs
- [x] T030 [US5] Modify `LightbulbProvider.cs` to yield RefactoringAction instances alongside existing FixAction/SuppressLineFixAction based on cursor context: SELECT * -> Expand Wildcards, unqualified table -> Qualify Names, text selected -> Surround options + Create Snippet, always -> Comment/Format. Verify lightbulb glyph icon appears in editor margin when actions are available (FR-023) in src/AkmlSql.Shell.Shared/Analysis/LightbulbProvider.cs

**Checkpoint**: Lightbulb popup shows context-appropriate analysis fixes + refactoring actions

---

## Phase 8: User Story 6 - Enhanced Results Grid (Priority: P3)

**Goal**: Column sorting on header click, column filtering via right-click menu, and aggregate statistics on cell selection.

**Independent Test**: Run any SELECT query, click column header to sort, right-click to filter, select numeric cells and verify Sum/Avg/Count/Min/Max in status bar.

### Implementation for User Story 6

- [x] T031 [P] [US6] Create `GridSortHandler.cs` that attaches to DataGridView.ColumnHeaderMouseClick, implements 3-click sort cycle (Ascending -> Descending -> None) via DataView.Sort on the backing DataTable, and shows sort direction indicator in src/AkmlSql.Shell.Shared/Productivity/Grid/GridSortHandler.cs
- [x] T032 [P] [US6] Create `GridFilterPopup.cs` as a small WinForms popup on column header right-click with text input for contains/equals filter, "Clear Filter" button, applying filter via DataView.RowFilter, with reset on new query execution in src/AkmlSql.Shell.Shared/Productivity/Grid/GridFilterPopup.cs
- [x] T033 [US6] Modify `GridFeatureInitializer.cs` to attach GridSortHandler and GridFilterPopup to discovered DataGridView instances alongside existing features (aggregates via existing GridAggregatesProvider -- already implemented, verify wiring is intact; copy-as, null highlight) in src/AkmlSql.Shell.Shared/Productivity/Grid/GridFeatureInitializer.cs

**Checkpoint**: Results grid supports sorting, filtering, and aggregate statistics

---

## Phase 9: User Story 7 - Object Definition Box (Priority: P3)

**Goal**: Secondary popup alongside completion list showing Summary (columns/types/keys/row count) and Script (CREATE DDL) tabs.

**Independent Test**: Trigger autocomplete after FROM, highlight a table, verify definition box appears to the right with Summary and Script tabs.

### Implementation for User Story 7

- [x] T034 [P] [US7] Create `ObjectDefinitionPanel.cs` as a WPF Border-based popup with two TabItems (Summary: DataGrid of column/type/nullable/key + row count; Script: TextBlock with syntax-highlighted CREATE DDL), ~300px wide, dark-themed matching completion popup in src/AkmlSql.Shell.Shared/Editor/Completion/ObjectDefinitionPanel.cs
- [x] T035 [US7] Modify `AkmlCompletionPopup.cs` to add ObjectDefinitionPanel positioned to the right of the popup, send QuickInfoRequest to engine on ListBox selection change (debounced 300ms), populate panel with response, dismiss panel when popup dismisses in src/AkmlSql.Shell.Shared/Editor/Completion/AkmlCompletionPopup.cs
- [x] T036 [US7] Modify `CompletionController.cs` to manage QuickInfo IPC requests: debounce 300ms after selection change, cancel pending request on new selection, handle async response to update ObjectDefinitionPanel in src/AkmlSql.Shell.Shared/Editor/Completion/CompletionController.cs

**Checkpoint**: Object definition box appears alongside completion popup with accurate schema info

---

## Phase 10: User Story 8 - Navigation Polish: Bookmarks & Document Outline (Priority: P4)

**Goal**: Line bookmarks with toggle/navigate shortcuts and a Document Outline tool window showing SQL file structure.

**Independent Test**: Open a long SQL file, set bookmarks on 3 lines with Ctrl+K,Ctrl+K, navigate between them with Ctrl+K,Ctrl+N/P. Open Document Outline, verify tree shows procedures/functions/CTEs.

### Implementation for User Story 8

- [x] T037 [P] [US8] Create `BookmarkManager.cs` as a static class with Dictionary<string, SortedSet<int>> (TextViewId -> line numbers), providing Toggle, GetAll, NextBookmark, PreviousBookmark, Clear methods, and cleanup on text view close in src/AkmlSql.Shell.Shared/Navigation/BookmarkManager.cs
- [x] T038 [P] [US8] Create `BookmarkGlyphFactory.cs` implementing IGlyphFactory (MEF export) that renders a blue circle icon in the editor margin for bookmarked lines, using IClassifierAggregatorService for line classification in src/AkmlSql.Shell.Shared/Navigation/BookmarkGlyphFactory.cs
- [x] T039 [P] [US8] Create `BookmarkCommands.cs` with three VS commands: Toggle Bookmark (Ctrl+K,Ctrl+K), Next Bookmark (Ctrl+K,Ctrl+N), Previous Bookmark (Ctrl+K,Ctrl+P), each calling BookmarkManager methods and navigating the editor caret in src/AkmlSql.Shell.Shared/Navigation/BookmarkCommands.cs
- [x] T040 [P] [US8] Create `DocumentOutlineHandler.cs` in Engine that parses SQL with TSql170Parser, walks AST to build OutlineNodeDto[] tree (CREATE PROCEDURE/FUNCTION/VIEW nodes, CTE nodes, temp table nodes, GO batch boundaries), returns sorted by offset in src/AkmlSql.Engine/Navigation/DocumentOutlineHandler.cs
- [x] T041 [US8] Complete `DocumentOutlineCommand.cs` stub to open DocumentOutlineToolWindow with TreeView bound to OutlineNodeDto[], click-to-navigate, and refresh on document change (debounced 500ms) in src/AkmlSql.Shell.Shared/Commands/DocumentOutlineCommand.cs
- [x] T042 [US8] Add unit tests for DocumentOutlineHandler covering procedure/function/view detection, CTE extraction, nested batch handling, and empty document in tests/AkmlSql.Core.Tests/Navigation/DocumentOutlineTests.cs

**Checkpoint**: Bookmarks and Document Outline functional -- power users can navigate large SQL files efficiently

---

## Phase 11: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories

- [x] T043 [P] Run full test suite and fix any regressions introduced across all phases via `dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj`
- [ ] T044 [P] Verify all new menu commands appear correctly in AKML SQL menu across SSMS 22 and VS 2022 by building and deploying the extension
- [ ] T045 Verify Settings UI correctly loads and saves all 50+ settings by opening Settings, changing one setting per page, clicking OK, and re-opening to confirm persistence
- [ ] T046 Run quickstart.md validation: build Engine + Shell + run tests per quickstart instructions

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies -- start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 -- adds shared SafetySettings + audit logging
- **US1 Execution Guard (Phase 3)**: Depends on Phase 2 (SafetySettings enhancement)
- **US2 Snippet Manager (Phase 4)**: Depends on Phase 1 only (no dependency on Phase 2)
- **US3 Settings UI (Phase 5)**: Depends on Phase 3 (Safety settings page needs guard config from US1)
- **US4 Safe Rename (Phase 6)**: Depends on Phase 1 only (independent)
- **US5 Actions List (Phase 7)**: Depends on Phase 1 only (independent)
- **US6 Grid (Phase 8)**: Depends on Phase 1 only (independent)
- **US7 Definition Box (Phase 9)**: Depends on Phase 1 only (independent)
- **US8 Navigation (Phase 10)**: Depends on Phase 1 only (independent)
- **Polish (Phase 11)**: Depends on all desired user stories being complete

### User Story Dependencies

- **US1 (Execution Guard)**: Requires Phase 2 foundational changes
- **US2 (Snippet Manager)**: Independent -- can start after Phase 1
- **US3 (Settings UI)**: Benefits from US1 (Safety page content) but can stub it
- **US4 (Safe Rename)**: Fully independent
- **US5 (Actions List)**: Fully independent
- **US6 (Grid)**: Fully independent
- **US7 (Definition Box)**: Fully independent
- **US8 (Navigation)**: Fully independent

### Within Each User Story

- Tasks marked [P] within the same phase can run in parallel
- Non-[P] tasks depend on preceding [P] tasks in the same phase
- Each story is independently testable at its checkpoint

### Parallel Opportunities

- **After Phase 2**: US1 + US2 can run in parallel (different file sets)
- **After Phase 1**: US4, US5, US6, US7, US8 can all start immediately (independent file sets)
- **Within each phase**: All [P]-marked tasks can run in parallel

---

## Parallel Example: Phase 3 + Phase 4 (US1 + US2)

```
# After Phase 2 completes, launch US1 and US2 in parallel:

Agent A (Execution Guard):
  T005: Wire IOleCommandTarget command filter in ExecutionInterceptor.cs
  T006: Extract SQL text and server name from active editor
  T007: Wire filter to call OnBeforeExecute()
  T008: Update SafetyWarningDialog for environment-aware severity
  T009: Add audit logging
  T010: Add SafetyCheckHandler unit tests

Agent B (Snippet Manager):
  T011: Create SnippetManagerViewModel.cs
  T012: Implement SnippetRequestHandler.HandleImport()
  T013: Create SnippetManagerDialog.cs
  T014: Create SnippetManagerCommand.cs
  T015: Add import unit tests
```

## Parallel Example: Phase 7 + Phase 8 + Phase 9 (US5 + US6 + US7)

```
# All three are fully independent, different file sets:

Agent A (Actions List):
  T029: Create RefactoringAction.cs
  T030: Modify LightbulbProvider.cs

Agent B (Grid):
  T031: Create GridSortHandler.cs
  T032: Create GridFilterPopup.cs
  T033: Modify GridFeatureInitializer.cs

Agent C (Definition Box):
  T034: Create ObjectDefinitionPanel.cs
  T035: Modify AkmlCompletionPopup.cs
  T036: Modify CompletionController.cs
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001-T002)
2. Complete Phase 2: Foundational (T003-T004)
3. Complete Phase 3: User Story 1 -- Execution Guard (T005-T010)
4. **STOP and VALIDATE**: Test execution guard with DELETE/DROP/TRUNCATE on Production-colored server
5. Deploy if ready -- users immediately protected from accidental destructive queries

### Incremental Delivery

1. Setup + Foundational -> Foundation ready
2. Add US1 (Execution Guard) -> Deploy (MVP -- safety net for DBAs)
3. Add US2 (Snippet Manager) -> Deploy (productivity unlocked)
4. Add US3 (Settings UI) -> Deploy (all settings accessible)
5. Add US4 (Safe Rename) -> Deploy (refactoring capability)
6. Add US5+US6+US7 in parallel -> Deploy (polish and UX improvements)
7. Add US8 (Navigation) -> Deploy (power user features)

### Parallel Team Strategy

With 3 developers after Phase 2:
- Dev A: US1 (Execution Guard) -> US3 (Settings UI)
- Dev B: US2 (Snippet Manager) -> US4 (Safe Rename)
- Dev C: US5 (Actions List) + US6 (Grid) + US7 (Definition Box) -> US8 (Navigation)

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks in same phase
- [Story] label maps task to specific user story for traceability
- Each user story is independently completable and testable at its checkpoint
- All WPF dialogs use programmatic layout (no XAML) for SharedProject compatibility
- All new files in Shell.Shared are automatically compiled into all 6 shell targets
- Build each shell project individually with MSBuild after changes (never dotnet build)
