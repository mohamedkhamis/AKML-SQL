# 02 — Formatting & Styles

Scope: the **Format SQL** command, formatting styles (create/edit/activate/share/import/export), the **Edit Style** option groups, and per-block formatting control.

Status legend: ✅ done · 🟡 partial · ❌ missing · ➖ out of scope
**[verify in UI]** = comes from the live Edit Style dialog, not the public docs — confirm exact label/availability in SQL Prompt 11.

---

## 1. Running formatting

| Feature | Description | Where / Shortcut | Status |
|---|---|---|---|
| Format SQL (whole doc) | Apply the active style to the whole query | `Ctrl + K, Ctrl + Y`; SQL Prompt menu; right-click | ✅ Ctrl+K,Y; FormatDocumentCommand → pipeline |
| Format selection only | Highlight a fragment then Format SQL (also from Actions list) | same | ✅ SelectionFormatter expands to statement, formats |
| Unformat | Remove *all* whitespace formatting from a selection | Actions list ▸ Unformat | ✅ UnformatCommand + UnformatOperation; Ctrl+B,Ctrl+U |
| Disable formatting for a block | Wrap selected code in markers so Format SQL skips it | `Ctrl` ▸ Actions ▸ "Disable formatting for selected text" | ✅ NoformatScanner honors noformat + SQL-Prompt directives |
| Formatting error popup | If code can't be parsed/formatted, shows reasons | — | ❌ diagnostics returned but only logged, no popup |
| Format-time actions | Format SQL can also run selected refactor actions (casing, semicolons, qualification, wildcard expansion, brackets) | Options ▸ Format ▸ Styles | ❌ FormatActionConfig not read by pipeline; standalone only |
| Syntax-error guard | Won't format code with syntax errors (message shown) | — | 🟡 SemanticValidator returns original unchanged; no message shown |

## 2. Managing styles

| Feature | Description | Where | Status |
|---|---|---|---|
| Edit Formatting Styles dialog | Central place to create/copy/edit/activate styles | SQL Prompt ▸ Edit Formatting Styles | 🟡 editor + ProfileEditorDialog reachable; no create/copy/activate hub |
| Built-in Redgate styles | Starter styles (e.g. Default, Collapsed, Indented, etc.) | Redgate styles list | ✅ 5 read-only .akmlstyle built-ins shipped (AKML-authored) |
| Create a style | "+ Create a style" — name it and pick a base style | Your Styles | 🟡 ProfileManager.Save backend; editor Create button deferred |
| Copy a style | Copy from a Redgate or your own style (vertical ellipsis ▸ Copy, or double-click a Redgate style) | — | 🟡 ProfileManager.Duplicate backend; editor Copy button deferred |
| Edit a style | Open the Style Options (left-hand pane) | ellipsis ▸ Edit | ✅ ProfileEditorDialog: category tree + controls + Save |
| Active style | The style used by Format SQL; marked with an indicator | ellipsis ▸ Set as active | 🟡 ActiveProfile config field honored; no set-active UI/indicator |
| Switch active style | From Options, the right-click **Active Style** menu, or the styles window | right-click ▸ Active Style | 🟡 config-only; no Options/right-click active-style picker |
| Multiple styles | Maintain many styles, switch which is active | — | ✅ ProfileManager.List (custom + built-in, shadowing) |
| Import old styles | Pre-v8 styles auto-imported with "(old)" suffix (may shift due to new options) | — | ❌ no pre-v8 auto-import / "(old)" suffix logic |
| Update pre-10.5 style files | Migration path for older style files | docs: "Updating style files from before 10.5" | 🟡 imports current .sqlpromptstylev2; no version-specific migration |

## 3. Sharing / import / export styles ("JSON upload")

> SQL Prompt stores a style as a single file you can share; on export it produces a style file that another user imports. (The on-disk schema is XML/JSON-like; AKML's analog is your `.akmlstyle`.)

| Feature | Description | Where | Status |
|---|---|---|---|
| Export a style to a file | Save a style as a shareable file | Edit Formatting Styles ▸ export | ✅ ProfileManager.Export (.akmlstyle) + SqlPromptExporter (msg 29) |
| Import a style file | Load a colleague's/team style file | Edit Formatting Styles ▸ import | ✅ HandleProfileImport: sqlpromptstylev2 + akmlstyle |
| Shared folder for styles | Point SQL Prompt at a network/Dropbox folder so a team shares one style set | docs: "Using a shared folder for formatting styles" | ❌ no shared-folder style location setting |
| Share via Redgate Platform | Cloud team spaces to share styles (Toolbelt Essentials subscription only) | Redgate Platform | ➖ Redgate-cloud-only; out of scope for AKML |
| Command-line / bulk formatting | Apply a style across many files/objects via CLI, PowerShell, or batch | CLI formatter; Bulk Actions | ✅ AkmlSql.Formatter CLI + BulkFormatter (parallel, in-place) |
| Import/Export *all* options | Export/import the entire SQL Prompt options set (incl. styles) | Options ▸ Import/Export buttons | ✅ SettingsWindow Import/Export serialize whole AppSettings JSON |

## 4. Edit Style — option groups (documented)

The docs confirm a style controls **DML statements, DDL statements, CASE statements, JOINs, CTEs, casing, wrapping, indentation, lists, parentheses, semicolons, and square brackets**, "and more." Below are the groups with the known granular toggles to verify in the live dialog.

### 4.1 Global / whitespace
| Option | Notes | Status |
|---|---|---|
| Indentation: tabs vs spaces, size | [verify in UI] | ✅ TextEmitter.AppendIndent honors TabStyle + TabSize |
| Wrapping & max line length | wrap long statements/lists at N chars [verify in UI] | 🟡 MaxLineWidth unused by pipeline; only collapse helpers |
| Spacing around operators/commas | [verify in UI] | ✅ LayoutEngine SpaceAroundOperators + LineBreakDecider SpaceAfterComma |
| Empty lines between statements/blocks | [verify in UI] | ✅ LineBreakDecider EmptyLineBetweenStatements + EmptyLineBeforeGo |
| Trailing whitespace handling | [verify in UI] | ✅ TextEmitter.RemoveTrailingWhitespace + finalNewline |

### 4.2 Casing (also runnable as an Action)
| Option | Notes | Status |
|---|---|---|
| Keywords casing (UPPER/lower/Capitalize) | [verify in UI] | ✅ CasingEngine ReservedKeywords (UPPER/lower/Pascal/camel) |
| Built-in function casing | [verify in UI] | ✅ CasingEngine BuiltInFunctions (function-name set) |
| Built-in data-type casing | [verify in UI] | ✅ CasingEngine BuiltInDataTypes (data-type set) |
| Global variable casing | [verify in UI] | 🟡 GlobalVariables/SystemObjects ignored by DetermineCasingMode |
| Apply Casing Options action | `Ctrl+B, Ctrl+U` | 🟡 CasingOnlyCommand exists; HandleFormatAction lacks action-0 dispatch |

### 4.3 Lists & commas
| Option | Notes | Status |
|---|---|---|
| Leading vs trailing commas | [verify in UI] | 🟡 LineBreakDecider emits trailing only; leading in unwired ListRules |
| One item per line vs packed | [verify in UI] | 🟡 SELECT one-per-line wired; packed/list-level only ListRules |
| Align list items / column alignment | [verify in UI] | 🟡 AlignmentCalculator.AlignSelectAliases not wired into pipeline |

### 4.4 Parentheses
| Option | Notes | Status |
|---|---|---|
| Parenthesis placement / wrapping | [verify in UI] | 🟡 ParenthesisRules + paren collapse not wired into pipeline |
| Spacing inside parentheses | [verify in UI] | 🟡 SpaceBeforeParentheses wired; SpaceInside not honored |

### 4.5 Data statements (DML)
| Option group | Notes | Status |
|---|---|---|
| SELECT layout | column list wrapping, FROM/JOIN/WHERE/GROUP BY/HAVING/ORDER BY placement [verify in UI] | ✅ LineBreakDecider: items + FROM/WHERE/GROUP/HAVING/ORDER |
| INSERT layout | column & VALUES list formatting [verify in UI] | 🟡 Into/Values/InsertColumnListFormat not in LineBreakDecider |
| UPDATE layout | SET list formatting [verify in UI] | 🟡 SetOnNewLine honored only by unwired DmlRules |
| DELETE layout | [verify in UI] | 🟡 DeleteFromOnSameLine honored only by unwired DmlRules |
| MERGE layout | [verify in UI] | 🟡 MergeWhenOnNewLine honored only by unwired DmlRules |

### 4.6 Clauses
| Option group | Notes | Status |
|---|---|---|
| FROM / JOIN | join keyword placement, ON-condition layout [verify in UI] | ✅ LineBreakDecider: FromOnNewLine + JOIN + ON-condition |
| WHERE | AND/OR placement, indentation [verify in UI] | ✅ LineBreakDecider: WhereOnNewLine + AndOrNewLine before |
| GROUP BY | each column on a new line; **add non-aggregated SELECT columns to GROUP BY** (your example) [verify in UI] | ✅ GroupByOnNewLine wired + AddGroupByColumnsOperation action |
| HAVING | [verify in UI] | ✅ LineBreakDecider HavingOnNewLine |
| ORDER BY | [verify in UI] | ✅ LineBreakDecider OrderByOnNewLine |

### 4.7 JOINs (documented group)
| Option | Notes | Status |
|---|---|---|
| JOIN alignment / new line per join | [verify in UI] | 🟡 OnNewLine wired; AlignJoinKeyword only in unwired JoinRules |
| ON / AND condition indentation | [verify in UI] | ✅ LineBreakDecider OnConditionNewLine + indent; AND/OR before |

### 4.8 CASE expressions (documented group)
| Option | Notes | Status |
|---|---|---|
| WHEN/THEN/ELSE/END placement & indentation | [verify in UI] | 🟡 ControlFlowRules.ApplyCaseRules not wired into pipeline |

### 4.9 CTEs (documented group)
| Option | Notes | Status |
|---|---|---|
| WITH / column-list layout, comma placement | [verify in UI] | 🟡 ControlFlowRules.ApplyCteRules not wired into pipeline |

### 4.10 DDL statements (documented group)
| Option group | Notes | Status |
|---|---|---|
| CREATE TABLE | column/constraint alignment, data-type alignment [verify in UI] | 🟡 DdlRules + AlignmentCalculator not wired into pipeline |
| CREATE PROCEDURE / FUNCTION | parameter list layout [verify in UI] | 🟡 param-alignment in unwired AlignmentCalculator/DdlRules |
| CREATE VIEW / INDEX / TRIGGER | [verify in UI] | 🟡 no dedicated DDL layout in wired pipeline path |

### 4.11 Variables, parameters, control flow
| Option group | Notes | Status |
|---|---|---|
| DECLARE / SET layout | [verify in UI] | 🟡 no DECLARE/SET layout in wired pipeline path |
| BEGIN…END, IF, WHILE, TRY/CATCH indentation | [verify in UI] | 🟡 ControlFlowRules begin/if/try passes not wired into pipeline |

### 4.12 Subqueries / derived tables
| Option | Notes | Status |
|---|---|---|
| Subquery indentation & wrapping | [verify in UI] | 🟡 basic paren indent only; subquery options in unwired rules |

### 4.13 Comments
| Option | Notes | Status |
|---|---|---|
| Comment alignment / preservation | [verify in UI] | 🟡 AstAnnotator preserves comments; CommentsOptions alignment unwired |

### 4.14 Style-level Actions (run as part of Format)
| Option | Shortcut | Status |
|---|---|---|
| Insert semicolons | `Ctrl+B, Ctrl+C` | ❌ class scaffold exists, no working path |
| Qualify object names | `Ctrl+B, Ctrl+Q` | ❌ class scaffold exists, no working path |
| Expand wildcards | `Ctrl+B, Ctrl+W` | ❌ action unwired; Tab-on-* popup separate |
| Add/remove square brackets | `Ctrl+B, Ctrl+B` | ❌ class scaffold exists, no working path |

## 5. Style preview (verify in UI)
| Feature | Description | Status |
|---|---|---|
| Sample SQL preview | Live preview of options on a sample query | ✅ FormatStylesEditor QueuePreviewAsync (debounced FormatPreview IPC) |
| Current-query preview | Preview options against your current editor content | ❌ preview uses fixed sample only; no active-document wire |

---

> **Recommended next step for true parity here:** export one SQL Prompt 11 style and one of your `.akmlstyle` files and let me diff the two schemas field-by-field. The docs alone do not expose every toggle; a schema diff is the only way to guarantee the full ≈200-option list is captured (this matches the gap analysis approach in your Phase 3.1 Formatter PRD).
