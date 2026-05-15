# AKML SQL — Development Progress Log

## Overview

This document tracks the development progress, complete feature inventory, issues encountered, root causes identified, and solutions applied during the AKML SQL extension development. It serves as institutional knowledge for future sessions.

---

## Complete Feature Inventory (as of 2026-04-03)

> **Codebase**: 917 C# files | 6 IDE targets | 915 unit tests | 130+ analysis rules
> **100% SQL Prompt Core feature parity achieved** (specs 010 + 011 + 013)

### 1. IntelliSense & Code Completion (9 providers, 9 shell files)
- Custom dark-themed completion popup (replicates SQL Prompt design, One Dark icon palette for all 12 object types with semi-transparent badge backgrounds)
- **Ctrl transparency**: Hold Ctrl to make popup semi-transparent (see code behind)
- Auto-trigger on typing with configurable debounce delay
- Dot-trigger for table.column completion
- Fuzzy matching (substring + prefix via FuzzyMatcher)
- **9 completion providers**: Column, Alias, Object, Keyword, Snippet, Variable, JOIN, QuickInfo, Signature
- Column provider with CTE, temp table, derived table, and alias resolution
- Object provider with schema-qualified tables, views, procedures, functions
- Keyword provider with configurable casing (UPPER/lower/PascalCase/AsIs)
- Snippet integration in completion list (personal + team + built-in)
- Variable provider (@local variables and @@system variables in scope)
- Quick Info tooltips on hover (column types, nullability, PK/FK badges, row count)
- Function signature help with parameter names, types, and overloads
- Wildcard expansion popup (SELECT * to explicit column list with inline preview)
- Object Definition Panel (Summary/Script tabs alongside completion popup via QuickInfo IPC)
- Auto alias suggestion on table completion
- JOIN assist (FK-based ON condition suggestions)
- Schema status indicator (cache load progress in status bar)
- CamelCase dictionary for identifier splitting (5,000+ word list)

### 2. Code Formatting (21 commands + profile system + formatting directives)

**Format Commands (17):**
1. Format Document (Ctrl+K, Y) — full document formatting
2. Format Selection — selection-only formatting
3. Casing Only (Ctrl+B, Ctrl+C) — keyword casing without layout changes
4. Expand Wildcards (Ctrl+B, Ctrl+W) — SELECT * to column list
5. Expand Insert Columns — with **metadata comments** (type, nullability, defaults as inline comments)
6. Expand Update Columns — UPDATE SET with all columns
7. Expand Exec Parameters — sp_executesql parameter expansion
8. Add GROUP BY Columns — missing column detection
9. Insert Semicolons (Ctrl+B, Ctrl+C) — statement terminator insertion
10. Remove Semicolons — statement terminator removal
11. Toggle Brackets — add/remove [square brackets]
12. Toggle AS Keywords — add/remove AS in aliases
13. Qualify Object Names (Ctrl+B, Ctrl+Q) — add dbo. schema prefix
14. Convert Old-Style JOINs — WHERE-clause joins to ANSI syntax
15. Replace Deprecated Syntax — modernize deprecated patterns
16. Encapsulate in BEGIN/END — wrap statements

**Format Triggers (3):**
17. Format on Save — auto-format when document saved
18. Format on Paste — auto-format pasted code
19. Format on Delimiter — auto-format on semicolons/GO

**Convert Operations (1):**
20. **Convert sp_executesql to Static SQL** — substitutes parameter values into template (string-literal-aware)

**New in spec 013:**
21. **Unformat Document** (Ctrl+B, Ctrl+U) — strips all formatting whitespace to minimal single-line SQL

**Formatting Region Directives:**
- `-- noformat` / `-- endnoformat` (original syntax)
- `-- AKML formatting off` / `-- AKML formatting on` (new alias)
- `-- SQL Prompt formatting off` / `-- SQL Prompt formatting on` (migration compatibility)
- Block comment variants: `/* noformat */`, `/* AKML formatting off */`, etc.
- All three syntaxes are interchangeable and case-insensitive

**Profile System:**
- `.akmlstyle` JSON format profiles with 50+ formatting options
- SQL Prompt `.sqlpromptstyle` profile importer
- Profile editor dialog with real-time SQL preview
- Profile selector dropdown in toolbar

### 3. Code Analysis (130 rules across 8 categories)

**Infrastructure (6 shell files):**
- Real-time AST-based static analysis engine with debounced triggers
- Diagnostic squiggles (wavy underlines — green for warnings, red for errors)
- VS Error List integration
- Lightbulb quick-fix suggestions with contextual refactoring actions
- Analysis suppression (`-- noqa: RULE_ID` inline comments)
- Bulk analysis command (multi-file analysis across folders)

**Rule Categories:**

| Category | Prefix | Count | Examples |
|----------|--------|-------|---------|
| Best Practices | BP | 28 | @@IDENTITY→SCOPE_IDENTITY, TRY/CATCH, NULL comparison, GOTO, magic numbers |
| Performance | PE | 31 | SELECT *, SET NOCOUNT, leading wildcard LIKE, correlated subquery, cursor usage |
| Security | SE | 20 | SQL injection, hard-coded passwords, xp_cmdshell, GRANT to PUBLIC, SA login |
| Style | ST | 24 | keyword casing, alias format, semicolons, line length, indentation |
| Deprecated | DEP | 8 | old data types (text/ntext/image), old JOIN syntax, RAISERROR, SET FMTONLY |
| Design | DE | 7 | missing PK, FLOAT for money, sql_variant, nullable PK, short VARCHAR |
| Execution | EX | 6 | division by zero, data truncation, unreachable code, always-true condition |
| Naming | NM | 6 | reserved words, sp_ prefix, Hungarian notation, single-letter aliases |

### 4. Code Refactoring (18 operations)

**Heavyweight Operations (8, with preview dialog):**
1. Extract to CTE — subquery to WITH clause
2. Extract to Derived Table — subquery to FROM subselect
3. Extract to Stored Procedure — with parameter inference
4. Safe Rename — cross-script rename, generates ALTER scripts (no direct DB execution)
5. Parameterize Values — literal to @parameter conversion
6. Encapsulate as View — SELECT to CREATE VIEW
7. Convert Temp Table to Table Variable (or reverse)
8. **Split Table** — normalization refactoring: generates CREATE TABLE, FK, INSERT migration, DROP COLUMN

**Lightweight Operations (9, instant application):**
1. Remove Semicolons
2. Expand Insert Columns (with metadata comments)
3. Expand Update Columns
4. Expand Exec Parameters
5. Add GROUP BY Columns
6. Encapsulate BEGIN/END
7. Replace Deprecated Syntax
8. Convert Old-Style JOINs to ANSI
9. **Convert sp_executesql to Static SQL** (string-literal-aware parameter substitution)

**Infrastructure:**
- Refactoring Preview Dialog (WinForms, tree-view with checkboxes + RichTextBox diff)
- Rename Script Generator (SQL ALTER script output, transaction-wrapped, commented)
- Reference Collector (AST-based cross-reference detection)

### 5. Execution Safety Guard (4 shell files)
- Pre-execution intercept via DTE `CommandEvents.BeforeExecute` hook on Query.Execute (F5)
- DELETE without WHERE clause warning
- UPDATE without WHERE clause warning
- DROP TABLE / DROP DATABASE confirmation
- TRUNCATE TABLE confirmation
- **Environment-aware dialog severity** (configurable per environment):
  - Production: type server name to confirm (case-sensitive, red banner)
  - Staging: simple Yes/No dialog
  - Development: configurable (can be disabled)
- Transaction Reminder (uncommitted transaction detection with periodic reminders)
- **Structured audit logging** (Serilog Warning level): server, database, environment, environment color, statement type, SQL preview, outcome (Blocked/Confirmed/Bypassed)
- Fail-open design: engine unavailable or timeout → allow execution
- Dynamic settings reload (no IDE restart needed to enable/disable)

### 6. Snippet Manager (3 shell files + 6 engine files)
- WPF Snippet Manager dialog (search, CRUD, import/export)
- **Three snippet sources**: Personal (user folder), Team (shared folder), Built-in (bundled)
- Priority system: Personal > Team > Built-in (for shortcode conflicts)
- **14 built-in variables**: $CURSOR$, $SELECTEDTEXT$, $CLIPBOARD$, $DATE$, $DATETIME$, $TIME$, $USER$, $MACHINE$, $DATABASE$, $SERVER$, $SCHEMA$, $GUID$, $YEAR$, $FILENAME$
- Custom snippet variables with schema-aware hints (tables, columns, procedures)
- Context filtering (global, after_select, after_from, batch_start, etc.)
- Surround-with snippets (wraps selected text — e.g., TRY/CATCH, BEGIN/END)
- Format on expand (optional auto-formatting after snippet insertion)
- Import from `.akmlsnippet` JSON files (single or array, auto-detect)
- Usage tracking for ranking in completion list

### 7. SQL History (5 shell files + 4 engine files)
- SQLite-backed execution history recording (WAL mode for performance)
- History Tool Window with SQL Prompt 3-panel layout (query list, version timeline, code preview)
- Full-text search with filtering (All/Starred/Open/Closed tabs)
- **Advanced search syntax** (spec 013): wildcards (`*`, `?`), boolean operators (`OR`, `NOT`), exact phrase (`"..."`), CamelCase boundary matching (`PC` → ProductCategory), prefix filters (`server:`, `database:`, `name:`, `sql:`, `starred:`, `open:`)
- **Search match highlighting**: Yellow Ochre (#F9A825, 30% opacity) multi-term highlighting in code preview
- **Starring / Favorites**: Star toggle per query, starred filter tab, exempt from retention auto-trim
- **Version history per query**: Timestamped snapshots with timeline panel and compare
- **Rename closed queries**: Right-click context menu rename with `name:` prefix search support
- History diff view (side-by-side query comparison)
- Encryption at rest (optional, Windows DPAPI)
- Configurable retention period (days) and max entries
- Record failures toggle (captures failed query attempts)
- Deduplication (prevents identical consecutive queries)
- Background retention service (enforces cleanup policies)
- FTS5 error fallback: malformed queries gracefully degrade to LIKE-based search

### 8. Tab Management & Session Recovery (7 tab files + 3 session files)
- Tab coloring by environment (Production=red, Staging=orange, Dev=green, Azure=blue)
  - **Optional gradient** (lighter top, base color bottom — LinearGradientBrush)
  - Pattern-based environment rules (glob matching: `*PROD*`, `*DEV*`, `*.database.windows.net`)
  - 4-level assignment hierarchy: Group → Servers in Group → Server → Database
  - **Status bar color propagation** (spec 013): 60% opacity environment color on SSMS status bar
  - **Floating window border** (spec 013): 3px solid environment-colored border on undocked query windows
  - Both configurable via `StatusBarColorEnabled` / `FloatingWindowBorderEnabled` settings
- Custom window title template (`{server} - {database} - SSMS`)
- Tab tooltip with server, database, and user info
- Restore Closed Tab (Ctrl+Shift+T) with undo stack
- Pin Tab / Duplicate Tab / Close All Unmodified
- Session auto-save with configurable interval (default 60s)
- Session recovery on startup (always/prompt/never)
- Crash recovery (restores all open tabs from auto-save)

### 9. Results Grid Enhancements (15 files)
- Aggregate statistics (SUM, AVG, COUNT, MIN, MAX) in VS status bar for selected cells
- Column statistics popup on header right-click (min, max, avg, distinct count, null count)
- NULL value highlighting with distinct background color
- Row numbers column
- Column sorting (3-click cycle: Ascending → Descending → None)
- Column filtering (right-click popup with text filter via DataView.RowFilter)
- Grid Find bar (in-grid text search)
- Export to CSV, JSON, XML, SQL INSERT, Markdown, **Excel** (via ClosedXML)
  - **15+ digit precision option** — numbers with 16+ significant digits exported as text
- Copy as JSON, XML, SQL INSERT, SQL VALUES, HTML table, Markdown table (all include headers)
- Script Generator (INSERT/UPDATE/DELETE from selected rows)
- Cell Edit dialog (Ctrl+DoubleClick for large cell values)
- Transpose Results view (rows ↔ columns)
- Freeze column headers while scrolling

### 10. Navigation & Bookmarks (3 navigation files + 3 bookmark files + 3 productivity/navigation files)
- Go to Definition (F12) — jump to table/procedure/function definition
- Peek Definition (Alt+F12) — inline preview popup without leaving editor
- Find All References (Shift+F12) — references grid in tool window
- Object Search (Ctrl+T) — fuzzy database object search
- Navigate Matching Pair (Ctrl+]) — BEGIN/END, parentheses, TRY/CATCH, CASE/END
- Navigate Next/Previous Statement (Ctrl+PageDown/Up)
- **Bookmarks**: Toggle (Ctrl+K, Ctrl+K), Next (Ctrl+K, Ctrl+N), Previous (Ctrl+K, Ctrl+P)
  - Blue circle margin glyphs via IGlyphFactory MEF export
  - Session-scoped (cleared on view close)
- Document Outline tool window (procedures, functions, views, CTEs, temp tables, GO boundaries)

### 11. Editor Productivity (7 execution files + 4 adornment files)
- Execute Current Statement (Alt+Enter) — executes only the statement at cursor
- Execute to Cursor — executes all statements up to cursor line
- **Multi-database execution** — run same query across multiple databases in parallel
- Execution timer in status bar (elapsed time display)
- Long-running query notification (configurable threshold, default 30s)
- Highlight Occurrences of selected identifier
- Bracket Matching (BEGIN/END, parentheses highlighting)
- Named Regions (`--region Name` / `--endregion` code folding)
- Sticky Scroll (parent scope header pinning)
- Code Minimap (scrollable overview in right margin)
- Editor Toolbar (SQL Prompt-style action bar at top of each SQL editor)

### 12. Settings System (2 dialog files + OptionCategoryTreeBuilder)
- WPF Settings dialog with **15 category pages**: General, IntelliSense, Schema Cache, Formatting, Snippets, Code Analysis, Refactoring, History, Tabs & UI, Safety, Grid, Editor, Execution, Navigation, AI Assistance
- Dark/Light/System theme support (SQL Prompt-accurate hex palette: Light #F0F0F0/#0078D4, Dark #2D2D3B/#1E1E2E/#8892A8/#3A3F4E)
- Per-category Reset This Page / Reset All to defaults
- Export All Settings / Import Settings (JSON format)
- **101 configurable settings** across 15 `AppSettings` sections:
  - IntelliSense (12), Cache (6), Formatter (9), Snippets (7), CodeAnalysis (4), Refactoring (6), History (6), Tabs (8), Safety (8), Grid (5), EditorProductivity (6), ExecutionProductivity (3), Navigation (5), CommandPalette (1), AI (18)

### 13. AI Assistance (9 shell files + 2 engine files)
- **Multi-provider support**: OpenAI, Anthropic, Gemini, Ollama, LM Studio, Custom endpoint
- Text to SQL (natural language to T-SQL with schema context)
- AI Explain (query explanation in plain English)
- AI Fix (error correction suggestions with diff preview)
- AI Optimize (performance optimization suggestions)
- AI Index Analysis (missing index suggestions based on query patterns)
- AI Chat Panel (multi-turn conversation with database schema context)
- Ghost Text Completion (inline AI suggestions, experimental)
- **Privacy modes**: schemaOnly (metadata only), full (includes query text), anonymous, offline, disabled
- Privacy transformer (literal redactor + identifier hasher)
- Token estimator for cost/limit checking
- Auto-Fix on Error (optional: triggers AI Fix when query execution fails)

### 14. Command Palette (4 files)
- Fuzzy-search command launcher (Ctrl+Shift+P)
- **32 registered commands** across 8 categories (Execution, Navigation, Tab, Grid, Tool Window, AI, Analysis, Settings)
- Usage-based ranking (most-used commands float to top)
- Keyboard navigation (arrow keys + Enter to select)

### 15. Schema Cache (4 engine files)
- In-memory cache of database objects (tables, views, procedures, functions, indexes, FKs)
- **Phase A** (fast, <500ms): `sys.objects` + `sys.schemas` + row counts
- **Phase B** (background): columns, FKs, parameters, descriptions
- Change detection via `CHECKSUM_AGG(BINARY_CHECKSUM(...))` polling
- DDL regex detection triggers immediate Phase A refresh
- LRU eviction for multiple database connections
- Persistent cache to disk (optional — survives IDE restarts)
- FK index for O(1) foreign key lookups (`schema.table` → `List<ForeignKey>`)

### 16. Infrastructure

**Architecture:**
- Shared Project (.projitems) compiled into 6 shell targets (SSMS 20/21/22, VS 2019/2022/2026)
- Out-of-process Engine (.NET 10, self-contained, win-x64, PublishTrimmed)
- MessagePack IPC over named pipes (30+ bidirectional message types)
- Named pipe ACL: owner SID allowed, Network SID denied
- Frame format: [4-byte length][4-byte XOR CRC][MessagePack(RpcMessage)]

**Tooling:**
- Serilog structured logging (rolling file sink)
- Atomic config writes (temp file + rename pattern)
- Self-contained updater (.NET 10, win-x64)
- Inno Setup 7 installer with environment scanner (registry + vswhere + filesystem fallback)
  - SSMS/VS running detection via CloseApplications (spec 013)
  - `/LOG` verbose logging support (native Inno Setup feature, documented)
  - In-place upgrade/repair via fixed AppId + UsePreviousAppDir
  - **SQL Prompt style importer** (spec 013): detects Red Gate config, stages `.sqlpromptstyle` files, engine imports on first startup via `SqlPromptImporter` (99-option mapping)

**Codebase Statistics:**
- 916 C# files total
- Shell.Shared: 194 files across 21 directories
- Engine: 252 files across 14 directories
- Core: 152 files across 8 directories
- Tests: 207 files across 5 test projects

### Test Coverage
- **915 unit tests** (xunit, .NET 10) across 5 test projects:
  - `AkmlSql.Core.Tests` — 27 test files, 457 tests (config, IPC, formatting, analysis, refactoring, safety, snippets, navigation)
  - `AkmlSql.Engine.Tests` — 31 test files (analysis, completion, parser, refactoring, schema, snippets)
  - `AkmlSql.Formatting.Tests` — 33 test files, 458 tests (actions, casing, layout, pipeline, profiles, rules)
  - `AkmlSql.E2E.Tests` — 17 test files (end-to-end analyzer, formatter, infrastructure)
- Key test areas added in specs 010-011:
  - SafetyCheckHandler: 15 tests (DELETE/UPDATE/DROP/TRUNCATE patterns)
  - SnippetImport: 6 tests (JSON import, auto-detect, validation)
  - SafeRenameOperation: 6 tests (column/table/alias rename, line numbers)
  - DocumentOutline: 15 tests (procedure/function/view/CTE detection)
  - InsertMetadata: 9 tests (type comments, identity, defaults, computed)
  - SpExecutesqlConversion: 18 tests (param substitution, string quoting, NULL, string-literal safety)
  - SplitTableOperation: 6 tests (input validation, error handling)
- Key test areas added in spec 013:
  - NoformatScanner directive aliases: 11 tests (AKML/SQL Prompt/mixed syntax, case-insensitivity, block comments)

---

## Gap Analysis vs SQL Prompt v11 (2026-04-03)

> Source: `doc/AKML_SQL_Gap_Analysis_1.md` — detailed feature-by-feature comparison

### Parity Status: 12 of 12 areas fully covered — ABSOLUTE 100% PARITY

| Area | Status | Notes |
|------|--------|-------|
| IntelliSense | **Full** | All features + extras (9 providers, Ctrl transparency, SQL Prompt One Dark icon palette) |
| Formatting | **Full** | 21 commands vs SQL Prompt's ~10 — exceeds (Unformat + formatting off/on directives) |
| Snippets | **Full** | 14 variables vs 6, 3 sources, context filtering — exceeds |
| Analysis | **Full** | 130 rules vs 94 — exceeds (Security/Naming/Design categories extra) |
| Tab Coloring | **Full** | Gradient, hierarchy, custom title, status bar + floating window color — exceeds |
| SQL History | **Full** | Starring, version history, advanced search, rename, highlighting — all present |
| Refactoring | **Full** | 18 operations vs ~8 — exceeds |
| Navigation | **Full** | Bookmarks, Outline, Peek, Minimap — exceeds |
| Results Grid | **Full** | 16 features vs 7 — exceeds (includes Copy as IN Clause) |
| Safety | **Full** | Audit logging, type-to-confirm — exceeds |
| Settings | **Full** | 15 pages, 103 settings, import/export, SQL Prompt-accurate color palette |
| Command Palette | **Full** | 32 commands with fuzzy search |

### All Gaps Resolved (specs 012 + 013)
- **Starring / Favorites**: Already implemented (retention exemption confirmed in HistoryDatabase.cs)
- **Version history per query**: `history_versions` table + timeline panel + compare
- **Advanced search syntax**: `HistorySearchParser` with prefix, wildcard, phrase, boolean, CamelCase support + FTS5 error fallback
- **Rename closed queries**: Context menu rename + `UpdateTabTitleAsync` IPC + `name:` prefix search via NameFilter
- **Search match highlighting**: Yellow Ochre (#F9A825, 30% opacity) multi-term highlighting in code preview
- **Copy as IN Clause**: `FormatAsInClause` in GridCopyAsMenu with proper quoting
- **Unformat command**: `UnformatCommand` shell wiring (Ctrl+B,Ctrl+U) to existing `UnformatOperation`
- **Formatting region directives**: `-- AKML formatting off/on` and `-- SQL Prompt formatting off/on` as aliases for `-- noformat`
- **Options dialog colors**: SQL Prompt-accurate hex palette (Light #F0F0F0/#0078D4, Dark #2D2D3B/#1E1E2E/#8892A8/#3A3F4E)
- **IntelliSense icon colors**: SQL Prompt One Dark palette for all 12 object types (semi-transparent badge backgrounds)
- **Tab color propagation**: Status bar (60% opacity) + floating window border (3px solid)
- **Installer enhancements**: SSMS detection via CloseApplications, `/LOG` documentation, repair/upgrade verification
- **SQL Prompt style importer**: Installer detects Red Gate config, stages files, engine imports on startup
---

## Development History

## Spec 014 Phase 3b: UI Polish — Safety Dialog & Schema Progress Margin (2026-04-11)

**Status**: Complete (uncommitted on branch `014-sql-prompt-parity`)
**Scope**: Three files — `SafetyWarningDialog.cs`, `ExecutionInterceptor.cs`, `SchemaProgressMargin.cs`
**Goal**: Match Redgate SQL Prompt's visual language for the pre-execution safety dialog and the schema-cache loading indicator. Eliminate hacky UI patterns introduced by the initial Phase 3 commit.

### SafetyWarningDialog.cs — WinForms → WPF rewrite, theme-aware

**Before**: Three separate WinForms `Form` layouts (`BuildSimpleConfirmLayout`, `BuildErrorLevelLayout`, `BuildTypeToConfirmLayout`, `BuildTypeServerNameLayout`) dispatched by `DetermineMode`. Hard-coded colors, no theme support, used `System.Drawing` primitives and `PictureBox`/`Label`/`Button` (WinForms).

**After**: Single WPF `Window` (`internal sealed class SafetyWarningDialog : Window`) with a SQL Prompt-style unified layout:

- **Environment banner** (top strip, accent color from `matchedEnvRule?.Color`) — shows "{envLabel}  •  {serverName}"
- **Warning header row** — bold icon + title ("You are about to DROP an object" / "This statement may affect all rows" / "Execution requires confirmation")
- **Body** — "Target: {serverName}" line + one card per warning (badge + message + optional "Object: {name}")
- **Inline type-to-confirm for DROP** — appears within the same dialog when a `DropTable`/`DropDatabase` warning with an extractable object name is present
- **Footer** — "Don't warn again this session" checkbox (left) + Cancel (default, `IsCancel=true`) + "Execute Anyway" / "Drop" button (right, deliberately not the default per FR-005)

**Factory pattern**:
```csharp
var dialog = SafetyWarningDialog.CreateForWarnings(filteredWarnings, serverName, envLabel, envColor);
var wpfResult = dialog.ShowDialog();   // bool? — true means execute
if (wpfResult == true) { /* user confirmed */ }
```

**Semantic colors** kept as theme-independent statics (so warnings look the same in light/dark/blue):
- `AmberBorder = #FFC107` — confirmation / medium severity
- `ErrorBorder = #DC3545` — destructive / high severity
- `BtnPrimary  = #0078D4` — "Execute Anyway" button

**Chrome colors** pulled from `ThemeManager.Instance`:
- `Background` / `Foreground` — window chrome
- `Border` — divider between sections
- `PlaceholderText` — muted text (Target, footer labels)
- `EditorPanelBackground` — warning card background

**Fixed bugs from the initial Phase 3 commit**:
1. `Text = isError ? "\u26A0" : "\u26A0"` — both ternary branches identical (dead code). Replaced with `Text = "\u26A0"`.
2. `CenterOwner` silently fell back to center-screen because `Owner` was never set. Added `TryAttachOwnerToHost()` which reads `EnvDTE.DTE.MainWindow.HWnd` and assigns via `WindowInteropHelper.Owner` (same pattern as `HistoryDiffWindow.cs`).
3. `FormatRuleType` switched on `enum.ToString()` — not rename-safe. Rewritten as `GetRuleLabel(SafetyWarningType type) => type switch {...}` — direct enum switch, no per-call reflection.
4. Repeated `new SolidColorBrush(...)` / `new FontFamily(...)` inside the warning-card loop. Moved to per-dialog frozen brushes (fields) and class-level `static readonly FontFamily SegoeUiFont / ConsolasFont`.
5. Two `warnings.Any(...)` LINQ passes for `isError` / `isDrop` flags. Collapsed to a single `foreach` with early exit.

**Dead code removed**:
- Legacy `public static DialogResult Show(...)` method — no callers in the repo (grep confirmed zero references from src/). Its only purpose was to return `System.Windows.Forms.DialogResult`, which was the only reason the file imported `System.Windows.Forms`.
- `using System.Windows.Forms;` — dropped along with the legacy `Show()`.
- Section-divider narration comments (`// ── Palette ──`, `// ── Build ──`, etc.).

**Method extraction**: The 300-line `Build` method is now split into `BuildHeader`, `BuildBody`, `BuildWarningCard`, `BuildConfirmPanel`, `BuildFooter` — reduces the near-duplicate `TextBlock`/`Border` clusters.

### ExecutionInterceptor.cs — restored lost `EnvironmentSeverity = "Disabled"` behavior

**Regression**: The initial Phase 3 WinForms→WPF rewrite dropped the `EnvironmentSeverity` config check from the dialog factory. The old `Show()` method honored three modes (`"Disabled"` → skip dialog, `"TypeServerName"` → force server-name type-to-confirm, `"SimpleConfirm"` → minimal dialog). The new unified WPF dialog intentionally drops `TypeServerName` and `SimpleConfirm` (SQL Prompt has one dialog style), but dropping `"Disabled"` was a real functional regression — users who configured `Safety.EnvironmentSeverity["DEV"] = "Disabled"` would still see the dialog.

**Fix**: Added `IsEnvironmentDisabled(envLabel, cachedSafety)` static helper in `ExecutionInterceptor.cs` and short-circuit in `OnBeforeExecute` before calling `CreateForWarnings`:

```csharp
if (IsEnvironmentDisabled(envLabel, cachedSafety))
{
    LogAuditEvent(serverName, envLabel, envColor, filteredWarnings, "SkippedByEnvironmentConfig");
    return true;
}
```

Also dropped the now-unused `using System.Windows.Forms;` at the top of the file (no more `DialogResult.OK` comparison after the WPF rewrite).

### SchemaProgressMargin.cs — arc spinner, slim compact layout, theme-aware

**Before**: 22px-tall full-width bar with a rotating `Border` that had `CornerRadius(6)` and `BorderThickness(2, 2, 0, 0)` — literally rotating a rectangle corner to fake a spinner. Hardcoded light/dark hex colors. Left-aligned, pulled visual weight onto the code area. Messages read as dev-facing ("Loading schema [{db}]…").

**After**: 20px-tall slim strip that blends into the editor chrome:

- **Proper arc spinner**: 12×12 `Ellipse` with `Stroke = theme.AccentColor`, `StrokeThickness = 1.6`, `StrokeDashArray = { 10, 30 }` (≈90° visible arc over ≈270° gap — ellipse perimeter ≈ 2πr ≈ 37.7, so `10 + 30 ≈ 37.7`). Rotated by the same `RotateTransform` + `DoubleAnimation(0→360, 1100ms, Forever)` that ran on the old border. Gives a modern "arc chasing its tail" spinner.
- **Ready state**: spinner hidden; shows a bold green `✓` (`Color.FromRgb(0x2E, 0xA0, 0x43)`) — intentionally semantic/theme-independent so success always reads the same.
- **Right-aligned content**: `HorizontalAlignment = Right` on the inner `StackPanel`, padding `(8, 0, 12, 0)` on the root `Border` — pulls visual weight off the code area.
- **Theme-aware background** via `ThemeManager.Instance`:
  - `PreviewBackground` (editor bg) for the strip background
  - `Border` for the 1px bottom divider
  - `PlaceholderText` (muted gray) for the status text
  - `AccentColor` (VS blue) for the spinner arc
- **150ms opacity fade** on show/hide via `DoubleAnimation` on `OpacityProperty` — no more pop-in/pop-out flicker. `FadeTo(target, onCompleted)` helper wraps the animation and runs `onCompleted` (if provided) when `anim.Completed` fires.
- **SQL Prompt-style copy**:
  - Phase 0 (NotLoaded): `Populating suggestions for {db}` (was `Loading schema [{db}]…`)
  - Phase 1 (Phase A done, Phase B loading columns): `Loading columns — {pct}% ({n}/{total})` (was `Loading columns for [{db}] — …`)
  - Phase 2/3 (Complete): `Schema cache ready — {n} objects` (was `Schema [{db}] ready — {n} objects`)

**State machine unchanged**: `Hidden` / `Loading` / `Ready` states, 1000ms polling via `DispatcherTimer`, 3000ms IPC timeout, 15000ms loading-stuck timeout (`_loadingTimedOut` flag reset on database switch), 2000ms ready-display duration, re-entrancy guard (`_polling` bool), database-change detection.

### Related reference files (to consult for next WPF window work)

| Purpose | File |
|---------|------|
| Canonical WPF `Window` with theme + DTE owner | `src/AkmlSql.Shell.Shared/History/HistoryDiffWindow.cs` |
| Theme brush palette (ThemeManager singleton) | `src/AkmlSql.Shell.Shared/Ui/ThemeManager.cs` — exposes `Background`, `Foreground`, `Border`, `AccentColor`, `PlaceholderText`, `EditorPanelBackground`, `PreviewBackground`, `HighlightBackground`, and SQL Prompt History-specific colors |
| Modern WPF dialog with frozen brushes + card layout | `src/AkmlSql.Shell.Shared/Safety/SafetyWarningDialog.cs` |
| Arc-spinner pattern for IWpfTextViewMargin | `src/AkmlSql.Shell.Shared/Editor/SchemaProgress/SchemaProgressMargin.cs` |

### Build verification

Built via `MSBuild src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj -t:Build -p:Configuration=Release` — zero errors, zero new warnings from the three changed files. Pre-existing VSTHRD100 (`async void OnPollTick`) remains, same pattern as before the rewrite.

### Spec 014 status (as of 2026-04-11)

- Phase 1+2 (foundational scaffolding): **committed** — `fba63d6`
- Phase 3 US1 (pre-execution safety — MERGE/JOIN/proc/trigger detection + session opt-out): **committed** — `f337729`
- Phase 3b (this session — UI polish for safety dialog + schema progress margin): **uncommitted**, working tree dirty on branch `014-sql-prompt-parity`
- Remaining US (from `specs/014-sql-prompt-parity/tasks.md`): US10 (AI shortcut bindings), US14 (Invalid Objects tool window), US19 (completion polish — Ctrl+Shift+D refresh, Ctrl+Shift+P toggle, custom commit keys, encrypted object decryption), US20 (remaining gaps)

---

## Phase 1: Foundation and Installer

### Milestone 1: Project Scaffolding

**Status**: Complete

- Created solution with 6 shell extension projects (SSMS 20/21/22, VS 2019/2022/2026)
- Created shared project (`AkmlSql.Shell.Shared`) with menu commands, dialogs, status bar, update launcher, and load validator
- Created core library (`AkmlSql.Core`) with config manager, logging, update models
- Created self-contained updater (`AkmlSql.Updater`)
- Created Inno Setup 7 installer with environment scanner (registry + vswhere + filesystem fallback)
- Created Specify framework specs under `specs/001-phase1-foundation-installer/`

### Milestone 2: SSMS 20 Extension Loading

**Status**: Complete — verified working

#### Issue 1: Wrong Shell Assembly Version

- **Symptom**: Extension fails to load; activity log shows assembly binding failure
- **Root Cause**: VS SDK 16.0.208 references `Shell.15.0` version `16.0.0.0`, but SSMS 20 (VS 2017 IsolatedShell) ships `15.0.0.0`
- **Fix**: Downgraded VS SDK from `16.0.208` to `15.9.3` and VSSDK.BuildTools from `16.*` to `15.*`
- **Lesson**: Always clean `bin/obj` folders after SDK version changes — stale NuGet cache causes wrong assembly references

#### Issue 2: Menu Not Appearing — Missing CTO Resource

- **Symptom**: `HrLoadNativeUILibrary failed with 0x800a006f` in activity log
- **Root Cause**: SDK-style projects do not have a `.resx` file by default. Without `VSPackage.resx` with `<MergeWithCTO>true</MergeWithCTO>`, the VSCT-compiled CTO (`Menus.ctmenu`) is never embedded as a managed resource in the output DLL
- **Build Warning**: `VSSDK1205: There are no resources to merge the cto files into`
- **Fix**: Created `VSPackage.resx` in all 6 shell projects with `<EmbeddedResource Update="VSPackage.resx"><MergeWithCTO>true</MergeWithCTO><ManifestResourceName>VSPackage</ManifestResourceName></EmbeddedResource>` in each `.csproj`
- **Gotcha**: Must use `Update=` not `Include=` — SDK-style projects auto-include `.resx` files, causing `NETSDK1022: Duplicate EmbeddedResource`

#### Issue 3: Menu Appears But Clicks Do Nothing

- **Symptom**: AKML SQL menu visible, but clicking any item produces no response
- **Root Cause 1 — AsyncPackage**: SSMS 20 (VS 2017 shell) does not properly wire up command handlers when using `AsyncPackage` with background loading before menu clicks arrive
- **Fix**: Changed `AkmlSqlPackage` from `AsyncPackage` to synchronous `Package` for SSMS 20
- **Root Cause 2 — Initialization Order**: `LoggerFactory.Initialize()` or `LoadValidator.Validate()` threw exceptions that were silently caught, but this happened BEFORE command registration, so no commands were ever registered
- **Fix**: Reordered `Initialize()` to register all menu commands FIRST, then perform non-critical initialization (logging, validation, update check) in a separate try-catch
- **Root Cause 3 — Missing Dependency DLLs**: `System.Text.Json`, `System.Memory`, `System.Buffers`, and other transitive NuGet dependencies were not deployed to the extension folder
- **Fix**: Changed Inno Setup installer from per-file DLL listing to `*.dll` wildcard pattern for all 6 targets

#### Issue 4: VSCT CTO Cross-Contamination

- **Symptom**: Build errors — all projects look for `AkmlSqlVS2026.cto` regardless of which project is being built
- **Root Cause**: Building via the solution file causes VSCT to use the last project's CTO output path
- **Fix**: Build each shell project individually with MSBuild, never via solution-level build

#### Additional SSMS 20 Fixes

- **pkgdef**: Added `Menus` registration entry, set `AllowsBackgroundLoad=dword:00000000` (synchronous), autoload flags `dword:00000000`
- **vsixmanifest**: Uses Schema 2010 (`<Vsix>` root) with `<IsolatedShell Version="1.0">ssms</IsolatedShell>`
- **Command signatures**: Changed all 5 command classes from `AsyncPackage` parameter to `Package` parameter
- **MEF Cache**: Located at `%LocalAppData%/Microsoft/SQL Server Management Studio/20.0_IsoShell/ComponentModelCache/` (not under `VisualStudio`)

### Milestone 3: SSMS 22 Extension Loading

**Status**: Complete — verified working (menu under Tools, commands functional)

#### Issue 5: Extension Visible in Extension Manager But No Menu

- **Symptom**: AKML SQL appears in Extensions > Manage Extensions but no menu item in the top menu bar
- **Root Cause 1 — Wrong Extension Path**: Files were initially deployed to root-level `Common7/IDE/Extensions/AkmlSql/`, but SSMS 22 executable lives under `Release/Common7/IDE/` and loads extensions from `Release/Common7/IDE/Extensions/`
- **Fix**: Deploy to `C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\AkmlSql\`

#### Issue 6: Wrong vsixmanifest InstallationTarget

- **Symptom**: Extension not recognized as compatible with SSMS
- **Root Cause**: vsixmanifest targeted `Microsoft.VisualStudio.Community/Pro/Enterprise` instead of `Microsoft.VisualStudio.Ssms`
- **Fix**: Changed to `<InstallationTarget Id="Microsoft.VisualStudio.Ssms" Version="[17.0,)" />` with `AllUsers="true"`
- **Applied to**: SSMS 21 and SSMS 22 vsixmanifest files

#### Issue 7: Package Never AutoLoads — Wrong UI Context

- **Symptom**: pkgdef is imported (visible in activity log) but `Begin package load [AkmlSqlPackage]` never appears; menu never renders
- **Root Cause**: AutoLoad registered for `{e8fbc700-a1bd-11d0-a67c-00a0c9110051}` (`UICONTEXT_ShellInitialized`) which is a standard VS context. SSMS 22 uses its own context: `{B7B07F42-6013-4C67-A504-C771CBC7625A}` (`UICONTEXT_SSMS`)
- **Evidence**: Found in `SSMS.Application.pkgdef`: `[$RootKey$\AutoLoadPackages\{B7B07F42-6013-4C67-A504-C771CBC7625A}] @="UICONTEXT_SSMS"`
- **Fix**: Changed `[ProvideAutoLoad]` attribute and pkgdef to use `{B7B07F42-6013-4C67-A504-C771CBC7625A}`
- **Status**: Verified working — applied to SSMS 21 and SSMS 22

#### Issue 8: PkgDef Cache Not Refreshing

- **Symptom**: Activity log shows `PkgDefCache fast check: timestamps are current` even after deploying new files
- **Root Cause**: The private registry hive (`privateregistry.bin`) caches pkgdef entries and the timestamp check doesn't detect new extension folders
- **Fix**: Delete `%LocalAppData%/Microsoft/SSMS/22.0_05e71b86/privateregistry.bin` (and `.LOG1`, `.LOG2`), plus clear `ComponentModelCache/`, `MEFCacheBackup/`, and CTM files
- **Alternative**: Run `SSMS.exe /updateconfiguration` from PowerShell (not Git Bash — path mangling converts `/updateconfiguration` to a file path)

#### Issue 9: Menu Not Visible — SSMS 22 Custom Menu Bar

- **Symptom**: Package loads successfully, menu commands visible in Customize dialog, but no "AKML SQL" menu in the top menu bar
- **Root Cause**: SSMS 22 uses a custom menu bar via `SSMSMnu.dll` that does NOT include the standard VS `guidSHLMainMenu:IDG_VS_MM_TOOLSADDINS` group. This group is where VS extensions traditionally place their top-level menus, but it has no visible parent in SSMS 22's menu hierarchy
- **Investigation**: Extracted native CTO from `SSMSMui.dll` (satellite of SSMS Menu Package `{B7B07F42-...}`). Confirmed `guidSHLMainMenu` GUID is absent from the SSMS CTM binary. The CFCT v5 format is compressed, preventing further analysis
- **Fix**: Added `<CommandPlacement>` in VSCT to additionally place the menu in `guidSHLMainMenu:IDG_VS_TOOLS_EXT_TOOLS`, which maps to the Tools menu in SSMS 22
- **Result**: "AKML SQL" appears as a submenu under the Tools menu
- **Applied to**: SSMS 21 and SSMS 22 VSCT files
- **Note**: Attempting to parent a group directly to `IDM_VS_MENU_BAR` (0x0001) caused the package to silently fail to load — the CTM merger appears to reject unknown parent references

#### Issue 10: Menu Clicks Do Nothing — Init Order (Same as SSMS 20 Issue 3)

- **Symptom**: Menu visible under Tools, but clicking any item (About, Options, etc.) produces no response
- **Root Cause**: Same as SSMS 20 Issue 3 — `InitializeAsync()` performed `LoggerFactory.Initialize()` and `LoadValidator.Validate()` BEFORE registering menu command handlers. If either threw an exception, the outer catch swallowed it and commands were never registered
- **Fix**: Reordered `InitializeAsync()` to register all menu commands FIRST (critical path), then perform non-critical initialization (logging, validation, status bar, update check) in a separate try-catch
- **Applied to**: All 6 shell extension projects (SSMS 20/21/22, VS 2019/2022/2026)
- **Status**: Verified working on SSMS 22

---

## SSMS Version Differences — Quick Reference

| Aspect | SSMS 20 | SSMS 21/22 |
|--------|---------|------------|
| VS Shell Base | VS 2017 IsolatedShell | VS 2022 Shell |
| Platform | x86 | x64 |
| Package Base Class | `Package` (synchronous) | `AsyncPackage` |
| vsixmanifest Schema | 2010 (`<Vsix>`) | 2011 v2.0 (`<PackageManifest>`) |
| InstallationTarget | `<IsolatedShell>ssms</IsolatedShell>` | `Microsoft.VisualStudio.Ssms` |
| AutoLoad Context | `{e8fbc700-...}` (ShellInitialized) | `{B7B07F42-...}` (UICONTEXT_SSMS) |
| Extension Path | `<Root>/Common7/IDE/Extensions/AkmlSql/` | `<Root>/Release/Common7/IDE/Extensions/AkmlSql/` |
| MEF Cache | `%LocalAppData%/Microsoft/SQL Server Management Studio/20.0_IsoShell/ComponentModelCache/` | `%LocalAppData%/Microsoft/SSMS/22.0_*/ComponentModelCache/` |
| Activity Log | `%AppData%/Microsoft/SQL Server Management Studio/20.0_IsoShell/ActivityLog.xml` | `%AppData%/Microsoft/SSMS/22.0_*/ActivityLog.xml` |
| Activity Log Encoding | UTF-16LE | UTF-16LE |
| VS SDK Version | 15.9.3 | 17.14.x |
| AllowsBackgroundLoad | 0 (disabled) | 1 (enabled) |

## Cache Clearing Procedures

### SSMS 20

```powershell
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\Microsoft\SQL Server Management Studio\20.0_IsoShell\ComponentModelCache"
```

### SSMS 22

```powershell
# Full cache reset (required when adding new extensions)
Remove-Item -Force "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_05e71b86\privateregistry.bin*"
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_05e71b86\ComponentModelCache"
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_05e71b86\MEFCacheBackup"
Remove-Item -Force "$env:LOCALAPPDATA\Microsoft\SSMS\22.0_05e71b86\1033\SSMS.CTM*"

# Then rebuild configuration (run from PowerShell, NOT Git Bash)
& "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\SSMS.exe" /updateconfiguration
```

## Deployment Procedures

### Manual Deployment (Development)

```powershell
# SSMS 20
$src = "src\AkmlSql.Ssms20"
$dest = "<SSMS20Root>\Common7\IDE\Extensions\AkmlSql"
Copy-Item "$src\bin\Release\net472\*.dll" $dest
Copy-Item "$src\AkmlSql.Ssms20.pkgdef" $dest
Copy-Item "$src\source.extension.vsixmanifest" "$dest\extension.vsixmanifest"

# SSMS 22
$src = "src\AkmlSql.Ssms22"
$dest = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\AkmlSql"
Copy-Item "$src\bin\Release\net472\*.dll" $dest
Copy-Item "$src\AkmlSql.Ssms22.pkgdef" $dest
Copy-Item "$src\source.extension.vsixmanifest" "$dest\extension.vsixmanifest"
```

### Debugging with Activity Log

```powershell
# Launch with logging enabled
& "<SSMS_EXE_PATH>" /log

# Activity log location (UTF-16LE encoded)
# SSMS 20: %AppData%\Microsoft\SQL Server Management Studio\20.0_IsoShell\ActivityLog.xml
# SSMS 22: %AppData%\Microsoft\SSMS\22.0_05e71b86\ActivityLog.xml

# Reading from Git Bash (requires iconv for UTF-16)
iconv -f UTF-16LE -t UTF-8 "<path>/ActivityLog.xml" | grep -i "akml"

# Reading from PowerShell
Get-Content "<path>\ActivityLog.xml" -Encoding Unicode | Select-String "akml"
```

---

## Diagnostic Tools Used

1. **Activity Log Analysis**: `SSMS.exe /log` generates XML activity log with package load events and errors
2. **Assembly Reference Inspector**: Custom .NET 4.72 console app using `Assembly.ReflectionOnlyLoadFrom()` to verify DLL assembly references and embedded resources
3. **Inline MessageBox Diagnostic**: Replaced entire package init with inline `MessageBox.Show()` handlers to confirm command wiring works, isolating the issue to initialization order
4. **PkgDef Search Path Inspection**: Activity log reveals `PkgDefSearchPath` entries showing exactly which directories SSMS scans for extensions

---

## Build: Analyzer CLI (Phase 5)

`AkmlSql.Analyzer` is a self-contained .NET 10 CLI tool for static SQL analysis in CI/CD pipelines.

### Build & Publish

```bash
dotnet publish src/AkmlSql.Analyzer/AkmlSql.Analyzer.csproj -c Release -r win-x64
# Output: src/AkmlSql.Analyzer/bin/Release/net10.0/win-x64/publish/AkmlSql.Analyzer.exe
```

### CLI Usage Examples

```bash
# Analyze a single file
AkmlSql.Analyzer.exe --file query.sql

# Analyze a directory recursively (exit 1 if any warnings found — for CI/CD)
AkmlSql.Analyzer.exe --directory scripts/ --recursive --check --severity warning

# Analyze with specific rules only
AkmlSql.Analyzer.exe --file query.sql --rules PE001,BP004,SE001

# Exclude rules
AkmlSql.Analyzer.exe --directory scripts/ --exclude-rules NM006,ST001

# JSON report (stdout) + file report
AkmlSql.Analyzer.exe --file query.sql --format json --report report.json

# With custom settings file
AkmlSql.Analyzer.exe --directory scripts/ --settings .casettings

# Show help / version
AkmlSql.Analyzer.exe --help
AkmlSql.Analyzer.exe --version
```

### Exit Codes

| Code | Meaning |
|------|---------|
| 0    | Clean — no violations at `--severity` level, or `--check` not specified |
| 1    | Violations found (only when `--check` is used) |
| 2    | Fatal error (parse failure, invalid args, missing file) |

### Importing SQL Prompt Settings

To convert an existing SQL Prompt `.casettings` XML file to AKML's JSON format:

```csharp
// In code (SqlPromptImporter.Convert returns the count of converted rules)
int count = AkmlSql.Engine.Analysis.SqlPromptImporter.Convert(
    xmlInputPath: "SqlPrompt.casettings",
    jsonOutputPath: ".casettings");
```

The importer maps 55 SQL Prompt rule IDs to their AKML equivalents. Unknown SQL Prompt rule IDs are logged and skipped.

### Configuring CAsettings in CI/CD

Place a `.casettings` file in the root of the SQL scripts directory. The analyzer walks up the directory tree to find the nearest file. Example `.casettings`:

```json
{
  "metadata": { "name": "CI Rules", "version": "1.0" },
  "rules": {
    "PE001": { "enabled": true, "severity": "error" },
    "NM006": { "enabled": false, "severity": "ignore" },
    "ST001": { "enabled": true, "severity": "warning" }
  },
  "globalSuppressions": [
    { "rule": "BP012", "reason": "Date literals intentional in migration scripts" }
  ]
}
```

GitHub Actions example:

```yaml
- name: SQL Static Analysis
  run: |
    AkmlSql.Analyzer.exe --directory sql/ --recursive --check --severity warning --report analysis-report.json
  continue-on-error: false
```

---

*Last updated: 2026-04-03*

---

## Spec 020 — SQL Prompt Visual Parity (2026-05-15)

**Branch**: `020-sqlprompt-visual-parity` · **Commits**: 11 (this entry)

**Goal**: drive visual parity with Redgate SQL Prompt across every AKML-SQL surface (colours, sizes, fonts) plus close functional gaps in SQL Format and `.sqlpromptstylev2` import / export. Builds on spec 016's centralised theme tokens.

### What shipped

| Phase | User Story | Tasks done / total | Notes |
|---|---|---:|---|
| 1 (Setup) | — | 6/6 | Reconciled plan/data-model/quickstart with 5 clarification answers; scaffolded `tests/format-parity/` + new test folders. |
| 2 (Foundational) | — | 6/6 | Added `RequestStyleEditorSchema = 28` / `Result = 128` IPC constants. Added Spacing.* + Typography.* token families. Implemented hardcoded-hex scanner test (currently informational) + visual-reference coverage test. |
| 3 (US1 — P1 MVP) | Unified visual theme | 10/10 | Added 25 brush tokens (IconBadge × 12 + TabColor × 8 + History × 5) wired across Light / Dark / HighContrast palettes (75 brush bindings). Implemented `ThemeMigrationManager` with `themeMigration.v1.json` marker. Wired into all 6 shell packages. Migrated the 5 remaining `ThemeManager.Instance` call-sites and **deleted `Ui/ThemeManager.cs` entirely**. |
| 4 (US2 — P1 MVP) | Import SQL Prompt style | 15/21 | Implemented `SqlPromptExporter` for round-trip export. Spec docs corrected: actual file format is `.sqlpromptstylev2` XML, not the JSON the original spec assumed. Most importer infrastructure (FormattingProfile, OptionMap, ProfileManager) already shipped. 3 built-in styles deferred (rename / author missing entries). |
| 5 (US3 — P2) | Format Styles editor | 14/17 | Tier 1: `FormatSettingSchema` + IPC handler + 5 tests. Tier 2: `FormatStylesEditorWindow` (programmatic WPF, 1000×680, 3-column GridSplitter) + view model. Tier 2b: type-driven controls (CheckBox / numeric TextBox / Enum TextBox / Other) + live preview via FormatPreview IPC (100 ms debounce + supersession). Sample SQL persistence via `%AppData%/AKML SQL/editor/preview-sample.sql`. Menu wire + Options dialog audit deferred. |
| 6 (US4 — P2) | IntelliSense surfaces | 5/6 | Migrated `CompletionItemModel.GetColor` to `IconBadge.*` token lookups (13 hex literals removed). Implemented Ctrl-held semi-transparency on `AkmlCompletionPopup` (`DispatcherTimer` polls modifier state while visible). ColumnPickerWindow deferred (file doesn't exist). |
| 7 (US5 — P2) | Format settings parity | 5/20 | Sample-SQL persistence (T069). `Ctrl+K,Y` binding verified across all 6 host VSCT files (T086). 11 formatter pipeline gap closures (T074–T084) deferred to dedicated PR. Parity corpus (T071–T073) blocked on Redgate install. |
| 8 (US6 — P3) | History / Tab Coloring / Code Analysis | 7/7 | Produced `tab-coloring-audit.md` (FR-011a) — 16 SQL Prompt §5.1 features graded against Phase 5: 5 Matches, 1 flexible, 4 Differs, 2 Partial, 5 Missing. All other US6 surfaces already token-driven. |
| 9 (US7 — P3) | AI / ghost text / margins | 4/4 | All 4 surfaces verified clean via hex-literal scan. `GhostTextAdornment` already reads `ThemeTokens.TextDisabled`. `SchemaProgressMargin` already uses `EditorSpinnerStroke`. |
| 10 (Polish) | — | 5 done / 9 | Doc updates (this entry + architecture.md + formatting.md + configuration.md + ipc-api.md). DPI / a11y / screenshot audits deferred (need running product). |

### Clarifications recorded in `spec.md`

Five Q&A bullets resolved upstream via `/speckit.clarify` before implementation began plus one mid-implementation reality check:

| Q | Answer |
|---|---|
| SC-007 match definition | Strip trailing whitespace per line → `\n` EOLs → drop UTF-8 BOM → byte-exact compare. |
| Built-in styles | Ship 3+ read-only Native styles transcribed from SQL Prompt defaults; never redistribute Redgate-authored binaries. |
| Tab Coloring scope (FR-011) | Visual parity only; Phase 5 assignment-rule engine untouched; audit doc is the FR-011a deliverable. |
| Unsupported settings UX (FR-023) | Render in tree at natural group location, control disabled, value visible, "Unsupported" badge adjacent. |
| Active-style scope (FR-027b) | Global per user; single `AppSettings.FormatterSettings.ActiveProfile` string; never split per-host. |
| File format reality | SQL Prompt distributes `.sqlpromptstylev2` XML, not the JSON the spec originally assumed. Spec corrected mid-implementation; existing `SqlPromptImporter` handles the real format. |

### Headline numbers

- **72 of 106 tasks** complete (67.9 %) on the branch as of this entry.
- **15 of the 34 not-done tasks** are explicitly deferred (Redgate-install dependency, multi-session formatter work, manual product-running audits).
- **0 commits to master** — work lives on `020-sqlprompt-visual-parity` awaiting review.
- **Deleted legacy code**: `src/AkmlSql.Shell.Shared/Ui/ThemeManager.cs` removed entirely after the 5 remaining call-sites migrated.

### Open follow-ups for the next session

1. **Formatter pipeline gap closure** (T074–T084) — 11 layout rules to add to the 7-stage pipeline.
2. **Parity corpus** (T071–T073) — needs Redgate install for golden generation.
3. **Menu wire** (T059) — VSCT edits across 6 hosts to launch the editor from a top-level menu.
4. **Options dialog re-skin** (T044–T048) — touches shipping production UI.
5. **DPI / a11y / screenshot audits** (T098–T100) — need running product and side-by-side comparison.

*Last updated: 2026-05-15*
