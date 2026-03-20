# Feature Specification: Core IntelliSense Engine

**Feature Branch**: `002-core-intellisense-engine`
**Created**: 2026-03-19
**Status**: Draft
**Input**: Core IntelliSense Engine — Schema-aware autocomplete system replacing SSMS's unreliable built-in IntelliSense with a fast, accurate, context-aware completion engine for SQL developers.

## Clarifications

### Session 2026-03-19

- Q: How is "usage frequency" determined for ranking completion items? → A: Static heuristics — rank by object properties (PK columns first, then FK, then by ordinal position; tables by row count or alphabetical). No persistent usage tracking subsystem in Phase 2.
- Q: Should the spec explicitly list what is out of scope for Phase 2? → A: Yes — add explicit out-of-scope section listing AI suggestions, full snippet manager, SQL formatting, SELECT * expansion, execution plan hints, tab history, and code refactoring.
- Q: What does the user see during initial engine startup before it is ready? → A: Silent — no indicator during startup; completion simply doesn't trigger until the engine is ready. No queuing of requests.

## Out of Scope

The following features are explicitly **not** included in Phase 2 and are deferred to later phases:

- **AI-powered suggestions** — Natural language to SQL, AI-assisted completions (Phase 10)
- **Full Snippet Manager** — Custom snippet creation, import/export, snippet editor UI (Phase 4; Phase 2 includes only a basic built-in snippet set)
- **SQL Formatting** — Code formatting, indentation, style enforcement (Phase 3)
- **SELECT * expansion** — Expanding `SELECT *` into an explicit column list
- **Execution plan hints** — Query plan visualization or performance suggestions
- **Tab history** — Recently-used tab tracking or tab management features
- **Code refactoring** — Rename, extract, inline refactoring operations
- **Linked server full support** — Full linked server metadata caching (Phase 2 attempts lazy-load only; no error if unavailable)
- **Azure Synapse full support** — Only partial IntelliSense due to non-standard system catalog (documented as known limitation)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Column Completion After Alias (Priority: P1)

A SQL developer types `SELECT o.` after writing `FROM dbo.Orders o` and immediately sees all columns of the Orders table in a popup list, ranked by static heuristics (PK columns first, then FK, then by ordinal position), with data types, nullability, and primary/foreign key indicators displayed alongside each column name.

**Why this priority**: This is the single most important IntelliSense interaction. Every SQL developer does this dozens of times per hour. If alias-based column completion works fast and accurately, the extension earns immediate trust. SSMS's built-in IntelliSense frequently fails at this exact scenario.

**Independent Test**: Can be fully tested by connecting to any database with tables, writing a FROM clause with an alias, typing the alias followed by a dot, and verifying columns appear correctly within 100ms.

**Acceptance Scenarios**:

1. **Given** a connected database with a `dbo.Orders` table, **When** user types `SELECT o.` after `FROM dbo.Orders o`, **Then** a popup appears within 100ms showing all columns of `dbo.Orders` with data types and key badges
2. **Given** a query with multiple table aliases (`FROM dbo.Orders o JOIN dbo.Customers c`), **When** user types `c.`, **Then** only columns from `dbo.Customers` appear
3. **Given** a self-join scenario (`FROM dbo.Orders o1 JOIN dbo.Orders o2`), **When** user types `o2.`, **Then** columns from `dbo.Orders` appear (same columns as `o1.` but scoped to the alias)
4. **Given** a table alias defined in a FROM clause, **When** user types that alias followed by `.` in SELECT, WHERE, GROUP BY, HAVING, or ORDER BY, **Then** column completion works in all clause contexts

---

### User Story 2 - Schema-Aware Object Completion (Priority: P1)

A SQL developer types `FROM dbo.` and sees all tables and views in the `dbo` schema listed in a popup. When typing `FROM ` without a schema qualifier, they see objects from the default schema and `dbo`, ranked by static heuristics (row count estimate descending, then alphabetical).

**Why this priority**: Object name completion is the second most frequent IntelliSense interaction. Developers need to discover and correctly reference database objects without memorizing exact names or switching to Object Explorer.

**Independent Test**: Can be fully tested by connecting to a database with multiple schemas and objects, typing `FROM` followed by a schema prefix and dot, and verifying all relevant objects appear with type indicators.

**Acceptance Scenarios**:

1. **Given** a connected database, **When** user types `FROM dbo.`, **Then** all tables and views in the `dbo` schema appear with type icons distinguishing tables from views
2. **Given** a connected database with schemas `dbo`, `sales`, `hr`, **When** user types `FROM sales.`, **Then** only objects in the `sales` schema appear
3. **Given** a cursor position after `EXEC `, **When** completion triggers, **Then** stored procedures appear (not tables or views)
4. **Given** a three-part name `database.schema.`, **When** user types the final dot, **Then** objects in that specific database and schema appear
5. **Given** no schema qualifier after `FROM `, **When** completion triggers, **Then** objects from the default schema and `dbo` appear, ranked by static heuristics (row count estimate descending, then alphabetical)

---

### User Story 3 - Keyword Completion in Context (Priority: P1)

A SQL developer typing SQL statements receives context-appropriate keyword suggestions. After `SELECT * `, `FROM` is suggested. After `FROM dbo.Orders o `, keywords like `WHERE`, `JOIN`, `INNER JOIN`, `LEFT JOIN`, `ORDER BY`, and `GROUP BY` are suggested.

**Why this priority**: Keyword completion reduces typos and speeds up writing SQL. It must be context-aware — suggesting only keywords valid at the current cursor position — to avoid noisy, unhelpful suggestions.

**Independent Test**: Can be fully tested by typing partial SQL statements at various positions and verifying that only contextually valid keywords appear.

**Acceptance Scenarios**:

1. **Given** cursor after `SEL`, **When** completion triggers, **Then** `SELECT` and `SET` appear ranked by frequency
2. **Given** cursor after `SELECT * FR`, **When** completion triggers, **Then** `FROM` appears as top suggestion
3. **Given** cursor after `FROM dbo.Orders o WH`, **When** completion triggers, **Then** `WHERE` appears as top suggestion
4. **Given** cursor after `FROM dbo.Orders o `, **When** completion triggers, **Then** `WHERE`, `JOIN`, `INNER JOIN`, `LEFT JOIN`, `RIGHT JOIN`, `CROSS JOIN`, `FULL JOIN`, `ORDER BY`, `GROUP BY` appear
5. **Given** the user's preference is UPPER case keywords, **When** a keyword is accepted, **Then** it is inserted in UPPER case

---

### User Story 4 - FK-Based JOIN Assistance (Priority: P2)

After typing `JOIN ` in a query that already references tables, the developer sees tables with foreign key relationships to already-referenced tables, and upon selecting one, the full `ON` clause is auto-generated based on the FK relationship.

**Why this priority**: JOIN assistance is a differentiating feature that saves significant time. Developers frequently forget exact FK column names, leading to incorrect joins. This feature prevents join errors and accelerates multi-table query writing.

**Independent Test**: Can be fully tested by connecting to a database with FK relationships, writing a FROM clause, typing JOIN, and verifying FK-related tables appear with auto-generated ON clauses.

**Acceptance Scenarios**:

1. **Given** `FROM dbo.Orders o` and FK from `OrderDetails.OrderID` → `Orders.OrderID`, **When** user types `JOIN `, **Then** `dbo.OrderDetails` appears as a top suggestion with preview `ON od.OrderID = o.OrderID`
2. **Given** multiple FK relationships exist, **When** JOIN completion triggers, **Then** tables with direct FK relationships appear ranked above unrelated tables
3. **Given** a multi-column FK, **When** a FK-related table is selected, **Then** the ON clause includes all FK columns joined with `AND`
4. **Given** no FK relationships exist in the database, **When** JOIN completion triggers, **Then** all tables appear without ON clause auto-generation

---

### User Story 5 - Function Signature Help (Priority: P2)

When a developer types an opening parenthesis `(` after a function or stored procedure name, a tooltip appears showing the parameter list with names, types, default values, and which parameter is currently being filled.

**Why this priority**: Parameter help eliminates the need to look up function signatures in documentation. It is especially valuable for built-in functions with many overloads (e.g., `CONVERT`, `DATEADD`) and user-defined procedures with many parameters.

**Independent Test**: Can be fully tested by typing a function name followed by `(` and verifying signature help appears with accurate parameter information.

**Acceptance Scenarios**:

1. **Given** user types `CONVERT(`, **Then** a tooltip shows `CONVERT(data_type, expression [, style])` with parameter descriptions
2. **Given** user has typed `CONVERT(int, ` (two arguments), **Then** the second parameter `expression` is highlighted in the tooltip
3. **Given** user types `EXEC dbo.MyProc(` where `MyProc` has parameters `@Id int, @Name nvarchar(100) = NULL`, **Then** tooltip shows both parameters with types and default values
4. **Given** user types `,` to advance to the next parameter, **Then** the tooltip updates to highlight the next parameter

---

### User Story 6 - Quick Info Tooltips (Priority: P2)

When a developer hovers over a database object identifier or presses the Quick Info shortcut, a tooltip appears showing metadata about that object — table row counts, column details, procedure parameters, or variable types.

**Why this priority**: Quick Info reduces context switching to Object Explorer or documentation. Seeing a table's row count or a column's type inline helps developers write more informed queries.

**Independent Test**: Can be fully tested by hovering over identifiers in a SQL script and verifying metadata tooltips appear with accurate information.

**Acceptance Scenarios**:

1. **Given** user hovers over a table name `dbo.Orders`, **Then** tooltip shows schema, estimated row count, column count, and description (if extended properties exist)
2. **Given** user hovers over a column name `OrderDate`, **Then** tooltip shows data type, nullability, default value, and description
3. **Given** user hovers over a variable `@TotalAmount`, **Then** tooltip shows declared type from earlier in the batch
4. **Given** user hovers over a keyword `COALESCE`, **Then** tooltip shows brief syntax help

---

### User Story 7 - CTE and Temp Table Column Completion (Priority: P2)

Columns defined in Common Table Expressions (CTEs) and temporary tables (#temp) created earlier in the same batch are available for completion in subsequent references.

**Why this priority**: CTEs and temp tables are essential SQL patterns. SSMS's built-in IntelliSense cannot resolve CTE columns at all, making this a clear improvement over the baseline.

**Independent Test**: Can be fully tested by writing a CTE or temp table creation statement followed by a SELECT that references it, and verifying column completion works.

**Acceptance Scenarios**:

1. **Given** a CTE `WITH OrderCTE AS (SELECT OrderID, CustomerID FROM dbo.Orders)`, **When** user types `SELECT oc.` after `FROM OrderCTE oc`, **Then** `OrderID` and `CustomerID` appear
2. **Given** `CREATE TABLE #TempOrders (OrderID int, Total decimal(18,2))` earlier in the batch, **When** user types `SELECT t.` after `FROM #TempOrders t`, **Then** `OrderID` and `Total` appear
3. **Given** nested CTEs, **When** an outer CTE references an inner CTE's columns, **Then** completion resolves through the chain correctly
4. **Given** a `##GlobalTemp` table, **When** referenced in a different batch, **Then** column completion works

---

### User Story 8 - Schema Cache Management (Priority: P3)

The extension automatically populates a schema cache when the user connects to a database, refreshes it in the background periodically, and allows manual refresh. The cache persists across sessions for faster startup.

**Why this priority**: The schema cache is the foundation for all completion features. While it runs silently in the background, its reliability and speed directly determine the user experience. Stale caches cause wrong suggestions — a critical trust issue.

**Independent Test**: Can be fully tested by connecting to a database, verifying initial cache load timing, executing DDL, and confirming the cache updates automatically.

**Acceptance Scenarios**:

1. **Given** a new database connection, **When** connection is established, **Then** basic object names (databases, schemas, tables, views) are available for completion within 3 seconds
2. **Given** a loaded cache, **When** user executes `ALTER TABLE dbo.Orders ADD NewColumn int`, **Then** the cache detects the DDL and refreshes the affected table within 5 seconds
3. **Given** user presses the manual refresh shortcut (Ctrl+Shift+R), **Then** the full cache for the current database is rebuilt
4. **Given** user reconnects to a previously-cached database, **When** a persisted cache exists on disk, **Then** completion is available immediately while background refresh validates freshness
5. **Given** a database with 10,000+ objects, **When** cache loads, **Then** completion speed does not degrade below the 100ms target

---

### User Story 9 - Completion UI with Fuzzy Matching (Priority: P3)

The completion popup supports fuzzy matching and CamelCase matching, allowing developers to type partial or abbreviated names to quickly find items. The popup follows the IDE theme, supports keyboard and mouse navigation, and provides filtering with match count feedback.

**Why this priority**: The popup UI is the user's primary interaction point. Fuzzy matching is expected by modern developers — typing `CustID` to find `CustomerID` or `OD` to find `OrderDate` — and its absence would feel like a regression from other editors.

**Independent Test**: Can be fully tested by triggering completion and typing partial/abbreviated identifiers, verifying matching behavior and UI responsiveness.

**Acceptance Scenarios**:

1. **Given** completion popup is showing, **When** user types `custid`, **Then** `CustomerID` appears as a match
2. **Given** completion popup is showing, **When** user types `OD` (uppercase), **Then** `OrderDate` appears via CamelCase matching
3. **Given** completion popup is showing, **When** user presses Down/Up arrows, **Then** selection moves accordingly; Enter accepts the selection; Escape dismisses
4. **Given** the IDE is using Dark theme, **When** completion popup appears, **Then** it renders with Dark theme colors
5. **Given** a high-DPI monitor setup, **When** completion popup appears, **Then** it renders at correct scaling without blurriness

---

### User Story 10 - Out-of-Process Engine Resilience (Priority: P3)

The IntelliSense engine runs as a separate process from the IDE. If the engine crashes, the IDE continues running normally and the engine automatically restarts within 2 seconds, reconnecting and resuming IntelliSense service.

**Why this priority**: The Phase 1 zero-crash guarantee for the IDE must be preserved. Running the engine out-of-process ensures that heavy operations (parsing, schema queries) never freeze or crash the IDE. This is a unique architectural advantage over competitors.

**Independent Test**: Can be fully tested by terminating the engine process and verifying the IDE remains responsive and the engine auto-restarts.

**Acceptance Scenarios**:

1. **Given** the engine process is running, **When** it crashes or is terminated, **Then** the IDE continues to function normally without freezing
2. **Given** the engine has crashed, **When** 2 seconds elapse, **Then** a new engine process starts and reconnects automatically
3. **Given** heavy schema loading is in progress, **When** the user is typing in the editor, **Then** the IDE UI never freezes or becomes unresponsive
4. **Given** the engine is restarting or has not yet started, **When** user triggers completion, **Then** no popup appears and no error is shown (silent — completion resumes once the engine is ready)

---

### User Story 11 - Native IntelliSense Conflict Resolution (Priority: P3)

On first activation, the extension detects SSMS's built-in IntelliSense and offers to disable it to avoid conflicts (double popups, keystroke interception fights). On uninstall, the original setting is restored.

**Why this priority**: Running two IntelliSense systems simultaneously creates a confusing and broken experience. This must be handled cleanly but is lower priority because it's a one-time setup interaction.

**Independent Test**: Can be fully tested by installing the extension in SSMS with native IntelliSense enabled and verifying the disable/restore workflow.

**Acceptance Scenarios**:

1. **Given** SSMS native IntelliSense is enabled, **When** the extension loads for the first time, **Then** a dialog offers to disable native IntelliSense with options [Yes] [No] [Don't ask again]
2. **Given** user chose Yes, **When** the dialog is dismissed, **Then** SSMS's built-in IntelliSense is disabled and only AKML SQL's IntelliSense is active
3. **Given** the extension is uninstalled, **When** uninstall completes, **Then** SSMS's native IntelliSense is re-enabled if it was disabled by AKML SQL
4. **Given** user chose "Don't ask again", **When** extension loads on subsequent sessions, **Then** the dialog does not appear

---

### Edge Cases

- What happens when the user has no permissions to read system catalogs? The system degrades gracefully, using `INFORMATION_SCHEMA` views as fallback, with a visible "limited metadata" indicator.
- What happens when the database connection drops mid-session? The completion system continues serving cached data with a "stale cache" indicator until reconnection.
- What happens when a user types inside a string literal or comment? Completion does not trigger inside comments (`--`, `/* */`) or string literals (`'...'`, `N'...'`).
- What happens when the user switches databases (USE statement or dropdown)? The cache for the new database loads (or reuses if already cached) and completion reflects the new context within 3 seconds.
- What happens with linked server four-part names (`server.database.schema.object`)? The system attempts lazy-loading of linked server metadata; if unavailable, no completion is shown (no error).
- What happens with SQLCMD mode directives (`:setvar`, `:connect`)? The parser recognizes SQLCMD directives and does not offer SQL keyword completion within them.
- What happens when two query windows reference different databases? Each window maintains its own completion context tied to its connection; switching windows switches the active context.
- What happens with very large scripts (10,000+ lines)? Incremental parsing ensures only the changed region is re-parsed, keeping per-keystroke parse time under 50ms.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST display column completion with data types, nullability, and PK/FK indicators when the user types an alias or table name followed by a dot (`.`)
- **FR-002**: System MUST resolve table aliases defined in FROM and JOIN clauses and provide column completion for those aliases in SELECT, WHERE, JOIN ON, GROUP BY, HAVING, ORDER BY, UPDATE SET, and INSERT column list contexts
- **FR-003**: System MUST display schema-qualified object completion (tables, views, functions, procedures, synonyms) when the user types a schema name followed by a dot
- **FR-004**: System MUST display context-appropriate keyword suggestions based on the current cursor position within a SQL statement
- **FR-005**: System MUST match keyword casing to the user's configured preference (UPPER, lower, PascalCase, or match-as-typed)
- **FR-006**: System MUST suggest tables with foreign key relationships after `JOIN` and auto-generate the `ON` clause when the user selects a FK-related table
- **FR-007**: System MUST display function and stored procedure parameter signatures when the user types `(` after a function or procedure name, highlighting the current parameter as subsequent arguments are typed
- **FR-008**: System MUST display Quick Info tooltips with object metadata (row count, column count, data types, descriptions) on hover or keyboard shortcut
- **FR-009**: System MUST resolve CTE column definitions and make them available for completion in the outer query
- **FR-010**: System MUST track temporary table definitions (`#temp`, `##temp`) within the current batch and provide column completion for them
- **FR-011**: System MUST track `@variable` declarations and their types within the current batch
- **FR-012**: System MUST support fuzzy matching (substring, CamelCase abbreviation) in the completion popup filter
- **FR-013**: System MUST populate a schema cache within 3 seconds of a new database connection, providing basic object names (databases, schemas, tables, views) for immediate completion
- **FR-014**: System MUST lazy-load column metadata for tables not yet referenced, loading on first reference
- **FR-015**: System MUST detect DDL statements executed by the user and incrementally refresh the affected objects in the cache
- **FR-016**: System MUST support manual full cache refresh via keyboard shortcut (Ctrl+Shift+R) and menu action
- **FR-017**: System MUST persist the schema cache to disk so that previously-connected databases are available immediately on next session startup
- **FR-018**: System MUST run the IntelliSense engine in a separate process from the IDE, ensuring the IDE UI thread is never blocked by engine operations
- **FR-019**: System MUST automatically restart the engine process within 2 seconds if it crashes, without requiring user intervention
- **FR-020**: System MUST detect and offer to disable SSMS's built-in IntelliSense on first load, and restore it on uninstall
- **FR-021**: System MUST suppress completion inside comments (`--`, `/* */`) and string literals (`'...'`, `N'...'`)
- **FR-022**: System MUST support the completion popup following the IDE's current theme (Light, Dark, Blue)
- **FR-023**: System MUST support keyboard navigation (Up/Down/Enter/Tab/Escape) and mouse interaction (click to select, double-click to accept) in the completion popup
- **FR-024**: System MUST provide a basic set of built-in code snippets (SELECT, INSERT, UPDATE, DELETE, CTE patterns) with tab-stop navigation
- **FR-025**: System MUST auto-suggest table aliases based on table name abbreviation rules (e.g., `Orders` → `o`, `OrderDetails` → `od`) after table references in FROM/JOIN
- **FR-026**: System MUST support background periodic schema refresh at a configurable interval (default 5 minutes)
- **FR-027**: System MUST degrade gracefully when user permissions are limited — falling back to `INFORMATION_SCHEMA` views if system catalog access is denied
- **FR-028**: System MUST support multi-database scenarios, maintaining separate caches per database and lazy-loading cross-database metadata when three-part names are used
- **FR-029**: System MUST provide version-aware keyword and function dictionaries that reflect the connected SQL Server version (2016 through 2025) and cloud targets (Azure SQL Database, Azure SQL Managed Instance)
- **FR-030**: System MUST render correctly on high-DPI and mixed-DPI multi-monitor setups
- **FR-031**: System MUST allow all IntelliSense and cache behaviors to be configured via a settings interface accessible from the extension menu
- **FR-032**: System MUST identify individual SQL statements separated by `GO` batch separators and maintain correct scope per batch

### Key Entities

- **Schema Cache**: In-memory representation of a database's metadata (databases, schemas, tables, views, columns, indexes, foreign keys, procedures, functions, parameters, types, synonyms, sequences). One cache instance per database connection. Supports phased population (immediate names, background columns/FKs, lazy on-demand details).
- **Completion Item**: A single suggestion shown in the popup, with properties: display text, insert text, object type (table/view/column/keyword/snippet/function/procedure), secondary text (data type, nullability, PK/FK badge), source table name, match score, and icon type.
- **Cursor Context**: The parser's determination of the current cursor position's semantic meaning — which SQL clause the cursor is in, what object scope is active, what type of completion is appropriate, and what aliases/CTEs/temp tables are in scope.
- **Engine Session**: Represents the state of one connected editor window — its database connection, schema cache reference, parsed document AST, and completion provider chain. Multiple sessions can be active simultaneously (multiple query windows).
- **Communication Message**: A typed message exchanged between the IDE shell and the out-of-process engine (e.g., ConnectionChanged, DocumentChanged, RequestCompletion, CompletionResult, SchemaRefreshRequest, EngineStatus).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Suggestions appear within 100ms of a keystroke for 95% of completion requests (measured across a corpus of typical SQL editing patterns)
- **SC-002**: Schema cache for a database with up to 500 tables and 5,000 columns loads within 3 seconds of connection establishment
- **SC-003**: Schema cache for a large database with 5,000+ tables loads within 10 seconds without blocking the user's ability to type
- **SC-004**: Over 95% of triggered completions include the correct item within the top 5 results (measured against a test corpus of 500+ completion scenarios)
- **SC-005**: Engine crash recovery completes within 2 seconds, with the IDE remaining fully responsive during recovery
- **SC-006**: Incremental document parsing after a keystroke completes within 50ms, even for scripts exceeding 10,000 lines
- **SC-007**: Memory usage of the engine process stays below 200MB for a typical database (500 tables) and below 500MB for a large database (5,000+ tables)
- **SC-008**: Over 80% of beta testers rate AKML SQL IntelliSense as "better than SSMS built-in IntelliSense" in user testing
- **SC-009**: Zero IDE crashes caused by the IntelliSense engine during an 8-hour workday session
- **SC-010**: All completion features work identically across SSMS 20, SSMS 21, SSMS 22, VS 2019, VS 2022, and VS 2026 targets
- **SC-011**: All completion features work against SQL Server 2016, 2017, 2019, 2022, 2025, Azure SQL Database, and Azure SQL Managed Instance
- **SC-012**: Over 90% of users keep IntelliSense enabled (do not disable it) after initial use

## Assumptions

- Phase 1 (Foundation & Installer) is complete and provides a stable VSPackage shell, menu system, configuration infrastructure, logging, and update mechanism.
- The extension has access to the active editor's text buffer and can intercept keystrokes via VS SDK editor hooks.
- The extension can detect the current database connection from SSMS's active query window (connection info is accessible via VS Shell APIs or SSMS-specific interfaces).
- Users have at minimum SELECT permission on `INFORMATION_SCHEMA` views; the system degrades gracefully below this level but cannot provide completion with zero database permissions.
- The built-in T-SQL keyword and function dictionaries will be maintained as static data files shipped with the extension, covering SQL Server 2016 through 2025.
- Named pipes are available and not blocked by security policies on the user's machine (standard Windows environments).
- The extension's out-of-process engine will be deployed alongside the extension (shipped via the installer from Phase 1).
- Performance targets (100ms p95 completion) are measured on hardware meeting minimum SSMS system requirements.
- The WPF popup UI approach is compatible with SSMS's editor hosting model across all target versions.
