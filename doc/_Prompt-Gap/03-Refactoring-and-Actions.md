# 03 — Refactoring & Actions

Scope: the **Actions list** (Ctrl menu), object/batch refactors, heavyweight database refactors, and query/result refactors.

Status legend: ✅ done · 🟡 partial · ❌ missing · ➖ out of scope

---

## 1. SQL Prompt Actions (runnable individually, or as part of Format SQL)

| Action | Description | Shortcut | Status |
|---|---|---|---|
| Apply casing options | Recase keywords, built-in functions, built-in data types, global variables per active style | `Ctrl+B, Ctrl+U` | 🟡 works via full Format; standalone action no-op |
| Insert semicolons | Add missing statement-terminating semicolons | `Ctrl+B, Ctrl+C` | ❌ class exists, engine dispatch unwired |
| Expand wildcards | Expand `SELECT *` / `SELECT table.*` to explicit column lists | `Ctrl+B, Ctrl+W` | ❌ class exists, engine dispatch unwired |
| Qualify object names | Qualify to `owner.object` and `table.column` (no server/DB qualifier added) | `Ctrl+B, Ctrl+Q` | ❌ class exists, engine dispatch unwired |
| Add square brackets | Bracket all identifiers | `Ctrl+B, Ctrl+B` | ❌ class exists, engine dispatch unwired |
| Remove square brackets | Strip brackets from identifiers that don't need delimiting | `Ctrl+B, Ctrl+B` | ❌ class exists, engine dispatch unwired |
| Actions list (lightbulb/Ctrl menu) | Context list of available actions at the cursor/selection | `Ctrl` | ✅ lightbulb provider (two entries no-op) |
| Disable formatting for selected text | Marker-wrap a block so it's skipped by Format | Actions list | ❌ no marker-insert command; markers only parsed |
| Unformat | Strip all formatting whitespace from selection | Actions list | ✅ UnformatOperation, wired `Ctrl+B,Ctrl+U` |

> Qualification scope is configurable in **Options ▸ Inserted code ▸ Qualification** (which object kinds get qualified).

## 2. Refactoring an object or a batch (local scope)

| Refactor | Description | Shortcut / Where | Status |
|---|---|---|---|
| Script object as ALTER | Open an object's definition as an `ALTER` script | context menu | ❌ T076 unimplemented; ScriptAs has no ALTER |
| Select object in Object Explorer | Jump from code to the object's node in Object Explorer | context menu | ❌ T077 unimplemented (command-id placeholder only) |
| Find invalid objects | Detect objects whose definitions are broken/invalid | SQL Prompt menu | ❌ engine handler is a not-implemented stub |
| Find unused variables & parameters | Highlight declared/assigned-but-unused variables & parameters, and unused assignments | SQL Prompt menu | 🟡 via analysis rules BP009/BP023; no dedicated panel |
| Summarize script | Outline/overview of a script; click an item to jump to the matching statement | SQL Prompt menu | 🟡 Document Outline tool window; skips flat statements |
| Rename objects (Smart Rename) | Rename tables, views, procs, functions, columns, parameters and update all references without breaking dependencies | Object Explorer context / wizard | 🟡 SafeRename name-based cross-file; no dep safety/wizard |
| Rename scripted object | Rename the object you're editing in the query window | `F2` (SSMS) / `Shift+F2` (VS) | 🟡 context-menu only; F2/Shift+F2 chord unbound |

## 3. Database refactoring (heavyweight)

| Refactor | Description | Shortcut / Where | Status |
|---|---|---|---|
| Smart Rename (deep) | Rename modules/tables/columns and rewrite referencing objects; keeps `sys.sql_modules` consistent; generates a reviewable script | Object Explorer | ❌ SmartRenameHandler (T103) unimplemented; SafeRename is text-only |
| Split table | Move/copy columns into a new table and rewrite referencing procs/views; can introduce referential-integrity tables | Object Explorer | 🟡 generates DDL script; no auto-rewrite of dependents |
| Encapsulate as new stored procedure | Turn a selection into a new stored procedure, optionally replacing the selection with a call | `Ctrl+B, Ctrl+E` | 🟡 ExtractToProc functional; no `Ctrl+B,Ctrl+E` chord |
| Inline stored procedure | Inline a procedure's body into the calling code | `Ctrl+B, Ctrl+I` | ❌ T105 unimplemented (command-id placeholder only) |

## 4. Refactoring queries & query results

| Refactor | Description | Where | Status |
|---|---|---|---|
| Inline EXEC | Turn an `EXEC` call into the inline query | context menu | ❌ no inline-proc-body operation |
| Refactor INSERT → UPDATE | Convert an `INSERT` statement into an `UPDATE` | context menu | ❌ no statement-level INSERT→UPDATE refactor |
| Script as INSERT | Turn grid results into `INSERT` statements | results grid right-click | ✅ GridCopyAsMenu / GridScriptGenerator |
| Copy as IN clause | Turn selected result values into an `IN (...)` list | results grid right-click | ✅ GridCopyAsMenu.FormatAsInClause |
| Open in Excel | Export grid results straight to Excel | results grid right-click | 🟡 XLSX export to file; no Excel auto-launch |

---

### Quick keyboard reference (this scope)
Casing `Ctrl+B,Ctrl+U` · Qualify `Ctrl+B,Ctrl+Q` · Expand wildcards `Ctrl+B,Ctrl+W` · Semicolons `Ctrl+B,Ctrl+C` · Brackets `Ctrl+B,Ctrl+B` · Inline proc `Ctrl+B,Ctrl+I` · Encapsulate `Ctrl+B,Ctrl+E` · Rename `F2` (SSMS) / `Shift+F2` (VS) · Actions list `Ctrl`.
