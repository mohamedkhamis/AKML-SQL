# Quickstart: validating the SSMS-grade web workspace (spec 041)

**Feature**: 041-web-ssms-ux-parity

This file is the acceptance gate for "done" (Constitution, Development Workflow). Run the
automated suites first, then the manual scenarios in a browser against the Debug web app and a
Debug engine paired to a local SQL Server.

**Rules for manual checks:**
- Use a throw-away database (`tempdb` or a copy of Northwind). Several scenarios execute
  `UPDATE`s and apply grid edits.
- Export your settings first (Settings › Export…) — the reset scenarios wipe preferences.
- Check every new surface in **Light, Dark and High contrast** (Settings › General) and with
  the OS "reduce motion" preference on.

## 0. Build, deploy, automated suites

```bash
# Theme gate + solution (regenerates nothing; fails on drift)
pwsh scripts/generate-theme-css.ps1 -CheckOnly
MSBUILD="/c/Program Files/Microsoft Visual Studio/18/Enterprise/MSBuild/Current/Bin/MSBuild.exe"
"$MSBUILD" AKML-SQL.slnx -t:Restore -v:quiet
"$MSBUILD" AKML-SQL.slnx -t:Build -p:Configuration=Release -m -v:minimal

# Debug builds for the browser tests (a Release-only build silently tests stale code)
dotnet build src/AkmlSql.Web/AkmlSql.Web.csproj -c Debug
dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Debug
dotnet build tests/AkmlSql.Web.E2E.Tests/AkmlSql.Web.E2E.Tests.csproj -c Debug
tests/AkmlSql.Web.E2E.Tests/bin/Debug/net10.0/playwright.ps1 install chromium

# Unit and integration suites
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj     # Execution tests skip without (local) SQL Server
dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj
node --test tests/AkmlSql.Web.Tests/js/*.test.mjs
dotnet test tests/AkmlSql.Site.Tests/AkmlSql.Site.Tests.csproj --filter ThemeCssSyncTests

# Browser suites (dev VM: Debug web + engine, Chromium, SQL Server up)
dotnet test tests/AkmlSql.Web.E2E.Tests/AkmlSql.Web.E2E.Tests.csproj --filter "Category=BridgeE2E"
```

**Gates:**
- **Completion corpus** and **format-parity goldens**: untouched by this feature; must not move.
- **Copy-As goldens** (`GridTextFormatsTests`): identical to the SSMS edition's output captured
  before the move.
- **SC-008 check by check**: before the first code change, record the pass list of every suite
  above with `--logger "trx;LogFileName=<suite>.before.trx" --results-directory <local dir>`
  (exclude `SiteScreenshotTour` by name in `Web.E2E`), then at each slice gate run
  `pwsh scripts/compare-test-results.ps1 -Before <dir> -After <dir>` and paste its per-test
  pass-lists into `baseline/tests/` and the table into `baseline.md`. Known pre-existing red
  (listed once in `baseline.md`): the 42 `FormatterServiceTests` sp031 goldens.
- **Web CSS token gate** (`WebCssTokenTests`): the literal ratchet may only go down; every
  `--akml-*` used is defined.
- **Shortcut collisions** (`ShortcutCollisionTests`): green.

**Run for manual checks:**
1. Start the Debug engine with the bridge enabled (or pair with the installed service — note
   that styles then read "On this engine").
2. `dotnet run --project src/AkmlSql.Web -c Debug --no-launch-profile --urls http://localhost:5000`
3. Pair the browser (Settings › Connections) and connect to `localhost` / `tempdb`
   (Windows authentication).

---

## 1. Result columns (User Story 1)

1. Run `SELECT name, database_id, create_date FROM sys.databases;` — `database_id` is narrow,
   `name` wider, `create_date` reads `2025-10-01 23:57:23.880`; no visible value is cut.
2. Run `SELECT REPLICATE('x', 2000) AS wide, 1 AS n;` — `wide` stops at the maximum width with
   an ellipsis; hover shows the start of the value; right-click › View value shows it all with
   Copy.
3. Drag the right edge of `name` 120 px wider — it follows the pointer without lag; the other
   columns keep their widths; a horizontal scrollbar appears when needed; the header stays
   aligned while scrolling.
4. Narrow `name` by hand, then double-click its edge — it returns to its content width.
   Header menu › Auto-fit all columns fits every column.
5. Resize two columns, scroll a 1,000-row result to the bottom and back, resize the window, hide
   the Schema panel — the two widths survive. Run a new query — widths are fitted afresh.
6. Click row number 3, Shift-click row 6, Ctrl+C, paste into a spreadsheet — four rows in
   separate cells, NULL as `NULL`. Click a column header, Ctrl+Shift+C — the column name is
   the first line. Drag across cells — a rectangle is selected; Shift+Arrow extends it.
7. Right-click a cell — the menu lists Copy, Copy with headers, Copy as ▸, Select all, View
   value, Set to NULL (only for an editable result), Save results as CSV…; Shift+F10 opens it
   from the keyboard. Copy as › JSON / CSV / Markdown / INSERT paste as expected.
8. Click a column's sort control once, twice, three times — ascending, descending, original;
   numbers sort as numbers, dates as dates, NULL first; run again — no sort.
9. Editable result (`SELECT * FROM dbo.T` on a table with a key): click a cell — selected, not
   editing; Enter / F2 / double-click / typing — edits; Escape — cancels; Tab — next editable
   cell; Ctrl+0 — NULL (disabled with a reason on a NOT NULL column); empty an `int` cell — the
   cell is marked invalid and Apply is disabled until a value or NULL is set; edit a `date` and a
   `money` cell and Apply — both persist (the old 255-scale failure is gone).
10. With pending edits, press F5, choose Open, New query, or navigate to History — a
    confirmation names the pending edits before they are discarded.
11. Keyboard only: Tab into the grid (one tab stop), arrow around, Shift+Alt+Right widens the
    focused column, Shift+F10 opens the menu, Escape closes it; a focused cut cell shows its
    tooltip; Escape dismisses it.

## 2. Results and Messages (User Story 2)

1. Run a single `SELECT` — Results and Messages tabs are both present; Results is active;
   Messages holds `(16 rows affected)` and a final `Completion time:` line in local time.
2. Run two SELECTs, a `PRINT 'step 1 done'` and an `UPDATE` in one batch — two grids stacked
   with their own headers and widths; Messages lists one `rows affected` line per statement in
   order, the PRINT text, and the completion line.
3. Run a query with an error on line 4 — Messages becomes active and shows
   `Msg …, Level …, State …, Line 4` + text; clicking it moves the caret to line 4 (also when
   the error is in a selection starting at line 10, and in a batch after `GO`); the line carries
   the analyser-style marker.
4. Run `SELECT 1; SELECT 1/0; SELECT 2;` — two grids with rows, one empty grid, the error
   between them; nothing is lost.
5. Run three `GO` batches whose second fails — the first and third result sets are present,
   the error sits between them with its document line. Run `SELECT 1 GO 3` — three result sets
   and the loop messages. Run `GO SELECT 2` — the script is refused with SSMS's message.
6. Run `CREATE PROCEDURE #p AS SELECT 1; GO EXEC #p; GO DROP PROCEDURE #p;` — Ok, one result
   set (this failed before the feature).
7. Run `WAITFOR DELAY '00:00:05'` with the toolbar timeout at 2 s — TimedOut; later batches
   are Skipped and say so.
8. Press F5 with focus in the editor, the results pane, the toolbar and the Schema panel — the
   query runs; the page does not reload. On the Settings page F5 reloads (expected).
9. While a long query runs, the results pane and the status bar show "Executing query…" with
   the elapsed time counting from zero; Execute is not offered (button, F5, palette). Stop the
   engine mid-query — the running state ends with "the statement may still have run".
10. Click Parse (Ctrl+F5) on a valid script — "Commands completed successfully."; on a script
    with a syntax error on line 3 of batch 2 — the error with its document line; nothing was
    executed (a `CREATE TABLE` in the parsed script created nothing); Execute afterwards still
    returns rows.
11. Save results as CSV and as tab-delimited — header row, quoting, `NULL`, the document's
    name; a truncated result asks first. Types: a `date` shows `2025-10-01`, `datetime2(7)` seven
    digits, `datetimeoffset` with a space before the offset, `varbinary` as `0x…`,
    `uniqueidentifier` upper case, `money` four decimals.

## 3. Workspace (User Story 3)

1. Hide the Schema panel (View ▾, F8, or its strip) and the AI panel — the editor fills the
   width; edge strips name the hidden panels; clicking a strip restores the panel at its size.
2. Ctrl+R hides the results pane; executing a query brings it back.
3. Drag the splitter between editor and results, and the Schema and AI splitters — live resize,
   minimums respected; with a splitter focused, Arrow keys and Shift+Arrow resize it, Home/End
   go to min/max, Enter collapses and restores.
4. Maximise results (View ▾) — the editor row collapses; one click/key restores the split.
5. Change visibility and sizes, reload — identical layout, including the AI panel's state and
   selected tab; close the browser and reopen — the same.
6. Resize the window to 1100 px — both side panels fold to the strips; a 120-character line
   fits without a horizontal scrollbar; widen — they return (unless hidden by hand).
7. View ▾ › Reset layout — defaults return; reload — still defaults.
8. View ▾ lists every toggle with its key; the Command Palette (Ctrl+P) lists the same
   commands; F6 cycles editor → results → schema → AI → toolbar.
9. Disconnect SQL — the Schema panel stays, showing "Connect to SQL Server…"; click it — the
   connection manager opens. Connected: the header shows `server · database`; type `ord` in the
   filter — only matching objects remain under their schema and kind; clear and click Refresh —
   a loading row, then the full tree.
10. Click Analyse on a script with a warning on line 7 — the Problems tab becomes active with a
    badge count, line 7 is underlined in the editor, and hovering shows the message; with
    analysis off (Settings › Code analysis) the tab says so and offers to turn it on.
11. Hide the navigation bar (View ▾) — a thin strip restores it; it is visible on every other
    page.

## 4. Settings (User Story 4)

1. The navigation has five entries (Editor, Snippets, Format styles, History, Settings); the
   current page is highlighted. `/settings/ai`, `/settings/schema-cache` and `/diagnostics`
   open their sections.
2. Settings shows the section list General, Editor, Format, Queries, Code analysis, AI
   assistance, Connections, Schema cache, Diagnostics; every setting from the old pages is
   present once.
3. Set the default row limit to 500 in Queries — the toolbar quick control shows 500 and the
   next run uses it; change the quick control to 50 — the saved default is still 500; reload —
   the quick control is back to 500.
4. Switch "Enable code analysis" off — Analyse does nothing and the Problems tab says analysis
   is off; "Turn on" restores it.
5. Suppress a rule everywhere from the Problems panel — it appears under Code analysis ›
   Suppressed rules; Undo removes it and the finding returns.
6. With three AI providers, Restore defaults on AI assistance — the confirmation says "This
   removes your 3 providers and their keys"; after confirming, General and Connections are
   unchanged.
7. Export settings — the file holds no key, password or token; import it in another browser —
   theme, editor, query, analysis and AI preferences and the provider list (marked "Needs key")
   apply; that browser's engine and SQL connections are untouched; an edited file with an
   unknown property reports "1 setting skipped".
8. Format › active style: the toolbar picker (labelled "Style") and the section agree; the
   style group reads "Shared with SSMS" only when paired with an SSMS-started engine, else "On
   this engine"; duplicate names show their group in the closed picker.
9. Editor › font size, word wrap, tab size change the editor live and survive reload; Reset
   editor session confirms, naming the document.
10. Type `timeout` in the filter box — only matching settings remain, each under its section;
    non-matching sections are greyed; clearing restores General in full.
11. Set Dark, reload — no light flash before the first paint.
12. Open two browser tabs; change the theme in one; reload the other — it uses the new value
    and never writes the old one back.

## 5. Tabs and documents (User Story 5)

1. In every tab strip (results, AI panel, style preview) Left/Right activate the previous/next
   tab at once, Home/End the first/last; the active marker and badges look the same.
2. New query with a modified document — confirmation; after confirming, `SQLQuery2` (next
   number, no `*`).
3. Open a `.sql` saved by SSMS (UTF-16) and one saved by VS Code (UTF-8) — both read correctly;
   the file name (without extension) becomes the document name; a modified document asks first;
   a file above 10 MB is refused naming the limit.
4. Save a default-named document — asks for a name; `orders-audit.sql` downloads (UTF-8 with
   BOM); the browser title reads `orders-audit - localhost.tempdb - AKML SQL`; type — `*`
   appears in the document bar and the title; Save as — asks again.
5. Run `orders-audit` — History shows the entry as `orders-audit` (earlier runs included); New
   query and run — a new entry; `orders-audit` keeps its name. A default-named document's runs
   appear as `query-NN`.

## 6. Shell polish (User Story 6)

1. Toolbar groups: db selector | Execute, Parse | Format, Analyse | Refactor, AI | New, Open,
   Save, Save as | View; hover shows name + key; icons come from one set (no emoji anywhere).
2. Pick another database in the toolbar selector — IntelliSense, the Schema panel and the
   status bar follow; a `#temp` table created before the switch is still readable; no password
   prompt for a SQL-auth session. Run `USE master;` — the selector follows.
3. Status bar: "Query executed successfully." / "Query completed with errors.", pill, `server ·
   database`, `16 rows · 87 ms`, `Ln 1, Col 12`, `Web 1.26.1007.0613`, `Engine 1.26.1007.0613`
   (hash on hover); pair with an engine of another build — the mismatch marker shows.
4. Every destructive action confirms in-app (remove provider, remove engine connection, delete
   saved connection, clear all cached schemas — also from the palette —, delete style, delete
   snippet, clear diagnostics, reset editor session, discard grid edits); no browser pop-up
   appears anywhere (rename style, new style name, History rename use the in-app prompt).
5. History, Snippets, Schema cache, Diagnostics and Format styles share one page header, button
   and table style; empty panes say what to do next; the start-up screen is styled.
6. Light / Dark / High contrast: selected rows, tabs, badges, splitters, menus, dialogs and
   toasts are readable; focus rings visible; no meaning by colour alone (edited/new/deleted rows
   carry glyphs); with reduced motion on, spinners and fades are static.

## 7. Record the outcome

Update `baseline.md` with the per-suite table, the SC-008 diff, the list of by-design test
changes (research R69) and the three-theme check results; refresh the product-site screenshots
through `SiteScreenshotTour` deliberately after deploying the new UI.
