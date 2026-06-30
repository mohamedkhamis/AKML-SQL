# 03 — Refactoring & Actions

Scope: the **Actions list** (Ctrl menu), object/batch refactors, heavyweight database refactors, and query/result refactors.

Status legend: ✅ done · 🟡 partial · ❌ missing · ➖ out of scope

---

## 1. SQL Prompt Actions (runnable individually, or as part of Format SQL)

| Action | Description | Shortcut | Status |
|---|---|---|---|
| Apply casing options | Recase keywords, built-in functions, built-in data types, global variables per active style | `Ctrl+B, Ctrl+U` | ✅ T016 [X]: FormatRequestHandler.cs:198 ResolveFormatAction dispatches CasingOnly(0) to CasingAction |
| Insert semicolons | Add missing statement-terminating semicolons | `Ctrl+B, Ctrl+C` | ✅ T016 [X]: FormatRequestHandler.cs:201 dispatches InsertSemicolonsAction() |
| Expand wildcards | Expand `SELECT *` / `SELECT table.*` to explicit column lists | `Ctrl+B, Ctrl+W` | 🟡 T016 [X]: FormatRequestHandler.cs:203 dispatches ExpandWildcardsAction() but returns schema-stub 'requires schema cache' message — not functional end-to-end |
| Qualify object names | Qualify to `owner.object` and `table.column` (no server/DB qualifier added) | `Ctrl+B, Ctrl+Q` | 🟡 T016 [X]: FormatRequestHandler.cs:204 dispatches QualifyObjectNamesAction() but returns schema-stub 'requires schema cache' message |
| Add square brackets | Bracket all identifiers | `Ctrl+B, Ctrl+B` | ✅ T016 [X]: FormatRequestHandler.cs:205 dispatches ToggleBracketsAction() with AddSquareBrackets=true |
| Remove square brackets | Strip brackets from identifiers that don't need delimiting | `Ctrl+B, Ctrl+B` | ✅ T016 [X]: FormatRequestHandler.cs:206 dispatches ToggleBracketsAction() with AddSquareBrackets=false |
| Actions list (lightbulb/Ctrl menu) | Context list of available actions at the cursor/selection | `Ctrl` | ✅ lightbulb provider (two entries no-op) |
| Disable formatting for selected text | Marker-wrap a block so it's skipped by Format | Actions list | ✅ T068 [X]: DisableFormattingForSelectionCommand.cs wraps selection in '-- AKML formatting off/on' markers; wired in both packages (AkmlSqlPackage.cs:79) |
| Unformat | Strip all formatting whitespace from selection | Actions list | ✅ UnformatOperation, wired `Ctrl+B,Ctrl+U` |

> Qualification scope is configurable in **Options ▸ Inserted code ▸ Qualification** (which object kinds get qualified).

## 2. Refactoring an object or a batch (local scope)

| Refactor | Description | Shortcut / Where | Status |
|---|---|---|---|
| Script object as ALTER | Open an object's definition as an `ALTER` script | context menu | ✅ T066+T067 [X]: ScriptAsAlterRewriter.ToAlter in ScriptAsHandler.cs:148; ScriptAsAlterCommand.cs wired in both packages (AkmlSqlPackage.cs:72) |
| Select object in Object Explorer | Jump from code to the object's node in Object Explorer | context menu | ❌ T077 unimplemented (command-id placeholder only) |
| Find invalid objects | Detect objects whose definitions are broken/invalid | SQL Prompt menu | ✅ T058+T059 [X]: FindInvalidObjectsHandler (sys.sql_expression_dependencies) + FindInvalidObjectsCommand.cs wired in both packages |
| Find unused variables & parameters | Highlight declared/assigned-but-unused variables & parameters, and unused assignments | SQL Prompt menu | 🟡 via analysis rules BP009/BP023; no dedicated panel |
| Summarize script | Outline/overview of a script; click an item to jump to the matching statement | SQL Prompt menu | 🟡 Document Outline tool window; skips flat statements |
| Rename objects (Smart Rename) | Rename tables, views, procs, functions, columns, parameters and update all references without breaking dependencies | Object Explorer context / wizard | ✅ T061+T062 [X]: SafeRenameCommand.cs uses DatabaseRenameDependencyReader (sys.sql_expression_dependencies) + DatabaseRenameScriptBuilder; dependency-aware reviewable script; dep safety achieved |
| Rename scripted object | Rename the object you're editing in the query window | `F2` (SSMS) / `Shift+F2` (VS) | 🟡 SafeRenameCommand.cs wired but no F2/Shift+F2 keybinding found in vsct or shell code; context-menu only path unchanged |

## 3. Database refactoring (heavyweight)

| Refactor | Description | Shortcut / Where | Status |
|---|---|---|---|
| Smart Rename (deep) | Rename modules/tables/columns and rewrite referencing objects; keeps `sys.sql_modules` consistent; generates a reviewable script | Object Explorer | ✅ T061+T062 [X]: DatabaseRenameScriptBuilder + DatabaseRenameDependencyReader in Engine/Refactoring/Operations/Heavyweight/; SafeRenameCommand wired in both packages |
| Split table | Move/copy columns into a new table and rewrite referencing procs/views; can introduce referential-integrity tables | Object Explorer | 🟡 generates DDL script; no auto-rewrite of dependents |
| Encapsulate as new stored procedure | Turn a selection into a new stored procedure, optionally replacing the selection with a call | `Ctrl+B, Ctrl+E` | 🟡 ExtractToProcCommand.cs:20-29 is still a placeholder stub (ShowMessageBox 'available in next update'); no Ctrl+B,Ctrl+E chord in vsct; engine ExtractToProcOperation.cs exists but not reachable |
| Inline stored procedure | Inline a procedure's body into the calling code | `Ctrl+B, Ctrl+I` | ✅ T063+T067 [X]: InlineStoredProcedureCommand.cs dispatches RefactorOperationType.InlineStoredProcedure via RefactorCommandHelper; wired in both packages (AkmlSqlPackage.cs:71) |

## 4. Refactoring queries & query results

| Refactor | Description | Where | Status |
|---|---|---|---|
| Inline EXEC | Turn an `EXEC` call into the inline query | context menu | ✅ T064+T067 [X]: InlineExecCommand initialized in both packages (AkmlSqlPackage.cs:69); InlineExecOperation.cs in Engine/Refactoring/Operations/Heavyweight/ |
| Refactor INSERT → UPDATE | Convert an `INSERT` statement into an `UPDATE` | context menu | ✅ T065+T067 [X]: InsertToUpdateCommand initialized in both packages (AkmlSqlPackage.cs:70); InsertToUpdateOperation.cs in Engine/Refactoring/Operations/Heavyweight/ |
| Script as INSERT | Turn grid results into `INSERT` statements | results grid right-click | ✅ GridCopyAsMenu / GridScriptGenerator |
| Copy as IN clause | Turn selected result values into an `IN (...)` list | results grid right-click | ✅ GridCopyAsMenu.FormatAsInClause |
| Open in Excel | Export grid results straight to Excel | results grid right-click | 🟡 XLSX export to file; no Excel auto-launch |

---

### Quick keyboard reference (this scope)
Casing `Ctrl+B,Ctrl+U` · Qualify `Ctrl+B,Ctrl+Q` · Expand wildcards `Ctrl+B,Ctrl+W` · Semicolons `Ctrl+B,Ctrl+C` · Brackets `Ctrl+B,Ctrl+B` · Inline proc `Ctrl+B,Ctrl+I` · Encapsulate `Ctrl+B,Ctrl+E` · Rename `F2` (SSMS) / `Shift+F2` (VS) · Actions list `Ctrl`.
