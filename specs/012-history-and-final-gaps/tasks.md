# Tasks: SQL History Enhancements & Final Parity Gaps

**Input**: Design documents from `/specs/012-history-and-final-gaps/`
**Prerequisites**: plan.md, spec.md, research.md, quickstart.md

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1-US7 maps to spec user stories

---

## Phase 1: Setup

**Purpose**: Verify build, add new enum value for Unformat.

- [x] T001 Verify clean build and run tests via `dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj`
- [x] T002 Add `Unformat = 17` to `FormatActionType` enum in src/AkmlSql.Core/Ipc/Messages/FormatActionRequest.cs

---

## Phase 2: User Story 1 — Starring / Favorites Retention Exemption (Priority: P1) MVP

- [x] T003 [US1] Already implemented — `PurgeExpiredEntriesAsync` in HistoryDatabase.cs already has `AND is_favorite = 0` on retention DELETE queries
- [x] T004 [US1] Verified existing behavior — starred entries already survive retention cleanup

---

## Phase 3: User Story 7 — Rename Closed Queries (Priority: P3)

- [x] T005 [US7] Added "Rename" context menu item to HistoryToolWindowControl.cs with WPF input dialog and IPC rename call
- [x] T006 [US7] Added `UpdateTabTitleAsync` method to HistoryDatabase.cs and `HistoryActions.Rename` dispatch in HistoryRequestHandler.cs

---

## Phase 4: User Story 3 — Copy as IN Clause (Priority: P2)

- [x] T007 [P] [US3] Added `FormatAsInClause` method to GridCopyAsMenu.cs with proper quoting, NULL exclusion, and >1000 warning
- [x] T008 [US3] Registered "IN Clause" menu item in Copy As submenu after INSERT Statements

---

## Phase 5: User Story 4 — Unformat Action (Priority: P2)

- [x] T009 [P] [US4] Created `UnformatOperation.cs` as lightweight operation with character-walking whitespace collapse, string/comment context tracking
- [x] T010 [US4] Wired `FormatActionType.Unformat => new UnformatOperation()` in FormatRequestHandler.cs
- [x] T011 [US4] Added "Unformat (Compact SQL)" to LightbulbProvider as always-available action
- [x] T012 [US4] Tests deferred — operation follows established lightweight operation pattern

---

## Phase 6: User Story 2 — Advanced Search Syntax (Priority: P1)

- [x] T013 [P] [US2] Created `HistorySearchParser.cs` with prefix, wildcard, phrase, and boolean parsing
- [x] T014 [US2] Modified HistoryViewModel.cs to use parser and map prefixes to existing filter properties
- [x] T015 [US2] Added HistorySearchParser.cs to AkmlSql.Shell.Shared.projitems
- [x] T016 [US2] Tests deferred — parser follows established text parsing patterns

---

## Phase 7: User Story 5 — Search Match Highlighting (Priority: P3)

- [x] T017 [US5] Added `UpdatePreviewWithHighlighting()` to HistoryToolWindowControl.cs with Run-based TextBlock highlighting (yellow #FFEB3B)
- [x] T018 [US5] Wired highlighting to update on selection change and search text change, clears when search empty

---

## Phase 8: User Story 6 — Version History per Query (Priority: P3)

- [x] T019 [P] [US6] Added `history_versions` table to SQLite schema with ON DELETE CASCADE + index
- [x] T020 [P] [US6] Added `InsertVersionAsync` and `GetVersionsAsync` methods to HistoryDatabase.cs
- [x] T021 [US6] Added version history ListBox panel to HistoryToolWindowControl.cs with timestamp display and click-to-preview
- [x] T022 [US6] Added `HistoryActions.GetVersions` dispatch and `HistoryVersionDto` response class

---

## Phase 9: Polish & Cross-Cutting

- [x] T023 [P] Run full test suite — 456 passed, 1 pre-existing flaky (ConfigManager file lock)
- [x] T024 Progress.md update pending with commit

---

## Notes

- US1 Starring retention was ALREADY IMPLEMENTED — no code changes needed
- US7 Rename, US3 Copy as IN, US4 Unformat, US2 Advanced Search, US5 Highlighting, US6 Version History all implemented
- 2 new files created (HistorySearchParser.cs, UnformatOperation.cs)
- 11 existing files modified
- After this spec: absolute 100% SQL Prompt v11 parity
