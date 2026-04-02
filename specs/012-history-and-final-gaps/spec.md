# Feature Specification: SQL History Enhancements & Final Parity Gaps

**Feature Branch**: `012-history-and-final-gaps`  
**Created**: 2026-04-02  
**Status**: Draft  
**Input**: Fill the 7 remaining gaps identified in `doc/AKML_SQL_Gap_Analysis_1.md` — 5 SQL History enhancements + Copy as IN clause + Unformat action.

## Gap Source

From `AKML_SQL_Gap_Analysis_1.md`, these are the only remaining items preventing absolute 100% SQL Prompt v11 parity:

| # | Gap | Area | Effort |
|---|-----|------|--------|
| 1 | Starring / Favorites | SQL History | Small |
| 2 | Version history per query | SQL History | Medium |
| 3 | Advanced search syntax | SQL History | Medium |
| 4 | Rename closed queries | SQL History | Small |
| 5 | Search match highlighting | SQL History | Small |
| 6 | Copy as IN clause | Results Grid | Small |
| 7 | Unformat action | Formatting | Small |

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Star / Favorite Queries in History (Priority: P1)

A developer frequently runs the same diagnostic query and wants to find it quickly in history. They click the star icon next to a query in the History tool window. The query is marked as a favorite and appears when they filter to "Starred" queries. Starred queries are exempt from retention auto-trim, so they persist indefinitely.

**Why this priority**: Starring is the most impactful History enhancement. Without it, important queries disappear when retention policies clean up old entries. Every DBA has a handful of go-to queries they want permanent access to.

**Independent Test**: Open History, star a query, switch to "Starred" filter, verify only starred queries appear. Wait past the retention period, verify starred queries survive cleanup.

**Acceptance Scenarios**:

1. **Given** the History tool window is open with query entries, **When** the user clicks the star icon next to a query, **Then** the star fills/highlights and the query is marked as a favorite.
2. **Given** starred queries exist, **When** the user clicks the "Starred" filter button, **Then** only starred queries are shown.
3. **Given** a query is starred, **When** the user clicks the star icon again, **Then** the star is cleared and the query is no longer a favorite.
4. **Given** the retention policy runs (e.g., delete entries older than 30 days), **When** starred queries are older than the retention period, **Then** they are NOT deleted.
5. **Given** the user is on the "All" filter, **When** starred queries are present, **Then** they display with a visible star indicator so they are distinguishable from unstarred queries.

---

### User Story 2 - Advanced Search Syntax in History (Priority: P1)

A developer needs to find a specific query they ran last week on a production server. They type `server:SQLPROD01 sql:ALTER TABLE` in the History search box. The system filters to queries matching both criteria. They can also use wildcards (`proc*`), exact phrases (`"CREATE VIEW"`), and boolean operators (`OR`, `NOT`).

**Why this priority**: Basic text search is insufficient for large history databases. DBAs often need to find queries by server, database, date range, or specific SQL patterns. Advanced search turns History from a simple log into a powerful query retrieval tool.

**Independent Test**: Type `server:PROD sql:DELETE` in the search box, verify results are filtered to queries run on servers matching "PROD" containing "DELETE".

**Acceptance Scenarios**:

1. **Given** the search box is focused, **When** the user types `server:PROD`, **Then** only queries executed on servers matching "PROD" are shown.
2. **Given** the search box, **When** the user types `sql:ALTER TABLE`, **Then** only queries whose SQL text contains "ALTER TABLE" are shown.
3. **Given** the search box, **When** the user types `database:Northwind`, **Then** only queries run against the "Northwind" database are shown.
4. **Given** the search box, **When** the user types `starred:true`, **Then** only starred queries are shown (equivalent to the Starred filter).
5. **Given** the search box, **When** the user types `proc*`, **Then** queries containing words starting with "proc" (procedure, process, etc.) are shown.
6. **Given** the search box, **When** the user types `"CREATE VIEW"` (in quotes), **Then** only queries containing the exact phrase "CREATE VIEW" are shown.
7. **Given** the search box, **When** the user types `ALTER OR DROP`, **Then** queries containing either "ALTER" or "DROP" are shown.
8. **Given** the search box, **When** the user types `SELECT NOT temp`, **Then** queries containing "SELECT" but not "temp" are shown.

---

### User Story 3 - Copy as IN Clause from Results Grid (Priority: P2)

A developer runs a query and wants to use the result values in a WHERE IN clause. They select cells in a column, right-click, and choose "Copy as IN Clause". The clipboard contains a ready-to-use `WHERE ColumnName IN ('val1', 'val2', 'val3')` expression.

**Why this priority**: This is a frequently used SQL Prompt feature for ad-hoc query building. Developers constantly copy result values to use as filter criteria in subsequent queries.

**Independent Test**: Run `SELECT CustomerID FROM dbo.Customers`, select several CustomerID cells, right-click "Copy as IN Clause", paste, verify the output is `WHERE CustomerID IN (1, 2, 3)`.

**Acceptance Scenarios**:

1. **Given** the user selects cells in a single column, **When** they choose "Copy as IN Clause", **Then** the clipboard contains `WHERE ColumnName IN (val1, val2, val3)` with appropriate quoting (strings quoted, numbers unquoted).
2. **Given** the selected values contain NULL, **When** the IN clause is generated, **Then** NULL values are excluded from the IN list and a comment notes "NULL values excluded".
3. **Given** the selected values are strings, **When** the IN clause is generated, **Then** values are wrapped in single quotes with internal quotes escaped (`'O''Brien'`).
4. **Given** the user selects cells in multiple columns, **When** they choose "Copy as IN Clause", **Then** only the first selected column is used for the IN clause.

---

### User Story 4 - Unformat Action (Priority: P2)

A developer wants to strip all formatting whitespace from a SQL statement to produce a compact single-line version (useful for embedding in log messages or dynamic SQL strings). They select the SQL, open the Actions List or use a keyboard shortcut, and choose "Unformat". All extra whitespace, indentation, and line breaks are collapsed to minimal spacing.

**Why this priority**: This is the inverse of Format Document and is a standard SQL Prompt action. It's useful for creating compact SQL strings for logging, EXEC statements, or copying into code.

**Independent Test**: Select a formatted multi-line SELECT statement, invoke Unformat, verify the output is a single-line compact statement with minimal whitespace.

**Acceptance Scenarios**:

1. **Given** the user selects a formatted multi-line SQL statement, **When** they invoke "Unformat", **Then** the SQL is collapsed to minimal whitespace (single spaces between keywords, no line breaks, no indentation).
2. **Given** the user has no selection, **When** they invoke "Unformat", **Then** the entire document is unformatted.
3. **Given** the SQL contains string literals with intentional whitespace, **When** unformatting occurs, **Then** whitespace inside string literals is preserved unchanged.
4. **Given** the SQL contains comments, **When** unformatting occurs, **Then** single-line comments (`--`) are preserved on their own lines (to avoid commenting out subsequent code).

---

### User Story 5 - Search Match Highlighting in History (Priority: P3)

When a user searches in the History tool window, matched text in the code preview pane is highlighted with a yellow/ochre background, making it easy to spot why each result matched.

**Why this priority**: Visual feedback for search matches is a polish feature that improves the search experience.

**Independent Test**: Search for "ALTER" in History, verify the word "ALTER" is highlighted in the code preview for each matching entry.

**Acceptance Scenarios**:

1. **Given** the user types a search term in the History search box, **When** results are displayed, **Then** matching text in the code preview pane is highlighted with a yellow/ochre background.
2. **Given** multiple matches exist in a single query, **When** the preview is displayed, **Then** all occurrences are highlighted.
3. **Given** the user clears the search, **When** the preview updates, **Then** all highlighting is removed.

---

### User Story 6 - Version History per Query (Priority: P3)

A developer opens the History tool window and selects a query. A version timeline panel shows all timestamped snapshots of that query (created on each auto-save event). They click a version to preview its content and can diff any two versions.

**Why this priority**: This is a powerful feature for tracking query evolution, but has higher implementation complexity and the diff view already provides partial coverage.

**Independent Test**: Open a query, make edits, wait for auto-save, verify a new version appears in the timeline. Click an older version, verify its content is shown.

**Acceptance Scenarios**:

1. **Given** a query has been auto-saved multiple times, **When** the user selects it in History, **Then** a version timeline shows all timestamped snapshots with timestamps.
2. **Given** the version timeline is visible, **When** the user clicks a version entry, **Then** the code preview shows the content of that specific version.
3. **Given** two versions are selected, **When** the user clicks "Compare", **Then** a side-by-side diff view shows the changes between versions.

---

### User Story 7 - Rename Closed Queries in History (Priority: P3)

A developer finds a useful query in their history but it has no descriptive name (just "Query1.sql" or "SQLQuery4.sql"). They right-click the entry and choose "Rename" to give it a descriptive name like "Monthly Sales Report". The name persists in history.

**Why this priority**: This is a small convenience feature that improves history organization.

**Independent Test**: Right-click a closed query in History, choose Rename, type a new name, verify it persists after reopening History.

**Acceptance Scenarios**:

1. **Given** a closed query in History, **When** the user right-clicks and selects "Rename", **Then** an inline text editor appears allowing them to type a new name.
2. **Given** the user types a new name and presses Enter, **When** the rename completes, **Then** the new name is displayed in the History list and persists across sessions.
3. **Given** the user presses Escape during rename, **When** the edit is cancelled, **Then** the original name is restored.

---

### Edge Cases

- What happens when the user stars a query and then the History database is compacted? Starred queries must survive compaction.
- What happens when an advanced search query has invalid syntax (e.g., unclosed quotes)? Fall back to plain text search with the raw query.
- What happens when Copy as IN clause has more than 1000 values? Include a comment noting the SQL Server IN clause limit.
- What happens when Unformat encounters SQLCMD directives (`:setvar`, `:r`)? Preserve SQLCMD lines on their own lines.
- What happens when version history has hundreds of versions for a single query? Show the most recent 50 with a "Load more" option.

## Requirements *(mandatory)*

### Functional Requirements

**Starring / Favorites:**
- **FR-001**: Each query entry in the History tool window MUST have a clickable star icon to toggle favorite status.
- **FR-002**: A "Starred" filter button MUST be available alongside existing All/Open/Closed filters.
- **FR-003**: Starred queries MUST be exempt from retention auto-trim policies.
- **FR-004**: Star status MUST persist across sessions (stored in the History database).

**Advanced Search:**
- **FR-005**: The History search box MUST support prefix-based filters: `server:`, `sql:`, `database:`, `name:`, `starred:`, `open:`.
- **FR-006**: The search MUST support wildcard matching (`*` for any characters, `?` for single character).
- **FR-007**: The search MUST support exact phrase matching using double quotes (`"exact phrase"`).
- **FR-008**: The search MUST support boolean operators: `OR` (union) and `NOT` (exclusion).
- **FR-009**: Invalid search syntax MUST fall back to plain text search without errors.

**Copy as IN Clause:**
- **FR-010**: The results grid context menu MUST include a "Copy as IN Clause" option.
- **FR-011**: The generated IN clause MUST use the column name from the selected column header.
- **FR-012**: String values MUST be single-quoted with internal quotes escaped. Numeric values MUST be unquoted.
- **FR-013**: NULL values MUST be excluded from the IN list with an explanatory comment.

**Unformat Action:**
- **FR-014**: An "Unformat" command MUST be available from the AKML SQL menu and Actions List.
- **FR-015**: Unformat MUST collapse all non-essential whitespace to single spaces and remove line breaks.
- **FR-016**: Whitespace inside string literals MUST be preserved unchanged.
- **FR-017**: Single-line comments (`--`) MUST be preserved on their own lines to prevent code breakage.

**Search Match Highlighting:**
- **FR-018**: When searching in History, matched text in the code preview MUST be highlighted with a yellow/ochre background color.
- **FR-019**: All occurrences of the search term MUST be highlighted, not just the first.
- **FR-020**: Highlighting MUST be removed when the search is cleared.

**Version History:**
- **FR-021**: The History tool window MUST show a version timeline for the selected query when multiple auto-save snapshots exist.
- **FR-022**: Clicking a version MUST display its content in the code preview.
- **FR-023**: Users MUST be able to compare two versions in a side-by-side diff view.

**Rename Closed Queries:**
- **FR-024**: Right-clicking a closed query MUST offer a "Rename" option in the context menu.
- **FR-025**: The rename MUST be persisted in the History database across sessions.
- **FR-026**: Pressing Escape during rename MUST cancel without changes.

### Key Entities

- **HistoryEntry**: Extended with `IsStarred` (boolean), `CustomName` (string, nullable), and `Versions` (list of timestamped snapshots).
- **HistoryVersion**: A timestamped snapshot of a query's SQL text, linked to its parent HistoryEntry.
- **SearchQuery**: Parsed representation of an advanced search with prefixes, wildcards, phrases, and boolean operators.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can star a query and find it via the Starred filter within 2 clicks.
- **SC-002**: Advanced search with `server:` prefix returns accurate results for 100% of matching queries.
- **SC-003**: Copy as IN Clause produces syntactically valid SQL that can be pasted directly into a WHERE clause without editing.
- **SC-004**: Unformat reduces a 20-line formatted query to a single line in under 100ms.
- **SC-005**: Search match highlighting shows all occurrences within 200ms of typing.
- **SC-006**: Version timeline loads within 500ms for queries with up to 50 versions.
- **SC-007**: Renamed queries retain their custom name across IDE restarts.

## Assumptions

- The existing SQLite History database can be extended with `is_starred`, `custom_name`, and a `versions` table without migration issues (new columns with defaults).
- Advanced search parsing is done client-side in the History ViewModel — no engine IPC needed.
- Copy as IN Clause reuses the existing `GridCopyAsMenu` infrastructure — just adds a new format option.
- Unformat can be implemented as a new `FormatActionType` enum value dispatched to a lightweight operation.
- Search match highlighting uses the existing RichTextBox in the History tool window with `SelectionBackColor`.

## Scope Boundaries

**In Scope:**
- All 7 gaps from AKML_SQL_Gap_Analysis_1.md
- SQLite schema extension for starred/versioned entries
- Advanced search parser
- Copy as IN Clause grid format
- Unformat formatting action
- Search highlighting in History code preview
- Version timeline panel in History tool window
- Rename context menu for closed queries

**Out of Scope:**
- AI features (separate phase)
- SQL Prompt AI features (NL-to-SQL, Explain, etc.)
- Any features already implemented
- Cross-machine History sync
