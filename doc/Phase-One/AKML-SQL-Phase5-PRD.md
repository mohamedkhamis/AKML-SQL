# AKML SQL — Phase 5: Static Code Analysis

> **Version:** 1.0 | **Date:** March 2026 | **Author:** Mohamed Khamis
> **Status:** Ready for Implementation | **Classification:** Confidential
> **Depends on:** Phase 4 (Snippet Manager) — snippet-based fix suggestions
> **Branch prefix:** `005-code-analysis`

---

## 1. Executive Summary

Phase 5 delivers the Static Code Analysis engine — a real-time system that scans your SQL as you type and highlights performance problems, security vulnerabilities, deprecated syntax, naming violations, design flaws, and best practice deviations. Think of it as a SQL linter that runs continuously in the background, underlining problematic code with colored squiggles and offering one-click fixes.

SQL Prompt ships with approximately 60 rules organized into categories (Best Practices, Performance, Deprecated, Style, Execution, Design). dbForge SQL Complete offers 180+ rules. AKML SQL targets **200+ rules** across 8 categories, with a unique differentiator: **auto-fix actions** — not just warnings, but one-click code transformations that resolve the issue in place.

### Why This Must Exist Before AI (Phase 9)

Static code analysis is the deterministic foundation that Phase 9's AI builds upon. AI suggestions are probabilistic — they might suggest an optimization that doesn't apply. Static analysis rules are provable — if rule PE001 fires, the code *definitely* uses `SELECT *` in a stored procedure. The trust hierarchy is: static analysis catches the definite problems, AI catches the subtle ones.

---

## 2. Document Metadata

| Field | Value |
|---|---|
| **Phase** | Phase 5 — Static Code Analysis |
| **Depends on** | Phase 2 (T-SQL parser, AST, schema cache), Phase 3 (formatter for auto-fixes) |
| **Target** | All SSMS + VS targets |
| **Performance Target** | Analyze 1,000-line script in < 200ms |
| **Benchmark** | SQL Prompt (~60 rules) + dbForge (180+ rules) combined |

---

## 3. Architecture Overview

### 3.1 Analysis Pipeline

```
┌──────────────────────────────────────────────────────────┐
│  IntelliSense Engine (out-of-proc)                        │
│                                                          │
│  Editor Text ──► T-SQL Parser ──► AST ──► Rule Engine    │
│                    (Phase 2)              │               │
│                                          ▼               │
│                                   ┌────────────┐         │
│  Schema Cache ──────────────────►│ 200+ Rules │         │
│  (Phase 2)                       │ (parallel)  │         │
│                                   └─────┬──────┘         │
│                                         ▼                │
│                                  ┌────────────┐          │
│                                  │ Diagnostics│          │
│                                  │ (warnings) │          │
│                                  └─────┬──────┘          │
│                                        ▼                 │
│                                 ┌─────────────┐          │
│                                 │ Fix Provider│          │
│                                 │ (lightbulbs)│          │
│                                 └─────────────┘          │
└──────────────────────────────────────────────────────────┘
```

### 3.2 Rule Execution Model

- Rules execute **incrementally** — when a single statement changes, only rules applicable to that statement re-run
- Rules execute in **parallel** — up to 8 rules evaluate the same AST concurrently
- Rules are **stateless** — each rule receives the AST + schema cache and returns diagnostics (no shared state)
- Rules have **severity levels**: Error, Warning, Information, Hint (configurable per rule)
- Each diagnostic can have **zero or more auto-fix actions** attached

---

## 4. Rule Categories & Complete Rule List

### 4.1 Category: Performance (PE) — 35 Rules

Rules that detect code patterns known to cause query performance problems.

| Rule ID | Name | Default Severity | Auto-Fix | Description |
|---|---|---|---|---|
| PE001 | Avoid SELECT * | Warning | ✔ | Replace `SELECT *` with explicit column list (requires schema cache) |
| PE002 | Unqualified object name | Warning | ✔ | Add schema prefix to unqualified table/view references |
| PE003 | Missing WHERE on DELETE/UPDATE | Error | — | DELETE or UPDATE without WHERE clause affects all rows |
| PE004 | LIKE with leading wildcard | Warning | — | `LIKE '%value'` prevents index usage |
| PE005 | Implicit column conversion | Warning | — | Comparing columns of different types causes implicit conversion |
| PE006 | Function on indexed column in WHERE | Warning | — | `WHERE YEAR(DateCol) = 2026` prevents index seek |
| PE007 | Cursor usage detected | Information | — | Cursors are often slower than set-based alternatives |
| PE008 | NOLOCK hint usage | Information | — | NOLOCK can cause dirty reads; verify intentional use |
| PE009 | Missing SET NOCOUNT ON | Warning | ✔ | Stored procedures should begin with SET NOCOUNT ON |
| PE010 | SELECT * in EXISTS | Warning | ✔ | Replace `EXISTS (SELECT * ...)` with `EXISTS (SELECT 1 ...)` |
| PE011 | ORDER BY in INSERT | Warning | ✔ | ORDER BY in INSERT INTO is meaningless and wastes CPU |
| PE012 | SET options causing recompilation | Warning | — | SET ANSI_NULLS etc. inside procs cause recompilation |
| PE013 | Scalar function in WHERE | Warning | — | Scalar UDFs in WHERE prevent parallelism |
| PE014 | Missing index on FK column | Information | — | Foreign key columns without indexes slow JOINs |
| PE015 | Large IN list | Warning | — | IN list with > 100 values; consider temp table or table variable |
| PE016 | Correlated subquery | Information | — | Correlated subqueries execute per-row; consider JOIN |
| PE017 | Non-SARGable expression | Warning | — | Expression prevents index seek (e.g., `ISNULL(col, 0) = 0`) |
| PE018 | Table variable for large datasets | Warning | — | Table variables don't have statistics; use temp tables for > 100 rows |
| PE019 | Missing clustered index | Warning | — | Heap tables without clustered index (requires schema cache) |
| PE020 | Unused index detected | Information | — | Index with zero reads but non-zero writes (requires DMV access) |
| PE021–PE035 | (Additional 15 performance rules) | Various | Mixed | Covering: DISTINCT misuse, UNION vs UNION ALL, excessive nesting, missing TOP, unnecessary GROUP BY, etc. |

### 4.2 Category: Best Practices (BP) — 30 Rules

| Rule ID | Name | Default Severity | Auto-Fix | Description |
|---|---|---|---|---|
| BP001 | Use SCOPE_IDENTITY() | Warning | ✔ | Replace @@IDENTITY with SCOPE_IDENTITY() |
| BP002 | Use TRY_CONVERT/TRY_CAST | Warning | ✔ | Replace IsNumeric() with TRY_CONVERT (SQL 2012+) |
| BP003 | Missing error handling | Warning | — | Stored procedures without TRY/CATCH |
| BP004 | Comparison with NULL | Error | ✔ | Replace `= NULL` with `IS NULL` |
| BP005 | EXEC(string) detected | Warning | — | Use sp_executesql instead of EXEC(string) |
| BP006 | Missing transaction in multi-statement proc | Information | — | Multi-statement modifications should be wrapped in transaction |
| BP007 | Empty CATCH block | Warning | — | CATCH block with no error handling |
| BP008 | Missing RETURN in procedure | Information | ✔ | Procedures should explicitly RETURN 0 on success |
| BP009 | Variable declared but never used | Warning | ✔ | Remove unused variable declarations |
| BP010 | Variable assigned but never read | Warning | — | Variable assigned a value that is never referenced |
| BP011 | Missing SET XACT_ABORT ON | Information | ✔ | Recommended for procedures with transactions |
| BP012 | Hard-coded date values | Information | — | Date literals that may need updating |
| BP013 | Non-parameterized dynamic SQL | Error | — | Dynamic SQL constructed by string concatenation |
| BP014 | Missing column list in INSERT | Warning | ✔ | `INSERT INTO table VALUES (...)` without column list |
| BP015 | Use BEGIN/END with IF | Information | ✔ | Single-statement IF should use BEGIN/END for clarity |
| BP016–BP030 | (Additional 15 BP rules) | Various | Mixed | Covering: GOTO usage, nested IF depth, magic numbers, output parameter not set, etc. |

### 4.3 Category: Security (SE) — 20 Rules

| Rule ID | Name | Default Severity | Auto-Fix | Description |
|---|---|---|---|---|
| SE001 | SQL injection risk | Error | — | Dynamic SQL with unsanitized input |
| SE002 | Hardcoded password/secret | Error | — | String literals assigned to variables named password, secret, key, token |
| SE003 | GRANT to public | Warning | — | Permissions granted to public role |
| SE004 | WITH EXECUTE AS OWNER | Warning | — | Procedure with elevated execution context |
| SE005 | TRUSTWORTHY database | Warning | — | Database set as TRUSTWORTHY |
| SE006 | Weak hash algorithm | Warning | ✔ | HASHBYTES with MD5/SHA1; suggest SHA2_256/SHA2_512 |
| SE007 | Cross-database ownership chaining | Warning | — | Potential security boundary violation |
| SE008 | xp_cmdshell usage | Error | — | Shell command execution from T-SQL |
| SE009 | OPENROWSET/OPENDATASOURCE | Warning | — | Ad-hoc distributed queries |
| SE010 | Unencrypted connection string | Warning | — | Missing `Encrypt=True` in connection string literals |
| SE011–SE020 | (Additional 10 security rules) | Various | Mixed | Covering: sa login usage, blank passwords, certificate expiry, etc. |

### 4.4 Category: Style (ST) — 25 Rules

| Rule ID | Name | Default Severity | Auto-Fix | Description |
|---|---|---|---|---|
| ST001 | Inconsistent keyword casing | Hint | ✔ | Keywords not matching the configured casing preference |
| ST002 | Old-style column alias (=) | Warning | ✔ | Replace `alias = expression` with `expression AS alias` |
| ST003 | Old-style JOIN syntax | Warning | ✔ | Replace comma-separated joins with ANSI JOIN syntax |
| ST004 | Missing semicolon terminator | Information | ✔ | Statement not terminated with semicolon |
| ST005 | Inconsistent alias style | Hint | ✔ | Mix of `AS alias` and bare alias in same query |
| ST006 | Unnecessary square brackets | Hint | ✔ | Square brackets on identifiers that don't need them |
| ST007 | Missing schema prefix | Hint | ✔ | Object references without schema qualification |
| ST008 | Inconsistent indentation | Hint | ✔ | Mixed tabs/spaces or irregular indentation |
| ST009–ST025 | (Additional 17 style rules) | Various | All ✔ | Covering: naming conventions, comment style, white space consistency, etc. |

### 4.5 Category: Deprecated (DEP) — 20 Rules

| Rule ID | Name | Default Severity | Auto-Fix | Description |
|---|---|---|---|---|
| DEP001 | Deprecated data type | Warning | ✔ | `text` → `varchar(max)`, `image` → `varbinary(max)`, `ntext` → `nvarchar(max)` |
| DEP002 | Deprecated system procedure | Warning | — | `sp_addlogin` → `CREATE LOGIN`, etc. |
| DEP003 | Deprecated SET option | Warning | — | `SET FMTONLY ON` → `sp_describe_first_result_set` |
| DEP004 | Deprecated JOIN syntax | Warning | ✔ | `*=` and `=*` old-style outer join |
| DEP005 | RAISERROR old syntax | Warning | ✔ | `RAISERROR 50001 'msg'` → `RAISERROR('msg', 16, 1)` |
| DEP006 | Numbered procedures | Warning | — | `CREATE PROCEDURE proc;1` syntax |
| DEP007 | GROUP BY ALL | Warning | — | Deprecated GROUP BY ALL syntax |
| DEP008 | Deprecated hint syntax | Warning | ✔ | `FROM table (INDEX = idx)` → `FROM table WITH (INDEX(idx))` |
| DEP009–DEP020 | (Additional 12 rules) | Various | Mixed | Covering: COMPUTE, WRITETEXT, legacy backup syntax, etc. |

### 4.6 Category: Design (DE) — 25 Rules

| Rule ID | Name | Default Severity | Auto-Fix | Description |
|---|---|---|---|---|
| DE001 | Missing primary key | Warning | — | Table without primary key constraint |
| DE002 | Missing clustered index | Warning | — | Table without clustered index (heap) |
| DE003 | Nullable column in primary key | Error | — | PK column allows NULL |
| DE004 | VARCHAR(1) or VARCHAR(2) | Warning | — | Very short variable-length columns; use CHAR instead |
| DE005 | Float/real for financial data | Warning | — | Use DECIMAL/NUMERIC for money, not float |
| DE006 | SQL_VARIANT usage | Warning | — | SQL_VARIANT loses type safety |
| DE007 | IDENTITY on non-integer | Warning | — | IDENTITY on decimal/numeric column |
| DE008 | Table without description | Information | — | Extended property 'MS_Description' missing |
| DE009–DE025 | (Additional 17 rules) | Various | Mixed | Covering: table naming, excessive columns, circular FKs, trigger complexity, etc. |

### 4.7 Category: Execution (EX) — 20 Rules

| Rule ID | Name | Default Severity | Auto-Fix | Description |
|---|---|---|---|---|
| EX001 | Division by zero risk | Warning | ✔ | Division without checking for zero; wrap with NULLIF |
| EX002 | Potential data truncation | Warning | — | Assigning larger type to smaller without explicit CAST |
| EX003 | Ambiguous column reference | Error | — | Column name exists in multiple tables without alias qualification |
| EX004 | Unreachable code | Information | ✔ | Code after unconditional RETURN or GOTO |
| EX005 | Identical branch conditions | Warning | — | IF and ELSE IF with same condition |
| EX006 | Always-true/false condition | Warning | — | `WHERE 1=1 AND ...` (sometimes intentional for dynamic SQL) |
| EX007–EX020 | (Additional 14 rules) | Various | Mixed | Covering: overflow risks, timezone issues, collation conflicts, etc. |

### 4.8 Category: Naming (NM) — 25 Rules

| Rule ID | Name | Default Severity | Auto-Fix | Description |
|---|---|---|---|---|
| NM001 | Reserved word as identifier | Warning | — | Table/column name is a T-SQL reserved word |
| NM002 | sp_ prefix on user procedure | Warning | ✔ | Rename to remove `sp_` prefix (reserved for system procs) |
| NM003 | Hungarian notation | Information | — | `tblCustomers`, `vwOrders` prefixes |
| NM004 | Inconsistent naming convention | Information | — | Mix of PascalCase, camelCase, snake_case in same database |
| NM005 | Special characters in names | Warning | — | Spaces, hyphens, or other special chars in identifiers |
| NM006 | Single-letter alias | Information | — | Non-descriptive alias (configurable; some teams prefer short aliases) |
| NM007–NM025 | (Additional 19 rules) | Various | Mixed | Covering: length limits, prefix/suffix patterns, abbreviation consistency, etc. |

---

## 5. Auto-Fix System

### 5.1 Fix Actions

When a rule fires, it can provide one or more fix actions displayed as lightbulb suggestions:

```
┌─ SELECT * FROM dbo.Orders                    ─┐
│  ~~~~~~                                        │
│  ⚠ PE001: Avoid SELECT * in stored procedures  │
│                                                │
│  💡 Expand to explicit column list             │
│  💡 Suppress PE001 for this line               │
│  💡 Suppress PE001 for this file               │
│  💡 Disable PE001 globally                     │
└────────────────────────────────────────────────┘
```

### 5.2 Fix Types

| Type | Description |
|---|---|
| **Transform** | Replace problematic code with corrected version |
| **Insert** | Add missing code (e.g., add SET NOCOUNT ON at procedure start) |
| **Remove** | Remove unnecessary code (e.g., unused variables) |
| **Suppress** | Add `-- noqa: PE001` comment to suppress the rule for this line |
| **Batch fix** | Apply the same fix to all occurrences in the file |

### 5.3 Suppression System

```sql
-- Suppress a specific rule for the next line
-- noqa: PE001
SELECT * FROM dbo.Orders;

-- Suppress multiple rules
-- noqa: PE001, ST004
SELECT * FROM dbo.Orders

-- Suppress all rules for a block
-- noqa-begin
SELECT * FROM dbo.Orders
SELECT * FROM dbo.Customers
-- noqa-end
```

---

## 6. Analysis Settings (CAsettings)

### 6.1 Settings File Format

```json
{
  "metadata": { "name": "Team Standard", "version": "1.0" },
  "rules": {
    "PE001": { "severity": "warning", "enabled": true },
    "PE002": { "severity": "error", "enabled": true },
    "PE008": { "severity": "ignore", "enabled": false },
    "ST002": { "severity": "warning", "enabled": true },
    "NM006": { "severity": "ignore", "enabled": false }
  },
  "globalSuppressions": [
    { "rule": "PE007", "reason": "Cursor usage acceptable in ETL procedures" }
  ]
}
```

### 6.2 Settings Operations

| Setting | Default | Description |
|---|---|---|
| `analysis.enabled` | `true` | Master switch |
| `analysis.runOnType` | `true` | Analyze as you type (real-time) |
| `analysis.runOnSave` | `true` | Analyze on file save |
| `analysis.squiggleStyle` | `underline` | `underline`, `highlight`, `gutter` |
| `analysis.showInErrorList` | `true` | Show in VS/SSMS Error List panel |
| `analysis.settingsFile` | (default CAsettings) | Path to CAsettings JSON file |
| `analysis.autoFixOnFormat` | `false` | Auto-apply safe fixes when running Format SQL |

---

## 7. Bulk Analysis & Reporting

### 7.1 Bulk Analysis (IDE)

AKML SQL menu → Run Code Analysis → Options:

- Analyze current file
- Analyze all open files
- Analyze directory (recursive)
- Analyze database project

### 7.2 CLI Tool

```bash
# Analyze a single file
akmlsql-analyze.exe --file "query.sql"

# Analyze directory with report
akmlsql-analyze.exe --directory "scripts/" --recursive --report "analysis.json"

# Check mode for CI/CD (exit code 1 if errors found)
akmlsql-analyze.exe --directory "scripts/" --check --severity error

# Use custom settings
akmlsql-analyze.exe --file "query.sql" --settings "team-casettings.json"
```

### 7.3 Report Format

```json
{
  "timestamp": "2026-08-01T10:00:00Z",
  "settings": "Team Standard",
  "summary": {
    "filesAnalyzed": 234,
    "totalIssues": 512,
    "errors": 23,
    "warnings": 341,
    "information": 128,
    "hints": 20
  },
  "byCategory": {
    "Performance": 89,
    "BestPractices": 134,
    "Security": 12,
    "Style": 187,
    "Deprecated": 34,
    "Design": 28,
    "Execution": 15,
    "Naming": 13
  },
  "issues": [
    { "file": "dbo.GetOrders.sql", "line": 15, "column": 8, "rule": "PE001", "severity": "warning", "message": "Avoid SELECT * in stored procedures", "fix": "available" }
  ]
}
```

---

## 8. SQL Prompt CAsettings Import

Auto-detect SQL Prompt's CAsettings XML file and convert to AKML JSON format. Map rule IDs:

| SQL Prompt Rule | AKML SQL Rule | Category |
|---|---|---|
| BP001–BP018 | BP001–BP018 | Best Practices |
| PE001–PE013 | PE001–PE013 | Performance |
| DEP001–DEP010 | DEP001–DEP010 | Deprecated |
| ST001–ST011 | ST001–ST011 | Style |
| EX001–EX006 | EX001–EX006 | Execution |

---

## 9. Performance Requirements

| Metric | Target |
|---|---|
| Single-statement analysis | < 20ms |
| Full file analysis (1,000 lines) | < 200ms |
| Full file analysis (10,000 lines) | < 1 second |
| Auto-fix application | < 50ms per fix |
| Bulk analysis (100 files) | < 30 seconds |

---

## 10. Testing Requirements

| Area | Test Count |
|---|---|
| Performance rules (PE) | 70+ |
| Best practice rules (BP) | 60+ |
| Security rules (SE) | 40+ |
| Style rules (ST) | 50+ |
| Deprecated rules (DEP) | 40+ |
| Design rules (DE) | 50+ |
| Execution rules (EX) | 40+ |
| Naming rules (NM) | 50+ |
| Auto-fix transformations | 100+ |
| False positive tests | 80+ |
| Suppression system | 20+ |
| CLI & bulk analysis | 25+ |

---

## 11. Competitive Comparison

| Feature | SQL Prompt | dbForge | AKML SQL Phase 5 |
|---|---|---|---|
| Total rules | ~60 | 180+ | **200+** |
| Rule categories | 5 (BP, PE, DEP, ST, EX) | Multiple | **8 (BP, PE, SE, ST, DEP, DE, EX, NM)** |
| Real-time analysis | ✔ | ✔ | ✔ |
| Auto-fix actions | Partial | No | **✔ (100+ rules with auto-fix)** |
| Suppression comments | No | No | **✔ (noqa syntax)** |
| Custom rules API | No | No | **✔ (plugin API, future)** |
| Bulk analysis CLI | ✔ (via SCA) | No | **✔ (standalone CLI)** |
| CI/CD integration | ✔ (via SCA) | No | **✔ (check mode + exit codes)** |
| CAsettings sharing | ✔ (file-based) | ✔ (file-based) | **✔ (file + AKML Platform future)** |
| SQL Prompt import | N/A | No | **✔** |
| Security rules | Limited | No | **✔ (20 dedicated rules)** |
| Design rules | Limited | Some | **✔ (25 dedicated rules)** |
| Naming rules | Limited | No | **✔ (25 dedicated rules)** |

---

## 12. Timeline & Milestones

| Week | Milestone | Deliverable |
|---|---|---|
| 1–2 | Rule engine & framework | Parallel rule execution, diagnostic model, severity system, AST visitor pattern |
| 3–4 | Performance & Best Practice rules | PE001–PE035, BP001–BP030 with auto-fixes |
| 5–6 | Security, Deprecated & Design rules | SE001–SE020, DEP001–DEP020, DE001–DE025 |
| 7–8 | Style, Execution & Naming rules | ST001–ST025, EX001–EX020, NM001–NM025 |
| 9 | Auto-fix system & suppression | Fix provider, lightbulb UI, noqa comments, batch fixes |
| 10 | Bulk analysis, CLI & QA | CLI tool, bulk wizard, CAsettings import, reporting, full test matrix |

**Total estimated duration: 10 weeks** (2.5 months).

---

## 13. Success Metrics

- **Rule count:** 200+ rules across 8 categories
- **Auto-fix coverage:** > 50% of rules have at least one auto-fix action
- **False positive rate:** < 5% across the full test corpus
- **Performance:** < 200ms for 1,000-line scripts
- **Adoption:** > 80% of users keep code analysis enabled
- **Phase 6 readiness:** Refactoring (Phase 6) leverages the analysis engine for safe transformations

---

*End of Phase 5 PRD — AKML SQL v1.0*
