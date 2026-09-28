# Contract: user-visible structure (SSMS 22 plugin)

**Feature**: 040-sqlprompt-ui-parity

These are the exact structures, labels and keys the UI must present. Tests and the quickstart
check against this file.

**Wording rules:**
- Sentence case throughout.
- SQL Prompt's wording wherever an equivalent exists.
- Window titles use an en dash: `AKML SQL – ‹Name›`.

---

## 1. Options tree (FR-050)

**How to read the table:**
- Page keys don't change; only labels move. That keeps deep links and tests working.
- The breadcrumb, `IPageBuilder.Display`, is `Group › Page`, or just `Page` for leaves at the
  top level.
- "Help topic" is the F1 target: `https://akml.khamis.work/docs/` + the slug.

| Group | Page label | Page key (unchanged) | Help topic (F1) |
|---|---|---|---|
| — | General | `General` | `topics/options#general` |
| Suggestions | Behavior | `IntelliSense` | `topics/options#suggestions-behavior` |
| Suggestions | Types of suggestion | `SuggestionTypes` | `topics/options#suggestions-types-of-suggestion` |
| Suggestions | Tooltips | `CompletionPolish` | `topics/options#suggestions-tooltips` |
| Suggestions | Connections | `ConnectionScope` | `topics/options#suggestions-connections` |
| Suggestions | Join conditions | `JoinOptions` | `topics/options#suggestions-join-conditions` |
| Suggestions | Snippets | `Snippets` | `topics/options#suggestions-snippets` |
| Suggestions | Warnings & highlighting | `Safety` | `topics/options#suggestions-warnings-highlighting` |
| Inserted code | Objects & statements | `InsertOptions` | `topics/options#inserted-code-objects-statements` |
| Inserted code | Qualification | `Qualification` | `topics/options#inserted-code-qualification` |
| Inserted code | Aliases | `Aliases` | `topics/options#inserted-code-aliases` |
| Inserted code | Special characters | `SpecialCharacters` | `topics/options#inserted-code-special-characters` |
| Format | Styles | `Formatting` | `topics/options#format-styles` |
| — | Navigation | `Navigation` | `topics/options#navigation` |
| Queries | Query results | `Grid` | `topics/options#queries-query-results` |
| Queries | History | `History` | `topics/options#queries-history` |
| Queries | Color | `Tabs & UI` | `topics/options#queries-color` |
| Queries | Execution | `Execution` | `topics/options#queries-execution` |
| Editor | Productivity | `Editor` | `topics/options#editor-productivity` |
| Editor | Refactoring | `Refactoring` | `topics/options#editor-refactoring` |
| — | Code analysis | `Code Analysis` | `topics/options#code-analysis` |
| — | Connections & memory | `ConnectionsMemory` | `topics/options#connections-memory` |
| — | AI assistance | `AI Assistance` | `topics/options#ai-assistance` |

**Pages removed from the tree** (every row on them does nothing, OPT-01):
- `Schema Cache` ("Suggestions › Database");
- `Labs`.

Their builders stay registered only if still needed for reset or import; otherwise remove them
and their reset cases.

**Rows that move page:**
- Restore on start, Maximum to restore, Reconnect restored queries, Auto-save interval and
  Max closed tabs move from `Tabs & UI` to `History`.
- Any schema-cache row still shown moves to `ConnectionsMemory`.

**Window chrome (unchanged, except where stated):**
- Search box ("Search options… (Ctrl+E)").
- Page header band: breadcrumb, "?" help toggle and **Restore defaults**.
- Bottom bar: **Restore all defaults**, **Import…**, **Export…** · **OK**, **Cancel**.
- The Import success text no longer mentions Apply.

**Restore defaults confirmation:**
`Reset the settings on ‹Display›?` When the page carries data beyond its visible rows, a
second line names it, for example `This also removes your 3 AI agents and their API keys.`

---

## 2. AKML SQL menu (FR-060, FR-034, SC-015)

The menu is built by `EnsureTopLevelMenu` (DTE) from one table, carrying the version marker
`akml-menu-v2`. There are **12 top-level entries**, which meets SC-015's cap. `│` marks a
separator; `▸` marks a submenu.

```text
AKML SQL
  Format Document                 Ctrl+K, Y
  Format Selection
  Active Style ▸        (dynamic: up to 30 styles, ✔ on active) │ Edit Styles…
  Formatting ▸          Unformat Document (Ctrl+B, Ctrl+U) · Edit Formatting Styles… · Bulk Format… ·
                        Disable Formatting for Selection
  │
  Refactor ▸            Smart Rename · Script as ALTER · Inline EXEC · Inline Stored Procedure ·
                        Convert INSERT to UPDATE · Split Table · Find Invalid Objects
  Navigate ▸            Go To Definition (F12) · Peek Definition (Alt+F12) · Find All References (Shift+F12) ·
                        Object Search (Ctrl+T) · Next Statement · Previous Statement · Matching Pair ·
                        Document Outline │ Toggle Bookmark · Next Bookmark · Previous Bookmark
  │
  SQL History                     Ctrl+Alt+H
  Tabs ▸                Restore Closed Tab (Ctrl+Shift+T) · Close All Unmodified · Duplicate Tab · Pin Tab
  │
  AI ▸                  Chat Panel · Explain SQL · Fix SQL        (shown only when AI is enabled, as today)
  Tools ▸               Command Palette (Ctrl+Shift+P) · Execute Current Statement (Alt+Enter) · Execute to Cursor │
                        Snippet Manager · Surround Selection With Snippet… │
                        Manage Code Analysis Rules… · Toggle Code Analysis │ Export Results Grid
  │
  Options…
  Help ▸                Send Feedback · View Logs · Refresh Schema Cache (Ctrl+Shift+D) │ Check for Updates · About AKML SQL
```

**Rules:**
- **Commands with no registered handler** (TextToSql, AI Optimize, AI Index Analysis,
  Generate CRUD Procedures, Find in Results Grid) are **not** placed. They are recorded as
  follow-ups.
- **Command IDs, canonical names and key bindings are unchanged.** Keyboard shortcuts work
  whatever the menu nesting.
- **VSCT groups** mirror this structure. The VSCT menu is currently invisible in SSMS 22
  (research N1).
- **The editor context menu** gets `Active Style ▸` plus `Format Document`. It is added to the
  SSMS query-editor context command bar found at runtime. If no bar is found, it is logged and
  skipped.

## 3. Keyboard maps

### SQL History tool window (FR-044)

| Focus | Key | Action |
|---|---|---|
| Search box | typing | Filters after 250 ms |
| Search box | Enter | Searches now; focus moves to the first row |
| Search box | Esc | Clears the text; if already empty, clears the filters |
| List | ↑ / ↓ / Home / End | Move the selection |
| List | Enter | Open query: switch to its tab when it's open, otherwise open it in a new tab |
| List | Delete | Remove query and its history (asks first) |
| List | F2 | Rename query (not while open) |
| List | Ctrl+C | Copy the selected version's SQL |
| List | Space | Star / un-star |
| List | Tab / Shift+Tab | Move to the versions list / preview / back |
| Preview | Ctrl+A, Ctrl+C | Select all, copy |
| Anywhere | F1 | `topics/sql-history` |

### Format Styles window (FR-036)

| Focus | Key | Action |
|---|---|---|
| Window | Ctrl+S | Save |
| Window | Ctrl+F | Focus "Search for options…" |
| Search box | Esc | Clears the search. A second Esc closes the window, through the Close button's `IsCancel` |
| Search box | Enter | Jump to the first match |
| Style list | F2 | Rename (not built-in or team) |
| Style list | Delete | Delete (refused for built-in, team and active styles, with a status message) |
| Style list | Enter / double-click | Set as active |
| Option label | Click | Toggles an on/off option |
| Window | F1 | `topics/formatting#edit-styles-with-live-preview` |

### Options window

The existing Ctrl+E / Ctrl+F (focus search) and Esc keys stay. **F1** opens the current page's
help topic.

---

## 4. Text that must match

| Where | Text |
|---|---|
| History search placeholder | `Search` |
| History row menu | `Open query` · `Copy SQL` · `Re-execute` · `Rename query` · `Compare…` · `Remove query and its history` · `Remove queries older than this…` |
| History toolbar overflow | `Export…` · `Clear history…` |
| History Delete confirmation | `Remove '‹name›' and its history?` |
| History Remove older | `Remove all queries older than ‹d MMM yyyy HH:mm›? Starred queries are kept. This can't be undone.` |
| History date groups | `Today` · `Yesterday` · `This week` · `Last week` · `This month` · `Older`, each followed by ` (n)` |
| History draft row | `Not executed` |
| History disconnected overlay | `History is unavailable — the AKML engine isn't connected.` [`Retry`] |
| History settings | `Enable SQL history` · `Maximum query size` · `Restore open queries when SSMS starts` · `Maximum number of queries to restore` · `Automatically reconnect restored queries` · `Remember advanced search settings` · `Automatically remove queries older than` · `Group repeated runs of the same query` |
| Advanced search | `Advanced search` · Period: `Everything`, `Last week`, `Last month`, `Last 3 months`, `Custom` · `Server` · `Database` · `Starred` · `Open` · `Reset` |
| Style editor search | `Search for options…` |
| Style option reset | Tooltip: `Back to SQL Prompt's default (‹value›)` |
| Status bar after format | `Formatted with '‹style›'` (about 4 s) |
| Active Style menu | Style names, ✔ on active · `Edit Styles…` |
| Team styles group | `TEAM STYLES` · unreachable: `Team styles unavailable — ‹folder› can't be reached` |
| Format SQL actions header | `When you run Format SQL, AKML SQL will:` |
| Restart note | `Takes effect after SSMS restarts` |
| Child row tooltip | `Takes effect when "‹parent›" is on` |

## 5. Window titles (FR-061)

Every AKML window title follows `AKML SQL – ‹Name›`, built with
`WindowTitles.For(name)`. Names used:

- `Options`
- `Format styles`
- `New style` / `Rename style`
- `Import summary: ‹style›`
- `Code analysis rules`
- `Snippet manager`
- `Surround with`
- `SQL History comparison`
- `Object search`
- `Command palette`
- `SQL authentication` / `Saved SQL credentials`
- `Execution warning`
- `Update available` / `Downloading update` / `Install update`
- `Log viewer`
- `Code analysis results`
- `Find invalid objects`
- `About`
- `Refactoring preview`
- `Edit cell`
- `Smart rename`
- `Split table — ‹table›`
- `Bulk format`
- `Formatting…`
- `Restore queries`
- `Text to SQL`
- `Generated SQL preview`
- `SQL explanation`
- `Fix preview`
- `Multi-database execution — ‹server›`
- `Multi-database results`
- `Column statistics: ‹column›`
- `Row details — row ‹n›`

Tool-window captions (SQL History, AI Chat, Document Outline, Find References) stay as they
are. VS shows them in tabs, where a product prefix doesn't fit.
