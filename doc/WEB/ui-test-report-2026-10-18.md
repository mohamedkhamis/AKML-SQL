# Web edition UI test: SQL Server and PostgreSQL (2026-10-18)

**Build tested:** Web and Engine 1.26.1018.0955 at `http://localhost:8083/`, Chromium, 1600×950, fresh browser profile.

**SQL Server target:** `(local)`, Windows authentication, database `Northwind_AutoTest`. `Northwind` is not visible to the engine's service account, so it was not in the list.

**PostgreSQL target:** PostgreSQL 18.3 on `localhost:5432`, database `USERDB_dev`, user `postgres`. Only a scratch table `akml_uitest` was written to. It was created, edited through the grid and dropped again, so the database is as it was.

**Screenshots:** `.playwright-mcp/ui-01…ui-28*.png` (local only).

**Severity:** High = data loss or a core action broken. Medium = wrong or confusing behaviour with a workaround. Low = polish.

---

## 0. Fix status (re-test 2026-10-18, same day)

The fixes are not deployed. `:8083` is the IIS install, and updating it needs an elevated copy. The re-test ran a local Debug build of the web app (`http://localhost:5000`) against the installed engine 1.26.1018.0955, with the same SQL Server and PostgreSQL targets. The engine was not changed. Screenshots: `.playwright-mcp/fix-01…fix-09*.png`.

| # | Status | What changed / how it was checked in the browser |
| --- | --- | --- |
| H1 | Fixed | The editor has an undo history. Inserting a name, then Ctrl+Z and Ctrl+Y: removed, then back. |
| H2 | Fixed | The database list marks the current value as selected, so the dialog shows the database it will use. Picked Northwind_AutoTest; the session opened Northwind_AutoTest. |
| H3 | Fixed | The editor pushes its initial text only when that parameter changes, not on every render. Switching SQL Server → PostgreSQL kept the open query. |
| H4 | Fixed | The last result is kept for the browser tab. Editor → History → Editor: the 196 rows were still there. |
| M1 / E3 | Fixed | A click selects an object (and shows its columns); a double-click or **Insert name** inserts it, with a space where it would touch other text. One Ctrl+Z removes it. |
| M2 / E2 | Fixed (part) | Right-click menu: Select Top 1000 Rows (PostgreSQL: Select first 1000 rows) opens a new query and runs it. Also Script as SELECT / INSERT / UPDATE / DELETE, Script as EXECUTE / CALL for routines, Insert name, Copy name, Refresh. The UPDATE and DELETE scripts match no rows until their WHERE is edited. Not done: View definition, Drop…, Execute with a parameter form. |
| M3 | Fixed | The explorer reloads once Phase B (the columns) has arrived. SQL Server tables could be expanded right after connecting. |
| M4 | Fixed | A click anywhere on a folder row opens it. |
| M5 | Fixed | A connection change clears the status-bar outcome. Existing Problems are re-checked for the new connection's kind (10 T-SQL findings became 1 PostgreSQL syntax error). Typing does not re-run the analysis. |
| M6 | Fixed | No completion list on `;`; Execute closes an open list. |
| M7 | Fixed | The explorer shows "Refreshing schema…" while it reloads. After `DROP TABLE` the tree updated in about 1 s, not 8–10 s. |
| M8 | Fixed | The Test / Connect result sits above the buttons, outside the scrolling form. |
| M9 | Fixed (timer) | The running clock ticks even when the run starts from F5 (00:00:00 → 00:00:02 during `pg_sleep(3)`). The 10 s first run was not retested on a cold engine: it had been running since the morning, and the first query after connecting took 3 ms. It is probably engine warm-up. |
| M10 | Fixed | The 7–59 day group is now "Last Two Months", on desktop too. Auto-named entries (`query-NN`, which restart every day, as SQL Prompt numbers them) show the start of their SQL under the name. "Not open in an editor" is a neutral ring, not a red dot. |
| M11 | Fixed | Switching the kind starts a new connection without the selected connection's name. The PostgreSQL Database field is a list with a refresh button: 12 databases listed, and you can still type one. The title follows the kind. |
| L1 | Fixed | While a query runs, Cancel takes Parse's place, and the database button has a fixed width. The toolbar height stayed 71 px during a run. |
| L2 | Fixed | "Not connected to a database." / "Connect…", and Settings says "Database connections". |
| L3 | Not a bug | A fresh editor opens empty and unmodified. The sample text came from a session saved in the test browser's profile by an earlier run. |
| L4 | Fixed | When the page is served from this computer, the status bar offers **Use this computer's engine**. One click paired with the local engine, with no PIN. It is a button rather than automatic pairing, so it cannot pick the wrong engine (the test harness, for one, pairs its own sandbox engine). |
| L5 | Fixed | Empty folders have no chevron. Long names end in "…" instead of a scrollbar. Open folders are remembered per server and database across reconnects. Objects are no longer styled as links. |
| L6 | Fixed (part) | PostgreSQL booleans read `true` / `false`, and an edited value widens its column. Not changed: the default results height (38 %). |
| L7 | Fixed | With Windows authentication, the hint under the list says why a database can be missing and what to do. |
| L8 | Fixed | The engine the page is using shows **Connected** instead of a Connect button. |
| E1 | Done | The Schema header has a pin and a close button. Unpinned, the panel folds to its edge strip and opens over the editor from the strip or F8. It hides again when you click or type in the editor, or press Escape. The pin state is remembered per browser. |

**Tests:** web unit tests 1666/1666 pass, plus new tests for the explorer menu, scripts, name padding, the kept result and booleans. JS tests 137/137 pass. The E2E project builds; the browser suites were not run. Two failures are not from these changes: `SnippetSaveRaceTests` fails intermittently under load and passes on its own, and the Core test `ProfileGetMessageTests.Response_key_layout_is_positional_and_append_only` fails (it expects 7 keys and finds 9) in a message this work does not touch.

**Still open:** E4–E12; from E2, View definition, Drop… and Execute with parameters; and an auto-sized results pane (E7).

---

## 1. Issues found

### High

| # | Area | Issue | Seen on | Cause / fix |
|---|---|---|---|---|
| H1 | Editor | **Undo and redo do nothing.** Ctrl+Z / Ctrl+Y fail for typed text and for names inserted from the explorer. Your "click a table, Ctrl+Z doesn't remove it" report is one symptom of this. | Both | `wwwroot/js/akml-editor.js` (~line 800) adds `cm.commands.historyKeymap` but never adds the `cm.commands.history()` extension, so the keys have no history to work on. One-line fix: add `cm.commands.history()` to the `extensions` list. |
| H2 | Connect dialog | **It connects to a different database than the one shown.** The Database dropdown showed `Northwind_AutoTest`, but the connection, and the saved connection, used `master`. Every later reconnect with the saved entry opens `master` too. | SQL Server | `ConnectionManagerModal.razor` `<select @bind="_database">`: the list of options loads after the value is bound. The browser then displays the first option while `_database` keeps `"master"`. Mark the matching `<option selected>`, or set `_database` explicitly once the list loads. |
| H3 | Editor / connection | **Switching connection replaced the editor text.** The first switch from SQL Server to PostgreSQL replaced the open query with the original sample (`SELECT * FROM sys.objects …`), with no prompt and no undo (see H1). A later switch kept the text. | SQL Server → PostgreSQL | Probably the editor being re-created from its initial text when the dialect changes. The text must never be replaced without confirmation. |
| H4 | Navigation | **Leaving the editor loses the results.** Going Editor → History → Editor cleared the results grid ("Run a query to see results here"). | Both | Keep the last results (or the whole editor page state) across top-navigation changes. |

### Medium

| # | Area | Issue | Seen on |
|---|---|---|---|
| M1 | Explorer | **A single click on a table inserts its name at the caret, with no separator.** Example: `[dbo].[Customers]SELECT *`. It is easy to trigger by accident (just selecting a node), and with H1 it can't be undone. | Both |
| M2 | Explorer | **Right-click does nothing.** There is no context menu on tables, views or procedures. See enhancement E2. | Both |
| M3 | Explorer | **Tables can't be expanded to their columns until a schema refresh.** PostgreSQL tables got their expand arrows only after the DDL refresh; SQL Server tables had none. | Both |
| M4 | Explorer | **Clicking a folder label ("Tables (9)") does not expand it.** Only the small chevron does. | Both |
| M5 | State after a switch | **Stale results stay on screen after switching connection.** The Problems tab kept the previous document's 3 warnings (`o`/`c` aliases), and the status bar kept "196 rows · 28 ms" from the SQL Server run until the next execute. | Both |
| M6 | Completion | **The completion list opens after typing `;`** (it lists ALTER, ANALYZE, …). The list also stays open while the query runs: F5 does not close it. | Both |
| M7 | Explorer | **The automatic refresh after DDL is slow and silent.** After `CREATE TABLE`, it took about 8–10 s before "Tables (14)" became "(15)", with no loading indicator. | PostgreSQL |
| M8 | Connect dialog | **The Test result is out of view.** "PostgreSQL 18.3 on localhost:5432" is rendered below the scrollable form, so you can't see whether Test passed without scrolling. | PostgreSQL |
| M9 | Execute | **The first execute after connecting took about 10 s, and the timer stayed at "00:00:00" throughout.** The server itself took 29 ms. Later runs were fast (about 30–50 ms). | SQL Server |
| M10 | History | **History entries are hard to tell apart, and grouped under wrong dates.** Every entry is named "query-01". The dates 2026-10-08 (10 days ago) and 2026-09-20 appear under "TWO MONTHS AGO". Every row has a red dot with no explanation. | Both |
| M11 | Connect dialog | **The dialog form is inconsistent.** Switching the kind on a selected saved connection keeps that connection's name (the PostgreSQL form showed "Local Northwind"). The PostgreSQL Database field is free text, while SQL Server has a list and a refresh button. The dialog opens titled "Connect to SQL Server". | Both |

### Low / polish

| # | Issue |
|---|---|
| L1 | The toolbar reflows. Buttons shift when the database name changes length, and while a query runs, "Cancel if not started" pushes "Save as" and "View" onto a second row. |
| L2 | Wording still says SQL Server only: "SQL Server connections", "Saved SQL Server connections. Passwords are never saved." and "Not connected to a SQL Server." |
| L3 | A new document opens already marked modified (`SQLQuery1 *`) and pre-filled with T-SQL sample text, even when you then connect to PostgreSQL. |
| L4 | First visit on the same machine needs Settings → Connections → Add → Pair. A local engine on the default port could be paired automatically. |
| L5 | Explorer polish:<br>• Empty folders such as "Views (0)" and "Procedures (0)" still show chevrons.<br>• Long names overflow with a horizontal scrollbar.<br>• The tree collapses on every reconnect.<br>• Objects are styled as blue links. |
| L6 | Results grid:<br>• PostgreSQL booleans show `t`/`f`.<br>• An edited value is cut short ("beta ed…") because the column doesn't widen.<br>• The editor keeps most of the height after a run, leaving room for about 9 result rows. |
| L7 | With Windows authentication, the database list shows only the databases the engine service account can open (`Northwind` was missing), and nothing says so. Allow typing a name, or explain why a database is missing. |
| L8 | The paired engine row in Settings shows a "Connect" button while the engine is already connected, so it's unclear which engine is active. |

---

## 2. What worked well

- **Engine and connections:**
  - Pairing with the local engine took one click (no PIN).
  - Connecting to SQL Server and PostgreSQL both work.
  - Saved connections work, and the PostgreSQL password can be remembered on the engine machine.
- **Completion:** schema-aware on both. On PostgreSQL, `us` + Enter gave `"USER"` with the correct quoting.
- **Execution:** fast after the first run (28 ms for 196 rows, 22 ms for a 3-statement script). Messages, errors and the per-statement command tags are correct.
- **Grid editing on PostgreSQL:** edit, the pending-change bar, Apply, and the "Applied 1 change(s)" confirmation.
- **Stale results:** after a switch, the grid says "These results came from an earlier connection" and disables editing.
- **Other:** Format, Analyse (the Problems tab with Info/Warn/Error filters), DDL auto-refresh (it works, just slowly), History with its kind badges and filters, and the AI panel empty state with a link to Settings.

---

## 3. Enhancements to make development faster and smoother

In priority order:

- **E1. Left panel that hides like the AI panel.**
  - Add a collapse button in the Schema header and an edge strip when collapsed, mirroring the right-hand AI panel.
  - Add a **pin / auto-hide** mode: when unpinned, the panel slides over the editor on click or hover of the strip and hides when focus returns to the editor (Visual Studio style).
  - Remember the state per browser. F8 already toggles it, so this is mostly UI.
- **E2. Explorer context menu** on tables and views:
  - **New query** (`SELECT TOP 1000 …` / `SELECT … LIMIT 1000`, opened in a new tab and run).
  - **Edit rows** (the same query with editing enabled).
  - **Script as** SELECT / INSERT / UPDATE / DELETE / CREATE.
  - **Copy name** and **Insert name**.
  - **Refresh**.
  - **View definition** (DDL).
  - **Drop…** (with confirmation).
  - Procedures and functions: **Execute…** with parameters and **Script as ALTER**.
- **E3. Safer insert from the explorer.**
  - Single click selects. Double-click, drag, or the context menu inserts.
  - Insert adds a separating space when needed.
  - Dragging a "Columns" node inserts the comma-separated column list.
- **E4. Several query tabs.** Ctrl+N opens a new tab on the current connection. Each tab keeps its own connection, text and results, and all tabs are restored after a reload. This removes H3 and H4 by design.
- **E5. Connection switcher in the toolbar.** A dropdown of recent and saved connections with their kind badges (instead of the dialog), plus an environment colour bar (for example red for production).
- **E6. Run current statement.**
  - Highlight the statement under the caret.
  - Ctrl+Enter runs just that statement, and F5 runs the selection or the script.
  - Show one results tab per statement.
- **E7. Results productivity.**
  - Quick filter and find in the grid.
  - Copy as INSERT / JSON / CSV from a right-click menu.
  - Auto-fit the results height after a run.
  - Double-click a header to sort.
  - A "show column stats" row (count, distinct, nulls).
- **E8. Columns in the explorer.** Show columns as soon as the catalog has them (fixes M3), with PK and FK icons, and a filter that searches column names too.
- **E9. Visible shortcuts.** A shortcuts cheat sheet (and command palette entries) listing F5, Ctrl+Enter, F8, Ctrl+K Ctrl+L, Ctrl+Alt+O and the rest.
- **E10. Query plan.** `EXPLAIN (ANALYZE, BUFFERS)` for PostgreSQL and the estimated plan for SQL Server, from the toolbar.
- **E11. History speed-ups.**
  - Name entries from the first table or statement instead of "query-01".
  - A quick-open list (Ctrl+E) of recent queries for the current connection.
- **E12. Autosave with recovery.** Never replace or clear the editor text silently; when something must, keep the old text one Ctrl+Z away (needs H1).

---

## 4. Suggested fix order

1. **H1** (one line, unblocks undo everywhere).
2. **H2** (wrong database).
3. **H3** and **H4** (lost text and results). E4 tabs would settle both.
4. **M1–M6**, together with **E1–E3**, which you asked for.
5. The rest as polish.
