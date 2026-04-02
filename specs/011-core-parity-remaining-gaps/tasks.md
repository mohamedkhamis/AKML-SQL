# Tasks: SQL Prompt Core Parity — Remaining Gaps

**Input**: Design documents from `/specs/011-core-parity-remaining-gaps/`
**Prerequisites**: plan.md, spec.md, research.md, quickstart.md
**Note**: US3 (Copy with Headers) removed — research found it's already implemented.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US4-US7)
- US3 skipped (already implemented)

---

## Phase 1: Setup

**Purpose**: Verify build, add new settings and enum values.

- [x] T001 Verify clean build and run tests via `dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj`
- [x] T002 [P] Add `InsertColumnsIncludeTypes` bool (default true) to `FormatterSettings` class in src/AkmlSql.Core/Config/AppSettings.cs
- [x] T003 [P] Add `ExcelLargeNumberAsText` bool (default true) to `GridSettings` class in src/AkmlSql.Core/Config/AppSettings.cs
- [x] T004 [P] Add `GradientColors` bool (default false) to `TabSettings` class in src/AkmlSql.Core/Config/AppSettings.cs
- [x] T005 [P] Add `ConvertSpExecutesql = 16` to `FormatActionType` enum in src/AkmlSql.Core/Ipc/Messages/FormatActionRequest.cs

---

## Phase 2: User Story 1 — INSERT Metadata Comments (Priority: P1) MVP

**Goal**: Expand Insert Columns generates column list with inline type/nullability/default comments.

**Independent Test**: Type `INSERT INTO dbo.Products`, trigger expansion, verify each column has `-- type, nullable, default` comment.

### Implementation for User Story 1

- [x] T006 [US1] Modify `ExpandInsertColumnsOperation.cs` to append metadata comment after each column name using `Column.TypeDisplay`, `IsNullable`, `DefaultValue`, and `IsIdentity` from the schema cache. Skip identity columns or mark with `-- IDENTITY`. Respect `InsertColumnsIncludeTypes` setting in src/AkmlSql.Engine/Refactoring/Operations/Lightweight/ExpandInsertColumnsOperation.cs
- [x] T007 [US1] Add unit tests for INSERT metadata comments covering: basic type+nullability comment, default value inclusion, identity column exclusion, and disabled setting (no comments) in tests/AkmlSql.Core.Tests/Refactoring/InsertMetadataTests.cs

**Checkpoint**: INSERT expansion shows accurate type, nullability, and default info as inline comments

---

## Phase 3: User Story 2 — Convert sp_executesql to Static SQL (Priority: P1)

**Goal**: Actions List offers conversion of `EXEC sp_executesql` calls to runnable static SQL with parameter values substituted.

**Independent Test**: Write `EXEC sp_executesql N'SELECT * FROM dbo.Orders WHERE OrderID = @id', N'@id int', @id = 5`, invoke action, verify output is `SELECT * FROM dbo.Orders WHERE OrderID = 5`.

### Implementation for User Story 2

- [x] T008 [P] [US2] Create `ConvertSpExecutesqlOperation.cs` as a new lightweight refactoring operation that: parses `EXEC sp_executesql @template, @paramDefs, @p1=v1...` from the document text, extracts the SQL template string, parses parameter definitions and values, substitutes each `@param` in the template with its literal value (preserving string quoting), and returns the static SQL in src/AkmlSql.Engine/Refactoring/Operations/Lightweight/ConvertSpExecutesqlOperation.cs
- [x] T009 [US2] Wire `ConvertSpExecutesqlOperation` into the FormatAction IPC handler by adding a case for `FormatActionType.ConvertSpExecutesql` (16) in the format action dispatcher in src/AkmlSql.Engine/Formatting/FormatActionHandler.cs (or equivalent dispatcher)
- [x] T010 [US2] Add sp_executesql detection to `LightbulbProvider.cs`: when the current line contains `sp_executesql`, add a RefactoringAction with DisplayText "Convert to Static SQL" and action type `FormatActionType.ConvertSpExecutesql` in src/AkmlSql.Shell.Shared/Analysis/LightbulbProvider.cs
- [x] T011 [US2] Add unit tests for sp_executesql conversion covering: basic parameter substitution, string parameter quoting, NULL parameter values, multiple parameters, and invalid input (not sp_executesql) in tests/AkmlSql.Core.Tests/Refactoring/SpExecutesqlConversionTests.cs

**Checkpoint**: sp_executesql calls can be converted to static SQL from the lightbulb menu

---

## Phase 4: User Story 4 — Completion Popup Ctrl Transparency (Priority: P3)

**Goal**: Holding Ctrl makes the completion popup semi-transparent so code behind is visible.

**Independent Test**: Trigger autocomplete, hold Ctrl, verify popup becomes transparent. Release Ctrl, verify full opacity.

### Implementation for User Story 4

- [x] T012 [US4] Modify `CompletionController.cs` to detect Ctrl key press/release in the `Exec` method: when Ctrl is held (without Space, i.e., not Ctrl+Space chord) and the popup is visible, set `_adornment.PopupOpacity = 0.3`; on Ctrl release, restore to `1.0` in src/AkmlSql.Shell.Shared/Editor/Completion/CompletionController.cs
- [x] T013 [US4] Add `PopupOpacity` property to `CompletionPopupAdornment` that forwards to the underlying WPF popup Border's `Opacity` property in src/AkmlSql.Shell.Shared/Editor/Completion/CompletionPopupAdornment.cs

**Checkpoint**: Completion popup becomes semi-transparent when Ctrl is held

---

## Phase 5: User Story 5 — Tab Color Gradient (Priority: P3)

**Goal**: Optional gradient rendering on tab header color bars (lighter top, base color bottom).

**Independent Test**: Enable gradient in Settings > Tabs, open a tab with environment color, verify gradient rendering.

### Implementation for User Story 5

- [x] T014 [US5] Modify `TabColoringManager.cs` to check `TabSettings.GradientColors` setting: when true, replace the `SolidColorBrush` used for the tab header bar with a `LinearGradientBrush` going from a lighter tint (20% toward white) at the top to the base environment color at the bottom in src/AkmlSql.Shell.Shared/Tabs/TabColoringManager.cs
- [x] T015 [US5] Add "Use gradient colors" toggle to the Tabs & UI settings page in `SettingsWindow.cs`, wired to `TabSettings.GradientColors` with load/save in src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs

**Checkpoint**: Tab header bars show gradient when enabled, flat color when disabled

---

## Phase 6: User Story 6 — Excel 15+ Digit Precision (Priority: P3)

**Goal**: Excel export preserves exact numeric values for 15+ digit numbers by formatting as text.

**Independent Test**: Export a result containing a 16-digit number to Excel, open in Excel, verify value is not rounded.

### Implementation for User Story 6

- [x] T016 [US6] Modify `GridExportService.cs` to check `GridSettings.ExcelLargeNumberAsText` setting: when formatting int/bigint cells, if the string representation has 15+ digits, set the cell's DataType to Text (XLDataType.Text) instead of Number to prevent Excel rounding in src/AkmlSql.Engine/Export/GridExportService.cs
- [x] T017 [US6] Add "Save 15+ digit numbers as text" toggle to the Grid settings page in `SettingsWindow.cs`, wired to `GridSettings.ExcelLargeNumberAsText` with load/save in src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs

**Checkpoint**: Large numeric IDs exported to Excel retain exact values

---

## Phase 7: User Story 7 — Split Table Refactoring (Priority: P4)

**Goal**: Heavyweight refactoring that splits selected columns into a new related table with FK, data migration, and dependent object updates.

**Independent Test**: Select columns from a table, invoke Split Table, verify generated script contains CREATE TABLE, ALTER TABLE FK, INSERT INTO migration, and dependent object updates.

### Implementation for User Story 7

- [x] T018 [P] [US7] Create `SplitTableOperation.cs` extending `HeavyweightOperationBase` with PreviewAsync that: accepts table name + column list to move, queries schema cache for FK dependencies, generates RefactorChangeInfo array containing CREATE TABLE DDL for new table, ALTER TABLE for FK constraint, INSERT INTO for data migration, and ALTER statements for dependent procedures/views in src/AkmlSql.Engine/Refactoring/Operations/Heavyweight/SplitTableOperation.cs
- [x] T019 [P] [US7] Create `SplitTableCommand.cs` as a shell command that: shows a column picker dialog (checkbox list of columns to move), sends RefactorPreviewRequest with OperationType=SplitTable to engine, opens RefactoringPreviewDialog with results, on "Generate Script" uses RenameScriptGenerator pattern to open script in new tab in src/AkmlSql.Shell.Shared/Refactoring/SplitTableCommand.cs
- [x] T020 [US7] Add `SplitTable = 8` to `RefactorOperationType` enum and wire the new operation into `RefactoringEngine` dispatch in src/AkmlSql.Core/Ipc/Messages/RefactorPreviewRequest.cs and src/AkmlSql.Engine/Refactoring/RefactoringEngine.cs
- [x] T021 [US7] Add SplitTableCommand.Initialize to all 6 AkmlSqlPackage.cs files and add SplitTable button + VSCT entry in all 6 .vsct files
- [x] T022 [US7] Add unit tests for SplitTableOperation covering: basic column split, FK generation, data migration INSERT, and empty column selection in tests/AkmlSql.Core.Tests/Refactoring/SplitTableOperationTests.cs

**Checkpoint**: Split Table generates complete migration script in a new editor tab

---

## Phase 8: Polish & Cross-Cutting

**Purpose**: Final validation across all features.

- [x] T023 [P] Run full test suite and fix regressions via `dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj`
- [x] T024 [P] Add new settings toggles (InsertColumnsIncludeTypes, GradientColors, ExcelLargeNumberAsText) to Settings UI pages in src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs if not already added in earlier tasks
- [x] T025 Update doc/progress.md feature inventory to include all 6 new features

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **US1 INSERT Metadata (Phase 2)**: Depends on T002 (FormatterSettings)
- **US2 sp_executesql (Phase 3)**: Depends on T005 (FormatActionType enum)
- **US4 Ctrl Transparency (Phase 4)**: Depends on Phase 1 only
- **US5 Tab Gradient (Phase 5)**: Depends on T004 (TabSettings)
- **US6 Excel Precision (Phase 6)**: Depends on T003 (GridSettings)
- **US7 Split Table (Phase 7)**: Depends on Phase 1 only
- **Polish (Phase 8)**: Depends on all desired phases being complete

### Parallel Opportunities

- **T002, T003, T004, T005**: All modify different sections of AppSettings — can run in parallel
- **US1 + US2**: Different file sets — can run in parallel after Phase 1
- **US4 + US5 + US6**: All independent, different files — can run in parallel
- **T018 + T019**: Different projects (Engine vs Shell) — can run in parallel

---

## Implementation Strategy

### MVP First (US1 Only)

1. Phase 1: Setup (T001-T005)
2. Phase 2: US1 INSERT Metadata (T006-T007)
3. **STOP**: Test INSERT expansion with metadata comments
4. Deploy — immediate value for INSERT workflow

### Incremental Delivery

1. US1 (INSERT Metadata) + US2 (sp_executesql) → Deploy (P1 features)
2. US4 + US5 + US6 in parallel → Deploy (P3 polish)
3. US7 (Split Table) → Deploy (P4 advanced refactoring)

### After All Phases: 100% SQL Prompt Core Parity

---

## Notes

- US3 (Copy with Headers) removed — research confirmed all copy formats already include headers
- All new settings default to reasonable values (types=true, gradient=false, precision=true)
- Split Table (US7) is the only high-complexity task; all others are low-to-medium effort
- Total new files: 3 (ConvertSpExecutesqlOperation, SplitTableOperation, SplitTableCommand)
- Total modified files: ~12 (AppSettings, enum, operations, controllers, settings UI, .vsct)
