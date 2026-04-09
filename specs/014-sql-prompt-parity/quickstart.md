# Quickstart: SQL Prompt Parity Manual Verification

**Branch**: `014-sql-prompt-parity` | **Date**: 2026-04-09

This document is a developer-facing walk-through that validates every shipped user story end-to-end after a milestone build. Run it before requesting merge to `master`. Each section corresponds to one user story; mark `[ ]` → `[x]` as you go.

## Prerequisites

- Two SQL Server instances reachable from the dev machine — one local, one remote (required for US11, US14, US15, US18). The local can be `(local)` / `localhost`; the remote should be a tagged "Production"-style server.
- A test database with at least 20 user objects, including a known-invalid view, a column referenced by 3+ procedures, an encrypted procedure (for US19 / FR-098), and a temp-table-using script (for US19 / FR-100).
- Inno Setup 7 installed (for the installer build) — `c:\Program Files\Inno Setup 7\ISCC.exe`.
- AKML SQL extension is installed in **all 6 hosts** (SSMS 20, SSMS 21, SSMS 22, VS 2019, VS 2022, VS 2026). Use `bash hotswap-ssms22.sh` for SSMS 22 dev iteration; for the other hosts, run the installer once.
- An AI provider configured in `%AppData%\AKML SQL\config.json` → `Ai.Enabled = true` (for US10, US18).

## Build

```bash
# Engine first (changes here block everything)
dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64

# All shell extensions individually (NEVER use solution build per CLAUDE.md)
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"
for proj in Ssms20 Ssms21 Ssms22 VS2019 VS2022 VS2026; do
  "$MSBUILD" "src/AkmlSql.$proj/AkmlSql.$proj.csproj" -t:Restore -p:Configuration=Release -v:quiet
  "$MSBUILD" "src/AkmlSql.$proj/AkmlSql.$proj.csproj" -t:Build    -p:Configuration=Release -v:minimal
done

# Updater
dotnet publish src/AkmlSql.Updater/AkmlSql.Updater.csproj -c Release

# Tests
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj

# Installer (only for full release)
"/c/Program Files/Inno Setup 7/ISCC.exe" src/AkmlSql.Installer/AkmlSqlSetup.iss
```

Expected: 0 errors. Engine tests pass `≥ 867`, Core tests pass `≥ 459`. Use `hotswap-ssms22.sh` to deploy to SSMS 22 between iterations without rerunning the installer.

---

## US1 — Pre-execution safety warnings

- [ ] Open SSMS 22 connected to a test database.
- [ ] Type `DELETE FROM TestTable;` (no `WHERE`) and press `F5`.
- [ ] **Verify**: a warning dialog appears naming the statement, the server, the database, and the environment color (if Production-tagged via US5). Default focus is **Cancel**.
- [ ] Press `Cancel` — verify no rows are affected.
- [ ] Press `F5` again, click **Execute anyway** — verify the DELETE runs.
- [ ] Repeat for `UPDATE X SET Col = 1;` and `DELETE X FROM X INNER JOIN Y ON X.Id = Y.Id;`.
- [ ] Wrap a `DELETE` in `CREATE PROCEDURE dbo.TestProc AS BEGIN DELETE FROM TestTable; END` and press `F5` — verify the dialog appears with the embedded statement cited.
- [ ] Tick "Don't ask again for this session" and re-run — verify no dialog.
- [ ] Open Settings → Execution Warnings, disable the master switch, re-run — verify no dialog.
- [ ] Re-enable, change DefaultButton to `Execute` in config.json, re-run — verify focus is on Execute.

## US2 — Column Picker

- [ ] In SSMS 22, type `SELECT  FROM dbo.LargeTable` (cursor before `FROM`).
- [ ] Press `Ctrl+Left` — verify a Column Picker tab appears in the popup with all columns of `LargeTable` in defined order, with PK ⚷ and FK 🔗 badges.
- [ ] Toggle the sort to alphabetical — verify the order changes.
- [ ] Press `Space` on 3 columns — verify checkmarks appear.
- [ ] Press `Ctrl+A` — verify all columns are selected.
- [ ] Press `Enter` — verify the selected columns are inserted comma-separated.
- [ ] Open a query with two tables `JOIN`-ed and repeat — verify each inserted column is alias-qualified.
- [ ] Press `Esc` while picker is open — verify nothing is inserted.

## US3 — Wildcard expansion

- [ ] Type `SELECT * FROM dbo.LargeTable c` and place cursor right after `*`.
- [ ] Press `Tab` — verify `*` expands to the explicit column list.
- [ ] Type `SELECT c.* FROM dbo.LargeTable c` and repeat — verify `c.*` expands to alias-qualified column names.
- [ ] Press `Tab` somewhere not after `*` — verify normal Tab behaviour.

## US4 — Command Palette

- [ ] In SSMS 22, press `Alt+S` — verify a modal popup appears.
- [ ] Type `format` — verify results include AKML SQL **Format Document**, the Format options page, and SSMS's `Edit → Advanced → Format Document`.
- [ ] Type a partial table name `Cust` — verify database objects appear (SSMS only).
- [ ] Press `Down` then `Enter` on a result — verify the matching action runs.
- [ ] Press `Esc` — verify the palette closes.
- [ ] In VS 2022, press `Alt+P` and verify the same flow without database objects.

## US5 — Tab coloring

- [ ] Right-click a query tab → **Tab Color (Server)** → pick `Production` — verify the tab header turns red with gradient.
- [ ] Open a second query against a different server tagged `Development` — verify the second tab is green.
- [ ] Open Options → Tabs → Color, edit the `Production` color to a different red — verify all Production tabs update without restart.
- [ ] Add a Server Group color, then connect to a server in that group — verify the new tab inherits the group color.
- [ ] Run a `DELETE` without `WHERE` from a Production tab — verify the safety dialog header is rendered in the same red.

## US6 — Code Analysis Issues window

- [ ] Open a script with ≥ 10 known analysis issues (mix of BP/PE/ST).
- [ ] Open AKML SQL → Show Code Analysis Issues — verify a dockable window lists every issue with rule id, severity, description, line, column.
- [ ] Click an issue — verify the editor jumps to and highlights that line.
- [ ] Click a column header — verify sorting.
- [ ] Edit the script — verify the list refreshes within 1 s of pause.
- [ ] Click Export — verify a CSV file is saved.
- [ ] Dock the window on the right, restart SSMS — verify it re-opens in the same position.
- [ ] Toggle grouping by rule off — verify a flat list with the total count header.

## US7 — `Ctrl+B` chord family

- [ ] Select a piece of code with mixed casing. Press `Ctrl+B, Ctrl+U` — verify keyword casing is normalized.
- [ ] Type a query with unqualified objects. Press `Ctrl+B, Ctrl+Q` — verify all object refs become schema-qualified.
- [ ] `SELECT *` → `Ctrl+B, Ctrl+W` — verify expansion.
- [ ] Query with missing semicolons → `Ctrl+B, Ctrl+C` — verify semicolons added.
- [ ] Selection of identifiers → `Ctrl+B, Ctrl+B` — verify brackets toggled.
- [ ] Selection containing `EXEC procName` → `Ctrl+B, Ctrl+I` — verify the procedure body is inlined.
- [ ] Selection of a SQL block → `Ctrl+B, Ctrl+E` — verify the dialog prompts for a new procedure name and the selection is replaced with `EXEC newProc @params`.

## US8 — Object Definition Box

- [ ] Type `SELECT * FROM Cust` and select `Customers` in the popup.
- [ ] Verify the side panel shows the Summary tab with column list and row count.
- [ ] Click Script — verify the `CREATE TABLE` statement appears with syntax coloring.
- [ ] Arrow-down to a stored procedure suggestion — verify Summary now shows parameters and Script shows the procedure body.
- [ ] Hold `Ctrl` while popup is visible — verify both popups become semi-transparent.
- [ ] Drag the bottom-right corner to resize — verify the new size persists across SSMS restart.
- [ ] Open the definition of an encrypted procedure — verify the Script tab shows the decrypted body with a "decrypted" badge if DAC is available.

## US9 — Inline `-- akml-format off / on` markers

- [ ] Write a script with a hand-aligned UNION block in the middle.
- [ ] Select the block, hold `Ctrl`, pick **Disable formatting for selected text** — verify the selection is wrapped with `-- akml-format off` and `-- akml-format on`.
- [ ] Press `Ctrl+K, Ctrl+Y` — verify the wrapped block is preserved verbatim while the rest of the document is formatted.
- [ ] Test nested off/off — verify no crash.
- [ ] Test unmatched off — verify everything from the off marker to EOF is left unformatted.

## US10 — AI keyboard shortcuts

- [ ] Ensure `Ai.Enabled = true`.
- [ ] Press `Alt+Z` — verify the AI panel opens.
- [ ] Select SQL, press `Shift+Alt+R` — verify Fix flow runs.
- [ ] Select SQL, press `Ctrl+Alt+Z` — verify Optimize flow runs.
- [ ] In an empty area, press `Ctrl+Alt+Up` — verify a ghost-text suggestion appears; press `Tab` to accept.
- [ ] Set `Ai.Enabled = false`, press the shortcuts — verify a status-bar message says AI is disabled.

## US11 — Dual-instance awareness

- [ ] Open a query window on Server A and a query window on a different Server B.
- [ ] In query A, type `USE ` — verify only Server A's databases appear.
- [ ] Switch to query B, type `USE ` — verify only Server B's databases appear.
- [ ] Repeat 50 times rapidly switching between the two — verify zero cross-server leaks.
- [ ] Inspect `%AppData%\AKML SQL\logs\akmlsql-*.log` for `SsmsConnectionDetector: matched text view to document` lines and ensure no `ActiveDocument` fallback warnings.

## US12 — Settings surface

- [ ] Open Options. Verify every spec 014 feature has a corresponding toggle or page.
- [ ] Use the search box at the top — verify each new feature is found by display label or description.
- [ ] Toggle a feature off — verify it stops working within 1 s without restart.
- [ ] Toggle on — verify it resumes.

## US13 — Script navigation chords

- [ ] Open a 500-line script with multiple stored-procedure definitions.
- [ ] Press `Ctrl+B, Ctrl+S` — verify the Summarize Script outline appears with each statement type and line range.
- [ ] Click an entry — verify the editor jumps to and highlights that line.
- [ ] Place the caret on a `dbo.MyProc` reference, press `F12` — verify a new query window opens with `ALTER PROCEDURE dbo.MyProc...`.
- [ ] Press `Ctrl+F12` on the same identifier — verify Object Explorer expands to and selects that node.
- [ ] In a script with `DECLARE @unused INT;` never read, press `Ctrl+B, Ctrl+F` — verify the Unused Variables panel lists `@unused`.

## US14 — Find Invalid Objects

- [ ] Right-click a database in Object Explorer → **Find Invalid Objects**.
- [ ] Verify a dockable window appears with object name, schema, type, error message, line number for each invalid object.
- [ ] Double-click an entry — verify Object Explorer jumps to that node.
- [ ] Select a row, click **Script as ALTER** — verify a new query window opens with the ALTER script.
- [ ] Multi-select rows, click **Script as ALTER** — verify scripts are concatenated.
- [ ] Run on a clean database — verify "No invalid objects found" message.

## US15 — Smart Rename

- [ ] Pick a column in a test database referenced by 3 views, 2 procedures, 1 trigger.
- [ ] Place caret on the column name, press `F2` — verify the Smart Rename dialog appears.
- [ ] Type a new name, click Preview — verify Actions / Warnings / Dependencies tabs show the dependent objects.
- [ ] Click Apply — verify the column is renamed and all 6 dependent objects still parse.
- [ ] Pick another column with a name collision in target schema — verify Apply is disabled and a warning is shown.

## US16 — Result-grid productivity

- [ ] Run `SELECT TOP 10 Id FROM Customers`. Right-click result grid → **Copy as IN Clause** — verify clipboard contains `(1, 2, 3, ..., 10)`.
- [ ] Right-click → **Script as INSERT** — verify clipboard contains a fully-formed `INSERT INTO Customers (Id) VALUES (1), (2), ...`.
- [ ] Run a query with a numeric column > 15 digits. Right-click → **Open in Excel** — verify Excel shows the full precision.
- [ ] Test with a NULL-containing column → Copy as IN Clause omits NULLs and reports the omission count.

## US17 — Lightbulb quick-fixes

- [ ] Type a query that triggers `BP002` (`!=` operator). Verify an orange lightbulb in the gutter.
- [ ] Hold `Ctrl` and hover the squiggle — verify the Issue Details popup with rule id, problem, remediation, **Apply Fix** button.
- [ ] Click Apply Fix — verify `!=` is replaced by `<>` and the squiggle clears.
- [ ] Trigger an advisory rule — verify a blue lightbulb with no Apply Fix.
- [ ] Click **Disable this rule** — verify the rule is added to inline `-- akml-disable` or `.casettings`.

## US18 — AI Explain, Index Analysis, fix-on-error, comment-to-SQL

- [ ] Select a 30-line stored procedure body, right-click → **Explain SQL** — verify the AI panel shows a plain-language explanation.
- [ ] Open a slow `SELECT ... WHERE col = @p`, run **Query Index Analysis** — verify the panel shows existing vs hinted plans, impact %, and a `CREATE INDEX` script.
- [ ] Run a query with a typo — verify the "Fix with AI" toast appears.
- [ ] Click the toast — verify the AI panel opens pre-filled with the failing batch and the SQL Server error.
- [ ] Type `-- generate: list the top 10 customers by revenue` and press Tab — verify the AI generates the matching SQL beneath the comment.
- [ ] Open the History tab in the AI panel — verify previous prompts/answers and "revert to this state" actions.
- [ ] Select a SQL block — verify an AI icon appears at the right edge with Explain / Fix / Optimize hover actions.

## US19 — Completion polish

- [ ] Press `Ctrl+Shift+P` — verify the popup is suppressed.
- [ ] Press `Ctrl+Shift+P` again — verify the popup resumes.
- [ ] Press `Ctrl+Shift+D` — verify a status-bar message indicates "Refreshing schema cache".
- [ ] Open Options → Completion → Commit keys, enable Space — verify typing `Ord ` commits `Orders`.
- [ ] In the popup, press `Ctrl+Down` — verify the category badge changes from "All" to "Tables".
- [ ] Hover an object with `MS_Description` — verify the description appears in the tooltip with clickable identifiers.
- [ ] Type `#tmp1 (a INT, b VARCHAR(50))` then `INSERT INTO #tmp1 (` — verify `a` and `b` are suggested.
- [ ] Open the definition of an encrypted procedure with DAC connection — verify the decrypted body shows.

## US20 — Execution shortcuts and Browse Open Tabs

- [ ] Open a script with three batches separated by `GO`. Place cursor in the second batch.
- [ ] Press `Alt+Shift+F5` — verify only the second batch runs.
- [ ] Press `Ctrl+Shift+F5` — verify only lines from the start of the batch up to the cursor run.
- [ ] Repeat with an unsafe `DELETE` in the about-to-run portion — verify the US1 safety dialog appears.
- [ ] Open multiple query tabs across SSMS, press `Ctrl+Q` — verify a popup lists every open tab with fuzzy search.
- [ ] Type a partial filename, press `Enter` — verify the matching tab is activated.

---

## Regression baseline

After every milestone, also re-run:

```bash
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj
```

Expected: `≥ 867 / ≥ 867` Engine tests pass and `≥ 459 / ≥ 459` Core tests pass (per SC-009). Any reduction in count is a regression — investigate before merging.

## Logs

If anything fails, the first place to look is `%AppData%\AKML SQL\logs\akmlsql-<date>.log`. The engine logs every connection change, every IPC dispatch, every cache invalidation, and every analysis run with structured Serilog output.
