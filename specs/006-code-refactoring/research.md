# Research: Code Refactoring Toolkit

**Branch**: `006-code-refactoring` | **Date**: 2026-03-23

---

## R-001: Lightweight Refactoring — Reuse FormatAction or New Message Type?

**Question**: Should the 15 lightweight refactoring operations be dispatched through the existing `FormatAction` (13) / `FormatActionResult` (113) message pair, or through new dedicated message types?

**Decision**: **Reuse `FormatAction` / `FormatActionResult`** — add new `FormatActionType` enum values (8–15) for the new lightweight operations.

**Rationale**:
- `FormatAction` is already stubbed in `FormatRequestHandler.HandleFormatAction()` and explicitly left open for "Phase 9" fill-in; using it avoids protocol churn
- The shell-side command infrastructure (5 existing commands: InsertSemicolons=1, ExpandWildcards=3, QualifyNames=4, ToggleBrackets=5, ToggleAs=7) is identical in structure to all new lightweight commands — same `FormatActionRequest` → `FormatActionResponse` pattern, same `FormatActionHelper.ApplyFormattedText()` to write results back to the buffer
- Existing enum values cover 0–7; new operations occupy 8–15 with no collision

**New `FormatActionType` values**:
| Value | Operation |
|-------|-----------|
| 8  | RemoveSemicolons |
| 9  | ExpandInsertColumns |
| 10 | ExpandExecParameters |
| 11 | ExpandUpdateColumns |
| 12 | ConvertOldStyleJoins |
| 13 | AddGroupByColumns |
| 14 | EncapsulateBeginEnd |
| 15 | ReplaceDeprecatedSyntax |

**Alternatives Considered**:
- *New dedicated message types per operation* — unnecessary protocol complexity; the request/response shape is identical to FormatAction
- *Handle in shell only, no engine round-trip* — insufficient; wildcard expansion, INSERT column list, and EXEC parameter expansion all require schema cache data held by the engine

---

## R-002: Heavyweight Refactoring — Message Protocol Design

**Question**: How should preview-then-apply heavyweight refactoring (Safe Rename, Extract to CTE, etc.) flow over the IPC protocol?

**Decision**: **Two-phase protocol** — `RequestRefactorPreview` (30) → `RefactorPreviewResult` (130), then optionally `RequestRefactorApply` (31) → `RefactorApplyResult` (131).

**Rationale**:
- Preview and apply are separate user actions with a dialog between them; two message types maps naturally to two IPC calls
- The preview response returns a `RefactorChangeInfo[]` — a flat list of file+span+old+new tuples — which the shell renders in the preview dialog. The user selects which changes to apply, and the apply request sends back only the approved subset
- No persistent server-side state between preview and apply; the changes are round-tripped in the apply request payload
- Available IPC range: requests 27–99, responses 126–200 (verified from existing MessageTypes.cs)

**Message flow**:
```
Shell                                Engine
  │── RequestRefactorPreview (30) ──▶│  Parse + collect all references
  │◀── RefactorPreviewResult (130) ──│  Returns RefactorChangeInfo[] + warnings
  │                                  │
  │  [user reviews dialog, selects]  │
  │                                  │
  │── RequestRefactorApply (31) ────▶│  Apply selected changes (text replacements)
  │◀── RefactorApplyResult (131) ────│  Success/failure per file
```

**Alternatives Considered**:
- *Single request that previews and applies* — violates the spec's preview-confirm-apply principle; no way to let the user cancel mid-preview
- *Persistent session state on engine* — adds stateful complexity and risks stale state if shell crashes between preview and apply

---

## R-003: Safe Rename — Identifier Reference Collection Strategy

**Question**: How should all references to a renamed identifier be located within a TSqlScript?

**Decision**: **`TSqlFragmentVisitor`-based `ReferenceCollector`** that visits all relevant node types: `ColumnReferenceExpression`, `NamedTableReference`, `SchemaObjectName`, `ProcedureReference`, `VariableReference`, and bare `Identifier` in procedure/function parameter lists.

**Rationale**:
- The existing `AliasResolver`, `CteResolver`, `VariableTracker`, and `TempTableTracker` all use the same `TSqlFragmentVisitor` pattern and visit overlapping node types — the rename collector is a natural extension of this established pattern
- `TSqlFragment.StartOffset` + `TSqlFragment.FragmentLength` give exact character ranges for text replacement without needing a secondary position calculation step
- Case-insensitive `OrdinalIgnoreCase` comparison is established throughout the codebase

**Scope-limiting**: The collector accepts a scope parameter (current statement / current script / cross-file). For cross-file, the engine re-parses each file independently and runs the collector on each `TSqlScript`; results are merged into a single `RefactorChangeInfo[]` sorted by file + offset.

**Identifier disambiguation**: When renaming a column named `Id`, the collector must distinguish `Orders.Id` from `Customers.Id`. For script-level rename (no live schema), disambiguation is based on alias resolution context from `AliasResolver` output. For cross-file rename, the scope is the full identifier including schema-qualified table name when available.

---

## R-004: Extract to CTE — AST Reconstruction vs. Text Manipulation

**Question**: Should "Extract to CTE" reconstruct the SQL text via ScriptDom's `SqlScriptGenerator` (AST → text), or use offset-based text splicing?

**Decision**: **Offset-based text splicing** for extraction operations, using the existing `FormatterPipeline`'s text emitter patterns where applicable.

**Rationale**:
- `SqlScriptGenerator` reformats the entire statement, potentially changing whitespace, keyword casing, and comment placement in ways the user didn't request
- Offset splicing preserves the original formatting of the extracted block — only the structural wrapper (WITH CTE AS (...)) and the replacement reference are generated as new text
- The Phase 3 formatter's `TextEmitter` already uses this approach: it works on positioned text ranges rather than re-emitting the full AST
- The `CteResolver` already infers column names from `SelectElements` — this logic is directly reusable for naming the CTE columns

**CTE column inference** (reusing `CteResolver` patterns):
1. Scan `QuerySpecification.SelectElements` for `SelectScalarExpression` with `.ColumnName` alias → use alias
2. For `ColumnReferenceExpression` without alias → use last part of `MultiPartIdentifier`
3. For unresolvable expressions → generate `Col1`, `Col2`, ... placeholders

---

## R-005: Extract to Stored Procedure — Parameter Detection

**Question**: How should variables used in a selected code block be detected and converted to procedure parameters?

**Decision**: **Walk `@variable` references** within the selected AST subtree using a `VariableReferenceVisitor`, then cross-reference against `DECLARE @var` statements in the outer scope to distinguish parameters (declared outside) from local variables (declared inside).

**Rationale**:
- `VariableTracker` already visits `DeclareVariableElement` nodes to collect known variables with their types; the same pattern extended to `VariableReference` nodes gives the full reference set
- Variables declared outside the selected block = input parameters to the new procedure
- Variables declared inside the selected block = local variables (preserved in the procedure body)
- Output detection: variables assigned inside the block AND used after the block = OUTPUT parameters (shown in wizard for user confirmation)

---

## R-006: Convert Old-Style JOINs — Ambiguous WHERE Conditions

**Question**: When converting comma-separated FROM to ANSI JOIN, how should WHERE clause conditions be handled?

**Decision**: **Heuristic split** — equi-join conditions (`t1.col = t2.col`) move to ON clauses; non-equi conditions (range, LIKE, IS NULL, function calls) stay in WHERE. Ambiguous cases (ORs spanning multiple tables) are flagged as warnings in the preview and left in WHERE.

**Rationale**:
- Pure equi-joins (`WHERE t1.id = t2.id`) are unambiguous — they are the join predicate by SQL convention
- Non-equi conditions are filter conditions, not join predicates, and belong in WHERE in ANSI syntax
- The `ST003_OldStyleJoin` analysis rule (Phase 5) already detects the pattern; the refactoring reuses the same detection (`FromClause.TableReferences` with no `JoinTableReference`) and adds the transformation pass

**Semantics preservation guarantee**: INNER JOIN with equi-predicate in ON is semantically identical to comma + WHERE. The preview diff shows the before/after for user confirmation, and the warning message explains any conditions that were left in WHERE as non-equi filters.

---

## R-007: Refactoring Settings Schema

**Question**: Should refactoring settings be stored in the existing `config.json` (`AppSettings`) or in a separate file?

**Decision**: **Add `RefactoringSettings` to `AppSettings`** (same `config.json`), following the identical pattern used for `CodeAnalysisSettings` in Phase 5.

**Rationale**:
- All feature settings (IntelliSense, Formatter, Snippets, CodeAnalysis) live in `AppSettings` — consistency is more important than separation
- Refactoring settings have no per-project or per-directory override need (unlike `.casettings`); global user settings are sufficient

**Settings properties**:
- `previewBeforeApply` (default: true)
- `createBackups` (default: true)
- `formatAfterRefactor` (default: true)
- `renameScope` (default: "currentScript"; values: currentScript, projectDirectory)
- `includeCommentsInRename` (default: true)
- `includeStringLiteralsInRename` (default: false)

---

## R-008: Undo Integration

**Question**: How should undo work for refactoring operations that modify the current document?

**Decision**: **Single undo step** — each refactoring operation wraps all its `ITextBuffer.Replace` calls in a single `ITextEdit` transaction. The VS text editing infrastructure automatically creates a single undo entry for each committed `ITextEdit`.

**Rationale**:
- The existing `FixAction.Invoke()` (Phase 5) already uses this pattern: `_buffer.CreateEdit()` → `edit.Replace(...)` → `edit.Apply()` — one edit, one undo step
- Multi-span refactoring (e.g., rename 8 references) must apply all replacements in reverse offset order within a single `ITextEdit` to avoid offset shifting, then commit once

**Cross-file undo**: Changes applied to files other than the current document cannot be undone via `Ctrl+Z` — the backups serve as the rollback mechanism. This is the same behaviour as other SSMS tools (SQL Prompt, ApexSQL Refactor) and is documented in the preview dialog.
