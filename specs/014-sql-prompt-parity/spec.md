# Feature Specification: SQL Prompt Parity — Close the Gap

**Feature Branch**: `014-sql-prompt-parity`
**Created**: 2026-04-09
**Status**: Draft
**Input**: User description: "with all gap analysis based on AKML SQL gaps vs. SQL Prompt"

## Summary

AKML SQL already delivers most of Red Gate SQL Prompt's core productivity features: IntelliSense, a seven-stage formatting pipeline, 120+ code-analysis rules, snippet expansion, SQL history, refactoring, and AI assistance. However, two structured reviews of the SQL Prompt 11.3 documentation surfaced concrete capabilities AKML SQL either does not have or has only partially implemented. The first review (12 user stories below) covered the core gaps. A second crawl on 2026-04-09 (User Stories 13–20) added the remaining items the first pass missed, primarily around script navigation/outline, object discovery, smart rename, result-grid productivity, AI explain/index analysis, code-analysis quick-fixes, and completion polish.

The combined gap inventory, grouped by workflow:

1. **Completion UX:** column picker, wildcard-to-column expansion on `*` + Tab, two-tab object definition box (Summary + Script), object tooltips with dependency information, suggestion-list polish (toggle on/off, refresh cache, custom commit keys, category filter, encrypted-object decryption, MS_Description tooltips, parameter highlighting, temp-table IntelliSense, customizable ALTER/INSERT templates).
2. **Refactoring reach:** full `Ctrl+B` chord family, database-wide Smart Rename with dependency preview, Split Table, Find Invalid Objects, Find Unused Variables and Parameters, Summarize Script, Refactor INSERT into UPDATE.
3. **Navigation:** Script Object as ALTER (`F12`), Select in Object Explorer (`Ctrl+F12`), Browse Open Tabs (`Ctrl+Q`).
4. **Execution shortcuts:** Execute Current Batch (`Alt+Shift+F5`), Execute To Cursor (`Ctrl+Shift+F5`).
5. **Formatting ergonomics:** inline `-- formatting off / on` marker blocks, per-text-selection Disable Formatting action.
6. **Safety:** pre-execution warning dialog for `DELETE` / `UPDATE` without `WHERE`, for the same pattern inside `INNER JOIN`, and for procedure/trigger creation that contains those patterns.
7. **Session productivity:** a unified Command Palette that filters across AKML SQL commands, SSMS/VS built-in commands, AKML SQL options and (in SSMS) database objects in the active connection.
8. **Tab management:** environment-based tab coloring with gradients, color inheritance from Registered Server Groups, options surface to edit the environment palette.
9. **Analysis discoverability:** a dockable "All Issues" window plus lightbulb auto-fix actions for individual rules and an Issue Details popup with rule/problem/remediation text.
10. **AI feature reach:** keyboard bindings for open-panel/fix-selection/optimize-selection/manual ghost-text trigger; Explain SQL; Query Index Analysis with ML-based recommendations; auto syntax-error fix popup after failed execution; comment-to-SQL; AI panel history/follow-up suggestions; editor-selection icon.
11. **Result-grid productivity:** Copy as IN Clause, Script as INSERT, Open in Excel with full-precision preservation.
12. **Dual-instance awareness:** completions must use the exact connection of the query window that spawned them, never leaking objects from a different window's server.
13. **Discoverability:** F1 contextual help linking from any AKML SQL UI surface to the matching documentation page.

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

### User Story 13 — Script navigation chords: Summarize, Script-as-ALTER, Find Unused, Open in Object Explorer (Priority: P2)

When working with a long script or hovering over an unfamiliar object reference, SQL Prompt gives keyboard-only authors four navigation moves AKML SQL today does not have: a hierarchical **Summarize Script** outline (`Ctrl+B, Ctrl+S`); **Script Object as ALTER** for the object under the caret (`F12`); **Select in Object Explorer** to jump the OE tree to that object (`Ctrl+F12`); and **Find Unused Variables and Parameters** within the active script (`Ctrl+B, Ctrl+F`). Each one removes a several-step menu dive.

**Why this priority**: every senior SQL author uses these dozens of times per day in SQL Prompt. They are pure productivity wins with low engine surface — most of the underlying queries already exist in `SchemaMetadataService` and the parser.

**Independent Test**: Open a 500-line script with multiple stored-procedure definitions. Press `Ctrl+B, Ctrl+S` — verify a hierarchical outline appears showing each statement type and its line range. Click an entry — verify the editor jumps to that line. Place the caret on a `dbo.MyProc` reference and press `F12` — verify a new query window opens with that procedure scripted as an `ALTER`. Press `Ctrl+F12` on the same identifier — verify Object Explorer expands to that node and selects it. Type a script with an unused `@p2` variable, press `Ctrl+B, Ctrl+F` — verify a panel lists `@p2` with line/column.

**Acceptance Scenarios**:

1. **Given** any script with multiple statements, **When** the user presses `Ctrl+B, Ctrl+S`, **Then** the **Summarize Script** dialog appears showing each top-level statement (CREATE/ALTER/SELECT/INSERT/UPDATE/DELETE/EXEC/USE), grouped and indented, with line numbers.
2. **Given** the Summarize dialog is open, **When** the user clicks an entry, **Then** the editor scrolls to and highlights the matching line.
3. **Given** the caret is on an object reference like `schema.Object`, **When** the user presses `F12`, **Then** a new query window opens containing the `ALTER` script for that object on the active connection.
4. **Given** the same caret position, **When** the user presses `Ctrl+F12`, **Then** Object Explorer expands to and selects the node for that object.
5. **Given** a script containing `DECLARE @unused INT;` that is never read, **When** the user presses `Ctrl+B, Ctrl+F`, **Then** an Unused Variables panel lists `@unused` with the line and column.
6. **Given** the same script also contains a stored procedure with an unused parameter, **When** the analysis runs, **Then** the parameter is also reported.
7. **Given** the user has no active connection, **When** they press `F12`, **Then** a status-bar message indicates a connection is required and no error dialog appears.

---

### User Story 14 — Find Invalid Objects across the database (Priority: P2)

After a schema migration or when adopting an unfamiliar database, the first question is "what is broken?". SQL Prompt's **Find Invalid Objects** scans every object in the connected database for broken references (missing tables, dropped columns, renamed schemas, deleted procedures cited in synonyms) and presents a dockable list. AKML SQL has nothing comparable today.

**Why this priority**: it pays for itself the first time it runs. Migrations routinely leave behind dozens of invalid views and procedures and there is no cheap way to find them all without writing a multi-statement metadata query.

**Independent Test**: Connect to a database with at least 3 known invalid objects (a view referencing a dropped column, a procedure referencing a missing table, a synonym pointing nowhere). Right-click the database in Object Explorer → **Find Invalid Objects**. Verify a dockable window lists all three with object name, schema, type, error message, and the offending line number from the original definition. Select one row, click **Script as ALTER**, and verify the matching `ALTER` script opens in a new query window.

**Acceptance Scenarios**:

1. **Given** a database with broken-reference objects, **When** the user runs Find Invalid Objects, **Then** a dockable tool window lists each invalid object with name, schema, type, error message, and line number.
2. **Given** the list is shown, **When** the user double-clicks a row, **Then** Object Explorer jumps to that object and the error message is shown in the status bar.
3. **Given** the list is shown, **When** the user clicks **Script as ALTER**, **Then** a new query window opens with the `ALTER` script for that object.
4. **Given** the user multi-selects rows and clicks **Script as ALTER**, **Then** all selected scripts are concatenated into one new query window.
5. **Given** a database with no invalid objects, **When** the scan completes, **Then** the window shows "No invalid objects found" and a refresh button.
6. **Given** the user lacks the permissions to read object metadata, **When** the scan runs, **Then** the window reports the permission error and lists only the objects it could verify.

---

### User Story 15 — Smart Rename with dependency preview (Priority: P3)

SQL Prompt's **Smart Rename** scans the entire connected database for every reference to a target table/column/procedure/parameter, generates a single rename script with `sp_rename` plus dependent-object updates, shows the user a preview dialog (Actions / Warnings / Dependencies tabs), and only applies the script after explicit confirmation. AKML SQL today only renames within the current document.

**Why this priority**: it is the single most-feared refactoring without it, because any DB-wide rename otherwise requires manual grep across hundreds of object definitions. The engine already has the schema metadata to do it.

**Independent Test**: In a test database, pick a table column referenced by 3 views, 2 procedures, and 1 trigger. Press `F2` on the column. Verify a dialog appears with Actions / Warnings / Dependencies tabs showing the count of dependent objects, the generated script, and any rename warnings (e.g. extended-property breakage). Click **Apply** — verify the column is renamed and all 6 dependent objects still parse cleanly.

**Acceptance Scenarios**:

1. **Given** the caret is on an identifier the user wants to rename, **When** they press `F2`, **Then** a Smart Rename dialog appears with the current name and a new-name field.
2. **Given** the new name is typed, **When** the user clicks **Preview**, **Then** the dialog shows Actions / Warnings / Dependencies tabs listing every dependent object with its count and updated definition.
3. **Given** there is a name collision in the target schema, **When** the preview runs, **Then** the Warnings tab lists the collision and the **Apply** button is disabled until the user changes the name.
4. **Given** the rename involves an extended property or permission, **When** the script runs, **Then** the property and permission are preserved on the renamed object.
5. **Given** any step of the rename fails (transient connection drop, permission error), **When** the failure is detected, **Then** the rename is rolled back and the original object is unchanged.
6. **Given** the same rename can also be invoked from Object Explorer right-click on the target node, **When** invoked that way, **Then** the same dialog appears.

---

### User Story 16 — Result-grid productivity: Copy as IN Clause, Script as INSERT, Open in Excel (Priority: P3)

After running a query, SQL Prompt lets users right-click the result grid and pick **Copy as IN Clause** (selected rows → `('val1','val2',…)` for the next query), **Script as INSERT** (selected rows → `INSERT INTO X (cols) VALUES …`), or **Open in Excel** (with full numeric precision preserved beyond Excel's default 15-digit truncation). AKML SQL has none of these today.

**Why this priority**: removes the most common copy-paste-and-edit dance in SQL day-to-day work. The Open in Excel precision fix is a niche but high-impact win for finance teams.

**Independent Test**: Run `SELECT TOP 10 Id FROM Customers`. Right-click the result grid → **Copy as IN Clause**. Paste into a new query — verify the clipboard contains `(1, 2, 3, 4, 5, 6, 7, 8, 9, 10)`. Right-click again → **Script as INSERT** — verify the clipboard contains a fully-formed `INSERT INTO Customers (Id) VALUES (1), (2), …` statement. Right-click → **Open in Excel** with a column containing `12345678901234567890.123` — verify the value in Excel is the full number, not the 15-digit truncation Excel does by default.

**Acceptance Scenarios**:

1. **Given** rows selected in a result grid, **When** the user picks **Copy as IN Clause**, **Then** the clipboard contains the values comma-separated, properly quoted by data type, wrapped in parentheses.
2. **Given** rows selected, **When** the user picks **Script as INSERT**, **Then** the clipboard contains a `INSERT INTO <table> (<cols>) VALUES (...), (...), ...` statement that round-trips.
3. **Given** the result grid contains numeric columns with > 15 significant digits, **When** the user picks **Open in Excel**, **Then** the cells in Excel contain the full original precision.
4. **Given** no rows are selected, **When** the user picks any of the three actions, **Then** they operate on all visible rows.
5. **Given** the result grid contains binary or geography/geometry columns, **When** **Script as INSERT** runs, **Then** the binary values are emitted as `0x...` literals and a warning is shown for unsupported types.

---

### User Story 17 — Code Analysis lightbulb quick-fixes and Issue Details popup (Priority: P2)

AKML SQL today shows squiggles for analysis violations but does not offer one-click fixes. SQL Prompt shows a **lightbulb icon** next to each violation (orange for fixable, blue for advisory) and an **Issue Details** popup when the user holds `Ctrl` over the squiggle, with the rule id, the problem text, and a remediation paragraph plus an **Apply Fix** button for the ~27 rules that have a known mechanical fix.

**Why this priority**: it converts the existing 120+ analysis rules from a passive nag into an active assistant. The auto-fix logic for many rules already exists in `RefactoringEngine`; the gap is wiring it to the squiggle UI.

**Independent Test**: Type a query that triggers a known auto-fixable rule (e.g. `BP002` deprecated `!=` operator). Verify an orange lightbulb appears in the gutter on that line. Hover the squiggle while holding `Ctrl` — verify a popup shows the rule id, problem text, remediation, and an **Apply Fix** button. Click the button — verify the `!=` is replaced by `<>`. Repeat for an advisory-only rule and verify the lightbulb is blue and no Apply button is shown.

**Acceptance Scenarios**:

1. **Given** a script triggers an auto-fixable analysis rule, **When** the squiggle is rendered, **Then** an orange lightbulb appears in the gutter on that line.
2. **Given** the squiggle is for an advisory-only rule, **When** rendered, **Then** the lightbulb is blue.
3. **Given** the user holds `Ctrl` and hovers the squiggle, **When** the popup appears, **Then** it contains the rule id, severity, the problem statement, and a remediation paragraph.
4. **Given** the popup is for an auto-fixable rule, **When** the user clicks **Apply Fix**, **Then** the offending text is replaced and the squiggle disappears.
5. **Given** the popup is open, **When** the user clicks **Disable this rule**, **Then** the rule is added to the inline `-- akml-disable` list at the top of the file (or to the per-project `.casettings` if the user picks the project-wide option).
6. **Given** an auto-fix would require schema-aware transformation that depends on cached metadata not yet loaded, **When** the user clicks **Apply Fix**, **Then** the fix is queued until Phase B completes and a status-bar message indicates "waiting for schema".

---

### User Story 18 — AI Explain, Query Index Analysis, auto-fix-on-error, comment-to-SQL (Priority: P3)

The AI feature shortcuts in User Story 10 cover the *transport* (open panel, fix selection, optimize selection, manual ghost-text). This story covers the *missing AI capabilities themselves* that SQL Prompt 11.3 ships:

- **Explain SQL** — selected SQL → plain-language paragraph in the AI panel.
- **Query Index Analysis** — uses an ML model to evaluate candidate indexes for a `SELECT … WHERE …` or `SELECT … JOIN …`, showing existing vs hinted plans with estimated impact and a copyable `CREATE INDEX` script.
- **Auto syntax-error fix popup** — after a failed execution, AKML SQL surfaces a one-click "Fix with AI" toast that pre-fills the AI panel with the failing batch and the SQL Server error.
- **Comment-to-SQL** — when the user types a `-- generate: …` comment line and presses Tab, AKML SQL asks the AI to produce the matching SQL beneath the comment.
- **AI panel history tab** — shows previous prompts and their answers within the current session, with a "revert to this state" action.
- **Editor selection icon** — when the user selects a SQL block, an unobtrusive AI icon appears at the selection's right edge with hover actions: Explain / Fix / Optimize.
- **Follow-up suggestions** — after an AI answer, the panel shows clickable next-prompt suggestions ("Show me an example", "Convert to CTE").

**Why this priority**: every item is independently valuable but each is also independently optional, so they collectively merit P3.

**Independent Test**: Select a 30-line stored procedure body. Right-click → **Explain SQL** — verify the AI panel shows a plain-language explanation. Open a query with a slow `SELECT … WHERE col = @p`. Run **Query Index Analysis** — verify the panel shows the existing plan, a hinted plan with the candidate index, and a `CREATE INDEX` script with an estimated improvement percentage. Run a query that has a typo. Verify a toast appears offering "Fix with AI". Type `-- generate: list the top 10 customers by revenue` and press Tab — verify the AI generates the matching SQL beneath the comment.

**Acceptance Scenarios**:

1. **Given** SQL is selected, **When** the user invokes **Explain SQL**, **Then** the AI panel returns a plain-language explanation within 10 seconds.
2. **Given** a query with a `WHERE` or `JOIN` clause is open, **When** the user invokes **Query Index Analysis**, **Then** the panel returns existing-vs-hinted plans, an estimated impact percentage, and a `CREATE INDEX` script ready to copy.
3. **Given** a query has just failed with a syntax error, **When** the failure dialog closes, **Then** a non-blocking toast offers "Fix with AI" and clicking it pre-fills the AI panel with the failing batch and the SQL Server error message.
4. **Given** the user types `-- generate: <natural language>` on a blank line and presses Tab, **When** the request returns, **Then** the natural-language line is replaced by SQL that satisfies the request, with the original comment retained above it.
5. **Given** the AI panel has history, **When** the user clicks the History tab, **Then** previous prompts and answers are listed in reverse chronological order with "revert to this state" actions.
6. **Given** the user selects a SQL block in the editor, **When** the selection is committed, **Then** an AI icon appears at the right edge of the selection with Explain / Fix / Optimize hover actions.
7. **Given** the AI returned an answer, **When** the panel renders, **Then** 1–3 follow-up prompt buttons are shown beneath the answer.
8. **Given** the user is offline or AI is rate-limited, **When** any of the above features is invoked, **Then** a clear status message indicates the reason and no partial state is left in the panel.

---

### User Story 19 — Completion suggestion polish (toggle, refresh, commit keys, category filter, tooltips, encrypted decryption) (Priority: P3)

Several SQL Prompt completion-popup conveniences are missing from AKML SQL today:

- **Toggle suggestions on/off** (`Ctrl+Shift+P`) — disable IntelliSense for the current session without opening Options.
- **Refresh suggestions cache** (`Ctrl+Shift+D`) — force the schema metadata to re-load (Phase A + Phase B) for the active connection.
- **Custom commit keys** — let the user pick which keystrokes (Space, Dot, Comma, Open Paren, Tab, Enter) commit the highlighted suggestion. Default Tab+Enter only.
- **Suggestion category filter** with `Ctrl+Up` / `Ctrl+Down` to cycle through Tables / Views / Columns / Functions / Procedures / Snippets / All.
- **MS_Description in tooltips** — extended property `MS_Description` shown on hover for every object that has one, with clickable references to the underlying object.
- **Parameter highlighting** in function-call signature popups — the next expected parameter is bolded.
- **Encrypted object decryption** — when the object definition box shows the Script tab for an encrypted procedure/function and the user has DAC permission, the decrypted body is shown (with a clear "decrypted" badge).
- **Customizable ALTER and INSERT statement templates** — when AKML SQL inserts a generated `ALTER TABLE` or `INSERT INTO` statement on commit, the template format is configurable (column order, line breaks, default values).
- **Temporary table IntelliSense** — `#temp` / `##temp` table column metadata is parsed from CREATE/SELECT-INTO and offered in completion within the same statement scope.

**Why this priority**: each item is small but together they remove the "rough edges" the first 12 user stories don't address. Power users notice the difference within an hour.

**Independent Test**: Press `Ctrl+Shift+P` — verify the completion popup stops appearing. Press `Ctrl+Shift+P` again — verify it resumes. Press `Ctrl+Shift+D` — verify a status-bar message says "Refreshing schema cache" and the cache is reloaded. Open Options → Completion → Commit keys, enable Space — verify typing `Ord ` commits `Orders`. Inside the popup press `Ctrl+Down` — verify the category badge changes from "All" to "Tables", filtering the list. Hover an object that has `MS_Description = 'top-level customer table'` — verify the description appears in the tooltip. Type `#tmp1 (a INT, b VARCHAR(50))` then `INSERT INTO #tmp1 (` — verify `a` and `b` are suggested. Open the definition of an encrypted procedure with DAC connection — verify the decrypted body shows.

**Acceptance Scenarios**:

1. **Given** the editor has focus, **When** the user presses `Ctrl+Shift+P`, **Then** the completion popup is suppressed for the rest of the session and a status-bar message confirms "AKML SQL completions: off".
2. **Given** the user presses `Ctrl+Shift+P` again, **When** the next character is typed, **Then** the popup resumes.
3. **Given** the user presses `Ctrl+Shift+D`, **When** the engine receives the request, **Then** Phase A and Phase B run for the active session and a status-bar message indicates progress.
4. **Given** the user has enabled Space as a commit key in Options, **When** they highlight a suggestion and press Space, **Then** the suggestion is committed followed by a single space.
5. **Given** the popup is open in a long suggestion list, **When** the user presses `Ctrl+Down`, **Then** the category filter cycles through Tables → Views → Columns → Functions → Procedures → Snippets → All, with the badge updating each press.
6. **Given** a hovered object has `MS_Description`, **When** the tooltip is rendered, **Then** the description text appears beneath the object name and any cited identifiers in the description are clickable to open their definition box.
7. **Given** the user is inside a function-call's argument list, **When** the parameter signature popup is shown, **Then** the next-expected parameter is bolded.
8. **Given** the user has DAC permission and views an encrypted object's definition, **When** the Script tab is selected, **Then** the decrypted body is shown with a clear "decrypted" badge.
9. **Given** the user has typed `CREATE TABLE #tmp (a INT, b NVARCHAR(20))` earlier in the same script, **When** they then type `INSERT INTO #tmp (`, **Then** completions for `a` and `b` appear.
10. **Given** a generated `ALTER TABLE` template, **When** the user has changed the column-order setting in Options, **Then** the inserted template reflects that order.

---

### User Story 20 — Execute-Current-Batch and Execute-To-Cursor execution shortcuts (Priority: P3)

SSMS exposes only `F5` (execute everything in the editor or selection) and `Shift+F5` (execute current statement). SQL Prompt adds two more shortcuts that AKML SQL can host:

- **Execute Current Batch** (`Alt+Shift+F5`) — execute the batch from the previous `GO` to the next `GO`.
- **Execute To Cursor** (`Ctrl+Shift+F5`) — execute everything from the start of the current batch up to (but not including) the line containing the cursor.

**Why this priority**: a small but daily-used pair that complements User Story 1's safety dialog (the dialog must hook all four execute paths, not just F5/Shift+F5).

**Independent Test**: Open a script with three batches separated by `GO`. Place the cursor in the second batch. Press `Alt+Shift+F5` — verify only the second batch runs. Place the cursor mid-way through the second batch. Press `Ctrl+Shift+F5` — verify only the lines from the start of the batch up to the line above the cursor run.

**Acceptance Scenarios**:

1. **Given** a script with multiple `GO`-separated batches, **When** the user presses `Alt+Shift+F5`, **Then** only the batch containing the cursor is executed.
2. **Given** the cursor is mid-batch, **When** the user presses `Ctrl+Shift+F5`, **Then** the execution runs from the start of the batch up to the line above the cursor and stops.
3. **Given** the safety dialog (User Story 1) is enabled and the about-to-run portion contains an unsafe statement, **When** either shortcut is pressed, **Then** the safety dialog appears the same way it does for `F5` / `Shift+F5`.
4. **Given** the user has no active connection, **When** either shortcut is pressed, **Then** SSMS's standard "no connection" prompt appears (AKML SQL does not interfere).

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
- **Summarize Script with > 10,000 lines**: The outline must virtualize entries; clicking an entry must scroll to the line in under 200 ms.
- **F12 Script-as-ALTER on a schema-bound object**: The generated `ALTER` must include the WITH SCHEMABINDING clause; without it the object would lose its binding.
- **Find Invalid Objects on a database with thousands of objects**: The scan must run in chunks and stream results into the window so users see partial results within 2 seconds.
- **Smart Rename on a column that is also a foreign-key target**: The preview must include the FK-side change; the apply step must be transactional so a failure in either side rolls both back.
- **Smart Rename on a system table or system column**: The preview must refuse and surface a clear "system objects cannot be renamed" message.
- **Copy as IN Clause on a column with NULL values**: NULL values must be omitted (an `IN` clause cannot match NULL); a status message must report the omission count.
- **Script as INSERT for a table with an IDENTITY column**: The generated INSERT must wrap with `SET IDENTITY_INSERT ON/OFF` only if the user opts in via the dialog.
- **Open in Excel with a date-only column**: Excel must show the date without spurious time components.
- **Lightbulb Apply Fix on multiple identical violations in one click**: Holding Shift on Apply Fix must apply the same fix to every occurrence in the document.
- **Issue Details popup over a multi-line span**: The popup must anchor to the first line of the span and the Apply Fix must operate on the entire span.
- **AI Explain on > 5000 lines of selection**: The selection must be truncated with a clear "truncated to first 5000 lines" warning.
- **Query Index Analysis on a table without column statistics**: The result must clearly indicate the recommendation is uncertain because statistics are missing.
- **Comment-to-SQL inside a comment block** (`/* generate: ... */`): The generation must trigger only on single-line `-- generate:` comments to avoid interfering with multi-line documentation.
- **Toggle suggestions off (`Ctrl+Shift+P`) persists across editor windows**: The toggle is per-session; closing all editor windows resets it back to "on" the next time SSMS is launched.
- **Refresh suggestions cache (`Ctrl+Shift+D`) while Phase B is already running**: The request must be coalesced — no second background populate runs concurrently.
- **Custom commit-key conflict with snippet expansion**: If the user picks Tab as a commit key but Tab is also the snippet trigger, snippet expansion takes precedence when the highlighted suggestion is a snippet.
- **Encrypted decryption without DAC permission**: The Script tab must show the encrypted placeholder and a "DAC required" hint; no decryption attempt is made.
- **Temp-table IntelliSense across statements**: A `#temp` table created in one batch must remain visible to completion in later batches within the same script until a `DROP TABLE #temp` is encountered.
- **Execute To Cursor on the very first line of a batch**: Nothing runs (the range is empty); a status-bar message indicates "no statements before cursor".
- **Browse Open Tabs (`Ctrl+Q`) when no tabs are open**: Show an empty popup with a "no open tabs" hint; do not crash.

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

#### Script navigation chords (P2, US13)

- **FR-061**: System MUST provide **Summarize Script** (`Ctrl+B, Ctrl+S`) producing a hierarchical outline of every top-level statement in the active document with a click-to-navigate behavior.
- **FR-062**: System MUST provide **Script Object as ALTER** (`F12`) that opens a new query window containing the `ALTER` definition for the object under the caret on the active connection.
- **FR-063**: System MUST provide **Select in Object Explorer** (`Ctrl+F12`) that expands the Object Explorer tree to and selects the node for the object under the caret.
- **FR-064**: System MUST provide **Find Unused Variables and Parameters** (`Ctrl+B, Ctrl+F`) that lists every declared variable and procedure/function parameter in the active document that is never read, with line and column.

#### Find Invalid Objects (P2, US14)

- **FR-065**: System MUST provide a **Find Invalid Objects** action on the Object Explorer database right-click menu that scans every user object for broken references and lists them in a dockable tool window.
- **FR-066**: The Invalid Objects window MUST contain columns for object name, schema, type, error message, and source line number, and MUST allow multi-row selection.
- **FR-067**: The Invalid Objects window MUST provide **Script as ALTER** that emits the matching `ALTER` script for the selected rows in a new query window (concatenated when multiple rows are selected).
- **FR-068**: Double-clicking an Invalid Objects row MUST jump Object Explorer to that node and surface the error message in the status bar.

#### Smart Rename (P3, US15)

- **FR-069**: System MUST provide a **Smart Rename** action (`F2` editor, Object Explorer right-click) that renames a database object/column/procedure/parameter across every dependent object in the active connection.
- **FR-070**: Smart Rename MUST display a preview dialog with Actions / Warnings / Dependencies tabs showing the generated script, every dependent object, and any name-collision or extended-property warnings before applying.
- **FR-071**: Smart Rename MUST be transactional: any failure mid-script rolls back the rename so the database is left in its original state.
- **FR-072**: Smart Rename MUST preserve extended properties and object permissions on the renamed object.
- **FR-073**: Smart Rename MUST disable the **Apply** button when the preview detects an unresolved name collision.

#### Result-grid productivity (P3, US16)

- **FR-074**: System MUST add **Copy as IN Clause**, **Script as INSERT**, and **Open in Excel** actions to the result-grid right-click menu for every connected database.
- **FR-075**: **Copy as IN Clause** MUST emit values comma-separated with proper string quoting and parenthesis wrapping, suitable to paste directly into a `WHERE col IN (...)` clause.
- **FR-076**: **Script as INSERT** MUST emit a `INSERT INTO <schema.table> (<cols>) VALUES (...), (...)` statement that round-trips when executed.
- **FR-077**: **Open in Excel** MUST preserve full numeric precision beyond Excel's default 15-digit truncation by formatting wide-precision cells as text.
- **FR-078**: All three actions MUST operate on the selected rows when a selection exists, else on every visible row.

#### Code Analysis lightbulbs (P2, US17)

- **FR-079**: For each analysis violation, the system MUST render a gutter lightbulb: orange for auto-fixable rules, blue for advisory-only rules.
- **FR-080**: Holding `Ctrl` over a squiggle MUST show an Issue Details popup containing the rule id, severity, problem text, remediation paragraph, and (for auto-fixable rules) an **Apply Fix** button.
- **FR-081**: Clicking **Apply Fix** MUST replace the offending text with the remediation; the squiggle MUST clear within 1 second.
- **FR-082**: The Issue Details popup MUST include a **Disable this rule** button offering both inline (`-- akml-disable RuleId`) and project-level (`.casettings`) targets.
- **FR-083**: When an auto-fix depends on schema metadata not yet loaded (Phase B in progress), the fix MUST be queued until Phase B completes and a status-bar message MUST indicate "waiting for schema".

#### AI feature reach (P3, US18)

- **FR-084**: System MUST provide an **Explain SQL** action (right-click selection, AKML SQL menu, Command Palette) that returns a plain-language explanation of the selected SQL in the AI panel within 10 seconds.
- **FR-085**: System MUST provide a **Query Index Analysis** action that runs an ML-based evaluation of candidate indexes for the active SELECT/JOIN statement and returns existing-vs-hinted plan summaries plus a copyable `CREATE INDEX` script.
- **FR-086**: After a SQL execution failure (any of the four execute shortcuts), the system MUST surface a non-blocking toast offering "Fix with AI" that, when clicked, pre-fills the AI panel with the failing batch and the SQL Server error message.
- **FR-087**: System MUST provide **comment-to-SQL**: when the user types `-- generate: <natural language>` on a blank line and presses Tab, the natural-language line MUST be replaced by the AI-generated SQL with the original comment retained above it.
- **FR-088**: The AI panel MUST include a History tab listing previous prompts and answers in reverse chronological order with a "revert to this state" action per entry.
- **FR-089**: When a SQL block is selected in the editor, the system MUST render a small AI icon at the right edge of the selection with hover actions: Explain / Fix / Optimize.
- **FR-090**: After every AI answer, the panel MUST render 1–3 clickable follow-up prompt buttons.
- **FR-091**: When AI is unavailable (offline, rate-limited, disabled), all of the above features MUST surface a clear status message and leave no partial state in the panel.

#### Completion polish (P3, US19)

- **FR-092**: System MUST bind `Ctrl+Shift+P` to a session-level toggle that suppresses / resumes the IntelliSense suggestion popup.
- **FR-093**: System MUST bind `Ctrl+Shift+D` to a manual schema cache refresh that re-runs Phase A and Phase B for the active session.
- **FR-094**: System MUST allow the user to configure which keystrokes commit the highlighted suggestion (Space, Dot, Comma, Open Paren, Tab, Enter), with Tab+Enter as the default.
- **FR-095**: While the suggestion popup is open, `Ctrl+Up` and `Ctrl+Down` MUST cycle the category filter through Tables → Views → Columns → Functions → Procedures → Snippets → All, with a visible badge.
- **FR-096**: Object tooltips MUST surface the `MS_Description` extended property when present, and any object identifier cited in the description MUST be a clickable link that opens the cited object's definition box.
- **FR-097**: When the parameter signature popup is shown for a function call, the next-expected parameter MUST be visually emphasised (bold).
- **FR-098**: For encrypted procedures/functions, when the user has DAC permission, the Script tab in the object definition box MUST show the decrypted body with a clear "decrypted" badge; without DAC permission it MUST show the encrypted placeholder.
- **FR-099**: Generated `ALTER TABLE` and `INSERT INTO` statement templates produced by completion MUST follow user-configurable formatting (column order, line breaks, default values).
- **FR-100**: System MUST parse `CREATE TABLE #temp …` and `SELECT … INTO #temp …` statements within the active script and offer column completions for `#temp` references later in the same script scope.

#### Execution shortcuts (P3, US20)

- **FR-101**: System MUST bind `Alt+Shift+F5` to **Execute Current Batch** (run the batch between the surrounding `GO` markers, or the whole document if there are none).
- **FR-102**: System MUST bind `Ctrl+Shift+F5` to **Execute To Cursor** (run from the start of the current batch up to the line above the cursor, exclusive).
- **FR-103**: Both new execution shortcuts MUST trigger the safety check from User Story 1 (FR-001) on the about-to-run text.

#### Discoverability (P3)

- **FR-104**: Every AKML SQL UI surface (Options pages, dialogs, tool windows, Settings sub-tabs) MUST honour `F1` to open the matching documentation page.
- **FR-105**: System MUST provide a **Browse Open Tabs** popup (`Ctrl+Q`) listing every open query tab across all SSMS / VS windows for the active host, with fuzzy search and Enter-to-activate.

### Key Entities

- **Execution Safety Rule**: A named rule (DELETE without WHERE, UPDATE without WHERE, etc.) that the pre-execution safety check evaluates against the about-to-run SQL. Each rule has an enabled/disabled flag (global and per-environment), a severity, a detection routine, and a message template shown in the safety dialog.

- **Environment**: A named grouping (Production, Staging, Development, Testing, Local, Custom...) used for tab coloring and for tinting the safety-warning dialog. Each environment has a name, a color (RGB), a gradient-enabled flag, and an optional label shown in tab tooltips.

- **Tab Color Assignment**: A mapping from a scope (server name / database name / registered server group id) to an Environment, with a priority so group inherits to members and individual server assignments override the group.

- **Analysis Issue**: A single finding produced by the analysis engine. Contains rule id, severity, description, start line, start column, end line, end column, auto-fix availability, and the underlying rule category (BP / PE / ST / SE / DE / DEP / EX / NM).

- **Column Picker Selection**: The transient set of columns the user has checked in the column picker, in insertion order, plus a reference to the parent table and its alias so the insert can qualify correctly.

- **Formatting Disable Region**: A span of text marked by `-- akml-format off` and `-- akml-format on` comments that the formatter pipeline copies verbatim from input to output.

- **Command Palette Entry**: A single searchable item with a display label, a category (AKML Command / AKML Option / SSMS Command / Database Object), a fuzzy-match score, an invoke action, and an optional icon.

- **Script Outline Node**: A single entry in the Summarize Script tree with a statement type (CREATE/ALTER/SELECT/INSERT/UPDATE/DELETE/EXEC/USE/...), a display label, a parent node id (for nested CTEs / EXEC AS REVERT pairs), and an editor offset for click-to-navigate.

- **Invalid Object Record**: An object found by Find Invalid Objects with object name, schema, type (TABLE/VIEW/PROC/FUNC/TRIG/SYNONYM), error message, source line number, and a reference to the dependent object that broke (for chained breakage).

- **Smart Rename Plan**: The bundle of (target identifier, new identifier, list of dependent objects to update, generated `sp_rename` + ALTER script, list of warnings, list of preserved permissions/extended properties). Persisted only for the duration of the preview dialog.

- **Result Grid Action Context**: The set of (table name + schema, column metadata with types and identity flags, selected row payload) needed by Copy as IN Clause / Script as INSERT / Open in Excel.

- **Lightbulb Fix**: A fix descriptor attached to an analysis rule containing the rule id, a "fixable" flag, the remediation text, and a fix-routine reference (the same routines `RefactoringEngine` already exposes).

- **AI Conversation Turn**: A single (prompt, answer) pair in the AI panel history, with timestamp, source action (Explain / Fix / Optimize / Comment-to-SQL / Manual), token count, and an optional "follow-up suggestions" array generated by the AI.

- **Suggestion Toggle State**: Per-session boolean (suppressed / active) controlled by `Ctrl+Shift+P`. Resets to "active" when SSMS is restarted.

- **Custom Commit Key Set**: A user-configurable set of keystrokes that commit the highlighted suggestion. Default `{Tab, Enter}`. Editable via Options → Completion → Commit keys.

- **Temp Table Schema**: An ephemeral schema descriptor for a `#temp` / `##temp` table parsed from the active document, with column name, type, and a scope (statement / batch / file).

- **Browse Open Tabs Entry**: A single entry in the `Ctrl+Q` popup with display label (filename + connection), host (SSMS / VS), tab index, and an activate action.

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
- **SC-011**: After installing the release that includes User Story 13, 80% of test users complete a "find a stored procedure named X and open its definition" task in under 5 seconds via `F12`, vs. 25+ seconds via Object Explorer click-through.
- **SC-012**: User Story 14's Find Invalid Objects scan completes in under 30 seconds for a database with 5,000 user objects on average hardware, and produces zero false positives across a corpus of 10 known-clean databases.
- **SC-013**: User Story 15's Smart Rename, when applied to a column referenced by 20 dependent objects, leaves all 20 dependents parseable and executable in 100% of test runs (zero broken dependents).
- **SC-014**: User Story 17's lightbulb auto-fix reduces the median time-to-fix for an `BP002` (deprecated `!=`) violation from 15+ seconds (manual edit) to under 2 seconds (one click).
- **SC-015**: User Story 18's Explain SQL returns a plain-language explanation in under 10 seconds for 95% of selections of ≤ 500 lines.
- **SC-016**: User Story 18's Query Index Analysis returns a recommendation in under 30 seconds for 95% of `SELECT … WHERE`/`JOIN` statements against tables with up to 1 million rows.
- **SC-017**: User Story 19's `Ctrl+Shift+P` toggle suppresses the popup within 100 ms of being pressed, measured in the editor.
- **SC-018**: User Story 20's `Alt+Shift+F5` and `Ctrl+Shift+F5` execution shortcuts trigger the User Story 1 safety dialog with the same coverage as `F5` / `Shift+F5`, verified by a regression suite of 30 unsafe-statement test cases.
- **SC-019**: F1 contextual help opens the matching documentation page in 100% of AKML SQL UI surfaces (Options pages, dialogs, tool windows).

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
- **A13**: The existing `TsqlParserService` and the `ScriptDom` AST it produces are sufficient for User Story 13's Summarize Script outline; no new parser is needed.
- **A14**: `SchemaMetadataService` already exposes the metadata queries needed by User Story 14's Find Invalid Objects (broken-reference detection via `sys.sql_expression_dependencies` and `sys.sql_modules`).
- **A15**: `RefactoringEngine`'s rename routines are sufficient for User Story 15's Smart Rename core; the new work is the dependency-resolution preview dialog and the transactional apply path.
- **A16**: The result-grid actions in User Story 16 hook the existing SSMS / VS result-grid right-click extensibility surface; no new grid renderer is built.
- **A17**: The auto-fix routines for the ~27 fixable rules in User Story 17 are the same routines `RefactoringEngine` already exposes for the corresponding `Ctrl+B` chords; no new fix engines are written.
- **A18**: The AI feature reach in User Story 18 reuses the existing `AiRequestHandler` for transport, model selection, rate limiting, and credential storage; this spec only covers the new request types and the new UI surfaces.
- **A19**: The completion polish in User Story 19 extends the existing `CompletionEngine` and `AkmlCompletionPopup`; the only persistence is in `config.json` (FR-094, FR-099).
- **A20**: User Story 20's two new execution shortcuts hook the same SSMS / VS execute-command pipeline that User Story 1 already intercepts.
- **A21**: The "second crawl" of the SQL Prompt 11.3 documentation completed on 2026-04-09 is the authoritative source for User Stories 13–20. If a future SQL Prompt release adds capabilities beyond what was crawled, those will be filed as a separate spec, not as further additions to 014.

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
