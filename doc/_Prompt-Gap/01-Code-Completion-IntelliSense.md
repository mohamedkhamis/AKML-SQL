# 01 — Code Completion & IntelliSense

Scope: the as-you-type suggestions box and everything around it (column picker, aliases, tooltips, object definition box, keyboard navigation).

Status legend: ✅ done · 🟡 partial · ❌ missing · ➖ out of scope

---

## 1. Suggestions box — core

| Feature | Description | Where / Shortcut | Status |
|---|---|---|---|
| As-you-type suggestions | Context-aware popup of tables, views, columns, schemas, databases, functions, procedures, keywords, etc. | Auto on type | ✅ |
| Manual trigger | Show suggestions on demand | `Ctrl + Space` | ✅ intercepts native COMPLETEWORD/898 |
| Toggle suggestions on/off | Globally enable/disable suggestions | `Ctrl + Shift + P` | ❌ Options toggle inert; no hotkey |
| Refresh suggestions | Re-read schema cache | `Ctrl + Shift + D` | ✅ |
| Prefix match | e.g. typing `ad` → `Address` | — | ✅ |
| CamelCase / compound match | typing `bea` → `BusinessEntityAddress` | — | ✅ |
| Mid-string match | typing `en` → `BusinessEntity` (matches substring) | — | ✅ |
| Ranked suggestions | Most-relevant items floated to top using type, closeness, and your usage history; toggleable | Options ▸ Suggestions ▸ Behavior | 🟡 type+rowcount+fuzzy, no usage history, not toggleable |
| Filtering as you type | Narrows list with each keystroke | — | ✅ |
| Close without inserting | `Esc`, or click elsewhere, or `Enter` when nothing selected | — | ✅ Esc/space/punctuation dismiss |
| Resizable box (remembered) | Drag resize handle; size persists across sessions | — | 🟡 resizable but size not persisted |
| Semi-transparent on demand | Hold `Ctrl` to see code behind the box; toggleable | Options ▸ Suggestions ▸ Behavior | 🟡 Ctrl-held works, not toggleable |
| Context-aware ordering | After `USE` → databases first; after `FROM` → tables→views→schemas→DBs; in `CREATE TABLE` after a column → data types first | — | 🟡 USE→db works; no FROM/datatype ordering |
| List all columns after SELECT | Optional: show every column alphabetically right after `SELECT` | Options ▸ Suggestions ▸ Types of suggestion | ❌ setting exists, never consumed |
| Decrypt encrypted objects | Show creation script of encrypted objects in definition box; toggleable | Options ▸ Suggestions ▸ Behavior | ❌ engine handler exists; shell never calls it |

## 2. Categories & object types in the box

| Feature | Description | Status |
|---|---|---|
| Category grouping | Tables / Views / Columns / Functions / Stored Procedures / Snippets / Other Suggestions | 🟡 flat list with type badges, no grouping |
| Switch category | `Ctrl + Up` / `Ctrl + Down`, or the "All Suggestions" dropdown | ❌ no categories to switch |
| Column metadata in list | Shows data type + table/alias; primary-key and foreign-key icons | 🟡 type+PK text+table; no PK/FK icons |
| "Other Suggestions" object types | DML triggers, DDL triggers, rules, users, defaults, roles, user-defined types, full-text catalogs, system variables, join suggestions, linked-server objects, assemblies, queues, asymmetric/symmetric keys, certificates, routes, contracts, services, schemas, service bindings, event notifications, message types, synonyms, partition functions/schemes, XML schema collections, full-text stoplist | 🟡 only synonyms/schemas/system-procs/variables |
| Schema (owner) name display | Toggle owner names on/off with the arrow at the box's bottom-left; box widens to show greyed owner | ❌ no owner-name toggle |

## 3. List navigation

| Feature | Shortcut | Status |
|---|---|---|
| Move one item | `Up` / `Down` (wraps top↔bottom) | ✅ |
| Move one page | `Page Up` / `Page Down` (also `Ctrl+PgUp`/`Ctrl+PgDn`) | ❌ not handled |
| Switch to/from column picker | `Ctrl + Left` / `Ctrl + Right` | ❌ no column picker |
| Move through category filters | `Ctrl + Up` / `Ctrl + Down` | ❌ no category filters |

## 4. Insertion behavior

| Feature | Description | Where | Status |
|---|---|---|---|
| Insertion keys | Keys that commit the selected suggestion (default `Enter` and `Tab`); customizable set | Options ▸ Main/Behavior | 🟡 Enter/Tab fixed; Space/Dot toggleable only |
| Tab-only cases | Where the name could be an alias, only `Tab` inserts | — | ❌ Enter and Tab commit identically |
| Insert at correct indent | Snippet/suggestion inserted at the current indentation level | — | 🟡 wildcard/snippet indent; plain commit span-replace |
| Expand `SELECT *` to columns | Press `Tab` after `*` to list all columns (formatted per style) | — | ✅ checkbox popup, multi-line insert |
| Auto-qualify on insert | Qualifies object to owner / column to alias when needed (e.g. JOIN conditions, ambiguous columns, cross-DB / linked-server) | Options ▸ Inserted code ▸ Qualification | 🟡 schema + multi-table column qualify; no cross-DB |
| JOIN condition completion | Suggests/fills `ON` conditions using FK metadata | Options ▸ Suggestions ▸ Join conditions | ✅ JoinOnFkProvider + JoinProvider |
| GROUP BY assistance | Helps fill in GROUP BY clauses (e.g. adding the needed non-aggregated columns) — *your example* | Suggestions box | ✅ SmartGroupByProvider |

## 5. Column picker

| Feature | Description | Status |
|---|---|---|
| Column picker panel | List of available columns with data types to multi-select and insert | ❌ ColumnPickerWindow not implemented |
| Toggle to/from picker | `Ctrl + Left` / `Ctrl + Right` from the suggestions box | ❌ no picker to toggle |

## 6. Aliases (Options ▸ Inserted code ▸ Aliases)

| Feature | Description | Status |
|---|---|---|
| Auto-assign aliases | Adds an alias to each referenced table/view when columns or `*` are selected | 🟡 suggests aliases + JOIN-insert aliases, not auto-add on `*` |
| Alias generation rules | First letter; respects underscores (`TBL_Contact`→`tc`), hyphens (`hyphenated-tablename`→`ht`), case (`MixedCase`→`mc`) | 🟡 first-letter/PascalCase/underscore; no hyphen handling |
| Include / exclude `AS` | "Include AS in alias definition" toggle | ❌ no AS toggle |
| Ambiguity handling | Generates extra aliases for self-joins | ✅ numbered-suffix on conflict (GenerateAlias) |
| Custom aliases | User-defined object→alias map (New / Save / Delete) | ❌ no custom-alias map |
| Prefixes to ignore | Ignore a prefix (e.g. `TBL`) when generating aliases; case-insensitive; underscore optional | ❌ no prefixes-to-ignore |

## 7. Object definition box & tooltips

| Feature | Description | Where | Status |
|---|---|---|---|
| Object definition box | Appears on selecting a suggestion: **Summary** tab (columns, data types, nullability) + **Script** tab (creation script, copyable) | Options ▸ Suggestions ▸ Behavior (Show object definitions) | 🟡 Summary works; Script tab shows description, not DDL |
| Object tooltips | Hover an object to see its definition; clickable for tables/views/procs | Options ▸ Suggestions ▸ Behavior (Show tooltips for) | ❌ QuickInfoSource is a stub TODO |
| Parameter tooltips | Hover/parameter hints, including for built-in functions | Options ▸ Tooltips | ❌ SignatureHelpSource stub; shell never calls IPC |
| Dependencies tooltip | For columns: click tooltip to see objects referencing / referenced by the column | Options ▸ Suggestions ▸ Behavior | ❌ only FK shown, no dependency graph |
| Fully-qualified name tooltip | Shows the fully qualified object name on hover | — | ❌ hover path (QuickInfoSource) is a stub |

## 8. Temp-table IntelliSense (with documented limits)

| Feature | Description | Status |
|---|---|---|
| `#temp` table suggestions | Columns suggested for temp tables | ❌ TempTableTracker exists but unwired |
| Structure captured at first parse | Reads structure at `CREATE TABLE` / `SELECT INTO` | ❌ tracker built+tested but never called |
| Known limitation | Later `ALTER TABLE` columns may not be re-recognized in the same script (by design) — recommendation is to define columns up front / use `SELECT INTO` | ❌ moot — temp completion unreachable |

## 9. Connections / suggestion scope (Options ▸ Suggestions ▸ Connections)

| Feature | Description | Status |
|---|---|---|
| Control databases/schemas suggested | Limit which DBs/schemas produce suggestions | ❌ no DB/schema scope filter |
| Linked-server suggestions toggle | "Load suggestions for linked servers" (also avoids needing master DB access) | ❌ no linked-server suggestions |

---

### Quick keyboard reference (this scope)
`Ctrl+Space` show · `Ctrl+Shift+P` toggle · `Ctrl+Shift+D` refresh · `Ctrl+←/→` column picker · `↑/↓` move · `Ctrl+↑/↓` categories.
