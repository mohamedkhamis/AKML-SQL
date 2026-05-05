# AKML SQL — Phase 6: Code Refactoring

> **Version:** 1.0 | **Date:** March 2026 | **Author:** Mohamed Khamis
> **Status:** Ready for Implementation | **Classification:** Confidential
> **Depends on:** Phase 5 (Static Code Analysis) — analysis engine for safe transformations
> **Branch prefix:** `006-code-refactoring`

---

## 1. Executive Summary

Phase 6 delivers the Code Refactoring toolkit — a set of automated code transformations that restructure SQL without changing its behavior. While Phase 3 (Formatter) changes how code looks and Phase 5 (Analysis) finds problems, Phase 6 *fixes* structural issues and modernizes SQL patterns. Safe rename with reference tracking, wildcard expansion, object qualification, encapsulation, and 15+ refactoring operations make legacy SQL maintenance dramatically faster.

### Core Philosophy

Every refactoring operation follows the **preview-confirm-apply** pattern: the user initiates a refactoring, sees a preview of all changes across all affected objects, and confirms before any modification is committed. No silent changes. No surprises.

---

## 2. Document Metadata

| Field | Value |
|---|---|
| **Phase** | Phase 6 — Code Refactoring |
| **Depends on** | Phase 2 (parser, schema cache), Phase 3 (formatter), Phase 5 (analysis engine) |
| **Target** | All SSMS + VS targets |
| **Benchmark** | SQL Prompt refactoring + dbForge refactoring + ApexSQL Refactor |

---

## 3. Refactoring Operations — Complete List

### 3.1 Lightweight Refactoring (Inline, Instant)

These run instantly within the current editor document without requiring cross-object analysis.

| # | Operation | Shortcut | Description |
|---|---|---|---|
| 1 | **Expand wildcards** | `Ctrl+B, W` | Replace `SELECT *` with explicit column list from schema cache |
| 2 | **Qualify object names** | `Ctrl+B, Q` | Add schema prefix: `Orders` → `dbo.Orders` |
| 3 | **Add/Remove AS keyword** | `Ctrl+B, A` | Toggle AS keyword on alias definitions |
| 4 | **Add/Remove square brackets** | `Ctrl+B, B` | Toggle `[square brackets]` on identifiers |
| 5 | **Insert semicolons** | `Ctrl+B, S` | Add missing statement terminators |
| 6 | **Remove semicolons** | — | Remove all statement terminators |
| 7 | **Expand INSERT columns** | `Ctrl+B, I` | Add column list to INSERT INTO ... VALUES |
| 8 | **Expand EXEC parameters** | `Ctrl+B, E` | Add named parameters to EXEC calls |
| 9 | **Expand UPDATE columns** | `Ctrl+B, U` | Expand UPDATE SET with all columns |
| 10 | **Convert old-style JOINs** | `Ctrl+B, J` | Convert comma-separated JOINs to ANSI JOIN syntax |
| 11 | **Add non-aggregated to GROUP BY** | `Ctrl+B, G` | Auto-populate GROUP BY from SELECT non-aggregated columns |
| 12 | **Encapsulate in BEGIN/END** | `Ctrl+B, N` | Wrap selected statement in BEGIN/END block |
| 13 | **Convert EXEC to inline script** | `Ctrl+B, X` | Replace EXEC sp_name with the procedure's body inlined |
| 14 | **Inline EXEC** | — | Convert dynamic SQL EXEC to inline statements |
| 15 | **Replace deprecated syntax** | `Ctrl+B, D` | Auto-fix deprecated constructs flagged by Phase 5 |

### 3.2 Heavyweight Refactoring (Wizard-Based, Cross-Object)

These require analysis across multiple database objects and present a preview wizard before applying changes.

| # | Operation | Description |
|---|---|---|
| 16 | **Safe rename** | Rename table, column, procedure, variable, or alias with automatic reference updates across the current script (and optionally across all scripts in a project/directory) |
| 17 | **Extract to stored procedure** | Select a block of code, extract it into a new stored procedure with parameters auto-generated from referenced variables and tables |
| 18 | **Extract to CTE** | Convert a subquery into a named CTE at the top of the query |
| 19 | **Extract to derived table** | Convert an inline expression to a derived table in FROM |
| 20 | **Encapsulate as view** | Select a query, create a view from it, replace original with SELECT from the new view |
| 21 | **Split table** | Extract columns from a table into a new related table with FK (generates ALTER scripts) |
| 22 | **Move to new query window** | Move selected code to a new editor tab with proper USE database context |
| 23 | **Convert temp table to table variable** | Replace #temp table with @table variable (with warnings about statistics impact) |
| 24 | **Convert table variable to temp table** | Reverse of above |
| 25 | **Parameterize literal values** | Replace hard-coded values with declared variables |

---

## 4. Safe Rename — Deep Dive

The most complex and valuable refactoring operation.

### 4.1 Scope Levels

| Scope | Description | Requires |
|---|---|---|
| **Current statement** | Rename within the current SQL statement only | Parser only |
| **Current script** | Rename across the entire active editor document | Parser + alias resolution |
| **All open scripts** | Rename across all currently open editor tabs | Multi-document parser |
| **Project/directory** | Rename across all .sql files in a project or directory | File system scan |
| **Database** | Rename the actual database object and update all stored procedures, views, functions, triggers | Schema cache + DDL generation |

### 4.2 Rename Preview Dialog

```
┌──────────────────────────────────────────────────────────────┐
│  Rename: [OrderDate] → [OrderPlacedDate]                [X] │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│  Found 47 references across 12 files:                        │
│                                                              │
│  ☑ dbo.GetOrders.sql           — 8 references    [Preview]  │
│  ☑ dbo.OrderReport.sql         — 12 references   [Preview]  │
│  ☑ dbo.vw_OrderSummary.sql     — 5 references    [Preview]  │
│  ☑ dbo.trg_OrderAudit.sql      — 3 references    [Preview]  │
│  ☐ archive/OldReport.sql       — 2 references    [Preview]  │
│                                                              │
│  Preview:                                                    │
│  ┌────────────────────────────────────────────────────────┐  │
│  │ - SELECT o.OrderDate, ...                              │  │
│  │ + SELECT o.OrderPlacedDate, ...                        │  │
│  │                                                        │  │
│  │ - WHERE o.OrderDate >= @StartDate                      │  │
│  │ + WHERE o.OrderPlacedDate >= @StartDate                │  │
│  └────────────────────────────────────────────────────────┘  │
│                                                              │
│  ☑ Generate ALTER TABLE script for database rename           │
│  ☑ Create backup of modified files                           │
│                                                              │
│                    [Cancel]  [Apply Selected]                 │
└──────────────────────────────────────────────────────────────┘
```

---

## 5. Configuration

| Setting | Default | Description |
|---|---|---|
| `refactoring.previewBeforeApply` | `true` | Always show preview dialog for heavyweight refactoring |
| `refactoring.createBackups` | `true` | Backup files before cross-file modifications |
| `refactoring.formatAfterRefactor` | `true` | Apply formatting after refactoring operations |
| `refactoring.renameScope` | `currentScript` | Default scope for safe rename |
| `refactoring.includeComments` | `true` | Update references in comments during rename |
| `refactoring.includeStrings` | `false` | Update references in string literals (risky, off by default) |

---

## 6. Performance Requirements

| Metric | Target |
|---|---|
| Lightweight refactoring | < 100ms |
| Safe rename (current script, 1000 lines) | < 200ms |
| Safe rename (cross-file, 100 files) | < 5 seconds |
| Extract to procedure wizard | < 500ms to generate preview |
| Wildcard expansion (50-column table) | < 100ms |

---

## 7. Competitive Comparison

| Feature | SQL Prompt | dbForge | ApexSQL Refactor | AKML SQL Phase 6 |
|---|---|---|---|---|
| Expand wildcards | ✔ | ✔ | ✔ | ✔ |
| Qualify object names | ✔ | ✔ | ✔ | ✔ |
| Safe rename (in-script) | ✔ | ✔ | ✔ | ✔ |
| Safe rename (cross-file) | No | No | No | **✔** |
| Safe rename (database) | No | No | ✔ (limited) | **✔** |
| Add/remove AS keyword | ✔ | ✔ | ✔ | ✔ |
| Add/remove brackets | ✔ | ✔ | ✔ | ✔ |
| Insert semicolons | ✔ | ✔ | ✔ | ✔ |
| Expand INSERT columns | ✔ | ✔ | No | ✔ |
| Expand EXEC parameters | ✔ | ✔ | No | ✔ |
| Convert old-style JOINs | No | No | ✔ | **✔** |
| Extract to procedure | No | No | ✔ | **✔** |
| Extract to CTE | No | No | No | **✔** |
| Encapsulate as view | No | No | ✔ | **✔** |
| Split table | No | No | ✔ | **✔** |
| Parameterize literals | No | No | No | **✔** |
| Convert temp/table var | No | No | No | **✔** |
| Rename preview dialog | No | No | ✔ | **✔** |
| Auto GROUP BY population | No | ✔ | No | **✔** |

---

## 8. Timeline & Milestones

| Week | Milestone | Deliverable |
|---|---|---|
| 1–2 | Lightweight refactoring (1–8) | Wildcard expansion, qualify names, AS keyword, brackets, semicolons, INSERT/EXEC expansion |
| 3–4 | Lightweight refactoring (9–15) | UPDATE expand, old-style JOINs, GROUP BY, BEGIN/END, EXEC conversion, deprecated fixes |
| 5–6 | Safe rename engine | In-script rename, cross-file rename, database rename with preview dialog |
| 7–8 | Heavyweight refactoring & QA | Extract to proc/CTE/view, split table, parameterize, full test matrix |

**Total estimated duration: 8 weeks** (2 months).

---

*End of Phase 6 PRD — AKML SQL v1.0*
