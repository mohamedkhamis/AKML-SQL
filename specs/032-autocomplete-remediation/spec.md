# Feature Specification: Autocomplete Campaign Remediation (Web + Engine)

**Feature Branch**: `030-closure-followups` *(spec ID `032-autocomplete-remediation`; kept on the current branch by user request — no new branch created)*
**Created**: 2026-07-17
**Status**: Draft
**Input**: User description: "based on this file @web-autocomplete-campaign-2026-07-16.md — remediate the confirmed findings of the 2026-07-16 web edition autocomplete + formatting validation campaign"
**Source report**: [doc/web-autocomplete-campaign-2026-07-16.md](../../doc/web-autocomplete-campaign-2026-07-16.md) — 1,370 autocomplete cases + 100 formatting cases + ~120 keystroke/UI scenarios run end-to-end against the live product; 75.6% pass rate; ~310 genuine failing cases collapsing into ~40 confirmed root causes (finding IDs 1–7 and A–J referenced below are defined in that report).

## Overview

The July 2026 validation campaign proved that the web edition's core SSMS-like flows work, but SQL developers hit systematic completion failures in everyday scenarios: typing `alias.` produces no suggestions, stored-procedure completion is almost entirely absent, suggestions inside subqueries and CTE bodies lose their scope, INSERT column lists are not scoped to the target table, and the keyboard cannot accept a completion (Tab) or execute a query (Ctrl+Enter). Because the completion engine is shared, most engine-side defects equally affect the desktop (SSMS/VS) edition. This feature closes the confirmed findings so that autocomplete behaves the way a longtime SSMS / SQL Prompt user expects, in both editions.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Completion appears when and how I type, and the keyboard works (Priority: P1)

A SQL developer in the web editor types `SELECT o` then `.` in a query that has `FROM dbo.Orders o`. A suggestion list of Orders columns opens immediately — no Ctrl+Space needed. Typing a space after `UPDATE`, `INSERT INTO`, `DELETE FROM`, or `EXEC` opens the relevant table/procedure list, just as it already does after `WHERE` or `FROM`. With the list open, Tab accepts the highlighted item; Ctrl+Enter runs the query. The developer can write, complete, and execute SQL without touching the mouse.

**Why this priority**: Dot-completion is the single most common completion gesture in SQL editing — 48 of 101 keystroke scenarios failed on it (report finding 1). Tab-accept and keyboard execution are muscle memory for every SSMS/VS user (findings 3–4). Without these, the product feels broken regardless of how good the suggestion contents are.

**Independent Test**: In the web editor connected to a database, type the gestures above with only the keyboard and confirm each opens/accepts/executes as described. Deliverable value: SSMS-parity editing feel, independent of any engine-side suggestion-quality fix.

**Acceptance Scenarios**:

1. **Given** a document containing `FROM dbo.Orders o`, **When** the user types `o.` anywhere a column reference is valid, **Then** a suggestion list opens automatically showing Orders columns (identical contents to pressing Ctrl+Space at the same caret).
2. **Given** an empty statement, **When** the user types `UPDATE ` (trailing space) — likewise `INSERT INTO `, `DELETE FROM `, `EXEC ` — **Then** the appropriate table or procedure list opens automatically.
3. **Given** an open suggestion list with an item highlighted, **When** the user presses Tab, **Then** the highlighted item is inserted (and Tab still indents when no list is open).
4. **Given** a runnable query in the editor, **When** the user presses Ctrl+Enter, **Then** the query executes and results appear (verified by a changed results grid).

---

### User Story 2 - Suggestions respect statement scope: subqueries, CTE bodies, aliased UPDATE/DELETE (Priority: P1)

A developer writes `UPDATE o SET | FROM Orders o` or places the caret inside a subquery or CTE body. The suggestion list reflects the tables actually in scope at the caret: the alias `o` resolves to Orders (not to a phantom table named `o`), a subquery sees both its own FROM tables and the outer query's aliases, and a CTE body sees its own sources.

**Why this priority**: Scope-resolution defects explain the worst campaign families — subqueries 15/70, delete 48/70, update SET/WHERE zero-item clusters (report findings A1–A6). Zero-item completion in mid-statement editing is the most jarring failure mode a user can hit.

**Independent Test**: Author the report's failing shapes (aliased UPDATE/DELETE, correlated subquery, caret inside CTE body, UNION branches, three-part names) and confirm each yields correctly scoped suggestions.

**Acceptance Scenarios**:

1. **Given** `UPDATE o SET | FROM dbo.Orders o` (and the DELETE equivalent), **When** completion is invoked at `|` or after `o.`, **Then** Orders columns are suggested (zero-item results eliminated).
2. **Given** a caret inside a subquery `(SELECT | FROM dbo.OrderDetails od)` nested in an outer query with alias `o`, **When** completion is invoked, **Then** both `od` columns and the outer alias `o` are available (inner scope wins on conflicts).
3. **Given** a caret inside a CTE body, **When** completion is invoked, **Then** the CTE body's own FROM tables are in scope.
4. **Given** `SELECT … FROM A UNION SELECT | FROM B`, **When** completion is invoked in the second branch, **Then** only B-branch scope is offered (no leakage from A).
5. **Given** a three-part name `OtherDb.dbo.Orders o`, **When** `o.` completion is invoked, **Then** the alias resolves to the correct table and no bogus aliases are registered.

---

### User Story 3 - Stored procedure execution assistance (Priority: P2)

A developer types `EXEC ` and gets a list of stored procedures (including schema-qualified ones such as `Sales.usp_MarkInvoicePaid`). After selecting `usp_GetCustomerOrders`, typing a space or `@` offers the procedure's parameters (`@CustomerID`, `@FromDate`, `@ToDate`). Declared local variables (`DECLARE @id INT`) also complete when the user types `@`.

**Why this priority**: exec-procs was among the worst families (15/60) and procedure execution is a daily DBA/developer task; parameter data is already collected by the product but never surfaced (report findings B1, C3–C5).

**Independent Test**: With the sandbox procedures in place, type `EXEC `, pick a procedure, and complete its parameters end-to-end; declare a variable and complete it.

**Acceptance Scenarios**:

1. **Given** an empty statement, **When** the user types `EXEC ` (or `EXECUTE `), **Then** stored procedure names are suggested.
2. **Given** `EXEC dbo.usp_GetCustomerOrders `, **When** completion is invoked (typed `@` or explicit), **Then** the procedure's parameters are suggested, and accepting one over a typed `@C` prefix yields a single correctly-spelled parameter (no `@@` duplication).
3. **Given** `DECLARE @CustomerID INT` earlier in the batch, **When** the user types `@` in an expression, **Then** `@CustomerID` is suggested.

---

### User Story 4 - INSERT statements guide the user to the right columns (Priority: P2)

A developer types `INSERT INTO Customers (` and is offered exactly the Customers columns — not a generic object list. At `INSERT INTO |` only valid insert targets (tables/views) are offered, and the `INTO` keyword itself is suggested after `INSERT`.

**Why this priority**: The insert family failed 38/80 on exactly this scoping gap (report findings C1–C2); INSERT is a top-five daily statement shape.

**Independent Test**: Type the two INSERT positions against a known table and verify the column list matches the table definition and the target list contains no procedures/functions.

**Acceptance Scenarios**:

1. **Given** `INSERT INTO dbo.Customers (|`, **When** completion is invoked, **Then** Customers columns are suggested.
2. **Given** `INSERT INTO |`, **When** completion is invoked, **Then** only insertable objects are suggested (no procedures or functions).
3. **Given** `INSERT |`, **When** completion is invoked, **Then** `INTO` is among the suggestions.

---

### User Story 5 - Context-correct keywords and built-in functions (Priority: P3)

In expression positions (`WHERE OrderDate >= |`, `SET Price = |`, `VALUES (|`), the developer is offered built-in functions (GETDATE, DATEADD, ISNULL, …) alongside columns. Keyword suggestions match the syntactic position: `ORDER |` offers `BY`; `LEFT |` offers `JOIN`/`OUTER`; `UNION |` offers `SELECT`/`ALL`; `DELETE |` offers `FROM`; inside a CASE expression `THEN`/`ELSE` are offered; `UPDATE TOP (5) dbo.Orders SET |` is treated as an UPDATE assignment position.

**Why this priority**: Broad but lower-severity polish — explains the functions (47/60), where-having (82/90), and keywords (29/50) families (report findings B2–B7, D). Each gap is noticeable but has an easy workaround (typing the keyword manually).

**Independent Test**: Walk the report's keyword-family failing cases (KW-023, KW-026…030, UNION/CASE/DELETE/TOP shapes) and the expression positions, verifying each offers the expected keyword/function set.

**Acceptance Scenarios**:

1. **Given** `SELECT * FROM Orders ORDER |`, **When** completion is invoked, **Then** `BY` is suggested (and tables/HAVING are not).
2. **Given** `… FROM Orders o LEFT |`, **When** completion is invoked, **Then** `JOIN` and `OUTER` are suggested.
3. **Given** `WHERE OrderDate >= |` (likewise `SET Price = |` and `VALUES (|`), **When** completion is invoked, **Then** built-in functions are suggested along with in-scope columns, and scalar user functions appear in JOIN ON positions.
4. **Given** a CASE expression after `WHEN <condition> |`, **When** completion is invoked, **Then** `THEN` is suggested (and `ELSE` in the corresponding position).

---

### User Story 6 - CTEs, temp tables, and bracketed/quoted names complete reliably (Priority: P3)

A developer aliasing a CTE (`FROM cte x` … `x.|`) gets the CTE's columns; a recursive CTE can reference itself inside its own body; CTEs do not leak across `;` statement boundaries. Temp-table names (`#t`) are suggested after FROM/JOIN, and `SELECT * INTO #t` produces a usable column list for `#t`. Typing `[dbo].[Cust`, `"dbo"."`, or `JOIN [Sales].[` keeps completion scoped and filtering correctly instead of returning nothing.

**Why this priority**: These families (cte 40/70, temp-tables 41/60, brackets-quoted 25/40) are frequent in real scripts but each shape is narrower than the P1/P2 stories (report findings E, F, G).

**Independent Test**: Run the report's failing CTE/temp/bracket shapes and confirm non-empty, correctly scoped suggestion lists.

**Acceptance Scenarios**:

1. **Given** `WITH cte AS (…) SELECT x.| FROM cte x`, **When** completion is invoked, **Then** the CTE's columns are suggested (including when the CTE body is `SELECT *` or the CTE declares an explicit column list).
2. **Given** two statements separated by `;` where the first defines a CTE, **When** completion is invoked in the second statement, **Then** the first statement's CTE is not offered.
3. **Given** `CREATE TABLE #t (…)` or `SELECT * INTO #t` earlier in the batch, **When** the user types `FROM #` or requests `#t` columns, **Then** the temp table and its columns are suggested — even while the statement being typed is incomplete.
4. **Given** a partially typed bracketed or double-quoted identifier (`[Cust`, `"dbo"."`, `JOIN [Sales].[`), **When** completion is invoked, **Then** matching items are returned with the typed schema qualifier respected, and an unterminated `[` or `"` does not blank out completion for the rest of the statement.

---

### User Story 7 - Trustworthy suggestions, ranking, and connection status (Priority: P3)

Suggestion lists do not mislead: ORDER BY/GROUP BY filtering matches what the user typed against column names (not table-name text), IDENTITY/computed columns are not offered as UPDATE SET targets, and `CROSS APPLY fn_|` offers table-valued functions. After a browser reload, the connection indicator tells the truth — either the previous SQL connection is restored automatically or the UI clearly shows that no database connection is active — and a saved connection displays its saved database (not `master`) when selected.

**Why this priority**: Correctness-of-trust issues; each can cause a wrong edit or silent degradation but occurs less often than the P1/P2 defects (report findings 5, 6, H).

**Independent Test**: Verify each ranking case against the report's repro; reload the browser with a saved connection and confirm status/database display.

**Acceptance Scenarios**:

1. **Given** an ORDER BY position with prefix typed, **When** completion filters, **Then** matches are computed against the insertable column name, so unrelated columns whose *table* name matches do not flood the list.
2. **Given** `UPDATE t SET |` on a table with IDENTITY/computed columns, **When** completion is invoked, **Then** those columns are not offered as assignment targets.
3. **Given** a page reload with a previously saved SQL connection, **When** the app finishes loading, **Then** either the connection is restored automatically or the status indicator clearly shows no active database connection (never a "Live"-style indicator with silently degraded suggestions).
4. **Given** a saved connection targeting a non-default database, **When** the user selects it, **Then** the database shown matches the database that will actually be connected to.

---

### User Story 8 - Formatting is idempotent and the web edition ships the product default style (Priority: P3)

Formatting a document twice yields the identical result the first time — the JOIN layout inside CTE bodies no longer oscillates between passes. The web edition offers the same built-in styles as the desktop edition (Khamis Style and Collapsed, with Khamis Style active by default), so web users format with the intended product default rather than internal fallback settings.

**Why this priority**: 99/100 formatting cases already pass; this closes the single idempotency defect and a product-consistency gap (report findings 7, J1–J3), lower urgency than completion fixes.

**Independent Test**: Format the report's FMTA-006 repro twice and diff the outputs; open the web edition's style list and verify built-ins and default.

**Acceptance Scenarios**:

1. **Given** the chained-CTE input from case FMTA-006, **When** the document is formatted and the output is formatted again, **Then** the two outputs are byte-identical (and no stray multi-space runs appear around JOIN keywords).
2. **Given** any document where the formatter detects first-pass/second-pass divergence, **When** formatting completes, **Then** the user receives the converged result and a visible notice rather than silently receiving the divergent first pass.
3. **Given** a fresh web-edition install, **When** the user opens the format-style list, **Then** Khamis Style and Collapsed built-ins are present with Khamis Style active by default.

---

### Edge Cases

- Suggestion lists are capped at a fixed maximum (50 items today): scoping fixes must ensure the *expected* items rank above the cap in the report's at-cap families (star-and-misc, from-tables) rather than relying on cap increases.
- Documents that do not parse at the caret (mid-edit, unbalanced parentheses, unterminated `[`/`"`/string): completion must degrade to correctly scoped fallback behavior, never to empty or cross-scope results.
- Offline/no-SQL-connection mode: trigger-behavior changes (dot, DML-space, Tab, Ctrl+Enter) must behave sensibly with keyword/snippet-only suggestions and must not open empty popups on every keystroke.
- Very large batches with many statements/CTEs: statement-boundary scoping must hold for statements beyond the first two and after mid-document edits.
- Identifiers that collide with keywords (a column ending in `Or`, a table named `Exec`): tokenizer-sensitive fixes must not misclassify them.
- Non-default collations / case variations: completion matching remains case-insensitive as today (casing-prefix family stays passing).
- Existing passing behavior is protected: comments-strings suppression (50/50), schema-qualified completion (58/60), and multi-statement isolation must not regress.

## Requirements *(mandatory)*

### Functional Requirements

**Editor trigger & keyboard behavior (web edition)** — report findings 1–4, I1–I4

- **FR-001**: Typing `.` after an alias, schema, or object name MUST automatically open the member suggestion list, with contents identical to an explicit invocation at the same caret.
- **FR-002**: Typing a space after `UPDATE`, `INSERT`, `INSERT INTO`, `DELETE`, `DELETE FROM`, `EXEC`, and `EXECUTE` MUST automatically open the appropriate object suggestion list (parity with the existing `WHERE`/`FROM`/`AND` behavior).
- **FR-003**: With the suggestion list open, Tab MUST accept the highlighted item; when no list is open, Tab MUST retain its current indent behavior.
- **FR-004**: Ctrl+Enter MUST execute the current query from the editor; at least one keyboard-only execution path MUST exist.
- **FR-005**: Accepting a suggestion that begins with `@` or `#` over a typed prefix MUST replace the full typed token (no doubled sigils such as `@@CustomerID`).

**Scope resolution (engine — affects web and desktop)** — report findings A1–A6

- **FR-006**: Completion invoked inside a parenthesized scope (subquery, CTE body, derived table) MUST resolve the innermost scope containing the caret, including when the statement does not fully parse.
- **FR-007**: Correlated subqueries MUST also see enclosing-scope aliases, with the inner scope winning name conflicts; derived tables MUST expose their projected columns.
- **FR-008**: In aliased UPDATE/DELETE statements (`UPDATE o SET … FROM Orders o`), the alias MUST resolve to the underlying table; FROM/JOIN definitions MUST take precedence over DML target tokens so the alias map cannot be poisoned.
- **FR-009**: UPDATE, DELETE, and MERGE statements that parse cleanly MUST use full-fidelity (parse-tree-based) alias resolution rather than degraded fallback behavior.
- **FR-010**: Set-operator branches (UNION/INTERSECT/EXCEPT) MUST be scope boundaries — completion in one branch never offers the other branch's tables/aliases.
- **FR-011**: Three-part names (`db.schema.object alias`) MUST resolve correctly for alias registration and for dot-scoped completion.

**Clause detection & keyword sets (engine)** — report findings B1–B7

- **FR-012**: `EXEC`/`EXECUTE` positions MUST be recognized as procedure-execution context and offer stored procedure names (including schema-qualified ones).
- **FR-013**: Keyword suggestions MUST match the syntactic position for at least: `ORDER |`→`BY`, `GROUP |`→`BY`, `LEFT|INNER|CROSS |`→join qualifiers (`JOIN`/`OUTER`/`APPLY`), `UNION |`→`SELECT`/`ALL`, `DELETE |`→`FROM`, and CASE-expression positions (`THEN`/`ELSE`).
- **FR-014**: `UPDATE TOP (n) <table> SET |` MUST be treated as an UPDATE assignment position (columns of the target), and `UPDATE TOP (n) |` MUST offer tables.

**INSERT / procedures / variables (engine)** — report findings C1–C4

- **FR-015**: `INSERT INTO <table> (|` MUST offer the target table's columns; `INSERT INTO |` MUST offer only insertable objects (no procedures/functions); `INSERT |` MUST offer `INTO`.
- **FR-016**: Stored-procedure parameters MUST be offered as completions in EXEC argument positions.
- **FR-017**: Variables declared earlier in the batch MUST be offered when the user types `@` in an expression position.

**Built-in functions (engine)** — report finding D

- **FR-018**: Built-in scalar functions MUST be offered in expression positions (WHERE/HAVING comparisons, SET assignments, VALUES slots, select-list expressions), and scalar user-defined functions MUST be eligible in JOIN ON positions.

**CTE resolution (engine)** — report finding E

- **FR-019**: An alias over a CTE (`FROM cte x` … `x.|`) MUST resolve to the CTE's columns, including when the CTE body is `SELECT *` (fall back to the body's source tables) or the CTE declares an explicit column list.
- **FR-020**: CTE visibility MUST be statement-scoped (not batch-scoped) — a CTE defined in one statement is not offered after the terminating `;`.
- **FR-021**: A recursive CTE MUST be able to reference itself inside its own body; completion inside second-and-later CTE bodies of a `WITH` chain MUST retain scope.

**Temp tables (engine)** — report finding F

- **FR-022**: Temp-table names MUST be suggested in table positions once defined earlier in the batch, and their tracked definitions MUST survive the current statement being incomplete/unparsable.
- **FR-023**: `SELECT * INTO #t` MUST record the expanded column list for `#t` when column metadata is available.

**Bracketed/quoted identifiers (engine)** — report finding G

- **FR-024**: A partially typed bracketed or double-quoted identifier MUST filter suggestions by the identifier text (delimiters excluded), and an unterminated `[` or `"` at the caret MUST NOT destroy completion for the remainder of the statement.
- **FR-025**: Dot-scoped completion MUST work across quoted parts (`"dbo"."|`), and JOIN suggestions after a typed schema qualifier (`JOIN [Sales].[|`) MUST respect that qualifier.

**Ranking & filter fidelity (engine)** — report finding H

- **FR-026**: Prefix filtering MUST match against the text the completion inserts (e.g., the column name), not against display-label decorations such as `Table.Column`.
- **FR-027**: IDENTITY and computed columns MUST NOT be offered as UPDATE SET assignment targets.
- **FR-028**: `CROSS/OUTER APPLY fn_|` positions MUST offer table-valued functions; identifiers whose text merely ends in a keyword (e.g., `…dbo.Or`) MUST NOT be misinterpreted as operators.

**Formatter (engine + web)** — report findings 7, J1–J3

- **FR-029**: Formatting MUST be idempotent for JOIN layout inside parenthesized bodies (CTE bodies, derived tables) — the FMTA-006 oscillation is eliminated.
- **FR-030**: When the formatter's convergence check detects first/second-pass divergence, the user MUST receive the converged result, and the condition MUST be surfaced to the user rather than silently dropped.
- **FR-031**: The web edition MUST ship the same built-in format styles as the desktop edition (Khamis Style, Collapsed) with Khamis Style as the default active style.

**Connection status honesty (web edition)** — report findings 5–6

- **FR-032**: After a page reload, the UI MUST NOT indicate full IntelliSense capability while no SQL connection exists; when a saved connection is available it SHOULD be restored automatically, and otherwise the status MUST clearly show that no database connection is active.
- **FR-033**: Selecting a saved connection MUST display the connection's saved database; when the available-database list is filtered by service-account permissions, the UI MUST indicate that some databases may not be listed.

### Key Entities

- **Completion context**: the syntactic position of the caret (clause, statement kind, enclosing scopes, partially typed token) from which suggestion contents are derived.
- **Scope / alias map**: the set of tables, aliases, CTEs, temp tables, and variables visible at the caret; correctness of this map is the root of most campaign failures.
- **Suggestion item**: a completable entry (column, table, procedure, parameter, variable, keyword, function, snippet) with insert text, display label, and rank.
- **Format style**: a named set of formatting rules; built-ins (Khamis Style, Collapsed) must be consistent across desktop and web editions.
- **Campaign corpus & results**: the 1,470-case corpus and per-case results from the 2026-07-16 run — the regression baseline for acceptance.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Re-running the campaign's 1,370-case autocomplete battery yields an overall pass rate of **≥ 95%** (baseline 75.6%), with corpus-mistake cases (the 24 reclassified) excluded from the denominator.
- **SC-002**: Zero-item completion failures (95 cases at baseline) are reduced to **0** across the battery.
- **SC-003**: Every previously failing family reaches **≥ 90%**: insert, update, delete, exec-procs, functions, cte, temp-tables, subqueries, brackets-quoted, keywords, where-having.
- **SC-004**: The keystroke-trigger pass reaches **100%** on dot-trigger scenarios (48 failing at baseline) and **100%** on DML-keyword-space, Tab-accept, and Ctrl+Enter-execute scenarios.
- **SC-005**: The 100-case formatting battery is **100% idempotent** (baseline 99/100), with literals/comments preserved and no exceptions.
- **SC-006**: A user can author, complete, and execute a query in the web editor **using only the keyboard** (no mouse interaction required at any step).
- **SC-007**: Families passing at baseline do not regress: comments-strings stays at 100%, schema-qualified ≥ 96%, multi-statement ≥ 82%, and the engine log shows **zero errors/warnings** across the re-run.
- **SC-008**: The desktop (SSMS/VS) edition's existing completion/formatting test suites pass unchanged, and the engine-side fixes are verified by at least one desktop smoke pass (same engine, both editions benefit).
- **SC-009**: After a browser reload with a saved connection, the user either keeps working with live database suggestions or sees an unambiguous "not connected" state within the first interaction — no silent degradation.

## Assumptions

- **Fuzzy matching is by design**: the 5-level non-contiguous matcher and compound keyword items (`ORDER BY` as one item, `INNER JOIN`, `IS NOT NULL`) are intended behavior; the 24 corpus cases reclassified as corpus mistakes remain excluded from all counts.
- **The 50-item suggestion cap is unchanged**: at-cap ambiguous failures (71 cases) are addressed only insofar as correct scoping/ranking naturally surfaces expected items; raising the cap is out of scope.
- **Shared engine**: engine-side fixes apply identically to web and desktop editions; desktop verification rides on existing test suites plus a smoke pass rather than a second full campaign.
- **Reload behavior default**: auto-restoring the saved SQL connection is the preferred resolution of finding 5; accurate status display is the mandatory floor (FR-032 reflects both).
- **Verification environment**: the campaign harness, corpus (22 JSON files), `Northwind_AutoTest` sandbox, and results baselines from the 2026-07-16 run remain available to re-run acceptance; the sandbox is dropped only after acceptance.
- **Branch handling**: this spec intentionally lives on the existing `030-closure-followups` branch (user instruction); the spec ID `032` was allocated from the global spec/branch numbering sequence.

## Out of Scope

- Redesigning the fuzzy matcher, ranking model, or suggestion cap.
- New completion features beyond SSMS/SQL Prompt-parity remediation (e.g., AI-assisted suggestions).
- Changing which databases the engine service account can access (finding 6 is addressed by display honesty, not by permission changes).
- Campaign artifact cleanup (dropping `Northwind_AutoTest`, removing `test-corpus/` and `.playwright-mcp/results-*.json`) — tracked as post-acceptance housekeeping in the source report, not as feature requirements.
