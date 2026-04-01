# AKML SQL — Static Code Analysis Rules

The analysis engine ships with 120+ rules across 8 categories. Rules run on every keystroke (debounced) and on save.

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
| ST001 | Information | Inconsistent keyword casing — enforce `UPPER`, `lower`, or `PascalCase` |
| ST002 | Information | Old-style implicit alias (`col alias` without `AS`) |
| ST003 | Information | Old-style comma join syntax (`FROM a, b WHERE a.id = b.id`) instead of explicit `JOIN` |
| ST004 | Information | Missing statement terminator (`;`) |
| ST005 | Information | Inconsistent alias naming convention within the same query |
| ST006 | Information | Unnecessary square-bracket quoting on a non-reserved identifier |
| ST007 | Information | Object reference missing schema prefix (`dbo.`) |
| ST008 | Information | Inconsistent indentation detected |
| ST010 | Information | Line length exceeds configured maximum (default 120 characters) |
| ST011 | Information | Multiple SQL statements on a single line |
| ST012 | Information | Table alias defined inline without `AS` keyword |
| ST013 | Information | Missing blank line between top-level statements |
| ST014 | Information | Comment style inconsistency (`--` vs `/* */`) |
| ST015 | Information | Data type keyword casing inconsistency |
| ST016 | Information | Built-in function reference missing schema prefix (e.g. `dbo.fn_`) |
| ST017 | Information | Column list items not aligned across clauses |
| ST018 | Information | `TOP` used without parentheses — `TOP 10` vs `TOP (10)` |
| ST019 | Information | `ORDER BY` using ordinal position number instead of column name |
| ST020 | Information | `SELECT DISTINCT` where `GROUP BY` would be clearer |
| ST021 | Information | Mixed single and double quotes for string literals |
| ST022 | Information | Column alias uses camelCase — prefer PascalCase or consistent convention |
| ST023 | Information | Wildcard in object name pattern |
| ST024 | Information | Ambiguous date literal format (e.g. `'01/02/03'`) — use ISO 8601 (`'2024-01-02'`) |
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
| DEP001 | Warning | Replace with `VARCHAR(MAX)` / `VARBINARY(MAX)` | `text`, `ntext`, or `image` data type — removed in SQL Server 2022+ |
| DEP002 | Warning | — | Deprecated system stored procedure (e.g. `sp_addtype`, `sp_bindrule`) |
| DEP003 | Warning | — | `SET FMTONLY ON` — removed in SQL Server 2012 |
| DEP004 | Warning | — | Old outer-join operators (`*=`, `=*`) — removed in SQL Server 2012 |
| DEP005 | Warning | — | `RAISERROR` with style 0 and without `NOWAIT` — use `THROW` instead |
| DEP006 | Warning | — | Numbered procedure suffix (`;1`) — deprecated and ignored by the engine |
| DEP007 | Warning | — | `GROUP BY ALL` — removed in SQL Server 2012 |
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
