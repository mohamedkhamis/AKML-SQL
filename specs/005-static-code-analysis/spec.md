# Feature Specification: Static Code Analysis Engine

**Feature Branch**: `005-static-code-analysis`
**Created**: 2026-03-22
**Status**: Draft
**Input**: User description: "Phase 5 Static Code Analysis — real-time SQL linter with 200+ rules across 8 categories, auto-fix actions, suppression system, bulk analysis CLI, and CI/CD integration"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Real-Time Issue Detection While Typing (Priority: P1)

A developer is writing a stored procedure in SSMS. As they type, AKML SQL continuously scans their SQL and immediately underlines problematic code with colored squiggles — a missing WHERE clause on a DELETE, a SELECT * in a stored procedure, a deprecated data type. Each squiggle shows a brief message explaining the problem and its severity. The developer can see issues the moment they introduce them, without ever leaving the editor.

**Why this priority**: Real-time feedback is the core value proposition. Without it, the feature is just a batch linter. This story delivers the "SQL has issues now" experience that makes the tool indispensable for daily use.

**Independent Test**: Can be fully tested by opening a SQL editor, typing a known-bad statement (e.g., `DELETE FROM dbo.Orders`), and verifying a colored underline and tooltip appear within one second.

**Acceptance Scenarios**:

1. **Given** a SQL editor is open with a stored procedure, **When** the user types `SELECT * FROM dbo.Orders`, **Then** a warning squiggle appears under `SELECT *` within one second with the message "Avoid SELECT * in stored procedures"
2. **Given** a rule is configured as Error severity, **When** the matching pattern appears in the editor, **Then** a red squiggle appears and the issue is added to the Error List panel
3. **Given** a rule is configured as Hint severity, **When** the matching pattern appears, **Then** a subtle underline or gutter marker appears without disrupting the editing experience
4. **Given** the user edits code to fix the issue, **When** the violation is no longer present, **Then** the squiggle disappears immediately

---

### User Story 2 - One-Click Auto-Fix (Priority: P2)

A developer sees a warning squiggle. They hover over it and a lightbulb icon appears. Clicking the lightbulb shows a menu of fix options: fix this instance, fix all occurrences in the file, suppress this rule for the line, or disable the rule globally. Selecting a fix instantly rewrites the problematic code in-place. No dialog, no confirmation — just instant transformation.

**Why this priority**: Auto-fix turns passive warnings into actionable productivity gains. Without fix actions, users see problems but must manually resolve each one. With fix, the tool does the work for them.

**Independent Test**: Can be fully tested by triggering rule BP004 (comparison with NULL) on `WHERE col = NULL`, clicking the lightbulb, selecting the fix, and verifying the editor now shows `WHERE col IS NULL`.

**Acceptance Scenarios**:

1. **Given** a fixable rule violation is underlined, **When** the user hovers over the squiggle, **Then** a lightbulb icon appears alongside the violation
2. **Given** the lightbulb is visible, **When** the user clicks it, **Then** a menu shows: fix this instance, fix all in file, suppress for this line, suppress for this file, disable rule globally
3. **Given** the user selects "Fix this instance", **When** the fix is applied, **Then** the code is rewritten correctly and the squiggle disappears, with the change undoable via Ctrl+Z
4. **Given** the user selects "Fix all in file", **When** the batch fix is applied, **Then** all matching violations in the current file are fixed in a single undoable operation
5. **Given** the user selects "Suppress for this line", **When** applied, **Then** a `-- noqa: RULEID` comment is inserted before the line and the squiggle disappears

---

### User Story 3 - Configuring Rules Per Team Standard (Priority: P3)

A lead developer wants to enforce their team's SQL standards. They open the AKML SQL Options dialog, navigate to the Code Analysis tab, and configure which rules are enabled, which are warnings vs. errors, and which are disabled entirely. They export this configuration as a CAsettings file and commit it to source control so all team members share the same rule set.

**Why this priority**: Without configuration, rules are either too noisy (flagging intentional patterns) or too permissive. Per-team configuration is what makes the tool fit real-world codebases.

**Independent Test**: Can be tested by disabling rule PE007 (cursor usage), verifying cursor code no longer shows a squiggle, then re-enabling it and verifying the squiggle returns.

**Acceptance Scenarios**:

1. **Given** the Options dialog is open on the Code Analysis tab, **When** the user disables a rule and saves, **Then** that rule no longer fires in the editor
2. **Given** a rule is configured as Error, **When** a violation is found, **Then** it appears in the VS/SSMS Error List as an error, not a warning
3. **Given** a CAsettings file exists in the project directory, **When** the editor opens a SQL file in that directory, **Then** the project-level settings override the global defaults
4. **Given** an existing SQL Prompt CAsettings XML file is detected, **When** the user imports it, **Then** all mapped rules are imported with their original severity settings

---

### User Story 4 - Bulk Analysis and Reporting (Priority: P4)

A database developer wants to audit an entire folder of SQL migration scripts before deploying to production. They use the AKML SQL menu → Run Code Analysis → Analyze Directory, select the scripts folder, and get a summary report showing total issues by category and severity. They can drill into any issue to open the exact file and line.

**Why this priority**: Single-file real-time analysis catches new issues; bulk analysis finds existing debt across a codebase. Both are needed for a complete code quality workflow.

**Independent Test**: Can be tested by running bulk analysis on a directory containing 5 SQL files with known violations and verifying the report lists the correct file names, line numbers, and rule IDs.

**Acceptance Scenarios**:

1. **Given** the user selects "Analyze Directory" from the AKML SQL menu, **When** analysis completes, **Then** a summary report shows total issues grouped by category and severity
2. **Given** a bulk analysis report is shown, **When** the user clicks an issue, **Then** the corresponding SQL file opens at the exact line of the violation
3. **Given** the CLI tool is run with `--check --severity error`, **When** any Error-severity violation exists in the target files, **Then** the process exits with code 1 (suitable for CI/CD pipelines)
4. **Given** the CLI tool is run with `--report output.json`, **When** analysis completes, **Then** a structured JSON report is written containing file, line, column, rule ID, severity, and message for every issue

---

### User Story 5 - Suppressing Rules in Code (Priority: P5)

A developer has code that intentionally uses a pattern that triggers a rule (e.g., a cursor in an ETL procedure where set-based is not viable). They add a `-- noqa: PE007` comment above the statement. The squiggle disappears for that line only. Other occurrences of cursor usage elsewhere in the file still show warnings. They can also suppress an entire block with `-- noqa-begin` / `-- noqa-end`.

**Why this priority**: Without suppression, developers either tolerate noisy false-positive warnings or disable rules entirely. Per-line suppression is the escape valve that keeps the rule set strict without frustrating legitimate exceptions.

**Independent Test**: Can be tested by adding `-- noqa: PE001` before a `SELECT *` statement and verifying the squiggle disappears only for that line while other `SELECT *` occurrences in the same file remain flagged.

**Acceptance Scenarios**:

1. **Given** a `-- noqa: RULEID` comment is on the line before a violation, **When** analysis runs, **Then** that specific violation is suppressed and no squiggle appears
2. **Given** a `-- noqa: RULEID1, RULEID2` comment is present, **When** analysis runs, **Then** both specified rules are suppressed for that line
3. **Given** code is wrapped between `-- noqa-begin` and `-- noqa-end`, **When** analysis runs, **Then** all rules are suppressed for every statement within the block
4. **Given** a suppression comment is present, **When** the suppressed rule is not violated on that line, **Then** the unused suppression comment itself is flagged as an information hint

---

### Edge Cases

- What happens when a SQL file is larger than 10,000 lines? Analysis must complete within one second without blocking the editor.
- What happens when the schema cache is not yet populated and a rule requires schema knowledge (e.g., PE001 expanding SELECT *)? The rule must either skip gracefully or show a "schema not available" hint without crashing.
- What happens when two rules both want to auto-fix the same span of text? The fix menu must present each option independently; batch fix must not apply conflicting transformations.
- What happens when a `-- noqa` comment suppresses a rule that doesn't exist? The unknown rule ID is flagged as a warning to prevent silent misconfigurations.
- What happens when the CAsettings file is malformed or inaccessible? The engine falls back to default settings and logs a warning; it never fails silently.
- What happens when analysis is running and the user types new characters? The in-progress analysis is cancelled and restarted from the current document state.

---

## Requirements *(mandatory)*

### Functional Requirements

**Analysis Engine**

- **FR-001**: The system MUST analyze SQL as the user types and update diagnostic markers within one second of the last keystroke
- **FR-002**: The system MUST analyze only the changed statement(s) when an edit occurs, not the entire file, to maintain real-time responsiveness
- **FR-003**: The system MUST support at least 200 analysis rules across 8 categories: Performance, Best Practices, Security, Style, Deprecated, Design, Execution, and Naming
- **FR-004**: Each rule MUST have a configurable severity: Error, Warning, Information, or Hint
- **FR-005**: Rules that require schema metadata MUST degrade gracefully when the schema cache is unavailable, skipping silently rather than producing false positives
- **FR-006**: The system MUST display diagnostic markers as colored underlines in the editor, styled according to severity
- **FR-007**: The system MUST populate the VS/SSMS Error List panel with all Error and Warning severity violations
- **FR-008**: The system MUST provide a master on/off switch for all code analysis

**Auto-Fix System**

- **FR-009**: Rules with auto-fix support MUST display a lightbulb icon when the cursor is on or near the violation
- **FR-010**: Each lightbulb menu MUST offer at minimum: fix this instance, fix all in file, suppress for this line, suppress for this file, and disable rule globally
- **FR-011**: All auto-fix transformations MUST be undoable via the standard editor undo mechanism
- **FR-012**: Batch fix (fix all in file) MUST apply as a single undoable operation
- **FR-013**: The system MUST cover at least 50% of all rules with at least one auto-fix action

**Suppression System**

- **FR-014**: The system MUST recognize `-- noqa: RULEID` inline comments and suppress the specified rule for that line
- **FR-015**: The system MUST recognize `-- noqa: RULEID1, RULEID2` for multi-rule suppression on a single line
- **FR-016**: The system MUST recognize `-- noqa-begin` and `-- noqa-end` block suppression markers
- **FR-017**: Suppression comments for non-existent rule IDs MUST themselves generate an Information-level diagnostic

**Rule Configuration**

- **FR-018**: Users MUST be able to enable/disable individual rules and change their severity via the Options dialog
- **FR-019**: Rule configuration MUST be exportable and importable as a CAsettings JSON file
- **FR-020**: A CAsettings file placed in a project directory MUST override global settings for SQL files in that directory
- **FR-021**: The system MUST be able to import SQL Prompt CAsettings XML files and map known rule IDs to the corresponding AKML SQL rules
- **FR-022**: Global suppressions (rules suppressed project-wide with a reason) MUST be configurable in the CAsettings file

**Bulk Analysis**

- **FR-023**: Users MUST be able to trigger bulk analysis on the current file, all open files, a selected directory (recursive), or a database project
- **FR-024**: Bulk analysis results MUST be presented in a summary view grouped by category and severity, with click-to-navigate to each issue
- **FR-025**: The CLI tool MUST support analyzing a single file, a directory, and a directory with recursive flag
- **FR-026**: The CLI tool MUST support a `--check` mode that exits with code 1 if any violation at or above the specified severity is found
- **FR-027**: The CLI tool MUST support writing a structured JSON report to a specified output file
- **FR-028**: The CLI tool MUST support a `--settings` flag to specify a CAsettings file

### Key Entities

- **Diagnostic**: A single detected violation — associated with a rule ID, severity, file, line, column, message, and zero or more fix actions
- **Rule**: A named, versioned analysis rule with a unique ID, category, default severity, description, and optional auto-fix provider
- **Fix Action**: A named code transformation attached to a diagnostic — type (transform, insert, remove, suppress), scope (instance, file, global), and the resulting text change
- **CAsettings**: A named, version-stamped configuration document listing per-rule overrides and global suppressions
- **Suppression**: An in-code comment that silences one or more rules for a line, a block, or a file

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Violations appear as editor underlines within one second of the user stopping typing on scripts up to 1,000 lines
- **SC-002**: Scripts of 10,000 lines complete full analysis within one second
- **SC-003**: At least 200 distinct rules are available across the 8 defined categories
- **SC-004**: At least 100 rules provide at least one auto-fix action
- **SC-005**: False positive rate across the full test corpus is below 5% (fewer than 5 in 100 diagnostics are incorrect on valid code)
- **SC-006**: Applying a single auto-fix completes within half a second and the change is visible immediately
- **SC-007**: Bulk analysis of 100 SQL files completes within 30 seconds
- **SC-008**: The CLI tool exits with the correct code (0 = no issues at threshold, 1 = issues found) 100% of the time in CI/CD use
- **SC-009**: At least 80% of users who enable code analysis keep it enabled after 30 days of use (adoption retention)
- **SC-010**: The full test suite (600+ tests per PRD) passes with zero failures before release

---

## Assumptions

- Schema cache (Phase 2) is available for rules that require object/column metadata; rules needing schema degrade gracefully when cache is absent
- The formatter (Phase 3) is available and used by auto-fix actions that rewrite SQL structure
- Snippet-based fix suggestions (Phase 4) are a future enhancement and out of scope for Phase 5
- The CLI tool is a separate published executable (`akmlsql-analyze.exe`), not integrated into the shell extensions
- SQL Prompt CAsettings import covers only the rule IDs listed in the PRD mapping table; unmapped rules are skipped with a logged notice
- The custom rules plugin API mentioned in the competitive comparison is out of scope for Phase 5 and targeted at a future phase
- All 6 host targets (SSMS 20/21/22, VS 2019/2022/2026) receive the same analysis capabilities; no host-specific rule exclusions
