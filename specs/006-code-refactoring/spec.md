# Feature Specification: Code Refactoring Toolkit

**Feature Branch**: `006-code-refactoring`
**Created**: 2026-03-23
**Status**: Draft
**Input**: Phase 6 PRD — AKML SQL Code Refactoring

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Instant Inline Refactoring (Priority: P1)

A developer working in SSMS or Visual Studio selects a code issue (e.g. `SELECT *`, an unqualified object name, missing semicolons, old-style JOIN syntax) and applies a one-click transformation to fix it without leaving the editor. The change is instant — no wizard, no dialog — and the result is ready to execute.

**Why this priority**: These 15 lightweight operations cover the most common daily friction points. They build on existing formatter infrastructure, are independently deliverable, and provide immediate value without cross-object analysis.

**Independent Test**: Can be fully tested by opening any SQL file, triggering a refactoring command (keyboard shortcut or menu), and verifying the document changes correctly in under 100ms.

**Acceptance Scenarios**:

1. **Given** a query containing `SELECT *`, **When** the user invokes "Expand wildcards", **Then** all `*` are replaced with the explicit column list from the schema cache.
2. **Given** an unqualified table reference `Orders`, **When** the user invokes "Qualify object names", **Then** the reference becomes `dbo.Orders`.
3. **Given** aliases written without the `AS` keyword, **When** the user invokes "Add AS keyword", **Then** all alias definitions include `AS`.
4. **Given** a query with comma-separated FROM clause table references (old-style JOIN), **When** the user invokes "Convert old-style JOINs", **Then** the query uses ANSI JOIN syntax with correct join types preserved.
5. **Given** an INSERT statement without a column list, **When** the user invokes "Expand INSERT columns", **Then** all target column names are added.
6. **Given** a SELECT with non-aggregated columns and no GROUP BY, **When** the user invokes "Add non-aggregated to GROUP BY", **Then** a complete GROUP BY clause is appended.
7. **Given** a deprecated construct flagged by the analysis engine, **When** the user invokes "Replace deprecated syntax", **Then** the construct is updated to the modern equivalent.

---

### User Story 2 - Safe Rename with Preview (Priority: P2)

A developer needs to rename a table column, stored procedure, variable, or alias. They invoke Safe Rename, see a preview of every affected location (grouped by file), can uncheck individual files or references, and then apply the rename atomically. No reference is missed; no silent change happens without confirmation.

**Why this priority**: Rename is the highest-value refactoring operation and the one most likely to cause regressions if done manually with Find & Replace. The preview-confirm-apply pattern is essential for user trust.

**Independent Test**: Can be fully tested by renaming a column within a single script, verifying the preview lists all occurrences, and confirming the applied result matches the preview exactly.

**Acceptance Scenarios**:

1. **Given** a column name used in 8 locations in the current script, **When** the user invokes Safe Rename and enters a new name, **Then** a preview dialog lists all 8 locations with before/after diffs.
2. **Given** the preview dialog, **When** the user unchecks 2 of 8 locations, **Then** only the 6 checked locations are updated when Apply is clicked.
3. **Given** a rename across 12 files in a project directory (all `.sql` files found recursively under the directory of the current file), **When** applied, **Then** all references are updated and each original file is backed up before modification.
4. **Given** a rename that would produce a name collision with an existing identifier, **When** the user clicks Apply, **Then** a warning is shown and the rename is blocked until the conflict is resolved.
5. **Given** the user clicks Cancel at any point in the rename flow, **Then** no changes are made to any file.

---

### User Story 3 - Extract to Named Unit (Priority: P3)

A developer selects a block of SQL code and extracts it into a new named unit — a stored procedure (with auto-generated parameters), a named CTE, a derived table, or a view. A wizard shows a preview of the new object and the updated original query before applying any change.

**Why this priority**: These heavyweight operations require multi-step analysis and a wizard UI, making them more complex than Stories 1–2. They are high value for large codebases but depend on the inline and rename infrastructure.

**Independent Test**: Can be fully tested by selecting a subquery, invoking "Extract to CTE", and verifying the result replaces the subquery with a CTE reference at the top of the statement.

**Acceptance Scenarios**:

1. **Given** a selected block of SQL, **When** the user invokes "Extract to stored procedure", **Then** a wizard shows the proposed procedure name, auto-detected parameters, and the updated call site before applying.
2. **Given** a subquery in a FROM clause, **When** the user invokes "Extract to CTE", **Then** the subquery is replaced with a CTE reference and the CTE is added at the top of the query.
3. **Given** a SELECT query, **When** the user invokes "Encapsulate as view", **Then** a CREATE VIEW statement is generated and the original query is replaced with a SELECT from the new view.
4. **Given** any extract wizard, **When** the user clicks Cancel, **Then** no changes are made.

---

### User Story 4 - Temp Table / Table Variable Conversion and Parameterization (Priority: P4)

A developer converts a `#temp` table to a `@table` variable (or vice versa) with a single command, receiving a warning about behavioral differences. They can also replace hard-coded literal values in a query with declared variables in one operation.

**Why this priority**: These operations complete the refactoring toolkit and are useful for performance tuning and code clarity, but are more niche than Stories 1–3 and do not block them.

**Independent Test**: Can be fully tested by running "Convert temp table to table variable" on a script using `#TempOrders` and verifying the result uses `@TempOrders` with the correct table variable declaration.

**Acceptance Scenarios**:

1. **Given** a script using `#TempOrders`, **When** the user invokes "Convert temp table to table variable", **Then** all references become `@TempOrders` and a warning about statistics differences is displayed.
2. **Given** a query containing literal values such as `'2024-01-01'` and `42`, **When** the user invokes "Parameterize literal values", **Then** the literals are replaced with declared variables at the top of the batch with inferred data types.

---

### Edge Cases

- What happens when `SELECT *` expansion cannot resolve columns (table not in schema cache)? → The operation skips that reference and notifies the user; other references in the same document are still expanded.
- What happens when Safe Rename finds a name collision? → A blocking warning is shown; the rename is not applied until the user resolves the conflict or cancels.
- What happens when a file is read-only or locked during cross-file rename? → That file is skipped and reported in an error summary; all other files are still processed.
- What happens when a target file has changed on disk between when the preview was computed and when Apply is executed? → That file is skipped (reported in `FailedFilePaths`) to prevent offset-corruption; all unchanged files are still processed. The user must re-run the preview for the skipped files.
- What happens when the user undoes a refactoring immediately after applying it? → Standard editor undo reverts the change completely as a single undo step (in-document operations only).
- What happens when a cross-file refactoring (e.g., Safe Rename across 12 files) has been applied and the user wants to undo it? → Backup files (`.refactor-backup`) are the official recovery mechanism; editor undo covers the current document buffer only and cannot span disk I/O across other files.
- What happens when "Extract to stored procedure" finds ambiguous parameter types (same variable used with multiple types)? → The wizard prompts the user to confirm or adjust parameter types before applying.
- What happens when the code selected for extraction is not a valid standalone statement? → An error message explains why extraction cannot proceed; no change is made.
- What happens when "Convert old-style JOINs" encounters a non-equi-join condition in the WHERE clause? → The ambiguous join condition is left unchanged in the output and a warning message is returned listing the skipped condition (same pattern as schema-cache miss warnings). The rest of the document is still converted.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Users MUST be able to apply any of the 15 lightweight refactoring operations from a keyboard shortcut or editor context menu without leaving the editor.
- **FR-002**: Every lightweight refactoring MUST complete and update the document within 100ms on documents up to 2,000 lines.
- **FR-003**: Safe Rename MUST present a preview dialog listing all affected locations grouped by file, with before/after diff context, before applying any change.
- **FR-004**: Safe Rename MUST support at minimum two scopes: current script and project/directory. "Project/directory" scope is defined as all `.sql` files found recursively under the directory containing the current file; no solution or project file is required.
- **FR-005**: Users MUST be able to selectively include or exclude individual files and references in the rename preview before applying.
- **FR-006**: Every heavyweight refactoring MUST follow the preview-confirm-apply pattern; no cross-file modification may occur without explicit user confirmation.
- **FR-007**: Wildcard expansion MUST use the connected schema cache to resolve column names and report unresolvable references without aborting the operation.
- **FR-008**: All operations that modify external files MUST create a backup of each file before modification when backups are enabled in settings. Backup files are the official recovery mechanism for cross-file operations (editor undo does not cover cross-file disk modifications).
- **FR-009**: Every in-document refactoring operation MUST be reversible via a single standard editor undo action. This requirement applies exclusively to changes made within the current editor buffer; cross-file changes are recovered via FR-008 backups.
- **FR-010**: "Extract to stored procedure" MUST auto-detect variables referenced in the selected block and generate them as typed parameters in the wizard.
- **FR-011**: "Convert old-style JOINs" MUST preserve query semantics (INNER, LEFT, RIGHT). Ambiguous non-equi-join conditions MUST be left unchanged in the output and reported as warnings (the same warning channel used for schema-cache misses); the remainder of the document is still converted.
- **FR-012**: "Parameterize literal values" MUST generate variable declarations at the top of the batch with inferred data types.
- **FR-013**: Users MUST be able to configure refactoring behavior via the Options dialog. The `previewBeforeApply` setting applies exclusively to heavyweight operations (Safe Rename, Extract operations, conversions); lightweight operations are always instant and are not affected by this setting. Other configurable settings: backup creation, format after refactor, default rename scope, include comments in rename, include string literals in rename.
- **FR-014**: Safe Rename MUST warn the user before applying when the new name already exists in the current scope.
- **FR-015**: All refactoring operations MUST be available from the AKML SQL top-level menu in addition to keyboard shortcuts.

### Key Entities

- **Refactoring Operation**: A named transformation with a type (lightweight or heavyweight), a keyboard shortcut, a scope, and a preview/apply behavior.
- **Rename Reference**: A single occurrence of an identifier at a specific file path, line, and column, with surrounding context used to generate the diff preview.
- **Refactoring Preview**: The complete set of proposed changes across all affected locations, presented to the user for selective approval before any file is modified.
- **Refactoring Settings**: User configuration controlling preview behavior, backup creation, post-apply formatting, default rename scope, and inclusion of comments/string literals in rename operations.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All 15 lightweight refactoring operations complete within 100ms on documents up to 2,000 lines.
- **SC-002**: Safe Rename within a single script of 1,000 lines completes and displays the preview in under 200ms.
- **SC-003**: Safe Rename across 100 files displays the preview in under 5 seconds.
- **SC-004**: Extract-to-procedure wizard generates the preview in under 500ms for any selected block.
- **SC-005**: Zero unconfirmed cross-file changes — every file-level modification requires explicit user approval in the preview dialog.
- **SC-006**: Undo successfully reverts any in-document refactoring in 100% of cases.
- **SC-007**: Wildcard expansion produces the correct column list for 100% of tables present in the schema cache.
- **SC-008**: Users can complete a Safe Rename across the current script in 3 interactions or fewer: invoke → review preview → apply.

## Scope

### In Scope

- 15 lightweight refactoring operations (inline, instant, keyboard-shortcut driven)
- Safe Rename at current-script scope and project/directory scope
- Extract to stored procedure, CTE, derived table, and view (wizard-based)
- Temp table ↔ table variable conversion
- Parameterize literal values
- Preview dialog for all heavyweight operations with selective include/exclude
- Keyboard shortcuts and context menu entries for all operations
- Refactoring settings in the Options dialog
- File backup before cross-file modifications

### Out of Scope

- Safe Rename at database scope (generating `sp_rename` DDL against a live server) — deferred
- "Split table" (generating ALTER TABLE scripts) — deferred
- "Move to new query window" — deferred
- Refactoring across version-controlled branches or remote repositories

## Dependencies

- Phase 2 (IntelliSense Engine) — schema cache required for wildcard expansion, object qualification, and parameter type inference
- Phase 3 (SQL Formatter) — required for "format after refactor" option; some lightweight operations (AS keyword, brackets, semicolons) reuse formatter transformations
- Phase 5 (Static Code Analysis) — required for "Replace deprecated syntax" (reads flagged issues to target specific constructs)

## Clarifications

### Session 2026-03-23

- Q: When a cross-file refactoring has been applied, how does the user undo it? → A: Backup files (`.refactor-backup`) are the official recovery mechanism for cross-file changes; editor undo covers in-document changes only (FR-009 is intentionally scoped to in-document operations).
- Q: When scope is "project/directory", which files are included in the rename search? → A: All `.sql` files found recursively under the directory containing the current file.
- Q: Does `previewBeforeApply` apply to lightweight operations? → A: No — `previewBeforeApply` applies only to heavyweight operations; lightweight ops are always instant regardless of the setting.
- Q: When "Convert old-style JOINs" encounters an ambiguous non-equi condition, what does "flags for manual review" mean in the UI? → A: The ambiguous join is left unchanged in the output and a warning message is returned listing the skipped condition (same pattern as schema-cache miss warnings).
- Q: If a target file has changed on disk between preview and apply, what should the engine do? → A: Skip files that have changed since preview was computed; report them in `FailedFilePaths` and continue applying to unchanged files.

## Assumptions

- Schema cache is populated before wildcard expansion or object qualification is invoked; if not, unresolvable references are skipped and reported rather than silently left unchanged.
- "Format after refactor" applies the user's active formatter profile, not a fixed built-in style.
- Default keyboard shortcuts (`Ctrl+B, W` etc.) can be reassigned by the user through the host IDE's standard keybinding settings.
- Cross-file rename backups are stored in a `.refactor-backup` subfolder adjacent to each modified file.
- The preview dialog renders diffs using a unified diff style (− / + lines) consistent with the Phase 3 bulk format preview dialog.
- Lightweight refactoring operations act on the full document by default; if text is selected, they act only on the selection.
