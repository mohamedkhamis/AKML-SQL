# AKML SQL — Phase 2: Core IntelliSense Engine

> **Version:** 1.0 | **Date:** March 2026 | **Author:** Abdulrahman Khamis
> **Status:** Ready for Implementation | **Classification:** Confidential
> **Depends on:** Phase 1 (Foundation & Windows EXE Installer) — must be complete
> **Branch prefix:** `002-core-intellisense`

---

## 1. Executive Summary

Phase 2 delivers the core IntelliSense engine — the feature that makes AKML SQL useful on day one. This is the schema-aware autocomplete system that replaces SSMS's notoriously unreliable built-in IntelliSense with a fast, accurate, context-aware completion engine. This phase focuses exclusively on **traditional, non-AI autocomplete** — schema metadata, keyword completion, alias resolution, and JOIN assistance. AI-powered suggestions come in Phase 10.

The goal: every SQL developer who installs AKML SQL should immediately feel faster. Typing `SELECT o.` after `FROM dbo.Orders o` should instantly show all columns of the Orders table, ranked by usage frequency. This is the table-stakes feature that earns trust before AI features are introduced.

### Why This Must Be Rock-Solid

SSMS's built-in IntelliSense has been unreliable since SQL Server 2008. It frequently stops working, fails to refresh after schema changes, doesn't handle aliases well, and breaks entirely in SQLCMD mode. SQL Prompt's #1 selling point is simply "IntelliSense that actually works." AKML SQL must match or exceed SQL Prompt's completion accuracy and speed from the first release.

---

## 2. Document Metadata

| Field | Value |
|---|---|
| **Phase** | Phase 2 — Core IntelliSense Engine |
| **Depends on** | Phase 1 (Extension host, installer, menu shell) |
| **Target SSMS** | SSMS 20 (x86), SSMS 21 (x64), SSMS 22 (x64) |
| **Target Visual Studio** | VS 2019, VS 2022, VS 2026 (with SSDT) |
| **Target SQL Server** | SQL Server 2016, 2017, 2019, 2022, 2025 |
| **Target Cloud** | Azure SQL Database, Azure SQL Managed Instance |
| **.NET Version** | .NET Fx 4.7.2 (shell) + .NET 10/11 (out-of-proc engine) |
| **Performance Target** | Suggestions appear within 100ms of keystroke |
| **Benchmark** | Redgate SQL Prompt IntelliSense feature parity |

---

## 3. Architecture Overview

### 3.1 High-Level Architecture

The IntelliSense engine runs as an out-of-process service (.NET 10/11) to avoid blocking the SSMS UI thread. The in-process VSPackage shell (Phase 1) communicates with the engine via named pipes.

```
┌─────────────────────────────────────────────────────────┐
│  SSMS / Visual Studio (UI Thread)                       │
│  ┌───────────────────────────────────────────────────┐  │
│  │  AkmlSql.Ssms22 VSPackage (.NET Fx 4.7.2)        │  │
│  │  ┌──────────────┐  ┌──────────────────────────┐   │  │
│  │  │ Key Listener │  │ Completion UI (WPF popup)│   │  │
│  │  │ (editor hook)│  │ with filtering & ranking │   │  │
│  │  └──────┬───────┘  └────────────▲─────────────┘   │  │
│  │         │ keystroke              │ suggestions     │  │
│  └─────────┼────────────────────────┼────────────────┘  │
│            │ Named Pipe             │                    │
└────────────┼────────────────────────┼────────────────────┘
             │                        │
┌────────────▼────────────────────────┴────────────────────┐
│  AkmlSql.IntelliSense Engine (.NET 10/11, out-of-proc)  │
│  ┌──────────────┐  ┌──────────────┐  ┌───────────────┐  │
│  │ T-SQL Parser │  │ Schema Cache │  │ Completion    │  │
│  │ (incremental)│  │ (in-memory)  │  │ Provider      │  │
│  └──────┬───────┘  └──────┬───────┘  └───────┬───────┘  │
│         │                 │                   │          │
│  ┌──────▼─────────────────▼───────────────────▼───────┐  │
│  │              Schema Metadata Service                │  │
│  │  (reads sys.objects, sys.columns, sys.types, etc.)  │  │
│  └─────────────────────┬──────────────────────────────┘  │
│                        │ SQL Connection                   │
└────────────────────────┼─────────────────────────────────┘
                         │
              ┌──────────▼──────────┐
              │   SQL Server        │
              │   (any version)     │
              └─────────────────────┘
```

### 3.2 Why Out-of-Process?

- **Non-blocking UI:** Heavy schema queries and parsing never freeze SSMS
- **Crash isolation:** If the engine crashes, SSMS continues normally (Phase 1 zero-crash guarantee preserved)
- **Independent .NET runtime:** Engine runs on .NET 10/11 while the shell stays on .NET Fx 4.7.2
- **Memory isolation:** Schema cache can grow large (thousands of objects) without competing with SSMS's own memory
- **Future AI integration:** Phase 10's AI engine plugs directly into this same out-of-process service

### 3.3 Communication Protocol

Named pipes with MessagePack serialization for low-latency, binary-efficient messaging between the VSPackage and the IntelliSense engine. Protocol messages:

| Message | Direction | Purpose |
|---|---|---|
| `ConnectionChanged` | Shell → Engine | New database connection established |
| `DocumentChanged` | Shell → Engine | Editor content changed (incremental diff) |
| `RequestCompletion` | Shell → Engine | User triggered completion (keystroke or Ctrl+Space) |
| `CompletionResult` | Engine → Shell | Ranked list of suggestions |
| `RequestSignatureHelp` | Shell → Engine | Function/procedure parameter info |
| `SignatureHelpResult` | Engine → Shell | Parameter signatures |
| `RequestQuickInfo` | Shell → Engine | Hover tooltip for an identifier |
| `QuickInfoResult` | Engine → Shell | Object metadata for tooltip |
| `SchemaRefreshRequest` | Shell → Engine | Manual cache refresh (Ctrl+Shift+R) |
| `SchemaRefreshComplete` | Engine → Shell | Cache refresh done |
| `EngineStatus` | Engine → Shell | Heartbeat, cache stats, errors |

---

## 4. Schema Metadata Service

### 4.1 What We Cache

The engine maintains an in-memory schema cache per database connection. The cache is populated on first connection and refreshed incrementally.

| Metadata | Source | Cache Strategy |
|---|---|---|
| **Databases** | `sys.databases` | Full list, refreshed on connection change |
| **Schemas** | `sys.schemas` | Per-database, cached until manual refresh |
| **Tables & Views** | `sys.objects` (type U, V) | Per-database with row count estimates from `sys.partitions` |
| **Columns** | `sys.columns` + `sys.types` | Per-table, lazy-loaded on first reference |
| **Indexes** | `sys.indexes` + `sys.index_columns` | Per-table, lazy-loaded |
| **Foreign Keys** | `sys.foreign_keys` + `sys.foreign_key_columns` | Per-database, critical for JOIN suggestions |
| **Stored Procedures** | `sys.procedures` | Per-database |
| **Functions** | `sys.objects` (type FN, IF, TF, AF) | Per-database |
| **Parameters** | `sys.parameters` | Per-procedure/function, lazy-loaded |
| **User-Defined Types** | `sys.types` (is_user_defined = 1) | Per-database |
| **Synonyms** | `sys.synonyms` | Per-database |
| **Sequences** | `sys.sequences` | Per-database |
| **T-SQL Keywords** | Built-in dictionary | Static, version-aware (SQL Server 2016–2025) |
| **Built-in Functions** | Built-in dictionary | Static, version-aware |
| **System Stored Procs** | Built-in dictionary | `sp_`, `xp_`, `fn_` prefixed |
| **DMVs** | Built-in dictionary | `sys.dm_*` |

### 4.2 Cache Population Strategy

```
Connection Established
  │
  ├─► Phase A (immediate, <500ms): Databases, Schemas, Tables, Views (names only)
  │   → Enables basic object completion immediately
  │
  ├─► Phase B (background, 1-5s): Columns for recently-used tables, Foreign Keys
  │   → Enables column completion and JOIN assistance
  │
  └─► Phase C (lazy, on-demand): Columns for unused tables, Parameters, Indexes
      → Loaded when user first references the object
```

### 4.3 Cache Invalidation

| Trigger | Action |
|---|---|
| Manual refresh (Ctrl+Shift+R or menu) | Full cache rebuild for current database |
| DDL detected in query execution | Incremental refresh of affected objects |
| Connection changed (different database) | Load cache for new database (or reuse if cached) |
| Timer (configurable, default 5 minutes) | Background check for schema changes via `sys.objects.modify_date` |
| Cross-database query detected | Lazy-load referenced database metadata |

### 4.4 Permissions Handling

The engine gracefully degrades when permissions are limited:

- **Full permissions (db_datareader or higher):** Complete schema cache
- **Limited permissions:** Cache only objects the user can see via `HAS_PERMS_BY_NAME`
- **No sys catalog access:** Fall back to `INFORMATION_SCHEMA` views (slower, less complete)
- **Connection failure:** Show cached data from last successful connection (with "stale cache" indicator)

---

## 5. T-SQL Parser

### 5.1 Incremental Parsing

The engine maintains a persistent parse tree of the current editor document. On each keystroke, only the changed region is re-parsed (incremental parsing), not the entire document. This is critical for large scripts (10,000+ lines).

### 5.2 Parser Capabilities

| Capability | Description |
|---|---|
| **Statement boundaries** | Identify individual SQL statements separated by `GO` batches |
| **Clause detection** | Know whether the cursor is in SELECT, FROM, WHERE, JOIN, GROUP BY, HAVING, ORDER BY, INSERT, UPDATE, DELETE, CREATE, ALTER, etc. |
| **Alias resolution** | Track table aliases defined in FROM/JOIN clauses and resolve them for column completion |
| **Subquery context** | Maintain separate scope for subqueries, CTEs, and derived tables |
| **CTE resolution** | Parse WITH clauses and make CTE columns available for completion |
| **Temp table tracking** | Track #temp and ##temp table definitions within the current batch |
| **Variable tracking** | Track @variable declarations and their types |
| **Cursor position context** | Determine what type of completion is appropriate at the current cursor position |
| **Error recovery** | Continue parsing after syntax errors (don't stop at the first error) |
| **Comment/string awareness** | Don't trigger completion inside comments or string literals |
| **SQLCMD mode detection** | Detect `:setvar`, `:connect` directives and handle accordingly |

### 5.3 Parser Technology

Use Microsoft's `Microsoft.SqlServer.TransactSql.ScriptDom` library (official T-SQL parser) as the foundation, wrapped with incremental re-parsing logic. ScriptDom provides:

- Full T-SQL grammar coverage (SQL Server 2016–2025)
- AST (Abstract Syntax Tree) generation
- Visitor pattern for tree traversal
- Token stream for fine-grained position mapping

Augmented with a lightweight custom tokenizer for sub-200ms incremental updates on large documents.

---

## 6. Completion Providers

Each completion provider handles a specific context. The engine routes completion requests to the appropriate provider(s) based on the parser's cursor context analysis.

### 6.1 Keyword Completion Provider

**Trigger:** Any context where a T-SQL keyword is expected.

**Behavior:**
- Context-aware keyword suggestions (e.g., after `SELECT` suggest `TOP`, `DISTINCT`, column-start keywords; after `FROM` suggest `JOIN`, `WHERE`, `CROSS`, etc.)
- Version-aware: Only suggest keywords available in the connected SQL Server version
- Case matching: Follow the user's casing preference (UPPER, lower, PascalCase)

**Examples:**
```sql
SEL|        → SELECT, SET (ranked by frequency)
SELECT * FR| → FROM
FROM dbo.Orders o WH| → WHERE, WITH (NOLOCK)
```

### 6.2 Database Object Completion Provider

**Trigger:** Cursor after a dot (`.`) or at a position where an object name is expected.

**Behavior:**
- `schema.` → List tables, views, functions, procedures in that schema
- `database.` → List schemas in that database
- `server.` → List databases (linked server context)
- Unqualified position → List all objects in current schema + dbo, ranked by usage frequency
- Show object type icons (table, view, function, procedure, synonym)

**Examples:**
```sql
FROM dbo.|           → Orders, Customers, Products, ... (tables and views)
SELECT * FROM |      → dbo.Orders, dbo.Customers, ... (all objects)
EXEC |               → dbo.sp_GetCustomer, sys.sp_help, ... (procedures)
```

### 6.3 Column Completion Provider

**Trigger:** Cursor after `table_alias.` or `table_name.` in a SELECT, WHERE, JOIN ON, GROUP BY, ORDER BY, UPDATE SET, or INSERT column list context.

**Behavior:**
- Resolve alias to table → list columns with data types
- In SELECT with multiple tables: show columns from all referenced tables, grouped by table
- Show column data type as secondary text
- Show nullability indicator
- Show computed column indicator
- Show primary key / foreign key badges

**Examples:**
```sql
SELECT o.|  (where o = dbo.Orders)
  → OrderID (int, PK), CustomerID (int, FK), OrderDate (datetime), ...

SELECT | FROM dbo.Orders o JOIN dbo.Customers c ON ...
  → o.OrderID, o.CustomerID, o.OrderDate, c.CustomerID, c.CompanyName, ...
```

### 6.4 Alias Completion Provider

**Trigger:** After typing a table reference in FROM or JOIN, suggest an alias.

**Behavior:**
- Auto-suggest alias based on table name abbreviation rules:
  - `dbo.Orders` → `o`
  - `dbo.OrderDetails` → `od`
  - `dbo.CustomerAddresses` → `ca`
  - `schema.TableName` → first letters of PascalCase parts
- Configurable alias generation rules
- Detect existing aliases in the query and avoid conflicts

**Examples:**
```sql
FROM dbo.Orders |    → suggest alias "o"
JOIN dbo.OrderDetails |  → suggest alias "od"
```

### 6.5 JOIN Completion Provider

**Trigger:** After typing `JOIN` keyword.

**Behavior:**
- Suggest tables that have foreign key relationships with already-referenced tables
- Auto-generate the `ON` clause based on FK relationships
- Rank by FK relevance (direct FK first, then indirect)
- Support INNER, LEFT, RIGHT, FULL, CROSS join types

**Examples:**
```sql
FROM dbo.Orders o
JOIN |
  → dbo.OrderDetails od ON od.OrderID = o.OrderID  (FK relationship)
  → dbo.Customers c ON c.CustomerID = o.CustomerID  (FK relationship)
  → dbo.Employees e ON e.EmployeeID = o.EmployeeID  (FK relationship)
```

### 6.6 Snippet Trigger Provider

**Trigger:** Typing a snippet shortcode.

**Behavior:**
- Phase 2 includes only a **basic** built-in snippet set (full Snippet Manager is Phase 4)
- Common patterns: `ssf` → `SELECT * FROM`, `sel` → `SELECT`, `ins` → `INSERT INTO ... VALUES`, `upd` → `UPDATE ... SET ... WHERE`, `del` → `DELETE FROM ... WHERE`, `cte` → `WITH cte AS (...) SELECT ...`
- Tab-stop navigation within expanded snippets

### 6.7 Function Signature Provider

**Trigger:** Typing `(` after a function or procedure name.

**Behavior:**
- Show parameter list with names, types, and optional/default indicators
- Highlight the current parameter as the user types
- Support overloaded functions (e.g., `CONVERT` has multiple signatures)
- Built-in function signatures for all T-SQL functions (static dictionary)
- User-defined function/procedure signatures from schema cache

**Examples:**
```sql
CONVERT(|
  → CONVERT(data_type, expression [, style])
  Parameter 1: data_type (sysname) — Target data type
  Parameter 2: expression (sql_variant) — Value to convert
  Parameter 3: style (int, optional) — Date/time format style
```

### 6.8 Quick Info Provider

**Trigger:** Hovering over an identifier or pressing Ctrl+K, Ctrl+I.

**Behavior:**
- Tables/Views: Show schema, row count estimate, column count, description (if extended properties exist)
- Columns: Show data type, nullability, default value, description
- Procedures/Functions: Show parameter list, return type, description
- Variables: Show declared type and assigned value (if detectable)
- Keywords: Show brief syntax help

---

## 7. Completion UI

### 7.1 Completion Popup

A WPF popup window that appears near the cursor position, showing:

```
┌────────────────────────────────────────────┐
│ 🔍 [filter text]                           │
├────────────────────────────────────────────┤
│ 📊 OrderID          int (PK)         ▲    │
│ 👤 CustomerID       int (FK → Customers) │ │
│ 📅 OrderDate        datetime              │
│ 💰 TotalAmount      decimal(18,2)         │
│ 📝 Notes            nvarchar(max) NULL    │
│ ✅ IsActive          bit                   │
│ 🕐 CreatedDate      datetime2        ▼    │
├────────────────────────────────────────────┤
│ dbo.Orders (6 of 12 columns)              │
└────────────────────────────────────────────┘
```

### 7.2 UI Features

| Feature | Description |
|---|---|
| **Fuzzy filtering** | Type `custid` to match `CustomerID`. Camel-case matching: `OD` matches `OrderDate`. |
| **Type icons** | Different icons for tables (📊), views (👁), columns, procedures (⚙), functions (fx), keywords (K), snippets ({}). |
| **Secondary text** | Data type, nullability, PK/FK badges shown on the right side. |
| **Status bar** | Bottom row shows source table name and match count. |
| **Keyboard navigation** | Up/Down arrows, Enter to accept, Tab to accept and move, Escape to dismiss. |
| **Mouse support** | Click to select, double-click to accept. |
| **Auto-sizing** | Width and height adjust to content. Max 12 items visible, scrollable. |
| **Theming** | Follows SSMS/VS theme (Light, Dark, Blue). |
| **Multi-monitor DPI** | Correct scaling on high-DPI and mixed-DPI setups. |
| **Quick dismiss** | Pressing space, semicolon, or typing a non-matching character dismisses the popup. |

### 7.3 Trigger Modes

| Mode | Trigger | Behavior |
|---|---|---|
| **Automatic** | Any letter or `.` typed | Popup appears after 100ms debounce (configurable) |
| **Manual** | Ctrl+Space or Ctrl+J | Popup appears immediately with full list |
| **After Dot** | `.` typed | Popup appears immediately (no debounce) |
| **Parameter** | `(` or `,` typed | Signature help appears |
| **Disabled** | User turns off auto-trigger | Only manual trigger works |

---

## 8. Configuration & Options

All settings are stored in `%AppData%\AKML SQL\config.json` and accessible via AKML SQL → Options in the IDE.

### 8.1 IntelliSense Settings

| Setting | Default | Description |
|---|---|---|
| `intellisense.enabled` | `true` | Master switch for IntelliSense |
| `intellisense.autoTrigger` | `true` | Auto-show suggestions on keystroke |
| `intellisense.triggerDelay` | `100` | Milliseconds to wait before showing (50-500) |
| `intellisense.afterDot` | `true` | Immediately trigger after `.` |
| `intellisense.maxSuggestions` | `50` | Maximum items in the popup |
| `intellisense.fuzzyMatch` | `true` | Enable fuzzy/camel-case matching |
| `intellisense.showDataTypes` | `true` | Show column data types in popup |
| `intellisense.showNullability` | `true` | Show NULL/NOT NULL indicators |
| `intellisense.showPkFk` | `true` | Show PK/FK badges |
| `intellisense.autoAlias` | `true` | Auto-suggest aliases after table names |
| `intellisense.joinAssist` | `true` | Auto-suggest FK-based JOINs |
| `intellisense.keywordCase` | `"UPPER"` | Keyword casing: UPPER, lower, PascalCase, AsIs |
| `intellisense.disableNativeIntelliSense` | `true` | Disable SSMS's built-in IntelliSense to avoid conflicts |

### 8.2 Schema Cache Settings

| Setting | Default | Description |
|---|---|---|
| `cache.autoRefresh` | `true` | Background schema refresh |
| `cache.refreshInterval` | `300` | Seconds between background refreshes (60-3600) |
| `cache.detectDDL` | `true` | Refresh after DDL execution |
| `cache.maxDatabases` | `10` | Max databases cached simultaneously |
| `cache.lazyLoadColumns` | `true` | Load columns on first reference only |
| `cache.persistToDisk` | `true` | Save cache to disk for faster startup |
| `cache.persistPath` | `%LocalAppData%\AKML SQL\cache\` | Cache persistence directory |

---

## 9. Handling SSMS Native IntelliSense Conflicts

### 9.1 The Problem

SSMS has its own built-in IntelliSense. Running two IntelliSense systems simultaneously creates chaos — double popups, conflicting suggestions, keystroke interception fights. SQL Prompt solves this by disabling SSMS's native IntelliSense during installation.

### 9.2 AKML SQL's Approach

On extension load, AKML SQL:

1. **Detects SSMS IntelliSense state** via `Tools → Options → Text Editor → Transact-SQL → IntelliSense`
2. **Offers to disable it** with a one-time dialog: "AKML SQL provides its own IntelliSense. Disable SSMS's built-in IntelliSense for the best experience? [Yes] [No] [Don't ask again]"
3. **If Yes:** Programmatically disables SSMS IntelliSense via the VS Shell settings API
4. **If No:** AKML SQL still works but warns about potential conflicts
5. **On uninstall:** Re-enables SSMS IntelliSense if it was disabled by AKML SQL

### 9.3 Visual Studio Handling

In Visual Studio with SSDT, AKML SQL coexists with VS's own IntelliSense for C#/VB but replaces T-SQL IntelliSense in `.sql` files and Database Projects. The extension registers a higher-priority `ICompletionSource` for the T-SQL content type.

---

## 10. Performance Requirements

| Metric | Target | Measurement |
|---|---|---|
| **Completion latency** | < 100ms (p95) | Time from keystroke to popup displayed |
| **Schema cache initial load** | < 3 seconds | Time from connection to basic completion ready |
| **Large database support** | 10,000+ objects | No degradation in completion speed |
| **Memory usage (engine)** | < 200MB | For a typical database (500 tables, 5000 columns) |
| **Memory usage (large DB)** | < 500MB | For a large database (5000+ tables) |
| **Document parse time** | < 50ms (incremental) | Time to re-parse after a keystroke |
| **Engine startup** | < 1 second | Time from IDE launch to engine ready |
| **Engine crash recovery** | < 2 seconds | Auto-restart and reconnect |
| **Named pipe latency** | < 5ms | Round-trip for a completion request |

---

## 11. Testing Requirements

### 11.1 Unit Tests

| Area | Test Count Target | Description |
|---|---|---|
| T-SQL Parser | 200+ | All SQL statement types, edge cases, malformed SQL |
| Alias Resolution | 50+ | Simple aliases, self-joins, subqueries, CTEs |
| Column Completion | 100+ | Single table, multi-table, JOINs, subqueries, star expansion |
| Keyword Completion | 80+ | All clause contexts, version-specific keywords |
| JOIN Completion | 40+ | FK detection, multi-column FKs, circular references |
| Fuzzy Matching | 30+ | CamelCase, substring, abbreviation matching |
| Cache Management | 30+ | Population, invalidation, lazy loading, permissions |
| Named Pipe Protocol | 20+ | Serialization, deserialization, error handling |

### 11.2 Integration Tests

| Test | Description |
|---|---|
| **End-to-end SSMS** | Install extension, connect to SQL Server, verify all completion types work |
| **End-to-end VS** | Same as above but in Visual Studio with SSDT project |
| **Large database** | Test against AdventureWorks, WideWorldImporters, and a synthetic 10K-table database |
| **Azure SQL** | Verify completion works against Azure SQL Database and Managed Instance |
| **Multi-database** | Switch between databases, verify cache per-database isolation |
| **Schema changes** | Create/alter/drop objects, verify cache refresh |
| **Permissions** | Test with read-only user, no-permission user, sysadmin |
| **Network latency** | Simulate high-latency connections (cloud DB), verify non-blocking UI |
| **Concurrent editing** | Multiple query windows open, each with different connections |
| **SQLCMD mode** | Verify behavior when SQLCMD mode is enabled |

### 11.3 Performance Tests

| Test | Target | Method |
|---|---|---|
| Completion latency (p50) | < 50ms | Automated keystroke simulation, 1000 iterations |
| Completion latency (p95) | < 100ms | Same, measure 95th percentile |
| Completion latency (p99) | < 200ms | Same, measure 99th percentile |
| Schema cache load (500 tables) | < 2s | Benchmark against AdventureWorks |
| Schema cache load (5000 tables) | < 10s | Benchmark against synthetic large DB |
| Memory at idle | < 50MB | Engine running with cache loaded, no active queries |
| Memory under load | < 200MB | Multiple connections, active completion |

---

## 12. Acceptance Criteria

1. **Keyword completion:** All T-SQL keywords suggest correctly in context (SELECT, FROM, WHERE, JOIN, GROUP BY, HAVING, ORDER BY, INSERT, UPDATE, DELETE, CREATE, ALTER, DROP, EXEC, WITH, MERGE, etc.)
2. **Table/view completion:** All tables and views appear when expected (after FROM, JOIN, INTO, UPDATE, etc.)
3. **Column completion:** Columns appear after `alias.` and `tablename.` with correct data types
4. **Alias resolution:** Aliases defined in FROM/JOIN are resolved correctly in all clause contexts
5. **CTE columns:** CTE-defined columns are available for completion in the outer query
6. **Temp tables:** #temp table columns from earlier in the batch are available
7. **JOIN assistance:** FK-based JOIN suggestions with auto-generated ON clauses
8. **Function signatures:** Parameter help for all built-in functions and user-defined functions/procedures
9. **Quick Info:** Hover tooltips show object metadata
10. **Fuzzy matching:** CamelCase and substring matching work (typing `CustID` matches `CustomerID`)
11. **Performance:** 100ms p95 completion latency
12. **Schema refresh:** Manual refresh (Ctrl+Shift+R) and automatic DDL-triggered refresh work
13. **Native IntelliSense handling:** Disabling/re-enabling SSMS IntelliSense works cleanly
14. **Theming:** Completion popup follows IDE theme (Light/Dark)
15. **No crashes:** Engine failure does not crash the IDE; auto-recovery within 2 seconds
16. **Settings:** All IntelliSense and cache settings configurable via Options dialog

---

## 13. SQL Server Version Compatibility

| SQL Server Version | Support Level | Notes |
|---|---|---|
| SQL Server 2016 (13.x) | Full | Minimum supported version |
| SQL Server 2017 (14.x) | Full | Graph tables, adaptive joins |
| SQL Server 2019 (15.x) | Full | UTF-8 columns, accelerated recovery |
| SQL Server 2022 (16.x) | Full | Ledger tables, JSON improvements |
| SQL Server 2025 (17.x) | Full | Latest features, database-scoped config |
| Azure SQL Database | Full | May have different DMV availability |
| Azure SQL Managed Instance | Full | Closest to on-prem feature parity |
| Azure Synapse (dedicated) | Partial | Limited IntelliSense (no standard sys catalog) |
| LocalDB | Full | For development scenarios |

---

## 14. Timeline & Milestones

| Week | Milestone | Deliverable |
|---|---|---|
| 1–2 | Schema Metadata Service | Database connection handling, sys catalog queries, in-memory cache with Phase A/B/C population strategy |
| 3–4 | T-SQL Parser Integration | ScriptDom integration, incremental parsing, cursor context detection, alias resolution |
| 5–6 | Completion Providers (Core) | Keyword, Database Object, Column completion providers. Basic end-to-end flow working. |
| 7–8 | Completion Providers (Advanced) | JOIN assist, Function Signature, Quick Info, Alias suggestion, Snippet triggers |
| 9–10 | Completion UI | WPF popup, fuzzy filtering, theming, keyboard/mouse navigation, multi-monitor DPI |
| 11–12 | Named Pipe Communication | Out-of-process engine setup, named pipe protocol, engine auto-start and crash recovery |
| 13–14 | Cache Management | Persistence to disk, background refresh, DDL detection, cross-database support |
| 15–16 | SSMS IntelliSense Handling | Native IntelliSense detection and disable/restore logic, VS SSDT integration |
| 17–18 | Settings UI | Options page in AKML SQL menu, all settings configurable |
| 19–20 | QA & Performance | Full test matrix, performance benchmarks, bug fixes, v2.0.0 release |

**Total estimated duration: 20 weeks** (5 months). This is the longest phase because IntelliSense is the foundation for all future features (formatting, error detection, AI suggestions all depend on the parser and schema cache).

---

## 15. Risks & Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| ScriptDom parser too slow for incremental use | High completion latency | Build lightweight tokenizer for hot path; use ScriptDom only for deep analysis |
| Named pipe communication unreliable | Suggestions fail intermittently | Implement retry logic, connection pooling, and fallback to in-process mode |
| Schema cache too large for big databases | High memory usage | Implement LRU eviction, lazy loading, and compression of cached metadata |
| SSMS IntelliSense disable breaks other extensions | User loses functionality | Only disable with explicit consent; re-enable on uninstall; detect other extensions |
| Azure SQL DMV differences | Schema cache incomplete | Abstract metadata queries behind a provider pattern; Azure-specific query variants |
| SSMS editor hook API changes | Keystroke interception breaks | Version-specific editor hooks per SSMS version (leverage Phase 1's per-version VSPackage) |
| User has custom sys catalog security | Cache population fails silently | Graceful degradation with clear user notification |
| Cross-database and linked server queries | Incomplete suggestions | Lazy-load cross-database metadata on demand; linked server support as stretch goal |

---

## 16. Dependencies

| Dependency | Version | Purpose |
|---|---|---|
| Microsoft.SqlServer.TransactSql.ScriptDom | Latest NuGet | T-SQL parsing and AST generation |
| MessagePack-CSharp | 2.x | Named pipe message serialization |
| Microsoft.VisualStudio.SDK | Per-SSMS-version | VSPackage host integration |
| Microsoft.VisualStudio.Editor | Per-SSMS-version | Editor text buffer hooks |
| System.IO.Pipes | .NET built-in | Named pipe communication |
| NLog or Serilog | Latest | Structured logging (shared with Phase 1) |

---

## 17. Success Metrics

- **Completion accuracy:** > 95% of triggered completions include the correct item in top 5 results
- **Completion latency:** < 100ms p95
- **Schema cache hit rate:** > 99% (suggestions served from cache, not live queries)
- **User satisfaction:** > 80% of beta testers rate IntelliSense as "better than SSMS built-in"
- **Adoption:** > 90% of AKML SQL users keep IntelliSense enabled (don't disable it)
- **Stability:** Zero engine crashes that require IDE restart in a typical 8-hour workday
- **Phase 3 readiness:** SQL Formatter (Phase 3) can reuse the T-SQL parser and AST without modifications

---

## 18. Competitive Comparison

| Feature | SSMS Native | SQL Prompt | dbForge Complete | AKML SQL Phase 2 |
|---|---|---|---|---|
| Keyword completion | Yes (buggy) | Yes | Yes | Yes |
| Schema-aware object completion | Yes (often stale) | Yes | Yes | Yes |
| Column completion with types | Partial | Yes | Yes | Yes (with PK/FK badges) |
| Alias resolution | Partial | Yes | Yes | Yes |
| CTE column completion | No | Yes | Yes | Yes |
| Temp table column completion | No | Yes | Partial | Yes |
| FK-based JOIN suggestion | No | Yes | No | Yes (with auto ON clause) |
| Fuzzy/CamelCase matching | No | Yes | Yes | Yes |
| Function signature help | Partial | Yes | Yes | Yes |
| Quick Info tooltips | Partial | Yes | Yes | Yes |
| Custom alias rules | No | No | Yes | Yes |
| Out-of-process engine | No | No | No | Yes (unique differentiator) |
| Disable native IntelliSense | N/A | Yes | Yes | Yes |
| Theme support | Partial | Yes | Yes | Yes |
| Configurable trigger delay | No | Yes | Yes | Yes |
| Reliable after schema changes | No | Yes | Yes | Yes (DDL-triggered refresh) |

---

*End of Phase 2 PRD — AKML SQL v1.0*
