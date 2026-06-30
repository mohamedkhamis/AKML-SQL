# Feature Specification: SQL Prompt Parity Gap Closure (excluding AI & licensing)

**Feature Branch**: `030-sqlprompt-parity-closure`  
**Created**: 2026-06-07  
**Status**: Draft  
**Input**: User description: "Close the Redgate SQL Prompt 11 feature-parity gaps found in the audit at `doc/_Prompt-Gap/`, excluding all AI features (file 07) and all licensing/edition-tier work (file 09 §2). Parity bar = desktop SSMS 22 + VS 2026."

## Context

A feature-by-feature audit of AKML SQL against Redgate SQL Prompt 11 (recorded in `doc/_Prompt-Gap/`, scorecard in `00-INDEX-and-Questions.md`) scored 324 features: **✅103 at parity · 🟡112 partial · ❌87 missing · ➖22 out-of-scope**. This feature closes the in-scope **🟡 partial** and **❌ missing** rows across IntelliSense (01), Formatting (02), Refactoring (03), Code Analysis (04), Snippets (05), Tab Management & SQL History (06), Options (08), and the non-licensing rows of Platform (09).

The audit's central, recurring finding is **"built but not wired"**: a large share of the gaps are capabilities that already exist in code but are unreachable to the user (e.g. formatting layout rules the format pipeline never runs, format actions the engine never dispatches, per-project analysis settings the live editor ignores, hover/signature surfaces that only log, snippet expansion that works only in the Web edition). Closing these is primarily *finishing and connecting* existing capability so it reaches the user — high leverage, lower effort than greenfield. The plan phase will size each item against the per-row code evidence in the audit.

## Clarifications

### Session 2026-06-07

- Q: How should this parity-closure (8 user stories) be planned and delivered? → A: As a **single feature** (030), planned and shipped in **phases by priority** (P1 → P2 → P3); not split into separate specs.
- Q: How far should Smart Rename (FR-018) propagate a rename? → A: **Database-wide** — rename the object and rewrite all referencing objects across the database, shown as a reviewable script the user approves before it is applied (true SQL Prompt parity).
- Q: Should this feature hold performance budgets as acceptance criteria? → A: **Yes** — hold existing latency budgets as non-regression success criteria (code completion p95 < 100 ms; Format SQL < 200 ms on typical scripts; large scripts never block the IDE UI).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Format SQL with full style fidelity (Priority: P1)

A developer selects a formatting style (built-in or custom), sets its options, and runs **Format SQL**. The formatted output reflects **every** option the style defines — CASE/CTE layout, DDL alignment, DML statement layout, list/comma style, alignment, parentheses, and line wrapping — with no setting silently ignored. The developer can also run individual formatting transformations on demand.

**Why this priority**: Formatting is SQL Prompt's most-used capability and the single largest cluster of partial/missing parity. Today many style settings have no visible effect, so output doesn't match the chosen style — the highest-leverage fix and a viable standalone MVP.

**Independent Test**: Choose a style, enable representative CASE/CTE/JOIN/DDL/list options plus a maximum line width, format a representative script, and confirm the output matches each option; then run each standalone action and confirm only that transformation is applied.

**Acceptance Scenarios**:

1. **Given** a style with "each GROUP BY column on a new line" and "leading commas" enabled, **When** the developer runs Format SQL, **Then** each GROUP BY column appears on its own line with leading commas.
2. **Given** a style with CASE, CTE, and CREATE TABLE layout options set, **When** the developer runs Format SQL, **Then** CASE expressions, CTEs, and CREATE TABLE column/constraint alignment match those options.
3. **Given** a long SELECT list and a configured maximum line width, **When** the developer runs Format SQL, **Then** lines wrap at the configured width.
4. **Given** a query, **When** the developer runs "Apply Casing", "Insert Semicolons", "Qualify Object Names", "Expand Wildcards", or "Add/Remove Square Brackets" as a standalone action, **Then** only that transformation is applied.
5. **Given** a style configured to run actions during formatting, **When** the developer runs Format SQL, **Then** those actions run as part of formatting.
6. **Given** SQL that cannot be parsed, **When** the developer runs Format SQL, **Then** the original text is preserved and a clear message explains why.

---

### User Story 2 - Trustworthy IntelliSense surfaces & honored settings (Priority: P2)

While authoring, the developer hovers objects to read their metadata, sees parameter help when calling functions/procedures, gets column suggestions for temp tables, and finds that the suggestion-related settings they toggle actually change behavior.

**Why this priority**: Hover info, parameter help, and temp-table completion are core authoring aids that are currently dead or stubbed; settings that don't take effect erode trust in the tool. High daily value, independently demonstrable.

**Independent Test**: Hover several object kinds; invoke a function; declare and reference a `#temp`; toggle the suggestion settings and confirm each takes effect; open the column picker and insert columns.

**Acceptance Scenarios**:

1. **Given** the cursor hovers a table, view, procedure, function, column, or variable, **When** the tooltip appears, **Then** it shows that object's metadata (type, columns/parameters, nullability, description).
2. **Given** the developer is typing a function or procedure call, **When** they type "(" or a comma, **Then** signature help shows and tracks the current parameter.
3. **Given** a `#temp` table created earlier in the script, **When** the developer types its name/alias and ".", **Then** its columns are suggested.
4. **Given** the developer turns off "enable suggestions" or "auto-trigger", **When** they type, **Then** suggestions do not auto-appear.
5. **Given** the suggestions box is open, **When** the developer switches to the column picker, **Then** they can multi-select columns and insert them.
6. **Given** "list all columns after SELECT" is enabled, **When** the developer types SELECT, **Then** all columns are offered.

---

### User Story 3 - Snippets that work on SSMS and Visual Studio (Priority: P2)

The developer types a snippet shortcode and it expands in SSMS and Visual Studio (not only in the Web edition); they can import their SQL Prompt snippets, create a snippet from a selection, and surround a selection with a wrap snippet.

**Why this priority**: Snippet insertion is currently broken on the desktop hosts that are the parity bar; snippets are a daily accelerator and a key migration draw from SQL Prompt.

**Independent Test**: Type a built-in shortcode and expand it on SSMS and VS; import a SQL Prompt snippet file; create a snippet from a selection; surround a selection.

**Acceptance Scenarios**:

1. **Given** a built-in snippet shortcode, **When** the developer types it and presses the commit key in SSMS or VS, **Then** the snippet body is inserted with placeholders and the caret lands at the defined position.
2. **Given** existing SQL Prompt snippet files, **When** the developer imports them, **Then** they become available with bodies and placeholders mapped to AKML's tokens.
3. **Given** a selection, **When** the developer chooses "create snippet from selection", **Then** a new snippet is created (auto-named) and saved.
4. **Given** a selection, **When** the developer invokes surround-with, **Then** a wrap snippet encloses the selection.
5. **Given** a snippet with custom variables, **When** the developer saves it in the Snippet Manager, **Then** the variables are preserved.

---

### User Story 4 - Live, configurable code analysis (Priority: P3)

A team's per-project analysis rules and a developer's inline suppressions take effect **in the editor** (not only in the command-line analyzer); the developer can manage rules from a dialog, tell auto-fixable issues from advisory ones at a glance, and toggle analysis quickly.

**Why this priority**: Team rule standards currently apply only in the CLI, so every developer sees defaults in the editor regardless of project settings — a correctness gap for teams, but lower frequency than formatting/IntelliSense.

**Independent Test**: Add a project rule-settings file overriding a rule; open a file beneath it; confirm the editor honors the override; suppress a rule inline; change a rule via the dialog; toggle analysis off and on.

**Acceptance Scenarios**:

1. **Given** a project rule-settings file that disables rule X, **When** the developer edits a file under that folder, **Then** rule X produces no squiggle in the editor.
2. **Given** an inline suppression comment, **When** analysis runs in the editor, **Then** the suppressed rule is not reported there.
3. **Given** the Manage Rules dialog, **When** the developer disables a rule or changes its severity, **Then** the editor reflects the change.
4. **Given** an auto-fixable issue, **When** its indicator is shown, **Then** it is visually distinct from an advisory-only issue.
5. **Given** analysis is on, **When** the developer toggles it off, **Then** squiggles clear; toggling on re-runs analysis.

---

### User Story 5 - Deeper refactoring (Priority: P3)

The developer renames an object and all references update via a reviewable script, finds invalid objects, inlines a procedure or an EXEC call, converts an INSERT to an UPDATE, and scripts an object as ALTER.

**Why this priority**: These object-level refactors are missing or stubbed; valuable for maintenance work but used less often than formatting/IntelliSense.

**Independent Test**: Rename a column referenced by procedures/views and verify a reviewable script updates all references; inline a procedure; convert an INSERT to an UPDATE; run "find invalid objects".

**Acceptance Scenarios**:

1. **Given** an object renamed via Smart Rename, **When** the developer applies it, **Then** a reviewable script updates the object and all referencing objects consistently.
2. **Given** "find invalid objects", **When** the developer runs it, **Then** objects with broken definitions are listed.
3. **Given** an EXEC of a procedure, **When** the developer inlines it, **Then** the procedure body replaces the call.
4. **Given** an INSERT statement, **When** the developer refactors it to UPDATE, **Then** an equivalent UPDATE is produced.

---

### User Story 6 - Tab coloring & history retention parity (Priority: P3)

The developer color-codes tabs by **database** (not only server), removes history older than a chosen point, and trusts that retention trims old versions while keeping the latest version and all execution records — and can turn auto-trim off.

**Why this priority**: Useful environment-safety and history-hygiene refinements; narrower audience than the core authoring stories.

**Acceptance Scenarios**:

1. **Given** a database→environment color rule, **When** the developer opens a tab connected to that database on any server, **Then** the tab takes the environment color.
2. **Given** retention runs, **When** it trims, **Then** old versions are removed but the latest version and execution records are kept.
3. **Given** a history entry, **When** the developer chooses "remove older than", **Then** all older entries are deleted.
4. **Given** retention, **When** the developer disables auto-trim in Options, **Then** nothing is purged.

---

### User Story 7 - Complete Options coverage (Priority: P3)

Every supported in-scope setting is discoverable and changeable from the Options dialog, including alias policy, special-character handling, tooltip toggles, active-style selection, and suggestion connection scope; each page offers help.

**Why this priority**: Several supported settings are configuration-file-only today, so users can't discover or change them; a polish/usability gap.

**Acceptance Scenarios**:

1. **Given** any in-scope supported setting, **When** the developer opens Options, **Then** there is a control to view and change it.
2. **Given** alias options (include "AS", custom map, prefixes-to-ignore), **When** the developer configures them, **Then** inserted aliases follow them.
3. **Given** special-character options (auto-close characters, add parentheses), **When** the developer types, **Then** they apply.
4. **Given** an Options page, **When** the developer requests help, **Then** page-specific help is shown.

---

### User Story 8 - Command Palette object search & bulk format access (Priority: P3)

The developer searches the Command Palette for database objects (not just commands) and reaches the bulk-format capability from a menu/palette command.

**Why this priority**: Convenience/discoverability improvements that build on already-present capability; lowest urgency.

**Acceptance Scenarios**:

1. **Given** the Command Palette, **When** the developer types an object name, **Then** matching database objects appear and selecting one navigates to or inserts it.
2. **Given** the bulk-format capability, **When** the developer invokes it from a menu or the palette, **Then** the bulk-format wizard opens and formats the selected files/objects.

---

### Edge Cases

- **Unparseable / partial SQL**: Format and format-actions preserve the original text and surface a clear message rather than corrupting the script.
- **Missing input for placeholders**: the selected-text placeholder with no selection, or a clipboard placeholder with an empty clipboard, resolves to empty without error.
- **Rename collisions**: Smart Rename detects a name collision in scope and blocks the apply with an explanation.
- **Conflicting project rule settings up the directory tree**: the nearest settings file wins.
- **Disable-formatting regions inside a selection**: marked regions are preserved verbatim when the surrounding selection is formatted.
- **Temp table altered mid-script**: columns added by a later `ALTER TABLE` may not be re-recognized in the same script (documented limit; define columns up front or use `SELECT INTO`).
- **No active database connection**: schema-dependent features degrade gracefully with a clear message instead of failing silently.
- **Very large scripts**: formatting and analysis complete without blocking the IDE UI.

## Requirements *(mandatory)*

### Functional Requirements

#### Formatting & Styles

- **FR-001**: Format SQL MUST apply all layout settings the active style defines, including SELECT/INSERT/UPDATE/DELETE/MERGE statement layout; FROM/JOIN/WHERE/GROUP BY/HAVING/ORDER BY placement; CASE expressions; CTEs; DDL (CREATE TABLE/PROCEDURE/FUNCTION/VIEW/INDEX/TRIGGER); control-flow blocks (BEGIN/END, IF, WHILE, TRY/CATCH); list and comma style (leading/trailing, one-item-per-line, alignment); parentheses; and subqueries — with no exposed style setting silently ignored.
- **FR-002**: Format SQL MUST wrap long statements and lists at a configurable maximum line width.
- **FR-003**: Users MUST be able to run each formatting transformation as a standalone action: Apply Casing, Insert Semicolons, Qualify Object Names, Expand Wildcards, Add Square Brackets, and Remove Square Brackets.
- **FR-004**: The active style MUST be able to run a selected set of these actions automatically as part of Format SQL.
- **FR-005**: When SQL cannot be parsed or formatted, the system MUST preserve the original text and present a clear, actionable message.
- **FR-006**: Users MUST be able to see which formatting style is active and switch the active style.
- **FR-007**: Users MUST be able to create, copy, edit, import, export, and set-active formatting styles from the style editor.
- **FR-008**: The formatting preview MUST be able to preview the active style against both a sample query and the current editor content.

#### Code Completion / IntelliSense

- **FR-009**: Hovering an object (table, view, procedure, function, column, or variable) in the editor MUST show a metadata tooltip for that object.
- **FR-010**: Parameter signature help MUST appear when invoking functions and procedures and MUST track the active parameter as the user types.
- **FR-011**: Columns of temporary (`#temp`) tables defined earlier in the script MUST be offered as suggestions.
- **FR-012**: The "enable suggestions", "automatically trigger suggestions", and "list all columns after SELECT" settings MUST take effect on completion behavior.
- **FR-013**: Users MUST be able to multi-select columns via a column picker and insert them together.
- **FR-014**: The suggestions box MUST group items by category, allow navigating between categories, and allow showing/hiding owner (schema) names.
- **FR-015**: Automatic alias generation MUST honor the "include AS" option, a user-defined object→alias map, and prefixes-to-ignore.
- **FR-016**: Users MUST be able to limit suggestions to chosen databases/schemas and optionally include linked-server objects.
- **FR-017**: The object definition surface MUST present the object's creation script (not only its description).

#### Refactoring & Actions

- **FR-018**: Smart Rename MUST rename an object and update all referencing objects **across the database**, producing a reviewable script that the user approves before it is applied.
- **FR-019**: Find Invalid Objects MUST list objects whose definitions are broken or invalid.
- **FR-020**: Users MUST be able to inline a stored procedure's body into the calling code and inline an EXEC call into its underlying query.
- **FR-021**: Users MUST be able to refactor an INSERT statement into an equivalent UPDATE statement.
- **FR-022**: Users MUST be able to script an existing object as an ALTER statement.
- **FR-023**: Users MUST be able to wrap a selection in disable-formatting markers so Format SQL skips it.

#### Code Analysis

- **FR-024**: Per-project rule-settings files MUST take effect in the live editor, matching the command-line analyzer's behavior on the same files.
- **FR-025**: Inline suppression comments MUST be honored in the live editor.
- **FR-026**: A Manage Rules dialog MUST let users enable/disable individual rules and set per-rule severities.
- **FR-027**: Auto-fixable issues MUST be visually distinguishable from advisory-only issues.
- **FR-028**: An issue-details popup MUST present the offending rule's description and any reference link.
- **FR-029**: Users MUST be able to toggle all analysis on and off quickly from a command.

#### Snippets

- **FR-030**: Typing a snippet shortcode and pressing a commit key MUST expand the snippet in both SSMS 22 and Visual Studio 2026.
- **FR-031**: A built-in snippet set MUST ship with the product and be available out of the box.
- **FR-032**: Users MUST be able to import SQL Prompt snippet files, with SQL Prompt placeholder tokens mapped to AKML equivalents.
- **FR-033**: Users MUST be able to create a snippet from the current editor selection.
- **FR-034**: Users MUST be able to surround a selection with a wrap snippet, the selection supplied to the snippet's selected-text placeholder.
- **FR-035**: The snippet caret-position placeholder MUST be honored on the desktop hosts after insertion.
- **FR-036**: The Snippet Manager MUST preserve a snippet's custom variables on save and allow editing them.
- **FR-037**: Snippet placeholders MUST include selection-range markers and custom date/time formats, and the selected-text placeholder MUST receive the editor selection on the desktop hosts.

#### Tab Management & SQL History

- **FR-038**: Tab coloring MUST support rules keyed by database and by database-on-any-server, in addition to server-only rules.
- **FR-039**: History retention MUST trim old versions while preserving each query's latest version and its execution records.
- **FR-040**: Users MUST be able to disable history auto-trim.
- **FR-041**: Users MUST be able to remove all history entries older than a selected entry.

#### Options & Settings

- **FR-042**: Every in-scope supported setting MUST be viewable and changeable from the Options dialog — no in-scope setting remains configuration-file-only.
- **FR-043**: Options MUST expose alias policy, special-character handling (auto-close characters, add parentheses), object/parameter tooltip toggles, encrypted-object decryption, active-style selection, and suggestion connection scope.
- **FR-044**: Options pages MUST provide page-specific help.

#### Platform & Productivity

- **FR-045**: The Command Palette MUST find and act on database objects in addition to commands.
- **FR-046**: The in-IDE bulk-format wizard MUST be reachable from a menu or Command Palette entry.

#### Cross-cutting

- **FR-047**: All in-scope capabilities MUST behave consistently in both SSMS 22 and Visual Studio 2026 (the desktop parity bar).
- **FR-048**: Schema-dependent capabilities MUST degrade gracefully with a clear message when no database connection is active.
- **FR-049**: Every applied transformation (format, format-action, refactor, snippet expansion, analysis auto-fix) MUST be reversible as a single undo step.

### Key Entities

- **Formatting Style**: A named set of layout, casing, list, and wrapping options applied by Format SQL; built-in or user-created; importable/exportable; one is active at a time.
- **Format Action**: A discrete code transformation (apply casing, insert semicolons, qualify names, expand wildcards, add/remove brackets) runnable standalone or as part of Format SQL.
- **Snippet**: A shortcode, a body, and zero or more placeholders/variables; built-in or personal; expandable in the editor and importable from SQL Prompt.
- **Placeholder / Variable**: A token replaced during snippet expansion (caret position, selected text, date/time, server/database/user, clipboard, or user-defined) with optional default and ordering.
- **Analysis Rule & Rule Settings**: A diagnostic rule plus per-project overrides (enabled state, severity) and inline suppressions that scope where a rule applies.
- **Tab-Color Environment & Rule**: A named environment (color + label) and the server/database matching rules that map a connection to an environment.
- **History Entry, Version & Execution**: A recorded query, its timeline of saved versions, and its execution records; subject to retention trimming.
- **Completion Item**: A suggestion (object, column, keyword, snippet, or function) carrying metadata (type, key/relationship indicators, description) shown in the suggestions box.
- **Options Setting**: A persisted configuration value paired with an Options-dialog control through which the user views and changes it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of formatting-style settings exposed by the style editor produce a visible, correct effect when Format SQL runs against a representative test corpus — no exposed setting is silently ignored.
- **SC-002**: All six standalone format actions and the format-time actions produce correct, isolated transformations across the test corpus.
- **SC-003**: Snippet shortcodes expand correctly on both SSMS 22 and Visual Studio 2026 for 100% of the built-in snippet pack, matching Web-edition behavior.
- **SC-004**: A developer migrating from SQL Prompt can import their existing snippet library and a formatting style and continue working with zero manual re-creation.
- **SC-005**: Per-project rule configuration and inline suppressions take effect in the live editor for 100% of configured rules, producing the same findings the command-line analyzer produces on the same files.
- **SC-006**: Hover tooltips and parameter signature help appear for every supported object and function type during editing.
- **SC-007**: Zero in-scope settings remain configuration-file-only — every supported in-scope setting is adjustable from the Options dialog.
- **SC-008**: All in-scope capabilities behave identically in SSMS 22 and Visual Studio 2026.
- **SC-009**: Every applied transformation is reversible with a single undo.
- **SC-010**: The in-scope 🟡/❌ rows this feature targets in the `doc/_Prompt-Gap/` audit move to ✅ on re-audit; the feature is complete when its targeted rows reach parity (measured against the audit scorecard, which excludes AI and licensing).
- **SC-011**: Activating the full formatting rule set and live analysis introduces no latency regression on the hot paths — code completion suggestions appear within 100 ms (p95) of the trigger, and Format SQL completes within 200 ms on typical scripts (≤ ~500 lines); large scripts never block the IDE UI.

## Assumptions

- The parity bar is desktop **SSMS 22 + VS 2026**; the Blazor Web edition is a reference/differentiator, not a target. Capabilities that currently work only in the Web edition are treated as gaps on the desktop bar.
- **Functional** parity is the goal. AKML's existing keyboard shortcuts are retained; re-mapping shortcuts to exactly match SQL Prompt's chords is out of scope unless a specific behavior depends on it.
- The built-in snippet pack provides a comparable starter set; it need not be byte-identical to SQL Prompt's library.
- The audit at `doc/_Prompt-Gap/` (files 01–06, 08, and the non-licensing rows of 09) is the authoritative gap list; each row carries an evidence note locating the code to change. Many targeted capabilities already exist in code but are unreachable ("built but not wired"); closing them is finishing/connecting existing capability, sized per-item in the plan phase.
- This is delivered as a **single feature** (030), planned and shipped in **phases by priority** (P1 as the MVP, then P2, then P3); the user stories are independently shippable, so the work is not split into separate specs.

## Dependencies

- A live database connection and a populated schema cache are required for schema-dependent capabilities (completion, qualification, wildcard expansion, smart rename, temp-table metadata, object definition, command-palette object search).
- Existing formatting, snippet, analysis-rule, history/tab, and Options subsystems — this feature finishes and connects them rather than replacing them.

## Out of Scope

- All AI features — Text-to-SQL, Explain, Fix, Optimize, Query Index Analysis, ghost-text completion, and the AI chat panel (audit file 07).
- Licensing and edition tiers (audit file 09 §2).
- Azure Data Studio and Microsoft Fabric hosts.
- Redgate-cloud (Redgate Platform) sharing of styles, snippets, or analysis rules.
- Redgate companion-product integrations (SQL Dependency Tracker, Data Modeler).
- Re-mapping keyboard shortcuts to exactly match SQL Prompt's chords.
