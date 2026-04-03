# Tasks: SQL Prompt Parity — Remaining Gaps

**Input**: Design documents from `specs/013-sqlprompt-parity-gaps/`  
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: No project structure changes needed — all changes fit existing architecture. This phase verifies prerequisites.

- [X] T001 Verify current branch is `013-sqlprompt-parity-gaps` and working tree is clean
- [X] T002 Read SQL Prompt reference color palette from `doc/SQL-PROMPT/SQL-Prompt-Features/SQL_Prompt_Features_Core.md` sections 1.2 and 10.5 to confirm all 12 object type colors

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Extract shared utility used by multiple user stories

**No blocking prerequisites** — all user stories modify independent files and can proceed directly.

**Checkpoint**: Setup complete — user story implementation can begin

---

## Phase 3: User Story 1 — Options Dialog Color Accuracy (Priority: P1)

**Goal**: Update Options dialog to use exact SQL Prompt hex color palette in both light and dark themes

**Independent Test**: Open Options dialog in light/dark themes, visually verify colors match spec (#F0F0F0, #2D2D3B, etc.)

### Implementation for User Story 1

- [X] T003 [US1] Update `ThemeBrushSet.Light` static constructor with SQL Prompt light palette (Main #F0F0F0, Panel #FFFFFF, Selected #0078D4 with white text, Border #CCCCCC) in `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs`
- [X] T004 [US1] Update `ThemeBrushSet.Dark` static constructor with SQL Prompt dark palette (Main #2D2D3B, Panel #1E1E2E, Text secondary #8892A8, Border #3A3F4E) in `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs`
- [X] T005 [US1] Verify selected item text is readable (white text on #0078D4 selection) and test theme switching in `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs`

**Checkpoint**: Options dialog matches SQL Prompt color spec in both themes

---

## Phase 4: User Story 2 — Suggestion Popup Icon Color Accuracy (Priority: P1)

**Goal**: Update all 12 IntelliSense icon badge colors to match SQL Prompt One Dark palette with semi-transparent backgrounds

**Independent Test**: Trigger Ctrl+Space in a query, verify Table=yellow(#E5C04B), View=teal(#56B6C2), Column=blue(#61AFEF), Procedure=purple(#C678DD), all with 20% opacity backgrounds

### Implementation for User Story 2

- [X] T006 [P] [US2] Update `GetColor()` switch in `src/AkmlSql.Shell.Shared/Editor/Completion/CompletionItemModel.cs` to SQL Prompt palette: Table=#E5C04B, View=#56B6C2, Column=#61AFEF, Keyword=#ABB2BF, Snippet=#3DD68C, Function=#D19A66, Procedure=#C678DD, Schema=#98C379, Database=#E06C75, Variable=#56B6C2, Alias=#61AFEF, Parameter=#C678DD
- [X] T007 [P] [US2] Modify `CreateItemVisual()` in `src/AkmlSql.Shell.Shared/Editor/Completion/AkmlCompletionPopup.cs` to render badge with semi-transparent background (20% opacity of text color, except Keyword which uses 15% opacity) and colored text instead of white-on-solid

**Checkpoint**: All 12 object type badges display correct SQL Prompt colors

---

## Phase 5: User Story 3 — Unformat SQL Command (Priority: P2)

**Goal**: Expose the existing `UnformatOperation` as a shell command accessible via keyboard shortcut and command palette

**Independent Test**: Select formatted multi-line SQL, invoke Ctrl+B Ctrl+U, verify output is single-line minimal whitespace

### Implementation for User Story 3

- [X] T008 [US3] Add `CmdUnformat = 0x021E` command ID constant in `src/AkmlSql.Shell.Shared/PackageGuids.cs`
- [X] T009 [US3] Create `UnformatCommand.cs` in `src/AkmlSql.Shell.Shared/Formatting/` following `FormatDocumentCommand.cs` pattern — send `FormatActionRequest` with `ActionType = 17 (Unformat)`, support selection-only mode
- [X] T010 [US3] Add Unformat button to AkmlSqlFormatGroup in `src/AkmlSql.Ssms21/AkmlSqlSsms21.vsct` with Ctrl+B,Ctrl+U keybinding
- [X] T011 [P] [US3] Add Unformat button to `src/AkmlSql.Ssms22/AkmlSqlSsms22.vsct` (same VSCT definition as T010)
- [X] T012 [P] [US3] Add Unformat button to `src/AkmlSql.VS2022/AkmlSqlVS2022.vsct` (same VSCT definition as T010)
- [X] T013 [P] [US3] Add Unformat button to remaining VSCT files: `src/AkmlSql.Ssms20/AkmlSqlSsms20.vsct`, `src/AkmlSql.VS2019/AkmlSqlVS2019.vsct`, `src/AkmlSql.VS2026/AkmlSqlVS2026.vsct`
- [X] T014 [US3] Register UnformatCommand in package initialization (same pattern as FormatDocumentCommand registration) in `src/AkmlSql.Shell.Shared/AkmlSqlPackage.cs` or the command registration file

**Checkpoint**: Unformat command works via Ctrl+B,Ctrl+U and Command Palette

---

## Phase 6: User Story 4 — Disable Formatting Region Directives (Priority: P2)

**Goal**: Extend NoformatScanner to recognize `-- AKML formatting off/on` and `-- SQL Prompt formatting off/on` as aliases

**Independent Test**: Wrap SQL block in `-- AKML formatting off/on`, run Format Document, verify block is preserved verbatim

### Implementation for User Story 4

- [X] T015 [US4] Extend regex patterns in `src/AkmlSql.Formatting/Pipeline/NoformatScanner.cs` to match `-- AKML formatting off`, `-- AKML formatting on`, `-- SQL Prompt formatting off`, `-- SQL Prompt formatting on` as aliases for `-- noformat`/`-- endnoformat` (case-insensitive)
- [X] T016 [US4] Add unit tests for all 3 directive syntaxes (noformat, AKML, SQL Prompt) plus mixed usage in `tests/AkmlSql.Formatting.Tests/Pipeline/NoformatScannerTests.cs` (create if not exists, or extend existing)

**Checkpoint**: All three directive syntaxes work identically through the full formatting pipeline

---

## Phase 7: User Story 7 — Rename Closed Queries in History (Priority: P3)

**Goal**: Verify existing rename works and custom names are searchable (must verify before US5 advanced search builds on it)

**Independent Test**: Right-click closed query → Rename → enter name → search for it → verify found

### Implementation for User Story 7

- [X] T017 [US7] Verify existing Rename action (Action=6) in History context menu works end-to-end in `src/AkmlSql.Shell.Shared/History/HistoryToolWindowControl.cs` — if the rename dialog or IPC flow has any issues, fix them
- [X] T018 [US7] Verify `name:` prefix search in `HistorySearchParser.cs` correctly searches the `tab_title` column — if `tab_title` is not included in FTS5 index or WHERE clause, add it to `SearchAsync()` in `src/AkmlSql.Engine/History/HistoryDatabase.cs`

**Checkpoint**: Renamed queries persist and are searchable via `name:` prefix

---

## Phase 8: User Story 5 — SQL History Advanced Search Syntax (Priority: P2)

**Goal**: Add wildcard, boolean, exact phrase, and CamelCase search to History

**Independent Test**: Search `Product*`, `SELECT OR DELETE`, `NOT DROP`, `"create view"`, `PC` — all return correct results

### Implementation for User Story 5

- [X] T019 [US5] Extend `HistorySearchParser.cs` in `src/AkmlSql.Shell.Shared/History/HistorySearchParser.cs` to detect and preserve wildcard tokens (`*`, `?`), boolean operators (`OR`, `NOT`), and exact phrase quotes (`"..."`) — pass them through to the FTS5 query string instead of stripping them
- [X] T020 [US5] Add CamelCase token detection in `HistorySearchParser.cs` — identify short uppercase-only tokens (2-4 chars like `PC`, `GCO`) and flag them as CamelCase post-filter tokens
- [X] T021 [US5] Add `CamelCaseTokens` field to `HistorySearchRequest` in `src/AkmlSql.Core/Ipc/Messages/HistorySearchRequest.cs` (MessagePack Key next available index) to carry CamelCase tokens from shell to engine
- [X] T022 [US5] Update `SearchInternalAsync()` in `src/AkmlSql.Shell.Shared/History/HistoryViewModel.cs` to pass CamelCase tokens from parsed search result into the `HistorySearchRequest.CamelCaseTokens` field before sending via IPC
- [X] T023 [US5] Update `SearchAsync()` in `src/AkmlSql.Engine/History/HistoryDatabase.cs` — remove the literal quote wrapping (`"\"" + sanitized + "\""`) and pass the parsed FTS5 query string directly to the MATCH clause, with proper sanitization of non-FTS special characters
- [X] T024 [US5] Add CamelCase post-filtering in `HistoryDatabase.SearchAsync()` — after FTS5 returns results, apply CamelCase boundary matching (extract logic from `CompletionItemModel.MatchesCamelCase()` into a shared static utility) to filter entries whose SQL text matches the CamelCase pattern

**Checkpoint**: All 5 advanced search types return correct results

---

## Phase 9: User Story 6 — SQL History Search Match Highlighting (Priority: P3)

**Goal**: Highlight matched search terms in code preview with Yellow Ochre background

**Independent Test**: Search for "SELECT", verify all occurrences in preview highlighted with #F9A825 at 30% opacity

### Implementation for User Story 6

- [X] T025 [US6] Update `UpdatePreviewWithHighlighting()` in `src/AkmlSql.Shell.Shared/History/HistoryToolWindowControl.cs` to: (a) parse search text into individual highlight terms (split by OR, strip NOT prefix, extract quoted phrases as whole terms), (b) highlight each term independently, (c) use Yellow Ochre color #F9A825 at 30% opacity for highlight background
- [X] T026 [US6] Update the highlight color definition in `src/AkmlSql.Shell.Shared/Ui/ThemeManager.cs` — set `HistorySearchHighlight` to #F9A825 with 30% opacity (0x4DF9A825) for both light and dark themes

**Checkpoint**: Multi-term highlighting visible in History code preview

---

## Phase 10: User Story 8 — Tab Color Propagation (Priority: P3)

**Goal**: Propagate environment color to SSMS status bar and floating window borders

**Independent Test**: Connect to PROD server, verify red color on tab + status bar + floating window border

### Implementation for User Story 8

- [X] T027 [US8] Implement status bar color injection in `src/AkmlSql.Shell.Shared/Tabs/TabColoringManager.cs` — on `WindowActivated` event, walk WPF visual tree from active document window to find SSMS status bar element, apply environment color as Background brush (wrap in try/catch for SSMS version safety)
- [X] T028 [US8] Implement floating window border coloring in `src/AkmlSql.Shell.Shared/Tabs/TabColoringManager.cs` — detect undocked query windows via `IVsWindowFrame` properties, apply 3px `BorderBrush` matching environment color to the floating window chrome
- [X] T029 [US8] Add focus-change color update in `TabColoringManager.cs` — when switching between tabs with different environment colors, update status bar color within 200ms of focus change

**Checkpoint**: Environment color visible on tab, status bar, and floating window border

---

## Phase 11: User Story 9 — Installer Silent Mode Enhancements (Priority: P3)

**Goal**: Add SSMS detection, AppMutex, logging, and repair/upgrade support to installer

**Independent Test**: Run `/VERYSILENT /log=install.log` and verify log file created, SSMS running detection works, re-run performs in-place upgrade

### Implementation for User Story 9

- [X] T030 [P] [US9] Add `AppMutex=Ssms.exe` and `CloseApplications=yes` to `[Setup]` section in `src/AkmlSql.Installer/AkmlSqlSetup.iss` to detect running SSMS instances and prompt user
- [X] T031 [P] [US9] Document `/LOG` flag support (native Inno Setup feature) in installer help text and README — add `/LOG=filename` example to the `/VERYSILENT` documentation in `src/AkmlSql.Installer/AkmlSqlSetup.iss` comments and `doc/deployment.md`
- [X] T032 [US9] Verify Inno Setup `AppId` and `UsePreviousAppDir=yes` settings ensure in-place upgrade/repair without manual uninstall — test by running installer twice and confirming clean upgrade in `src/AkmlSql.Installer/AkmlSqlSetup.iss`

**Checkpoint**: Installer detects running SSMS, creates log files, and supports in-place upgrade

---

## Phase 12: User Story 10 — SQL Prompt Style Importer (Priority: P3)

**Goal**: Auto-detect SQL Prompt config during installation and offer to import formatting styles

**Independent Test**: Install on machine with SQL Prompt config in `%LocalAppData%\Red Gate\SQL Prompt`, verify import offer appears

### Implementation for User Story 10

- [X] T033 [US10] Add post-install Pascal Script function in `src/AkmlSql.Installer/AkmlSqlSetup.iss` to check if `{localappdata}\Red Gate\SQL Prompt` directory exists, show import checkbox if found
- [X] T034 [US10] Implement installer import action — if user accepts import, copy `.sqlpromptstyle` files to `{app}\ImportedStyles\` staging directory and write a flag file `{userappdata}\AKML SQL\pending-import.json` with paths to imported files
- [X] T035 [US10] Add import processing on first engine startup in `src/AkmlSql.Engine/Server/PipeRpcServer.cs` or startup initialization — check for `pending-import.json`, invoke existing `SqlPromptImporter.ImportFromFile()` for each style, write results to AKML profile directory, delete flag file

**Checkpoint**: SQL Prompt styles detected and imported during installation flow

---

## Phase 13: Polish & Cross-Cutting Concerns

**Purpose**: Final validation and cleanup

- [X] T036 Run all 8 quickstart verification scenarios from `specs/013-sqlprompt-parity-gaps/quickstart.md` including performance checks (Unformat <200ms, search <500ms, color update <200ms)
- [X] T037 Build all 6 shell projects individually with MSBuild to verify VSCT changes compile without cross-contamination
- [X] T038 Run `dotnet test tests/AkmlSql.Core.Tests/` and `dotnet test tests/AkmlSql.Formatting.Tests/` (if exists) to verify no regressions

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (Foundational)**: No blocking work needed
- **Phases 3-4 (US1, US2)**: Independent — can run in parallel with each other and all other phases
- **Phases 5-6 (US3, US4)**: Independent — can run in parallel with each other and all other phases
- **Phase 7 (US7)**: Independent — rename verification, should run before US5
- **Phase 8 (US5)**: Depends on Phase 7 (US7) — rename/name-search must be verified before advanced search builds on it
- **Phase 9 (US6)**: Depends on Phase 8 (US5) — highlighting must parse same tokens as search
- **Phases 10-12 (US8, US9, US10)**: Independent — can run in parallel with each other
- **Phase 13 (Polish)**: Depends on all user stories being complete

### User Story Dependencies

- **US1 (Options Colors)**: Independent — no dependencies on other stories
- **US2 (Icon Colors)**: Independent — no dependencies on other stories
- **US3 (Unformat)**: Independent — no dependencies on other stories
- **US4 (Formatting Directives)**: Independent — no dependencies on other stories
- **US7 (Rename)**: Independent — verification task, should complete before US5
- **US5 (Advanced Search)**: Depends on US7 (rename/name-search verification)
- **US6 (Search Highlighting)**: Depends on US5 (same search token parsing)
- **US8 (Tab Colors)**: Independent — no dependencies on other stories
- **US9 (Installer Silent)**: Independent — no dependencies on other stories
- **US10 (Style Import)**: Independent — no dependencies on other stories

### Parallel Opportunities

- **Maximum parallelism**: US1 + US2 + US3 + US4 + US7 + US8 + US9 can all run simultaneously (7 stories in parallel)
- **Sequential chain**: US7 → US5 → US6 (History search chain)
- **Sequential dependency**: US10 tasks T033→T034→T035 are ordered within the story

---

## Parallel Example: Phase A (UI/Colors)

```bash
# These modify completely different files — safe to run in parallel:
Task T003+T004: Update ThemeBrushSet Light+Dark in SettingsWindow.cs (same file, sequential)
Task T006: Update GetColor() in CompletionItemModel.cs
Task T007: Update CreateItemVisual() in AkmlCompletionPopup.cs
```

## Parallel Example: Phase B (Formatting)

```bash
# VSCT files are per-target — safe to run in parallel:
Task T011: VSCT for SSMS 22
Task T012: VSCT for VS 2022
Task T013: VSCT for remaining targets (SSMS 20, VS 2019, VS 2026)
```

---

## Implementation Strategy

### MVP First (US1 + US2 — Visual Polish)

1. Complete T003-T005 (Options dialog colors)
2. Complete T006-T007 (Icon colors)
3. **STOP and VALIDATE**: Visual audit against SQL Prompt reference screenshots
4. Build and deploy to SSMS 22 for testing

### Incremental Delivery

1. **Phase A** (US1+US2): Visual accuracy — immediate perceived quality improvement
2. **Phase B** (US3+US4): Formatting commands — completes formatting feature set
3. **Phase C** (US5+US6+US7): History — closes last major gap area
4. **Phase D** (US8+US9+US10): Environment/Installer — enterprise polish
5. Each phase delivers independently testable value

---

## Notes

- T003/T004 modify the same file (SettingsWindow.cs) — run sequentially, not in parallel
- VSCT changes (T010-T013) must be built individually per project — never build via solution (VSCT CTO cross-contamination)
- US7 (Rename verification) runs before US5 (Advanced Search) because name: search must work before building advanced search on top of it
- Research found NoformatScanner and UnformatOperation already exist — US3 and US4 are wiring/extension tasks, not greenfield
- FTS5 natively supports wildcards and boolean — US5 is primarily about removing the quote-wrapping in HistoryDatabase.SearchAsync
- T032 (US9) verifies Inno Setup repair/upgrade behavior per spec acceptance scenario 3
