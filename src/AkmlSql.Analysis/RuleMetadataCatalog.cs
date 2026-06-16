namespace AkmlSql.Engine.Analysis;

/// <summary>
/// Display metadata for an analysis rule: a short human-readable name, a one-line
/// description, and whether the rule ships a concrete auto-fix.
/// </summary>
public sealed class RuleMetadata
{
    public RuleMetadata(string name, string description, bool autoFixable, string referenceUrl = "")
    {
        Name = name;
        Description = description;
        AutoFixable = autoFixable;
        ReferenceUrl = referenceUrl;
    }

    /// <summary>Short rule name shown in the Manage Rules list (e.g. "SELECT * in procedures/views").</summary>
    public string Name { get; }

    /// <summary>One-line description shown in the rule detail pane / tooltip.</summary>
    public string Description { get; }

    /// <summary>
    /// True when the rule provides a deterministic auto-fix. Drives the lightbulb colour
    /// (T054): orange = auto-fixable quick action, blue = informational only.
    /// </summary>
    public bool AutoFixable { get; }

    /// <summary>
    /// Spec 030 T055 (FR-028) — optional reference/documentation URL (http/https) shown as a
    /// clickable link in the Ctrl-hover issue-details popup. Empty when none is configured.
    /// </summary>
    public string ReferenceUrl { get; }
}

/// <summary>
/// Spec 030 T052 — sidecar catalog mapping each <see cref="IAnalysisRule.RuleId"/> to its display
/// metadata (name, description, auto-fixable flag). Extracted verbatim from
/// <c>doc/analysis-rules.md</c> so the doc stays the single source of truth.
///
/// <para>This is a sidecar rather than fields on <see cref="IAnalysisRule"/> on purpose: it keeps
/// metadata in one reviewable table instead of scattering Name/Description literals across 130+
/// rule classes, and it lets the <c>ListAnalysisRules</c> handler report a rule that has no doc
/// entry without crashing — <see cref="Get"/> falls back to (RuleId, empty, false).</para>
/// </summary>
public static class RuleMetadataCatalog
{
    /// <summary>
    /// Looks up display metadata for a rule id. Unknown ids fall back to a synthetic entry whose
    /// Name is the id itself (so the dialog still renders a row) with an empty description and no
    /// auto-fix.
    /// </summary>
    public static RuleMetadata Get(string ruleId) =>
        Entries.TryGetValue(ruleId, out var meta) ? meta : new RuleMetadata(ruleId, string.Empty, false);

    /// <summary>True when the catalog has an explicit entry for the rule id.</summary>
    public static bool Contains(string ruleId) => Entries.ContainsKey(ruleId);

    /// <summary>The number of catalogued rules (used by tests to assert coverage).</summary>
    public static int Count => Entries.Count;

    private static readonly Dictionary<string, RuleMetadata> Entries = new(StringComparer.OrdinalIgnoreCase)
    {
        // ── Performance (PE) ──
        ["PE001"] = new("SELECT * in procedures/views", "`SELECT *` in stored procedures/views — returns unnecessary columns, prevents index-only scans", false),
        ["PE002"] = new("Unqualified object name", "Unqualified object names — causes plan cache pollution via recompilation", true),
        ["PE003"] = new("DELETE/UPDATE without WHERE", "`DELETE` or `UPDATE` without a `WHERE` clause — affects all rows", false),
        ["PE004"] = new("Leading wildcard LIKE", "`LIKE '%value'` leading wildcard — forces full table scan, cannot use index", false),
        ["PE009"] = new("Missing SET NOCOUNT ON", "Missing `SET NOCOUNT ON` in procedure — sends unnecessary row-count messages", true),
        ["PE010"] = new("SELECT * inside EXISTS", "`SELECT *` inside `EXISTS(...)` — wastes I/O, only existence matters", true),
        ["PE011"] = new("ORDER BY in INSERT SELECT", "`ORDER BY` inside `INSERT INTO … SELECT` — no effect and wastes sort cost", false),
        ["PE012"] = new("SET options in procedure body", "`SET` options (`ANSI_NULLS`, `QUOTED_IDENTIFIER`, etc.) inside procedure body — should be outside", false),
        ["PE013"] = new("Function on column in WHERE", "Scalar function applied to a column in `WHERE` — makes the predicate non-SARGable", false),
        ["PE014"] = new("Missing index on FK column", "Missing index on foreign key column — FK lookups require table scans (schema required)", false),
        ["PE015"] = new("Large IN literal list", "`IN` list with more than 100 literal values — consider a temp table or TVP", false),
        ["PE016"] = new("Correlated subquery in WHERE", "Correlated subquery in `WHERE` — re-executes per outer row; consider a JOIN", false),
        ["PE017"] = new("Non-SARGable function in WHERE", "Non-SARGable function in `WHERE` (e.g. `YEAR(col) = 2024`) — use range predicate instead", false),
        ["PE018"] = new("Table variable for large sets", "Table variable used for datasets that may exceed a few hundred rows — no statistics", false),
        ["PE019"] = new("Table missing clustered index", "Table missing a clustered index — heap tables hurt range queries (schema required)", false),
        ["PE020"] = new("Unused index", "Index appears to be unused based on usage metadata (schema required)", false),
        ["PE021"] = new("DISTINCT with GROUP BY", "`DISTINCT` combined with `GROUP BY` — `DISTINCT` is redundant after aggregation", false),
        ["PE022"] = new("UNION without ALL", "`UNION` without `ALL` — implicit `DISTINCT` adds a sort; use `UNION ALL` if duplicates are acceptable", false),
        ["PE023"] = new("Deep subquery nesting", "Subquery nesting depth exceeds 3 — consider CTEs for readability and potential plan improvements", false),
        ["PE024"] = new("Unbounded SELECT on large table", "Unbounded `SELECT` without `TOP`/`OFFSET-FETCH` on large tables (schema required)", false),
        ["PE025"] = new("GROUP BY without aggregate", "`GROUP BY` clause with no aggregate functions — may indicate a `DISTINCT` was intended (schema required)", false),
        ["PE026"] = new("CROSS JOIN Cartesian product", "`CROSS JOIN` — produces a Cartesian product; ensure this is intentional", false),
        ["PE027"] = new("SELECT INTO temp in loop", "`SELECT INTO #temp` inside a loop — repeated temp-table creation is expensive", false),
        ["PE028"] = new("Cursor usage", "Cursor usage — prefer set-based operations where possible", false),
        ["PE029"] = new("Row-by-row WHILE loop", "`WHILE` loop iterating over a table row-by-row — consider set-based alternative", false),
        ["PE030"] = new("Repeated temp table usage", "Repeated temp table usage — consider a Table-Valued Parameter for batching", false),
        ["PE031"] = new("Implicit cast in predicate", "Implicit data-type cast in predicate — may prevent index use (schema required)", false),
        ["PE032"] = new("Stale statistics", "Statistics may be stale based on modification counters (schema required)", false),
        ["PE033"] = new("NOLOCK dirty reads", "`NOLOCK` / `READUNCOMMITTED` hint — can return dirty reads and phantom rows", false, "https://learn.microsoft.com/sql/t-sql/queries/hints-transact-sql-table"),
        ["PE034"] = new("RECOMPILE hint", "`RECOMPILE` hint — verify it is needed; excessive recompilation degrades throughput", false),
        ["PE035"] = new("View without SCHEMABINDING", "View without `SCHEMABINDING` — prevents indexed views and allows unnoticed breaking changes (schema required)", false),

        // ── Best Practices (BP) ──
        ["BP001"] = new("@@IDENTITY used", "`@@IDENTITY` used — returns identity from any scope; use `SCOPE_IDENTITY()` instead", false, "https://learn.microsoft.com/sql/t-sql/functions/scope-identity-transact-sql"),
        ["BP002"] = new("ISNUMERIC used", "`ISNUMERIC()` used — returns true for values like `$` and `1e2`; use `TRY_CONVERT`", false),
        ["BP003"] = new("No TRY/CATCH with DML", "No `TRY/CATCH` block in procedure with DML — unhandled errors leave transactions open", false),
        ["BP004"] = new("= NULL comparison", "`= NULL` comparison — always evaluates to `UNKNOWN`; use `IS NULL`", true, "https://learn.microsoft.com/sql/t-sql/language-elements/null-and-unknown-transact-sql"),
        ["BP005"] = new("EXEC string pattern", "`EXEC(string)` pattern — use `sp_executesql` with parameters to avoid SQL injection", false),
        ["BP006"] = new("DML without transaction", "Multiple DML statements without an explicit transaction — data may be partially committed", false),
        ["BP007"] = new("Empty CATCH block", "Empty `CATCH` block — swallows errors silently", false),
        ["BP008"] = new("Missing RETURN in procedure", "Missing `RETURN` at end of procedure — implicit `RETURN 0` is not obvious", true),
        ["BP009"] = new("Unread variable declaration", "Variable declared but never read", true),
        ["BP011"] = new("Missing SET XACT_ABORT ON", "`SET XACT_ABORT ON` missing in a procedure that uses transactions", true),
        ["BP012"] = new("Hard-coded date literal", "Hard-coded date literal (e.g. `'2024-01-01'`) — use a parameter or `GETDATE()`", false),
        ["BP013"] = new("Dynamic SQL concatenation", "Dynamic SQL built with string concatenation — use `sp_executesql` with parameters", false),
        ["BP014"] = new("INSERT without column list", "`INSERT` without an explicit column list — breaks if table schema changes", false),
        ["BP015"] = new("IF without BEGIN/END", "Single-statement `IF` without `BEGIN/END` — fragile if a statement is added later", true),
        ["BP017"] = new("GOTO usage", "`GOTO` usage — makes control flow hard to follow", false),
        ["BP018"] = new("Deep IF nesting", "`IF` nesting depth exceeds 3 — refactor into sub-procedures or CTEs", false),
        ["BP019"] = new("Magic numeric constant", "Magic numeric constant — consider a named variable or configuration table", false),
        ["BP020"] = new("OUTPUT param not always assigned", "`OUTPUT` parameter not assigned on all code paths", false),
        ["BP021"] = new("Single-row result as OUTPUT", "Procedure returns a single-row result set that could be `OUTPUT` parameters (schema required)", false),
        ["BP022"] = new("PRINT statement", "`PRINT` statement — remove before production deployment", false),
        ["BP023"] = new("OUTPUT param never assigned", "`OUTPUT` parameter declared but never assigned (schema required)", false),
        ["BP024"] = new("Parameter without default", "Parameter with no default value — callers must always supply it", false),
        ["BP025"] = new("Procedure body too long", "Procedure body exceeds 500 lines — consider splitting into sub-procedures", false),
        ["BP026"] = new("SELECT without FROM", "`SELECT` without `FROM` — use `SELECT @var = value` or a `VALUES` clause", false),
        ["BP027"] = new("UPDATE with FROM JOIN", "`UPDATE` with a `FROM` JOIN — non-standard; behavior differs from ISO SQL", false),
        ["BP028"] = new("DELETE with FROM JOIN", "`DELETE` with a `FROM` JOIN — non-standard extension", false),
        ["BP029"] = new("Scalar subquery in SELECT", "Scalar subquery in `SELECT` list — re-executes per row; consider a JOIN", false),
        ["BP030"] = new("Temp table without qualifier", "Temp table referenced without schema qualifier — use `#table` consistently", false),

        // ── Security (SE) ──
        ["SE001"] = new("EXEC concatenated string", "`EXEC()` with concatenated string — SQL injection vector", false),
        ["SE002"] = new("Hard-coded credential literal", "Hard-coded password or credential literal in SQL text", false),
        ["SE003"] = new("GRANT to PUBLIC role", "`GRANT` privilege to `PUBLIC` role — affects all users", false),
        ["SE004"] = new("EXECUTE AS OWNER", "`EXECUTE AS OWNER` — elevates to object owner's permissions; verify intent", false),
        ["SE005"] = new("TRUSTWORTHY ON", "`TRUSTWORTHY ON` for a database — allows CLR and ownership-chaining exploits", false, "https://learn.microsoft.com/sql/relational-databases/security/trustworthy-database-property"),
        ["SE006"] = new("Weak hash algorithm", "Weak hash algorithm (`MD5` or `SHA1`) — not collision-resistant", true),
        ["SE007"] = new("Cross-database object reference", "Cross-database object reference (`OtherDb.dbo.Table`) — increases attack surface", false),
        ["SE008"] = new("xp_cmdshell usage", "`xp_cmdshell` — executes OS commands; should be disabled in production", false, "https://learn.microsoft.com/sql/relational-databases/system-stored-procedures/xp-cmdshell-transact-sql"),
        ["SE009"] = new("OPENROWSET external data", "`OPENROWSET` — accesses external data sources; can exfiltrate data", false),
        ["SE010"] = new("Connection string with credentials", "Connection string containing credentials embedded in SQL", false),
        ["SE011"] = new("sa login used directly", "`sa` login used directly — use dedicated least-privilege accounts", false),
        ["SE012"] = new("Blank or empty password", "Blank or empty password detected", false),
        ["SE013"] = new("Overly broad permission grant", "Overly broad permission grant (e.g. `CONTROL SERVER`)", false),
        ["SE014"] = new("DDL inside procedure body", "DDL statement (`CREATE`/`ALTER`/`DROP`) inside a stored procedure body", false),
        ["SE015"] = new("Object accessed without permission check", "Object accessed without role-based permission check (schema required)", false),
        ["SE016"] = new("Sensitive column unencrypted", "Column name suggests sensitive data (SSN, credit card, etc.) without encryption", false),
        ["SE017"] = new("Row-level security bypass", "Row-level security policy may be bypassed by the current user context (schema required)", false),
        ["SE018"] = new("ENCRYPTBYPASSPHRASE used", "`ENCRYPTBYPASSPHRASE` — symmetric key encryption; prefer `ENCRYPTBYKEY` with AES-256", false),
        ["SE019"] = new("Connection string in procedure", "Connection string literal found in procedure body", false),
        ["SE020"] = new("Privilege escalation pattern", "Pattern suggests privilege escalation (e.g. adding a user to `sysadmin`)", false),

        // ── Style (ST) ──
        ["ST001"] = new("Inconsistent keyword casing", "Inconsistent keyword casing — enforce `UPPER`, `lower`, or `PascalCase`", false),
        ["ST002"] = new("Implicit alias without AS", "Old-style implicit alias (`col alias` without `AS`)", false),
        ["ST003"] = new("Old-style comma join", "Old-style comma join syntax (`FROM a, b WHERE a.id = b.id`) instead of explicit `JOIN`", false),
        ["ST004"] = new("Missing statement terminator", "Missing statement terminator (`;`)", false),
        ["ST005"] = new("Inconsistent alias naming", "Inconsistent alias naming convention within the same query", false),
        ["ST006"] = new("Unnecessary bracket quoting", "Unnecessary square-bracket quoting on a non-reserved identifier", false),
        ["ST007"] = new("Missing schema prefix", "Object reference missing schema prefix (`dbo.`)", false),
        ["ST008"] = new("Inconsistent indentation", "Inconsistent indentation detected", false),
        ["ST010"] = new("Line length exceeded", "Line length exceeds configured maximum (default 120 characters)", false),
        ["ST011"] = new("Multiple statements per line", "Multiple SQL statements on a single line", false),
        ["ST012"] = new("Inline alias without AS", "Table alias defined inline without `AS` keyword", false),
        ["ST013"] = new("Missing blank line between statements", "Missing blank line between top-level statements", false),
        ["ST014"] = new("Inconsistent comment style", "Comment style inconsistency (`--` vs `/* */`)", false),
        ["ST015"] = new("Data type keyword casing", "Data type keyword casing inconsistency", false),
        ["ST016"] = new("Function missing schema prefix", "Built-in function reference missing schema prefix (e.g. `dbo.fn_`)", false),
        ["ST017"] = new("Column list not aligned", "Column list items not aligned across clauses", false),
        ["ST018"] = new("TOP without parentheses", "`TOP` used without parentheses — `TOP 10` vs `TOP (10)`", false),
        ["ST019"] = new("ORDER BY ordinal position", "`ORDER BY` using ordinal position number instead of column name", false),
        ["ST020"] = new("DISTINCT over GROUP BY", "`SELECT DISTINCT` where `GROUP BY` would be clearer", false),
        ["ST021"] = new("Mixed quote styles", "Mixed single and double quotes for string literals", false),
        ["ST022"] = new("camelCase column alias", "Column alias uses camelCase — prefer PascalCase or consistent convention", false),
        ["ST023"] = new("Wildcard in object name", "Wildcard in object name pattern", false),
        ["ST024"] = new("Ambiguous date literal format", "Ambiguous date literal format (e.g. `'01/02/03'`) — use ISO 8601 (`'2024-01-02'`)", false),
        ["ST025"] = new("Excessive comment density", "Excessive comment density — more comments than code lines", false),

        // ── Design (DE) ──
        ["DE001"] = new("Table without PRIMARY KEY", "Table `CREATE` statement has no `PRIMARY KEY` constraint", false),
        ["DE002"] = new("Table without clustered index", "Table has no clustered index (schema required)", false),
        ["DE003"] = new("Nullable column in PRIMARY KEY", "Nullable column included in a `PRIMARY KEY` constraint", false),
        ["DE004"] = new("Short VARCHAR over CHAR", "`VARCHAR(1)` or `VARCHAR(2)` — consider `CHAR(n)` for fixed-length values", false),
        ["DE005"] = new("FLOAT for monetary data", "`FLOAT` or `REAL` used for monetary/financial data — use `DECIMAL`/`MONEY`", false),
        ["DE006"] = new("SQL_VARIANT column", "`SQL_VARIANT` column — poor for indexing and type safety", false),
        ["DE007"] = new("IDENTITY on non-integer column", "`IDENTITY` on a non-integer column type — unexpected behavior", false),

        // ── Deprecated (DEP) ──
        ["DEP001"] = new("text/ntext/image data type", "`text`, `ntext`, or `image` data type — removed in SQL Server 2022+", true, "https://learn.microsoft.com/sql/t-sql/data-types/ntext-text-and-image-transact-sql"),
        ["DEP002"] = new("Deprecated system procedure", "Deprecated system stored procedure (e.g. `sp_addtype`, `sp_bindrule`)", false),
        ["DEP003"] = new("SET FMTONLY ON", "`SET FMTONLY ON` — removed in SQL Server 2012", false),
        ["DEP004"] = new("Old outer-join operators", "Old outer-join operators (`*=`, `=*`) — removed in SQL Server 2012", false),
        ["DEP005"] = new("RAISERROR style 0", "`RAISERROR` with style 0 and without `NOWAIT` — use `THROW` instead", false),
        ["DEP006"] = new("Numbered procedure suffix", "Numbered procedure suffix (`;1`) — deprecated and ignored by the engine", false),
        ["DEP007"] = new("GROUP BY ALL", "`GROUP BY ALL` — removed in SQL Server 2012", false),
        ["DEP008"] = new("Locking hint without WITH", "Old-style locking hint without `WITH` (e.g. `(NOLOCK)` vs `WITH (NOLOCK)`)", false),

        // ── Execution (EX) ──
        ["EX001"] = new("Division by literal zero", "Division by literal zero (`/ 0`)", false),
        ["EX002"] = new("Potential data truncation", "Potential data truncation — inserting a longer value into a narrower column (schema required)", false),
        ["EX003"] = new("Ambiguous column reference", "Ambiguous column reference — same column name exists in multiple joined tables (schema required)", false),
        ["EX004"] = new("Unreachable code", "Unreachable code after `RETURN` or `THROW`", false),
        ["EX005"] = new("Identical branch condition", "Identical condition in `IF`/`CASE` branches — one branch is dead code", false),
        ["EX006"] = new("Always-true condition", "Always-true condition (`1=1`, `0=0`) — likely a copy-paste artifact", false),

        // ── Naming (NM) ──
        ["NM001"] = new("Reserved word as identifier", "Reserved word used as an identifier without quoting", false),
        ["NM002"] = new("Procedure name starts with sp_", "Procedure name starts with `sp_` — SQL Server searches `master` first", true),
        ["NM003"] = new("Hungarian notation on object", "Hungarian notation on table or view (`tbl_`, `vw_`)", false),
        ["NM004"] = new("Inconsistent naming style", "Inconsistent naming style across objects of the same type", false),
        ["NM005"] = new("Special characters in identifier", "Special characters in unquoted identifier", false),
        ["NM006"] = new("Single-letter table alias", "Single-letter table alias (`a`, `b`) — prefer descriptive aliases", false),
    };
}
