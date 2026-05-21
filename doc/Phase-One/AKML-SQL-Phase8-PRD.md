# AKML SQL — Phase 8: Productivity Toolkit

> **Version:** 1.0 | **Date:** March 2026 | **Author:** Mohamed Khamis
> **Status:** Ready for Implementation | **Classification:** Confidential
> **Depends on:** Phase 7 (SQL History & Tab Management) — editor hooks infrastructure
> **Branch prefix:** `008-productivity-toolkit`

---

## 1. Executive Summary

Phase 8 is a collection of 15+ productivity enhancements that individually seem small but collectively transform the SSMS experience. These are the "Swiss Army knife" features — Find in Results Grid, grid aggregates, data export, multi-database execution, Command Palette, document outline, and more. Each feature saves 30 seconds to 5 minutes per use; across a typical 8-hour workday, they compound into 30–60 minutes of recovered productivity.

This phase cherry-picks the best productivity features from every competitor — SSMSBoost's grid tools, dbForge's data visualizers, SQL Prompt's command palette — and packages them into a cohesive, integrated toolkit.

---

## 2. Features — Complete List

### 2.1 Results Grid Enhancements

| # | Feature | Description |
|---|---|---|
| 1 | **Find in Results Grid** | Ctrl+F in the results grid — search/highlight across all columns and rows with regex support |
| 2 | **Grid aggregates** | Select cells → status bar shows SUM, AVG, COUNT, MIN, MAX (like Excel) |
| 3 | **Copy data as formats** | Right-click → Copy As → CSV, TSV, JSON, XML, HTML table, INSERT statements, Excel format |
| 4 | **Export to Excel** | One-click export of entire result set to .xlsx with auto-formatted headers |
| 5 | **Export to file** | Export results to CSV, JSON, XML, SQL (INSERT scripts), or Markdown table |
| 6 | **Generate script from grid** | Right-click rows → Generate INSERT, UPDATE, or DELETE script for selected data |
| 7 | **Cell editing** | Double-click a cell to edit → generates and executes UPDATE statement (with confirmation) |
| 8 | **Data visualizer** | Select numeric column → quick chart (bar, line, pie) popup for visual data exploration |
| 9 | **Column statistics** | Right-click column header → show min, max, avg, distinct count, null count, data distribution |
| 10 | **Row numbering** | Optional row numbers column in results grid |
| 11 | **Freeze header row** | Results grid header stays visible when scrolling |
| 12 | **Transpose results** | Rotate results 90° (rows become columns) for single-row result inspection |
| 13 | **Null highlighting** | Visually distinguish NULL values from empty strings in the grid |

### 2.2 Editor Enhancements

| # | Feature | Description |
|---|---|---|
| 14 | **Command Palette** | `Ctrl+Shift+P` — searchable list of all AKML SQL commands and SSMS commands |
| 15 | **Document Outline** | Side panel showing the structure of the current script (statements, procedures, CTEs, temp tables) |
| 16 | **Highlight occurrences** | Click on an identifier → all occurrences highlighted in the editor |
| 17 | **Bracket/pair matching** | Visual highlighting of matching BEGIN/END, parentheses, CASE/END, TRY/CATCH pairs |
| 18 | **Navigate between queries** | Ctrl+PageUp/PageDown to jump between SQL statements in a script |
| 19 | **Navigate to matching pair** | Ctrl+] jumps to matching BEGIN/END, parenthesis, CASE/END |
| 20 | **Named regions** | `--region Name` / `--endregion` collapsible code regions |
| 21 | **Sticky scroll** | Current procedure/statement name stays visible when scrolling through large scripts |
| 22 | **Minimap** | Compact code overview in the right margin (like VS Code) |
| 23 | **Multi-cursor editing** | Ctrl+Alt+Click for multiple cursors; Ctrl+D to select next occurrence |

### 2.3 Execution Enhancements

| # | Feature | Description |
|---|---|---|
| 24 | **Execute current statement** | Alt+Enter executes only the statement at cursor position (no need to highlight) |
| 25 | **Execute to cursor** | Execute all statements from the beginning up to the cursor position |
| 26 | **Multi-database execution** | Execute the same script against multiple databases simultaneously with comparison view |
| 27 | **Execution notifications** | Windows toast notification when a long-running query completes (configurable threshold) |
| 28 | **Execution timer** | Live elapsed time display in status bar during query execution |
| 29 | **CRUD generation** | Right-click table in Object Explorer → Generate SELECT/INSERT/UPDATE/DELETE procedures |
| 30 | **Script as...** | Extended scripting options: Script table as CREATE, INSERT, SELECT, MERGE, BCP, etc. |

### 2.4 Connection & Navigation

| # | Feature | Description |
|---|---|---|
| 31 | **Go to definition** | F12 on any object name → navigate to its definition (CREATE script) |
| 32 | **Peek definition** | Alt+F12 → inline preview of object definition without leaving current tab |
| 33 | **Find all references** | Shift+F12 → list all references to an object across open files and database |
| 34 | **Object search** | Ctrl+T → quick search for any database object by name (jump to definition) |
| 35 | **Connection aliases** | Assign friendly names to server connections (e.g., "Production - East Coast") |

---

## 3. Command Palette — Deep Dive

The Command Palette (`Ctrl+Shift+P`) is a searchable command launcher that unifies access to every AKML SQL feature and common SSMS commands:

```
┌─────────────────────────────────────────────────┐
│  > [format current do...]                        │
│                                                 │
│  📝 Format SQL                  Ctrl+K, Y       │
│  📝 Format Selection            Ctrl+K, F       │
│  🔧 Edit Formatting Profiles                    │
│  🔧 Switch Formatting Profile → Compact         │
│  🔍 Find in Results Grid       Ctrl+F (Grid)    │
│  📊 Run Code Analysis                           │
│  📋 Open SQL History           Ctrl+Alt+H       │
│  ⚙  Open Options                                │
│  ...                                            │
└─────────────────────────────────────────────────┘
```

Commands are fuzzy-matched, frequently-used commands rise to the top, and keyboard shortcut hints are shown alongside each command.

---

## 4. Configuration

| Setting | Default | Description |
|---|---|---|
| `grid.findShortcut` | `Ctrl+F` (in grid context) | Find in Results Grid shortcut |
| `grid.aggregates` | `true` | Show aggregate calculations on selection |
| `grid.nullHighlight` | `true` | Visually distinguish NULL values |
| `grid.rowNumbers` | `false` | Show row numbers column |
| `editor.commandPaletteShortcut` | `Ctrl+Shift+P` | Command Palette shortcut |
| `editor.highlightOccurrences` | `true` | Auto-highlight identifier occurrences |
| `editor.bracketMatching` | `true` | Visual bracket/pair matching |
| `editor.namedRegions` | `true` | Enable collapsible named regions |
| `editor.stickyScroll` | `true` | Enable sticky scroll |
| `editor.minimap` | `false` | Enable minimap |
| `execution.currentStatementShortcut` | `Alt+Enter` | Execute current statement shortcut |
| `execution.notificationThreshold` | `30` | Seconds before completion notification triggers |
| `execution.multiDatabase` | `true` | Enable multi-database execution feature |

---

## 5. Timeline & Milestones

| Week | Milestone | Deliverable |
|---|---|---|
| 1–2 | Results Grid enhancements | Find in Grid, aggregates, copy-as formats, export to Excel/file, script generation |
| 3–4 | Results Grid advanced + Editor basics | Cell editing, data visualizer, column stats, transpose. Command Palette, document outline, highlight occurrences. |
| 5–6 | Editor advanced + Execution | Bracket matching, named regions, sticky scroll, multi-cursor. Execute current statement, multi-DB execution, notifications. |
| 7–8 | Navigation + CRUD + QA | Go to definition, peek, find references, object search. CRUD generation. Full test matrix. |
| 9–10 | Integration & polish | Multi-database execution comparison view, connection aliases, minimap, final QA and performance tuning |

**Total estimated duration: 10 weeks** (2.5 months).

---

## 6. Competitive Comparison

| Feature | SQL Prompt | SSMSBoost | dbForge | DataGrip | AKML SQL Phase 8 |
|---|---|---|---|---|---|
| Command Palette | ✔ | No | No | ✔ | **✔** |
| Find in Results Grid | No | ✔ | ✔ | ✔ | **✔** |
| Grid aggregates | ✔ | No | ✔ | ✔ | **✔** |
| Export to Excel | No | ✔ | ✔ | ✔ | **✔** |
| Data visualizer | No | No | ✔ | ✔ | **✔** |
| Document outline | No | No | ✔ | ✔ | **✔** |
| Multi-database exec | No | No | ✔ | ✔ | **✔** |
| Execute current stmt | ✔ | No | ✔ | ✔ | **✔** |
| CRUD generation | No | No | ✔ | No | **✔** |
| Go to definition | ✔ | ✔ | ✔ | ✔ | **✔** |
| Peek definition | No | No | No | ✔ | **✔** |
| Find all references | No | No | No | ✔ | **✔** |
| Named regions | ✔ | No | ✔ | No | **✔** |
| Multi-cursor | No | No | No | ✔ | **✔** |
| Sticky scroll | No | No | No | ✔ | **✔** |
| Minimap | No | No | No | ✔ | **✔** |
| Transpose results | No | No | No | ✔ | **✔** |
| Cell editing | No | No | ✔ | ✔ | **✔** |
| Completion notifications | No | No | ✔ | No | **✔** |
| Bracket matching | ✔ | No | ✔ | ✔ | **✔** |

---

*End of Phase 8 PRD — AKML SQL v1.0*
