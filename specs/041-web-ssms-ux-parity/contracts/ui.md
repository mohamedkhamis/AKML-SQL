# Contract: web UI (layout, keys, commands, settings, status bar, dialogs, documents)

**Feature**: 041-web-ssms-ux-parity

Exact names, keys and numbers the implementation and the tests share. Planning constants marked
*(validate)* are confirmed against the real CodeMirror gutter width before being frozen.

---

## 1. Workspace layout (FR-040..FR-048)

```text
┌ nav (hideable on the editor route) ───────────────────────────────────────────┐
├ toolbar: [db selector] | [Execute F5] [Parse Ctrl+F5] | [Format] [Analyse] | [Refactor ▾] [AI ▾] | [New] [Open] [Save] [Save as] | [View ▾]  rows/timeout quick control ┤
├ document bar: <icon> SQLQuery1*                                                 ┤
│ ┌schema┐ ║ ┌──────────── editor ────────────┐ ║ ┌ AI ┐ │
│ │      │ ║ │                                │ ║ │    │ │
│ │      │ ║ ╞══════════ hsplit ══════════════╡ ║ │    │ │
│ │      │ ║ │ results pane: Results │ Messages │ Problems (badge) │ ║ │    │ │
│ └──────┘ ║ └────────────────────────────────┘ ║ └────┘ │
├ status: [outcome] [pill] [server · database] [rows · ms] [Ln, Col] [Web x | Engine x] ┤
```

| Constant | Value | Notes |
|---|---|---|
| Editor minimum width | 920 px *(validate)* | 120 Consolas-13 characters ≈ 858 px + gutter + scrollbar (SC-003) |
| Editor minimum height | 320 px | existing |
| Schema panel min / default | 180 / 260 px | |
| AI panel min / default | 300 / 380 px | |
| Results pane min / default | 120 px / 38 % of the workspace height | |
| Edge strip | 24 px | `<button>` with `writing-mode: vertical-rl`, `title="Show Schema (F8)"` |
| Splitter track / hit area | 6 px / 12 px | track colour `--akml-border-splitter`, hover/focus `--akml-accent-primary` |
| Splitter keyboard step | 16 px (Shift: 64 px) | Home/End → min/max; Enter/Space collapse/restore; defaults cancelled by a targeted listener (Tab is never cancelled) |
| Fold-away rule | measured | when `workspace − visible side widths − splitters < editor minimum`: fold AI first, then Schema; with default sizes Schema folds below ≈ 1210 px, so at 1100 px both are folded; derived, not stored |
| `.akml-main` padding | 0 on the editor route | 12 px elsewhere until FR-085 unifies pages |

Regions and their toggles: navigation bar (`view:toggle-nav`), Schema panel (`view:toggle-schema`,
F8), AI panel (`view:toggle-ai`), results pane (`view:toggle-results`, Ctrl+R),
maximise/restore results (`view:maximise-results`), cycle panes (`view:cycle-panes`, F6),
reset layout (`view:reset-layout`). The toolbar and status bar are always visible.

## 2. Keyboard map (FR-028, FR-049)

Claimed only while the editor page is mounted (document-level capture listener; ignored while
an `[aria-modal="true"]` element is open; `e.repeat` ignored; `e.code` matched).

| Keys | Command | Notes |
|---|---|---|
| F5 | `editor:execute` | also Ctrl+Enter (CodeMirror keymap, unchanged) |
| Ctrl+F5 | `editor:parse` | |
| Ctrl+R | `view:toggle-results` | |
| F8 | `view:toggle-schema` | `lintKeymap` is **not** installed (it binds F8) |
| F6 / Shift+F6 | `view:cycle-panes` forward / back | editor → results → schema → AI → toolbar; **verify** cancellability per browser (FR-049(c)); fallback shown in the menu if a browser keeps it |
| Ctrl+K, Ctrl+F / Ctrl+L / Ctrl+S | format / analyse / surround | 1.5 s chord handled in JS so the second stroke never reaches CodeMirror's Find |
| Ctrl+S | `editor:save` | |
| Ctrl+Shift+S | `editor:saveas` | |
| Ctrl+Alt+N | `editor:new` | Ctrl+N is browser-reserved |
| Ctrl+Alt+O | `editor:open` | Ctrl+O is browser-reserved |
| Ctrl+P | Command Palette | existing |

Grid keys (only while a grid cell has focus; the inline editor claims only Tab/Enter/Escape):
Arrows, Home/End, PageUp/PageDown, Ctrl+Home/End, Shift+Arrow (extend), Ctrl+A (select all),
Ctrl+C (copy TSV), Ctrl+Shift+C (copy with headers), Ctrl+0 (Set to NULL), Enter / F2 /
printable character (edit), Escape (cancel edit), Tab / Shift+Tab (next / previous editable
cell), Delete (toggle row delete when the row-number cell is focused), Shift+F10 / Menu
(context menu), Shift+Alt+Left/Right (resize the focused column by 16 px).

Browser-reserved and not attempted: Ctrl+N, Ctrl+T, Ctrl+W, Ctrl+Tab, Ctrl+Shift+Tab.
`ShortcutCollisionTests` asserts no duplicate and no collision with CodeMirror's bound keys
(Mod-Enter, Mod-F, F3, Mod-G, F12, Escape, Tab, Mod-Z/Y, Mod-/, Mod-D).

## 3. Command ids (`ICommandRegistry`, View menu, palette)

| Id | Title | Shortcut | Group | Checked / Enabled |
|---|---|---|---|---|
| `editor:execute` | Execute | F5 | Action | enabled when not running and connected |
| `editor:parse` | Parse | Ctrl+F5 | Action | enabled when `execute.v2` and connected |
| `editor:new` | New query | Ctrl+Alt+N | File | |
| `editor:open` | Open file… | Ctrl+Alt+O | File | |
| `editor:save` | Save | Ctrl+S | File | |
| `editor:saveas` | Save as… | Ctrl+Shift+S | File | |
| `view:toggle-nav` | Navigation bar | | View | checked = visible |
| `view:toggle-schema` | Schema panel | F8 | View | checked |
| `view:toggle-results` | Results pane | Ctrl+R | View | checked |
| `view:toggle-ai` | AI panel | | View | checked (replaces `editor:ai`) |
| `view:maximise-results` | Maximise results / Restore results | | View | |
| `view:cycle-panes` | Next pane | F6 | View | |
| `view:reset-layout` | Reset layout | | View | |
| `editor:format`, `editor:analyse`, `editor:refactor`, `editor:surround` | *(existing)* | | | |

`CommandAction` gains optional `Func<bool>? IsChecked` and `Func<bool>? IsEnabled`.

## 4. Results pane (FR-020..FR-035)

- Tabs: `Results` · `Messages` · `Problems` (badge = findings passing the current severity
  filters; hidden when 0 or analysis is off). Always present.
- Active tab after Execute: Results when ≥ 1 set and no error, else Messages; after Analyse:
  Problems when findings > 0 (pane shown if hidden).
- Empty state: "Run a query to see results here (F5)". Running state: spinner + elapsed time;
  previous results dimmed and marked stale.
- Stacked sets: one `ResultSetGrid` per set with a divider and header `Result N (M rows)`; a
  "jump to set" control when > 5 sets; each editable set has its own Apply / Discard bar.
- Messages text: `Msg 208, Level 16, State 1, Line 4` (+ `Procedure <name>, ` before `Line`)
  then the message; `(N rows affected)` / `(N rows returned; more exist)` / `(0 rows affected)`;
  PRINT text; `Commands completed successfully.`; `Beginning execution loop` /
  `Batch execution completed N times.`; `The remaining N batch(es) did not run because the
  connection was closed.`; last line `Completion time: 2026-10-08T12:34:56.1234567+02:00`
  (browser local time). Errors are clickable when they map to a document line.
- Save results as: menu item `Save results as…` → format choice (CSV comma-delimited / tab
  delimited) → confirmation when truncated or holding engine-cut values → download
  `<document>.csv` / `.tsv`, UTF-8 with BOM.

## 5. Grid menus (FR-005, FR-017)

Cell / selection menu, in order: Copy (Ctrl+C) · Copy with headers (Ctrl+Shift+C) · Copy as ▸
(CSV · JSON · Markdown table · INSERT statements) · Select all (Ctrl+A) · View value ·
Set to NULL (Ctrl+0; editable only; disabled with reason) · Save results as CSV… · — ·
Delete row / Restore row (editable only).

Column header menu: Auto-fit this column · Auto-fit all columns · Reset column widths · Copy
column name · Sort ascending · Sort descending · Clear sort.

Column widths: min 48 px, max 480 px, default 120 px; header sets the floor; row-number
column sized by digit count.

## 6. Settings (FR-050..FR-064)

Routes: `/settings` (General), `/settings/{section}`, `/diagnostics` (alias of
`/settings/diagnostics`); aliases `ai` → `ai-assistance`, `schema-cache` → `schema-cache`;
unknown → General.

| Section id | Title | Contents | Restore defaults removes |
|---|---|---|---|
| `general` | General | Theme (System / Light / Dark / High contrast) | theme |
| `editor` | Editor | Font size, Word wrap, Tab size; Reset editor session (confirm, names the document) | editor options |
| `format` | Format | Active style (label "Style"), Format styles… link | active style → default |
| `queries` | Queries | Default row limit, Command timeout, Show column types, Retain line breaks on copy or save | the four defaults (and the session override) |
| `code-analysis` | Code analysis | Enable code analysis, Re-analyse after format, Default Problems filters, Suppressed rules (Undo) | switches, filters, suppressed rules |
| `ai-assistance` | AI assistance | Providers (Active radio, Needs key badge), Add/edit form, Privacy modes, Ghost text | providers **and their keys**, active provider, privacy, ghost text (not chat history) |
| `connections` | Connections | Engine connections (add / connect / remove / re-pair), SQL Server connections (saved, connect, disconnect, manage) | *(no Restore defaults)* |
| `schema-cache` | Schema cache | Cached databases list, Refresh, Clear, Clear all | = Clear all (confirm names N databases and size) |
| `diagnostics` | Diagnostics | Level filters, log table, Export, Clear | level filters |

Header: filter box (`settings-filter`, searches every section; greys zero-match sections),
Restore all defaults, Export…, Import…. Every destructive action confirms through
`IDialogService` with the section's `DescribeResetAsync()` lines.

Save model: simple preferences apply at once with a notification; the provider form and the
connection manager keep explicit Save / Cancel.

## 7. Status bar segments (FR-083)

Left to right: outcome (`Query executed successfully.` / `Query completed with errors.` /
`Executing query…`, icon + text, `role=status aria-live=polite`) · availability pill (existing
texts; click → `/diagnostics`) · `Server/Database` (existing text format, kept for the E2E
suites; click → connection manager; login shown beside it when known) · `16 rows · 87 ms` or
elapsed counter · `Ln 1, Col 12` · `Web 1.26.1007.0613` ·
`Engine 1.26.1007.0613` (raw string with hash on hover; `triangle-alert` "versions differ"
when the stripped versions disagree — expected in developer builds, where each project stamps
its own build minute; release builds built with one `-p:Version` match; the web segment reads
the web assembly's own version).

## 8. Dialogs and notifications (FR-086)

`IDialogService.ConfirmAsync(DialogRequest)`, `PromptAsync(PromptRequest)`, `AlertAsync` —
`DialogRequest { Title, Message, Details, ConfirmLabel = "OK", CancelLabel = "Cancel",
Destructive }`. Cancel is focused on open and is the Escape target; a destructive action button
is never the Enter target. Dialogs with custom bodies (View value, History Compare) are their
own components on the shared `.akml-scrim` / `.akml-dialog` styles and focus helpers.
`INotificationService.Notify(text, kind, duration)`: info/success auto-dismiss after 4 s,
errors stay; at most three visible; `role=status` / `role=alert`. Both hosts are
`position:fixed` (MainLayout's grid has exactly three in-flow rows). The Format styles page
keeps its inline `styles-status` line. Browser `alert/confirm/prompt` are never used (SC-012).

## 9. Tab strip (FR-070)

`<TabStrip Tabs ActiveId ActiveIdChanged AriaLabel Trailing>`; `TabItem(Id, Label, Badge,
BadgeKind, TestId, Title)`; 28 px tall; active marker = 2 px bottom border
`--akml-accent-primary`; Left/Right wrap and activate; Home/End activate first/last; Enter/Space
activate; `aria-selected`, `aria-controls`; `Trailing` renders beside the `role=tablist`, never
inside it; consumers render `role=tabpanel`; existing tab ids (`ai-tab-chat`, `preview-mysql`,
…) are preserved through `TestId`.

## 10. Documents (FR-072, FR-073, FR-089)

Default name `SQLQuery{n}` (n counts up within the visit; never reused). Modified = text ≠
baseline; `*` after the name in the document bar and the browser title
`SQLQuery1* - localhost.master - AKML SQL`. New / Open confirm when modified. Open accepts
`.sql`/`.txt`, UTF-8 (with or without BOM) and UTF-16 LE/BE (BOM), refuses files above the
10 MB document limit naming it. Save prompts for a name only while the name is the default;
Save as always prompts; downloads `{name}.sql` as UTF-8 with BOM. History: `TabTitle` = the
name when not default, else null (engine names it `query-NN`); a new `SessionKey` per New / Open.

## 11. Schema panel (FR-048, FR-093)

Header `server · database`, Refresh (sends `SchemaRefreshRequest` 6 as a **notification**,
re-arms polling, then fetches Phase A and B and reloads the tree), filter box
(`aria-label="Filter objects"`, 150 ms debounce, parents kept, matches expanded); icons per kind
from `--akml-iconbadge-*` (keys use the `index` token); when disconnected: "Connect to SQL
Server…" button; loading row while the schema loads.

## 12. Icons (FR-081)

Lucide sprite `lib/icons/akml-icons.svg`, `<Icon Name="play" Size="16" />`; names used:
play, file-check (parse), wand-sparkles (format), search-check (analyse), replace (refactor),
bot (AI), file-plus, folder-open, save, save-all, database, table-2, eye, square-function,
scroll-text, key-round, columns-3, zap, list-ordered, link, scissors, history, settings, plug,
plug-zap, refresh-cw, x, chevron-down, chevron-right, panel-left, panel-right, panel-bottom,
maximize-2, minimize-2, circle-check, circle-alert, triangle-alert, info, loader-circle, copy,
download, filter, search, star, ellipsis-vertical, file-code.
