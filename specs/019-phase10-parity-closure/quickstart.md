# Quickstart: Phase 10 Verification

This document is the reviewer-targeted manual smoke-test guide for Phase 10. It is organised by milestone (M0–M5 per Phase 10 PRD § 7) and within each milestone by user story. Each section answers two questions: **what to set up** and **what to verify**.

Run these tests in SSMS 22 as the primary host (the most representative of the user base). For US7 (Browse Open Tabs) and US12 (live theme switching), also confirm in VS 2022. Hot-swap deployment: `bash hotswap-ssms22.sh` (existing script).

---

## Milestone M0 — Merge + documentation hygiene

### M0 verification (US1)

**Setup**: Merge branch `018-options-dialog-phase2` to `master` via reviewed PR. Apply the doc updates (FR-002..FR-005) in the same PR.

**Verify**:

1. `git log --oneline master` shows the Options Dialog Phase 1 + Phase 2 commits (`5efe39a` … `3ec5755`) interleaved cleanly without merge conflicts.
2. `git branch --show-current` after checking out the merged result equals what `CLAUDE.md`'s `Active branch:` line says.
3. Search `doc/progress.md` for `"100% SQL Prompt v11 parity"` — no hits.
4. Open `doc/bugs.md` and read the closure note at the end.
5. Open `doc/AKML_SQL_Gap_Analysis_1.md` — the first heading is now a banner `> **Superseded by Phase 10 PRD §3**`.
6. Open `specs/014-sql-prompt-parity/tasks.md` — US1 and US5 are marked `[X]`; the remaining stories point at `specs/019-phase10-parity-closure/`.
7. Run the Phase 10 PRD §3 reconciliation: every row labelled "❌ Absent" or "⚠️ Partial" should be greppable to confirm the absence/partiality in the merged master.

---

## Milestone M1 — Daily-use parity, batch 1

### Column Picker + Wildcard-Tab (US2)

**Setup**: Connect SSMS 22 to a database with a table that has ≥ 20 columns including a PK (e.g., `Id`) and an FK (e.g., `CustomerId`). Use `AdventureWorks` or a synthetic test DB.

**Verify Column Picker**:

1. Open a new query. Type `SELECT  FROM dbo.MyTable` with the cursor placed before `FROM`.
2. Press `Ctrl+Left Arrow`. The Column Picker opens listing every column in table-defined order.
3. The PK column has a key icon ⚷; FK columns have a chain icon 🔗.
4. Press `Space` on 3 columns scattered in the list. Each row shows a checkmark and the selected-count footer reads "3 columns selected".
5. Press `Enter`. The three column names are inserted comma-separated at the caret, in the order they were selected.
6. Repeat with a query that has two tables in scope: `SELECT  FROM dbo.A a JOIN dbo.B b ON a.Id = b.AId`. Press `Ctrl+Left` from cursor before `FROM`, multi-select columns from `A`. Verify each inserted column is qualified with `a.` (alias).
7. Press `Esc` while the picker is open — picker closes, nothing inserted.
8. Open the picker, press `Ctrl+A` — all columns selected. Press `Enter` — every column inserted.

**Verify Wildcard `*` + Tab**:

1. Type `SELECT * FROM dbo.Customers c`. Place the cursor immediately after the `*`.
2. Press `Tab`. The `*` is replaced with the explicit comma-separated column list per the active format style.
3. Type `SELECT c.* FROM dbo.Customers c`. Place cursor immediately after `*`. Press `Tab`. The `c.*` becomes alias-qualified column names.
4. Type a column whose name is a reserved keyword (`User`). Re-run wildcard expansion. The inserted column name is bracketed: `[User]`.
5. Type `SELECT * FROM dbo.Customers c JOIN dbo.Orders o ON c.Id = o.CustomerId`. Place cursor after `*`. Press `Tab`. All columns from both tables expand alias-qualified.
6. Type any query and place the cursor in the middle of an identifier (not after `*`). Press `Tab`. Normal Tab behavior (indent or completion commit) applies — no wildcard expansion.

### Code Analysis Issues window + lightbulb quick-fixes (US3)

**Setup**: Open a script with ≥ 10 known analysis issues across categories. Sample:

```sql
SELECT * FROM Customers;                          -- BP005 (SELECT *)
DELETE FROM Orders;                                -- BP018 (DELETE without WHERE)
SELECT * FROM dbo.X WHERE a != 1;                  -- BP002 deprecated !=
SELECT Top 10 * FROM dbo.X;                        -- BP006 TOP without ORDER BY
DECLARE @a INT;                                    -- MI005 unused variable
EXEC sp_executesql N'SELECT 1';                    -- PE001 unqualified
```

**Verify Issues window**:

1. Open AKML SQL → "Show Code Analysis Issues". A dockable tool window appears listing every issue with rule ID, severity, description, line, column.
2. Click a row — the editor scrolls to and highlights the offending text within 1 second.
3. Click the "Rule" column header — list sorts by rule. Click again — descending.
4. Toggle "Group by Rule" — issues reorganise; the total-count header is preserved.
5. Click "Export to CSV". A `Save As` dialog appears. Save and open the CSV — verify it has the same columns and row count, RFC 4180 quoted.
6. Type a new violation in the editor and pause. Within 1 second the new issue appears in the window.
7. Close SSMS and re-open it. The Issues window reopens in its previously docked position and size.

**Verify lightbulb quick-fixes**:

1. With the script above, find the `!= 1` violation. An orange lightbulb 💡 appears in the gutter on that line.
2. Find the `SELECT *` violation (BP005 has no auto-fix in current implementation — verify). The lightbulb is blue, not orange.
3. Hold `Ctrl` and hover over the `!=` squiggle. A popup shows: "BP002", "Warning", "Use `<>` instead of `!=` for non-equality comparison.", "Apply Fix".
4. Click "Apply Fix". The `!=` becomes `<>`. The squiggle disappears within 1 second.
5. Hold `Ctrl` over a BP005 squiggle. The popup shows the rule but NO Apply Fix button.
6. Click "Disable this rule" in any popup. Choose "This file" — verify a `-- akml-disable BP002` comment is inserted at the top of the file.
7. Set the schema cache to "loading" state (disconnect mid-load). Trigger an auto-fix that requires schema metadata. The popup queues the fix, status bar reads "waiting for schema". After reconnecting and Phase B completes, the fix is auto-applied.

### Right-click tab color + WCAG clamp (US4)

**Setup**: Have at least two query tabs open to different servers (one tagged Production, one Development via existing environment rules).

**Verify**:

1. Right-click a query tab. Three submenus appear: "Tab Color (Server)", "Tab Color (Database)", and (if the server is in a Registered Server Group) "Tab Color (Server Group)".
2. Pick "Tab Color (Server) → Production". The tab paints in the Production red. Any other tabs on the same server repaint within 1 second.
3. Pick "Tab Color (Database) → Custom-UAT" (after defining a Custom-UAT environment in Options). The tab repaints with the Custom-UAT color, overriding the server-level assignment per FR-045.
4. Switch Windows to a High Contrast theme. Open a Production-colored tab. The red is darkened/clamped; foreground text reads against the clamped color (WCAG AA 4.5:1).
5. Open Options → Tabs → Color. Add a new environment "Pre-Prod" with color `#FF9F43`. Click OK. Right-click a tab. The submenu now includes "Pre-Prod" without restart.

### Installer icon and banner (US5)

**Setup**: Build the installer: `iscc src/AkmlSql.Installer/AkmlSqlSetup.iss`. Take the resulting `AKMLSQLSetup.exe` to a clean Windows 11 VM (or check `Properties → Details` on the dev machine).

**Verify**:

1. Windows Explorer shows the AKML SQL custom icon on the EXE (not the default Inno Setup icon).
2. Run the installer. On every wizard page, the AKML SQL header banner is rendered.
3. Run `AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA`. The install completes silently. The product icon appears in `Add or Remove Programs`.

---

## Milestone M2 — Daily-use parity, batch 2

### Command Palette (US6)

**Setup**: AKML SQL is loaded in SSMS 22 with an active connection to a database with at least a few tables.

**Verify**:

1. Press `Alt+S`. The palette opens with the search box focused. The result list is empty (or shows the 10 most-recent picks if any).
2. Type `format`. Results update live across four categories: AKML SQL commands (e.g., "Format Document"), AKML SQL options (e.g., "Format › Styles"), host commands (e.g., "Edit → Advanced → Format Document"), and (since this is SSMS) no database objects.
3. Each row has a small category badge ("Command", "Option", "Host", "DB Object").
4. Type a partial table name from the active database. Database object entries appear with the "DB Object" badge.
5. Pick an Options entry — Settings opens scrolled to and highlights the matching control.
6. Pick a database object — Object Explorer navigates to that node, or the object's definition opens in a new query window.
7. Press `Esc`. Palette closes. Focus returns to the editor.
8. Re-open the palette — the recently-picked entries appear first when the search box is empty.

### Script navigation chords + Browse Open Tabs + F1 help (US7)

**Setup**: Open a 500-line SQL script with multiple `CREATE PROCEDURE` blocks and a `DECLARE @unused INT;` that is never read.

**Verify**:

1. Press `Ctrl+B, Ctrl+S`. A Summarize Script dialog appears showing each top-level statement (CREATE / ALTER / SELECT / INSERT / UPDATE / DELETE / EXEC / USE) with line numbers.
2. Click an entry — editor jumps to and highlights the matching line.
3. Place the caret on a `dbo.MyProc` reference. Press `F12`. A new query window opens with the `ALTER PROCEDURE dbo.MyProc` script. Schema-bound objects retain `WITH SCHEMABINDING`.
4. Place the caret on a `dbo.MyProc` reference. Press `Ctrl+F12`. Object Explorer expands to and selects the `dbo.MyProc` node.
5. Open a script with `DECLARE @unused INT;` never read. Press `Ctrl+B, Ctrl+F`. A panel lists `@unused` with line/column. Unused procedure / function parameters are also reported.
6. Open three query windows in SSMS plus a fourth in VS 2022 (cross-host). Press `Ctrl+Q` in SSMS. The popup lists every open query tab in SSMS only (host-scoped). Fuzzy-type a filename — matches narrow. Press `Enter` — that tab activates.
7. Focus any AKML SQL UI surface (Settings dialog, History tool window, Snippet Manager). Press `F1`. The matching documentation page opens. Repeat for every surface — coverage is 100%.

### Find Invalid Objects (US8)

**Setup**: Create a test database with at least 3 known invalid objects:

```sql
CREATE TABLE dbo.A (Id INT);
CREATE VIEW dbo.V_A AS SELECT Id, NoSuchColumn FROM dbo.A;  -- broken: NoSuchColumn doesn't exist
CREATE PROCEDURE dbo.P_A AS SELECT * FROM dbo.NoSuchTable;  -- broken: NoSuchTable doesn't exist
CREATE SYNONYM dbo.S_A FOR dbo.AlsoNoSuchTable;             -- broken
```

**Verify**:

1. Right-click the test database in Object Explorer. Pick "Find Invalid Objects".
2. A dockable tool window appears with at least 3 rows: `V_A` (View), `P_A` (Procedure), `S_A` (Synonym).
3. Each row shows: object name, schema, type, error message, source line number.
4. Double-click `V_A`. Object Explorer jumps to the view. Status bar shows "Invalid column 'NoSuchColumn' in view dbo.V_A".
5. Multi-select all three rows. Click "Script as ALTER". A new query window opens with three concatenated `ALTER` scripts.
6. Connect to a database with no invalid objects. Run the scan. Window shows "No invalid objects found" + Refresh button.
7. Open a database with 5,000+ user objects. Run the scan. Verify partial results stream into the window within 2 seconds and the full scan completes within 30 seconds.

### Result-grid productivity audit (US9)

**Setup**: Run `SELECT TOP 10 Id, Name FROM Customers UNION ALL SELECT NULL, 'test'`.

**Verify**:

1. Right-click the result grid → "Copy as IN Clause". Paste into a new query — verify `(1, 2, 3, …)` minus the NULL row. Status bar reads "Copied 10 values to clipboard. 1 NULL value omitted."
2. Run `SELECT TOP 5 Id, Name FROM Customers` against a table with an IDENTITY column. Right-click → "Script as INSERT". A dialog asks "Wrap with `SET IDENTITY_INSERT ON/OFF`?". Pick Yes. The clipboard contains the INSERT wrapped in IDENTITY_INSERT toggles.
3. Run a query producing a numeric value with 18 significant digits (e.g., `SELECT 12345678901234567890.123 AS BigNum`). Right-click → "Open in Excel". Excel opens with `12345678901234567890.123` (not the 15-digit truncation `12345678901234600000`).

---

## Milestone M3 — Refactoring & execution shortcuts

### Refactoring chord family + Smart Rename + execution shortcuts (US10)

**Setup**: A test database with a table column referenced by ≥ 3 views, 2 procedures, and a trigger.

**Verify chord family**:

1. Select identifiers in any query. Press `Ctrl+B, Ctrl+B`. Brackets toggle on every identifier in the selection.
2. Select an `EXEC procName` call. Press `Ctrl+B, Ctrl+I`. The procedure body is inlined where the EXEC was (when inlineable).
3. Select a block of SQL. Press `Ctrl+B, Ctrl+E`. A dialog asks for a new procedure name. Type "MyEncapsulated". Click OK. The selection is replaced with `EXEC dbo.MyEncapsulated @params` and a new `CREATE PROCEDURE dbo.MyEncapsulated` window opens with the original SQL as the body.

**Verify Smart Rename**:

1. Caret on the test column. Press `F2`. A dialog opens with the current name (e.g., `OldName`) and a new-name field.
2. Type `NewName`. Click "Preview". Three tabs appear: Actions (generated T-SQL), Warnings (any collisions or extended-property notes), Dependencies (list of every dependent object with line/col).
3. The Dependencies tab lists the 3 views, 2 procedures, and 1 trigger. Total count is 6.
4. Type a name that already exists in the schema. The Warnings tab lists the collision. The Apply button is disabled.
5. Type a unique name and click "Apply". The script runs transactionally. Verify all 6 dependents still parse cleanly (run `sp_refreshsqlmodule` on each, no errors).
6. Intentionally trigger a mid-script failure (e.g., disconnect during apply). The transaction rolls back. The original column name remains.

**Verify execution shortcuts**:

1. Open a script with three `GO`-separated batches. Place the cursor in batch 2. Press `Alt+Shift+F5`. Only batch 2 runs (results pane shows results for batch 2 only).
2. Place the cursor mid-batch. Press `Ctrl+Shift+F5`. Execution runs from the start of the batch up to the line above the cursor.
3. Place the cursor on a line with a `DELETE FROM X` (no WHERE) within batch 2. Press `Alt+Shift+F5`. The pre-execution safety dialog appears. Cancel — nothing runs.
4. Place the cursor on the very first line of a batch. Press `Ctrl+Shift+F5`. Status bar shows "No statements before cursor."

### Completion polish + Object Definition Box + dual-instance test + format markers (US11)

**Verify**:

1. Press `Ctrl+Shift+P`. Status bar: "AKML SQL suggestions suppressed for this session". Type — completion popup does NOT appear.
2. Press `Ctrl+Shift+P` again. Status bar: "AKML SQL suggestions enabled". Type — popup resumes.
3. Open Options → Completion → Commit Keys. Enable Space. Save. Type `Ord ` (with Space). Completion commits `Orders` and inserts a Space.
4. Open the completion popup. Press `Ctrl+Down`. Category badge changes from "All" to "Tables". Continues: Views → Columns → Functions → Procedures → Snippets → back to All.
5. Hover an object with `MS_Description = 'Top-level customer table'` (set the description via SSMS). The tooltip includes the description text.
6. Inside a function call, the parameter signature popup is shown. The next-expected parameter is bolded.
7. Open an encrypted procedure with DAC permission. View the Script tab in the Object Definition Box. The decrypted body shows with a "decrypted" badge.
8. Without DAC permission, the Script tab shows the encrypted placeholder + "DAC required" hint.
9. Type `CREATE TABLE #tmp (a INT, b NVARCHAR(50))` then on a later line `INSERT INTO #tmp (`. Completion offers `a` and `b`.
10. Open query window 1 connected to ServerA. Open query window 2 connected to ServerB. Focus window 1, type `USE `. Only ServerA's databases appear. Focus window 2, type `USE `. Only ServerB's databases appear. No cross-server leak.
11. Select a hand-aligned SQL block. Invoke "Disable formatting for selected text" from the Actions list. The selection is wrapped in `-- akml-format off` / `-- akml-format on`. Run Format Document — the wrapped block is preserved verbatim.

---

## Milestone M4 — WPF theme refresh + Options Dialog Phase 3

### WPF theme refresh continuation + SettingsWindow migration (US12)

**Verify**:

1. Open SSMS 22 in Dark theme. AKML SQL → Options. The window opens. Compare against `doc/SQL-PROMPT/SQL-Prompt-Option/13_options_dialog.svg`. Match layout, navigation pane, content panel, headings, primary/secondary button hierarchy.
2. Same in Light theme. Switch hosts (e.g., re-test in VS 2022 in both themes).
3. With Options open, change the AKML theme dropdown from Dark → Light. Within 1 second, the window re-renders without losing in-flight unsaved edits.
4. Open every WPF surface in the inventory (SnippetManagerDialog, ProfileEditorDialog, HistoryToolWindow, HistoryDiffWindow, AiChatToolWindow, DocumentOutlineToolWindow, ObjectSearchWindow, CommandPaletteWindow, SchemaProgressMargin, completion popup chrome, peek-definition control, analysis-finding tooltips, editor toolbar). Verify none uses legacy chrome.
5. Run `pwsh scripts/audit-wpf-theme.ps1`. Zero hits.

### Options Dialog Phase 3 — Format Styles editor, built-ins, Redgate import, environment color editor (US12)

**Verify**:

1. Options → Format → Styles. The page is slim: Active Style dropdown + Edit button.
2. Click Edit. A 3-column editor opens: Style List | Categories tree | Options + Preview.
3. The Style List shows user styles + 4 built-ins (Compact, Aligned, Verbose, Redgate Compatible). Built-ins have a lock icon and the right-side panels are read-only.
4. Click Import. Select a `.sqlpromptstylev2` file. After import, a dialog reports `translatedCount`, `unsupportedCount`, and lists any unsupported options.
5. Open Options → Tabs → Color. Click "Manage Environments". A sub-dialog opens with Label/Pattern/Color fields and live color preview. Add a "Pre-Prod" environment. Click OK. The right-click tab submenus (US4) now include "Pre-Prod" without restart.

---

## Milestone M5 — AI feature reach + finishing

### AI keyboard shortcuts + feature reach (US13)

**Setup**: Enable AI in Settings with at least one provider configured (Claude or Gemini with a valid API key, which is stored DPAPI-encrypted).

**Verify**:

1. Press `Alt+Z`. AI panel opens and receives focus.
2. Select a SQL snippet, press `Shift+Alt+R`. AI Fix returns a revised version in-panel.
3. Select SQL, press `Ctrl+Alt+Z`. AI Optimize runs.
4. Press `Ctrl+Alt+Up Arrow` in the editor. AI ghost-text completion renders inline. Press Tab — accept. Press Esc — dismiss.
5. Disable AI in Settings. Press any AI shortcut. Status bar: "AI is disabled. Enable in Settings → AI."
6. Right-click a selected SQL block → "Explain SQL". Panel returns a plain-language explanation in under 10 seconds (for ≤ 500-line selections).
7. Open a `SELECT * FROM Orders WHERE OrderDate >= '2026-01-01'`. Invoke Query Index Analysis. Panel shows existing plan + hinted plan with proposed index + `CREATE INDEX` script + estimated improvement % within 30 seconds.
8. Run a query with a syntax typo. After the failure, a non-blocking toast offers "Fix with AI". Click — the AI panel pre-fills with the failing batch + the SQL Server error message.
9. Type `-- generate: list the top 10 customers by revenue` on a blank line. Press Tab. The comment is replaced by AI-generated SQL with the original comment retained above.
10. Click the AI panel's History tab. Previous prompts/answers list in reverse chronological order. Click "Revert to this state" on an old entry — the editor returns to that caret + content state (best-effort).
11. Select a SQL block in the editor. A small AI icon appears at the right edge. Hover — three actions (Explain / Fix / Optimize) show.
12. After every AI answer, 1–3 follow-up prompt buttons appear beneath the answer.

### Code-audit TODO closure + refactoring debt (US14)

**Verify**:

1. Open the codebase audit. For each of the 14 TODOs, verify the file no longer contains a TODO marker, OR the skeleton class has been deleted.
2. Run `pwsh scripts/audit-wpf-theme.ps1` on `src/`. Zero hardcoded chrome hex.
3. Open `src/AkmlSql.Engine/Server/PipeRpcServer.cs`. Verify the dispatch is a `Dictionary<int, IMessageHandler>` lookup, not a switch. File size < 300 lines.
4. Open `src/AkmlSql.Core/Config/AppSettings.cs`. Root `AppSettings` class is < 200 lines. Each nested settings class is in its own sibling file under `Config/`.
5. Verify that adding a new MessageType (try it locally) requires only one new file (the handler) and one dictionary insertion — no changes to `PipeRpcServer.cs`.

---

## Test gates

Run after every milestone PR:

```powershell
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj
dotnet test tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj
dotnet test tests/AkmlSql.E2E.Tests/AkmlSql.E2E.Tests.csproj
```

Expected baselines (from the spec.md SC-016 inheritance):

- Engine: ≥ 867 tests passing
- Core: ≥ 526 tests passing
- Formatting: ≥ 458 tests passing
- E2E: existing baseline (per current `master` count)

Phase 10 adds new tests per the implementation-first-with-test-backfill convention. Final post-Phase-10 expected counts will be ~50 above each baseline (one or two tests per FR landing).

Known flake: `ConfigManagerTests.Load_WhenFileAbsent_CreatesDefaultsAndSavesFile` (1-in-3 flake, parallel test runner race) — documented in `doc/spec-014-progress.md` and not a regression.

---

## Static audit script (US12 + US14)

Run after every milestone touching WPF surfaces:

```powershell
pwsh scripts/audit-wpf-theme.ps1
```

Expected: 0 hits (excluding the documented allow-list of semantic constants).

Plus the new TODO audit (US14):

```powershell
# From repo root
$pattern = 'TODO|FIXME|HACK'
$exclude = 'GridScriptGenerator\.cs'    # generated SQL output, intentional
Get-ChildItem -Path src/AkmlSql.Shell.Shared, src/AkmlSql.Engine -Recurse -Filter *.cs |
  Where-Object { $_.FullName -notmatch $exclude } |
  Select-String -Pattern $pattern |
  Measure-Object | Select-Object -ExpandProperty Count
```

Expected: 0 by end of M5.
