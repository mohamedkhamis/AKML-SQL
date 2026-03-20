# Feature Specification: SQL Formatter & Code Beautifier

**Feature Branch**: `003-sql-formatter`
**Created**: 2026-03-19
**Status**: Draft
**Input**: Phase 3 PRD — AST-based SQL formatting engine with 250+ configurable options, predefined profiles, custom profiles, noformat regions, bulk formatting, CLI formatter, format-on-paste/save/delimiter, live preview profile editor, and semantic preservation validation.
**Depends on**: Phase 2 (Core IntelliSense Engine) — T-SQL parser and out-of-process engine must be complete

## Out of Scope

- AI-powered formatting suggestions (deferred to Phase 9)
- Cross-database formatting (PostgreSQL, MySQL) — T-SQL only
- Formatting of embedded SQL in application code (C#, VB)
- EditorConfig integration (deferred to Phase 8+)
- Cloud-synced team profiles via AKML Platform (future enhancement; export/import is in scope)
- Real-time collaborative formatting (multiple users editing same profile simultaneously)

## User Scenarios & Testing

### User Story 1 — One-Click Format Document (Priority: P1)

A developer opens a SQL script that contains messy, inconsistent formatting — mixed indentation, inconsistent keyword casing, no alignment. They press a keyboard shortcut and the entire document is instantly reformatted according to their active formatting profile, producing clean, readable, standardized SQL without changing any query semantics.

**Why this priority**: This is the core value proposition of the formatter. Every other feature builds on the ability to take raw SQL and produce correctly formatted output. Without this, there is no formatter.

**Independent Test**: Can be fully tested by opening any SQL file, triggering the format command, and verifying the output matches the active profile's rules while preserving query semantics.

**Acceptance Scenarios**:

1. **Given** a SQL script with inconsistent formatting is open in the editor, **When** the user presses the format shortcut (Ctrl+K, Y), **Then** the entire document is reformatted according to the active profile within 200ms for typical scripts (under 1,000 lines).
2. **Given** a formatted SQL script, **When** the user formats it again with the same profile, **Then** the output is identical to the input (idempotent formatting).
3. **Given** a SQL script with syntax errors, **When** the user triggers formatting, **Then** the formatter reformats the portions it can parse and returns the rest unchanged, never corrupting the file.
4. **Given** a SQL script containing `--noformat` / `--endnoformat` comment regions, **When** formatting is applied, **Then** the text inside noformat regions is preserved exactly as-is (whitespace, casing, alignment unchanged).
5. **Given** any valid SQL input, **When** formatting is applied, **Then** the formatted output is semantically identical to the input — it produces the same query execution plan and results.

---

### User Story 2 — Format Selection (Priority: P1)

A developer wants to format only a specific portion of a large SQL script — perhaps a single SELECT statement or a stored procedure body — without touching the rest of the file.

**Why this priority**: Selection formatting is essential for working with large files where only a portion needs cleanup. It is a core formatting action alongside full-document formatting.

**Independent Test**: Can be tested by selecting a block of SQL, triggering format selection, and verifying only the selection changes while surrounding code remains untouched.

**Acceptance Scenarios**:

1. **Given** a SQL script is open and the user selects a complete statement, **When** they press the format selection shortcut (Ctrl+K, F), **Then** only the selected text is reformatted.
2. **Given** the user selects a partial statement (e.g., just the WHERE clause), **When** they trigger format selection, **Then** the formatter expands to the nearest complete parseable unit and formats it, or formats what it can within the selection.
3. **Given** the user selects text that includes a noformat region, **When** formatting is applied, **Then** the noformat region within the selection is preserved.

---

### User Story 3 — Predefined Formatting Profiles (Priority: P1)

A developer wants to quickly switch between different formatting styles depending on the context — one style for their team's production code, another compact style for quick ad-hoc queries. The tool ships with several built-in profiles that cover common formatting preferences.

**Why this priority**: Built-in profiles provide immediate value out of the box. Users should not need to configure 250+ options before getting useful formatting. Profiles are also the foundation for custom profiles and team sharing.

**Independent Test**: Can be tested by switching between built-in profiles and verifying each produces visually distinct, correct output for the same SQL input.

**Acceptance Scenarios**:

1. **Given** the formatter is installed, **When** the user opens the profile selector, **Then** at least 5 built-in profiles are available: Default, Compact, Expanded, Leading Commas, and Minimalist.
2. **Given** the user selects a different built-in profile, **When** they format a SQL script, **Then** the output reflects the selected profile's rules and is visually distinct from other profiles.
3. **Given** a built-in profile, **When** the user attempts to modify it directly, **Then** the system prevents modification and offers to create a copy instead.
4. **Given** the user switches profiles, **When** the switch completes, **Then** the status bar indicator updates to reflect the new active profile name.

---

### User Story 4 — Custom Formatting Profiles (Priority: P2)

A developer wants to create their own formatting profile with specific preferences — perhaps leading commas, uppercase keywords, and 2-space indentation. They create a custom profile, configure the options they care about, and save it for ongoing use.

**Why this priority**: Custom profiles enable teams and individuals to enforce their specific standards. This builds on the predefined profiles foundation.

**Independent Test**: Can be tested by creating a new profile, modifying options, saving it, applying it to format SQL, and verifying the output matches the custom settings.

**Acceptance Scenarios**:

1. **Given** the user opens the profile management interface, **When** they choose to create a new profile, **Then** they can create it from scratch or copy an existing profile as a starting point.
2. **Given** the user is editing a custom profile, **When** they change any formatting option, **Then** the change is reflected in real-time in a preview pane showing before/after formatting.
3. **Given** the user has created a custom profile, **When** they export it, **Then** a portable file is generated that can be imported on another machine or shared with teammates.
4. **Given** the user receives an exported profile file, **When** they import it, **Then** the profile is added to their available profiles and can be set as active.
5. **Given** a user has multiple custom profiles, **When** they want to compare two profiles, **Then** a side-by-side diff view shows all options that differ between them.

---

### User Story 5 — 250+ Formatting Options (Priority: P2)

A developer needs granular control over every aspect of SQL formatting — comma placement, keyword casing, JOIN alignment, parenthesis positioning, indentation depth, line break rules, and more. Each option is individually configurable within a profile.

**Why this priority**: The breadth of formatting options is what makes the tool competitive with established products. Options are exercised through the profile system (US3/US4).

**Independent Test**: Can be tested by verifying each formatting option category (whitespace, casing, lists, parentheses, DML, JOINs, DDL, control flow/CASE/CTEs) independently produces the expected output when toggled.

**Acceptance Scenarios**:

1. **Given** the profile editor is open, **When** the user browses formatting options, **Then** at least 250 individually configurable options are available across 8+ categories: whitespace/indentation, casing, lists/alignment, parentheses, DML statements, JOIN clauses, DDL statements, and control flow/CASE/CTEs/expressions.
2. **Given** a formatting option is changed, **When** the user formats a SQL script, **Then** the output reflects the changed option precisely.
3. **Given** multiple options in different categories are configured, **When** formatting is applied, **Then** all options are applied consistently without conflicts.
4. **Given** a formatting option, **When** the user views it in the profile editor, **Then** a description explains what it controls, the allowed values are shown, and the default value is indicated.

---

### User Story 6 — Casing Rules with Database Sync (Priority: P2)

A developer wants consistent casing across their SQL — uppercase keywords, lowercase data types, and identifier casing that matches the actual database catalog. When connected to a database, the formatter can synchronize identifier casing with the catalog so that `customerid` becomes `CustomerID` exactly as defined in the schema.

**Why this priority**: Casing is one of the most visible formatting improvements and a top request. Database sync elevates it beyond simple rules.

**Independent Test**: Can be tested by formatting SQL with various casing rules and verifying keywords, functions, data types, and identifiers are cased correctly. Database sync can be tested with an active connection.

**Acceptance Scenarios**:

1. **Given** a casing rule is set (e.g., keywords = UPPERCASE), **When** the user formats SQL containing mixed-case keywords, **Then** all keywords are transformed to the configured case.
2. **Given** database identifier sync is enabled and a connection is active, **When** the user formats SQL containing identifiers, **Then** identifier casing matches the database catalog exactly.
3. **Given** a CamelCase dictionary is enabled, **When** the user formats identifiers like `customerorderid`, **Then** they are transformed to `CustomerOrderId` using word boundary detection.
4. **Given** no database connection is active and identifier sync is enabled, **When** the user formats SQL, **Then** identifiers are formatted using the fallback casing rule (e.g., AsIs) without error.

---

### User Story 7 — Noformat Regions (Priority: P2)

A developer has a section of SQL that must not be touched by the formatter — perhaps dynamically generated SQL, carefully hand-aligned columns, or vendor-specific syntax. They wrap it in noformat comment tags, and the formatter skips it entirely.

**Why this priority**: Noformat regions are critical for real-world adoption. Without them, users cannot adopt the formatter for files containing any hand-tuned sections.

**Independent Test**: Can be tested by placing noformat tags around SQL blocks and verifying the formatter preserves them exactly while formatting surrounding code.

**Acceptance Scenarios**:

1. **Given** a SQL script contains `--noformat` and `--endnoformat` comment tags, **When** formatting is applied, **Then** the text between the tags is preserved exactly as-is.
2. **Given** block comment syntax (`/* noformat */` ... `/* endnoformat */`) is used, **When** formatting is applied, **Then** the region is preserved identically to line comment syntax.
3. **Given** a `--noformat` tag has no matching `--endnoformat`, **When** formatting is applied, **Then** everything from the tag to the end of the file is preserved as-is.
4. **Given** noformat tags are nested, **When** formatting is applied, **Then** the entire region from the first open to the last close is preserved.

---

### User Story 8 — Standalone Format Actions (Priority: P2)

A developer wants to apply specific formatting transformations independently — apply casing only without changing layout, insert missing semicolons, expand `SELECT *` to explicit columns, add schema qualifiers, or toggle square brackets on identifiers. These actions can run standalone or be included as part of the full Format SQL command.

**Why this priority**: Standalone actions provide targeted fixes without full reformatting, which is valuable when a developer only wants one specific transformation.

**Independent Test**: Can be tested by triggering each action individually and verifying it applies only its specific transformation without side effects.

**Acceptance Scenarios**:

1. **Given** SQL with mixed-case keywords, **When** the user triggers "Apply Casing Only," **Then** casing rules are applied but layout (indentation, line breaks) is unchanged.
2. **Given** SQL statements without semicolons, **When** the user triggers "Insert Semicolons," **Then** semicolons are added at the end of each statement.
3. **Given** a `SELECT * FROM Orders` statement and an active database connection, **When** the user triggers "Expand Wildcards," **Then** `*` is replaced with the explicit column list from the schema.
4. **Given** unqualified table names (e.g., `Orders`), **When** the user triggers "Qualify Object Names," **Then** schema prefixes are added (e.g., `dbo.Orders`).
5. **Given** the user configures which actions are included in the full Format SQL command, **When** they trigger Format SQL, **Then** only the selected actions are applied.

---

### User Story 9 — Auto-Format Triggers (Priority: P3)

A developer wants formatting to happen automatically at certain points — when pasting SQL from the clipboard, when saving a .sql file, or when completing a statement (typing `;` or `GO`). These triggers reduce the need to manually invoke formatting.

**Why this priority**: Auto-format triggers are convenience features that build on the core formatting engine. They are not essential for initial adoption but improve workflow once users are comfortable with their profile.

**Independent Test**: Can be tested by enabling each trigger independently and verifying formatting is applied at the correct moment.

**Acceptance Scenarios**:

1. **Given** format-on-paste is enabled, **When** the user pastes SQL from the clipboard, **Then** the pasted content is automatically formatted according to the active profile.
2. **Given** format-on-save is enabled, **When** the user saves a .sql file, **Then** the file is formatted before saving.
3. **Given** format-on-delimiter is enabled, **When** the user types `;` or `GO`, **Then** the completed statement is automatically formatted.
4. **Given** all auto-format triggers are disabled (default), **When** the user pastes, saves, or types delimiters, **Then** no automatic formatting occurs.
5. **Given** format-on-paste is enabled and the pasted content is not SQL (e.g., C# code), **When** the paste occurs, **Then** the content is pasted without formatting.

---

### User Story 10 — Profile Editor with Live Preview (Priority: P3)

A developer wants to visually configure their formatting profile with immediate feedback. They open a split-pane editor that shows their options on the left and a live preview on the right. As they change options, the preview updates in real-time so they can see the effect before saving.

**Why this priority**: The live preview editor makes the 250+ options approachable. Without it, users would need to trial-and-error each option. It's an important UX feature but not required for core formatting to work.

**Independent Test**: Can be tested by opening the profile editor, changing options, and verifying the preview updates in real-time.

**Acceptance Scenarios**:

1. **Given** the user opens the profile editor, **When** it loads, **Then** a split-pane view shows options on the left and a before/after preview on the right.
2. **Given** the user changes any option, **When** the change is made, **Then** the preview updates within 100ms showing the effect of the change.
3. **Given** the user has an active editor document, **When** they open the profile editor, **Then** a "Your Code Preview" section shows their actual code formatted with current settings.
4. **Given** the user has made changes in the editor, **When** they click "Reset Category," **Then** all options in that category revert to the base profile's defaults.
5. **Given** the user wants to find a specific option, **When** they type in the search bar, **Then** matching options are highlighted and scrolled into view.

---

### User Story 11 — Bulk File Formatting (Priority: P3)

A team lead wants to standardize formatting across hundreds of SQL files in a project directory. They use the bulk format feature to apply their team profile to an entire directory of scripts in one operation, with a detailed report of what changed.

**Why this priority**: Bulk formatting is essential for initial adoption in teams with large existing codebases, but individual file formatting (US1) must work first.

**Independent Test**: Can be tested by running bulk format against a directory of SQL files and verifying all files are formatted correctly with a summary report.

**Acceptance Scenarios**:

1. **Given** a directory containing SQL files, **When** the user initiates bulk formatting, **Then** all .sql files (optionally recursive) are formatted with the selected profile.
2. **Given** bulk formatting is running, **When** a file has parse errors, **Then** that file is skipped (or best-effort formatted) and reported in the summary.
3. **Given** the user selects "preview mode," **When** bulk format runs, **Then** a report is generated showing what would change without modifying any files.
4. **Given** bulk formatting completes, **When** the user reviews the report, **Then** it shows: total files, files formatted, files already formatted, files with errors, total lines changed, and elapsed time.
5. **Given** the backup option is enabled, **When** bulk formatting modifies files, **Then** .bak copies of originals are created before overwriting.

---

### User Story 12 — Command-Line Formatter (Priority: P3)

A DevOps engineer wants to integrate SQL formatting validation into their CI/CD pipeline and Git pre-commit hooks. They use a CLI tool that can check if files are properly formatted (returning a non-zero exit code if not), format files in-place, or output diffs showing what would change.

**Why this priority**: CLI integration enables team-wide enforcement and automation. It depends on the core formatting engine but is a separate delivery vehicle.

**Independent Test**: Can be tested by running the CLI tool against SQL files in various modes (format, check, diff) and verifying correct output and exit codes.

**Acceptance Scenarios**:

1. **Given** a SQL file, **When** the user runs the CLI with format mode, **Then** the file is formatted in-place using the specified profile.
2. **Given** a directory of SQL files, **When** the user runs the CLI with check mode, **Then** the exit code is 0 if all files are correctly formatted, or 1 if violations are found.
3. **Given** a SQL file, **When** the user runs the CLI with diff mode, **Then** a diff showing proposed changes is printed to stdout without modifying the file.
4. **Given** the user runs the CLI with pipe mode (stdin/stdout), **When** SQL is piped in, **Then** formatted SQL is written to stdout.
5. **Given** the CLI is integrated into a Git pre-commit hook, **When** a developer commits SQL files that are not formatted, **Then** the commit is rejected with a message indicating which files need formatting.

---

### User Story 13 — SQL Prompt Profile Import (Priority: P3)

A team migrating from Redgate SQL Prompt wants to bring their existing formatting profiles to AKML SQL. They import their `.sqlpromptstyle` file, and the tool converts it to a native profile with best-effort mapping of equivalent options.

**Why this priority**: Profile import reduces migration friction for the primary competitor's user base. It's a one-time action per team and depends on the profile system (US4).

**Independent Test**: Can be tested by importing a SQL Prompt .sqlpromptstyle file and verifying the resulting profile produces similar formatting output.

**Acceptance Scenarios**:

1. **Given** a SQL Prompt `.sqlpromptstyle` file, **When** the user imports it, **Then** a native AKML SQL profile is created with equivalent options mapped.
2. **Given** the import completes, **When** options that cannot be mapped exist, **Then** the user is shown a summary of unmapped options with suggested defaults.
3. **Given** the imported profile, **When** the user formats SQL, **Then** the output is at least 90% consistent with SQL Prompt's output for the same input and profile.

---

### Edge Cases

- What happens when formatting a file larger than 50,000 lines? The formatter must complete within 2 seconds and never cause the editor to become unresponsive.
- What happens when a SQL file contains only comments? The formatter preserves them with appropriate whitespace cleanup per the active profile.
- What happens when a file contains only `GO` batch separators and empty lines? The formatter normalizes spacing per profile rules without removing content.
- What happens when the formatter engine crashes during formatting? The original document text is preserved — no changes are applied.
- What happens when a new format request arrives while one is in progress? The in-flight request is cancelled via CancellationToken, the new request runs on the latest document state.
- What happens when two formatting options conflict (e.g., collapse threshold vs. one-item-per-line)? Options have a defined precedence: explicit layout rules override collapse thresholds.
- What happens when format-on-paste receives non-SQL content? The pasted content must be detected as non-SQL (via keyword heuristic) and pasted without modification.
- What happens when formatting SQL that uses SQLCMD mode syntax (`:setvar`, `$(variable)`)? SQLCMD directives are preserved as-is; surrounding SQL is formatted normally.
- What happens when a noformat region spans a GO batch boundary? The noformat region continues across the batch boundary until the closing tag.
- What happens when the user formats SQL with an active profile that was deleted? The formatter falls back to the Default built-in profile and notifies the user.
- What happens when bulk formatting encounters a read-only file? The file is skipped and reported in the summary.

## Clarifications

### Session 2026-03-20

- Q: What file format should formatting profiles use? → A: JSON (`.akmlformat.json`)
- Q: Is the CLI formatter a separate executable or a subcommand of the existing Engine? → A: Engine subcommand (`AkmlSql.Engine.exe format`)
- Q: How should the system handle profile schema changes across versions? → A: Auto-migrate on load (new options get defaults, removed options silently dropped, version bumped on next save)
- Q: How should the system handle concurrent formatting requests on the same document? → A: Cancel-and-replace (new request cancels in-flight request, then runs)
- Q: Which SQL Prompt versions should the profile importer support? → A: All versions (v1 through latest `.sqlpromptstyle` format variants)

## Requirements

### Functional Requirements

**Core Formatting**
- **FR-001**: System MUST format an entire SQL document when the user triggers the format command, applying all rules from the active profile.
- **FR-002**: System MUST format only the selected text when the user triggers the format selection command.
- **FR-003**: System MUST preserve the semantic meaning of all SQL statements during formatting — formatted output MUST produce an identical query execution plan.
- **FR-004**: System MUST validate formatting output by comparing the parse tree of the input and output, returning the original text unchanged if validation fails.
- **FR-005**: System MUST handle SQL files with syntax errors by formatting the portions that can be parsed and preserving the rest unchanged.
- **FR-006**: System MUST produce idempotent formatting — formatting already-formatted SQL with the same profile produces identical output.

**Formatting Options**
- **FR-007**: System MUST provide at least 250 individually configurable formatting options organized into 8 categories: whitespace/indentation, casing, lists/alignment, parentheses, DML statements, JOIN clauses, DDL statements, and control flow/CASE/CTEs/expressions.
- **FR-008**: System MUST support casing rules for reserved keywords, built-in functions, data types, system objects, global variables, local variables, and identifiers with at least 5 casing modes each (UPPERCASE, lowercase, PascalCase, camelCase, AsIs).
- **FR-009**: System MUST support database identifier casing synchronization using the schema cache when an active database connection exists.
- **FR-010**: System MUST support a CamelCase dictionary for splitting compound identifiers (e.g., `customerorderid` to `CustomerOrderId`).
- **FR-011**: System MUST support configurable line-width wrapping with a user-defined maximum column width.

**Profiles**
- **FR-012**: System MUST ship with at least 5 predefined formatting profiles: Default, Compact, Expanded, Leading Commas, and Minimalist.
- **FR-013**: System MUST allow users to create, edit, duplicate, delete, export, and import custom formatting profiles.
- **FR-014**: System MUST prevent direct modification of built-in profiles while allowing them to be copied as a base for custom profiles.
- **FR-015**: System MUST store profiles as portable JSON files (`.akmlformat.json`) that can be shared between machines.
- **FR-015a**: System MUST auto-migrate profiles on load: new options receive defaults, removed options are silently dropped, and the profile schema version is bumped on next save.
- **FR-016**: System MUST support importing SQL Prompt `.sqlpromptstyle` files (all versions, v1 through latest) with best-effort option mapping.
- **FR-017**: System MUST support side-by-side comparison of two profiles showing all differing options.
- **FR-018**: System MUST allow quick profile switching via a toolbar dropdown with status bar indication of the active profile.

**Noformat Regions**
- **FR-019**: System MUST support `--noformat` / `--endnoformat` line comment tags to exclude code from formatting.
- **FR-020**: System MUST support `/* noformat */` / `/* endnoformat */` block comment tags with the same behavior.
- **FR-021**: System MUST treat noformat tags as case-insensitive.
- **FR-022**: System MUST preserve all text inside noformat regions exactly as-is, including whitespace and casing.
- **FR-023**: System MUST treat an unmatched `--noformat` (no closing tag) as preserving the rest of the file.

**Standalone Actions**
- **FR-024**: System MUST support "Apply Casing Only" as a standalone action that changes keyword/identifier case without altering layout.
- **FR-025**: System MUST support "Insert Semicolons" to add missing statement terminators.
- **FR-026**: System MUST support "Expand Wildcards" to replace `SELECT *` with explicit column lists (requires active database connection).
- **FR-027**: System MUST support "Qualify Object Names" to add schema prefixes to unqualified object references (requires active database connection).
- **FR-028**: System MUST support "Add/Remove Square Brackets" to toggle bracket quoting on identifiers.
- **FR-029**: System MUST support "Add/Remove AS Keyword" to toggle the AS keyword on alias definitions.
- **FR-030**: System MUST allow users to configure which standalone actions are included when running the full Format SQL command.

**Auto-Format Triggers**
- **FR-031**: System MUST support format-on-paste that automatically formats SQL content pasted from the clipboard.
- **FR-032**: System MUST support format-on-save that automatically formats .sql files when saved.
- **FR-033**: System MUST support format-on-delimiter that formats the completed statement when `;` or `GO` is typed.
- **FR-034**: All auto-format triggers MUST be disabled by default and individually toggleable.
- **FR-035**: Format-on-paste MUST detect non-SQL content and skip formatting for non-SQL pastes.

**Profile Editor**
- **FR-036**: System MUST provide a visual profile editor with a split-pane layout: options on the left, live preview on the right.
- **FR-037**: The profile editor MUST update the preview within 100ms of any option change.
- **FR-038**: The profile editor MUST include a search function to find options by name or keyword.
- **FR-039**: The profile editor MUST support per-category reset and full reset to base profile defaults.
- **FR-040**: The profile editor MUST support undo/redo within the editing session.

**Bulk Formatting**
- **FR-041**: System MUST support bulk formatting of all SQL files in a directory (with optional recursion).
- **FR-042**: System MUST support a preview mode for bulk formatting that reports what would change without modifying files.
- **FR-043**: System MUST generate a summary report after bulk formatting showing files processed, changed, skipped, and errored.
- **FR-044**: System MUST support creating backup copies of original files before bulk modification.
- **FR-045**: System MUST skip read-only files during bulk formatting and report them.

**Command-Line Formatter**
- **FR-046**: System MUST provide CLI formatting as a subcommand of the existing Engine (`AkmlSql.Engine.exe format`), sharing the same formatting engine binary used by the IDE extension.
- **FR-047**: The CLI MUST support format mode (modify files in-place), check mode (validate formatting, exit code 0/1), and diff mode (show proposed changes).
- **FR-048**: The CLI MUST support pipe mode (read from stdin, write to stdout).
- **FR-049**: The CLI MUST support profile selection by name or by file path.
- **FR-050**: The CLI MUST support recursive directory formatting with file pattern filtering.
- **FR-051**: The CLI MUST provide well-defined exit codes: 0 (success), 1 (formatting violations), 2 (parse error), 3 (file not found), 4 (invalid profile), 5 (internal error).
- **FR-052**: The CLI MUST support generating a JSON report for bulk operations.

**SQL Syntax Coverage**
- **FR-053**: System MUST support formatting all standard T-SQL DML statements (SELECT, INSERT, UPDATE, DELETE, MERGE, TRUNCATE).
- **FR-054**: System MUST support formatting all standard T-SQL DDL statements (CREATE/ALTER/DROP for TABLE, VIEW, PROCEDURE, FUNCTION, INDEX, TRIGGER, SCHEMA).
- **FR-055**: System MUST support formatting JOINs (INNER, LEFT/RIGHT/FULL OUTER, CROSS, CROSS APPLY, OUTER APPLY).
- **FR-056**: System MUST support formatting CTEs (WITH...AS), including recursive CTEs.
- **FR-057**: System MUST support formatting window functions (OVER with PARTITION BY, ORDER BY, ROWS/RANGE).
- **FR-058**: System MUST support formatting CASE expressions (simple and searched).
- **FR-059**: System MUST support formatting control flow (IF/ELSE, WHILE, TRY/CATCH, BEGIN/END).
- **FR-060**: System MUST preserve SQLCMD directives (`:setvar`, `:connect`, `$(variable)`) as-is during formatting.
- **FR-061**: System MUST preserve all comments (`--` and `/* */`) in their correct positions relative to the code they annotate.
- **FR-062**: System MUST support SQL Server 2016 through 2025 syntax, Azure SQL Database, Azure SQL Managed Instance, and Microsoft Fabric.

**Integration**
- **FR-063**: System MUST work in SSMS 20, SSMS 21, SSMS 22, VS 2019, VS 2022, and VS 2026.
- **FR-064**: The profile editor and preview MUST follow the host IDE's visual theme (Light/Dark).
- **FR-065**: All keyboard shortcuts MUST be configurable to avoid conflicts with other extensions.
- **FR-066**: The formatter MUST reuse the existing Phase 2 out-of-process engine and named pipe communication channel.

### Key Entities

- **Formatting Profile**: A named collection of 250+ formatting option values that defines a complete formatting style. Has metadata (name, description, author, version, creation/modification dates, base profile) and option values organized by category. Can be built-in (read-only) or custom (user-editable). Stored as portable JSON files (`.akmlformat.json`) using System.Text.Json serialization.
- **Formatting Option**: A single configurable setting within a profile. Has a category, name, description, default value, allowed values, and current value. Organized into 8 categories.
- **Noformat Region**: A section of SQL text delimited by comment-based tags that the formatter must preserve exactly as-is. Defined by an opening tag and optional closing tag.
- **Format Action**: A discrete formatting transformation that can be applied independently or as part of the full format command. Each action has a type (layout, casing, semicolons, wildcards, qualification, brackets, AS keyword), a keyboard shortcut, and a toggle for inclusion in the full format.
- **Bulk Format Report**: A summary document generated after bulk formatting operations. Contains file-level details (path, status, lines changed, errors) and aggregate statistics (total files, formatted count, error count, elapsed time).
- **Format Result**: The output of a formatting operation. Contains the formatted text, a success/failure indicator, and diagnostics if formatting was partial or failed.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Users can format a typical SQL script (under 1,000 lines) within 200ms of pressing the keyboard shortcut — indistinguishable from instant.
- **SC-002**: Users can format a large SQL script (10,000 lines) within 500ms.
- **SC-003**: 100% of formatted outputs are semantically identical to their inputs — zero cases where formatting changes query behavior.
- **SC-004**: At least 250 individually configurable formatting options are available across 8+ categories.
- **SC-005**: At least 5 built-in formatting profiles produce visually distinct, correct output.
- **SC-006**: Users can create, export, import, and switch between custom profiles without restarting the IDE.
- **SC-007**: The CLI formatter validates formatting in a CI/CD pipeline, returning correct exit codes in under 200ms per file.
- **SC-008**: Bulk formatting processes at least 50 files per second (500-line files).
- **SC-009**: Over 90% of SQL Prompt formatting options are correctly mapped during profile import.
- **SC-010**: The profile editor preview updates within 100ms of any option change.
- **SC-011**: The formatter adds no more than 20MB of additional memory beyond the Phase 2 engine baseline.
- **SC-012**: Formatting works identically across all 6 supported IDE targets (SSMS 20/21/22, VS 2019/2022/2026).
- **SC-013**: Noformat regions preserve 100% of their content byte-for-byte after formatting.
- **SC-014**: Over 90% of beta testers rate the formatting output as "equal or better than" competing products.

## Assumptions

- Phase 2 (Core IntelliSense Engine) is complete, providing the T-SQL parser, out-of-process engine infrastructure, named pipe communication, and schema cache.
- The formatter operates within the existing Phase 2 out-of-process engine — no additional process is spawned.
- The T-SQL parser (ScriptDom) can produce an AST for all supported SQL Server syntax versions.
- Built-in profiles are bundled with the installer and deployed alongside the extension.
- Custom profiles are stored in the user's application data directory.
- The CLI formatter is a subcommand of the existing Engine executable (`AkmlSql.Engine.exe format`) and does not require the IDE to be running.
- Database identifier casing sync uses the Phase 2 schema cache (already loaded in memory), not live database queries during formatting.
- Format-on-paste SQL detection uses a lightweight heuristic (keyword presence in first 200 characters), not a full parse attempt.
- Keyboard shortcuts use the Ctrl+K chord prefix to align with Visual Studio conventions and minimize conflicts.
