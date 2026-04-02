# Implementation Plan: Code Refactoring Toolkit

**Branch**: `006-code-refactoring` | **Date**: 2026-03-23 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/006-code-refactoring/spec.md`

## Summary

Implement a code refactoring toolkit that provides 15 instant lightweight transformations (reusing the existing `FormatAction` IPC channel with new `FormatActionType` values 8–15) and 7 heavyweight wizard-based operations (Safe Rename, Extract to CTE/Procedure/Derived Table/View, Temp↔TableVar conversion, Parameterize Values) delivered through a new two-phase preview-then-apply IPC protocol (`RequestRefactorPreview` / `RequestRefactorApply`). All heavyweight operations go through a WinForms preview dialog that shows per-file, per-reference diffs with selective apply. The rename engine uses a `TSqlFragmentVisitor`-based `ReferenceCollector` that leverages the established `AliasResolver`, `CteResolver`, and `VariableTracker` patterns.

---

## Technical Context

**Language/Version**: C# / .NET 10 (Engine), .NET Standard 2.0 (Core models), .NET Framework 4.7.2 (Shell)
**Primary Dependencies**: Microsoft.SqlServer.TransactSql.ScriptDom (AST + visitors), MessagePack (IPC), System.Text.Json (settings), Serilog, xUnit 2.x (tests)
**Storage**: `config.json` extended with `refactoring` section; backup files in `.refactor-backup/` folders
**Testing**: xUnit 2.x via `dotnet test`; one test class per refactoring operation; Arrange-Act-Assert with `[Fact]`/`[Theory]`
**Target Platform**: Windows; out-of-process Engine (win-x64 .NET 10); shell extension (.NET 4.7.2); SSMS 20/21/22 + VS 2019/2022/2026
**Project Type**: Extension to existing Engine service + VS shell extension consumer
**Performance Goals**: Lightweight ops < 100ms; Safe Rename (1,000-line script) < 200ms; Safe Rename (100 files) < 5s; Extract wizard preview < 500ms
**Constraints**: No new .NET projects (refactoring engine is a folder inside `AkmlSql.Engine`); all shell-side UI in `AkmlSql.Shell.Shared`; multi-span edits applied in reverse offset order within a single `ITextEdit` for correct undo
**Scale/Scope**: 15 lightweight + 7 heavyweight operations; ~80 new tests; 6 shell targets via shared project

---

## Constitution Check

*No constitution.md exists — gates inferred from CLAUDE.md conventions and codebase patterns.*

| Gate | Status | Notes |
|------|--------|-------|
| No new standalone projects | PASS | Refactoring engine lives as a folder namespace inside existing `AkmlSql.Engine`; no new .csproj |
| New code follows established patterns | PASS | `TSqlFragmentVisitor`, MessagePack IPC, `FormatAction` channel, xUnit test style |
| Shell extensions use Shared project pattern | PASS | All new shell commands and dialogs in `AkmlSql.Shell.Shared` |
| No blocking UI-thread calls | PASS | Preview/Apply run in Engine (out-of-proc); shell fires async RPC |
| Tests for all new Engine logic | PASS | One test class per operation planned in `AkmlSql.Engine.Tests/Refactoring/` |
| Reuse existing IPC before adding new message types | PASS | Lightweight ops extend `FormatAction` (13); only 2 new message types (30, 31) for heavyweight |

---

## Project Structure

### Documentation (this feature)

```text
specs/006-code-refactoring/
├── plan.md                        # This file
├── research.md                    # Phase 0 — architectural decisions
├── data-model.md                  # Phase 1 — entities, enums, IPC assignments
├── quickstart.md                  # Phase 1 — developer test scenarios
├── contracts/
│   ├── rpc-messages.md            # IPC request/response contracts (types 30, 31, 130, 131)
│   └── refactoring-settings-schema.md  # config.json refactoring section schema
└── tasks.md                       # Phase 2 output (/speckit.tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
├── AkmlSql.Core/
│   ├── Config/
│   │   └── AppSettings.cs                    # + RefactoringSettings class
│   └── Ipc/
│       ├── MessageTypes.cs                   # + RequestRefactorPreview=30, RequestRefactorApply=31,
│       │                                     #   RefactorPreviewResult=130, RefactorApplyResult=131
│       └── Messages/
│           ├── RefactorPreviewRequest.cs     # NEW — operation type, scope, document text, selection
│           ├── RefactorPreviewResponse.cs    # NEW — changes[], warnings[], errors[], canApply
│           ├── RefactorApplyRequest.cs       # NEW — approved changes subset, backup/format flags
│           ├── RefactorApplyResponse.cs      # NEW — success, applied count, failed files, updated text
│           └── RefactorChangeInfo.cs         # NEW — file path, span, old/new text, context snippet
│
├── AkmlSql.Engine/
│   ├── Formatter/
│   │   └── FormatRequestHandler.cs           # Extend HandleFormatAction() for ActionType 8–15
│   └── Refactoring/
│       ├── RefactoringEngine.cs              # Orchestrator: dispatch preview/apply by OperationType
│       ├── RefactoringContext.cs             # Per-request input: AST, tokens, scope, settings
│       ├── ReferenceCollector.cs             # TSqlFragmentVisitor: collect all identifier references
│       └── Operations/
│           ├── Lightweight/
│           │   ├── RemoveSemicolonsOperation.cs
│           │   ├── ExpandInsertColumnsOperation.cs
│           │   ├── ExpandExecParametersOperation.cs
│           │   ├── ExpandUpdateColumnsOperation.cs
│           │   ├── ConvertOldStyleJoinsOperation.cs
│           │   ├── AddGroupByColumnsOperation.cs
│           │   ├── EncapsulateBeginEndOperation.cs
│           │   └── ReplaceDeprecatedSyntaxOperation.cs
│           └── Heavyweight/
│               ├── SafeRenameOperation.cs
│               ├── ExtractToCteOperation.cs
│               ├── ExtractToProcOperation.cs
│               ├── ExtractToDerivedTableOperation.cs
│               ├── EncapsulateAsViewOperation.cs
│               ├── ConvertTempTableOperation.cs
│               └── ParameterizeValuesOperation.cs
│
├── AkmlSql.Shell.Shared/
│   ├── Formatting/
│   │   ├── RemoveSemicolonsCommand.cs        # NEW (FormatActionType.RemoveSemicolons)
│   │   ├── ExpandInsertColumnsCommand.cs     # NEW
│   │   ├── ExpandExecParametersCommand.cs    # NEW
│   │   ├── ExpandUpdateColumnsCommand.cs     # NEW
│   │   ├── ConvertOldStyleJoinsCommand.cs    # NEW
│   │   ├── AddGroupByColumnsCommand.cs       # NEW
│   │   ├── EncapsulateBeginEndCommand.cs     # NEW
│   │   └── ReplaceDeprecatedSyntaxCommand.cs # NEW
│   ├── Refactoring/
│   │   ├── SafeRenameCommand.cs             # NEW — shows rename input dialog then preview
│   │   ├── ExtractToCteCommand.cs           # NEW
│   │   ├── ExtractToProcCommand.cs          # NEW
│   │   ├── ExtractToDerivedTableCommand.cs  # NEW
│   │   ├── EncapsulateAsViewCommand.cs      # NEW (different from Formatting/EncapsulateBeginEnd)
│   │   ├── ConvertTempTableCommand.cs       # NEW
│   │   ├── ParameterizeValuesCommand.cs     # NEW
│   │   └── RefactoringPreviewDialog.cs      # NEW — WinForms: file tree + diff view + checkboxes
│   └── Dialogs/
│       └── SettingsDialog.cs                # + Refactoring tab

tests/
└── AkmlSql.Engine.Tests/
    └── Refactoring/
        ├── RefactoringEngineTests.cs
        ├── ReferenceCollectorTests.cs
        ├── Operations/
        │   ├── Lightweight/
        │   │   ├── RemoveSemicolonsTests.cs
        │   │   ├── ExpandInsertColumnsTests.cs
        │   │   ├── ExpandExecParametersTests.cs
        │   │   ├── ConvertOldStyleJoinsTests.cs
        │   │   ├── AddGroupByColumnsTests.cs
        │   │   ├── EncapsulateBeginEndTests.cs
        │   │   └── ReplaceDeprecatedSyntaxTests.cs
        │   └── Heavyweight/
        │       ├── SafeRenameTests.cs
        │       ├── ExtractToCteTests.cs
        │       ├── ExtractToProcTests.cs
        │       ├── EncapsulateAsViewTests.cs
        │       ├── ConvertTempTableTests.cs
        │       └── ParameterizeValuesTests.cs
```

**Structure Decision**: Refactoring engine lives as a folder namespace inside the existing `AkmlSql.Engine` project — identical to the `Analysis/` folder added in Phase 5. No new .csproj files. All shell-side commands and the preview dialog go into `AkmlSql.Shell.Shared` following the established shared-project pattern.

---

## Implementation Phases

### Phase 0 — Research & Decisions *(complete — see research.md)*

- Lightweight ops reuse `FormatAction` channel (ActionTypes 8–15) — see R-001
- Heavyweight ops use new two-phase protocol (types 30/31/130/131) — see R-002
- `ReferenceCollector` visitor pattern for Safe Rename — see R-003
- Offset-based text splicing for CTE/proc extraction — see R-004
- Variable detection for procedure parameter inference — see R-005
- Equi-join predicate heuristic for old-style JOIN conversion — see R-006
- Refactoring settings in `AppSettings` (config.json) — see R-007
- Single `ITextEdit` transaction for undo integration — see R-008

---

### Phase 1 — Core Models & IPC

1. Add `RefactoringSettings` to `AppSettings`
2. Add `RequestRefactorPreview=30`, `RequestRefactorApply=31`, `RefactorPreviewResult=130`, `RefactorApplyResult=131` to `MessageTypes.cs`
3. Create `RefactorPreviewRequest`, `RefactorPreviewResponse`, `RefactorApplyRequest`, `RefactorApplyResponse`, `RefactorChangeInfo` MessagePack models
4. Extend `FormatActionType` enum with values 8–15
5. Create `RefactoringEngine`, `RefactoringContext` scaffolding
6. Wire `RequestRefactorPreview` and `RequestRefactorApply` into `PipeRpcServer.DispatchAsync`
7. Tests for models (serialization round-trip)

---

### Phase 2 — ReferenceCollector + Lightweight Ops

1. Implement `ReferenceCollector` (TSqlFragmentVisitor for ColumnReferenceExpression, NamedTableReference, SchemaObjectName, VariableReference, ProcedureReference, Identifier)
2. Extend `FormatRequestHandler.HandleFormatAction()` for ActionTypes 8–15:
   - RemoveSemicolons (8)
   - ExpandInsertColumns (9) — uses schema cache
   - ExpandExecParameters (10) — uses schema cache
   - ExpandUpdateColumns (11) — uses schema cache
   - ConvertOldStyleJoins (12) — equi-predicate heuristic
   - AddGroupByColumns (13) — infer from SelectElements
   - EncapsulateBeginEnd (14) — wrap selection
   - ReplaceDeprecatedSyntax (15) — delegate to Phase 5 rule fixes
3. Tests for all 8 lightweight operations (trigger + schema-cache-miss + selection-only scenarios)
4. Tests for ReferenceCollector (simple identifier, qualified identifier, alias, variable)

---

### Phase 3 — Safe Rename Operation

1. Implement `SafeRenameOperation` (uses `ReferenceCollector`, scoped to CurrentScript or ProjectDirectory)
2. Name collision detection (check target name against collector's symbol table)
3. Comment and string-literal search (honoring settings)
4. Cross-file: parse each additional file, collect references, merge into `RefactorChangeInfo[]` sorted by file then offset descending
5. Tests: rename column alias, rename variable, rename table reference, collision blocking, cross-file, case-insensitive

---

### Phase 4 — Extract Operations

1. `ExtractToCteOperation` — offset splicing: wrap selected QuerySpecification as CTE, replace with reference; infer column names from `CteResolver` patterns
2. `ExtractToProcOperation` — parameter detection via `VariableReferenceVisitor` + cross-reference with outer `DeclareVariableElement` scope; generate CREATE PROCEDURE + call site
3. `ExtractToDerivedTableOperation` — wrap subquery as derived table with user-supplied alias
4. `EncapsulateAsViewOperation` — generate CREATE VIEW + replace with SELECT from view
5. Tests for each: basic extraction, CTE column inference, proc parameter detection, name input, cancel path

---

### Phase 5 — Conversion Operations

1. `ConvertTempTableOperation` — replace `CREATE TABLE #Name` → `DECLARE @Name TABLE`, all `#Name` references → `@Name`; include warning about statistics in preview
2. `ConvertTableVarToTempOperation` — reverse of above
3. `ParameterizeValuesOperation` — collect IntegerLiteral, StringLiteral, RealLiteral in WHERE/JOIN; infer type; generate DECLARE at batch top; replace literals with variable references
4. Tests: basic conversion, multiple temp table references, collision with existing var names, literal type inference

---

### Phase 6 — Shell Extension UI

1. 8 new lightweight shell command classes (pattern identical to existing `InsertSemicolonsCommand`, `ExpandWildcardsCommand`)
2. `SafeRenameCommand` — show text input dialog for new name → async preview RPC → `RefactoringPreviewDialog`
3. `ExtractToCteCommand`, `ExtractToProcCommand`, `ExtractToDerivedTableCommand`, `EncapsulateAsViewCommand` — show name input dialog → async preview RPC → `RefactoringPreviewDialog`
4. `ConvertTempTableCommand`, `ParameterizeValuesCommand` — async preview RPC → `RefactoringPreviewDialog` (no name input)
5. `RefactoringPreviewDialog` — WinForms: left panel = file tree with checkbox per file; right panel = unified diff view; bottom = Apply Selected / Cancel; blocks apply if `CanApply = false`
6. Add Refactoring tab to `SettingsDialog` (6 settings checkboxes + rename scope dropdown)
7. Add all new commands to `PackageGuids.cs` (new `CmdId` constants) and `.vsct` command table (keyboard shortcuts)
8. Add all new shell files to `AkmlSql.Shell.Shared.projitems`

---

### Phase 7 — QA & Performance

1. Performance benchmarks: lightweight ops on 2,000-line document (target < 100ms); Safe Rename 1,000-line script (target < 200ms); Safe Rename 100-file directory (target < 5s)
2. Undo integration test: apply rename → verify single undo step restores document
3. False-positive sweep on test corpus (ensure refactoring doesn't corrupt valid SQL)
4. Cross-file rename with read-only file — verify skip + error report
5. Integration test on SSMS 22 and VS 2022 builds

---

## Complexity Tracking

*No constitution violations requiring justification.*
