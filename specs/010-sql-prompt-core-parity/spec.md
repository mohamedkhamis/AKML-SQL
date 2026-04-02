# Feature Specification: SQL Prompt Core Feature Parity

**Feature Branch**: `010-sql-prompt-core-parity`  
**Created**: 2026-04-01  
**Status**: Draft  
**Input**: Fill feature gaps between AKML SQL's current implementation and Redgate SQL Prompt's core (non-AI) feature set, based on the reference document `doc/SQL-Prompt-Features/SQL_Prompt_Features_Core.md`.

## Clarifications

### Session 2026-04-01

- Q: Should Safe Rename execute ALTER scripts directly against the database or generate a script file for the user? → A: Generate a script file the user can review, modify, and execute manually (no direct database execution).
- Q: What confirmation mechanism should Execution Guard use for Production vs non-Production? → A: Production requires typing the server name to confirm; non-Production environments use a simple Yes/No dialog.
- Q: Should TRUNCATE TABLE also be intercepted by Execution Guard? → A: Yes, TRUNCATE TABLE is included alongside DELETE/UPDATE without WHERE and DROP statements.
- Q: Should grid sorting/filtering be deferred if SSMS grid control limits feasibility? → A: No, keep all three (sorting, filtering, aggregates) in scope regardless of complexity.
- Q: Should Execution Guard log intercepted events for audit purposes? → A: Yes, log all events (blocked, confirmed, bypassed) to the AKML SQL log file.

## Gap Analysis Summary

Based on a thorough scan of the AKML SQL codebase against the SQL Prompt Core Features reference, the following gap areas were identified. Features already at 90%+ parity (Code Completion, Formatting, Analysis, Settings architecture) are excluded -- this spec focuses only on **meaningful gaps** that affect user experience.

| Gap Area                          | Current Coverage                            | Priority | Impact                                            |
|-----------------------------------|---------------------------------------------|----------|---------------------------------------------------|
| Execution Guard (safety warnings) | 0% -- not implemented                       | P1       | Prevents accidental data loss on production        |
| Snippet Manager UI (dialog)       | 0% -- engine exists, no UI                  | P1       | Users cannot create/edit/manage snippets visually  |
| Settings UI Completeness          | 40% -- basic dialog exists, many options missing | P2  | Users cannot configure 60%+ of available settings  |
| Safe Rename Refactoring           | 0% -- stub exists (T040)                    | P2       | Cannot rename objects across database references   |
| Actions List (lightbulb menu)     | 50% -- some actions, no unified popup       | P3       | Discoverable quick-fix/refactoring actions         |
| Grid Column Filtering & Sorting   | 0% -- not implemented                       | P3       | Users cannot filter/sort result grids interactively|
| Object Definition Box             | 30% -- QuickInfo exists, no dual-tab popup  | P3       | No Summary/Script tabbed popup alongside suggestions|
| Navigation Polish                 | 70% -- core features exist, some gaps       | P4       | Bookmarks, breadcrumbs, symbol outline tree        |

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Execution Guard: Prevent Accidental Destructive Queries (Priority: P1)

A database administrator connected to a production server accidentally runs a `DELETE FROM Orders` query without a `WHERE` clause. The system intercepts the execution, displays a prominent warning dialog showing the server name, environment type (Production), and the destructive nature of the query, and requires explicit confirmation before proceeding.

**Why this priority**: This is a safety-critical feature that prevents real data loss. SQL Prompt's execution guard is one of its most valued features among DBAs. A single accidental DELETE or DROP on production can cause catastrophic damage.

**Independent Test**: Can be tested by connecting to any server with a tab-coloring environment assigned, writing a DELETE without WHERE, and pressing F5 -- the confirmation dialog must appear before execution proceeds.

**Acceptance Scenarios**:

1. **Given** a query tab connected to a Production-colored server, **When** the user executes `DELETE FROM dbo.Orders` (no WHERE clause), **Then** a confirmation dialog appears with the server name, database name, environment color (red), and a warning message requiring the user to type the server name to confirm execution.

2. **Given** a query tab connected to a Development-colored server, **When** the user executes `DELETE FROM dbo.Orders` (no WHERE clause), **Then** a simple Yes/No confirmation dialog appears with standard warning styling (not red/alarming), since the environment is non-production.

3. **Given** a query tab connected to a Production-colored server, **When** the user executes `DROP TABLE dbo.Orders`, **Then** a confirmation dialog appears with maximum-severity styling (red background matching environment color) and a prominent display of the object being dropped.

4. **Given** a query tab connected to any server, **When** the user executes `UPDATE dbo.Orders SET Status = 'Cancelled'` (no WHERE clause), **Then** a confirmation dialog appears warning about an unfiltered UPDATE affecting all rows.

5. **Given** execution guard is disabled in settings, **When** the user executes any destructive query, **Then** the query executes immediately without interception.

6. **Given** a query with a valid WHERE clause like `DELETE FROM dbo.Orders WHERE OrderID = 5`, **When** the user executes it, **Then** no confirmation dialog appears -- execution proceeds normally.

---

### User Story 2 - Snippet Manager Dialog (Priority: P1)

A developer wants to create a custom snippet for their team's standard audit-table pattern. They open the Snippet Manager from the AKML SQL menu, see a searchable list of all snippets (built-in and custom) on the left, a body editor with syntax highlighting on the right, and can create, edit, duplicate, delete, import, and export snippets. They define a shortcut abbreviation, add placeholder macros like `$CURSOR$` and `$DBNAME$`, save it, and immediately use it in the editor by typing the abbreviation and pressing Tab.

**Why this priority**: The snippet engine exists and works, but without a management UI, users have no discoverable way to create, view, or edit snippets. This is a core productivity feature that every SQL Prompt user relies on daily.

**Independent Test**: Can be tested by opening the Snippet Manager, creating a new snippet with abbreviation "test", body containing `SELECT $CURSOR$ FROM $DBNAME$..`, saving it, then typing "test" + Tab in the editor and verifying it expands correctly.

**Acceptance Scenarios**:

1. **Given** the user opens the Snippet Manager from the AKML SQL menu, **When** the dialog loads, **Then** the left panel shows a searchable list of all snippets (built-in + custom) with their abbreviation, name, and a snippet icon badge, and the right panel shows the selected snippet's body with syntax highlighting.

2. **Given** the user clicks "New" in the Snippet Manager, **When** they fill in the name, abbreviation, description, and body fields, **Then** they can save the snippet and it appears in the list and is immediately available in the editor's suggestion popup.

3. **Given** the user selects a built-in snippet, **When** they try to edit it, **Then** the built-in snippet is read-only but the user can duplicate it to create an editable copy.

4. **Given** the user has custom snippets, **When** they click "Export", **Then** selected snippets are saved to a `.akmlsnippet` JSON file that can be shared with team members.

5. **Given** the user has a `.akmlsnippet` file, **When** they click "Import" in the Snippet Manager, **Then** the snippets are loaded and added to their custom collection.

6. **Given** the user types a search term in the Snippet Manager filter box, **When** the term matches snippet names, abbreviations, or body content, **Then** the list is filtered in real-time to show only matching snippets.

---

### User Story 3 - Settings UI Completeness (Priority: P2)

A user opens the AKML SQL Options dialog and can configure all 50+ available settings through a well-organized category tree. Each settings page shows the relevant options with appropriate controls (toggles, dropdowns, numeric inputs, color pickers), and changes take effect immediately or after clicking OK. The user can also reset individual pages or all settings to defaults.

**Why this priority**: Many powerful features (formatting profiles, analysis rules, tab coloring rules, grid options) exist in the engine but are only configurable by manually editing `config.json`. A complete settings UI makes these features accessible to all users.

**Independent Test**: Can be tested by opening the Settings dialog, navigating to each category page, changing a setting, clicking OK, and verifying the change persists in `config.json` and affects the corresponding feature behavior.

**Acceptance Scenarios**:

1. **Given** the user opens AKML SQL Options, **When** the dialog loads, **Then** a category tree on the left shows all settings groups: IntelliSense, Formatting, Snippets, Code Analysis, Tabs & History, Refactoring, Grid, Navigation, Safety, and a "Reset" section, matching the SQL Prompt options layout.

2. **Given** the user navigates to the Formatting settings page, **When** they select an active formatting profile from a dropdown, **Then** a real-time preview panel shows sample SQL formatted with that profile, and they can click "Edit Style" to open the profile editor.

3. **Given** the user navigates to the Code Analysis settings page, **When** they see the rule list, **Then** each rule shows its ID, description, current severity (Error/Warning/Info/Ignore), and a toggle -- and changes to severity levels are saved to the `.casettings` file.

4. **Given** the user navigates to the Tabs settings page, **When** they see the environment rules editor, **Then** they can add, edit, and remove server-to-environment color mappings with a color picker, and the changes take effect on the next tab opened.

5. **Given** the user clicks "Reset This Page", **When** confirmed, **Then** only the settings on the current page revert to defaults while all other settings are preserved.

6. **Given** the user clicks "Export All Settings", **When** they choose a file path, **Then** all settings are exported to a JSON file that can be imported on another machine.

---

### User Story 4 - Safe Rename Refactoring (Priority: P2)

A developer needs to rename a table column `OrderDate` to `OrderPlacedDate` across an entire database. They right-click the column in the editor or Object Explorer, choose "Safe Rename", type the new name, and see a preview showing all stored procedures, views, functions, and triggers that reference the column. After reviewing the dependency tree, they click "Generate Script" and the tool produces a complete SQL script in a new query tab containing all the ALTER statements to rename the column and update all references, which the developer can review and execute at their discretion.

**Why this priority**: Safe rename across database objects is a signature refactoring feature of SQL Prompt. The engine stub (T040) already exists but needs full implementation. Without it, renaming a column requires manually finding and updating every reference -- error-prone and time-consuming.

**Independent Test**: Can be tested by creating a table with a column referenced by a view and a stored procedure, invoking Safe Rename on the column, verifying the preview shows both dependent objects, and applying the rename.

**Acceptance Scenarios**:

1. **Given** the user selects an identifier (table, column, procedure name) in the editor, **When** they invoke "Safe Rename" (via context menu or F2), **Then** an inline rename textbox appears allowing them to type the new name.

2. **Given** the user has entered a new name, **When** they press Enter, **Then** a preview dialog appears showing a dependency tree of all database objects that reference the renamed identifier, with a diff view showing the before/after for each affected script.

3. **Given** the preview dialog is shown, **When** the user clicks "Generate Script", **Then** the system creates a complete SQL script containing all ALTER statements (ALTER TABLE for columns, ALTER PROCEDURE for proc renames, etc.) and opens it in a new query editor tab for user review and manual execution.

4. **Given** the user attempts to rename a column that is part of a primary key or foreign key, **When** the preview loads, **Then** the dialog warns about constraint dependencies and includes constraint drop/recreate in the generated script.

5. **Given** the generated script is opened in a new tab, **When** the user reviews it, **Then** they can modify any part of the script before executing it, and the script includes comments explaining each change.

---

### User Story 5 - Actions List (Lightbulb Quick Actions Menu) (Priority: P3)

A developer selects a block of SQL code and sees a lightbulb icon appear in the left margin. Clicking it (or pressing Ctrl+.) opens a contextual menu of available actions: Qualify Object Names, Expand Wildcards, Surround with BEGIN/END, Surround with TRY/CATCH, Comment/Uncomment, Create Snippet from Selection, Convert sp_executesql to SQL, Rename Alias, and any applicable auto-fixes from code analysis.

**Why this priority**: Many of these individual actions already exist as separate commands, but presenting them in a unified, contextual lightbulb menu makes them discoverable and efficient. This is a standard IDE pattern that users expect.

**Independent Test**: Can be tested by selecting any SQL code block, verifying the lightbulb appears, clicking it, and seeing context-appropriate actions listed.

**Acceptance Scenarios**:

1. **Given** the user selects a block of SQL code, **When** a lightbulb icon appears in the margin gutter, **Then** clicking it shows a popup menu listing all applicable actions for the selection context.

2. **Given** the user's cursor is on a `SELECT *` expression, **When** they open the actions list, **Then** "Expand Wildcards" appears as an available action, and selecting it replaces `*` with the explicit column list.

3. **Given** a code analysis warning exists on a line, **When** the user opens the actions list on that line, **Then** the auto-fix action for that specific rule appears (e.g., "Add schema qualifier" for PE002).

4. **Given** the user selects multiple statements, **When** they open the actions list, **Then** "Surround with BEGIN/END" and "Surround with TRY/CATCH" are available.

5. **Given** no selection and the cursor is on a plain code line with no warnings, **When** the user opens the actions list, **Then** only universally applicable actions appear (Comment, Format Selection).

---

### User Story 6 - Enhanced Results Grid (Priority: P3)

A developer runs a query returning thousands of rows and wants to quickly find specific data without writing another query. They right-click a column header in the results grid to filter or sort by that column. They also select a range of numeric cells and see aggregate statistics (Sum, Average, Count, Min, Max) in a status area below the grid.

**Why this priority**: Results grid filtering and sorting reduce the need to re-run queries with additional WHERE clauses, saving time during data exploration. Aggregate statistics on selection are a frequently used SQL Prompt feature.

**Independent Test**: Can be tested by running any SELECT query, right-clicking a column header to sort/filter, selecting numeric cells, and verifying aggregates appear in the status area.

**Acceptance Scenarios**:

1. **Given** a query result is displayed in the grid, **When** the user clicks a column header, **Then** the results are sorted by that column (ascending on first click, descending on second, unsorted on third).

2. **Given** a query result is displayed, **When** the user right-clicks a column header and selects "Filter", **Then** a filter input appears allowing text matching or value selection, and the grid shows only matching rows.

3. **Given** the user selects a range of cells containing numeric values, **When** the selection is made, **Then** a status area at the bottom of the grid shows: Sum, Average, Count, Min, and Max of the selected values.

4. **Given** the user selects cells containing mixed types (some numeric, some text), **When** the status area updates, **Then** it shows Count for all cells but Sum/Average/Min/Max only for numeric values.

---

### User Story 7 - Object Definition Box (Summary/Script Tabs) (Priority: P3)

While browsing the autocomplete suggestion list, a developer highlights a table name and sees a secondary popup panel appear to the right. This panel has two tabs: "Summary" showing column names, data types, nullability, key icons, and estimated row count; and "Script" showing the full CREATE TABLE statement with syntax highlighting.

**Why this priority**: The current QuickInfo hover provides basic information, but the SQL Prompt-style dual-tab definition box alongside the suggestion popup provides richer context without leaving the autocomplete flow.

**Independent Test**: Can be tested by triggering autocomplete after `FROM`, highlighting a table, and verifying the definition box appears with both Summary and Script tabs populated correctly.

**Acceptance Scenarios**:

1. **Given** the suggestion popup is visible with a table highlighted, **When** the user pauses on the highlighted item, **Then** a secondary popup appears to the right showing the "Summary" tab by default with columns, types, nullability markers, key icons (PK/FK/UQ), and approximate row count.

2. **Given** the definition box is visible with the Summary tab active, **When** the user clicks the "Script" tab, **Then** the panel shows the full CREATE TABLE DDL with syntax highlighting.

3. **Given** the user highlights a stored procedure in the suggestion list, **When** the definition box appears, **Then** the Summary tab shows parameter names, types, and directions, and the Script tab shows the CREATE PROCEDURE body.

4. **Given** the suggestion popup is dismissed, **When** the user continues typing, **Then** the definition box also dismisses immediately.

---

### User Story 8 - Navigation Polish: Bookmarks & Symbol Outline (Priority: P4)

A developer working on a long stored procedure wants to mark key sections for quick navigation. They place bookmarks on important lines using a keyboard shortcut and navigate between them. They also open a Document Outline panel that shows the structure of their SQL file (procedures, functions, CTEs, temp tables) as a navigable tree.

**Why this priority**: These are polish features that improve productivity for power users working with large SQL files. They are nice-to-have but not blocking for core feature parity.

**Independent Test**: Can be tested by opening a long SQL file, setting bookmarks on 3 lines, navigating between them with shortcuts, and verifying the Document Outline shows the correct structure.

**Acceptance Scenarios**:

1. **Given** the user's cursor is on a line, **When** they press Ctrl+K, Ctrl+K (toggle bookmark), **Then** a bookmark icon appears in the left margin gutter on that line.

2. **Given** multiple bookmarks are set, **When** the user presses Ctrl+K, Ctrl+N (next bookmark), **Then** the cursor jumps to the next bookmarked line. Ctrl+K, Ctrl+P jumps to the previous.

3. **Given** the user opens the Document Outline panel, **When** a SQL file is active, **Then** the panel shows a tree structure with nodes for each procedure, function, view, CTE, temp table, and major statement block in the file.

4. **Given** the Document Outline is visible, **When** the user clicks a node, **Then** the editor scrolls to and highlights the corresponding code section.

---

### Edge Cases

- What happens when execution guard is triggered on a query with GO batch separators containing multiple destructive statements? Each batch should be checked independently.
- How does the snippet manager handle duplicate abbreviations between built-in and custom snippets? Custom snippets should take precedence.
- What happens when safe rename encounters a dynamic SQL string referencing the column by name? The preview should warn that dynamic SQL references cannot be updated automatically.
- How does the settings dialog handle concurrent modification if the user edits `config.json` externally while the dialog is open? The dialog should detect changes on save and prompt to merge or overwrite.
- What happens when the results grid filter is applied but the user runs a new query? Filters should reset when new results are loaded.
- What happens when execution guard intercepts a query inside a transaction that was started by a previous batch? The guard should still fire since the destructive statement is being submitted for execution regardless of transaction context.

## Requirements *(mandatory)*

### Functional Requirements

**Execution Guard:**
- **FR-001**: System MUST intercept DELETE statements without a WHERE clause before execution and display a confirmation dialog.
- **FR-002**: System MUST intercept UPDATE statements without a WHERE clause before execution and display a confirmation dialog.
- **FR-003**: System MUST intercept TRUNCATE TABLE, DROP TABLE, DROP DATABASE, DROP INDEX, DROP PROCEDURE, DROP VIEW, and DROP FUNCTION statements before execution and display a confirmation dialog.
- **FR-004**: The confirmation dialog MUST prominently display the server name, database name, and environment color (matching tab coloring).
- **FR-005**: For Production-colored environments, the confirmation dialog MUST use maximum-severity styling (environment color as background) and require the user to type the server name to confirm. For non-Production environments, a simple Yes/No confirmation dialog MUST be shown instead.
- **FR-006**: System MUST provide a global toggle to enable/disable execution guard in settings.
- **FR-007**: System MUST allow per-environment severity configuration (e.g., always warn on Production, optional on Development).
- **FR-007a**: System MUST log all execution guard events (query blocked, user confirmed execution, guard bypassed due to disabled setting) to the AKML SQL log file, including server name, database name, environment, statement type, and timestamp.

**Snippet Manager:**
- **FR-008**: System MUST provide a Snippet Manager dialog accessible from the AKML SQL menu.
- **FR-009**: The Snippet Manager MUST display all snippets (built-in and custom) in a searchable, scrollable list with abbreviation, name, and icon.
- **FR-010**: Users MUST be able to create new snippets with name, abbreviation, description, and body fields.
- **FR-011**: Users MUST be able to edit and delete custom snippets. Built-in snippets MUST be read-only but duplicable.
- **FR-012**: The snippet body editor MUST support syntax highlighting and placeholder macro insertion ($CURSOR$, $SELECTEDTEXT$, $DBNAME$, etc.).
- **FR-013**: Users MUST be able to import and export snippets as `.akmlsnippet` files.

**Settings UI:**
- **FR-014**: The Settings dialog MUST present all configurable options organized in a category tree matching the feature areas.
- **FR-015**: Each settings page MUST use appropriate input controls (toggles for booleans, dropdowns for enums, numeric inputs for numbers, color pickers for colors).
- **FR-016**: The Settings dialog MUST support "Reset This Page" and "Reset All" actions.
- **FR-017**: The Settings dialog MUST support "Export All Settings" and "Import Settings" for portability.
- **FR-018**: Settings changes MUST take effect upon clicking OK (or Apply if provided) without requiring a restart.

**Safe Rename:**
- **FR-019**: System MUST support renaming tables, columns, stored procedures, functions, and views across all database references.
- **FR-020**: Safe Rename MUST display a preview dialog showing all affected objects with a diff view of the before/after for each affected script.
- **FR-021**: Safe Rename MUST generate a complete SQL script file containing all ALTER statements and reference updates. The system MUST NOT execute changes directly against the database.
- **FR-022**: The generated script MUST be opened in a new query editor tab so the user can review, modify, and execute it manually.

**Actions List:**
- **FR-023**: System MUST display a lightbulb icon in the editor margin when the cursor is on a line with available actions.
- **FR-024**: Clicking the lightbulb (or pressing Ctrl+.) MUST open a contextual popup listing all applicable quick actions.
- **FR-025**: The actions list MUST include: Qualify Object Names, Expand Wildcards, Surround with BEGIN/END, Surround with TRY/CATCH, Comment/Uncomment, Create Snippet from Selection, and any applicable code analysis auto-fixes.

**Results Grid:**
- **FR-026**: Users MUST be able to sort results by clicking column headers (ascending/descending/unsorted cycle).
- **FR-027**: Users MUST be able to filter results by column values via a right-click column header menu.
- **FR-028**: System MUST display aggregate statistics (Sum, Average, Count, Min, Max) for selected numeric cells in a status area.

**Object Definition Box:**
- **FR-029**: The suggestion popup MUST show a secondary panel with Summary and Script tabs when an item is highlighted.
- **FR-030**: The Summary tab MUST show column names, data types, nullability, key icons (PK, FK, UQ), and estimated row count for tables.
- **FR-031**: The Script tab MUST show the full CREATE statement with syntax highlighting.

**Navigation Polish:**
- **FR-032**: Users MUST be able to set, clear, and navigate between line bookmarks via keyboard shortcuts.
- **FR-033**: System MUST provide a Document Outline panel showing the structural hierarchy of the active SQL file.

### Key Entities

- **Environment Rule**: Maps server name patterns to environment types (Production, Staging, Development, etc.) with associated colors and execution guard severity levels.
- **Snippet**: A reusable code template with name, abbreviation, description, body (with placeholder macros), scope (global or language-specific), and source (built-in or custom).
- **Settings Profile**: A collection of all user preferences, exportable/importable as a JSON file, with per-category reset capability.
- **Refactoring Preview**: A transient view showing all database objects affected by a rename operation, with before/after diffs. On confirmation, produces a SQL script file opened in a new editor tab (no direct database execution).
- **Bookmark**: A line-level marker in the editor gutter, persisted per-file for the duration of the session.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users connected to Production environments see a confirmation dialog 100% of the time before DELETE/UPDATE without WHERE, TRUNCATE TABLE, or DROP statements execute -- zero accidental unguarded executions.
- **SC-002**: Users can create a new snippet and use it in the editor within 60 seconds via the Snippet Manager dialog.
- **SC-003**: 100% of the 50+ settings defined in AppSettings are configurable through the Settings UI -- no setting requires manual JSON editing.
- **SC-004**: Safe Rename correctly identifies and updates all references across stored procedures, views, functions, and triggers for the renamed object -- verified by zero broken references after rename.
- **SC-005**: The Actions List popup appears within 200ms of the user's trigger (click lightbulb or Ctrl+.) and shows all context-appropriate actions.
- **SC-006**: Results grid sorting responds within 500ms for result sets up to 10,000 rows.
- **SC-007**: Aggregate statistics (Sum, Avg, Count, Min, Max) update within 300ms when the user changes cell selection.
- **SC-008**: The Object Definition Box appears within 300ms of highlighting a suggestion item and shows accurate schema information.

## Assumptions

- The existing tab coloring infrastructure (environment detection, color rules) provides the foundation for execution guard -- the guard piggybacks on environment assignments already configured by the user.
- The existing snippet engine (SnippetLoader, SnippetIndex, IPC snippet messages) provides the backend for the Snippet Manager UI -- only the WPF dialog needs to be built.
- The existing SettingsDialog/SettingsWindow WPF infrastructure can be extended with additional pages rather than rebuilt from scratch.
- Safe Rename will initially support single-database scope (not cross-database references) -- cross-database rename is deferred to a future enhancement.
- The Actions List will integrate with the existing code analysis auto-fix infrastructure, presenting analysis fixes alongside refactoring actions in a unified menu.
- Results grid enhancements (sorting, filtering, aggregates) are all in scope. If the native SSMS grid control does not support sorting/filtering, a custom grid wrapper or overlay must be implemented to provide this functionality.
- Bookmarks are session-scoped (not persisted across SSMS restarts) in the initial implementation.

## Scope Boundaries

**In Scope (this feature):**
- Execution Guard for DELETE/UPDATE without WHERE and DROP statements
- Snippet Manager WPF dialog (CRUD, search, import/export)
- Settings UI pages for all 50+ settings in AppSettings
- Safe Rename for tables, columns, procedures, functions, views (single-database)
- Actions List lightbulb popup with contextual quick actions
- Results grid column sorting, filtering, and aggregate statistics
- Object Definition Box (Summary/Script tabs) alongside suggestion popup
- Bookmark toggle and navigation shortcuts
- Document Outline panel

**Out of Scope (deferred):**
- AI features (NL-to-SQL, Explain SQL, AI Fix, Ghost Text, Optimize) -- separate phase
- Cross-database safe rename
- Smart Rename via Object Explorer integration (requires separate SSMS Object Explorer extensibility)
- Results grid pivot tables, charting, or data visualization
- Team settings sync / Redgate Platform integration
- Snippet regular expression transformations
- Multi-cursor editing
- History sync across machines
