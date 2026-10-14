# Feature Specification: PostgreSQL support in the engine and the web edition

**Feature Branch**: `042-postgresql-support`
**Created**: 2026-10-14
**Status**: Draft
**Input**: User description: "please i want to add postgres database  at akml and i will start with engine and web at this spec   and in future will develop plugin into  pgadmin  , but now i want to focus on engin and web akml and smothly switch or use same sql server and postgres  , and also new sql command of postgres and reed document o it well and syntax of writing queries andi want to isolate it than sql server   , finialy if you have questions let me know"

## Overview

AKML SQL speaks one language today: Transact-SQL against Microsoft SQL Server. Every surface the
user touches assumes it: the connection dialog, the way a script is split and run, the Schema
explorer, completion and hover help, the formatter and the analyser, SQL History, and the AI
prompts.

This feature adds **PostgreSQL** as a second *kind* of database server, in the **engine** and the
**web edition** only. A user connects to a PostgreSQL server the way they connect to SQL Server,
runs scripts the way PostgreSQL runs them, browses the database's schemas, and gets suggestions,
hover help and signature help that know PostgreSQL's SQL commands, functions, data types and
operators. That knowledge is not guessed: it is derived from the official PostgreSQL
documentation and kept as a reviewable reference. Switching between a SQL Server connection and a
PostgreSQL connection switches every dialect-aware surface at once, without reloading and
without losing the editor text.

The two dialects are **kept apart**. Nothing from one leaks into the other, and SQL Server
behaviour is unchanged. This matters more than it sounds because the engine is shared: the SSMS
extension and the web edition talk to the same engine, so "engine and web only" still means
"SSMS must behave exactly as before".

This release ships the full workspace for PostgreSQL, not a read-only preview: results can be
edited in place, scripts can be formatted with the user's styles and checked with Parse, and a
PostgreSQL code analysis rule set reports findings the way the SQL Server rules do. Only the
refactorings stay for a later spec.

A pgAdmin plugin is a later feature. This spec keeps the PostgreSQL capability reachable by any
client of the engine, so that plugin can be a thin client rather than a second implementation.

Terms used below:

- **Database kind** (or **kind**): SQL Server or PostgreSQL.
- **Dialect**: the SQL language of a kind (T-SQL, PostgreSQL SQL).
- **PostgreSQL reference**: the product's curated knowledge of PostgreSQL commands, keywords,
  functions, operators, data types and lexical rules, derived from the official documentation.
- **Dialect-aware surfaces**: highlighting, completion, quick info, signature help, snippets,
  wildcard expansion, go to definition, formatting, Parse, code analysis, results editing, the
  Schema explorer, execution messages, history recording, and AI prompts.

## Clarifications

### Session 2026-10-14

- Q: Which PostgreSQL features ship in this release? → A: The full tier. The core workspace
  (connect, execute, results, messages, database selector, Schema explorer, highlighting,
  completion, quick info, signature help, snippets, history, AI dialect awareness) plus results
  editing, Format SQL with the existing styles, Parse, and a PostgreSQL code analysis rule set.
  Refactorings stay out.
- Q: May the web edition remember a PostgreSQL password? → A: Yes, as an opt-in per saved
  connection. The password is kept in a protected store on the engine machine, never in the
  browser, and a connection with a remembered password connects without a prompt.
- Q: Does PostgreSQL mode exist without a connection? → A: Connected only. With no active
  connection the editor is in SQL Server mode, as today. The mode follows the active
  connection's kind until the user disconnects or switches, so a dropped connection that is
  awaiting reconnect does not flip the editor.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Connect to PostgreSQL and run a script (Priority: P1)

A developer opens the web edition, chooses **PostgreSQL** in the connection dialog, enters host,
port, database, user name and password, tests the connection, saves it, and connects. They paste
a script with several statements, press F5, and see each statement's rows as its own result set,
the server's notices and command tags in Messages, and any error with its SQLSTATE code, message,
detail, hint and position, exactly as PostgreSQL reports it. The status bar tells them, at a
glance, that they are on a PostgreSQL server, which one, which database, as which user.

**Why this priority**: Without connect and execute there is no PostgreSQL product at all. This
is the slice a user can demonstrate on day one.

**Independent Test**: Can be fully tested by connecting to a PostgreSQL server with a sample
database, running a script of SELECT / INSERT / UPDATE / DDL statements (including one that
fails), and checking results, messages, errors and the status bar. Needs no completion, no
Schema explorer.

**Acceptance Scenarios**:

1. **Given** the connection dialog, **When** the user picks PostgreSQL and enters valid details,
   **Then** Test reports success with the server version, Save stores the connection with its
   kind, and Connect opens a session whose kind, server, database and user are shown in the
   status bar.
2. **Given** a connected PostgreSQL session, **When** the user runs a script of three
   statements that each return rows, **Then** three result sets appear in order, each with the
   row count and the elapsed time, and every result-grid feature available for SQL Server
   results (column widths, selection, copy, copy with headers, copy as, sort, view value, save
   as CSV/TSV, row cap) works on them; editing is User Story 6.
3. **Given** a connected PostgreSQL session, **When** a statement raises an error, **Then**
   Messages shows the SQLSTATE code, severity, message, detail and hint; the Problems tab lists
   it; clicking it moves the caret to the failing position in the document; execution stops at
   that statement; the result sets of the statements before it are kept.
4. **Given** a script with a `RAISE NOTICE`, **When** it runs, **Then** the notice appears in
   Messages in order, labelled as a notice, not as an error.
5. **Given** a script that begins a transaction and then fails, **When** the user runs another
   statement, **Then** the status bar shows the transaction is aborted, the message explains that
   every statement fails until rollback, and a one-click Rollback is offered.
6. **Given** the user points a SQL Server connection at a PostgreSQL port (or the reverse),
   **When** they test it, **Then** the error says the server did not answer as a server of the
   chosen kind and suggests checking the kind, instead of a generic protocol error.
7. **Given** a script containing a `GO` line, **When** it runs against PostgreSQL, **Then** the
   server's syntax error is shown with a hint that `GO` is a SQL Server batch separator with no
   meaning in PostgreSQL; nothing is silently stripped.

---

### User Story 2 - Browse a PostgreSQL database (Priority: P1)

Once connected, the developer opens the Schema explorer and sees the database's schemas and,
under each, its tables, views, materialized views, functions, procedures and sequences, with
columns, keys, foreign keys, indexes and comments. They switch to another database on the same
server from the database selector and the explorer, status bar and suggestions follow.

**Why this priority**: Browsing is how a developer learns an unfamiliar database, and the
schema cache it produces is what makes User Story 3 schema-aware. It is independently useful
even without completion.

**Independent Test**: Connect to a sample PostgreSQL database with objects in more than one
schema, open the Schema explorer, expand a table, read its columns and foreign keys, create a
table from the editor and watch it appear, then switch database.

**Acceptance Scenarios**:

1. **Given** a connected PostgreSQL session, **When** the Schema explorer loads, **Then** object
   names appear first (within seconds) and columns, keys, parameters and comments shortly after,
   with the same progress indication as for SQL Server.
2. **Given** a database with a `public` schema and a `sales` schema, **When** the explorer is
   expanded, **Then** both schemas are listed with their objects; system schemas are hidden by
   default behind a toggle.
3. **Given** a table with a primary key, two foreign keys, a comment and an identity column,
   **When** the user expands it, **Then** the columns show PostgreSQL type names as PostgreSQL
   displays them, nullability, defaults and identity; the keys, foreign keys and the comment are
   shown.
4. **Given** the user runs `CREATE TABLE` from the editor, **When** it succeeds, **Then** the new
   table appears in the explorer without a manual refresh.
5. **Given** the database selector, **When** the user picks another database, **Then** the
   product first tells them that PostgreSQL requires a reconnect and that temporary tables,
   session settings and any open transaction will be lost; after confirming, the explorer,
   status bar and suggestions reflect the new database.
6. **Given** a user without privileges on one schema, **When** the explorer loads, **Then** the
   visible schemas load normally and the restricted one is marked as not accessible, not failed.

---

### User Story 3 - Write PostgreSQL with help (Priority: P1)

While typing a PostgreSQL script, the developer gets PostgreSQL highlighting, completion for
keywords, commands, functions, data types, schemas, tables, columns, aliases and snippets, hover
help that explains a command or function and links to its official documentation page, and
signature help for built-in and user-defined functions. T-SQL things never appear: no `GO`, no
`@variables`, no `#temp` tables, no `[brackets]`, no `sp_` procedures.

**Why this priority**: This is the user's "new SQL commands of PostgreSQL and the syntax of
writing queries". It is what makes the web edition an editor for PostgreSQL rather than a
terminal with a grid.

**Independent Test**: With a PostgreSQL connection to the sample database, type scripts from a
PostgreSQL completion corpus and check suggestions, hover, signature help and highlighting;
run the corpus gate; run the dictionary cross-check for cross-dialect leakage.

**Acceptance Scenarios**:

1. **Given** the caret one space after `SELECT * FROM`, **When** completion opens, **Then** the tables,
   views, materialized views and sequences of the schemas on the session's search path are
   offered unqualified first, other schemas' objects qualified; a name that needs quoting is
   inserted in double quotes.
2. **Given** `SELECT o. FROM orders o`, **When** completion opens after the dot, **Then** the
   columns of `orders` are offered with their PostgreSQL types.
3. **Given** the caret one space after `INSERT INTO orders (...) VALUES (...) ON CONFLICT`, **When** completion opens,
   **Then** the suggestions are PostgreSQL's (`DO NOTHING`, `DO UPDATE`, constraint and column
   targets), and the statement does not confuse the context analysis.
4. **Given** the caret inside a dollar-quoted function body containing semicolons, **When**
   completion opens, **Then** suggestions are still correct for the body's SQL, and the script
   still runs as one function definition.
5. **Given** the mouse over `string_agg`, **When** quick info opens, **Then** it shows the
   function's signature(s), return type, a one-line description, the version it first appeared
   in if relevant, and a link to the official documentation page.
6. **Given** the caret inside `date_trunc(`, **When** signature help opens, **Then** the
   parameters and the active parameter are shown; the same works for a user-defined function
   from the schema.
7. **Given** a PostgreSQL connection, **When** the user types `@` or `#` or `[`, **Then** no
   T-SQL variable, temp table or bracket-identifier suggestions appear; **and given** a SQL
   Server connection, no PostgreSQL command, function, type or operator appears.
8. **Given** `SELECT ... FROM t WHERE id = $1 AND created::date = ...`, **When** the user types,
   **Then** highlighting treats `$1` as a parameter and `::` as a cast, and completion keeps
   working after both.
9. **Given** a connection to an older server, **When** completion opens, **Then** commands,
   functions and options introduced after that server's version are not offered.

---

### User Story 4 - Switch between SQL Server and PostgreSQL without friction (Priority: P2)

The developer keeps both a SQL Server connection and a PostgreSQL connection saved. They switch
between them from the connection picker in two interactions. Every dialect-aware surface follows
the active connection; the editor text stays; SQL History records each run with its kind and
can be filtered by kind; the AI assistant answers in the active dialect.

**Why this priority**: "Smoothly switch or use the same product for SQL Server and PostgreSQL"
is the user's stated goal. It builds on Stories 1 to 3 but is a distinct experience to test.

**Independent Test**: Save one connection of each kind; switch back and forth ten times while
editing; check the status bar, explorer, completion and AI on each side; open History and filter
by kind; import an older settings export and check the kind defaults.

**Acceptance Scenarios**:

1. **Given** a PostgreSQL session with text in the editor, **When** the user switches to a saved
   SQL Server connection, **Then** within a few seconds the status bar, Schema explorer,
   completion, quick info, highlighting and snippets are SQL Server's, the editor text is
   unchanged, and no page reload happened.
2. **Given** both kinds of connections saved, **When** the picker, Settings and the command
   palette list them, **Then** every entry shows its kind with a distinct label and icon, not by
   colour alone.
3. **Given** runs on both kinds, **When** the user opens History, **Then** each entry shows its
   kind, the search syntax accepts a kind term, a filter control narrows to one kind, and
   restoring an entry suggests the matching saved connection when it still exists.
4. **Given** a PostgreSQL connection, **When** the user asks the AI assistant to write or fix a
   query, **Then** the answer is PostgreSQL SQL (double-quoted identifiers, `LIMIT`, `RETURNING`,
   PostgreSQL functions), and a SQL Server connection gets T-SQL as before.
5. **Given** a settings export made before this feature, **When** it is imported, **Then** its
   saved connections are treated as SQL Server and nothing else changes.
6. **Given** the paired engine does not support PostgreSQL, **When** the connection dialog opens,
   **Then** the PostgreSQL choice is disabled with the reason and the engine version needed.
7. **Given** a saved PostgreSQL connection with "Remember password" turned on, **When** the user
   connects from the picker, **Then** no password prompt appears; **and when** the engine no
   longer has the password (another engine, or the store was cleared), **Then** the user is
   prompted and offered to remember it again.
8. **Given** a remembered password, **When** the user chooses Forget in Settings → Connections,
   **Then** the next connect prompts for the password and nothing about it remains on the engine
   machine.

---

### User Story 5 - Nothing leaks, nothing pretends (Priority: P2)

A long-time SQL Server user notices no difference: every existing behaviour, suggestion,
formatting result, analysis finding, history entry and SSMS feature is the same as before. A
PostgreSQL user who invokes a feature that is not yet available for PostgreSQL is told so in a
sentence, instead of watching a T-SQL implementation mangle their script.

**Why this priority**: The user asked for isolation explicitly. The shared engine makes this a
hard requirement, not a nicety: a regression here reaches SSMS users who never asked for
PostgreSQL.

**Independent Test**: Run every existing automated gate (completion corpus, format parity
goldens, analysis tests, web unit and browser tests, SSMS shell tests) and compare results to
the baseline; then, on a PostgreSQL connection, invoke each unsupported surface and check the
message.

**Acceptance Scenarios**:

1. **Given** the full existing automated suite, **When** it runs on this feature's build,
   **Then** every result is identical to the baseline (same passes, same known reds).
2. **Given** a PostgreSQL connection, **When** the user invokes a refactoring, Find invalid
   objects, or any other surface this spec does not list as supported for PostgreSQL, **Then**
   the command is disabled (menu, palette, shortcut) with the tooltip or status message "Not
   available for PostgreSQL", and the T-SQL implementation never runs on the text.
3. **Given** the SSMS extension, **When** it is used against SQL Server after this feature,
   **Then** no new UI, prompt, menu item or behaviour appears, and its own test suite is
   unchanged.
4. **Given** the complete keyword, function, type and snippet dictionaries of both dialects,
   **When** the automated cross-check runs, **Then** it finds no item present in both, and no
   SQL Server-only item reachable from PostgreSQL mode.

---

### User Story 6 - Edit PostgreSQL results in place (Priority: P2)

The developer runs a single-table SELECT that includes the table's primary key, clicks a cell,
changes the value, sets another cell to NULL, adds a row and deletes one, and applies. Each
change is written to exactly the row it belongs to, the grid shows what the server committed,
and a row that has disappeared or changed underneath them is reported, not overwritten.

**Why this priority**: The SQL Server grid can do this; a PostgreSQL user would feel the gap
immediately. It depends on User Story 1 only.

**Independent Test**: On the sample database, edit, insert and delete rows across every common
column type and verify the table contents afterwards; try a JOIN, an aggregate and a table
without a primary key and check the grid is read-only with the reason.

**Acceptance Scenarios**:

1. **Given** a result set from one table whose primary key columns are in the result, **When**
   the user edits a cell and applies, **Then** exactly that row is updated, the grid shows the
   committed value, and Messages reports the write.
2. **Given** a result from a JOIN, an aggregate, a view, or a table without a primary key,
   **When** the user tries to edit, **Then** the grid is read-only and the reason is shown, as it
   is for SQL Server.
3. **Given** a value typed the way PostgreSQL displays it (a date, `true`, an array literal, a
   json document, a uuid), **When** it is applied, **Then** it is converted to the column's type;
   an invalid value marks the cell invalid with the server's message and is not applied.
4. **Given** Ctrl+0 on a cell, **When** applied, **Then** the column is set to NULL; a NOT NULL
   column refuses with the server's message and the cell stays marked.
5. **Given** a row deleted or re-keyed by someone else since the query ran, **When** the user
   applies, **Then** the Apply is reported as not applied with the row identified, and no other
   row is changed.
6. **Given** pending edits, **When** the user runs again, navigates away or switches connection,
   **Then** the existing pending-edit confirmation applies.

---

### User Story 7 - Format PostgreSQL scripts (Priority: P2)

The developer presses Format SQL on a PostgreSQL script and gets it laid out in their active
style: keyword casing, line breaks, indentation and alignment as the style says. Nothing
changes meaning: quoted identifiers keep their quotes and case, dollar-quoted bodies are left as
written, casts, operators and parameters survive. Formatting twice gives the same text. Parse
tells them about syntax errors without touching the server.

**Why this priority**: Formatting is the product's signature feature for SQL Server. The user
asked for the full tier; it also gives the product its own understanding of PostgreSQL syntax,
which code analysis and Parse build on.

**Independent Test**: Format a PostgreSQL corpus of real-world scripts and check each result
reads as the same statements, is stable on a second pass, and keeps every quoted identifier and
dollar-quoted body verbatim; run Parse on a script with known errors.

**Acceptance Scenarios**:

1. **Given** a script of SELECT, INSERT ... ON CONFLICT, UPDATE ... RETURNING, a CTE and a window
   function, **When** Format SQL runs, **Then** the result follows the active style's casing,
   breaks and indentation, and reads as the same statements.
2. **Given** `CREATE FUNCTION ... AS $$ ... $$ LANGUAGE plpgsql`, **When** formatted, **Then** the
   dollar-quoted body is unchanged character for character and the statement around it is
   formatted.
3. **Given** identifiers `"OrderId"` and `orderid`, **When** a style that upper-cases identifiers
   runs, **Then** `"OrderId"` is untouched and `orderid` may be re-cased only if the style asks;
   no quotes are added or removed anywhere.
4. **Given** a formatted script, **When** Format SQL runs again, **Then** nothing changes.
5. **Given** a script with a syntax error, **When** Format SQL runs, **Then** the text is returned
   unchanged and a message points at the first error.
6. **Given** a PostgreSQL connection and the Format Styles page, **When** a style option has no
   meaning in PostgreSQL, **Then** it is marked "no effect for PostgreSQL" and the preview shows a
   PostgreSQL sample.
7. **Given** Ctrl+F5 on a script with three syntax errors, **When** Parse runs, **Then** all three
   are listed in Problems with line and column, the server was not contacted, and the message
   says object names and permissions were not checked.

---

### User Story 8 - Code analysis for PostgreSQL (Priority: P3)

While editing, the developer sees PostgreSQL findings in the gutter and the Problems tab: an
identifier that will be silently truncated, a mixed-case name that will be folded, an UPDATE
without WHERE, a `timestamp` column that should carry a time zone, a statement that cannot run
inside a transaction block. Each finding explains the PostgreSQL behaviour behind it and links to
the documentation; the user can suppress it per line, script, session or everywhere, as with SQL
Server rules.

**Why this priority**: Valuable, but it rests on User Story 7's syntax understanding and is the
last slice a user would miss.

**Independent Test**: Run analysis on a corpus of PostgreSQL scripts seeded with each rule's
positive and negative cases; check findings, suppressions, the rules window and that no SQL
Server rule fires.

**Acceptance Scenarios**:

1. **Given** a PostgreSQL script with a 70-character identifier, **When** analysed, **Then** a
   finding says the name will be truncated to the server's limit, with the documentation link.
2. **Given** `DELETE FROM orders;`, **When** analysed, **Then** a finding warns about the missing
   WHERE, and `-- akml-disable-line <rule>` on that line removes it.
3. **Given** `BEGIN; CREATE INDEX CONCURRENTLY ...; COMMIT;`, **When** analysed, **Then** a finding
   says the statement cannot run inside a transaction block.
4. **Given** the rules window on a PostgreSQL connection, **When** opened, **Then** it lists the
   PostgreSQL rule set by category with severities, and the SQL Server rules are listed
   separately under their own kind.
5. **Given** a PostgreSQL script, **When** analysed, **Then** no SQL Server rule produces a finding;
   and a SQL Server script produces no PostgreSQL finding.
6. **Given** a script that does not parse, **When** analysed, **Then** one syntax finding points at
   the first error and no rule findings are produced.

---

### Edge Cases

- **Mixed-case identifiers**: an unquoted `CREATE TABLE Orders` creates `orders`; suggestions
  for a quoted `"Orders"` must keep the quotes, and completion must never insert
  `[brackets]`.
- **Objects outside the search path**: a table in schema `sales` with the same name as one in
  `public`; unqualified suggestions prefer the search path and the qualified form is offered.
- **Dollar quoting and nested blocks**: `$$ ... $$`, `$body$ ... $body$`, semicolons and `--`
  inside; the script is sent as one statement, and completion inside the body still works.
- **PostgreSQL-only lexical forms**: `E'...'` escape strings, `::type` casts, `$1` parameters,
  `->`, `->>`, `@>`, `||`, array literals `'{1,2}'` and `ARRAY[...]`, nested `/* */` comments,
  `U&'...'` strings. None of these may break highlighting, context analysis or statement
  splitting.
- **psql meta-commands** (`\d`, `\dt`, `\c`) and **`COPY ... FROM STDIN`**: reported as not
  supported in the workspace with a hint, instead of a confusing server error.
- **Open transaction at switch time**: switching connection or database while a transaction is
  open asks before rolling it back; it never commits silently.
- **Connection lost mid-run**: the outcome is reported as Disconnected, the session is marked
  down, and reconnect re-opens the same database; nothing is retried automatically.
- **Timeouts and caps**: a statement that exceeds the query timeout is cancelled on the server
  and reported as timed out; the session stays usable; row and size caps apply as for SQL Server.
- **Server older than the supported floor**: connection succeeds with a warning; version-gated
  reference entries are withheld.
- **Compatible services** (Azure Database for PostgreSQL, Amazon RDS / Aurora PostgreSQL,
  Supabase, Neon): work as PostgreSQL. Forks that do not expose the standard catalogs
  (CockroachDB, Yugabyte, Redshift) are not targets; the product reports what it could not read.
- **Same database name on both kinds**: schema caches, history grouping and saved connections
  are keyed by kind as well, so a SQL Server `northwind` and a PostgreSQL `northwind` never share
  a cache or a history group.
- **TLS**: a server that requires TLS, a self-signed certificate under a verifying mode, or a
  hostname mismatch each produce a specific message; no mode silently trusts a certificate the
  user asked to verify.
- **Value display**: `bytea`, arrays, `json`/`jsonb`, `uuid`, `interval`, `timestamptz`,
  `numeric`, enums, composite and range types display as PostgreSQL's own text output; NULL
  displays as NULL as it does for SQL Server results.
- **Very large schemas** (10,000+ objects): names first, details in the background, the
  explorer remains responsive, and the cache size settings apply.
- **Reference drift**: a command or function present on the server but missing from the
  reference still completes from the schema where possible and never produces an error.
- **Privacy redaction before AI**: PostgreSQL text whose literals cannot be redacted is not sent
  to a cloud provider without the user's say-so.
- **Editing a key column**: the row is addressed by its original key values; the new key is
  written like any other value.
- **Editing results of a query with `LIMIT` or `ORDER BY`**: still a single-table result, still
  editable; sorting in the grid never changes which row an edit belongs to.
- **Formatting procedural bodies**: PL/pgSQL and other procedural-language bodies inside dollar
  quotes are preserved verbatim; only the SQL around them is laid out.
- **Styles written for SQL Server**: options about batch separators, bracket quoting, table hints
  or T-SQL-only clauses have no effect on PostgreSQL scripts and are marked so.
- **Very long scripts for analysis or formatting**: the document limit applies; the operation
  reports partial completion instead of hanging the editor.
- **Remembered password on a shared engine**: any client paired with that engine can use it; the
  opt-in text says so, and the password never leaves the engine machine.

## Requirements *(mandatory)*

### Functional Requirements

#### Connections and switching (User Stories 1, 4)

- **FR-001**: Every connection MUST have a database kind, SQL Server or PostgreSQL, chosen in the
  connection dialog, stored with the saved connection, and shown wherever a connection is named
  (connection picker, status bar, Settings → Connections, command palette). Saved connections
  that predate this feature MUST be treated as SQL Server.
- **FR-002**: The PostgreSQL connection form MUST take host, port (default 5432), database
  (default `postgres`), user name, password, and a TLS mode (disable, prefer, require,
  verify-CA, verify-full; default prefer) with an optional CA certificate for the verifying
  modes. Test MUST report success with the server version, or the server's own error text
  with a one-line hint.
- **FR-003**: Connecting MUST open one persistent session per workspace document, used for every
  run until disconnect or database change, so session settings, temporary tables and
  transactions persist between runs as they do in a PostgreSQL client.
- **FR-004**: The user MUST be able to switch the active connection to any saved connection of
  either kind from the picker in at most two interactions. Switching MUST re-target execution,
  the Schema explorer, completion, quick info, signature help, highlighting, snippets, AI and
  history recording to the new connection's kind and database, without a page reload and
  without changing the editor text.
- **FR-005**: The status bar MUST show the kind of the active connection with a distinct label
  and icon (never by colour alone), plus server, database, user and server version.
- **FR-006**: The engine MUST advertise PostgreSQL support to its clients at connection time. A
  web edition paired with an engine that lacks it MUST disable the PostgreSQL choice and say
  which engine version is needed.
- **FR-007**: Connection failures (authentication, unknown database, TLS required or rejected,
  unreachable host, wrong kind for the port) MUST be reported with the server's message and a
  one-line hint; a PostgreSQL failure MUST never be worded as a SQL Server one.
- **FR-008**: When a server answers with the other kind's protocol, the product SHOULD say so
  and suggest checking the kind.
- **FR-009**: Switching connection or database while a transaction is open MUST ask the user
  before rolling it back and MUST never commit it silently.

#### Execution, results and messages (User Story 1)

- **FR-010**: Run MUST send the selection, or the whole document, to the PostgreSQL session and
  run it as PostgreSQL runs it: statements in order, separated by semicolons outside string
  constants, comments, dollar-quoted blocks and quoted identifiers; there is no batch separator.
  A `GO` line MUST NOT be treated as a separator or stripped; the resulting server error MUST
  carry a hint that `GO` is SQL Server-only.
- **FR-011**: Each statement that returns rows MUST produce its own result set in the Results
  tab, stacked in order, with the same grid features as SQL Server results (column widths and
  resize, selection, copy, copy with headers, copy as, sort, view value, save as CSV/TSV, row
  cap, status-bar row counts); editing is covered by FR-130 to FR-134.
- **FR-012**: Statements that return no rows MUST report the server's command tag and affected
  row count in Messages (for example `UPDATE 12`), with elapsed time, in the same message style
  as SQL Server runs.
- **FR-013**: Server notices, warnings and info messages MUST appear in Messages in order, with
  their level, and MUST NOT be presented as errors.
- **FR-014**: Errors MUST show SQLSTATE code, severity, message, detail, hint, and the failing
  position mapped to a line and column of the document; selecting the error MUST move the caret
  there; the Problems tab MUST list it.
- **FR-015**: On error, execution MUST stop at the failing statement, keep the results of the
  statements that ran before it, and state how many statements ran. The Messages text MUST make
  clear that earlier statements stay applied unless they were inside an explicit transaction.
- **FR-016**: The status bar MUST show when the session has an open transaction and when that
  transaction is aborted after an error. While aborted, the product MUST offer a one-click
  Rollback and explain that every statement fails until rollback.
- **FR-017**: The query timeout and row/size caps from Settings MUST apply; a statement that
  exceeds the timeout MUST be cancelled on the server and reported as timed out, leaving the
  session usable.
- **FR-018**: Column values MUST display as PostgreSQL's own text output for every built-in type
  (including arrays, json/jsonb, uuid, interval, timestamptz, numeric, bytea, enums, composite and
  range types); NULL displays as NULL.
- **FR-019**: psql meta-commands and `COPY ... FROM STDIN` MUST be recognised before the script
  is sent and reported as not supported in the workspace, with a hint.
- **FR-020**: Every run MUST be recorded in SQL History with its kind, server, database, user,
  outcome, duration and row count, as SQL Server runs are.

#### Database selection (User Story 2)

- **FR-030**: The database selector MUST list the databases on the PostgreSQL server the user is
  allowed to connect to, excluding template databases.
- **FR-031**: Choosing another database MUST reconnect the session to it. Before doing so the
  product MUST tell the user that PostgreSQL cannot switch databases in place and that temporary
  tables, session settings and any open transaction are lost; with an open transaction it MUST
  follow FR-009.
- **FR-032**: After the switch, the Schema explorer, completion, status bar and history
  recording MUST reflect the new database.

#### Schema explorer and cache (User Story 2)

- **FR-040**: For the connected PostgreSQL database the Schema explorer MUST show schemas and,
  under each, tables, views, materialized views, functions, procedures and sequences. System
  schemas MUST be hidden by default behind a toggle.
- **FR-041**: For tables and views it MUST show columns (name, PostgreSQL type as the server
  displays it, nullability, default, identity or serial), primary key, foreign keys and indexes;
  for functions and procedures, parameters and return type; object and column comments MUST be
  shown as descriptions.
- **FR-042**: Loading MUST happen in two phases like SQL Server: object names first, details in
  the background, with the same progress indication.
- **FR-043**: Schema changes made by a run from the workspace MUST appear in the explorer
  without a manual refresh; changes made elsewhere MUST appear on manual refresh or within the
  periodic refresh interval.
- **FR-044**: Schema filtering, manual refresh, and the schema cache settings (size, eviction,
  clear) MUST work for PostgreSQL caches. Caches MUST be keyed by kind, server and database so a
  SQL Server and a PostgreSQL database with the same names never share or overwrite a cache.
- **FR-045**: Explorer actions that insert or copy an object name MUST use PostgreSQL quoting
  (double quotes only when needed) and schema qualification.
- **FR-046**: When the user lacks privileges on part of the database, the explorer MUST show
  what is visible and mark the rest as not accessible, without failing the whole load.

#### PostgreSQL reference from the official documentation (User Story 3)

- **FR-050**: The product MUST ship a PostgreSQL reference covering: every SQL command in the
  official SQL command reference; reserved and non-reserved keywords; every built-in data type
  with its aliases; built-in functions and operators with signatures, return types and one-line
  descriptions; and the lexical rules (identifier case folding and quoting, string constant
  forms, numeric constants, comments, operators, casts, positional parameters).
- **FR-051**: The reference MUST be derived from the official PostgreSQL documentation of a
  named major version (the newest community-supported release at implementation time). Every
  entry MUST record the documentation section it came from and, where applicable, the first
  version it applies to. The reference MUST be reviewable as a human-readable document in the
  repository, with an explicit list of anything deliberately left out.
- **FR-052**: Entries whose availability depends on the server version MUST be offered only when
  the connected server supports them.
- **FR-053**: Quick info for a command, function, operator, type or keyword MUST show its
  description, a syntax summary (commands) or signature(s) (functions), and a link to the
  official documentation page.
- **FR-054**: The PostgreSQL reference MUST contain no SQL Server item, and the SQL Server
  dictionaries MUST gain no PostgreSQL item.

#### Dialect-aware editing (User Story 3)

- **FR-060**: Editor highlighting MUST follow the active dialect. In PostgreSQL mode it MUST
  recognise PostgreSQL keywords, dollar-quoted strings, escape strings, double-quoted
  identifiers, `::` casts, positional parameters, and nested block comments, and MUST NOT apply
  T-SQL colouring (brackets, `@variables`).
- **FR-061**: Completion in PostgreSQL mode MUST offer, as appropriate to the caret position:
  keywords and commands; schemas; tables, views, materialized views and sequences (search-path
  aware: unqualified from the search path first, qualified otherwise); columns after an alias or
  dot and in SELECT, WHERE, GROUP BY, ORDER BY, ON, SET and RETURNING; JOIN ON conditions from
  foreign keys; built-in and user-defined functions; data types in CAST, `::` and DDL; aliases
  and CTE names; PostgreSQL snippets. Names that need quoting MUST be inserted double-quoted.
- **FR-062**: Signature help MUST work for built-in functions from the reference and for
  user-defined functions and procedures from the schema.
- **FR-063**: Quick info on a schema object MUST show its kind, schema, columns or parameters,
  and comment.
- **FR-064**: Context analysis MUST survive PostgreSQL constructs without errors or T-SQL
  fallbacks: dollar-quoted bodies, `::` casts, positional parameters, `RETURNING`,
  `ON CONFLICT`, `LIMIT`/`OFFSET`, `ILIKE`, `LATERAL`, `WITH ... AS [NOT] MATERIALIZED`, window
  functions, array subscripts and json operators, `DISTINCT ON`, `FILTER (WHERE ...)`.
- **FR-065**: PostgreSQL mode MUST exist only while the active connection is a PostgreSQL
  connection. With no active connection the editor MUST be in SQL Server mode, as today. The
  mode MUST follow the active connection's kind until the user disconnects or switches, so a
  dropped connection that is awaiting reconnect does not change the editor's dialect.
- **FR-066**: A built-in set of PostgreSQL snippets MUST mirror the SQL Server built-ins where an
  equivalent exists (select, insert, update, delete, CTE, create table, create index, create
  function, create view, transaction block) using PostgreSQL syntax. User snippets MUST be kept
  per kind so a snippet never shows in the other dialect.
- **FR-067**: Wildcard expansion (`SELECT *` to the column list) MUST work in PostgreSQL mode with
  PostgreSQL quoting.
- **FR-068**: Go to definition SHOULD show the source of a view, function or procedure as the
  server returns it.
- **FR-069**: The destructive-statement confirmation (DROP, TRUNCATE, DELETE or UPDATE without
  WHERE) SHOULD work for PostgreSQL scripts.

#### History (User Story 4)

- **FR-070**: History entries MUST record the kind. The History page MUST show the kind on each
  entry, accept a kind term in the search syntax, offer a kind filter control, and, on restore,
  suggest the matching saved connection when it still exists.
- **FR-071**: History grouping (query sessions, server/database groups) MUST include the kind so
  entries from different kinds are never grouped together.
- **FR-072**: The history store MUST remain readable by the SSMS extension as it is today.

#### AI assistance (User Story 4)

- **FR-080**: Every AI feature offered in the web edition (chat, explain, fix, optimize,
  text-to-SQL, ghost text, index analysis where applicable) MUST be told the active kind and
  server version and MUST produce PostgreSQL SQL when the active connection is PostgreSQL. The
  existing instruction to avoid PostgreSQL syntax MUST apply only in SQL Server mode.
- **FR-081**: The privacy transformation applied before sending SQL to a cloud provider MUST
  work for PostgreSQL text. If a script cannot be redacted, the request MUST NOT be sent with
  unredacted literals; the user MUST be told and MAY choose to send anyway, consistent with the
  existing consent model.
- **FR-082**: AI "fix" for a PostgreSQL error MUST receive the SQLSTATE code and the server's
  message, detail and hint.

#### Isolation and unsupported surfaces (User Story 5)

- **FR-090**: No PostgreSQL item MAY appear in SQL Server mode and no SQL Server item MAY appear
  in PostgreSQL mode, across completion, quick info, signature help, snippets, highlighting, AI
  prompts and message wording.
- **FR-091**: SQL Server behaviour MUST be unchanged: every existing automated gate (completion
  corpus, format parity goldens, analysis tests, web unit and browser tests, SSMS shell tests)
  MUST produce identical results, and the SSMS extension MUST show no new UI, prompt or
  behaviour.
- **FR-092**: Surfaces not available for PostgreSQL in this release MUST say so when invoked:
  menu, command-palette and toolbar entries disabled with the tooltip "Not available for
  PostgreSQL", keyboard shortcuts answered with the same status message, and the T-SQL
  implementation never run on PostgreSQL text. In this release that list is: the refactorings
  (lightweight and schema-aware), Find invalid objects, and any surface this spec does not list
  as supported for PostgreSQL.
- **FR-093**: PostgreSQL support MUST be switchable as one unit: a build or configuration without
  it MUST leave the product identical to today.

#### Security and privacy

- **FR-100**: PostgreSQL passwords MUST never appear in logs, history, diagnostics, settings
  exports, URLs or the browser's storage.
- **FR-104**: "Remember password" MUST be an opt-in per saved connection, off by default. A
  remembered password MUST be kept in a protected store on the engine machine, readable only by
  the Windows account the engine runs as, and MUST never be sent back to the browser. Without
  the opt-in, the password is entered per browser session and held in memory only until the tab
  closes.
- **FR-105**: A saved connection with a remembered password MUST connect without a prompt. When
  the engine cannot find the password (a different engine, a cleared store) or the server
  rejects it, the user MUST be prompted and offered to remember the new one.
- **FR-106**: The user MUST be able to forget a remembered password from Settings → Connections
  and from the connection dialog; deleting the saved connection MUST forget it. The opt-in text
  MUST state that any client paired with this engine can use the remembered password.
- **FR-101**: TLS modes MUST behave as PostgreSQL clients define them; the verifying modes MUST
  reject an untrusted certificate or a hostname mismatch with a specific message.
- **FR-102**: PostgreSQL connections made from a browser-paired client MUST use explicit
  credentials; authentication that would use the engine machine's own identity (integrated or
  Kerberos/GSSAPI) MUST NOT be offered from the web edition.
- **FR-103**: Connection details written to logs MUST omit the password.

#### Settings, portability and diagnostics

- **FR-110**: Settings → Connections MUST list both kinds with a kind badge. The kind travels
  with the saved-connection record; a record without a kind (saved before this feature) MUST be
  treated as SQL Server. Settings Export and Import stay as they are: the envelope is built from
  preference stores only and never includes saved connections or passwords. (Amended at plan
  time: the previous wording asked exports to carry connections, which the spec 041 design rules
  out by construction.)
- **FR-111**: Diagnostics MUST show whether the paired engine supports PostgreSQL, and the active
  connection's kind and server version; the engine log tail MUST include PostgreSQL connection
  events.
- **FR-112**: Keyboard shortcuts and commands MUST be the same for both kinds wherever the
  feature exists for both.

#### Compatibility and future clients

- **FR-120**: Everything the web edition does for PostgreSQL MUST go through the engine's
  existing client protocol, so that another client (the planned pgAdmin plugin) can connect,
  execute, browse schema and get completion and quick info without web-edition code.
- **FR-121**: Document, session, frame and result limits that apply to SQL Server MUST apply to
  PostgreSQL unchanged.

#### Results editing (User Story 6)

- **FR-130**: A PostgreSQL result set MUST be editable when it comes from one table and includes
  that table's primary key columns; otherwise the grid MUST be read-only with the reason shown,
  as for SQL Server results.
- **FR-131**: Edits, inserts and deletes MUST be written by the row's original key values and
  affect exactly one row each. An Apply MUST be all-or-nothing: if any write fails (row gone,
  key changed, constraint violated), no write is kept, and the failing row and the server's
  reason are identified.
- **FR-132**: Values MUST be accepted as PostgreSQL displays them and converted to the column's
  type on Apply. An invalid value MUST mark the cell invalid with the server's message and MUST
  NOT be applied. Set to NULL (Ctrl+0) MUST work; a NOT NULL column MUST refuse with the
  server's message.
- **FR-133**: After a successful Apply the grid MUST show the committed values without
  re-running the script; only the affected rows are read back.
- **FR-134**: The pending-edit confirmations, keyboard behaviour and invalid-cell handling of the
  SQL Server grid MUST apply unchanged.

#### Formatting and Parse (User Story 7)

- **FR-140**: Format SQL MUST lay out a PostgreSQL script with the user's active style: keyword
  casing, line breaks, indentation, alignment, comma and parenthesis placement and blank lines as
  the style defines them.
- **FR-141**: The formatter MUST preserve meaning: the formatted script MUST read as the same
  statements as the original. Comments, string constants, dollar-quoted bodies, casts,
  operators and positional parameters MUST be preserved verbatim. When the check fails the
  original MUST be returned unchanged with a message.
- **FR-142**: Formatting MUST be idempotent: formatting an already formatted script changes
  nothing.
- **FR-143**: Identifier casing options MUST apply only to unquoted identifiers. The formatter
  MUST never add, remove or change quotes around an identifier and MUST never change the case
  of a quoted identifier.
- **FR-144**: A script with a syntax error MUST be returned unchanged with a message pointing at
  the first error.
- **FR-145**: Style options with no PostgreSQL meaning MUST have no effect on PostgreSQL scripts
  and MUST be marked "no effect for PostgreSQL" in the Format Styles page while a PostgreSQL
  connection is active; the page's preview MUST show a PostgreSQL sample in that case.
- **FR-146**: Parse (Ctrl+F5) MUST check a PostgreSQL script's syntax without contacting the
  server, list every error in Problems with line and column, and say that object names and
  permissions were not checked.
- **FR-147**: The Format SQL actions (expand wildcards, qualify names) MUST use PostgreSQL
  quoting and schema qualification.

#### Code analysis (User Story 8)

- **FR-150**: The product MUST ship a PostgreSQL rule set of at least 40 rules at release across
  the existing eight categories (performance, best practices, security, style, design,
  deprecated, execution, naming). Rule identifiers MUST be distinct from the SQL Server set.
  Every rule MUST document the PostgreSQL behaviour it rests on with a reference to the
  official documentation.
- **FR-151**: Findings MUST appear in the Problems tab and the editor gutter as SQL Server
  findings do. Severities, the per-project settings file, inline suppressions, the four disable
  scopes and the rules window MUST work for PostgreSQL rules.
- **FR-152**: The rules window and settings MUST show rules grouped by kind. PostgreSQL rules
  MUST never run on SQL Server text and SQL Server rules MUST never run on PostgreSQL text.
- **FR-153**: The initial rule set MUST cover at least: identifiers longer than the server's
  limit (silently truncated); unquoted mixed-case identifiers that will be folded; reserved
  words used as identifiers; `SELECT *`; UPDATE or DELETE without WHERE; leading-wildcard LIKE;
  a function applied to a column in WHERE; NOT IN with a nullable subquery; `timestamp` without
  time zone; `char(n)` and `money`; `serial` where identity columns are available; tables
  without a primary key; foreign keys without a supporting index; statements that cannot run
  inside a transaction block; dynamic SQL built by string concatenation; broad GRANTs; keyword
  casing; missing statement terminators.
- **FR-154**: A script that does not parse MUST produce one syntax finding at the first error
  and no rule findings.
- **FR-155**: The "code analysis off" setting MUST return no findings without running the
  analyser, as it does for SQL Server.

### Key Entities

- **Database kind**: SQL Server or PostgreSQL. Attached to connections, sessions, schema caches,
  history entries, snippets and the reference; decides which dialect-aware surfaces are active.
- **Saved connection**: kind, display name, host or server, port, database, user name,
  authentication and TLS options, "remember password" flag, last connected time. The password
  itself is never part of the saved connection. Existing SQL Server saved connections gain the
  kind SQL Server.
- **Remembered password**: a secret tied to one saved connection, kept in a protected store on
  the engine machine for the account the engine runs as; forgettable; never exported, never
  returned to a client.
- **Session**: the live connection a document runs on: kind, server identity, server version,
  current database, user, search path, transaction state (none, open, aborted).
- **PostgreSQL reference**: entries of category command, keyword, function, operator, type or
  lexical rule, each with name, syntax or signature(s), description, first version, and the
  documentation section and link it came from; plus a list of deliberate omissions.
- **Schema snapshot (PostgreSQL)**: keyed by kind, server and database; schemas with tables,
  views, materialized views, functions, procedures, sequences; columns with type, nullability,
  default, identity; primary keys, foreign keys, indexes, comments; the user's search path;
  per-schema accessibility.
- **Execution outcome**: statements run, result sets in order, command tags, notices and
  warnings, errors (SQLSTATE, severity, message, detail, hint, position), elapsed times, and the
  transaction state after the run.
- **History entry**: as today plus the kind; grouping keys include the kind.
- **Grid edit (PostgreSQL)**: table, original key values, changed columns with new values, or an
  insert or delete; outcome applied or not applied with the server's reason.
- **PostgreSQL analysis rule**: identifier, category, default severity, description, the
  PostgreSQL behaviour it rests on, documentation reference; belongs to the PostgreSQL rule set
  only.
- **Engine capability**: the engine's advertised support for PostgreSQL, checked by clients
  before offering the kind.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user with valid details can connect to a PostgreSQL server and see their first
  query result within 60 seconds of opening the connection dialog; 5 of 5 hallway testers
  succeed on the first attempt.
- **SC-002**: Switching between a SQL Server and a PostgreSQL connection updates the status bar,
  Schema explorer, completion and highlighting within 3 seconds, with no page reload and the
  editor text intact, in 20 of 20 scripted switches.
- **SC-003**: Every existing automated gate (completion corpus, format parity goldens, analysis
  tests, web unit and browser tests, SSMS shell tests) produces results identical to the
  pre-feature baseline: same passes, same known reds, no new failures.
- **SC-004**: An automated cross-check over the complete dictionaries of both dialects finds 0
  items present in both; a 200-case probe in PostgreSQL mode finds 0 SQL Server-only suggestions
  (`GO`, `@`, `#`, `[`, `sp_`, `xp_`, `NOCOUNT`), and the reverse probe finds 0 PostgreSQL-only
  suggestions in SQL Server mode.
- **SC-005**: A PostgreSQL completion corpus of at least 300 cases, covering every construct in
  FR-064, passes at 95% or better at release, with a ratchet so it cannot regress.
- **SC-006**: The reference covers 100% of the SQL commands in the official command index and
  100% of the built-in data types for the named version, and at least 95% of the built-in
  functions and operators in the official function tables, with every omission listed.
- **SC-007**: In a test script of 20 distinct errors, 100% show the SQLSTATE code and message,
  and selecting the error moves the caret to the correct line in 100% of cases.
- **SC-008**: For a local database of 1,000 objects, object names appear in the Schema explorer
  within 2 seconds of connecting and columns, keys and parameters within 10 seconds.
- **SC-009**: 100% of PostgreSQL runs appear in History with the kind; filtering by kind returns
  only that kind.
- **SC-010**: For every surface listed as unavailable for PostgreSQL, invoking it in PostgreSQL
  mode shows the "Not available for PostgreSQL" message in 100% of automated checks, and the
  T-SQL implementation is never invoked (0 occurrences in the engine log).
- **SC-011**: In 10 scripted AI prompts per feature on a PostgreSQL connection, 10 of 10 answers
  use PostgreSQL syntax (manual review); the same prompts on SQL Server use T-SQL as before.
- **SC-012**: 5 of 5 hallway testers correctly name the kind of the active connection from the
  workspace alone within 5 seconds.
- **SC-013**: The same test script with a `RAISE NOTICE`, a warning and an error shows all three
  in Messages in server order with the correct levels in 100% of runs.
- **SC-014**: In a scripted editing session of 30 edits, inserts and deletes across 10 column
  types on the sample database, 30 of 30 land as intended and no other row changes (table
  contents compared before and after).
- **SC-015**: A PostgreSQL formatting corpus of at least 100 real-world scripts formats with
  100% meaning preserved, 100% stable on a second pass, and 0 valid scripts returned unchanged
  because of a parse failure; every quoted identifier and dollar-quoted body is verbatim.
- **SC-016**: The PostgreSQL rule set has at least 40 rules, each with at least one positive and
  one negative test; on the formatting corpus the analyser produces 0 crashes and 0 findings
  from SQL Server rules.
- **SC-017**: Parse of a script with 20 seeded syntax errors lists all 20 with the correct line,
  and the server log shows no contact.
- **SC-018**: A saved connection with a remembered password connects with 0 prompts in 10 of 10
  attempts, and after Forget the next attempt prompts; an inspection of the browser's storage,
  the logs and a settings export finds the password in none of them.

## Assumptions

- **One active connection per workspace.** The web edition keeps spec 041's single document and
  single active connection; "switching" means picking another saved connection. Saved
  connections of both kinds coexist. Multiple simultaneous connections are out of scope.
- **PostgreSQL versions.** The supported range is the community-supported major releases at
  implementation time (to be confirmed from the official versioning page during planning). The
  reference is built from the newest of them; older servers get version-gated entries; servers
  below the floor still connect with a warning.
- **Compatible services.** Anything that speaks the PostgreSQL protocol and exposes the standard
  catalogs is treated as PostgreSQL (Azure Database for PostgreSQL, Amazon RDS and Aurora
  PostgreSQL, Supabase, Neon). Forks without the standard catalogs are not targets.
- **Authentication.** Password authentication (the server's standard challenge methods) with
  the listed TLS modes. Certificate, Kerberos/GSSAPI, LDAP and PAM are not offered from the web
  edition. The engine runs on Windows, so Unix-socket connections are not offered.
- **Execution model.** Autocommit, as PostgreSQL clients default to; an explicit `BEGIN` opens a
  transaction that persists across runs on the session. Stopping a run is bounded by the query
  timeout, as it is for SQL Server on the current transport.
- **Database switch is a reconnect.** PostgreSQL cannot change the database of an open session.
  The spec 041 promise of a session-preserving selector does not hold for PostgreSQL; the user
  is told.
- **History is shared with SSMS.** PostgreSQL entries are stored in the same history as SQL
  Server runs. The SSMS extension's History window is not changed by this feature; it lists
  PostgreSQL entries as ordinary entries with their server and database names.
- **Formatting, Parse and analysis need the product's own understanding of PostgreSQL
  syntax.** The user chose the full tier; formatting, Parse and the rule set therefore rest on
  the product parsing PostgreSQL itself, not on the server. Refactorings and Find invalid
  objects stay out and follow FR-092. The existing style model is reused; options without a
  PostgreSQL meaning are marked and ignored. Formatting and analysis today run both in the
  browser and in the engine for SQL Server; both paths are dialect-aware.
- **Passwords.** Entered per browser session and held in memory by default; "Remember
  password" is an opt-in per saved connection that keeps the password in a protected store on
  the engine machine, the same approach the SSMS extension already uses for SQL Server logins.
  The browser never stores it.
- **Dialect follows the connection.** PostgreSQL mode exists only while a PostgreSQL connection
  is active; with no connection the editor is SQL Server mode as today. The reference lives
  with the engine.
- **Defaults.** Port 5432, database `postgres`, TLS mode prefer, application name "AKML SQL"
  shown to the server.
- **Documentation to study during planning** (official PostgreSQL manual of the named version):
  the SQL Commands reference; Lexical Structure (identifiers, constants, operators, comments);
  Data Types; Functions and Operators; the SQL Key Words appendix; the Error Codes appendix
  (SQLSTATE); System Catalogs and the Information Schema (for the explorer and cache); and the
  client connection parameters (for TLS mode semantics).
- **Language.** English only, as the product is today.
- **Settings import.** Exports made before this feature carry no kind; their connections become
  SQL Server on import.

## Out of Scope

- The SSMS extension: no PostgreSQL UI, prompts or behaviour in SSMS; SSMS stays SQL Server only.
- The pgAdmin plugin (a later spec; FR-120 keeps the door open).
- Refactorings (lightweight and schema-aware) and Find invalid objects for PostgreSQL: a
  follow-up spec.
- Server administration: roles and users, backups, restore, vacuum, replication, monitoring,
  extension management UI (`CREATE EXTENSION` runs as any SQL statement).
- Cross-dialect translation of scripts (T-SQL to PostgreSQL or back).
- Multiple simultaneous connections or per-document connections.
- psql meta-commands and `COPY ... FROM STDIN` / `TO STDOUT` (detected and refused with a hint).
- Certificate, Kerberos/GSSAPI, LDAP and PAM authentication; Unix-socket connections.
- PL/pgSQL debugging; `LISTEN`/`NOTIFY` listener UI; streaming replication.
- Formatting the inside of PL/pgSQL and other procedural-language bodies (preserved verbatim).
- Remembering passwords in the browser.

## Dependencies and Risks

- **A PostgreSQL server for verification**: a local installation or container with a sample
  database of comparable shape to the Northwind database used for SQL Server checks (a Northwind
  port, Pagila, or the chinook sample). Needed for live acceptance; automated gates run against
  a fake schema of the same shape.
- **The shared engine**: every engine change rides the SSMS path. Mitigation: FR-091 and SC-003
  make the existing suites the regression gate for the whole feature.
- **Understanding PostgreSQL syntax**: formatting, Parse and the rule set need the product to
  parse PostgreSQL itself. This is the largest single piece of the feature and the first thing
  the plan must settle (build, adopt or port, and how it is validated). Mitigation: a PostgreSQL
  formatting corpus with goldens, like the SQL Server parity corpus, and SC-015.
- **Remembered passwords widen the trust boundary**: any client paired with the engine can use
  one. Mitigation: opt-in per connection, protected store for the engine's account, explicit
  consent text (FR-104 to FR-106), and SC-018.
- **Editable results need key detection for PostgreSQL rows**: the product must learn which
  table and key a result column comes from. Mitigation: FR-130 keeps the rule simple (one table,
  primary key present) and FR-131 makes Apply all-or-nothing.
- **T-SQL code paths in the browser**: formatting, analysis and offline completion run in the
  browser today and would mangle PostgreSQL text if reached. Mitigation: every such path is
  chosen by kind in both the browser and the engine (FR-090, FR-152), and anything without a
  PostgreSQL counterpart follows FR-092.
- **Reference size and drift**: the official function and operator tables are large and change
  per release. Mitigation: FR-051 names the version, records sources, and keeps an omissions
  list; SC-006 sets the coverage bar.
- **Spec 041's database selector promise**: session preservation cannot hold on PostgreSQL;
  FR-031 makes the trade-off visible to the user.
- **AI privacy redaction**: the current literal redaction relies on parsing T-SQL. FR-081 turns a
  silent gap into an explicit choice for the user.
- **Spec 041 workspace**: this feature builds on the results pane, Messages and Problems tabs,
  Settings page, History page and status bar delivered there.
- **History store**: adding the kind to a shared store must keep older SSMS builds reading it
  (FR-072).
