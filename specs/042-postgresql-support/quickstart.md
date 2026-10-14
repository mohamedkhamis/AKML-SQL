# Quickstart: PostgreSQL support in the engine and the web edition

Acceptance gate for "done" (constitution, Development Workflow). Each scenario names the success
criterion it proves. Automated gates first, then the manual walk-through.

## 0. Environment

1. **PostgreSQL 18** on `localhost:5432` (installer from postgresql.org, or
   `docker run -e POSTGRES_PASSWORD=akml -p 5432:5432 postgres:18`). Enable `ssl = on` with the
   default self-signed certificate in `postgresql.conf` for the TLS checks (FR-101).
2. **Sample database**: load a Northwind port, e.g. `northwind.sql` from
   `github.com/tasawar-hussain/northwind_psql` (or `pthom/northwind_psql`), into a database named
   `northwind`; then create a second schema for the search-path checks:
   ```sql
   CREATE SCHEMA sales;
   CREATE TABLE sales.orders (id serial PRIMARY KEY, note text);
   COMMENT ON TABLE sales.orders IS 'Shadow of public.orders for search-path tests';
   ```
3. **A SQL Server** with Northwind on `localhost:1433` (as for spec 041) for the switching
   scenarios.
4. Builds (Debug for the browser suites, see CLAUDE.md):
   ```bash
   "$MSBUILD" AKML-SQL.slnx -t:Restore -v:quiet
   "$MSBUILD" AKML-SQL.slnx -t:Build -p:Configuration=Release -m -v:minimal
   dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Debug
   dotnet build src/AkmlSql.Web/AkmlSql.Web.csproj -c Debug
   dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64
   ```
   The published engine folder must contain the pgsqlparser native library (spike S1).
5. Live-test switches: `AKML_PG_TEST_CONNECTION="Host=localhost;Port=5432;Database=northwind;Username=postgres;Password=akml"`.
   Tests that need a server call `WithPgOrSkipAsync` and are skipped when it is unset or the
   server is unreachable.

## 1. Automated gates

| Gate | Command | Proves |
| --- | --- | --- |
| Existing suites unchanged | `dotnet test tests/AkmlSql.Engine.Tests`, `tests/AkmlSql.Formatting.Tests`, `tests/AkmlSql.Analysis.Tests`, `tests/AkmlSql.IntelliSense.Tests`, `tests/AkmlSql.Core.Tests`, `tests/AkmlSql.Web.Tests`, `tests/AkmlSql.Shell.Shared.Tests`; compare with `scripts/compare-test-results.ps1` against the pre-feature run | SC-003, FR-091 |
| Cross-dialect isolation | `dotnet test tests/AkmlSql.PostgreSql.Tests --filter Category=Isolation` (dictionary cross-check + 200-case probes both ways) | SC-004 |
| PostgreSQL completion corpus | `dotnet test tests/AkmlSql.PostgreSql.Tests --filter Category=CompletionCorpus` (≥ 300 cases, ≥ 95 %, ratchet file `tests/pg-completion-corpus/gate.json`) | SC-005 |
| Reference coverage | `dotnet test tests/AkmlSql.PostgreSql.Tests --filter Category=Reference` (100 % commands and types, ≥ 95 % functions and operators against the generator's source counts; omissions listed) | SC-006 |
| Format corpus | `dotnet test tests/AkmlSql.PostgreSql.Tests --filter Category=FormatCorpus` (≥ 100 scripts; parse-equivalence, idempotency, verbatim quoted identifiers and dollar bodies) | SC-015 |
| Rules | `dotnet test tests/AkmlSql.PostgreSql.Tests --filter Category=Rules` (≥ 40 rules, positive + negative each; 0 crashes and 0 SQL Server findings on the format corpus) | SC-016 |
| Parse | `--filter Category=Parse` (20 seeded errors, all listed with the right line; no server contact) | SC-017 |
| Engine handlers (live) | `dotnet test tests/AkmlSql.Engine.Tests --filter Category=PostgreSql` with `AKML_PG_TEST_CONNECTION` set | SC-007, SC-008, SC-013, SC-014 |
| Browser | `dotnet build src/AkmlSql.Web -c Debug` then `dotnet test tests/AkmlSql.Web.E2E.Tests --filter PostgresWorkspaceTests -- xUnit.ParallelizeTestCollections=false` | SC-001, SC-002, SC-009, SC-010, SC-018 |
| Theme gate | `scripts/generate-theme-css.ps1 -CheckOnly` | constitution II |

## 2. Manual walk-through (one scenario per user story)

### US1 Connect and run (SC-001, SC-007, SC-013)

1. Open the web edition paired with the Debug engine. Status bar → connection → the dialog shows
   the kind switch; pick **PostgreSQL**, enter `localhost`, 5432, `northwind`, `postgres`,
   password, TLS *prefer*. **Test** → "PostgreSQL 18.x on localhost:5432". **Save**, **Connect**.
   Status bar shows the PostgreSQL label and icon, server, database, user, version.
2. Run:
   ```sql
   SELECT customer_id, company_name FROM customers LIMIT 3;
   SELECT count(*) FROM orders;
   UPDATE products SET units_in_stock = units_in_stock WHERE product_id = 1;
   DO $$ BEGIN RAISE NOTICE 'hello from plpgsql'; END $$;
   SELECT * FROM no_such_table;
   SELECT 'never runs';
   ```
   Expect: two result sets, `UPDATE 1` and the notice in Messages in order, an error with
   `SQLSTATE 42P01`, detail/hint lines, click moves the caret to line 5, status says 4 of 6
   statements ran, the last SELECT did not run.
3. Run `BEGIN; SELECT 1/0;` then `SELECT 1;` → status shows "Transaction aborted", the message
   explains, **Rollback** clears it.
4. Put a `GO` line in the script → server error with the "GO is a SQL Server batch separator"
   hint; nothing stripped.
5. Point a SQL Server connection at port 5432 and Test → "did not answer as a SQL Server
   server. Check the database kind."

### US2 Browse (SC-008)

1. Schema explorer: `public` and `sales` schemas; Tables / Views / Materialized Views /
   Functions / Procedures / Sequences headers; system schemas hidden behind the toggle.
2. Expand `public.orders`: columns with `format_type` names, nullability, defaults, identity;
   primary key, foreign keys, indexes; comments shown.
3. Run `CREATE TABLE public.qs_demo (id int PRIMARY KEY);` → appears without manual refresh.
4. Database selector → pick `postgres` → warning about reconnect and lost session state →
   confirm → explorer, status and suggestions follow. Switch back.
5. Connect as a role without USAGE on `sales` → `public` loads; `sales` marked not accessible.

### US3 Write with help (SC-005)

1. Type `SELECT * FROM` and a space → tables of the search path unqualified, `sales.orders` qualified;
   `"Order Details"`-style names inserted double-quoted.
2. `SELECT o. FROM orders o` → columns with types. `INSERT INTO orders (...) VALUES (...) ON
   CONFLICT` and a space → `DO NOTHING` / `DO UPDATE`.
3. Hover `string_agg` → signatures, description, link; inside `date_trunc(` → signature help.
4. Type `@`, `#`, `[` → no T-SQL suggestions. Switch to the SQL Server connection → no PostgreSQL
   functions or types offered.
5. Connect to a PostgreSQL 14 server (if available) → entries added after 14 absent.

### US4 Switch (SC-002, SC-009, SC-011, SC-012)

1. With text in the editor, switch SQL Server ↔ PostgreSQL ten times from the picker: status
   bar, explorer, highlighting, completion follow within 3 s, text intact, no reload.
2. History page: kind badge; filter PostgreSQL; `kind:postgresql` in search; restore suggests
   the saved connection.
3. AI: "write a query for the three most recent orders per customer" on PostgreSQL → `LIMIT`,
   window function or `DISTINCT ON`, double quotes if needed; on SQL Server → `TOP`/T-SQL.
4. Turn on **Remember password**, reconnect → no prompt (SC-018); Settings → Connections →
   Forget → next connect prompts; inspect the browser storage, the engine log and a settings
   export → no password anywhere.
5. Pair with an engine built before this feature (or set `postgreSql.enabled: false`) → the
   PostgreSQL choice is disabled with the reason.

### US5 Isolation (SC-003, SC-004, SC-010)

1. Compare the full existing automated run with the baseline: identical.
2. On PostgreSQL: Refactor menu entries and Find invalid objects disabled with "Not available for
   PostgreSQL"; the shortcuts show the message; the engine log shows no T-SQL handler invoked for
   the session.
3. Open SSMS with the same engine: no new UI, prompt or menu; its history window lists
   PostgreSQL entries as ordinary entries.

### US6 Edit results (SC-014)

1. `SELECT * FROM products WHERE product_id <= 5;` → editable. Change `unit_price`, set
   `supplier_id` to NULL (Ctrl+0), add a row, delete a row, Apply → each lands; table contents
   verified in psql.
2. `SELECT p.*, c.category_name FROM products p JOIN categories c USING (category_id);` →
   read-only with the reason. `SELECT * FROM v_demo;` (a view) → read-only.
3. Type `abc` into a numeric cell → invalid, server message, not applied. Delete a row in psql,
   then Apply an edit to it → "not applied", nothing else changed.

### US7 Format and Parse (SC-015, SC-017)

1. Format a script with SELECT, `INSERT ... ON CONFLICT`, `UPDATE ... RETURNING`, a CTE and a
   window function → laid out per the active style; format again → unchanged.
2. Format `CREATE FUNCTION f() RETURNS int AS $$ BEGIN RETURN 1; END $$ LANGUAGE plpgsql;` →
   body verbatim; `"OrderId"` keeps quotes and case.
3. Format a script with a syntax error → unchanged + message. Format Styles page on a PostgreSQL
   session → T-SQL-only options marked "no effect for PostgreSQL", PostgreSQL preview sample.
4. Ctrl+F5 on a script with three errors → three Problems entries with line/column; the engine
   log shows no server round trip.

### US8 Code analysis (SC-016)

1. Script with a 70-character identifier, `DELETE FROM orders;`, `BEGIN; CREATE INDEX
   CONCURRENTLY ...; COMMIT;`, a `timestamp` column → PGNM001, PGBP002, PGEX001, PGDE001 with
   links; `-- akml-disable-line PGBP002` removes that one.
2. Rules window: PostgreSQL rules grouped by category under their own kind; SQL Server rules
   separate. A T-SQL script on a SQL Server session shows no PG findings.

## 3. Release notes and docs

`doc/progress.md` (spec 042 section), `doc/ipc-api.md` (keys and messages 221–224),
`doc/configuration.md` (`postgreSql.enabled`), `doc/analysis-rules.md` (PG rule set),
`doc/postgresql-reference.md` (generated), `CLAUDE.md` (project structure + technologies),
`THIRD-PARTY-NOTICES.txt` (libpg_query, upb, utf8_range, xxHash, Npgsql, PostgreSQL docs).
