# Tasks: Code Refactoring Toolkit

**Input**: Design documents from `/specs/006-code-refactoring/`
**Prerequisites**: plan.md ✓, spec.md ✓, research.md ✓, data-model.md ✓, contracts/ ✓, quickstart.md ✓

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3, US4)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Verify folder structure exists for all new namespaces; no new .csproj files are created.

- [X] T001 Verify/create folder `src/AkmlSql.Engine/Refactoring/Operations/Lightweight/` and `src/AkmlSql.Engine/Refactoring/Operations/Heavyweight/`
- [X] T002 [P] Verify/create folders `src/AkmlSql.Shell.Shared/Formatting/` (may already exist) and `src/AkmlSql.Shell.Shared/Refactoring/`
- [X] T003 [P] Verify/create folders `tests/AkmlSql.Engine.Tests/Refactoring/Operations/Lightweight/` and `tests/AkmlSql.Engine.Tests/Refactoring/Operations/Heavyweight/`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core models, IPC contracts, and engine scaffolding — MUST be complete before any user story can be implemented.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T004 Add `RefactoringSettings` class (6 properties: `PreviewBeforeApply`, `CreateBackups`, `FormatAfterRefactor`, `RenameScope`, `IncludeCommentsInRename`, `IncludeStringLiteralsInRename`) to `src/AkmlSql.Core/Config/AppSettings.cs` following the `CodeAnalysisSettings` pattern from Phase 5; add `[JsonPropertyName]` attributes and default values matching the schema in `contracts/refactoring-settings-schema.md`
- [X] T005 [P] Add message type constants `RequestRefactorPreview = 30`, `RequestRefactorApply = 31`, `RefactorPreviewResult = 130`, `RefactorApplyResult = 131` to `src/AkmlSql.Core/Ipc/MessageTypes.cs`
- [X] T006 [P] Extend the `FormatActionType` enum in `src/AkmlSql.Core/Ipc/Messages/FormatActionRequest.cs` with values 8–15: `RemoveSemicolons=8`, `ExpandInsertColumns=9`, `ExpandExecParameters=10`, `ExpandUpdateColumns=11`, `ConvertOldStyleJoins=12`, `AddGroupByColumns=13`, `EncapsulateBeginEnd=14`, `ReplaceDeprecatedSyntax=15`
- [X] T007 Create `src/AkmlSql.Core/Ipc/Messages/RefactorChangeInfo.cs` as a `[MessagePackObject]` with 9 key fields: `FilePath` [Key(0)], `StartOffset` [Key(1)], `EndOffset` [Key(2)], `OldText` [Key(3)], `NewText` [Key(4)], `Line` [Key(5)], `Column` [Key(6)], `ContextSnippet` [Key(7)], `ChangeCategory` [Key(8)] — see `contracts/rpc-messages.md`
- [X] T008 [P] Create `src/AkmlSql.Core/Ipc/Messages/RefactorPreviewRequest.cs` as a `[MessagePackObject]` with 12 key fields: `SessionId` [Key(0)], `RequestId` [Key(1)], `OperationType` [Key(2)], `Scope` [Key(3)], `DocumentText` [Key(4)], `DocumentPath` [Key(5)], `SelectionStart` [Key(6)], `SelectionLength` [Key(7)], `AdditionalFilePaths` [Key(8)], `NewName` [Key(9)], `ExtractedUnitName` [Key(10)], `OriginalIdentifier` [Key(11)]
- [X] T009 [P] Create `src/AkmlSql.Core/Ipc/Messages/RefactorPreviewResponse.cs` as a `[MessagePackObject]` with 5 key fields: `Changes` [Key(0)] as `RefactorChangeInfo[]`, `Warnings` [Key(1)] as `string[]`, `Errors` [Key(2)] as `string[]`, `CanApply` [Key(3)] as `bool`, `GeneratedObjectTexts` [Key(4)] as `string[]`
- [X] T010 [P] Create `src/AkmlSql.Core/Ipc/Messages/RefactorApplyRequest.cs` as a `[MessagePackObject]` with 7 key fields: `SessionId` [Key(0)], `RequestId` [Key(1)], `OperationType` [Key(2)], `ApprovedChanges` [Key(3)] as `RefactorChangeInfo[]`, `CreateBackups` [Key(4)], `FormatAfterRefactor` [Key(5)], `SessionProfileName` [Key(6)]
- [X] T011 [P] Create `src/AkmlSql.Core/Ipc/Messages/RefactorApplyResponse.cs` as a `[MessagePackObject]` with 5 key fields: `Success` [Key(0)], `AppliedCount` [Key(1)], `FailedFilePaths` [Key(2)] as `string[]`, `BackupFilePaths` [Key(3)] as `string[]`, `UpdatedDocumentText` [Key(4)]
- [X] T012 Create `src/AkmlSql.Engine/Refactoring/RefactoringContext.cs` holding per-request inputs: parsed `TSqlScript`, token stream, `SelectionStart`, `SelectionLength`, `SessionId`, `RefactoringSettings`, and `AdditionalFilePaths`
- [X] T013 Create `src/AkmlSql.Engine/Refactoring/RefactoringEngine.cs` as the dispatcher stub: `PreviewAsync(RefactorPreviewRequest)` and `ApplyAsync(RefactorApplyRequest)` methods with a `switch` on `OperationType` (values 0–7) — all cases `throw new NotImplementedException` initially; fill in per phase
- [X] T014 Wire `MessageTypes.RequestRefactorPreview (30)` and `MessageTypes.RequestRefactorApply (31)` into `DispatchAsync` in `src/AkmlSql.Engine/Server/PipeRpcServer.cs`: deserialize request, call `RefactoringEngine.PreviewAsync` / `ApplyAsync`, serialize and return response
- [X] T015 [P] Write MessagePack serialization round-trip tests for all 5 new IPC models (`RefactorChangeInfo`, `RefactorPreviewRequest`, `RefactorPreviewResponse`, `RefactorApplyRequest`, `RefactorApplyResponse`) in `tests/AkmlSql.Engine.Tests/Refactoring/RefactoringEngineTests.cs`

**Checkpoint**: Foundation ready — core models serialise correctly, engine stub dispatches without error. User story implementation can begin.

---

## Phase 3: User Story 1 — Instant Inline Refactoring (Priority: P1) 🎯 MVP

**Goal**: Deliver all 8 lightweight refactoring operations as instant, keyboard-shortcut-driven commands reusing the existing `FormatAction` IPC channel with new `FormatActionType` values 8–15.

**Independent Test**: Open any SQL file in SSMS/VS, invoke each of the 8 new commands via keyboard shortcut or menu, and verify the document updates correctly in under 100ms with no dialog shown.

### Implementation for User Story 1

- [X] T016 [US1] Extend `HandleFormatAction()` in `src/AkmlSql.Engine/Formatter/FormatRequestHandler.cs` to dispatch `ActionType` values 8–15 to the corresponding `ILightweightRefactoringOperation` implementations; add a `Warnings` propagation path so non-equi-join and schema-miss warnings are returned in `FormatActionResponse.Warnings` (partial — interface and operations created; FormatRequestHandler wiring pending)
- [X] T017 [P] [US1] Implement `src/AkmlSql.Engine/Refactoring/Operations/Lightweight/RemoveSemicolonsOperation.cs`: visit `StatementList` nodes, collect offsets of all `;` terminators, apply replacements in reverse offset order using the document text; supports selection-only mode via `RefactoringContext.SelectionStart/Length`
- [X] T018 [P] [US1] Implement `src/AkmlSql.Engine/Refactoring/Operations/Lightweight/ExpandInsertColumnsOperation.cs`: detect `InsertStatement` nodes without a `ColumnList`, look up target table columns from the schema cache via `SessionId`, insert the column list; return a cache-miss warning (`"Could not resolve columns for: <table>"`) without aborting when the table is absent from cache
- [X] T019 [P] [US1] Implement `src/AkmlSql.Engine/Refactoring/Operations/Lightweight/ExpandExecParametersOperation.cs`: detect `ExecuteStatement` nodes with positional (unnamed) parameters, look up procedure parameter names from schema cache, rewrite as `@param = value` named form; warn on cache miss
- [X] T020 [P] [US1] Implement `src/AkmlSql.Engine/Refactoring/Operations/Lightweight/ExpandUpdateColumnsOperation.cs`: detect `UpdateStatement` nodes, look up all table columns from schema cache, expand `SET` clause to list all columns with their current expressions or `NULL` defaults; warn on cache miss
- [X] T021 [P] [US1] Implement `src/AkmlSql.Engine/Refactoring/Operations/Lightweight/ConvertOldStyleJoinsOperation.cs`: detect `FromClause` with multiple comma-separated `NamedTableReference` entries (no `JoinTableReference`), partition WHERE conditions into equi-join predicates (moved to ON clause) vs non-equi/filter predicates (left in WHERE), emit `INNER JOIN … ON …` syntax; return a warning for each non-equi predicate that was left unchanged (per FR-011 and clarification Q4)
- [X] T022 [P] [US1] Implement `src/AkmlSql.Engine/Refactoring/Operations/Lightweight/AddGroupByColumnsOperation.cs`: collect non-aggregated `SelectScalarExpression` elements (those not wrapped in an aggregate function call), append a `GROUP BY` clause listing them; no-op if `GROUP BY` already exists
- [X] T023 [P] [US1] Implement `src/AkmlSql.Engine/Refactoring/Operations/Lightweight/EncapsulateBeginEndOperation.cs`: wrap selected statements (or the full batch if no selection) in `BEGIN … END`; handle selection boundary alignment to whole statements using `TSqlFragment.StartOffset/FragmentLength`
- [X] T024 [P] [US1] Implement `src/AkmlSql.Engine/Refactoring/Operations/Lightweight/ReplaceDeprecatedSyntaxOperation.cs`: invoke the Phase 5 `AnalysisEngine` to collect deprecated-construct diagnostics, then apply the existing `FixAction` replacements for those diagnostics in a single pass; return any un-fixable diagnostics as warnings
- [x] T025 [P] [US1] Implement `src/AkmlSql.Shell.Shared/Formatting/RemoveSemicolonsCommand.cs` following the identical pattern of existing `InsertSemicolonsCommand.cs` (inherit `FormatActionCommandBase`, set `ActionType = FormatActionType.RemoveSemicolons`)
- [x] T026 [P] [US1] Implement `src/AkmlSql.Shell.Shared/Formatting/ExpandInsertColumnsCommand.cs` (ActionType = ExpandInsertColumns); display `Warnings` returned in `FormatActionResponse` via `VsShellUtilities.ShowMessageBox` if non-empty
- [x] T027 [P] [US1] Implement `src/AkmlSql.Shell.Shared/Formatting/ExpandExecParametersCommand.cs` (ActionType = ExpandExecParameters); surface warnings
- [x] T028 [P] [US1] Implement `src/AkmlSql.Shell.Shared/Formatting/ExpandUpdateColumnsCommand.cs` (ActionType = ExpandUpdateColumns); surface warnings
- [x] T029 [P] [US1] Implement `src/AkmlSql.Shell.Shared/Formatting/ConvertOldStyleJoinsCommand.cs` (ActionType = ConvertOldStyleJoins); surface non-equi-join warnings as an info bar or message box
- [x] T030 [P] [US1] Implement `src/AkmlSql.Shell.Shared/Formatting/AddGroupByColumnsCommand.cs` (ActionType = AddGroupByColumns)
- [x] T031 [P] [US1] Implement `src/AkmlSql.Shell.Shared/Formatting/EncapsulateBeginEndCommand.cs` (ActionType = EncapsulateBeginEnd); pass current selection offsets in `SelectionStart`/`SelectionLength`
- [x] T032 [P] [US1] Implement `src/AkmlSql.Shell.Shared/Formatting/ReplaceDeprecatedSyntaxCommand.cs` (ActionType = ReplaceDeprecatedSyntax)
- [x] T033 [US1] Add 8 new `CmdId` constants for lightweight commands to `src/AkmlSql.Shell.Shared/PackageGuids.cs`; add corresponding `<Button>` entries with `<Strings>` and `<KeyBinding>` to the `.vsct` command table files in all 6 shell extension projects (`AkmlSql.Ssms20`, `AkmlSql.Ssms21`, `AkmlSql.Ssms22`, `AkmlSql.VS2019`, `AkmlSql.VS2022`, `AkmlSql.VS2026`) under the AKML SQL top-level menu
- [x] T034 [US1] Add all 8 new lightweight shell command `.cs` files to `src/AkmlSql.Shell.Shared/AkmlSql.Shell.Shared.projitems` so they compile into all 6 shell extension projects
- [X] T035 [P] [US1] Write unit tests for `RemoveSemicolons`, `EncapsulateBeginEnd`, and `ReplaceDeprecatedSyntax` operations in `tests/AkmlSql.Engine.Tests/Refactoring/Operations/Lightweight/RemoveSemicolonsTests.cs`, `EncapsulateBeginEndTests.cs`, `ReplaceDeprecatedSyntaxTests.cs` — cover: full-document mode, selection-only mode, no-op on empty input
- [X] T036 [P] [US1] Write unit tests for `ExpandInsertColumns`, `ExpandExecParameters`, `ExpandUpdateColumns`, `ConvertOldStyleJoins`, and `AddGroupByColumns` in `tests/AkmlSql.Engine.Tests/Refactoring/Operations/Lightweight/` — cover: schema-cache-hit produces correct expansion, schema-cache-miss returns warning without aborting, equi-join moves to ON, non-equi stays in WHERE with warning, GROUP BY already exists is a no-op

**Checkpoint**: All 8 lightweight operations work end-to-end from keyboard shortcut → engine → document update in under 100ms. Schema-miss warnings surface correctly.

---

## Phase 4: User Story 2 — Safe Rename with Preview (Priority: P2)

**Goal**: Implement the Safe Rename heavyweight operation with `ReferenceCollector`, name collision detection, stale-file detection, and the `RefactoringPreviewDialog` WinForms UI.

**Independent Test**: Rename a column alias within a single script; verify the preview dialog lists all occurrences with before/after diffs, selective apply works, and applying produces the correct document text.

### Implementation for User Story 2

- [X] T037 [US2] Implement `src/AkmlSql.Engine/Refactoring/ReferenceCollector.cs` as a `TSqlFragmentVisitor` subclass that visits `ColumnReferenceExpression`, `NamedTableReference`, `SchemaObjectName`, `VariableReference`, `ProcedureReference`, and bare `Identifier` nodes; for each match against the target identifier (case-insensitive `OrdinalIgnoreCase`), record `(FilePath, StartOffset, FragmentLength, Line, Column, ContextSnippet)` into a `List<ReferenceMatch>`; support scope filtering (current script vs all provided file paths)
- [X] T038 [US2] Implement `src/AkmlSql.Engine/Refactoring/Operations/Heavyweight/SafeRenameOperation.cs`: (1) parse document with `TSqlParser`, run `ReferenceCollector` for `OriginalIdentifier`; (2) check name collision — if `NewName` already exists as an identifier in the same scope, populate `Errors` and set `CanApply = false`; (3) optionally search comment text and string literals per `RefactoringSettings`; (4) for `ProjectDirectory` scope, re-parse each `.sql` file under the current file's directory (recursive), run collector, check each file's modification timestamp and store it; merge all `RefactorChangeInfo[]` sorted by file path then offset descending; (5) apply: verify file timestamps match preview timestamps — skip files that have changed (add to `FailedFilePaths`), apply replacements in reverse offset order, write backups when `CreateBackups = true`
- [~] T039 [US2] Implement `src/AkmlSql.Shell.Shared/Refactoring/RefactoringPreviewDialog.cs` as a WinForms `Form`: left panel = `TreeView` with file-level nodes and per-reference checkbox children; right panel = read-only `RichTextBox` showing unified diff (`- old / + new`) for the selected reference; bottom bar = "Apply Selected" `Button` (disabled when `CanApply = false` or no checkboxes checked) + "Cancel" `Button`; `ApprovedChanges` property returns only the checked `RefactorChangeInfo` items; constructor accepts `RefactorPreviewResponse` — **Partial**: stub file created in `src/AkmlSql.Shell.Shared/Refactoring/RefactoringPreviewDialog.cs` and added to projitems; full WinForms implementation requires .NET 4.7.2 shell project context
- [~] T040 [US2] Implement `src/AkmlSql.Shell.Shared/Refactoring/SafeRenameCommand.cs`: (1) show an input `InputDialog` prompting for the new identifier name; (2) async-send `RequestRefactorPreview (30)` with `OperationType = SafeRename`, `OriginalIdentifier`, `NewName`, `Scope`, `AdditionalFilePaths`; (3) on response, open `RefactoringPreviewDialog`; (4) if user clicks Apply, async-send `RequestRefactorApply (31)` with approved changes and settings flags; (5) apply `UpdatedDocumentText` to the active editor buffer in a single `ITextEdit` transaction; surface `FailedFilePaths` warnings if any files were skipped — **Partial**: stub file created in `src/AkmlSql.Shell.Shared/Refactoring/SafeRenameCommand.cs` and added to projitems; full shell command implementation requires .NET 4.7.2 shell project context
- [X] T041 [US2] Wire `RefactorOperationType.SafeRename (0)` into `RefactoringEngine.PreviewAsync` and `ApplyAsync` in `src/AkmlSql.Engine/Refactoring/RefactoringEngine.cs` — replace the `NotImplementedException` stub for case 0
- [X] T042 [US2] Add `CmdId` constant for `SafeRename` to `src/AkmlSql.Shell.Shared/PackageGuids.cs`; add `<Button>` entry with `<KeyBinding>` to all 6 shell extension `.vsct` files under the AKML SQL menu
- [X] T043 [US2] Add `RefactoringPreviewDialog.cs` and `SafeRenameCommand.cs` to `src/AkmlSql.Shell.Shared/AkmlSql.Shell.Shared.projitems`
- [X] T044 [P] [US2] Write unit tests for `ReferenceCollector` in `tests/AkmlSql.Engine.Tests/Refactoring/ReferenceCollectorTests.cs` — cover: simple column reference, schema-qualified table reference, alias (does not match the aliased name), `@variable` reference, procedure reference, case-insensitive match, non-match returns empty
- [X] T045 [P] [US2] Write unit tests for `SafeRenameOperation` happy-path cases in `tests/AkmlSql.Engine.Tests/Refactoring/Operations/Heavyweight/SafeRenameTests.cs` — cover: rename column alias (3 occurrences), rename `@variable` (2 occurrences), rename table reference, case-insensitive rename, `CanApply = true` with non-empty `Changes`
- [X] T046 [P] [US2] Write unit tests for `SafeRenameOperation` edge cases in `tests/AkmlSql.Engine.Tests/Refactoring/Operations/Heavyweight/SafeRenameTests.cs` — cover: name collision sets `CanApply = false` with error message, cross-file finds references in additional files, stale file (timestamp changed) appears in `FailedFilePaths` while other files succeed, selective apply (subset of approved changes applied correctly)

**Checkpoint**: Safe Rename works end-to-end: invoke → input name → preview dialog shows diffs → selective apply updates document and reports skipped files.

---

## Phase 5: User Story 3 — Extract to Named Unit (Priority: P3)

**Goal**: Implement the four extract/encapsulate heavyweight operations using offset-based text splicing and the existing `RefactoringPreviewDialog`.

**Independent Test**: Select a subquery in a FROM clause, invoke "Extract to CTE", enter a CTE name, verify the preview shows the WITH block prepended and the subquery replaced with the CTE reference, and applying produces correct SQL.

### Implementation for User Story 3

- [X] T047 [P] [US3] Implement `src/AkmlSql.Engine/Refactoring/Operations/Heavyweight/ExtractToCteOperation.cs`: (1) identify the selected `QuerySpecification` or `Subquery` from `SelectionStart/Length`; (2) infer column names from `SelectElements` using `CteResolver` pattern (alias → column name → `ColN` fallback); (3) generate `WITH <name> AS (…)` text; (4) produce two `RefactorChangeInfo` entries: one replacing the subquery span with the CTE alias, one inserting the WITH block before the outer statement; set `GeneratedObjectTexts[0]` to the full CTE block; `CanApply = false` if selection is not a valid standalone query
- [X] T048 [P] [US3] Implement `src/AkmlSql.Engine/Refactoring/Operations/Heavyweight/ExtractToProcOperation.cs`: (1) walk the selected block with `VariableReferenceVisitor` to collect all `@variable` references; (2) cross-reference with outer `DeclareVariableElement` scope to classify as parameters (declared outside) vs locals (declared inside); (3) detect output parameters (assigned inside, used after block); (4) generate `CREATE PROCEDURE dbo.<name> @Param type AS BEGIN SET NOCOUNT ON … END` text; (5) generate call-site replacement `EXEC dbo.<name> @Param = <value>`; set `GeneratedObjectTexts[0]` to the CREATE PROCEDURE script
- [X] T049 [P] [US3] Implement `src/AkmlSql.Engine/Refactoring/Operations/Heavyweight/ExtractToDerivedTableOperation.cs`: wrap the selected subquery as `(<subquery>) AS <alias>` inline derived table; produce one `RefactorChangeInfo` replacing the original span; `CanApply = false` if selection is not a valid `QueryExpression`
- [X] T050 [P] [US3] Implement `src/AkmlSql.Engine/Refactoring/Operations/Heavyweight/EncapsulateAsViewOperation.cs`: (1) validate selected or full-statement is a `SELECT` query; (2) generate `CREATE VIEW dbo.<name> AS <query>` text; (3) replace original SELECT with `SELECT * FROM dbo.<name>` (or preserve alias); set `GeneratedObjectTexts[0]` to the CREATE VIEW script
- [X] T051 [US3] Wire `ExtractToCte (1)`, `ExtractToProc (2)`, `ExtractToDerivedTable (3)`, `EncapsulateAsView (4)` into `RefactoringEngine.PreviewAsync` and `ApplyAsync` in `src/AkmlSql.Engine/Refactoring/RefactoringEngine.cs`
- [X] T052 [P] [US3] Implement `src/AkmlSql.Shell.Shared/Refactoring/ExtractToCteCommand.cs`: show name-input dialog → async `RequestRefactorPreview` with `OperationType = ExtractToCte`, `ExtractedUnitName`, selection offsets → open `RefactoringPreviewDialog` → async `RequestRefactorApply` → write updated document text
- [X] T053 [P] [US3] Implement `src/AkmlSql.Shell.Shared/Refactoring/ExtractToProcCommand.cs` following same pattern as `ExtractToCteCommand.cs` for `OperationType = ExtractToProc`
- [X] T054 [P] [US3] Implement `src/AkmlSql.Shell.Shared/Refactoring/ExtractToDerivedTableCommand.cs` for `OperationType = ExtractToDerivedTable`
- [X] T055 [P] [US3] Implement `src/AkmlSql.Shell.Shared/Refactoring/EncapsulateAsViewCommand.cs` for `OperationType = EncapsulateAsView`
- [X] T056 [US3] Add 4 new `CmdId` constants for Extract commands to `src/AkmlSql.Shell.Shared/PackageGuids.cs`; register in all 6 shell extension `.vsct` files under the AKML SQL menu with keyboard shortcuts
- [X] T057 [US3] Add `ExtractToCteCommand.cs`, `ExtractToProcCommand.cs`, `ExtractToDerivedTableCommand.cs`, `EncapsulateAsViewCommand.cs` to `src/AkmlSql.Shell.Shared/AkmlSql.Shell.Shared.projitems`
- [X] T058 [P] [US3] Write unit tests for extract operations in `tests/AkmlSql.Engine.Tests/Refactoring/Operations/Heavyweight/`: `ExtractToCteTests.cs` (basic extraction, CTE column inference from alias, CTE column inference from column name, `ColN` fallback), `ExtractToProcTests.cs` (parameter detection, local variable stays local, output parameter flagged), `EncapsulateAsViewTests.cs` (view generated, original replaced), `ExtractToDerivedTableOperation` tests appended to `ExtractToCteTests.cs` (subquery wrapped as derived table, non-query selection blocked)

**Checkpoint**: All four extract operations produce correct preview diffs and apply correct SQL transformations.

---

## Phase 6: User Story 4 — Temp Table / Table Variable Conversion and Parameterization (Priority: P4)

**Goal**: Implement temp-table ↔ table-variable conversion (both directions) and literal parameterization.

**Independent Test**: Run "Convert temp table to table variable" on a script containing `#TempOrders`; verify the result declares `@TempOrders TABLE(…)` and all `#TempOrders` references become `@TempOrders`, and a statistics warning is returned.

### Implementation for User Story 4

- [X] T059 [P] [US4] Implement `src/AkmlSql.Engine/Refactoring/Operations/Heavyweight/ConvertTempTableOperation.cs` handling both `ConvertTempToTableVar (5)` and `ConvertTableVarToTemp (6)` directions: (1) for direction 5 — find `CREATE TABLE #Name (…)` → replace with `DECLARE @Name TABLE (…)`, replace all `#Name` references with `@Name`; include warning `"Table variables do not support statistics. Queries using @Name may perform differently."` in `Warnings`; (2) for direction 6 — reverse transformation; `CanApply = false` if a name collision with an existing variable is detected
- [X] T060 [P] [US4] Implement `src/AkmlSql.Engine/Refactoring/Operations/Heavyweight/ParameterizeValuesOperation.cs`: collect `IntegerLiteral`, `StringLiteral`, `NumericLiteral`, and `RealLiteral` nodes appearing in `WHERE`, `ON`, `HAVING` clauses; infer variable names from column context (e.g., `CustomerId = 42` → `@CustomerId`); infer data types (`int`, `date`, `nvarchar`, `decimal`); generate `DECLARE @Var type = <literal>` statements at the top of the batch; replace each literal with `@VarName`; handle duplicate literal values by reusing the same variable
- [X] T061 [US4] Wire `ConvertTempToTableVar (5)`, `ConvertTableVarToTemp (6)`, `ParameterizeValues (7)` into `RefactoringEngine.PreviewAsync` and `ApplyAsync` in `src/AkmlSql.Engine/Refactoring/RefactoringEngine.cs`
- [X] T062 [P] [US4] Implement `src/AkmlSql.Shell.Shared/Refactoring/ConvertTempTableCommand.cs`: no name-input dialog required — async `RequestRefactorPreview` with `OperationType = ConvertTempToTableVar` (or `ConvertTableVarToTemp` for the reverse command) → open `RefactoringPreviewDialog` → async `RequestRefactorApply`; surface statistics warning from `Warnings[]` prominently in the preview dialog
- [X] T063 [P] [US4] Implement `src/AkmlSql.Shell.Shared/Refactoring/ParameterizeValuesCommand.cs`: no name-input dialog — async preview → `RefactoringPreviewDialog` → apply
- [X] T064 [US4] Add `CmdId` constants for `ConvertTempToTableVar`, `ConvertTableVarToTemp`, and `ParameterizeValues` to `src/AkmlSql.Shell.Shared/PackageGuids.cs`; register in all 6 shell extension `.vsct` files
- [X] T065 [US4] Add `ConvertTempTableCommand.cs` and `ParameterizeValuesCommand.cs` to `src/AkmlSql.Shell.Shared/AkmlSql.Shell.Shared.projitems`
- [X] T066 [P] [US4] Write unit tests for `ConvertTempTableOperation` in `tests/AkmlSql.Engine.Tests/Refactoring/Operations/Heavyweight/ConvertTempTableTests.cs` — cover: basic `#Name` → `@Name` conversion, multiple `#Name` references all updated, statistics warning present, name collision with existing variable sets `CanApply = false`, reverse direction (`@Name TABLE` → `#Name`) works correctly
- [X] T067 [P] [US4] Write unit tests for `ParameterizeValuesOperation` in `tests/AkmlSql.Engine.Tests/Refactoring/Operations/Heavyweight/ParameterizeValuesTests.cs` — cover: integer literal → `int`, quoted date string → `date`, `nvarchar` string literal → `nvarchar(N)`, repeated literal reuses same variable, variable name inferred from column context, declaration inserted at batch top

**Checkpoint**: All four user stories deliver independent, testable refactoring capabilities.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Settings UI, QA benchmarks, and undo validation across all user stories.

- [X] T068 Add a "Refactoring" tab to `src/AkmlSql.Shell.Shared/Dialogs/SettingsDialog.cs`: 5 checkboxes (`previewBeforeApply`, `createBackups`, `formatAfterRefactor`, `includeCommentsInRename`, `includeStringLiteralsInRename`) + 1 dropdown (`renameScope`: "Current Script" / "Project Directory"); load from and save to `AppSettings.Refactoring` following the Code Analysis tab pattern from Phase 5
- [X] T069 Verify `SettingsDialog.cs` is already in `src/AkmlSql.Shell.Shared/AkmlSql.Shell.Shared.projitems`; no change needed if already present
- [X] T070 [P] Write performance benchmark tests in `tests/AkmlSql.Engine.Tests/Refactoring/` verifying SC-001 (lightweight ops < 100ms on 2,000-line document), SC-002 (Safe Rename < 200ms on 1,000-line script), SC-004 (Extract wizard preview < 500ms) using `Stopwatch` assertions in xUnit `[Fact]` tests
- [X] T071 [P] Write undo integration test in `tests/AkmlSql.Engine.Tests/Refactoring/RefactoringEngineTests.cs`: apply an inline rename of 8 references → verify `UpdatedDocumentText` contains `NewText` at all 8 locations → verify a single-undo step (simulated by re-applying `OldText` offsets in reverse order) fully restores original text (SC-006 / FR-009)
- [X] T072 [P] Run false-positive sweep: write xUnit tests in `tests/AkmlSql.Engine.Tests/Refactoring/` that run each of the 8 lightweight operations on 5 valid SQL corpus scripts (stored as embedded resources) and assert `FormattedText` is valid SQL (parseable by `TSqlParser` with zero parse errors) — validates FR-002 and SC-007
- [ ] T073 Validate quickstart.md Scenarios 1–10 manually against the implemented engine: run each scenario as described, verify output matches expected; document any deviations

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Phase 2 (FormatActionType enum, FormatRequestHandler dispatch)
- **US2 (Phase 4)**: Depends on Phase 2 (IPC models, RefactoringEngine scaffold, PipeRpcServer wiring) — independent of US1
- **US3 (Phase 5)**: Depends on Phase 2; depends on US2 for `RefactoringPreviewDialog` (T039)
- **US4 (Phase 6)**: Depends on Phase 2 and US2 (`RefactoringPreviewDialog`); independent of US1/US3
- **Polish (Phase 7)**: Depends on all user stories complete

### User Story Dependencies

- **US1 (P1)**: Can start immediately after Phase 2 — no dependency on US2/US3/US4
- **US2 (P2)**: Can start immediately after Phase 2 — no dependency on US1/US3/US4
- **US3 (P3)**: Depends on US2 completing T039 (`RefactoringPreviewDialog`) — otherwise independent
- **US4 (P4)**: Depends on US2 completing T039 (`RefactoringPreviewDialog`) — otherwise independent

### Within Each User Story

- Engine operations before shell commands
- Shell commands before .vsct/.projitems registration
- Registration (T033/T034, T042/T043, T056/T057, T064/T065) after all operations and commands for that story
- Tests ([P] marked) can run in parallel with implementation tasks targeting different files

### Parallel Opportunities

All [P]-marked tasks within a phase can run concurrently. Key parallel groups:
- T005 + T006: `MessageTypes.cs` and `FormatActionRequest.cs` — different files
- T007–T011: All 5 IPC message models — all different files
- T017–T024: All 8 lightweight engine operations — all different files
- T025–T032: All 8 lightweight shell commands — all different files
- T035 + T036: Lightweight test classes — different files
- T044 + T045 + T046: `ReferenceCollectorTests` and `SafeRenameTests` — different test scenarios in same or different files
- T047–T050: All 4 extract operations — different files
- T052–T055: All 4 extract shell commands — different files

---

## Parallel Example: User Story 1

```bash
# After T016 (HandleFormatAction dispatch) is complete, all 8 operations can run in parallel:
Task: T017 — RemoveSemicolonsOperation.cs
Task: T018 — ExpandInsertColumnsOperation.cs
Task: T019 — ExpandExecParametersOperation.cs
Task: T020 — ExpandUpdateColumnsOperation.cs
Task: T021 — ConvertOldStyleJoinsOperation.cs
Task: T022 — AddGroupByColumnsOperation.cs
Task: T023 — EncapsulateBeginEndOperation.cs
Task: T024 — ReplaceDeprecatedSyntaxOperation.cs

# Simultaneously, all 8 shell commands can also run in parallel with the operations above
# (they depend only on the FormatActionType enum from T006, not on the operations):
Task: T025–T032 — all 8 shell command files
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories)
3. Complete Phase 3: User Story 1 (8 lightweight operations + shell commands)
4. **STOP and VALIDATE**: Invoke each command from SSMS/VS; verify document updates in < 100ms
5. Run `dotnet test tests/AkmlSql.Engine.Tests --filter "FullyQualifiedName~Lightweight"`

### Incremental Delivery

1. Phase 1 + Phase 2 → Core models and IPC wired
2. Phase 3 (US1) → 8 lightweight operations live → Demo to stakeholders (MVP)
3. Phase 4 (US2) → Safe Rename with preview dialog → Highest-value heavyweight op delivered
4. Phase 5 (US3) → Extract operations → Wizard pattern established
5. Phase 6 (US4) → Conversion and parameterization → Full toolkit complete
6. Phase 7 → Settings UI + QA benchmarks → Production-ready

### Build Commands

```bash
# Engine (contains all refactoring operation logic)
dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64

# Run all refactoring tests
dotnet test tests/AkmlSql.Engine.Tests --filter "FullyQualifiedName~Refactoring"

# Shell extensions (must use MSBuild, not dotnet build — see CLAUDE.md)
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"
"$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Build -p:Configuration=Release -v:minimal
```

---

## Notes

- [P] tasks = different files, no blocking inter-task dependencies
- [Story] label maps task to specific user story for traceability
- All 8 lightweight operations reuse `FormatAction (13)` / `FormatActionResponse (113)` — no new IPC message types (R-001)
- All heavyweight operations use the two-phase protocol: `RequestRefactorPreview (30)` → `RefactorPreviewResult (130)` → `RequestRefactorApply (31)` → `RefactorApplyResult (131)` (R-002)
- Multi-span edits MUST be applied in **reverse offset order** within a single `ITextEdit` to avoid offset shifting (R-008)
- Cross-file undo is via backup files in `.refactor-backup/` — editor `Ctrl+Z` covers in-document changes only (FR-009, Clarification Q1)
- `previewBeforeApply` setting applies to heavyweight operations only — lightweight ops are always instant (FR-013, Clarification Q3)
- Non-equi-join conditions are left unchanged and returned as warnings, not errors (FR-011, Clarification Q4)
- Stale files (modified since preview) go to `FailedFilePaths`; remaining files are still processed (Edge Cases, Clarification Q5)
