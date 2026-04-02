# Tasks: Custom WPF Completion Popup

**Input**: Design documents from `/specs/009-ai-sql-assistance/` and `/docs/superpowers/specs/2026-03-29-custom-completion-popup-design.md`
**Prerequisites**: plan.md (required), design spec (required)

**Tests**: Manual testing in SSMS 22 (MEF adornments require VS host process).

**Organization**: Tasks organized by feature area. All tasks target the shared project at `src/AkmlSql.Shell.Shared/`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story (US1=Popup UI, US2=Keystroke Control, US3=Schema Status, US4=Wiring+Test)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: Data model and cleanup preparation

- [ ] T001 [P] [US1] Create CompletionItemModel with SQL Prompt color mapping and fuzzy filter in src/AkmlSql.Shell.Shared/Editor/Completion/CompletionItemModel.cs
- [ ] T002 [P] Update CompletionRpcHelper to return CompletionItemModel[] instead of VS Completion objects in src/AkmlSql.Shell.Shared/Editor/CompletionRpcHelper.cs

---

## Phase 2: Foundational (Popup UI)

**Purpose**: The popup WPF control — all other phases depend on this

**Goal**: Render a SQL Prompt-style popup with colored badges, list navigation, footer, and loading state

**Independent Test**: Instantiate popup, call SetItems() with test data, verify items render with correct colors

- [ ] T003 [US1] Create AkmlCompletionPopup code-only WPF control with ListBox, badge icons, footer, dark theme in src/AkmlSql.Shell.Shared/Editor/Completion/AkmlCompletionPopup.cs
- [ ] T004 [US1] Implement client-side fuzzy filtering (SetFilter method) and selection navigation (MoveSelection, GetSelectedItem) in src/AkmlSql.Shell.Shared/Editor/Completion/AkmlCompletionPopup.cs
- [ ] T005 [US1] Create CompletionPopupAdornment managing popup lifecycle (show/hide/position at caret, flip above if near bottom, reposition on scroll) in src/AkmlSql.Shell.Shared/Editor/Completion/CompletionPopupAdornment.cs

**Checkpoint**: Popup can be shown/hidden/positioned as a WPF adornment with SQL Prompt styling

---

## Phase 3: Keystroke Controller (US2)

**Goal**: Intercept keystrokes, trigger Engine RPC, update popup, handle commit/dismiss

**Independent Test**: Open query in SSMS 22, type `SELECT * FROM `, see tables appear in popup; Tab commits; Esc dismisses

- [ ] T006 [US2] Create CompletionController implementing IOleCommandTarget with debounced Engine RPC (150ms), DocumentChanged sync, and CompletionRequest dispatch in src/AkmlSql.Shell.Shared/Editor/Completion/CompletionController.cs
- [ ] T007 [US2] Implement keyboard handling: letter/dot/@ trigger, Up/Down navigate, Tab/Enter commit, Esc/Space dismiss, Backspace re-filter in src/AkmlSql.Shell.Shared/Editor/Completion/CompletionController.cs
- [ ] T008 [US2] Implement native IntelliSense suppression via ICompletionBroker.DismissAllSessions when AKML popup is active in src/AkmlSql.Shell.Shared/Editor/Completion/CompletionController.cs
- [ ] T009 [US2] Implement dot-commit behavior: commit current item + trigger new completion for schema.table.column navigation in src/AkmlSql.Shell.Shared/Editor/Completion/CompletionController.cs

**Checkpoint**: Full keystroke lifecycle works — type triggers popup, filter narrows, Tab commits, Esc dismisses

---

## Phase 4: Schema Status Indicator (US3)

**Goal**: Show schema loading progress in bottom-right corner of editor

**Independent Test**: Connect to database in SSMS 22, see spinner during Phase A, then "ready" message

- [ ] T010 [P] [US3] Create SchemaStatusIndicator as bottom-right WPF adornment showing loading/ready/hidden states in src/AkmlSql.Shell.Shared/Editor/Completion/SchemaStatusIndicator.cs
- [ ] T011 [US3] Wire SchemaStatusIndicator to ConnectionWiringHelper to show status during Phase A schema loading in src/AkmlSql.Shell.Shared/Editor/ConnectionWiringHelper.cs

**Checkpoint**: Schema loading spinner appears on connection, shows object count when ready, then hides

---

## Phase 5: MEF Wiring + Cleanup (US4)

**Goal**: Wire everything together, remove old broken completion code, verify end-to-end

**Independent Test**: Full SSMS 22 test — connect, open query, type SQL, see completions with SQL Prompt colors

- [ ] T012 [US4] Create CompletionPopupProvider as MEF IWpfTextViewCreationListener (no [Import]s, ContentType SQL Server Tools+SQL+T-SQL, TextViewRole Document) exporting adornment layer in src/AkmlSql.Shell.Shared/Editor/Completion/CompletionPopupProvider.cs
- [ ] T013 [US4] Update TextViewCreationListener to remove old CompletionCommandHandler wiring in src/AkmlSql.Shell.Shared/Editor/TextViewCreationListener.cs
- [ ] T014 [US4] Add all new Completion/ files to projitems and remove CompletionCommandHandler.cs + CompletionSource.cs entries in src/AkmlSql.Shell.Shared/AkmlSql.Shell.Shared.projitems
- [ ] T015 [US4] Delete old files: src/AkmlSql.Shell.Shared/Editor/CompletionCommandHandler.cs and src/AkmlSql.Shell.Shared/Editor/CompletionSource.cs

**Checkpoint**: Extension builds, loads in SSMS 22, popup appears with SQL Prompt style

---

## Phase 6: Build, Deploy, End-to-End Test

**Purpose**: Full validation across scenarios

- [ ] T016 Clean rebuild SSMS 22 shell project and verify 0 errors
- [ ] T017 Deploy to SSMS 22 and clear MEF/private registry caches
- [ ] T018 Test: connect to database, verify schema loading spinner appears
- [ ] T019 Test: type `se` → popup shows SELECT keyword with blue-gray badge
- [ ] T020 Test: type `SELECT * FROM ` + Ctrl+Space → popup shows tables with blue T badge
- [ ] T021 Test: Tab/Enter commits selected item into editor
- [ ] T022 Test: dot navigation — type `dbo.` → popup shows schema objects
- [ ] T023 Test: Esc dismisses popup, no SSMS freeze or crash

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately. T001 and T002 are parallel.
- **Phase 2 (Popup UI)**: Depends on T001 (model). T003→T004→T005 sequential.
- **Phase 3 (Controller)**: Depends on T005 (adornment). T006→T007→T008→T009 sequential.
- **Phase 4 (Spinner)**: Independent — T010 can run in parallel with Phase 2/3.
- **Phase 5 (Wiring)**: Depends on Phases 2, 3, 4 complete. T012→T013→T014→T015 sequential.
- **Phase 6 (Test)**: Depends on Phase 5. T016→T017→T018-T023 (tests are sequential).

### Parallel Opportunities

```
T001 (model) ────────── T003 (popup) ── T004 (filter) ── T005 (adornment) ──┐
T002 (RPC helper) ──────────────────────────────────────────────────────────┤
T010 (spinner) ─────────────────────────────────────────────────────────────┤
                         T006 (controller) ── T007 (keys) ── T008 (suppress) ── T009 (dot) ──┤
                                                                                              ├── T012 (MEF) ── T013-T015 (wire) ── T016-T023 (test)
```

T001, T002, T010 can all start in parallel. T003 starts after T001. T006 starts after T005. T010 is fully independent.

---

## Implementation Strategy

### MVP First (Phases 1-3)

1. Complete Phase 1: Model + RPC helper update
2. Complete Phase 2: Popup UI renders with SQL Prompt style
3. Complete Phase 3: Keystrokes work, completions appear
4. **STOP and VALIDATE**: Test in SSMS 22 — type SQL, see popup
5. If working, proceed to Phases 4-6

### Incremental Delivery

1. Model + Popup UI → Can verify rendering
2. Add Controller → Can verify keystrokes + Engine integration
3. Add Spinner → Can verify schema loading UX
4. Wire + Test → Full SQL Prompt experience

---

## Notes

- [P] tasks = different files, no dependencies
- All new files go in `src/AkmlSql.Shell.Shared/Editor/Completion/`
- Code-only WPF (no XAML) — matches StickyScroll/Minimap pattern
- No `[Import]` on MEF-exported types — use `ServiceProvider.GlobalProvider`
- IPC types behind `[NoInlining]` to prevent JIT assembly loading before AssemblyResolver
- Commit after each phase checkpoint
