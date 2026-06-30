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
| List all columns after SELECT | Optional: show every column alphabetically right after `SELECT` | Options ▸ Suggestions ▸ Types of suggestion | ✅ delivered end-to-end by ColumnScope.All / GetAllTableColumns (T032); SuggestAllColumnsAfterSelect checkbox is a redundant duplicate switch (engine-unread) |
| Decrypt encrypted objects | Show creation script of encrypted objects in definition box; toggleable | Options ▸ Suggestions ▸ Behavior | 🟡 Options toggle + AppSettings EnableEncryptedDecryption present; shell LoadScriptTab (T027) does not pass flag to GetObjectDefinition IPC; engine handler exists |

## 2. Categories & object types in the box

| Feature | Description | Status |
|---|---|---|
| Category grouping | Tables / Views / Columns / Functions / Stored Procedures / Snippets / Other Suggestions | ✅ spec-030 T034; AkmlCompletionPopup.cs groups by category with header rows |
| Switch category | `Ctrl + Up` / `Ctrl + Down`, or the "All Suggestions" dropdown | ✅ spec-030 T034; CompletionController.cs MoveCategory(±1) wired to Ctrl+arrow |
| Column metadata in list | Shows data type + table/alias; primary-key and foreign-key icons | 🟡 type+PK text+table; no PK/FK icons |
| "Other Suggestions" object types | DML triggers, DDL triggers, rules, users, defaults, roles, user-defined types, full-text catalogs, system variables, join suggestions, linked-server objects, assemblies, queues, asymmetric/symmetric keys, certificates, routes, contracts, services, schemas, service bindings, event notifications, message types, synonyms, partition functions/schemes, XML schema collections, full-text stoplist | 🟡 only synonyms/schemas/system-procs/variables |
| Schema (owner) name display | Toggle owner names on/off with the arrow at the box's bottom-left; box widens to show greyed owner | ✅ spec-030 T034; AkmlCompletionPopup.cs footer toggle for _showOwnerNames; hides leading schema. prefix on display labels |

## 3. List navigation

| Feature | Shortcut | Status |
|---|---|---|
| Move one item | `Up` / `Down` (wraps top↔bottom) | ✅ |
| Move one page | `Page Up` / `Page Down` (also `Ctrl+PgUp`/`Ctrl+PgDn`) | ❌ not handled |
| Switch to/from column picker | `Ctrl + Left` / `Ctrl + Right` | ✅ spec-030 T033; WORDPREV/WORDNEXT intercepted; TriggerColumnPicker() wired |
| Move through category filters | `Ctrl + Up` / `Ctrl + Down` | ✅ spec-030 T034; MoveCategory(±1) wired — same as Switch category above |

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
| Column picker panel | List of available columns with data types to multi-select and insert | ✅ spec-030 T033; TriggerColumnPicker reuses WildcardExpansionPopup in picker mode |
| Toggle to/from picker | `Ctrl + Left` / `Ctrl + Right` from the suggestions box | ✅ spec-030 T033; Ctrl+Left/Right toggling between suggestions box and column picker wired |

## 6. Aliases (Options ▸ Inserted code ▸ Aliases)

| Feature | Description | Status |
|---|---|---|
| Auto-assign aliases | Adds an alias to each referenced table/view when columns or `*` are selected | 🟡 suggests aliases + JOIN-insert aliases, not auto-add on `*` |
| Alias generation rules | First letter; respects underscores (`TBL_Contact`→`tc`), hyphens (`hyphenated-tablename`→`ht`), case (`MixedCase`→`mc`) | 🟡 first-letter/PascalCase/underscore; no hyphen handling |
| Include / exclude `AS` | "Include AS in alias definition" toggle | ✅ spec-030 T035; AliasesPage.cs toggle; AliasProvider.IncludeAs consumed by CompletionEngine |
| Ambiguity handling | Generates extra aliases for self-joins | ✅ numbered-suffix on conflict (GenerateAlias) |
| Custom aliases | User-defined object→alias map (New / Save / Delete) | ✅ spec-030 T035; AliasesPage.cs custom map UI; AliasProvider.ObjectAliasMap consumed by CompletionEngine |
| Prefixes to ignore | Ignore a prefix (e.g. `TBL`) when generating aliases; case-insensitive; underscore optional | ✅ spec-030 T035; AliasesPage.cs prefixes text; AliasProvider.PrefixesToIgnore consumed by CompletionEngine |

## 7. Object definition box & tooltips

| Feature | Description | Where | Status |
|---|---|---|---|
| Object definition box | Appears on selecting a suggestion: **Summary** tab (columns, data types, nullability) + **Script** tab (creation script, copyable) | Options ▸ Suggestions ▸ Behavior (Show object definitions) | ✅ spec-030 T027; LoadScriptTab fetches GetObjectDefinition IPC; real DDL from sys.sql_modules/schema cache |
| Object tooltips | Hover an object to see its definition; clickable for tables/views/procs | Options ▸ Suggestions ▸ Behavior (Show tooltips for) | ✅ spec-030 T025; QuickInfoSource.cs sends RequestQuickInfo IPC with cache-and-retrigger bridge |
| Parameter tooltips | Hover/parameter hints, including for built-in functions | Options ▸ Tooltips | ✅ spec-030 T026; SignatureHelpSource.cs sends RequestSignatureHelp IPC; triggered on '('/',' in CompletionController |
| Dependencies tooltip | For columns: click tooltip to see objects referencing / referenced by the column | Options ▸ Suggestions ▸ Behavior | ❌ only FK shown, no dependency graph |
| Fully-qualified name tooltip | Shows the fully qualified object name on hover | — | ✅ spec-030 T025; QuickInfoSource renders Header with qualified name from QuickInfoResponse |

## 8. Temp-table IntelliSense (with documented limits)

| Feature | Description | Status |
|---|---|---|
| `#temp` table suggestions | Columns suggested for temp tables | ✅ spec-030 T029; CompletionEngine populates AvailableTempTables; ColumnProvider serves temp columns |
| Structure captured at first parse | Reads structure at `CREATE TABLE` / `SELECT INTO` | ✅ spec-030 T029; TempTableTracker.TrackTempTables called with prefix-parse recovery for mid-edit cursor |
| Known limitation | Later `ALTER TABLE` columns may not be re-recognized in the same script (by design) — recommendation is to define columns up front / use `SELECT INTO` | 🟡 feature now reachable; limitation still by design; EnableTempTableIntellisense not gating engine (always on) |

## 9. Connections / suggestion scope (Options ▸ Suggestions ▸ Connections)

| Feature | Description | Status |
|---|---|---|
| Control databases/schemas suggested | Limit which DBs/schemas produce suggestions | ✅ spec-030 T036; ConnectionScope threaded to ObjectProvider via CompletionEngine; ConnectionScopeTests +8 |
| Linked-server suggestions toggle | "Load suggestions for linked servers" (also avoids needing master DB access) | 🟡 spec-030 T036; includeLinkedServers threaded but inert — schema cache loads no linked-server objects; forward-looking |

---

### Quick keyboard reference (this scope)
`Ctrl+Space` show · `Ctrl+Shift+P` toggle · `Ctrl+Shift+D` refresh · `Ctrl+←/→` column picker · `↑/↓` move · `Ctrl+↑/↓` categories.
