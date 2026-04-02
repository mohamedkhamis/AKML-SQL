# Feature Specification: Productivity Toolkit

**Feature Branch**: `008-productivity-toolkit`
**Created**: 2026-03-24
**Status**: Draft
**Input**: Phase 8 PRD — Productivity Toolkit for SSMS

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Results Grid Search and Aggregates (Priority: P1)

As a database developer reviewing query results, I can press Ctrl+F in the results grid to search/highlight across all columns and rows (with regex support), and when I select cells the status bar instantly shows SUM, AVG, COUNT, MIN, and MAX — so I can quickly find and analyze data without leaving SSMS.

**Why this priority**: Find in Grid and cell aggregates are the two most-requested grid features. They eliminate the need to write additional queries just to locate or summarize data within existing results. Every database tool competitor offers these.

**Independent Test**: Execute a query returning many rows, press Ctrl+F in the grid, type a search term, verify matches highlight. Select a range of numeric cells, verify the status bar shows aggregate values.

**Acceptance Scenarios**:

1. **Given** a results grid is displayed with data, **When** the user presses Ctrl+F while the grid has focus, **Then** a search bar appears at the top of the grid with a text input, match count indicator, and next/previous navigation buttons.
2. **Given** the grid search bar is open, **When** the user types a search term, **Then** all matching cells across all columns are highlighted and the match count is updated in real time.
3. **Given** the grid search bar is open, **When** the user enables the regex toggle and types a pattern (e.g., `^\d{3}-`), **Then** cells matching the regex pattern are highlighted.
4. **Given** a results grid with numeric data, **When** the user selects multiple cells in a numeric column, **Then** the status bar displays SUM, AVG, COUNT, MIN, and MAX of the selected values.
5. **Given** a results grid with mixed data types, **When** the user selects cells containing non-numeric values, **Then** the status bar shows COUNT only (SUM/AVG/MIN/MAX are suppressed for non-numeric data).

---

### User Story 2 - Copy and Export Results Data (Priority: P1)

As a database developer, I can right-click in the results grid and copy selected data in multiple formats (CSV, JSON, XML, INSERT statements, HTML table) or export the entire result set to Excel, CSV, JSON, XML, SQL scripts, or Markdown table — so I can quickly share or import data without manual formatting.

**Why this priority**: Data export is a daily workflow for every database developer. The ability to generate INSERT scripts from result rows alone saves significant time when moving data between environments.

**Independent Test**: Execute a query, right-click selected rows, choose "Copy As > JSON", paste into a text editor and verify valid JSON. Click "Export to Excel" and verify a properly formatted .xlsx file is created.

**Acceptance Scenarios**:

1. **Given** rows are selected in the results grid, **When** the user right-clicks and selects "Copy As > CSV," **Then** the clipboard contains the selected data in CSV format with headers.
2. **Given** rows are selected in the results grid, **When** the user selects "Copy As > INSERT statements," **Then** the clipboard contains valid INSERT INTO statements for each selected row with proper data type formatting (strings quoted, NULLs preserved, dates formatted).
3. **Given** a results grid with data, **When** the user clicks "Export to Excel," **Then** a Save dialog appears, and the exported .xlsx file contains the full result set with auto-formatted column headers and appropriate column widths.
4. **Given** a results grid with data, **When** the user selects "Export to File > Markdown Table," **Then** a properly formatted Markdown table is written to the selected file.
5. **Given** rows are selected in the results grid, **When** the user selects "Generate Script > UPDATE," **Then** an UPDATE statement is generated for each selected row using the primary key columns in the WHERE clause.

---

### User Story 3 - Command Palette (Priority: P1)

As a database developer, I can press Ctrl+Shift+P to open a searchable command launcher that lists all AKML SQL commands and common SSMS commands with fuzzy matching and keyboard shortcut hints — so I can discover and quickly invoke any feature without memorizing shortcuts or navigating menus.

**Why this priority**: The Command Palette is the single-point entry to the entire feature set. It makes all features discoverable and reduces learning curve dramatically. This is the feature that ties everything together.

**Independent Test**: Press Ctrl+Shift+P, type partial text like "form sel", verify "Format Selection" appears with its shortcut hint. Press Enter to execute the command.

**Acceptance Scenarios**:

1. **Given** the user is in the SSMS editor, **When** they press Ctrl+Shift+P, **Then** the Command Palette appears as a centered overlay with a text input field and a scrollable list of all available commands.
2. **Given** the Command Palette is open, **When** the user types "hist," **Then** commands matching "hist" are shown (e.g., "Open SQL History") with fuzzy matching, ranked by relevance.
3. **Given** the Command Palette is showing results, **When** the user selects a command with Enter or mouse click, **Then** the command executes immediately and the palette closes.
4. **Given** the Command Palette is showing results, **When** a command has a keyboard shortcut, **Then** the shortcut is displayed on the right side of the command entry (e.g., "Ctrl+Alt+H").
5. **Given** the user frequently invokes "Format SQL" via the palette, **When** they open the palette next time, **Then** "Format SQL" appears higher in the list due to usage frequency tracking.

---

### User Story 4 - Execute Current Statement (Priority: P2)

As a database developer working in a script with multiple SQL statements, I can press Alt+Enter to execute only the statement at the cursor position without having to manually highlight it — so I can quickly test individual statements within a large script.

**Why this priority**: This is one of the most time-saving editor features. Currently users must manually highlight statements before executing, which is tedious in large scripts. Alt+Enter eliminates this friction.

**Independent Test**: Open a script with multiple statements, place cursor inside one, press Alt+Enter, verify only that statement executes and results appear.

**Acceptance Scenarios**:

1. **Given** a script containing three SELECT statements separated by GO or semicolons, **When** the cursor is inside the second statement and the user presses Alt+Enter, **Then** only the second statement executes and its results are displayed.
2. **Given** a script with a stored procedure definition, **When** the cursor is inside the CREATE PROCEDURE block and the user presses Alt+Enter, **Then** the entire procedure definition (from CREATE to the end of the body) is executed.
3. **Given** a script where the cursor is on a blank line between two statements, **When** the user presses Alt+Enter, **Then** the nearest preceding statement executes.

---

### User Story 5 - Document Outline (Priority: P2)

As a database developer working with large SQL scripts, I can open a Document Outline panel that shows the structural hierarchy of the current script (procedures, functions, CTEs, temp tables, statement types) — so I can navigate large files quickly by clicking on any element to jump to its location.

**Why this priority**: Large SQL scripts (1000+ lines) are common in database development. Without an outline view, navigating them requires scrolling or Ctrl+F searching. The outline provides instant structural awareness.

**Independent Test**: Open a large SQL script with multiple procedures and CTEs, open the Document Outline panel, verify it shows the script structure, click on an item and verify the editor jumps to that location.

**Acceptance Scenarios**:

1. **Given** a SQL script containing a stored procedure and two CTEs, **When** the user opens the Document Outline panel, **Then** the outline shows a tree with the procedure name, each CTE name, and the main SELECT as child nodes.
2. **Given** the Document Outline is visible, **When** the user clicks on a CTE node in the outline, **Then** the editor scrolls to and highlights that CTE definition.
3. **Given** the user edits the script by adding a new temp table, **When** they save or after a brief debounce, **Then** the Document Outline updates automatically to include the new temp table.
4. **Given** a script with nested BEGIN...END blocks, **When** the Document Outline renders, **Then** the nesting is reflected in the tree hierarchy.

---

### User Story 6 - Highlight Occurrences and Bracket Matching (Priority: P2)

As a database developer reading SQL code, when I click on an identifier all other occurrences are highlighted in the editor, and matching pairs (BEGIN/END, parentheses, CASE/END, TRY/CATCH) are visually indicated — so I can quickly understand variable scope and code structure.

**Why this priority**: These are fundamental code navigation aids that significantly improve readability, especially for unfamiliar or complex scripts. They reduce cognitive load when reviewing code.

**Independent Test**: Click on a variable name like `@CustomerID`, verify all occurrences highlight. Place cursor on BEGIN, verify matching END is highlighted with a subtle background color.

**Acceptance Scenarios**:

1. **Given** a SQL script with `@OrderID` used in 5 places, **When** the user clicks on any occurrence of `@OrderID`, **Then** all 5 occurrences are highlighted with a subtle background color.
2. **Given** a SQL script with nested BEGIN...END blocks, **When** the cursor is on a BEGIN keyword, **Then** the matching END keyword is highlighted and a subtle line or background connects them.
3. **Given** a SQL script with CASE...WHEN...END, **When** the cursor is on the CASE keyword, **Then** all WHEN keywords and the closing END are highlighted.
4. **Given** parentheses in a complex WHERE clause, **When** the cursor is on an opening parenthesis, **Then** the matching closing parenthesis is highlighted.
5. **Given** a TRY...CATCH block, **When** the cursor is on BEGIN TRY, **Then** the matching END TRY, BEGIN CATCH, and END CATCH are all highlighted.

---

### User Story 7 - Go to Definition and Peek Definition (Priority: P2)

As a database developer, I can press F12 on any database object name to navigate to its CREATE script definition, or press Alt+F12 to see an inline preview without leaving the current tab — so I can quickly inspect objects referenced in my queries.

**Why this priority**: Go to Definition is a core IDE feature expected by all developers. It eliminates the manual workflow of opening Object Explorer, finding the object, and scripting it.

**Independent Test**: Write `SELECT * FROM dbo.Orders`, press F12 on `dbo.Orders`, verify the table's CREATE TABLE script opens in a new tab. Press Alt+F12 on a stored procedure name, verify an inline preview panel appears.

**Acceptance Scenarios**:

1. **Given** a SQL script referencing `dbo.Orders`, **When** the user places the cursor on `dbo.Orders` and presses F12, **Then** a new tab opens containing the CREATE TABLE script for dbo.Orders.
2. **Given** a SQL script referencing `dbo.sp_GetCustomerOrders`, **When** the user presses Alt+F12 on the procedure name, **Then** an inline peek panel appears below the current line showing the procedure's CREATE script, scrollable and dismissible with Escape.
3. **Given** a script referencing a view `dbo.vw_ActiveOrders`, **When** the user presses F12, **Then** the view's CREATE VIEW script opens in a new tab.
4. **Given** a script referencing an object that doesn't exist in the connected database, **When** the user presses F12, **Then** a message appears: "Definition not found for [object name]."

---

### User Story 8 - Named Regions and Navigate Between Queries (Priority: P3)

As a database developer, I can define collapsible `--region Name` / `--endregion` sections in my scripts for organization, and I can press Ctrl+PageUp/PageDown to jump between SQL statements — so I can efficiently navigate and organize large scripts.

**Why this priority**: Organizational and navigation features that enhance productivity for power users working with large scripts. Lower priority because they augment rather than replace existing capabilities.

**Independent Test**: Add `--region Setup` and `--endregion` markers, verify the region is collapsible. Press Ctrl+PageDown repeatedly, verify cursor jumps to the start of each subsequent statement.

**Acceptance Scenarios**:

1. **Given** a script with `--region Setup` and `--endregion` markers, **When** the user clicks the collapse indicator, **Then** the region collapses to a single line showing "Setup...".
2. **Given** a script with 10 SQL statements, **When** the user presses Ctrl+PageDown, **Then** the cursor jumps to the beginning of the next SQL statement. Pressing repeatedly navigates through all statements sequentially.
3. **Given** the cursor is on the 5th statement, **When** the user presses Ctrl+PageUp, **Then** the cursor jumps back to the beginning of the 4th statement.
4. **Given** a script with `--region` markers and the cursor on a BEGIN keyword, **When** the user presses Ctrl+], **Then** the cursor jumps to the matching END keyword.

---

### User Story 9 - Grid Advanced Features (Priority: P3)

As a database developer analyzing query results, I can view column statistics (min, max, distinct count, null count), transpose single-row results for easy reading, see NULL values distinctly highlighted, and optionally display row numbers — so I have powerful data analysis tools directly in the grid.

**Why this priority**: These are power-user features that enhance the grid experience. Each individually saves small amounts of time but collectively make the results grid a true data analysis tool.

**Independent Test**: Right-click a column header, select "Column Statistics," verify a popup shows min, max, avg, distinct count, null count. Execute a query returning one row, click "Transpose Results," verify rows become columns.

**Acceptance Scenarios**:

1. **Given** a results grid with a numeric column, **When** the user right-clicks the column header and selects "Column Statistics," **Then** a popup shows: min value, max value, average, distinct count, null count, and a simple data distribution summary.
2. **Given** a single-row result, **When** the user clicks "Transpose Results," **Then** the display rotates so each column name appears as a row label with its value beside it.
3. **Given** a results grid with NULL values, **When** NULL highlighting is enabled (default), **Then** NULL cells display with a distinct visual indicator (e.g., italic "NULL" text with a different background color) clearly distinguishable from empty strings.
4. **Given** row numbering is enabled in settings, **When** query results are displayed, **Then** a "Row #" column appears as the first column with sequential numbers starting from 1.
5. **Given** a results grid with data, **When** the user double-clicks a cell, **Then** an edit dialog appears showing the current value, allowing modification, and on confirmation generates and executes an UPDATE statement (with the table's primary key in the WHERE clause).

---

### User Story 10 - Multi-Database Execution (Priority: P3)

As a database developer, I can execute the same SQL script against multiple databases simultaneously and see a comparison view of results — so I can verify consistency across environments or run maintenance scripts across many databases at once.

**Why this priority**: Valuable for DBAs and developers managing multiple environments but is a specialized workflow. Lower priority because it serves a narrower audience than the core editor/grid features.

**Independent Test**: Select 3 databases from a picker, execute a query, verify results from all 3 databases appear in a tabbed or side-by-side comparison view.

**Acceptance Scenarios**:

1. **Given** the user enables multi-database execution, **When** they click "Select Databases," **Then** a dialog shows all databases on the current server with checkboxes for selection.
2. **Given** 3 databases are selected, **When** the user executes a query, **Then** the query runs against all 3 databases in parallel and results appear in separate tabs or a comparison view labeled by database name.
3. **Given** multi-database results are displayed, **When** one database returns an error while others succeed, **Then** the error database shows the error message while successful results display normally.
4. **Given** multi-database execution with different row counts per database, **When** results are shown, **Then** a summary row displays the row count per database for quick comparison.

---

### User Story 11 - Execution Notifications and Timer (Priority: P3)

As a database developer running long queries, I see a live elapsed time display in the status bar during execution, and when a query exceeding a configurable threshold completes, I receive a Windows toast notification — so I can switch to other tasks without watching the query and know immediately when it finishes.

**Why this priority**: Quality-of-life improvement for long-running queries. The timer adds constant value; notifications are useful when multitasking.

**Independent Test**: Execute a long-running query, verify elapsed time updates in the status bar. Configure threshold to 5 seconds, run a 6-second query, verify a Windows toast notification appears on completion.

**Acceptance Scenarios**:

1. **Given** a query is executing, **When** the user observes the status bar, **Then** a live timer shows elapsed time updating every second (e.g., "Executing... 00:05").
2. **Given** the notification threshold is set to 30 seconds and a query runs for 45 seconds, **When** the query completes, **Then** a Windows toast notification appears showing "Query completed in 45 seconds — 1,234 rows returned."
3. **Given** the notification threshold is set to 30 seconds and a query runs for 10 seconds, **When** the query completes, **Then** no notification is shown (below threshold).
4. **Given** a query fails after 60 seconds, **When** the error occurs, **Then** the notification shows "Query failed after 60 seconds" with a brief error summary.

---

### User Story 12 - Object Search and Find All References (Priority: P3)

As a database developer, I can press Ctrl+T to open a quick search for any database object by name (with fuzzy matching), and Shift+F12 to find all references to an object across open files and the database — so I can navigate large databases efficiently and understand object dependencies.

**Why this priority**: Navigation features that are very valuable for complex databases but complement the core Go to Definition feature (US7).

**Independent Test**: Press Ctrl+T, type "Cust," verify matching objects (tables, views, procedures containing "Cust") appear. Select one to jump to its definition. Press Shift+F12 on a table name, verify a panel shows all procedures and views referencing it.

**Acceptance Scenarios**:

1. **Given** the user presses Ctrl+T, **When** a quick search overlay appears and the user types "Ord," **Then** all database objects containing "Ord" are listed (e.g., dbo.Orders, dbo.OrderDetails, dbo.sp_GetOrders) with object type icons.
2. **Given** the user selects an object from the quick search, **When** they press Enter, **Then** the object's CREATE script opens in a new tab (same as F12 Go to Definition).
3. **Given** a SQL script referencing `dbo.Customers`, **When** the user presses Shift+F12 on the table name, **Then** a "References" panel shows all stored procedures, views, and functions in the database that reference `dbo.Customers`.
4. **Given** the References panel is showing results, **When** the user clicks on a reference entry, **Then** the editor navigates to that object's definition at the line where the reference occurs.

---

### User Story 13 - Sticky Scroll and Minimap (Priority: P3)

As a database developer scrolling through large scripts, the current procedure or statement context stays visible at the top of the editor (sticky scroll), and a compact code overview appears in the right margin (minimap) — so I always know where I am in the script.

**Why this priority**: Visual orientation features for large scripts. Sticky scroll is more useful than minimap for SQL, but both are standard modern editor features.

**Independent Test**: Open a long stored procedure, scroll down so the CREATE PROCEDURE line is off-screen, verify the procedure name remains visible at the top. Enable minimap in settings, verify a compact overview appears in the right margin.

**Acceptance Scenarios**:

1. **Given** a long stored procedure (100+ lines), **When** the user scrolls past the CREATE PROCEDURE line, **Then** the procedure name and signature remain pinned at the top of the editor as a sticky header.
2. **Given** sticky scroll is active and the user scrolls into a nested BEGIN...END block, **When** the block start scrolls off-screen, **Then** the sticky scroll shows the nesting chain (e.g., "CREATE PROCEDURE... > IF... > BEGIN").
3. **Given** minimap is enabled in settings, **When** a SQL script is open, **Then** a compact overview of the entire script appears in the right margin, highlighting the visible viewport area and matching the color scheme of SQL syntax highlighting.
4. **Given** the minimap is visible, **When** the user clicks on a region in the minimap, **Then** the editor scrolls to that position in the script.

---

### User Story 14 - CRUD Generation and Script As (Priority: P3)

As a database developer, I can right-click a table in Object Explorer and generate full CRUD stored procedures (SELECT, INSERT, UPDATE, DELETE) or script a table as CREATE, INSERT, SELECT, MERGE, BCP, etc. — so I can quickly scaffold common database patterns.

**Why this priority**: Code generation features that save time when creating repetitive boilerplate. Lower priority because they are less frequently used than editor and grid features.

**Independent Test**: Right-click a table in Object Explorer, select "Generate CRUD Procedures," verify 4 stored procedures (GetById, Insert, Update, Delete) are generated in a new tab with proper parameters and error handling.

**Acceptance Scenarios**:

1. **Given** a table `dbo.Products` with columns (ProductID PK, Name, Price, CategoryID FK), **When** the user right-clicks it in Object Explorer and selects "Generate CRUD Procedures," **Then** a new tab opens with 4 stored procedures: `sp_Products_GetById`, `sp_Products_Insert`, `sp_Products_Update`, `sp_Products_Delete` with appropriate parameters, error handling, and comments.
2. **Given** a table `dbo.Orders`, **When** the user right-clicks and selects "Script As > MERGE," **Then** a MERGE statement template is generated using the table's columns with source/target placeholders.
3. **Given** a table `dbo.Customers`, **When** the user selects "Script As > BCP," **Then** a BCP command template is generated with the correct table name, format file reference, and common options.

---

### User Story 15 - Connection Aliases (Priority: P3)

As a database developer working with multiple servers, I can assign friendly names to server connections (e.g., "Production - East Coast" instead of "SQL-PROD-EC-01\INST02") — so connection lists and tab titles are more readable and meaningful.

**Why this priority**: A quality-of-life improvement that enhances readability but doesn't add new functional capabilities.

**Independent Test**: Open connection alias settings, assign "Prod East" to a server, verify the alias appears in connection dropdowns and tab titles instead of the raw server name.

**Acceptance Scenarios**:

1. **Given** the user configures an alias "Production East" for server "SQL-PROD-EC-01\INST02," **When** they connect to that server, **Then** all UI elements (tab titles, status bar, history entries) show "Production East" instead of the raw server name.
2. **Given** aliases are configured, **When** the user opens the connection dialog, **Then** the server list shows aliases alongside server names.
3. **Given** an alias is defined, **When** the user hovers over the alias in the tab title, **Then** a tooltip shows the actual server name and connection details.

---

### Edge Cases

- What happens when Find in Grid searches a result set with 1 million rows? The search operates on the in-memory grid data with progressive results — first matches appear immediately and remaining matches populate in the background.
- What happens when the user tries to export results larger than available memory to Excel? The export streams data to disk progressively rather than loading the entire result set into memory, and shows a progress indicator.
- What happens when Go to Definition is invoked on an object with no CREATE script permissions? The system shows a message: "Insufficient permissions to view the definition of [object name]."
- What happens when cell editing attempts an UPDATE on a table without a primary key? The system shows a warning: "Cannot generate UPDATE statement — table has no primary key. Use a WHERE clause manually."
- What happens when multi-database execution targets databases with different schemas? Each database's results are displayed independently — schema differences are reflected in each database's own error/results panel.
- What happens when Named Regions have mismatched `--region`/`--endregion` markers? The system treats unmatched markers as regular comments and does not collapse them.
- What happens when the Command Palette has no matching results? The palette displays "No commands match '[search text]'" with a suggestion to check spelling.
- What happens when the user opens the Document Outline for an empty script? The outline shows "(Empty document)" placeholder text.
- What happens when Column Statistics is requested on a column with all NULL values? Statistics show: count=N, null count=N, distinct count=0, with min/max/avg displayed as "N/A."

## Requirements *(mandatory)*

### Functional Requirements

**Results Grid**:
- **FR-001**: System MUST provide a Find in Grid feature (Ctrl+F in grid context) that searches across all columns and rows with text matching and optional regex support, highlighting all matches with a match count indicator and next/previous navigation.
- **FR-002**: System MUST display aggregate calculations (SUM, AVG, COUNT, MIN, MAX) in the status bar when one or more cells are selected in the results grid. Non-numeric selections show COUNT only.
- **FR-003**: System MUST support right-click "Copy As" in the results grid with at least these formats: CSV, TSV, JSON, XML, HTML table, and INSERT statements.
- **FR-004**: System MUST support one-click export of the full result set to Excel (.xlsx) with auto-formatted headers and appropriate column widths.
- **FR-005**: System MUST support exporting results to file in CSV, JSON, XML, SQL (INSERT scripts), and Markdown table formats.
- **FR-006**: System MUST support generating INSERT, UPDATE, or DELETE scripts from selected rows in the results grid, using primary key columns for WHERE clauses.
- **FR-007**: System MUST support inline cell editing that generates and executes an UPDATE statement with user confirmation, using the table's primary key in the WHERE clause.
- **FR-008**: System MUST provide column statistics on right-click of a column header: min, max, average, distinct count, null count, and data distribution summary.
- **FR-009**: System MUST support transposing a single-row result set so column names become row labels.
- **FR-010**: System MUST visually distinguish NULL values from empty strings in the results grid with a configurable visual indicator (default: enabled).
- **FR-011**: System MUST support an optional row numbers column in the results grid (default: disabled).
- **FR-012**: System MUST keep the results grid header row visible when scrolling.

**Editor**:
- **FR-013**: System MUST provide a Command Palette (Ctrl+Shift+P) that lists all AKML SQL commands and common SSMS commands, with fuzzy search, keyboard shortcut display, and usage-frequency-based ranking.
- **FR-014**: System MUST provide a Document Outline panel showing the structural hierarchy of the current script (procedures, functions, CTEs, temp tables, statement types) with click-to-navigate functionality and auto-refresh on edits.
- **FR-015**: System MUST highlight all occurrences of an identifier when the user clicks on it in the editor.
- **FR-016**: System MUST visually indicate matching pairs: BEGIN/END, parentheses, CASE/END, TRY/CATCH, and IF/ELSE blocks.
- **FR-017**: System MUST support Ctrl+PageUp/PageDown to navigate between SQL statements in a script, and Ctrl+] to jump to matching pair elements.
- **FR-018**: System MUST support collapsible named regions using `--region Name` / `--endregion` comment markers.
- **FR-019**: System MUST support sticky scroll — displaying the current procedure or statement context at the top of the editor when the defining line has scrolled out of view.
- **FR-020**: System MUST support an optional minimap (compact code overview in the right margin) with syntax-colored rendering and click-to-navigate (default: disabled).

**Execution**:
- **FR-021**: System MUST support executing only the SQL statement at the cursor position via Alt+Enter, without requiring manual text selection.
- **FR-022**: System MUST support executing all statements from the beginning of the script up to the cursor position.
- **FR-023**: System MUST support multi-database execution — running the same script against multiple selected databases simultaneously with results displayed per-database.
- **FR-024**: System MUST display a live elapsed time counter in the status bar during query execution, updating every second.
- **FR-025**: System MUST send a Windows toast notification when a query exceeding a configurable time threshold (default 30 seconds) completes, showing duration and row count or error summary.
- **FR-026**: System MUST support right-click on a table in Object Explorer to generate full CRUD stored procedures (SELECT by PK, INSERT, UPDATE, DELETE) with parameters, error handling, and comments.
- **FR-027**: System MUST provide extended "Script As" options for tables including CREATE, INSERT, SELECT, MERGE, and BCP templates.

**Navigation**:
- **FR-028**: System MUST support F12 (Go to Definition) to navigate to the CREATE script of any referenced database object in a new tab.
- **FR-029**: System MUST support Alt+F12 (Peek Definition) to show an inline scrollable preview of an object's CREATE script without opening a new tab, dismissible with Escape.
- **FR-030**: System MUST support Shift+F12 (Find All References) to list all database objects (procedures, views, functions) that reference the selected object.
- **FR-031**: System MUST provide Ctrl+T (Object Search) — a quick overlay for searching any database object by name with fuzzy matching and jump-to-definition on selection.
- **FR-032**: System MUST support connection aliases — user-defined friendly names that appear in place of raw server names throughout the UI (tab titles, status bar, history, connection dialogs).
- **FR-033**: System MUST allow all productivity features to be individually enabled or disabled via configuration.

### Key Entities

- **Command Entry**: An item in the Command Palette. Key attributes: name, category, keyboard shortcut (optional), execution action, usage count (for ranking). Categories include: Format, Analysis, History, Refactoring, Navigation, Settings.
- **Grid Export Format**: A supported output format for grid data export. Key attributes: format identifier, file extension, MIME type, supports streaming. Includes: CSV, TSV, JSON, XML, XLSX, HTML, SQL (INSERT), Markdown.
- **Document Outline Node**: A structural element in the script outline. Key attributes: name, node type (Procedure, Function, CTE, TempTable, Statement, Region, Block), line number, nesting level, children.
- **Connection Alias**: A user-defined friendly name for a server connection. Key attributes: alias name, server name, instance name (optional). Stored in user configuration.
- **Multi-Database Target**: A database selected for multi-database execution. Key attributes: database name, server name, execution status, result set, error message.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can find any value in a results grid of up to 100,000 rows within 2 seconds using Find in Grid.
- **SC-002**: Selected cell aggregates (SUM, AVG, COUNT, MIN, MAX) appear in the status bar within 500 milliseconds of cell selection for grids up to 100,000 rows.
- **SC-003**: The Command Palette opens in under 500 milliseconds and shows the first matching results within 100 milliseconds of typing.
- **SC-004**: Go to Definition (F12) retrieves and displays an object's CREATE script in under 3 seconds for typical database objects.
- **SC-005**: Document Outline updates within 1 second of a script edit, even for scripts with 5,000+ lines.
- **SC-006**: Execute Current Statement (Alt+Enter) correctly identifies and executes the single statement at cursor position 100% of the time for well-formed SQL.
- **SC-007**: Excel export completes in under 10 seconds for result sets of up to 100,000 rows.
- **SC-008**: Multi-database execution begins within 2 seconds across up to 10 databases simultaneously.
- **SC-009**: All 35 productivity features are individually configurable — users can enable or disable any feature without affecting others.
- **SC-010**: The combined overhead of all enabled productivity features adds less than 200 MB memory and no perceptible startup delay to SSMS.

## Assumptions

- Users work in SSMS 20, 21, or 22 and use standard query execution methods.
- The results grid is the standard SSMS DataGridView-based grid — features hook into the existing grid infrastructure.
- Database objects for Go to Definition and CRUD generation are accessible via the active connection's schema cache (Phase 2 infrastructure).
- The Command Palette registers all existing AKML SQL commands (from Phases 1–7) plus common SSMS commands accessible via DTE.
- Excel export uses a library or built-in capability to produce .xlsx files without requiring Excel to be installed.
- Connection aliases are stored in the user's config.json alongside other AKML SQL settings.
- Multi-database execution uses the same connection credentials across all selected databases on the same server.
- Named regions (`--region`/`--endregion`) are T-SQL comments and do not affect script execution.

## Scope Boundaries

**In scope:**
- All 35 features listed in the PRD (13 grid, 10 editor, 7 execution, 5 navigation)
- Command Palette with fuzzy search and frequency ranking
- Results grid find, aggregates, export (CSV/JSON/XML/XLSX/MD/SQL), copy-as, cell editing, column stats, transpose, null highlight, row numbers, frozen headers
- Document Outline with auto-refresh
- Highlight occurrences and bracket/pair matching
- Named regions, sticky scroll, minimap
- Execute current statement, execute to cursor, multi-database execution
- Execution timer, completion notifications
- Go to Definition (F12), Peek Definition (Alt+F12), Find All References (Shift+F12)
- Object Search (Ctrl+T), connection aliases
- CRUD generation and extended Script As options
- All settings individually configurable

**Out of scope:**
- Multi-cursor editing (Ctrl+Alt+Click, Ctrl+D) — deferred to a future phase due to deep editor integration complexity
- Data visualizer (chart popup) — deferred to a future phase as it requires a charting library
- Script execution across multiple servers (multi-database is same-server only)
- Object Explorer integration beyond right-click context menu
- Custom command creation by users (palette is read-only command list)
