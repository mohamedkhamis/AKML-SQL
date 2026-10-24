# AKML SQL — Static Code Analysis Rules

The analysis engine ships with 132 rules across 8 categories. Rules run as you type (debounced). A separate set of 52 rules analyses PostgreSQL connections: see [PostgreSQL rules](#postgresql-rules-pg).

## Rule Identifiers

Each rule has a unique ID of the form `{Category}{Number}`, e.g. `PE001`. Rules are configurable per-project via `.casettings` files and can be suppressed inline with `-- akml-disable RuleId` comments.

## Severity Levels

| Level | Description |
|-------|-------------|
| `Error` | Definite bug or security issue — blocks or warns loudly |
| `Warning` | Likely problem, should be reviewed |
| `Information` | Style or best-practice guidance |
| `Hint` | Subtle improvement, low priority |
| `None` | Rule disabled |

Severities can be overridden per-project in `.casettings`.

---

## Performance (PE)

Rules that detect query patterns that harm runtime performance.

| Rule | Severity | Auto-Fix | Description |
|------|----------|----------|-------------|
| PE001 | Warning | — | `SELECT *` in stored procedures/views — returns unnecessary columns, prevents index-only scans |
| PE002 | Warning | Add `dbo.` | Unqualified object names — causes plan cache pollution via recompilation |
| PE003 | Error | — | `DELETE` or `UPDATE` without a `WHERE` clause — affects all rows |
| PE004 | Warning | — | `LIKE '%value'` leading wildcard — forces full table scan, cannot use index |
| PE009 | Warning | Insert `SET NOCOUNT ON` | Missing `SET NOCOUNT ON` in procedure — sends unnecessary row-count messages |
| PE010 | Warning | Replace with `SELECT 1` | `SELECT *` inside `EXISTS(...)` — wastes I/O, only existence matters |
| PE011 | Warning | — | `ORDER BY` inside `INSERT INTO … SELECT` — no effect and wastes sort cost |
| PE012 | Warning | — | `SET` options (`ANSI_NULLS`, `QUOTED_IDENTIFIER`, etc.) inside procedure body — should be outside |
| PE013 | Warning | — | Scalar function applied to a column in `WHERE` — makes the predicate non-SARGable |
| PE014 | Information | — | Missing index on foreign key column — FK lookups require table scans (schema required) |
| PE015 | Warning | — | `IN` list with more than 100 literal values — consider a temp table or TVP |
| PE016 | Information | — | Correlated subquery in `WHERE` — re-executes per outer row; consider a JOIN |
| PE017 | Warning | — | Non-SARGable function in `WHERE` (e.g. `YEAR(col) = 2024`) — use range predicate instead |
| PE018 | Warning | — | Table variable used for datasets that may exceed a few hundred rows — no statistics |
| PE019 | Warning | — | Table missing a clustered index — heap tables hurt range queries (schema required) |
| PE020 | Information | — | Index appears to be unused based on usage metadata (schema required) |
| PE021 | Warning | — | `DISTINCT` combined with `GROUP BY` — `DISTINCT` is redundant after aggregation |
| PE022 | Warning | — | `UNION` without `ALL` — implicit `DISTINCT` adds a sort; use `UNION ALL` if duplicates are acceptable |
| PE023 | Warning | — | Subquery nesting depth exceeds 3 — consider CTEs for readability and potential plan improvements |
| PE024 | Warning | — | Unbounded `SELECT` without `TOP`/`OFFSET-FETCH` on large tables (schema required) |
| PE025 | Warning | — | `GROUP BY` clause with no aggregate functions — may indicate a `DISTINCT` was intended (schema required) |
| PE026 | Information | — | `CROSS JOIN` — produces a Cartesian product; ensure this is intentional |
| PE027 | Warning | — | `SELECT INTO #temp` inside a loop — repeated temp-table creation is expensive |
| PE028 | Warning | — | Cursor usage — prefer set-based operations where possible |
| PE029 | Warning | — | `WHILE` loop iterating over a table row-by-row — consider set-based alternative |
| PE030 | Warning | — | Repeated temp table usage — consider a Table-Valued Parameter for batching |
| PE031 | Warning | — | Implicit data-type cast in predicate — may prevent index use (schema required) |
| PE032 | Warning | — | Statistics may be stale based on modification counters (schema required) |
| PE033 | Warning | — | `NOLOCK` / `READUNCOMMITTED` hint — can return dirty reads and phantom rows |
| PE034 | Information | — | `RECOMPILE` hint — verify it is needed; excessive recompilation degrades throughput |
| PE035 | Warning | — | View without `SCHEMABINDING` — prevents indexed views and allows unnoticed breaking changes (schema required) |

---

## Best Practices (BP)

Rules that enforce correct, maintainable T-SQL patterns.

| Rule | Severity | Auto-Fix | Description |
|------|----------|----------|-------------|
| BP001 | Warning | — | `@@IDENTITY` used — returns identity from any scope; use `SCOPE_IDENTITY()` instead |
| BP002 | Warning | — | `ISNUMERIC()` used — returns true for values like `$` and `1e2`; use `TRY_CONVERT` |
| BP003 | Warning | — | No `TRY/CATCH` block in procedure with DML — unhandled errors leave transactions open |
| BP004 | Error | Replace with `IS NULL` | `= NULL` comparison — always evaluates to `UNKNOWN`; use `IS NULL` |
| BP005 | Warning | — | `EXEC(string)` pattern — use `sp_executesql` with parameters to avoid SQL injection |
| BP006 | Information | — | Multiple DML statements without an explicit transaction — data may be partially committed |
| BP007 | Warning | — | Empty `CATCH` block — swallows errors silently |
| BP008 | Information | Add `RETURN` | Missing `RETURN` at end of procedure — implicit `RETURN 0` is not obvious |
| BP009 | Warning | Remove declaration | Variable declared but never read |
| BP011 | Information | Insert `SET XACT_ABORT ON` | `SET XACT_ABORT ON` missing in a procedure that uses transactions |
| BP012 | Information | — | Hard-coded date literal (e.g. `'2024-01-01'`) — use a parameter or `GETDATE()` |
| BP013 | Error | — | Dynamic SQL built with string concatenation — use `sp_executesql` with parameters |
| BP014 | Warning | — | `INSERT` without an explicit column list — breaks if table schema changes |
| BP015 | Information | Add `BEGIN/END` | Single-statement `IF` without `BEGIN/END` — fragile if a statement is added later |
| BP017 | Warning | — | `GOTO` usage — makes control flow hard to follow |
| BP018 | Information | — | `IF` nesting depth exceeds 3 — refactor into sub-procedures or CTEs |
| BP019 | Hint | — | Magic numeric constant — consider a named variable or configuration table |
| BP020 | Warning | — | `OUTPUT` parameter not assigned on all code paths |
| BP021 | Information | — | Procedure returns a single-row result set that could be `OUTPUT` parameters (schema required) |
| BP022 | Hint | — | `PRINT` statement — remove before production deployment |
| BP023 | Warning | — | `OUTPUT` parameter declared but never assigned (schema required) |
| BP024 | Hint | — | Parameter with no default value — callers must always supply it |
| BP025 | Hint | — | Procedure body exceeds 500 lines — consider splitting into sub-procedures |
| BP026 | Information | — | `SELECT` without `FROM` — use `SELECT @var = value` or a `VALUES` clause |
| BP027 | Information | — | `UPDATE` with a `FROM` JOIN — non-standard; behavior differs from ISO SQL |
| BP028 | Information | — | `DELETE` with a `FROM` JOIN — non-standard extension |
| BP029 | Hint | — | Scalar subquery in `SELECT` list — re-executes per row; consider a JOIN |
| BP030 | Information | — | Temp table referenced without schema qualifier — use `#table` consistently |

---

## Security (SE)

Rules that identify security vulnerabilities and misconfigurations.

| Rule | Severity | Auto-Fix | Description |
|------|----------|----------|-------------|
| SE001 | Error | — | `EXEC()` with concatenated string — SQL injection vector |
| SE002 | Error | — | Hard-coded password or credential literal in SQL text |
| SE003 | Warning | — | `GRANT` privilege to `PUBLIC` role — affects all users |
| SE004 | Warning | — | `EXECUTE AS OWNER` — elevates to object owner's permissions; verify intent |
| SE005 | Warning | — | `TRUSTWORTHY ON` for a database — allows CLR and ownership-chaining exploits |
| SE006 | Warning | Replace with `HASHBYTES('SHA2_256',…)` | Weak hash algorithm (`MD5` or `SHA1`) — not collision-resistant |
| SE007 | Warning | — | Cross-database object reference (`OtherDb.dbo.Table`) — increases attack surface |
| SE008 | Error | — | `xp_cmdshell` — executes OS commands; should be disabled in production |
| SE009 | Warning | — | `OPENROWSET` — accesses external data sources; can exfiltrate data |
| SE010 | Warning | — | Connection string containing credentials embedded in SQL |
| SE011 | Warning | — | `sa` login used directly — use dedicated least-privilege accounts |
| SE012 | Warning | — | Blank or empty password detected |
| SE013 | Error | — | Overly broad permission grant (e.g. `CONTROL SERVER`) |
| SE014 | Warning | — | DDL statement (`CREATE`/`ALTER`/`DROP`) inside a stored procedure body |
| SE015 | Warning | — | Object accessed without role-based permission check (schema required) |
| SE016 | Warning | — | Column name suggests sensitive data (SSN, credit card, etc.) without encryption |
| SE017 | Warning | — | Row-level security policy may be bypassed by the current user context (schema required) |
| SE018 | Warning | — | `ENCRYPTBYPASSPHRASE` — symmetric key encryption; prefer `ENCRYPTBYKEY` with AES-256 |
| SE019 | Warning | — | Connection string literal found in procedure body |
| SE020 | Warning | — | Pattern suggests privilege escalation (e.g. adding a user to `sysadmin`) |

---

## Style (ST)

Rules that enforce consistent formatting and naming conventions.

| Rule | Severity | Description |
|------|----------|-------------|
| ST001 | Hint | Inconsistent keyword casing — enforce `UPPER`, `lower`, or `PascalCase` |
| ST002 | Warning | Old-style implicit alias (`col alias` without `AS`) |
| ST003 | Warning | Old-style comma join syntax (`FROM a, b WHERE a.id = b.id`) instead of explicit `JOIN` |
| ST004 | Information | Missing statement terminator (`;`) |
| ST005 | Hint | Inconsistent alias naming convention within the same query |
| ST006 | Hint | Unnecessary square-bracket quoting on a non-reserved identifier |
| ST007 | Hint | Object reference missing schema prefix (`dbo.`) |
| ST008 | Hint | Inconsistent indentation detected |
| ST010 | Hint | Line length exceeds configured maximum (default 120 characters) |
| ST011 | Hint | Multiple SQL statements on a single line |
| ST012 | Hint | Table alias defined inline without `AS` keyword |
| ST013 | Hint | Missing blank line between top-level statements |
| ST014 | Hint | Comment style inconsistency (`--` vs `/* */`) |
| ST015 | Hint | Data type keyword casing inconsistency |
| ST016 | Hint | Built-in function reference missing schema prefix (e.g. `dbo.fn_`) |
| ST017 | Hint | Column list items not aligned across clauses |
| ST018 | Hint | `TOP` used without parentheses — `TOP 10` vs `TOP (10)` |
| ST019 | Warning | `ORDER BY` using ordinal position number instead of column name |
| ST020 | Hint | `SELECT DISTINCT` where `GROUP BY` would be clearer |
| ST021 | Hint | Mixed single and double quotes for string literals |
| ST022 | Hint | Column alias uses camelCase — prefer PascalCase or consistent convention |
| ST023 | Hint | Wildcard in object name pattern |
| ST024 | Hint | Ambiguous date literal format (e.g. `'01/02/03'`) — use ISO 8601 (`'2024-01-02'`) |
| ST025 | Hint | Excessive comment density — more comments than code lines |

---

## Design (DE)

Rules that identify structural schema design problems (checked in DDL statements).

| Rule | Severity | Description |
|------|----------|-------------|
| DE001 | Warning | Table `CREATE` statement has no `PRIMARY KEY` constraint |
| DE002 | Warning | Table has no clustered index (schema required) |
| DE003 | Error | Nullable column included in a `PRIMARY KEY` constraint |
| DE004 | Warning | `VARCHAR(1)` or `VARCHAR(2)` — consider `CHAR(n)` for fixed-length values |
| DE005 | Warning | `FLOAT` or `REAL` used for monetary/financial data — use `DECIMAL`/`MONEY` |
| DE006 | Warning | `SQL_VARIANT` column — poor for indexing and type safety |
| DE007 | Warning | `IDENTITY` on a non-integer column type — unexpected behavior |

---

## Deprecated (DEP)

Rules that flag SQL Server features removed or discouraged in modern versions.

| Rule | Severity | Auto-Fix | Description |
|------|----------|----------|-------------|
| DEP001 | Warning | Replace with `VARCHAR(MAX)` / `VARBINARY(MAX)` | `text`, `ntext`, or `image` data type — deprecated, to be removed in a future SQL Server version |
| DEP002 | Warning | — | Deprecated system stored procedure (e.g. `sp_addtype`, `sp_bindrule`) |
| DEP003 | Warning | — | `SET FMTONLY ON` — deprecated since SQL Server 2012, to be removed in a future version |
| DEP004 | Warning | — | Old outer-join operators (`*=`, `=*`) — removed in SQL Server 2012 |
| DEP005 | Warning | — | `RAISERROR` with style 0 and without `NOWAIT` — use `THROW` instead |
| DEP006 | Warning | — | Numbered procedure suffix (`;1`) — deprecated and ignored by the engine |
| DEP007 | Warning | — | `GROUP BY ALL` — deprecated, to be removed in a future SQL Server version |
| DEP008 | Warning | — | Old-style locking hint without `WITH` (e.g. `(NOLOCK)` vs `WITH (NOLOCK)`) |

---

## Execution (EX)

Rules that detect runtime errors detectable at parse/analysis time.

| Rule | Severity | Description |
|------|----------|-------------|
| EX001 | Warning | Division by literal zero (`/ 0`) |
| EX002 | Warning | Potential data truncation — inserting a longer value into a narrower column (schema required) |
| EX003 | Error | Ambiguous column reference — same column name exists in multiple joined tables (schema required) |
| EX004 | Information | Unreachable code after `RETURN` or `THROW` |
| EX005 | Warning | Identical condition in `IF`/`CASE` branches — one branch is dead code |
| EX006 | Warning | Always-true condition (`1=1`, `0=0`) — likely a copy-paste artifact |
| EX007 | Warning | Unclosed cursor — a `DECLARE CURSOR` without a matching `CLOSE` + `DEALLOCATE` in the same batch |
| EX008 | Warning | Unbalanced transaction — `BEGIN TRANSACTION` count differs from `COMMIT`/`ROLLBACK` count (CATCH-block rollbacks excluded) |

---

## Naming (NM)

Rules that enforce naming conventions for database objects.

| Rule | Severity | Auto-Fix | Description |
|------|----------|----------|-------------|
| NM001 | Warning | — | Reserved word used as an identifier without quoting |
| NM002 | Warning | Rename to `usp_` | Procedure name starts with `sp_` — SQL Server searches `master` first |
| NM003 | Information | — | Hungarian notation on table or view (`tbl_`, `vw_`) |
| NM004 | Information | — | Inconsistent naming style across objects of the same type |
| NM005 | Warning | — | Special characters in unquoted identifier |
| NM006 | Information | — | Single-letter table alias (`a`, `b`) — prefer descriptive aliases |

---

## PostgreSQL rules (PG)

A second rule set of 52 rules in the same eight categories analyses the scripts of a **PostgreSQL connection** (spec 042). The two sets never
meet: PostgreSQL text is never given to the SQL Server rules above, and a SQL Server script is never given to these. They run in the engine
(the web edition asks it), so they need an engine that advertises `dialect.postgresql.v1`, and they follow the `postgreSql.enabled` setting.

- **Ids** are `PG`, the category code and three digits (`PGPE001`). `.casettings`, `codeAnalysis.ruleOverrides`, the inline directives
  (`-- akml-disable PGBP002`, `-- akml-disable-line`, `-- noqa`), the session scope and the web edition's browser-local overrides all use them
  exactly as they use the SQL Server ids.
- **A script that does not parse** produces one finding, `PGSYN001`, at the first error, and no rule runs until it is fixed (the Problems tab
  after Parse lists every error). `PGSYN001` is not a rule: it is not in the rules list and cannot be switched off.
- **Schema.** A rule marked "required" runs only when the connection's schema is loaded, and stays silent about tables it does not know.
- **Statements are read one at a time**, as they are executed, so a psql meta-command (`\d`) is a finding of its own (`PGEX004`) and not a syntax error.
- **Every rule links to the PostgreSQL 18 manual page** it rests on (`PGST005`, a product convention, links here). The finding's description
  says which behaviour the rule is about.
- **Where they are listed.** Settings › Code analysis in the web edition lists every rule grouped by database and then category. The engine's
  `ListAnalysisRules` returns the PostgreSQL set only to a client that asks (`IncludePostgreSql`), so the SSMS Manage Rules window keeps listing
  the SQL Server rules.

### Performance (PGPE)

| Rule | Severity | Schema | Fix | What it flags, and the PostgreSQL behaviour behind it |
|------|----------|--------|-----|-------------------------------------------------------|
| PGPE001 | Warning | — | — | **SELECT *.** SELECT * sends every column of every row to the client, including columns added later, and rules out an index-only scan. It is not reported inside EXISTS, where the select list is never evaluated, and COUNT(*) is not a column list. [Manual](https://www.postgresql.org/docs/18/sql-select.html) |
| PGPE002 | Warning | — | — | **Leading wildcard in LIKE.** A B-tree index can answer LIKE and ILIKE only when the pattern starts with fixed text. A pattern that starts with % scans the whole table; a trigram index (pg_trgm) is the usual remedy. [Manual](https://www.postgresql.org/docs/18/indexes-types.html) |
| PGPE003 | Warning | — | — | **Function on a column in a predicate.** An index on a column is used for a condition on the column itself. When the column is wrapped in a function (lower(name) = 'x') the planner cannot use that index; an expression index on the function, or a rewritten condition, restores it. [Manual](https://www.postgresql.org/docs/18/indexes-expressional.html) |
| PGPE004 | Warning | required | — | **NOT IN over a nullable column.** x NOT IN (subquery) is unknown, not true, for every row when the subquery returns a NULL, so the query silently returns no rows; it also cannot be planned as an anti-join. NOT EXISTS has neither problem. Needs the schema to know the column is nullable. [Manual](https://www.postgresql.org/docs/18/functions-subquery.html) |
| PGPE005 | Information | required | — | **Implicit cast of a column.** There is no operator between an integer and a numeric, so integer_column = 1.5 converts the column to numeric for every row and an index on it is not used. Write the constant in the column's type, or compare ranges. (A quoted literal such as '5' takes the column's type and is not affected.) Needs the schema. [Manual](https://www.postgresql.org/docs/18/typeconv-oper.html) |
| PGPE006 | Information | — | — | **ORDER BY in a subquery.** The order of rows produced by a subquery or a common table expression is not kept by the query that uses it (SQL defines a table as unordered), so the sort is wasted unless a LIMIT, OFFSET or FETCH depends on it. DISTINCT ON and ARRAY(...) subqueries do depend on the order and are not reported. [Manual](https://www.postgresql.org/docs/18/sql-select.html) |
| PGPE007 | Information | — | — | **COUNT(*) used as an existence test.** count(*) has to visit every matching row, while EXISTS stops at the first one. Comparing a count with 0 (or testing it for >= 1) only asks whether a row exists. [Manual](https://www.postgresql.org/docs/18/functions-subquery.html) |
| PGPE008 | Hint | — | — | **Correlated subquery in the select list.** A scalar subquery in the select list that refers to the outer query is evaluated for each outer row. A join, a lateral join or an aggregate over a grouped subquery lets the planner choose a better strategy. Only references qualified with the outer table's name or alias are recognised. [Manual](https://www.postgresql.org/docs/18/sql-select.html) |
| PGPE009 | Information | — | — | **Large IN list.** An IN list is expanded into one comparison per value, so a list of more than 100 values is slow to parse and to plan. Pass the values as one array (= ANY($1)) or join to a VALUES list. [Manual](https://www.postgresql.org/docs/18/functions-comparisons.html) |
| PGPE010 | Warning | — | — | **Cross join.** Tables listed with commas and no condition that relates them, or CROSS JOIN, produce every combination of rows: a thousand rows against a thousand is a million. Set-returning functions and LATERAL items are expected to be combined with the rows before them and are not reported. [Manual](https://www.postgresql.org/docs/18/queries-table-expressions.html) |

### Best practices (PGBP)

| Rule | Severity | Schema | Fix | What it flags, and the PostgreSQL behaviour behind it |
|------|----------|--------|-----|-------------------------------------------------------|
| PGBP001 | Warning | — | — | **INSERT without a column list.** Without a column list, INSERT matches values to the table's columns by position. Dropping a column, or rebuilding the table with its columns in another order, changes what the statement means or makes it fail. Naming the columns keeps it correct. [Manual](https://www.postgresql.org/docs/18/sql-insert.html) |
| PGBP002 | Error | — | — | **UPDATE or DELETE without WHERE.** UPDATE and DELETE without a WHERE clause apply to every row of the table, and in autocommit mode the change is committed at once. Write WHERE true to say it is intended, or use TRUNCATE to empty a table. [Manual](https://www.postgresql.org/docs/18/sql-update.html) |
| PGBP003 | Information | — | — | **Comma-separated table list.** FROM a, b WHERE a.id = b.id is the pre-SQL-92 way to join. The join condition sits in WHERE, where forgetting it silently produces a cross join, and outer joins cannot be written. JOIN ... ON keeps the condition with the join. Set-returning functions and LATERAL items are not reported. [Manual](https://www.postgresql.org/docs/18/queries-table-expressions.html) |
| PGBP004 | Information | — | — | **Tables without aliases in a multi-table query.** When a query reads from several tables, columns should be qualified with a short alias; without aliases the same table cannot appear twice, and a column added to another table later can make an unqualified name ambiguous. [Manual](https://www.postgresql.org/docs/18/queries-table-expressions.html) |
| PGBP005 | Warning | required | — | **Ambiguous unqualified column.** PostgreSQL refuses a query in which an unqualified column name belongs to more than one table of the FROM list ("column reference is ambiguous"). Columns named in JOIN ... USING are merged and are exempt. Needs the schema to know each table's columns. [Manual](https://www.postgresql.org/docs/18/queries-table-expressions.html) |
| PGBP006 | Information | — | — | **SELECT INTO a table.** In SQL, SELECT ... INTO new_table creates a table; inside PL/pgSQL the same words assign variables, which is a common mix-up. CREATE TABLE AS says what the statement does, and accepts IF NOT EXISTS and storage options. [Manual](https://www.postgresql.org/docs/18/sql-selectinto.html) |
| PGBP007 | Warning | — | — | **Transaction never ended.** A transaction that is not ended holds its locks and its snapshot until the session closes, blocks VACUUM from removing dead rows, and loses its changes if the connection drops. End every BEGIN or START TRANSACTION with COMMIT or ROLLBACK. [Manual](https://www.postgresql.org/docs/18/sql-begin.html) |
| PGBP008 | Information | — | — | **SET search_path in a script.** Unqualified table and function names are looked up in the schemas of search_path, in order. Changing it in the middle of a script changes what every later unqualified name refers to, and the setting lasts for the session unless SET LOCAL is used inside a transaction. [Manual](https://www.postgresql.org/docs/18/ddl-schemas.html) |
| PGBP009 | Warning | — | — | **NATURAL JOIN.** NATURAL JOIN joins on every pair of columns with the same name. Adding a column to either table (a created_at, an id) changes the join silently. JOIN ... USING (column) names the columns and keeps the merged-column behaviour. [Manual](https://www.postgresql.org/docs/18/queries-table-expressions.html) |
| PGBP010 | Information | — | — | **Unused common table expression.** A WITH query that the rest of the statement never reads is dead code (PostgreSQL does not run it). A data-modifying WITH query (INSERT, UPDATE, DELETE) runs whether or not it is read, and is not reported. [Manual](https://www.postgresql.org/docs/18/queries-with.html) |

### Security (PGSE)

| Rule | Severity | Schema | Fix | What it flags, and the PostgreSQL behaviour behind it |
|------|----------|--------|-----|-------------------------------------------------------|
| PGSE001 | Warning | — | — | **Dynamic SQL built by concatenation.** EXECUTE runs a string as SQL. Building that string with &#124;&#124; puts values and names into the command text, which is how SQL injection happens. format() with %I (identifier) and %L (literal), and EXECUTE ... USING for values, quote every part correctly. Only function and DO bodies are examined. [Manual](https://www.postgresql.org/docs/18/plpgsql-statements.html#PLPGSQL-STATEMENTS-EXECUTING-DYN) |
| PGSE002 | Warning | — | — | **Broad GRANT.** GRANT ALL gives every privilege the object type has, including ones added in later releases, and a grant to PUBLIC gives it to every role that exists or will exist. Grant the specific privileges to specific roles (the principle of least privilege). [Manual](https://www.postgresql.org/docs/18/sql-grant.html) |
| PGSE003 | Error | — | — | **Password literal in a role statement.** A password written in CREATE ROLE or ALTER ROLE is stored in the script, in the server log when log_statement is on, and in pg_stat_activity while it runs. Set it from psql with \password, which sends it already hashed, or supply it from a secret store. [Manual](https://www.postgresql.org/docs/18/sql-createrole.html) |
| PGSE004 | Warning | — | — | **SECURITY DEFINER without a fixed search_path.** A SECURITY DEFINER function runs with its owner's privileges. If the function does not set its own search_path, the caller's decides which objects its unqualified names resolve to, and a caller who can create objects in an earlier schema can make the function run their code with the owner's rights. [Manual](https://www.postgresql.org/docs/18/sql-createfunction.html) |
| PGSE005 | Warning | — | — | **Server file or program access.** COPY with a file name or PROGRAM, and functions such as pg_read_file, pg_ls_dir, lo_import and lo_export, act on the server's file system with the server's account, not the client's. They need elevated privileges, and a value that reaches them from outside is a path to files and commands on the server. [Manual](https://www.postgresql.org/docs/18/sql-copy.html) |

### Style (PGST)

| Rule | Severity | Schema | Fix | What it flags, and the PostgreSQL behaviour behind it |
|------|----------|--------|-----|-------------------------------------------------------|
| PGST001 | Hint | — | — | **Inconsistent keyword case.** PostgreSQL reads key words in any case, so SELECT and select are the same word; a script that mixes styles is harder to read. The Format SQL command applies the active style's casing to the whole script. [Manual](https://www.postgresql.org/docs/18/sql-syntax-lexical.html) |
| PGST002 | Hint | — | yes | **Missing statement terminator.** A semicolon ends a statement. The last statement of a script may omit it, but adding another statement after it then merges the two, and psql and most tools need it to know the statement is complete. [Manual](https://www.postgresql.org/docs/18/sql-syntax.html) |
| PGST003 | Hint | — | yes | **!= instead of <>.** PostgreSQL accepts != but converts it to <>, the SQL standard operator, while parsing; both mean the same. <> is what the server prints in plans, logs and view definitions. [Manual](https://www.postgresql.org/docs/18/functions-comparison.html) |
| PGST004 | Hint | — | yes | **Column alias without AS.** AS is optional before a column alias, but leaving it out turns a forgotten comma into an alias: SELECT a b FROM t returns one column named b. Writing AS makes the alias explicit. [Manual](https://www.postgresql.org/docs/18/queries-select-lists.html) |
| PGST005 | Hint | — | — | **Line longer than 200 characters.** Lines of more than 200 characters are hard to read and to review, and a diff shows the whole line as changed. Break the statement over several lines (the Format SQL command does). [Manual](https://akml.khamis.work/docs/analysis-rules) |

### Design (PGDE)

| Rule | Severity | Schema | Fix | What it flags, and the PostgreSQL behaviour behind it |
|------|----------|--------|-----|-------------------------------------------------------|
| PGDE001 | Warning | — | — | **timestamp without time zone.** timestamp stores a date and time with no zone and returns it unchanged, so what it means depends on the session's TimeZone of whoever wrote it. timestamptz stores an absolute instant (in UTC) and shows it in the reader's zone; PostgreSQL's own wiki recommends it for almost every use. [Manual](https://www.postgresql.org/docs/18/datatype-datetime.html) |
| PGDE002 | Information | — | — | **char(n) column.** char(n) pads every value with spaces to n characters and has no speed or storage advantage in PostgreSQL; it is usually slower because of the padding and surprises comparisons and concatenation. text or varchar(n) is almost always what is meant. [Manual](https://www.postgresql.org/docs/18/datatype-character.html) |
| PGDE003 | Warning | — | — | **money column.** money has a fixed fractional precision taken from the lc_monetary setting of the session, so the same value can be read or restored differently on another server. numeric(p, s) holds an exact amount with a precision you choose. [Manual](https://www.postgresql.org/docs/18/datatype-money.html) |
| PGDE004 | Information | — | — | **serial column.** serial is shorthand for a sequence plus a default, owned by the column only by convention: a copied table keeps using the original sequence, and the privileges on the sequence must be granted separately. GENERATED ... AS IDENTITY (PostgreSQL 10 and later) is the standard form and avoids both. [Manual](https://www.postgresql.org/docs/18/datatype-numeric.html) |
| PGDE005 | Warning | — | — | **Table without a primary key.** Without a primary key, rows cannot be told apart, duplicates are accepted, logical replication cannot replicate UPDATE and DELETE, and tools cannot edit the rows by key. Partitions (which take the key of their parent), typed tables, temporary tables and tables created LIKE another table are not reported. [Manual](https://www.postgresql.org/docs/18/ddl-constraints.html) |
| PGDE006 | Warning | required | — | **Foreign key without an index.** PostgreSQL indexes the referenced (primary key) side of a foreign key but not the referencing side. Deleting or updating a row of the referenced table then scans the whole referencing table, and joins on the key are slow. An index whose leading columns are the key's columns fixes both. Needs the schema to see existing indexes. [Manual](https://www.postgresql.org/docs/18/ddl-constraints.html) |
| PGDE007 | Information | — | — | **json column.** json stores the text as written and re-parses it on every access; it cannot be indexed or compared. jsonb stores a decomposed binary form, supports GIN indexes and containment operators, and is what almost every application wants. [Manual](https://www.postgresql.org/docs/18/datatype-json.html) |
| PGDE008 | Information | — | — | **timetz column.** time with time zone attaches a zone offset to a time of day, which has no meaning without a date (daylight saving cannot be applied). The manual itself discourages it: use time, or timestamptz when the instant matters. [Manual](https://www.postgresql.org/docs/18/datatype-datetime.html) |

### Deprecated (PGDEP)

| Rule | Severity | Schema | Fix | What it flags, and the PostgreSQL behaviour behind it |
|------|----------|--------|-----|-------------------------------------------------------|
| PGDEP001 | Error | — | — | **WITH OIDS and default_with_oids.** PostgreSQL 12 removed the hidden oid column of user tables: CREATE TABLE ... WITH OIDS is a syntax error (and is reported as one), and the default_with_oids setting no longer exists. The forms that still parse, SET default_with_oids and WITH (oids = ...), are reported here. [Manual](https://www.postgresql.org/docs/18/sql-createtable.html) |
| PGDEP002 | Information | — | yes | **Named argument with :=.** func(name := value) is the older spelling of a named argument; the SQL standard form name => value has been the documented one since PostgreSQL 9.5, and := is kept only for compatibility. [Manual](https://www.postgresql.org/docs/18/sql-syntax-calling-funcs.html) |
| PGDEP003 | Error | — | — | **Removed time types.** abstime, reltime and tinterval were removed in PostgreSQL 12. Use timestamptz (or timestamp), interval and a pair of timestamps or a tstzrange. [Manual](https://www.postgresql.org/docs/18/datatype-datetime.html) |
| PGDEP004 | Information | — | — | **Table inheritance.** Splitting a large table into child tables with INHERITS needs constraints, triggers or rules that you write and maintain, and does not share indexes or unique constraints. Declarative partitioning (PARTITION BY, since PostgreSQL 10) does the routing and pruning itself. [Manual](https://www.postgresql.org/docs/18/ddl-partitioning.html) |

### Execution (PGEX)

| Rule | Severity | Schema | Fix | What it flags, and the PostgreSQL behaviour behind it |
|------|----------|--------|-----|-------------------------------------------------------|
| PGEX001 | Error | — | — | **Statement that cannot run in a transaction block.** CREATE INDEX CONCURRENTLY, DROP INDEX CONCURRENTLY, REINDEX CONCURRENTLY, VACUUM, CREATE and DROP DATABASE, CREATE and DROP TABLESPACE, ALTER SYSTEM, CLUSTER without a table and DISCARD ALL manage resources outside the transaction system, so the server rejects them between BEGIN and COMMIT. [Manual](https://www.postgresql.org/docs/18/sql-createindex.html) |
| PGEX002 | Hint | — | — | **DROP or TRUNCATE outside a transaction.** In autocommit mode each statement commits as it finishes, so a DROP or TRUNCATE cannot be undone. PostgreSQL, unlike most databases, runs DDL inside a transaction: BEGIN; ... ROLLBACK; lets you check the effect first. [Manual](https://www.postgresql.org/docs/18/sql-begin.html) |
| PGEX003 | Warning | — | — | **SET LOCAL outside a transaction.** SET LOCAL lasts until the end of the current transaction. Outside a transaction block that is the end of the statement itself, so the setting has no effect and the server only issues a warning. [Manual](https://www.postgresql.org/docs/18/sql-set.html) |
| PGEX004 | Warning | — | — | **psql meta-command.** Backslash commands (\d, \c, \gexec, \i ...) belong to the psql client; the server does not understand them. The workspace does not send them to the server and does not run them, so what follows may not behave as in psql. [Manual](https://www.postgresql.org/docs/18/app-psql.html) |
| PGEX005 | Warning | — | — | **COPY FROM STDIN / TO STDOUT.** COPY ... FROM STDIN and COPY ... TO STDOUT stream the data through the client connection. A script run from the workspace cannot supply or collect that stream, so the statement is not run; use a query, INSERT, or psql's \copy. [Manual](https://www.postgresql.org/docs/18/sql-copy.html) |

### Naming (PGNM)

| Rule | Severity | Schema | Fix | What it flags, and the PostgreSQL behaviour behind it |
|------|----------|--------|-----|-------------------------------------------------------|
| PGNM001 | Warning | — | — | **Identifier longer than 63 bytes.** PostgreSQL stores a name in 63 bytes (NAMEDATALEN - 1) and silently truncates a longer one, with only a NOTICE. Two long names that differ after the 63rd byte become the same name, and the stored name no longer matches what was written. The limit counts UTF-8 bytes, not characters. [Manual](https://www.postgresql.org/docs/18/sql-syntax-lexical.html#SQL-SYNTAX-IDENTIFIERS) |
| PGNM002 | Information | — | — | **Capitals in an unquoted name.** PostgreSQL folds an unquoted name to lower case: Orders, ORDERS and orders are the same table. A table created as "Orders" (quoted) is a different table that unquoted references can never reach. Write names in lower case, or quote them every time. Function names and types are not reported. [Manual](https://www.postgresql.org/docs/18/sql-syntax-lexical.html#SQL-SYNTAX-IDENTIFIERS) |
| PGNM003 | Warning | — | — | **Reserved word used as a name.** A reserved word (user, order, group, table, select ...) can only be used as a name when it is written in double quotes, and then every statement that mentions it, in every client and tool, must quote it too. A different name avoids the problem. [Manual](https://www.postgresql.org/docs/18/sql-keywords-appendix.html) |
| PGNM004 | Warning | — | — | **Name used both quoted and unquoted.** "Orders" (quoted, with a capital) and Orders (unquoted, folded to orders) are two different names. A script that uses both for one object refers to two objects, and the unquoted one usually fails with "does not exist". [Manual](https://www.postgresql.org/docs/18/sql-syntax-lexical.html#SQL-SYNTAX-IDENTIFIERS) |
| PGNM005 | Information | — | — | **Name that always needs quotes.** A name with spaces or punctuation, or one that begins with a digit, cannot be written without double quotes in any statement, view, function body or tool. Letters, digits and underscores, starting with a letter or underscore, keep the name usable everywhere. [Manual](https://www.postgresql.org/docs/18/sql-syntax-lexical.html#SQL-SYNTAX-IDENTIFIERS) |

## Configuration

### Per-Project Overrides (`.casettings`)

Place a `.casettings` file in any directory to override rule severities for that subtree:

```jsonc
{
  "rules": {
    "PE001": { "severity": "Error",   "enabled": true },
    "ST008": { "severity": "None",    "enabled": false },
    "NM002": { "severity": "Warning", "enabled": true }
  },
  "globalSuppressions": [
    { "ruleId": "NM003", "reason": "Legacy naming convention" }
  ]
}
```

### Inline Suppressions

Suppress a rule for a block:

```sql
-- akml-disable PE001
SELECT * FROM dbo.Orders
-- akml-enable PE001
```

Suppress for a single line:

```sql
SELECT * FROM dbo.Orders  -- akml-disable-line PE001
```

Suppress for the rest of the file — a disable with no matching enable:

```sql
-- akml-disable PE001
```

See [Static code analysis](topics/static-analysis.md#turn-a-rule-off) for the full set of scopes,
including the session-only one offered by the lightbulb and warning-glyph menus.

### Global Settings

See [configuration.md](configuration.md) for the `codeAnalysis` section of `config.json`:

| Setting | Default | Description |
|---------|---------|-------------|
| `enabled` | `true` | Master switch |
| `runOnType` | `true` | Analyze after each keystroke (debounced) |
| `runOnSave` | `true` | Analyze on file save |
| `autoFixOnFormat` | `false` | Apply safe auto-fixes when running Format Document |
| `squiggleStyle` | `"underline"` | Squiggle rendering: `underline`, `dotted`, `solid` |
| `showInErrorList` | `true` | Show issues in the VS Error List window |
