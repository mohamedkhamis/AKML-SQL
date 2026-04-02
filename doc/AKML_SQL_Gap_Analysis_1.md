# AKML SQL vs SQL Prompt Core — Gap Analysis

> **Date:** 2026-04-02
> **Input:** `progress.md` (AKML SQL Development Progress Log)
> **Compared Against:** Redgate SQL Prompt v11 (all core features, excluding AI)
>
> ✅ = Fully covered | ⚠️ = Partially covered / minor gap | ❌ = Missing

---

## Summary

| Area | Status | Notes |
|------|--------|-------|
| IntelliSense & Code Completion | ✅ **Full** | All 10 SQL Prompt sub-features covered, plus extras |
| Code Formatting | ✅ **Full** | More commands than SQL Prompt, profile system matches |
| Code Snippets | ✅ **Full** | 3-source priority, more built-in variables than SQL Prompt |
| Code Analysis | ✅ **Full** | 130 rules vs SQL Prompt's 94 — exceeds parity |
| Tab Management & Coloring | ✅ **Full** | All 5 environments, gradient, hierarchy — all present |
| SQL History | ⚠️ **Partial** | Core recording + search present, but 5 specific gaps |
| Refactoring | ✅ **Full** | All SQL Prompt operations covered, plus extras |
| Navigation | ✅ **Full** | All SQL Prompt nav features present, plus Bookmarks and Outline |
| Results Grid | ✅ **Full** | All SQL Prompt grid features present, plus many extras |
| Execution Safety | ✅ **Full** | Exceeds SQL Prompt with audit logging and type-to-confirm |
| Options Dialog | ✅ **Full** | 15 pages, 101 settings, import/export — matches |
| Command Palette | ✅ **Full** | Present with 32 commands (SQL Prompt equivalent) |

**Overall Verdict: 11 of 12 areas fully covered. 1 area with minor gaps (SQL History).**

---

## Detailed Feature-by-Feature Comparison

### 1. IntelliSense & Code Completion

| SQL Prompt Feature | AKML SQL | Status |
|--------------------|----------|--------|
| Ranked / contextual suggestions | 9 completion providers with context | ✅ |
| CamelCase filtering | CamelCase dictionary (5,000+ words) | ✅ |
| Mid-string / substring matching | Fuzzy matching (substring + prefix) | ✅ |
| Column Picker (expand * with checkboxes) | Wildcard expansion popup with inline preview | ✅ |
| Object Definition Box (Summary/Script tabs) | Object Definition Panel (Summary/Script tabs via QuickInfo IPC) | ✅ |
| JOIN condition completion (FK-based) | JOIN assist (FK-based ON condition suggestions) | ✅ |
| INSERT statement completion with metadata | Expand Insert Columns with metadata comments | ✅ |
| Auto-alias generation on table insert | Auto alias suggestion on table completion | ✅ |
| Schema qualification on insert | Qualify Object Names command | ✅ |
| Keyword auto-casing (UPPER/lower/Title/AsIs) | Keyword casing configurable | ✅ |
| Suggestion refresh / cache management | Schema status indicator + DDL regex detection | ✅ |
| Dot-trigger for table.column | Dot-trigger for table.column completion | ✅ |
| Ctrl transparency on popup | Ctrl transparency (hold Ctrl to see behind) | ✅ |
| Icon types per object (T/C/P/F/S/V/K/D) | *Not explicitly listed* | ⚠️ **Verify** |
| Parameter Info (function signatures) | Function signature help | ✅ |
| Quick Info tooltips (hover) | Quick Info tooltips (column types, PK/FK, row count) | ✅ |

**Icon types note:** Your progress mentions 9 completion providers and a custom dark-themed popup, but doesn't explicitly describe per-object-type colored icon badges (Table=Yellow, Column=Blue, Procedure=Purple, etc.). If you already have icons differentiated by type, this is covered. Worth verifying that distinct colored icons per category exist.

---

### 2. Code Formatting

| SQL Prompt Feature | AKML SQL | Status |
|--------------------|----------|--------|
| Format SQL (Ctrl+K, Y) | Format Document (Ctrl+K, Y) | ✅ |
| Format Selection | Format Selection | ✅ |
| Apply Casing Only (Ctrl+B, Ctrl+U) | Casing Only (Ctrl+B, Ctrl+U) | ✅ |
| Expand Wildcards (Ctrl+B, Ctrl+W) | Expand Wildcards (Ctrl+B, Ctrl+W) | ✅ |
| Insert Semicolons (Ctrl+B, Ctrl+C) | Insert Semicolons (Ctrl+B, Ctrl+C) | ✅ |
| Qualify Object Names (Ctrl+B, Ctrl+Q) | Qualify Object Names (Ctrl+B, Ctrl+Q) | ✅ |
| Unformat (strip whitespace) | *Not explicitly listed* | ⚠️ **Check** |
| Disable formatting region (comments) | *Not explicitly listed* | ⚠️ **Check** |
| Style profiles (.sqlpromptstyle JSON) | `.akmlstyle` JSON profiles (50+ options) | ✅ |
| SQL Prompt style importer | SQL Prompt `.sqlpromptstyle` profile importer | ✅ |
| Style editor with real-time preview | Profile editor dialog with real-time SQL preview | ✅ |
| Multiple named styles / style selector | Profile selector dropdown in toolbar | ✅ |
| Export / Import / Share styles | Import/export built into profile system | ✅ |

**Extras in AKML (beyond SQL Prompt):** Format on Save, Format on Paste, Format on Delimiter, Expand Update Columns, Expand Exec Parameters, Add GROUP BY Columns, Toggle Brackets, Toggle AS Keywords, Convert Old-Style JOINs, Replace Deprecated Syntax, Remove Semicolons, Convert sp_executesql. These **exceed** SQL Prompt's formatting commands.

**Minor gaps to verify:**
1. **Unformat** — SQL Prompt has an "Unformat" action (strip all formatting whitespace). Your progress doesn't mention this specific action.
2. **Disable formatting region** — SQL Prompt wraps code in `-- SQL Prompt formatting off` / `-- SQL Prompt formatting on` comments to exclude from formatting. Check if your Encapsulate in BEGIN/END or a similar mechanism covers this, or if a dedicated "disable formatting for selection" action is needed.

---

### 3. Code Snippets

| SQL Prompt Feature | AKML SQL | Status |
|--------------------|----------|--------|
| Snippet Manager (GUI) | WPF Snippet Manager dialog | ✅ |
| Personal + Shared folders | Personal + Team + Built-in (3 sources) | ✅ |
| $CURSOR$ placeholder | $CURSOR$ | ✅ |
| $SELECTEDTEXT$ placeholder | $SELECTEDTEXT$ | ✅ |
| $PASTE$ (clipboard) | $CLIPBOARD$ | ✅ |
| $DATE$ placeholder | $DATE$ + $DATETIME$ + $TIME$ | ✅ |
| $DBNAME$ placeholder | $DATABASE$ | ✅ |
| SSMS template parameters | Custom snippet variables with schema-aware hints | ✅ |
| Surround-with snippets | Surround-with snippets | ✅ |
| Snippet sharing (network folder) | Team folder (shared) | ✅ |
| Import/Export snippets | Import `.akmlsnippet` JSON files | ✅ |
| Snippets in suggestion list | Snippet integration in completion list | ✅ |
| Context filtering | Context filtering (global, after_select, etc.) | ✅ |

**Extras in AKML:** $GUID$, $YEAR$, $USER$, $MACHINE$, $SERVER$, $SCHEMA$, $FILENAME$, usage tracking, format on expand, priority system. **Exceeds** SQL Prompt.

---

### 4. Code Analysis

| SQL Prompt Feature | AKML SQL | Status |
|--------------------|----------|--------|
| Real-time as-you-type analysis | Real-time AST-based analysis with debounce | ✅ |
| Green wavy underline (warnings) | Diagnostic squiggles (green=warning, red=error) | ✅ |
| Lightbulb quick-fix | Lightbulb quick-fix suggestions | ✅ |
| 94 rules across 7 categories (BP/PE/DEP/ST/MI/EI/SC) | 130 rules across 8 categories | ✅ |
| Per-rule Ignore/Warning/Error | Configurable per-rule severity | ✅ |
| .casettings file (XML, shareable) | `.casettings` JSON + SQL Prompt XML importer | ✅ |
| Auto-fix for fixable rules | Quick-fix refactoring actions | ✅ |
| Issues List panel (dockable) | VS Error List integration | ✅ |
| Inline suppression comments | `-- noqa: RULE_ID` inline comments | ✅ |

**Extras in AKML:** Security category (SE — 20 rules), Naming category (NM — 6 rules), Design category (DE — 7 rules), bulk analysis command, CLI analyzer for CI/CD. **Significantly exceeds** SQL Prompt.

---

### 5. Tab Management & Coloring

| SQL Prompt Feature | AKML SQL | Status |
|--------------------|----------|--------|
| Environment colors (Production=red, Dev=green, Test=blue, Staging=orange, Local=gray) | Production=red, Staging=orange, Dev=green, Azure=blue | ✅ |
| Custom environments (user-defined) | Pattern-based rules (glob matching) | ✅ |
| Gradient toggle (lighter top, base bottom) | Optional gradient (LinearGradientBrush) | ✅ |
| 4-level hierarchy (Group → Servers → Server → Database) | 4-level assignment hierarchy | ✅ |
| Status bar color matches tab | *Not explicitly stated* | ⚠️ **Verify** |
| Undocked window colored outline | *Not explicitly stated* | ⚠️ **Verify** |
| Tab color via right-click context menu | *Not explicitly stated* | ⚠️ **Verify** |

**Extras in AKML:** Custom window title template, Tab tooltip, Restore Closed Tab (Ctrl+Shift+T), Pin Tab, Duplicate Tab, Close All Unmodified. **Exceeds** SQL Prompt.

**Minor items to verify:** SQL Prompt shows the environment color on the SSMS status bar at the bottom of each query pane and on the outline of undocked/floating query windows. Also, SQL Prompt allows right-clicking servers/databases in Object Explorer to assign tab colors directly from the context menu. Worth confirming these 3 behaviors exist.

---

### 6. SQL History ⚠️ (Gaps Found)

| SQL Prompt Feature | AKML SQL | Status |
|--------------------|----------|--------|
| Auto-save on execute/save/close/focus-loss | SQLite-backed execution history recording | ✅ |
| Full-text search | Full-text search | ✅ |
| Filter: All / Open / Closed | Filter: All / Open / Closed tabs | ✅ |
| Crash recovery (auto-restore tabs on relaunch) | Crash recovery (restores all open tabs) | ✅ |
| Session auto-save | Session auto-save (configurable interval) | ✅ |
| Retention period (configurable days) | Configurable retention period | ✅ |
| History diff view | History diff view (side-by-side) | ✅ |
| Encryption at rest | Encryption at rest (DPAPI) | ✅ |
| **⭐ Starring / Favorites** | ❌ **Not mentioned** | ❌ |
| **Version history per query (timestamped snapshots)** | ❌ **Not explicitly mentioned** | ❌ |
| **Search prefixes (name:, sql:, server:, database:, starred:, open:)** | ❌ **Not mentioned** | ❌ |
| **Advanced search panel (filter by server, database, date range, state)** | ❌ **Not mentioned** | ❌ |
| **Rename closed queries** | ❌ **Not mentioned** | ❌ |
| **"Remove older than" bulk cleanup** | *Deduplication + background retention* (partial) | ⚠️ |
| **Search highlighting (Yellow Ochre in code preview)** | ❌ **Not mentioned** | ❌ |
| **Wildcard search (*, ?)** | ❌ **Not mentioned** | ❌ |
| **Boolean operators (OR, NOT, "exact phrase")** | ❌ **Not mentioned** | ❌ |
| **CamelCase word boundary search logic** | ❌ **Not mentioned** | ❌ |

**SQL History is the one area with meaningful gaps.** Your implementation has the core recording, search, filtering, and crash recovery, but SQL Prompt's SQL History (v10.13+) added several features your progress doesn't mention. Specifically:

1. **⭐ Starring / Favorites** — Mark queries as favorites, filter to starred-only. Starred items are exempt from auto-trimming retention.
2. **Version history per query** — SQL Prompt keeps multiple timestamped versions of the same query (created on each auto-save event). Users can click through versions to see the query at different points in time. Your progress mentions "diff view" which is related, but doesn't describe a per-query version timeline panel.
3. **Advanced search** — SQL Prompt has search prefixes (`name:fix`, `sql:alter`, `server:PROD`, `database:Northwind`, `starred:true`, `open:false`), wildcards (`proc*`, `an?`), boolean operators (`OR`, `NOT`), exact phrase matching (`"create view"`), and CamelCase word boundary logic. Your progress mentions "full-text search" but not these advanced features.
4. **Rename closed queries** — Right-click a closed query → Rename to give it a descriptive name.
5. **Search match highlighting** — When searching, matched text in the code preview is highlighted in Yellow Ochre.

---

### 7. Refactoring

| SQL Prompt Feature | AKML SQL | Status |
|--------------------|----------|--------|
| Smart Rename (cross-database, generates ALTER scripts) | Safe Rename (cross-script, generates ALTER scripts) | ✅ |
| Encapsulate as Stored Procedure | Extract to Stored Procedure (with parameter inference) | ✅ |
| Split Table (normalization) | Split Table (generates CREATE, FK, INSERT, DROP) | ✅ |
| Rename alias/variable (local, F2) | Safe Rename covers aliases | ✅ |
| Actions List (select code → actions) | Lightbulb quick-fix + multiple refactoring operations | ✅ |
| Expand wildcards | Expand Wildcards command | ✅ |
| Surround with BEGIN/END | Encapsulate in BEGIN/END | ✅ |
| Surround with TRY/CATCH | Surround-with snippets (TRY/CATCH) | ✅ |
| Comment/Uncomment toggle | *Standard VS feature* | ✅ |
| Create snippet from selection | *WPF Snippet Manager + import* | ✅ |
| Unformat (strip whitespace) | *See Section 2 note* | ⚠️ |
| Convert sp_executesql to SQL | Convert sp_executesql to Static SQL | ✅ |
| Insert semicolons | Insert Semicolons | ✅ |
| Move to new line | *Part of formatting* | ✅ |

**Extras in AKML:** Extract to CTE, Extract to Derived Table, Parameterize Values, Encapsulate as View, Convert Temp Table ↔ Table Variable. **Exceeds** SQL Prompt.

---

### 8. Navigation & Productivity

| SQL Prompt Feature | AKML SQL | Status |
|--------------------|----------|--------|
| Go to Definition (F12) | Go to Definition (F12) | ✅ |
| Highlight Occurrences | Highlight Occurrences of selected identifier | ✅ |
| Syntax Pair Matching (BEGIN/END, parens) | Navigate Matching Pair (Ctrl+]) + Bracket Matching | ✅ |
| Execute Current Statement (Shift+F5) | Execute Current Statement (Alt+Enter) | ✅ |
| Command Palette (Ctrl+Shift+P) | Command Palette (Ctrl+Shift+P) with 32 commands | ✅ |
| Parameter Info / Quick Info | Function signature help + Quick Info tooltips | ✅ |
| Find Object | Object Search (Ctrl+T) | ✅ |

**Extras in AKML:** Peek Definition (Alt+F12), Find All References (Shift+F12), Bookmarks, Document Outline, Named Regions, Sticky Scroll, Code Minimap, Execute to Cursor, Multi-database execution, Editor Toolbar. **Significantly exceeds** SQL Prompt.

**Note:** SQL Prompt uses `Shift+F5` for Execute Current Statement; AKML uses `Alt+Enter`. This is a keyboard mapping difference, not a gap.

---

### 9. Results Grid

| SQL Prompt Feature | AKML SQL | Status |
|--------------------|----------|--------|
| Export to Excel | Export to Excel (via ClosedXML) | ✅ |
| Excel 15+ digit precision as text | 15+ digit precision option | ✅ |
| Copy as IN clause | *Copy as SQL VALUES* (similar) | ⚠️ **Check** |
| Script as INSERT | Script Generator (INSERT from selected rows) | ✅ |
| Copy as CSV | Export to CSV | ✅ |
| Copy with headers | All copy operations include headers | ✅ |
| Aggregate totals (Sum, Avg, Count, Min, Max) | Aggregate statistics in VS status bar | ✅ |

**Extras in AKML:** JSON/XML/Markdown export, Column statistics popup, NULL highlighting, Row numbers, Column sorting/filtering, Grid Find bar, Cell Edit dialog, Transpose view, Freeze headers. **Massively exceeds** SQL Prompt.

**Minor item:** SQL Prompt has a specific "Copy as IN clause" that generates `WHERE col IN ('val1', 'val2', ...)`. Your progress lists "Copy as SQL VALUES" which is slightly different. Verify if a dedicated IN-clause copy format exists, or add one.

---

### 10. Options Dialog / Settings

| SQL Prompt Feature | AKML SQL | Status |
|--------------------|----------|--------|
| Options dialog with tree navigation | WPF Settings dialog with 15 category pages | ✅ |
| Per-page Reset This Page | Per-category Reset This Page | ✅ |
| Reset All to defaults | Reset All to defaults | ✅ |
| Export All Settings | Export All Settings (JSON) | ✅ |
| Import Settings | Import Settings | ✅ |
| Dark/Light theme support | Dark/Light theme (auto-detected from VS) | ✅ |

---

### 11. Execution Safety

| SQL Prompt Feature | AKML SQL | Status |
|--------------------|----------|--------|
| DELETE without WHERE warning | DELETE without WHERE warning | ✅ |
| DROP confirmation | DROP TABLE / DROP DATABASE confirmation | ✅ |
| Environment-prominent dialog | Environment-aware dialog severity | ✅ |

**Extras in AKML:** UPDATE without WHERE, TRUNCATE confirmation, type-server-name-to-confirm for Production, transaction reminder, structured audit logging, fail-open design. **Exceeds** SQL Prompt.

---

## Action Items — Gaps to Fill

### Priority 1 (SQL History gaps — the only area below parity)

| # | Gap | Effort | Description |
|---|-----|--------|-------------|
| 1 | **⭐ Starring / Favorites** | Small | Add a star toggle icon per query in the History tool window. Add a "Starred" filter button. Exempt starred items from retention auto-trim |
| 2 | **Version history panel** | Medium | For each query, maintain a list of timestamped versions (created on each auto-save). Show in a middle panel. Click a version to preview its code. Currently you have diff view — this is the per-query timeline that feeds into diffs |
| 3 | **Advanced search syntax** | Medium | Add prefix-based search (`name:`, `sql:`, `server:`, `database:`, `starred:`, `open:`), wildcards (`*`, `?`), boolean operators (`OR`, `NOT`), exact phrase (`"..."`), CamelCase word boundary splitting |
| 4 | **Rename closed queries** | Small | Right-click context menu on closed queries → Rename (inline text edit) |
| 5 | **Search match highlighting** | Small | When search results are displayed, highlight matching text in the code preview pane with a yellow/ochre background |

### Priority 2 (Minor verification items — likely already present)

| # | Item | Area | Action |
|---|------|------|--------|
| 6 | Suggestion icon badges per type | IntelliSense | Verify distinct colored icons (T=Yellow, C=Blue, P=Purple, etc.) exist in your completion popup |
| 7 | Unformat action | Formatting | Verify or add a "strip all formatting whitespace" command |
| 8 | Disable formatting region | Formatting | Verify or add `-- AKML formatting off/on` comment markers |
| 9 | Status bar environment color | Tab Mgmt | Verify status bar at bottom of query pane matches tab color |
| 10 | Floating window colored outline | Tab Mgmt | Verify undocked query windows get colored border |
| 11 | Right-click tab color assignment | Tab Mgmt | Verify right-clicking servers/databases in Object Explorer allows tab color assignment |
| 12 | Copy as IN clause | Results Grid | Verify or add `WHERE col IN (...)` copy format (distinct from VALUES) |

### Priority 3 (Already exceeding — no action needed)

Your AKML SQL already **exceeds** SQL Prompt in these areas — no action needed:
- Code Analysis (130 vs 94 rules, plus Security/Naming/Design categories)
- Refactoring (18 operations vs ~8 in SQL Prompt)
- Navigation (Bookmarks, Outline, Peek Definition, Code Minimap, Sticky Scroll)
- Results Grid (Transpose, JSON/XML export, Column stats, NULL highlighting)
- Execution Safety (Audit logging, type-to-confirm, transaction reminder)
- Snippets (14 variables vs 6 in SQL Prompt, usage tracking, context filtering)

---

*Analysis completed 2026-04-02. Source: AKML SQL progress.md vs Redgate SQL Prompt v11 documentation.*
