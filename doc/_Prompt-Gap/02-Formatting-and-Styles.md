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
| Formatting error popup | If code can't be parsed/formatted, shows reasons | — | ✅ T018: FormatFailureNotifier + VsShellUtilities.ShowMessageBox in FormatDocumentCommand/FormatSelectionCommand |
| Format-time actions | Format SQL can also run selected refactor actions (casing, semicolons, qualification, wildcard expansion, brackets) | Options ▸ Format ▸ Styles | ❌ FormatActionConfig not read by pipeline; standalone only |
| Syntax-error guard | Won't format code with syntax errors (message shown) | — | ✅ T018: FormatFailureNotifier branches on Success==false vs ValidationPassed; message shown in both hosts |

## 2. Managing styles

| Feature | Description | Where | Status |
|---|---|---|---|
| Edit Formatting Styles dialog | Central place to create/copy/edit/activate styles | SQL Prompt ▸ Edit Formatting Styles | ✅ T020: New/Copy/Set-Active/Export buttons added to FormatStylesEditorWindow left panel |
| Built-in Redgate styles | Starter styles (e.g. Default, Collapsed, Indented, etc.) | Redgate styles list | ✅ 5 read-only .akmlstyle built-ins shipped (AKML-authored) |
| Create a style | "+ Create a style" — name it and pick a base style | Your Styles | ✅ T020: New button via DuplicateProfile IPC (32/132) wrapping ProfileManager.Duplicate |
| Copy a style | Copy from a Redgate or your own style (vertical ellipsis ▸ Copy, or double-click a Redgate style) | — | ✅ T020: Copy button via DuplicateProfile IPC; list refreshes + new style auto-selected |
| Edit a style | Open the Style Options (left-hand pane) | ellipsis ▸ Edit | ✅ ProfileEditorDialog: category tree + controls + Save |
| Active style | The style used by Format SQL; marked with an indicator | ellipsis ▸ Set as active | ✅ T020/T021: Set-Active writes AppSettings.Formatter.ActiveProfile + StatusBarManager idle text shows active style |
| Switch active style | From Options, the right-click **Active Style** menu, or the styles window | right-click ▸ Active Style | ✅ T021: Active style dropdown on FormattingPage (async-populated from ProfileList IPC) |
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
| Wrapping & max line length | wrap long statements/lists at N chars [verify in UI] | ✅ T012: LineWrapper wired as last post-collapse finalization pass in FormatterPipeline |
| Spacing around operators/commas | [verify in UI] | ✅ LayoutEngine SpaceAroundOperators + LineBreakDecider SpaceAfterComma |
| Empty lines between statements/blocks | [verify in UI] | ✅ LineBreakDecider EmptyLineBetweenStatements + EmptyLineBeforeGo |
| Trailing whitespace handling | [verify in UI] | ✅ TextEmitter.RemoveTrailingWhitespace + finalNewline |

### 4.2 Casing (also runnable as an Action)
| Option | Notes | Status |
|---|---|---|
| Keywords casing (UPPER/lower/Capitalize) | [verify in UI] | ✅ CasingEngine ReservedKeywords (UPPER/lower/Pascal/camel) |
| Built-in function casing | [verify in UI] | ✅ CasingEngine BuiltInFunctions (function-name set) |
| Built-in data-type casing | [verify in UI] | ✅ CasingEngine BuiltInDataTypes (data-type set) |
| Global variable casing | [verify in UI] | ✅ CasingEngine @@-prefix split routes globals through GlobalVariables option (fixed 2026-06-23); +4 CasingEngineTests |
| Apply Casing Options action | `Ctrl+B, Ctrl+U` | ✅ T016: CasingOnly (action-0) dispatched in FormatRequestHandler.cs:200; CasingOnlyCommand sends it |

### 4.3 Lists & commas
| Option | Notes | Status |
|---|---|---|
| Leading vs trailing commas | [verify in UI] | ✅ T011: ApplyCommaPosition in ListRules.cs:29 wired after break-affecting passes |
| One item per line vs packed | [verify in UI] | ✅ T011: ListRules fully wired via RuleEngine.DefaultOrder in FormatterPipeline |
| Align list items / column alignment | [verify in UI] | ✅ T011: ListRules.AlignAliases called at FormatterPipeline.cs:47 post-collapse finalization |

### 4.4 Parentheses
| Option | Notes | Status |
|---|---|---|
| Parenthesis placement / wrapping | [verify in UI] | 🟡 ParenthesisRules + paren collapse not wired into pipeline |
| Spacing inside parentheses | [verify in UI] | ✅ T011: ApplySpaceInside in ParenthesisRules.cs:27 wired via RuleEngine; spaceInsideParentheses in profiles |

### 4.5 Data statements (DML)
| Option group | Notes | Status |
|---|---|---|
| SELECT layout | column list wrapping, FROM/JOIN/WHERE/GROUP BY/HAVING/ORDER BY placement [verify in UI] | ✅ LineBreakDecider: items + FROM/WHERE/GROUP/HAVING/ORDER |
| INSERT layout | column & VALUES list formatting [verify in UI] | ✅ T008: DmlRules (INTO/VALUES) delta-rework landed, RuleEngine.DefaultOrder on in production |
| UPDATE layout | SET list formatting [verify in UI] | ✅ T008: DmlRules SET delta-rework complete, all DmlRules verified correct |
| DELETE layout | [verify in UI] | ✅ T008: DmlRules including DeleteFromOnSameLine in production via RuleEngine.DefaultOrder |
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
| JOIN alignment / new line per join | [verify in UI] | ✅ T010: JoinLayoutTests +3; JOIN modifier as IsListBoundary; AlignJoinKeyword via DdlRules now wired |
| ON / AND condition indentation | [verify in UI] | ✅ LineBreakDecider OnConditionNewLine + indent; AND/OR before |

### 4.8 CASE expressions (documented group)
| Option | Notes | Status |
|---|---|---|
| WHEN/THEN/ELSE/END placement & indentation | [verify in UI] | ✅ T008/T009: ApplyCaseRules + ApplyCaseEndAlignment wired in ControlFlowRules.cs:28,36; WhenAlignment/EndAlignment sentinel logic; 614/614 tests green |

### 4.9 CTEs (documented group)
| Option | Notes | Status |
|---|---|---|
| WITH / column-list layout, comma placement | [verify in UI] | ✅ T009: ApplyCteRules wired (ControlFlowRules.cs:29); CTE paren-boundary + main-SELECT fixes shipped |

### 4.10 DDL statements (documented group)
| Option group | Notes | Status |
|---|---|---|
| CREATE TABLE | column/constraint alignment, data-type alignment [verify in UI] | ✅ T010: DdlRules wired via RuleEngine.DefaultOrder; DdlAlignmentLayoutTests +4; nested type-arg + parameterAlignment fixed |
| CREATE PROCEDURE / FUNCTION | parameter list layout [verify in UI] | ✅ T010: param-alignment aligned wired; DdlRules one-per-line with datatype+default alignment |
| CREATE VIEW / INDEX / TRIGGER | [verify in UI] | ✅ T008/T010: DdlRules wired for all DDL via RuleEngine.DefaultOrder; 0/78 DdlRules golden drift |

### 4.11 Variables, parameters, control flow
| Option group | Notes | Status |
|---|---|---|
| DECLARE / SET layout | [verify in UI] | 🟡 no DECLARE/SET layout in wired pipeline path |
| BEGIN…END, IF, WHILE, TRY/CATCH indentation | [verify in UI] | ✅ T009: BuildNestedStatementStartSet added; NestedStatementLayoutTests +4; BEGIN-cram fixed for proc/function/trigger/TryCatch/BeginEnd |

### 4.12 Subqueries / derived tables
| Option | Notes | Status |
|---|---|---|
| Subquery indentation & wrapping | [verify in UI] | ✅ T009: FindListEnd paren-depth aware; CTE/subquery enclosing-paren boundary fixed; T011 ApplyCollapseShortStatements depth-0 anchor |

### 4.13 Comments
| Option | Notes | Status |
|---|---|---|
| Comment alignment / preservation | [verify in UI] | 🟡 AstAnnotator preserves comments; CommentsOptions alignment unwired |

### 4.14 Style-level Actions (run as part of Format)
| Option | Shortcut | Status |
|---|---|---|
| Insert semicolons | `Ctrl+B, Ctrl+C` | ✅ T016: InsertSemicolonsAction dispatched at FormatRequestHandler.cs:201; standalone command working |
| Qualify object names | `Ctrl+B, Ctrl+Q` | 🟡 T016: QualifyObjectNamesAction dispatched at FormatRequestHandler.cs:204 but returns schema-stub message (requires schema cache) |
| Expand wildcards | `Ctrl+B, Ctrl+W` | 🟡 T016: ExpandWildcardsAction dispatched at FormatRequestHandler.cs:203 but returns schema-stub message (requires schema cache) |
| Add/remove square brackets | `Ctrl+B, Ctrl+B` | ✅ T016: ToggleBracketsAction dispatched at FormatRequestHandler.cs:205,206; AddSquareBrackets/RemoveSquareBrackets working |

## 5. Style preview (verify in UI)
| Feature | Description | Status |
|---|---|---|
| Sample SQL preview | Live preview of options on a sample query | ✅ FormatStylesEditor QueuePreviewAsync (debounced FormatPreview IPC) |
| Current-query preview | Preview options against your current editor content | ✅ T019: FormatPreviewSource enum + Sample/Current-query radio toggle; FormatStylesEditorWindow.cs:588-589; TryGetActiveDocumentText at launch |

---

> **Recommended next step for true parity here:** export one SQL Prompt 11 style and one of your `.akmlstyle` files and let me diff the two schemas field-by-field. The docs alone do not expose every toggle; a schema diff is the only way to guarantee the full ≈200-option list is captured (this matches the gap analysis approach in your Phase 3.1 Formatter PRD).
