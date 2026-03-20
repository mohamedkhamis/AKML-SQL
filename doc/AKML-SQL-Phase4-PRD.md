# AKML SQL — Phase 4: Snippet Manager

> **Version:** 1.0 | **Date:** March 2026 | **Author:** Abdulrahman Khamis
> **Status:** Ready for Implementation | **Classification:** Confidential
> **Depends on:** Phase 3 (SQL Formatter) — formatting engine must be complete
> **Branch prefix:** `004-snippet-manager`

---

## 1. Executive Summary

Phase 4 delivers the Snippet Manager — a system for creating, managing, and instantly inserting reusable SQL code fragments. Snippets are the "muscle memory" of SQL development: type `ssf` and get `SELECT * FROM`, type `ct` and get a full CREATE TABLE skeleton with tab-stop navigation through the table name, columns, and data types. This phase goes beyond basic text expansion to deliver schema-aware, context-sensitive, parameterized code templates with team sharing.

SQL Prompt's snippet system is beloved by its users but has architectural limitations — snippets are stored as individual XML files in a single folder, sharing requires the Redgate Platform (paid TBE license), and there's no version history. AKML SQL reimagines snippets with a modern architecture: JSON-based storage, Git-friendly format, built-in versioning, multi-folder sources (personal + team + community), and deep integration with the Phase 2 IntelliSense engine for schema-aware placeholder expansion.

### Core Philosophy

Snippets should feel like an extension of IntelliSense, not a separate feature. When you type a snippet shortcode, it should appear in the same completion popup alongside keywords and objects, ranked by relevance. When a snippet expands, its placeholders should offer the same schema-aware suggestions as regular IntelliSense. The boundary between "typing code" and "using a snippet" should be invisible.

---

## 2. Document Metadata

| Field | Value |
|---|---|
| **Phase** | Phase 4 — Snippet Manager |
| **Depends on** | Phase 2 (IntelliSense engine, schema cache), Phase 3 (formatter integration) |
| **Target SSMS** | SSMS 20 (x86), SSMS 21 (x64), SSMS 22 (x64) |
| **Target Visual Studio** | VS 2019, VS 2022, VS 2026 (with SSDT) |
| **.NET Version** | .NET Fx 4.7.2 (shell) + .NET 10/11 (snippet engine, out-of-proc) |
| **Benchmark** | SQL Prompt snippets + dbForge snippets (combined feature set) |

---

## 3. Goals & Non-Goals

### 3.1 Goals

- **75+ built-in snippets** covering DML, DDL, DBA, metadata queries, error handling, and common patterns
- **Custom snippet creation** with rich editor, tab-stop placeholders, and live preview
- **Schema-aware placeholders** that offer IntelliSense suggestions inside snippet expansion (e.g., `$TABLE$` shows table list)
- **Surround-with snippets** that wrap selected code (e.g., wrap in TRY/CATCH, wrap in transaction)
- **Context-sensitive activation** — snippets appear in IntelliSense popup ranked by relevance to cursor context
- **Multi-source snippet library** — personal folder + team folder + community repository (AKML Hub, future)
- **Snippet categories and tags** for organization and searchable discovery
- **Variable system** with built-in variables (`$DATE$`, `$USER$`, `$DATABASE$`, `$SCHEMA$`, `$SELECTEDTEXT$`, `$CLIPBOARD$`, `$CURSOR$`) and custom named placeholders
- **Tab-stop navigation** — Tab/Shift+Tab moves between placeholders; editing one placeholder updates all linked instances
- **Snippet versioning** — track changes to shared snippets with rollback capability
- **Import/export** — import from SQL Prompt `.sqlpromptsnippet` XML files, SSMS native `.snippet` XML files, and export as `.akmlsnippet` JSON
- **Format-on-expand** — apply active formatting profile when snippet is inserted
- **Snippet statistics** — track usage frequency to improve ranking in suggestions

### 3.2 Non-Goals

- No AI-generated snippets (Phase 9)
- No snippet marketplace or paid community snippets
- No cross-language snippets (C#, PowerShell) — T-SQL only

---

## 4. Architecture Overview

### 4.1 Snippet Engine

The snippet engine runs as a module within the Phase 2 out-of-process IntelliSense engine, leveraging the existing schema cache and parser.

```
┌──────────────────────────────────────────────────────────────┐
│  IntelliSense Engine (out-of-proc, .NET 10/11)               │
│                                                              │
│  ┌────────────────┐   ┌────────────────┐   ┌──────────────┐ │
│  │ Snippet Loader  │──►│ Snippet Index  │──►│ Completion   │ │
│  │ (multi-source)  │   │ (in-memory)    │   │ Integration  │ │
│  └────────────────┘   └────────────────┘   └──────────────┘ │
│                                                              │
│  ┌────────────────┐   ┌────────────────┐   ┌──────────────┐ │
│  │ Placeholder    │──►│ Schema-Aware   │──►│ Tab-Stop     │ │
│  │ Parser         │   │ Resolver       │   │ Navigator    │ │
│  └────────────────┘   └────────────────┘   └──────────────┘ │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐  │
│  │           Snippet File Watcher (hot reload)             │  │
│  └────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────┘
```

### 4.2 Snippet Sources (Multi-Folder)

| Source | Path | Priority | Writeable | Description |
|---|---|---|---|---|
| Built-in | `<install>\snippets\` | 3 (lowest) | No | 75+ snippets shipped with AKML SQL |
| Personal | `%AppData%\AKML SQL\snippets\` | 1 (highest) | Yes | User's custom snippets |
| Team | Configurable shared path | 2 | Configurable | Team-shared snippets (network share, Git repo, or AKML Platform) |
| Community | AKML Hub (future) | 4 | No | Community-contributed snippets |

When multiple snippets share the same shortcode, the highest-priority source wins. Users can override a built-in snippet by creating a personal snippet with the same shortcode.

---

## 5. Snippet File Format

### 5.1 `.akmlsnippet` JSON Format

```json
{
  "metadata": {
    "id": "7f3a1b2c-4d5e-6789-abcd-ef0123456789",
    "shortcode": "ct",
    "name": "Create Table",
    "description": "Creates a new table with primary key and common columns",
    "author": "AKML SQL",
    "version": "1.0",
    "created": "2026-06-01T00:00:00Z",
    "modified": "2026-06-01T00:00:00Z",
    "category": "DDL",
    "tags": ["create", "table", "ddl", "schema"],
    "context": ["global", "batch_start"],
    "surroundsWith": false
  },
  "variables": [
    { "name": "SchemaName", "default": "dbo", "tooltip": "Schema name", "schemaAware": "schemas" },
    { "name": "TableName", "default": "NewTable", "tooltip": "Table name" },
    { "name": "PKColumn", "default": "Id", "tooltip": "Primary key column name" },
    { "name": "PKType", "default": "int", "tooltip": "Primary key data type", "schemaAware": "datatypes" }
  ],
  "body": [
    "CREATE TABLE [$SchemaName$].[$TableName$]",
    "(",
    "    [$PKColumn$] $PKType$ IDENTITY(1, 1) NOT NULL,",
    "    $CURSOR$",
    "    CONSTRAINT [PK_$TableName$] PRIMARY KEY CLUSTERED ([$PKColumn$])",
    ");",
    "GO"
  ]
}
```

### 5.2 Built-in Variables

| Variable | Description | Example Output |
|---|---|---|
| `$CURSOR$` | Final cursor position after snippet expansion | — |
| `$SELECTEDTEXT$` | Currently selected text (for surround-with snippets) | `SELECT * FROM Orders` |
| `$CLIPBOARD$` | Current clipboard content | — |
| `$DATE$` | Current date in ISO format | `2026-07-15` |
| `$DATETIME$` | Current date and time | `2026-07-15 14:30:00` |
| `$TIME$` | Current time | `14:30:00` |
| `$USER$` | Current Windows username | `akhamis` |
| `$MACHINE$` | Machine name | `DEV-PC-01` |
| `$DATABASE$` | Current database name | `AdventureWorks` |
| `$SERVER$` | Current server name | `SQL-PROD-01` |
| `$SCHEMA$` | Current default schema | `dbo` |
| `$GUID$` | New random GUID | `a1b2c3d4-...` |
| `$YEAR$` | Current year | `2026` |
| `$FILENAME$` | Current file name | `GetOrders.sql` |

### 5.3 Schema-Aware Placeholders

When a variable has `"schemaAware"` set, the placeholder shows IntelliSense suggestions during tab-stop navigation:

| Schema-Aware Type | Suggestions Offered |
|---|---|
| `schemas` | List of schemas from the schema cache |
| `tables` | List of tables |
| `views` | List of views |
| `columns` | List of columns (when table context is known) |
| `procedures` | List of stored procedures |
| `functions` | List of functions |
| `datatypes` | List of SQL Server data types (built-in + UDTs) |
| `databases` | List of databases |
| `indexes` | List of indexes |

---

## 6. Built-in Snippet Library (75+ Snippets)

### 6.1 DML Snippets (20)

| Shortcode | Name | Expansion |
|---|---|---|
| `ssf` | Select Star From | `SELECT * FROM $TABLE$` |
| `sel` | Select | `SELECT $COLUMNS$ FROM $TABLE$ WHERE $CONDITION$` |
| `selc` | Select Count | `SELECT COUNT(*) FROM $TABLE$ WHERE $CONDITION$` |
| `selt` | Select Top | `SELECT TOP ($N$) * FROM $TABLE$ ORDER BY $COLUMN$` |
| `seld` | Select Distinct | `SELECT DISTINCT $COLUMNS$ FROM $TABLE$` |
| `ins` | Insert Into | `INSERT INTO $TABLE$ ($COLUMNS$) VALUES ($VALUES$)` |
| `inss` | Insert Select | `INSERT INTO $TARGET$ ($COLUMNS$) SELECT $COLUMNS$ FROM $SOURCE$` |
| `upd` | Update | `UPDATE $TABLE$ SET $COLUMN$ = $VALUE$ WHERE $CONDITION$` |
| `del` | Delete | `DELETE FROM $TABLE$ WHERE $CONDITION$` |
| `mer` | Merge | Full MERGE statement with MATCHED/NOT MATCHED |
| `cte` | Common Table Expression | `WITH $Name$ AS ($QUERY$) SELECT * FROM $Name$` |
| `rcte` | Recursive CTE | Recursive CTE with anchor and recursive members |
| `piv` | Pivot | PIVOT query template |
| `unpiv` | Unpivot | UNPIVOT query template |
| `ex` | Exists | `IF EXISTS (SELECT 1 FROM $TABLE$ WHERE $CONDITION$)` |
| `nex` | Not Exists | `IF NOT EXISTS (...)` |
| `j` | Join | `JOIN $TABLE$ $ALIAS$ ON $ALIAS$.$COLUMN$ = $COLUMN$` |
| `lj` | Left Join | `LEFT JOIN ...` |
| `cj` | Cross Join | `CROSS JOIN $TABLE$ $ALIAS$` |
| `ca` | Cross Apply | `CROSS APPLY (...) AS $ALIAS$` |

### 6.2 DDL Snippets (15)

| Shortcode | Name | Expansion |
|---|---|---|
| `ct` | Create Table | Full CREATE TABLE with PK |
| `ci` | Create Index | `CREATE NONCLUSTERED INDEX ...` |
| `cci` | Create Clustered Index | `CREATE CLUSTERED INDEX ...` |
| `cui` | Create Unique Index | `CREATE UNIQUE INDEX ...` |
| `cp` | Create Procedure | Full stored procedure skeleton with error handling |
| `cf` | Create Function (Scalar) | Scalar function skeleton |
| `ctf` | Create Table Function | Table-valued function skeleton |
| `cv` | Create View | View skeleton |
| `ctr` | Create Trigger | Trigger skeleton with INSERTED/DELETED |
| `cs` | Create Schema | `CREATE SCHEMA $Name$` |
| `ac` | Add Column | `ALTER TABLE $TABLE$ ADD $COLUMN$ $TYPE$` |
| `dc` | Drop Column | `ALTER TABLE $TABLE$ DROP COLUMN $COLUMN$` |
| `afk` | Add Foreign Key | Foreign key constraint |
| `adf` | Add Default | Default constraint |
| `ack` | Add Check Constraint | Check constraint |

### 6.3 DBA & Metadata Snippets (20)

| Shortcode | Name | Expansion |
|---|---|---|
| `sp` | sp_help | `EXEC sp_help '$OBJECT$'` |
| `sh` | sp_helptext | `EXEC sp_helptext '$OBJECT$'` |
| `sw` | sp_who2 | `EXEC sp_who2` |
| `dbsize` | Database Size | Query for database file sizes |
| `tsize` | Table Sizes | Query for table row counts and sizes |
| `idx` | Index Usage | Index usage statistics query |
| `midx` | Missing Indexes | Missing index DMV query |
| `locks` | Active Locks | Current lock information |
| `blocks` | Blocking Queries | Active blocking chains |
| `waits` | Wait Stats | Top wait statistics |
| `cpu` | CPU Usage by Query | Top queries by CPU |
| `io` | I/O Usage by Query | Top queries by I/O |
| `plan` | Cached Plans | Query plan cache analysis |
| `frag` | Index Fragmentation | Index fragmentation report |
| `deps` | Object Dependencies | `sys.dm_sql_referenced_entities` query |
| `cols` | Column Info | Column metadata for a table |
| `fks` | Foreign Keys | Foreign key relationships query |
| `perms` | Permissions | Object permissions query |
| `bak` | Backup Database | Full backup command |
| `rest` | Restore Database | Restore command skeleton |

### 6.4 Error Handling & Control Flow Snippets (10)

| Shortcode | Name | Expansion |
|---|---|---|
| `tc` | Try-Catch | Full TRY/CATCH block with error handling |
| `tct` | Try-Catch Transaction | TRY/CATCH with BEGIN TRAN/COMMIT/ROLLBACK |
| `ife` | If-Else | IF/ELSE block |
| `ifex` | If Exists | IF EXISTS (...) BEGIN ... END |
| `wh` | While Loop | WHILE loop skeleton |
| `cur` | Cursor | Full cursor skeleton (FAST_FORWARD) |
| `tran` | Transaction | BEGIN TRAN / COMMIT / ROLLBACK skeleton |
| `raiserr` | Raise Error | RAISERROR with message and severity |
| `throw` | Throw | THROW statement |
| `print` | Print Debug | `PRINT CONCAT('Debug: ', $MESSAGE$)` |

### 6.5 Surround-With Snippets (10)

| Shortcode | Name | Wraps Selected Code With |
|---|---|---|
| `stc` | Surround Try-Catch | TRY/CATCH around selection |
| `stran` | Surround Transaction | Transaction around selection |
| `sife` | Surround If-Exists | IF EXISTS check around selection |
| `sbe` | Surround Begin-End | BEGIN/END around selection |
| `stiming` | Surround Timing | Execution timing measurement around selection |
| `snocount` | Surround SET NOCOUNT | SET NOCOUNT ON/OFF around selection |
| `scomment` | Surround Comment Block | Block comment around selection |
| `sregion` | Surround Region | Named region comments around selection |
| `snoformat` | Surround Noformat | `--noformat` / `--endnoformat` tags around selection |
| `stemp` | Surround Temp Table | Insert selection results into #temp table |

---

## 7. Snippet Manager UI

### 7.1 Snippet Manager Dialog

```
┌─────────────────────────────────────────────────────────────────┐
│  Snippet Manager                                          [X]   │
├───────────────────┬─────────────────────────────────────────────┤
│  [🔍 Search...]   │  ┌─ Snippet Editor ──────────────────────┐  │
│                   │  │ Name: [Create Table              ]    │  │
│  ▼ Personal (12)  │  │ Shortcode: [ct     ]                 │  │
│    ct ✱           │  │ Category: [DDL ▼]  Tags: [create,..] │  │
│    myproc         │  │ Description: [Creates a new table...] │  │
│    myheader       │  │                                       │  │
│  ▼ Team (8)       │  │ ── Variables ──────────────────────── │  │
│    corp_header    │  │ SchemaName  [dbo]     schemas  ☑      │  │
│    corp_errorlog  │  │ TableName   [NewTable] text           │  │
│  ▼ Built-in (75)  │  │ PKColumn    [Id]      text            │  │
│   ▶ DML (20)      │  │ PKType      [int]     datatypes ☑    │  │
│   ▶ DDL (15)      │  │                                       │  │
│   ▶ DBA (20)      │  │ ── Code ──────────────────────────── │  │
│   ▶ Control (10)  │  │ CREATE TABLE [$SchemaName$].[...      │  │
│   ▶ Surround (10) │  │                                       │  │
│                   │  │ ── Preview ────────────────────────── │  │
│ [New] [Import]    │  │ CREATE TABLE [dbo].[NewTable]         │  │
│ [Export] [Delete]  │  │ (                                    │  │
│                   │  │     [Id] int IDENTITY(1,1) NOT NULL,  │  │
│                   │  │ ...                                   │  │
├───────────────────┴──┴───────────────────────────────────────┤  │
│            [Cancel]  [Save]  [Save & Close]                    │
└─────────────────────────────────────────────────────────────────┘
```

### 7.2 Key UI Features

| Feature | Description |
|---|---|
| **Search** | Full-text search across snippet name, shortcode, description, tags, and body |
| **Category tree** | Collapsible tree view organized by source (Personal/Team/Built-in) and category |
| **Drag-and-drop** | Reorder snippets within categories; drag between Personal and Team sources |
| **Live preview** | Bottom pane shows expanded snippet with current variable defaults and active formatting profile |
| **Variable editor** | Visual editor for placeholder variables with type selection (text, schema-aware type) |
| **Code editor** | Syntax-highlighted code editor for snippet body with variable insertion toolbar |
| **Usage stats** | Small badge showing usage count next to each snippet |
| **Create from selection** | Right-click selected code in editor → "Create Snippet from Selection" |
| **Quick-edit** | Double-click a snippet in the completion popup to open its editor |
| **Duplicate detection** | Warning when creating a snippet with a shortcode that conflicts with an existing one |

---

## 8. Snippet Integration with IntelliSense

### 8.1 Completion Popup Integration

Snippets appear in the Phase 2 completion popup alongside keywords, objects, and columns:

```
┌────────────────────────────────────────┐
│ 🔍 ct                                  │
├────────────────────────────────────────┤
│ {} ct          Create Table      ▲     │
│ {} ctf         Create Table Func       │
│ 📊 Customers   dbo.Customers    ▼     │
├────────────────────────────────────────┤
│ Snippets: 2 | Objects: 1               │
└────────────────────────────────────────┘
```

Snippets are indicated with a `{}` icon and sorted by usage frequency within their relevance group.

### 8.2 Context-Sensitive Filtering

The snippet engine filters suggestions based on cursor context (from Phase 2 parser):

| Cursor Context | Snippets Shown |
|---|---|
| Start of batch / after GO | All snippets (DDL, DML, DBA, Control) |
| After SELECT | Column-related snippets, aggregate snippets |
| After FROM | Table-related snippets, JOIN snippets |
| After WHERE | Condition snippets, EXISTS patterns |
| Inside CREATE TABLE | Column definition snippets |
| Selection active | Surround-with snippets only |

---

## 9. Import/Export & Migration

### 9.1 Import Sources

| Source | Format | Import Method |
|---|---|---|
| SQL Prompt | `.sqlpromptsnippet` (XML) | Auto-detect, convert variables to AKML format |
| SSMS Native | `.snippet` (XML, VS CodeSnippet schema) | Auto-detect, map VS-style placeholders |
| AKML SQL | `.akmlsnippet` (JSON) | Direct import |
| Bulk import | Directory of any format | Scan directory, convert all found snippets |

### 9.2 SQL Prompt Migration

Import dialog auto-detects the SQL Prompt snippet folder (`%LocalAppData%\Red Gate\SQL Prompt *\Snippets\`) and offers one-click migration of all snippets with variable mapping:

| SQL Prompt Variable | AKML SQL Equivalent |
|---|---|
| `$SELECTEDTEXT$` | `$SELECTEDTEXT$` (identical) |
| `$CURSOR$` | `$CURSOR$` (identical) |
| `$DATE$` | `$DATE$` (identical) |
| `$TIME$` | `$TIME$` (identical) |
| `$DBNAME$` | `$DATABASE$` |
| Custom `$variablename$` | Custom variable with same name |

---

## 10. Configuration & Options

| Setting | Default | Description |
|---|---|---|
| `snippets.enabled` | `true` | Master switch for snippets |
| `snippets.showInCompletion` | `true` | Show snippets in IntelliSense popup |
| `snippets.triggerKey` | `Tab` | Key to expand snippet (Tab or Enter) |
| `snippets.formatOnExpand` | `true` | Apply active formatting profile on expansion |
| `snippets.personalFolder` | `%AppData%\AKML SQL\snippets\` | Personal snippet directory |
| `snippets.teamFolder` | (empty) | Team snippet shared directory |
| `snippets.contextFilter` | `true` | Filter snippets by cursor context |
| `snippets.surroundShortcut` | `Ctrl+K, Ctrl+S` | Shortcut for surround-with snippet list |
| `snippets.trackUsage` | `true` | Track snippet usage for ranking |

---

## 11. Performance Requirements

| Metric | Target |
|---|---|
| Snippet expansion latency | < 20ms |
| Snippet search (across 500 snippets) | < 50ms |
| File watcher reload (hot reload) | < 100ms |
| Tab-stop navigation | < 10ms per jump |
| Schema-aware placeholder suggestion | < 100ms (uses Phase 2 cache) |

---

## 12. Testing Requirements

| Area | Test Count | Description |
|---|---|---|
| Built-in snippets | 75+ | Every built-in snippet expands correctly |
| Custom snippets | 30+ | CRUD operations, variable types, validation |
| Schema-aware placeholders | 25+ | All schema-aware types resolve correctly |
| Surround-with | 15+ | All surround snippets with various selections |
| Tab-stop navigation | 20+ | Single/multiple/linked placeholders, nested |
| Import/export | 20+ | SQL Prompt, SSMS native, bulk import |
| Context filtering | 20+ | All cursor contexts filter correctly |
| IntelliSense integration | 15+ | Ranking, display, expansion from popup |
| Format on expand | 10+ | Various formatting profiles applied on expand |

---

## 13. Competitive Comparison

| Feature | SSMS Native | SQL Prompt | dbForge | AKML SQL Phase 4 |
|---|---|---|---|---|
| Built-in snippets | ~30 | ~30 | ~40 | **75+** |
| Custom snippets | Yes (XML) | Yes (XML) | Yes | Yes (JSON) |
| Schema-aware placeholders | No | No | No | **Yes (unique)** |
| Surround-with | Yes (basic) | Yes | No | **Yes (10 built-in)** |
| Context-sensitive | No | Partial | No | **Yes (parser-based)** |
| IntelliSense integration | No | Yes | Yes | **Yes (deep)** |
| Tab-stop navigation | Yes | Yes | Yes | Yes |
| Linked placeholders | Yes | Yes | No | Yes |
| Team sharing | No | Yes (Redgate Platform, TBE) | File export | **Yes (multi-folder)** |
| Format on expand | No | No | No | **Yes** |
| Usage statistics | No | No | No | **Yes** |
| SQL Prompt import | N/A | N/A | No | **Yes** |
| Create from selection | No | Yes | No | **Yes** |
| Snippet versioning | No | No | No | **Yes** |

---

## 14. Timeline & Milestones

| Week | Milestone | Deliverable |
|---|---|---|
| 1–2 | Snippet engine & file format | Snippet loader, JSON parser, multi-source resolver, file watcher |
| 3–4 | Expansion engine | Variable system, placeholder parser, tab-stop navigation, linked placeholders, schema-aware resolution |
| 5–6 | IntelliSense integration & surround-with | Completion popup integration, context filtering, surround-with logic, format-on-expand |
| 7 | Snippet Manager UI | Full dialog with editor, preview, search, categories, drag-and-drop |
| 8 | Import/export, migration & QA | SQL Prompt import, SSMS import, bulk import, usage tracking, full test matrix |

**Total estimated duration: 8 weeks** (2 months).

---

## 15. Success Metrics

- **Built-in coverage:** 75+ snippets covering all common SQL patterns
- **Expansion accuracy:** 100% of snippets expand correctly with all variable types
- **Import success:** > 95% of SQL Prompt snippets import without manual intervention
- **User adoption:** > 70% of AKML SQL users use snippets at least once per session
- **Performance:** < 20ms snippet expansion, < 50ms search across 500 snippets
- **Phase 5 readiness:** Code analysis (Phase 5) can leverage snippets to suggest pattern-based fixes

---

*End of Phase 4 PRD — AKML SQL v1.0*
