# Feature Specification: SQL Prompt Parity — Close the Gap

**Feature Branch**: `014-sql-prompt-parity`
**Created**: 2026-04-09
**Status**: Draft
**Input**: User description: "with all gap analysis based on AKML SQL gaps vs. SQL Prompt"

## Summary

AKML SQL already delivers most of Red Gate SQL Prompt's core productivity features: IntelliSense, a seven-stage formatting pipeline, 120+ code-analysis rules, snippet expansion, SQL history, refactoring, and AI assistance. However, a structured review of the SQL Prompt 11.3 documentation (all 20 top-level sections, every sub-page) surfaced a concrete list of capabilities that AKML SQL either does not have or has only partially implemented. This feature specification covers every one of those gaps so SQL authors working in SSMS 20/21/22 and Visual Studio 2019/2022/2026 with AKML SQL can accomplish every task SQL Prompt users can accomplish, without having to fall back to SQL Prompt or to copy-paste workarounds.

The gaps, grouped by workflow, are:

1. **Completion UX:** column picker, wildcard-to-column expansion on `*` + Tab, a two-tab object definition box (Summary + Script), richer object tooltips with dependency information.
2. **Refactoring reach:** full `Ctrl+B` chord family (Apply Casing, Qualify Object Names, Expand Wildcards, Insert Semicolons, Add/Remove Brackets, Inline Procedure, Encapsulate as Procedure), plus database-wide Smart Rename and Split Table.
3. **Formatting ergonomics:** inline `-- formatting off / on` marker blocks in the currently-active style, on-demand formatting-errors panel, per-text-selection Disable Formatting action.
4. **Safety:** pre-execution warning dialog for `DELETE` / `UPDATE` without `WHERE`, for the same pattern inside `INNER JOIN`, and for procedure/trigger creation that contains those patterns.
5. **Session productivity:** a unified Command Palette that filters across AKML SQL commands, SSMS/VS built-in commands, AKML SQL options and (in SSMS) database objects in the active connection.
6. **Tab management:** environment-based tab coloring with gradients, color inheritance from Registered Server Groups / Central Management Servers, and a single options surface to edit the environment palette.
7. **Analysis discoverability:** a dockable "All Issues" window for the current script with grouping, CSV export, and click-to-navigate behavior.
8. **AI feature shortcuts:** dedicated keyboard bindings for open-panel, fix-selection, optimize-selection, and manual ghost-text trigger — so users never need to use the mouse for AI workflows.
9. **Dual-instance awareness:** completions must use the exact connection of the query window that spawned them, never leaking objects from a different window's server (follow-through on the Apr 9 diagnosis that the issue was caption mis-attribution).

This feature does **not** re-scope existing capabilities that already work; it only closes the measurable gaps listed above. Each gap is expressed as an independently shippable user story so the team can deliver incremental value with every milestone.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Pre-execution safety warnings (Priority: P1)

Any mutation without a `WHERE` clause is the single highest-impact source of accidental data loss in SQL authoring. Today AKML SQL has the engine-side `SafetyCheckHandler` but does not show a blocking confirmation dialog before the user hits F5, which means the guard is ineffective for the exact moment it is needed. SQL Prompt prevents accidents at execution time, and users expect the same.

The user writes a DELETE or UPDATE that has no `WHERE`, or writes one embedded in an `INNER JOIN`, or creates a stored procedure / trigger that contains such a statement, and presses the SSMS / VS **Execute** button. AKML SQL must interrupt the execution with a clearly worded confirmation dialog that identifies exactly which statement triggered the warning, names the server and database, and offers **Execute anyway** / **Cancel**. The user should be able to suppress the dialog for statements they deliberately want to run (e.g., a full-table UPDATE used for data migration) without disabling the feature globally.

**Why this priority**: data safety. A single mis-run query on a production environment is materially worse than any other missing feature in this spec. This is the only P1.

**Independent Test**: Open a query in SSMS 22 connected to a test database. Type `DELETE FROM TestTable;` (no `WHERE`). Press F5. Verify a warning dialog appears naming the statement, the server, the database, and offers Execute / Cancel. Press Cancel — nothing runs. Press Execute — the statement runs. Repeat for `UPDATE` without `WHERE`, `DELETE ... INNER JOIN` without `WHERE`, `CREATE PROCEDURE` wrapping a DELETE without `WHERE`, and the same cases run via `Shift+F5` (execute-current-statement). All should trigger the dialog.

**Acceptance Scenarios**:

1. **Given** a query containing `DELETE FROM X;`, **When** the user presses F5, **Then** a warning dialog appears identifying the statement, the server, and the database, and execution does not proceed until the user explicitly confirms.
2. **Given** a query containing `UPDATE X SET Col = 1;`, **When** the user presses F5, **Then** the same confirmation dialog appears.
3. **Given** a `DELETE X FROM X INNER JOIN Y ON X.id = Y.id;` with no `WHERE`, **When** executed, **Then** the dialog appears because the join is not a row filter.
4. **Given** a `CREATE PROCEDURE` or `CREATE TRIGGER` whose body contains `DELETE` or `UPDATE` without `WHERE`, **When** executed, **Then** the dialog appears citing the embedded dangerous statement.
5. **Given** the same unsafe statement after the user has checked "Don't ask again for this session", **When** executed again in the same editor session, **Then** no dialog appears.
6. **Given** the user has disabled the feature in Settings, **When** running any of the above, **Then** no dialog appears (back to stock SSMS behaviour).
7. **Given** the query window is connected to a server tagged as "Production" via tab coloring, **When** the dialog appears, **Then** the environment label is prominently displayed in the dialog header with its environment color so the user cannot miss where they are about to run.

---

### User Story 2 — Column Picker inside the completion popup (Priority: P2)

When a user writes `SELECT ... FROM Customers`, they often want to select a specific subset of columns rather than the three that fuzzy-matching returns. SQL Prompt's Column Picker lets you multi-select columns with Space, toggle table-order vs. alphabetical, and insert all selected columns at once. AKML SQL today only supports one-at-a-time completion, so users have to commit each column individually or fall back to typing `*`.

The user types a table name followed by a dot, then presses `Ctrl+Left Arrow` (or clicks a dedicated Column Picker tab in the popup) and is shown the full column list of the table in defined order, with primary-key and foreign-key badges. The user presses `Space` on each desired column (or `Ctrl+A`), then `Enter` to insert a comma-separated list of those columns at the cursor position, qualified with the table's alias when more than one table is in scope.

**Why this priority**: it is the single most-requested missing productivity feature from the SQL Prompt feature set that daily authors use; it is a well-understood, visually bounded feature that integrates cleanly with the existing `AkmlCompletionPopup` WPF control.

**Independent Test**: Open a query connected to a database with a table that has 20+ columns including a PK and a FK. Type `SELECT FROM dbo.MyTable` (with the cursor before `FROM`). Press `Ctrl+Left` — verify a Column Picker opens inside the completion popup showing all columns in table order, with ⚷ PK and 🔗 FK markers. Press `Space` on 3 columns, then `Enter`. Verify the 3 column names are inserted comma-separated at the cursor, correctly qualified if multiple tables are in scope.

**Acceptance Scenarios**:

1. **Given** a query `SELECT | FROM Customers`, **When** user presses `Ctrl+Left`, **Then** a Column Picker opens listing all columns of `Customers` in defined order with PK / FK badges.
2. **Given** the Column Picker is open, **When** user presses `Space` on multiple rows, **Then** each pressed row shows a checkmark indicating it is selected.
3. **Given** several rows are selected, **When** user presses `Enter`, **Then** the selected columns are inserted at the caret position, comma-separated, in the order they were selected.
4. **Given** more than one table is in scope via FROM/JOIN, **When** the selected columns are inserted, **Then** each is qualified with its table alias (e.g., `c.Name, o.OrderDate`).
5. **Given** the Column Picker is open, **When** user presses `Ctrl+A`, **Then** all columns are selected.
6. **Given** the Column Picker is open, **When** user presses `Ctrl+Right`, **Then** focus returns to the regular suggestion list.
7. **Given** the Column Picker is open, **When** user presses `Esc`, **Then** the picker closes without inserting anything.

---

### User Story 3 — Wildcard expansion (`*` + Tab) (Priority: P2)

SQL Prompt lets users type `SELECT t.* `, press `Tab`, and have the `t.*` replaced with the explicit column list of `t`. This is the fast path for "I want all columns but I need to see them". AKML SQL has an engine-side `WildcardExpansionHandler` but it is only wired to the `Ctrl+B, Ctrl+W` refactoring command, not to an inline `Tab` action.

**Why this priority**: low effort (the engine work is done) with high everyday value. Pair with Column Picker in the same milestone.

**Independent Test**: Type `SELECT * FROM Customers c` in a query window. Position the cursor right after the `*`. Press `Tab`. Verify the `*` expands to the explicit column list of `Customers` (respecting the current formatting style). Repeat with `SELECT c.* FROM Customers c` and verify `c.*` expands to alias-qualified names.

**Acceptance Scenarios**:

1. **Given** `SELECT *| FROM Customers`, **When** user presses `Tab`, **Then** `*` is replaced with the comma-separated column list of `Customers` on one or more lines according to the active format style.
2. **Given** `SELECT c.*| FROM Customers c`, **When** user presses `Tab`, **Then** `c.*` is replaced with alias-qualified column names.
3. **Given** the cursor is not immediately after a `*`, **When** user presses `Tab`, **Then** the normal Tab behaviour (indent or completion commit) applies and no wildcard expansion happens.
4. **Given** `SELECT * FROM Customers c JOIN Orders o`, **When** user positions at `*` and presses `Tab`, **Then** all columns from both tables are inserted alias-qualified.

---

### User Story 4 — Command Palette (Priority: P2)

SQL Prompt's Command Palette (`Alt+S` in SSMS) is a unified fuzzy-searchable launcher for every SQL Prompt command, every SQL Prompt option, SSMS menu commands, and even database objects in the active connection. AKML SQL already has a `CommandPaletteCommand` stub but the existing implementation filters only AKML SQL's own commands. The gap is broadening the source list and polishing the rendering.

**Why this priority**: Discovery. New users cannot memorise every chord shortcut. The Command Palette is the single most effective way to expose the full feature surface of the extension without a menu dive.

**Independent Test**: Press the Command Palette shortcut (`Alt+S` in SSMS or `Alt+P` in VS). Type "format" — verify results include AKML SQL's **Format Document** command, the Options pages that mention formatting, and SSMS's own Edit → Advanced → Format Document. Type a partial table name like "Cust" — verify database objects from the active connection appear (SSMS only). Pick a result — verify the corresponding action runs or the editor navigates to the object.

**Acceptance Scenarios**:

1. **Given** a query editor has focus, **When** user presses the Command Palette shortcut, **Then** a modal popup appears with a search box and an empty result list (or most-recent items if history is present).
2. **Given** the palette is open, **When** the user types a query, **Then** results update live with fuzzy ranking across four result categories: AKML SQL commands, AKML SQL options, SSMS/VS built-in commands, and (SSMS only) database objects from the active connection.
3. **Given** results are displayed, **When** the user presses Down / Enter, **Then** the selected entry is executed: commands run, options open the Settings dialog scrolled to the option, objects open their definition.
4. **Given** the palette is open, **When** the user presses `Esc`, **Then** the palette closes and focus returns to the editor.
5. **Given** no active connection, **When** the user searches, **Then** database objects are omitted from results (no blank category).

---

### User Story 5 — Environment-based tab coloring (Priority: P2)

SQL Prompt colors each query tab according to the environment of its connection: Production red, Staging orange, Development green, etc., with optional gradients. This is the single best visual cue against running a destructive query in the wrong environment. AKML SQL has partial infrastructure (`EnvironmentDetector`, `TabColoringManager`) but the feature is not fully wired to give users the UI to assign colors to servers / databases / registered-server-groups from the query window's right-click menu.

**Why this priority**: It is a safety feature whose value compounds with User Story 1 (pre-execution warnings). Combined they deliver a clear "don't run this in Prod" signal.

**Independent Test**: Right-click a query tab — verify a **Tab Color (Server)** submenu lets the user pick an environment. Open a second query against a different server in a different environment — verify the second tab gets its own distinct color. Open the Options dialog → Tabs → Color and verify that server/database/group → environment assignments can be edited there, and that **Use gradient colors** toggles a lighter-at-top gradient render. Change an environment's color — verify all tabs bound to that environment update immediately.

**Acceptance Scenarios**:

1. **Given** a query tab is open, **When** user right-clicks the tab, **Then** a **Tab Color (Server)**, **Tab Color (Database)** and (if applicable) **Tab Color (Server Group)** submenu lets them pick one of the defined environments.
2. **Given** an environment color has been assigned to a server, **When** any new query window connects to that server, **Then** the tab header is rendered in that environment's color (with gradient if enabled).
3. **Given** the user opens Options → Tabs → Color, **When** they edit the list of environments, **Then** changes apply immediately to all open tabs without restart.
4. **Given** a server belongs to a Registered Server Group that has a color, **When** the user opens a query on that server with no direct assignment, **Then** the tab inherits the group's color.
5. **Given** a query tab is Production-red, **When** the user executes a DELETE without WHERE (User Story 1), **Then** the safety-warning dialog header is visually tinted to match the same red so the environment is unmissable.

---

### User Story 6 — Code Analysis Issues Window (Priority: P2)

SQL Prompt has a dockable "All Issues" window showing every code-analysis finding in the current script, grouped by rule, with click-to-navigate, CSV export, and persistent layout. AKML SQL today renders squiggles inline and supports reporting to the SSMS Error List, but has no dedicated window that shows the entire issue set at a glance.

**Why this priority**: it turns the existing 120+ analysis rules from a "nag" (squiggles while typing) into a "review tool" (audit before committing). The underlying engine work is already complete — this is primarily a shell UI feature.

**Independent Test**: Open a script that has ≥10 known analysis issues (mix of Best Practice, Performance, Style rules). Open **AKML SQL → Show Code Analysis Issues** (or the command palette entry). Verify a dockable tool window appears listing every issue with rule id, severity, description, and line number. Click an issue — verify the editor jumps to and highlights that line. Click the CSV export button — verify a CSV file is saved with the same columns.

**Acceptance Scenarios**:

1. **Given** a script with multiple analysis issues, **When** the user opens the Issues window, **Then** all issues are listed with columns: rule id, severity, description, line, column.
2. **Given** the issues window is open, **When** the user clicks an issue, **Then** the editor scrolls to the issue location and highlights the offending text.
3. **Given** the issues window is open, **When** the user clicks a column header, **Then** the list sorts by that column.
4. **Given** the issues window is open, **When** the user changes the script, **Then** the list refreshes automatically within 1 second of the user pausing typing.
5. **Given** the issues window is open, **When** the user clicks Export, **Then** a CSV file is written containing every visible issue.
6. **Given** the issues window was docked on the right and the user restarts SSMS, **When** the extension loads, **Then** the window re-opens in its previous docked position and size.
7. **Given** the issues window is grouped by Rule, **When** the user toggles grouping off, **Then** issues appear as a flat list and the total count header still shows the overall total.

---

### User Story 7 — Full `Ctrl+B` refactoring chord family (Priority: P3)

SQL Prompt binds a chord family for every refactoring action: `Ctrl+B, Ctrl+Y` (Format), `Ctrl+B, Ctrl+U` (Apply Casing), `Ctrl+B, Ctrl+Q` (Qualify Object Names), `Ctrl+B, Ctrl+W` (Expand Wildcards), `Ctrl+B, Ctrl+C` (Insert Semicolons), `Ctrl+B, Ctrl+B` (Add/Remove Square Brackets), `Ctrl+B, Ctrl+I` (Inline Stored Procedure), `Ctrl+B, Ctrl+E` (Encapsulate as Stored Procedure). AKML SQL has some of these commands via menu but not the complete keyboard chord family wired to the VSCT.

**Why this priority**: keyboard-first authors move significantly faster with chords than with menus. Enables the full SQL Prompt muscle memory transfer. The engine actions largely exist in `RefactoringEngine`; this is VSCT wiring and handler classes.

**Independent Test**: In any query, select text that would benefit from a casing change. Press `Ctrl+B, Ctrl+U`. Verify the selection has its keyword casing normalized per the active style. Repeat for each of the other `Ctrl+B, Ctrl+*` bindings and verify each performs its respective action.

**Acceptance Scenarios**:

1. **Given** an active editor selection, **When** the user presses `Ctrl+B, Ctrl+U`, **Then** keyword casing in the selection is normalized per the active formatting style.
2. **Given** a query containing unqualified object names, **When** the user presses `Ctrl+B, Ctrl+Q`, **Then** all object references become schema-qualified per the Qualification settings.
3. **Given** a query containing `SELECT *`, **When** the user presses `Ctrl+B, Ctrl+W`, **Then** the `*` is expanded to an explicit column list.
4. **Given** a query with missing semicolons, **When** the user presses `Ctrl+B, Ctrl+C`, **Then** statement-terminator semicolons are inserted where required.
5. **Given** a selection of identifiers without brackets, **When** the user presses `Ctrl+B, Ctrl+B`, **Then** brackets are added or removed as a toggle.
6. **Given** a selection containing an `EXEC procName` call, **When** the user presses `Ctrl+B, Ctrl+I`, **Then** the procedure body is inlined in place of the EXEC (when the procedure is simple enough to inline).
7. **Given** a selected block of SQL, **When** the user presses `Ctrl+B, Ctrl+E`, **Then** the user is prompted for a new procedure name and the selection is replaced with an `EXEC newProc @params` while a new CREATE PROCEDURE is inserted above or opened in a new window.

---

### User Story 8 — Object definition box with Summary + Script tabs (Priority: P3)

Today AKML SQL shows a quick-info tooltip for hovered objects. SQL Prompt goes further: when a suggestion is highlighted in the completion popup, an adjacent panel shows a **Summary** tab (columns, parameters, return types, row count) and a **Script** tab (the CREATE statement for the object, including decryption of encrypted procedures when allowed). The panel is resizable and keyboard-dismissable.

**Why this priority**: it turns every completion into a mini "Object Explorer" without leaving the keyboard. Great productivity boost for SQL authors exploring unfamiliar schemas.

**Independent Test**: Type `SELECT * FROM Cust` and select `Customers` in the popup. Verify an adjacent panel shows the column list with data types on the Summary tab. Click Script — verify the `CREATE TABLE` statement appears. Arrow-down to a stored procedure suggestion — verify the Summary tab now shows parameters and the Script tab shows the procedure body. Drag the panel corner — verify it resizes and the new size persists across sessions.

**Acceptance Scenarios**:

1. **Given** the completion popup is open and a table is selected, **When** the object definition box is enabled in Options, **Then** a side panel shows the Summary tab by default listing columns, data types, nullability, and row count.
2. **Given** the definition box is visible, **When** the user clicks the Script tab, **Then** the CREATE statement for the object is shown, with syntax coloring.
3. **Given** a procedure is selected, **When** the Summary tab is visible, **Then** it shows parameters, data types, directions, default values and the declared return type (if any).
4. **Given** the user presses `Ctrl` while the popup is visible, **When** `Ctrl` is held, **Then** both popups become semi-transparent so the user can read the code underneath.
5. **Given** the user drags the bottom-right corner of the definition box, **When** released, **Then** the new size is remembered for the next time the popup opens.

---

### User Story 9 — Inline `-- formatting off / on` markers (Priority: P3)

SQL Prompt honors a special comment marker (`-- SQL Prompt formatting off` / `-- SQL Prompt formatting on`) that tells the formatter to skip a block of code. AKML SQL has a `NoformatScanner` that implements the mechanism using the `--akml-format-off` / `--akml-format-on` marker, but no UI action exposes it — users have to type the marker manually, and the marker is AKML-specific rather than a format-tool-neutral convention.

**Why this priority**: quality-of-life for contributors who want some sections of a script preserved verbatim (ASCII art, aligned UNIONs) but still want the rest formatted.

**Independent Test**: Write a query with a hand-aligned block in the middle. Select the block. Open the action list (`Ctrl`) — pick **Disable formatting for selected text**. Verify the block is now wrapped in `-- akml-format off` / `-- akml-format on` comment markers. Press `Ctrl+K, Ctrl+Y` to format the document. Verify the wrapped block is preserved exactly while the rest of the document is reformatted.

**Acceptance Scenarios**:

1. **Given** a selection in the editor, **When** the user invokes "Disable formatting for selected text", **Then** the selection is wrapped in `-- akml-format off` and `-- akml-format on` comment lines.
2. **Given** a document containing `-- akml-format off` / `-- akml-format on` markers, **When** the user runs Format Document, **Then** the content between the markers is preserved verbatim and content outside is formatted.
3. **Given** nested markers (off inside off), **When** the document is formatted, **Then** the outer marker wins and the parser does not crash.
4. **Given** a marker with only an "off" but no matching "on", **When** the document is formatted, **Then** everything from the "off" to the end of document is left unformatted and no error is shown.

---

### User Story 10 — AI feature keyboard shortcuts (Priority: P3)

SQL Prompt binds keyboard shortcuts to all AI actions: `Alt+Z` opens the AI panel, `Shift+Alt+R` fixes the selected SQL, `Ctrl+Alt+Z` optimizes the selection, `Ctrl+Alt+Up Arrow` manually triggers ghost-text completion. AKML SQL ships the AI handlers but only wires the chat panel command via the menu.

**Why this priority**: keyboard-only workflows for users who have the AI features enabled. Small lift once the commands exist.

**Independent Test**: With AI enabled in Settings, open a query. Press `Alt+Z` — verify the AI chat panel opens. Select a SQL snippet and press `Shift+Alt+R` — verify the AI returns a fixed version. Select and press `Ctrl+Alt+Z` — verify an optimized version is suggested. In an empty area, press `Ctrl+Alt+↑` — verify a ghost-text suggestion appears.

**Acceptance Scenarios**:

1. **Given** AI is enabled, **When** the user presses `Alt+Z`, **Then** the AI chat panel opens and receives focus.
2. **Given** SQL is selected, **When** the user presses `Shift+Alt+R`, **Then** the AI Fix flow runs against the selection and returns a revised version in-panel.
3. **Given** SQL is selected, **When** the user presses `Ctrl+Alt+Z`, **Then** the AI Optimize flow runs against the selection.
4. **Given** the caret is in an editable position, **When** the user presses `Ctrl+Alt+↑`, **Then** a ghost-text AI completion is rendered inline; pressing `Tab` accepts it and `Esc` dismisses it.
5. **Given** AI is disabled in Settings, **When** any of the above shortcuts are pressed, **Then** a brief status bar message indicates AI is disabled and no action is taken.

---

### User Story 11 — Dual-instance awareness in completion (Priority: P3)

On Apr 9 a bug was diagnosed where a new query window opened for Server B could be incorrectly assigned Server A's connection info because `DTE.ActiveDocument` pointed to the previously focused window during `TextViewCreated`. A partial fix landed (per-text-view file path lookup in `SsmsConnectionDetector`). This story ensures the behavior is observable, tested, and permanently guarded.

**Why this priority**: prevents the earlier cross-server leak from recurring after future refactoring.

**Independent Test**: Open query A against Server A, focus it. Open query B against Server B (a different server — NOT local). In query B, type `USE ` — verify only Server B's databases appear. Close query A and reopen it — verify its USE completion still shows Server A's databases. Log file should contain `SsmsConnectionDetector: matched text view to document 'QueryA.sql - ServerA ...'` and not `ActiveDocument` fallbacks.

**Acceptance Scenarios**:

1. **Given** two query windows open against different servers, **When** the user types `USE ` in either, **Then** only that window's server's databases appear.
2. **Given** a new query window is being created, **When** its text view is first registered, **Then** connection detection must use the text view's file path to identify the correct DTE document, never `DTE.ActiveDocument`.
3. **Given** the file path is not yet available (brand-new unsaved buffer), **When** the detector runs, **Then** it returns null and the existing retry loop re-attempts after 500 ms up to 10 times.
4. **Given** the user reconnects a query to a different server via SSMS's Change Connection, **When** the next completion fires, **Then** it uses the new server's databases and the old cache for that session is evicted.

---

### User Story 12 — Settings surface for every new feature (Priority: P3)

Every feature added above must be toggleable and discoverable from the existing Options dialog. Users should not have to edit `config.json` by hand to enable Column Picker, AI shortcuts, safety warnings, tab colors, etc.

**Why this priority**: without this, each new capability ships but is unreachable to users who don't read docs. Low effort when added as part of the feature that introduces each toggle, cumbersome if deferred.

**Independent Test**: After all stories ship, open Options. Verify every feature in this spec has a corresponding toggle or page, that each toggle has an explanatory subtitle, that each is covered by the search box at the top of the Options dialog, and that toggling any one of them updates behavior without restart.

**Acceptance Scenarios**:

1. **Given** a new feature in this spec is installed, **When** the user opens Options and searches for the feature name, **Then** the relevant setting appears and is visually highlighted.
2. **Given** the user toggles a feature off, **When** they close Options and return to the editor, **Then** the feature stops working within 1 second, without requiring an SSMS restart.
3. **Given** the user toggles a feature on, **When** they retry the workflow that was previously inert, **Then** the feature responds normally.

---

### Edge Cases

- **DELETE with subquery WHERE**: A statement like `DELETE FROM X WHERE id IN (SELECT id FROM Y)` is safe — the safety check must recognize a WHERE clause exists even if it references a subquery, and not warn.
- **MERGE statements**: MERGE without a WHEN MATCHED filter is roughly equivalent to DELETE/UPDATE without WHERE. The safety check should treat unfiltered MERGE the same way (warn).
- **Dynamic SQL**: Safety checks parse static T-SQL. A DELETE inside `EXEC sp_executesql N'DELETE FROM X'` is invisible to the parser. The feature must not crash on dynamic SQL; it simply cannot inspect it (documented limitation, same as SQL Prompt).
- **Column Picker with 500+ columns**: The picker must virtualize the list so a wide table does not freeze the UI.
- **Wildcard expansion with columns containing reserved keywords**: The expanded column list must bracket any column whose name is a reserved keyword or contains spaces.
- **Command Palette while no connection is active**: Database objects are simply absent from results. Commands and options still work.
- **Tab coloring in high-contrast Windows themes**: Environment colors must remain legible in high-contrast accessibility modes.
- **Formatting markers inside string literals**: `-- akml-format off` inside a SELECT literal (e.g., `SELECT '-- akml-format off' AS Literal`) must NOT be parsed as a real marker.
- **Safety dialog in unsaved-buffer scenario**: Even if the file has no path, the dialog must still work using the session's connection info from the existing session manager.
- **Two query windows on same server, different databases**: USE completion must use the window-specific database, not cross-mix database lists.
- **AI shortcuts while AI is rate-limited**: Show a clear "rate limited, retry in N seconds" status; do not silently ignore.

## Requirements *(mandatory)*

### Functional Requirements

#### Safety (P1)

- **FR-001**: System MUST intercept SSMS/VS Execute commands (F5, Shift+F5, Alt+F5) and inspect the statement(s) about to run before SSMS sends them to SQL Server.
- **FR-002**: System MUST detect DELETE without WHERE, UPDATE without WHERE, DELETE/UPDATE inside INNER JOIN without WHERE, and MERGE without WHEN MATCHED filter.
- **FR-003**: System MUST also detect these patterns inside the body of `CREATE` / `ALTER PROCEDURE` and `CREATE` / `ALTER TRIGGER` statements that are being executed.
- **FR-004**: When an unsafe statement is detected, the system MUST show a modal confirmation dialog naming the statement, the server, the database, and the environment (if tab-colored), and offer **Execute** / **Cancel**.
- **FR-005**: The dialog MUST default to the Cancel button so accidental Enter presses do not run unsafe SQL.
- **FR-006**: Users MUST be able to suppress the dialog for the current editor session per statement type (DELETE / UPDATE / MERGE / INNER JOIN / in procedure body) via an opt-out checkbox.
- **FR-007**: Users MUST be able to disable the whole feature via Options → Queries → Execution Warnings, with per-pattern toggles.
- **FR-008**: When the target server has been tagged Production via tab coloring, the dialog MUST render its header in the environment color so users cannot miss where they are about to run.
- **FR-009**: The safety check MUST NOT block execution if it takes more than 500 ms to complete; in that case the check yields and execution proceeds with a non-blocking toast warning.

#### Completion UX (P2, P3)

- **FR-010**: System MUST provide an in-popup Column Picker reached via `Ctrl+Left Arrow` from the suggestion list.
- **FR-011**: The Column Picker MUST list columns in the table's defined order by default with an option to toggle alphabetical sort.
- **FR-012**: The Column Picker MUST visually mark primary-key and foreign-key columns with distinctive badges.
- **FR-013**: The Column Picker MUST support multi-selection via `Space` and `Ctrl+A` (select all).
- **FR-014**: The Column Picker MUST insert selected columns comma-separated at the caret position on `Enter` or `Tab`.
- **FR-015**: When multiple tables are in scope via FROM/JOIN, inserted columns MUST be qualified with the table alias to prevent ambiguity.
- **FR-016**: The Column Picker MUST be closable via `Esc` without inserting anything.
- **FR-017**: System MUST expand `*` or `alias.*` to the explicit column list when the user presses `Tab` with the caret immediately after the asterisk.
- **FR-018**: Wildcard expansion MUST respect the active format style for line breaks and indentation.
- **FR-019**: Wildcard expansion MUST bracket any column name that is a reserved keyword or contains special characters.
- **FR-020**: System MUST provide an object definition side panel that shows next to the completion popup with Summary and Script tabs.
- **FR-021**: The Summary tab MUST show columns, data types, nullability, row count for tables/views and parameters, types, return type for procedures/functions.
- **FR-022**: The Script tab MUST show the CREATE statement for the selected object with syntax coloring.
- **FR-023**: The definition panel MUST be resizable by dragging, and the size MUST persist across sessions.
- **FR-024**: When Ctrl is held, both the completion popup and the definition panel MUST become semi-transparent so the user can see code underneath.

#### Dual-instance awareness (P3)

- **FR-025**: Connection detection MUST use the specific text view's file path to locate the corresponding DTE document, and MUST NOT fall back to `DTE.ActiveDocument` at text-view-creation time.
- **FR-026**: On `ConnectionChanged` notifications, the per-session database list cache MUST be invalidated so subsequent USE completions hit the new server.
- **FR-027**: When a session's connection string changes, the cached database list for that session MUST be discarded even if still within its TTL.

#### Refactoring (P3)

- **FR-028**: System MUST provide keyboard chord bindings for all refactoring actions in the SQL Prompt chord family: `Ctrl+B, Ctrl+U` (Apply Casing), `Ctrl+B, Ctrl+Q` (Qualify Object Names), `Ctrl+B, Ctrl+W` (Expand Wildcards), `Ctrl+B, Ctrl+C` (Insert Semicolons), `Ctrl+B, Ctrl+B` (Add/Remove Brackets), `Ctrl+B, Ctrl+I` (Inline Stored Procedure), `Ctrl+B, Ctrl+E` (Encapsulate as Stored Procedure).
- **FR-029**: Each chord action MUST be discoverable in the Command Palette and as a menu item under the AKML SQL menu.
- **FR-030**: Each chord action MUST operate on the current selection if one exists, else on the whole document.

#### Formatting (P3)

- **FR-031**: System MUST provide an action "Disable formatting for selected text" on the editor action list that wraps the selection in `-- akml-format off` / `-- akml-format on` marker comments.
- **FR-032**: The formatter MUST skip content between such markers, preserving it verbatim.
- **FR-033**: The formatter MUST treat unmatched or nested markers gracefully (no crashes, no unformatted whole-document fallback except when an unmatched `off` reaches end of document).
- **FR-034**: Markers inside string literals MUST NOT be parsed as real markers.

#### Code Analysis (P2)

- **FR-035**: System MUST provide a dockable Issues tool window that lists every analysis issue in the current script.
- **FR-036**: The Issues window MUST include columns for rule id, severity, description, line, column.
- **FR-037**: Clicking an issue MUST scroll the editor to and highlight the offending text.
- **FR-038**: The Issues window MUST support sorting by any column, grouping by rule or severity, and exporting to CSV.
- **FR-039**: The Issues window MUST refresh automatically within 1 second of the user pausing typing.
- **FR-040**: The Issues window MUST persist its docked position and size across SSMS restarts.

#### Tab coloring (P2)

- **FR-041**: Right-click on a query tab MUST provide **Tab Color (Server)**, **Tab Color (Database)**, and (when applicable) **Tab Color (Server Group)** submenus.
- **FR-042**: Environment color assignments MUST apply immediately to all open tabs bound to the affected server/database/group, without requiring SSMS restart.
- **FR-043**: The Options → Tabs → Color page MUST allow editing the list of environments, including name, color, gradient toggle.
- **FR-044**: Tabs MUST render with a subtle gradient (lighter at top) when the gradient option is enabled; flat color otherwise.
- **FR-045**: When a server belongs to a Registered Server Group that has a color, the group's color MUST be inherited unless a direct server assignment overrides.
- **FR-046**: Colors MUST remain legible in Windows high-contrast accessibility themes (lighten or darken the text to maintain WCAG AA contrast).

#### Command Palette (P2)

- **FR-047**: System MUST provide a Command Palette reachable via `Alt+S` (SSMS) and `Alt+P` (Visual Studio).
- **FR-048**: The palette MUST aggregate four result sources: AKML SQL commands, AKML SQL Options settings, SSMS/VS built-in commands, and (SSMS only) database objects from the active connection.
- **FR-049**: The palette MUST rank results by fuzzy match score and show each with a small category badge.
- **FR-050**: Selecting an Options result MUST open the Settings dialog scrolled to the matching control and highlight it.
- **FR-051**: Selecting a database-object result MUST navigate the user to that object in Object Explorer or open its definition.
- **FR-052**: The palette MUST remember the 10 most recent selections per IDE host and surface them first when the search box is empty.

#### AI shortcuts (P3)

- **FR-053**: System MUST bind `Alt+Z` to open the AI chat panel.
- **FR-054**: System MUST bind `Shift+Alt+R` to AI Fix on the current selection.
- **FR-055**: System MUST bind `Ctrl+Alt+Z` to AI Optimize on the current selection.
- **FR-056**: System MUST bind `Ctrl+Alt+Up Arrow` to manual AI ghost-text trigger.
- **FR-057**: When AI is disabled in Settings, these shortcuts MUST show a brief status-bar message and take no other action.

#### Settings (P3)

- **FR-058**: Every feature introduced by this specification MUST have a corresponding Options entry with a descriptive subtitle.
- **FR-059**: The Options search box MUST find each new feature by its display label and description.
- **FR-060**: Toggling a feature off MUST take effect within 1 second without requiring an SSMS restart.

### Key Entities

- **Execution Safety Rule**: A named rule (DELETE without WHERE, UPDATE without WHERE, etc.) that the pre-execution safety check evaluates against the about-to-run SQL. Each rule has an enabled/disabled flag (global and per-environment), a severity, a detection routine, and a message template shown in the safety dialog.

- **Environment**: A named grouping (Production, Staging, Development, Testing, Local, Custom...) used for tab coloring and for tinting the safety-warning dialog. Each environment has a name, a color (RGB), a gradient-enabled flag, and an optional label shown in tab tooltips.

- **Tab Color Assignment**: A mapping from a scope (server name / database name / registered server group id) to an Environment, with a priority so group inherits to members and individual server assignments override the group.

- **Analysis Issue**: A single finding produced by the analysis engine. Contains rule id, severity, description, start line, start column, end line, end column, auto-fix availability, and the underlying rule category (BP / PE / ST / SE / DE / DEP / EX / NM).

- **Column Picker Selection**: The transient set of columns the user has checked in the column picker, in insertion order, plus a reference to the parent table and its alias so the insert can qualify correctly.

- **Formatting Disable Region**: A span of text marked by `-- akml-format off` and `-- akml-format on` comments that the formatter pipeline copies verbatim from input to output.

- **Command Palette Entry**: A single searchable item with a display label, a category (AKML Command / AKML Option / SSMS Command / Database Object), a fuzzy-match score, an invoke action, and an optional icon.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In independent testing against a 10-user pool of AKML SQL users, 90% of users who previously relied on SQL Prompt for safety (DELETE/UPDATE warnings) report that AKML SQL's safety feature is at least as protective as SQL Prompt's.
- **SC-002**: After installing this release, the number of "new query" operations where the user opens the Column Picker and inserts 3+ columns per minute is ≥ 1 per average working hour for the test group (evidence that the feature is discoverable and used).
- **SC-003**: In usability testing, 80% of users can invoke Wildcard Expansion (`*` + Tab) after reading the keyboard-shortcut tooltip without additional instruction.
- **SC-004**: In timed tests, users complete a "find the formatting option named X" task via the Command Palette in under 5 seconds on average (vs. >20 seconds via menu navigation).
- **SC-005**: In a manual cross-server test (query window A on Server A, query window B on Server B at the same time), 100% of USE-completion invocations return only the databases of that window's server — zero cross-server leaks across 50 sequential test runs.
- **SC-006**: For analysis issues, 100% of users can locate and navigate to a failing rule in under 10 seconds using the Issues window vs. the existing squiggle-only workflow where the median is 30+ seconds.
- **SC-007**: Tab coloring is assigned to every Production server within 2 minutes of the user's first installation, measured by the percentage of production-tagged tabs after a 1-day shakeout across the test group.
- **SC-008**: The pre-execution safety check completes in under 500 ms for 99% of statements measured across a corpus of 1,000 representative queries (the documented limit from FR-009).
- **SC-009**: Zero regressions: all existing 861+ engine tests and 459+ core tests continue to pass for every milestone delivered under this spec.
- **SC-010**: Every new feature introduced by this spec is controllable via an Options toggle, and the Options search finds each one by name with 100% recall.

## Assumptions

- **A1**: The existing `SafetyCheckHandler` in the Engine provides the detection primitives for User Story 1; this spec only covers the shell-side interception of execute commands and the confirmation dialog.
- **A2**: The existing `EnvironmentDetector` and `TabColoringManager` in the shell provide the runtime infrastructure for User Story 5; this spec only covers the missing menu/options UI and the integration with User Story 1.
- **A3**: The existing `WildcardExpansionHandler` in the Engine handles User Story 3's expansion logic; this spec only covers the Tab-key wiring.
- **A4**: The existing `RefactoringEngine` supports the refactoring actions in User Story 7; this spec only covers the VSCT keyboard bindings and the corresponding command classes.
- **A5**: The existing `AnalysisEngine` produces the issue data consumed by User Story 6; this spec only covers the new dockable tool window.
- **A6**: The existing `AiRequestHandler` provides the AI functions for User Story 10; this spec only covers the keyboard bindings and the command classes.
- **A7**: The existing `CommandPaletteCommand` stub is extended to cover User Story 4; it does not need to be rewritten.
- **A8**: The existing `NoformatScanner` parses the `-- akml-format off` / `-- akml-format on` markers for User Story 9; this spec only covers the editor action that inserts the markers.
- **A9**: The existing completion popup control (`AkmlCompletionPopup`) is extended to host the Column Picker tab and the Object Definition Box for User Stories 2 and 8, rather than being replaced.
- **A10**: Phase A / Phase B schema loading already provides the column metadata consumed by the Column Picker, including PK/FK identification.
- **A11**: The test environment has at least one local SQL Server instance and at least one remote SQL Server instance reachable from the machine — required for User Story 11 acceptance testing.
- **A12**: Settings storage in `%AppData%\AKML SQL\config.json` is the single source of truth for feature toggles; no new persistence layer is introduced.

## Out of Scope

Explicitly NOT included in this specification:

- **Redgate Platform integration**: sharing snippets / styles / analysis rules via an external cloud service.
- **Redgate licensing model**: AKML SQL is MIT-licensed and does not require activation.
- **Azure Synapse, Microsoft Fabric, SQL Server 2025 preview dialects**: AKML SQL already handles mainline T-SQL; dialect extensions are a separate effort.
- **Entra ID MFA custom authentication**: covered by existing SSMS connection infrastructure.
- **Rewriting the completion popup control**: User Stories 2 and 8 extend the existing control; a full WPF rewrite is out of scope.
- **AI model hosting**: AKML SQL calls external AI providers as configured; running local LLMs is out of scope.
- **SQL History migration from an older format**: the existing SQL History implementation is kept as-is.
- **Worked-example tutorials**: documentation is a follow-up effort, not part of this spec.
- **Command Palette "Recent items" persisted across machines**: per-machine only.

## Dependencies

- **SSMS 20 / 21 / 22** — target IDE hosts. Features may degrade in SSMS 20 where custom menu bar APIs differ.
- **Visual Studio 2019 / 2022 / 2026** — secondary IDE hosts.
- **The running Engine process** — all completion, formatting, analysis, and schema loading is out-of-process; any new feature that needs engine data must add an IPC message following the existing pattern.
- **The current settings file at `%AppData%\AKML SQL\config.json`** — any new toggle is added here with a safe default.
