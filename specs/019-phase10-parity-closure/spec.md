# Feature Specification: Phase 10 — SQL Prompt Parity Closure & Bug Fixes

**Feature Branch**: `019-phase10-parity-closure`
**Created**: 2026-05-13
**Status**: Draft
**Input**: User description: "for doc/AKML-SQL-Phase10-SqlPromptParity-and-Bugs-PRD.md"
**Source of truth**: [doc/AKML-SQL-Phase10-SqlPromptParity-and-Bugs-PRD.md](../../doc/AKML-SQL-Phase10-SqlPromptParity-and-Bugs-PRD.md)

## Summary

AKML SQL has been built up across specs 001–016 to roughly two thirds of Redgate SQL Prompt 11.3's feature surface. Phase 10 is the bounded closure effort that brings the remaining one third to parity, finishes the one open bug from spec 015 (installer branding), absorbs the in-flight Options Dialog Phase 2 work currently sitting on branch `018-options-dialog-phase2`, completes spec 016's WPF theme refresh on the remaining ~15 surfaces, and resolves the 14 code-level TODOs flagged by the 2026-05-05 codebase audit. The PRD this spec implements (`doc/AKML-SQL-Phase10-SqlPromptParity-and-Bugs-PRD.md`) is the authoritative source for the gap inventory and the reconciliation against what already ships on `master`.

The motivation is twofold. First, several recently-merged features (spec 014 US1 safety dialog, spec 014 US5 tab coloring core, spec 015's 13 user stories) are not reflected in `progress.md` or `bugs.md` and the on-disk `tasks.md` files; the documentation no longer matches the code. Second, the headline SQL Prompt features SQL developers reach for in their first hour with AKML SQL — Column Picker, wildcard expansion on `*`+Tab, dockable Issues window, lightbulb quick-fixes, right-click tab color assignment — are still missing.

Phase 10 succeeds when (a) every "shipped" claim in `doc/progress.md` and this spec ties to a verifiable code path, (b) the 17 remaining spec-014 user stories (US2–US20 minus US1 and US5 which are already done) all land, (c) BUG-B14 (installer branding) closes, (d) every spec-016 WPF surface either uses ThemeTokens or is explicitly listed as a WinForms exclusion, and (e) every audit TODO is either wired or deleted.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Documentation matches the code; in-flight work is on master (Priority: P1)

A new contributor opens `doc/progress.md`, `doc/bugs.md`, `CLAUDE.md`, and the various `specs/0NN/tasks.md` files and gets a single, accurate picture of "what ships today, what is in progress, and what is open". Today they get three contradictory pictures. The in-flight Options Dialog Phase 2 branch (`018-options-dialog-phase2`) is merged to `master` and the documentation is updated to cite real commit hashes for everything that has shipped.

**Why this priority**: every other story depends on a shared, accurate baseline. Without this, the team will continue to redo work that is already done (Column Picker scaffolding, DPAPI migration, US5 tab coloring core were all at risk of being re-implemented because the docs claim they were missing or in the future).

**Independent Test**: a fresh reader reads only `doc/progress.md` + this spec's §1, then runs `git log --oneline master ^v1.0` and confirms the commit summaries match the doc's "shipped" sections; every gap row in the Phase 10 PRD §3 reconciliation table has been verified by either a code grep or a passing acceptance test.

**Acceptance Scenarios**:

1. **Given** branch `018-options-dialog-phase2`, **When** the team finishes Phase 10 M0, **Then** the branch is merged into `master` via a reviewed PR with the Phase 1+2 Options Dialog page-split commits intact (`5efe39a` … `3ec5755`).
2. **Given** `doc/progress.md` is read by a new contributor, **When** they search for "100% parity", **Then** they see no such claim — instead the doc points at this spec's reconciliation table.
3. **Given** the `Active branch:` line in `CLAUDE.md` is read, **When** the reader compares it to `git branch --show-current`, **Then** the two agree.
4. **Given** the `doc/bugs.md` file is opened, **When** a reader reaches the end, **Then** a closure note states that the file is historical (37 bugs all from March 2026, all fixed) and live bugs now live in spec 015 + the codebase-audit § 1.
5. **Given** the Phase 10 PRD §3 reconciliation table is opened, **When** every "❌ Absent" or "⚠️ Partial" row is checked against code, **Then** each row matches reality (no false positives, no false negatives).

---

### User Story 2 — Column Picker for multi-column SELECT, plus `*`+Tab expansion (Priority: P1)

A SQL developer typing `SELECT  FROM dbo.Customers` (cursor before `FROM`) presses `Ctrl+Left Arrow` and sees a Column Picker inside the completion popup listing every column of `Customers` with primary-key and foreign-key badges. They press `Space` on the three columns they want and `Enter` to insert all three, comma-separated and alias-qualified if multiple tables are in scope. Separately, when they have `SELECT t.* FROM dbo.Customers t`, they place the cursor right after `*` and press `Tab` to inline-expand the wildcard into the explicit column list. Today AKML SQL only supports one-at-a-time column completion and only expands wildcards through the `Ctrl+B, Ctrl+W` chord, which means daily authors fall back to typing `SELECT *` and editing manually.

**Why this priority**: in a feedback survey of SQL Prompt users, Column Picker and `*`+Tab are the two single most-used productivity moves. Shipping these closes the most-felt daily ergonomics gap.

**Independent Test**: in a database with a table that has ≥ 20 columns including a PK and an FK, type `SELECT  FROM dbo.TestTable`, press `Ctrl+Left`, multi-select 3 columns with `Space`, press `Enter`, and verify the 3 columns are inserted comma-separated at the caret. Separately, type `SELECT t.* FROM dbo.TestTable t`, place the cursor right after `*`, press `Tab`, and verify `t.*` becomes the explicit alias-qualified column list.

**Acceptance Scenarios**:

1. **Given** an open completion popup, **When** the user presses `Ctrl+Left Arrow`, **Then** a Column Picker opens listing every column of the table under the caret in defined order, with PK and FK badge icons.
2. **Given** the Column Picker is open, **When** the user presses `Space` on multiple rows, **Then** each row shows a check mark and the selected-count footer updates.
3. **Given** ≥ 1 column is selected and the user presses `Enter` or `Tab`, **Then** the selected columns are inserted comma-separated at the caret position, alias-qualified if more than one table is in scope.
4. **Given** the Column Picker is open, **When** the user presses `Ctrl+A`, **Then** all columns are selected.
5. **Given** the Column Picker is open, **When** the user presses `Esc`, **Then** the picker closes without inserting anything.
6. **Given** `SELECT * FROM Customers c` with the cursor right after `*`, **When** the user presses `Tab`, **Then** `*` is replaced with the explicit column list of `Customers` per the active format style.
7. **Given** `SELECT c.* FROM Customers c JOIN Orders o`, **When** the user positions at `*` and presses `Tab`, **Then** the wildcard expands to alias-qualified columns from the matching table only.
8. **Given** the cursor is not immediately after a `*`, **When** the user presses `Tab`, **Then** the normal Tab behavior (indent or completion commit) applies — no wildcard expansion.

---

### User Story 3 — Dockable Code Analysis Issues window + lightbulb quick-fixes (Priority: P1)

A developer opens a 300-line stored procedure that triggers 12 analysis warnings. They invoke "Show Code Analysis Issues" and a dockable tool window appears listing every issue with rule id, severity, description, line, and column. Clicking an issue scrolls the editor to and highlights the offending text. The window supports sorting by any column, grouping by rule or severity, CSV export, and persists its docked position across SSMS restarts. Separately, in the editor, each squiggle has a gutter lightbulb — orange for auto-fixable rules, blue for advisory. Holding `Ctrl` over a squiggle shows an Issue Details popup with rule id, problem text, remediation paragraph, and an **Apply Fix** button for auto-fixable rules. Today AKML SQL only reports findings inline as squiggles and silently into the SSMS Error List — there is no audit-the-whole-script view.

**Why this priority**: turns the existing 130 analysis rules from a passive nag into an active reviewing tool, and converts the existing lightbulb infrastructure into a one-click fixing experience. Both engine paths exist; this is shell-side UI work.

**Independent Test**: open a script with ≥ 10 known issues across rule categories (BP, PE, ST, DEP). Open the Issues window and verify all issues are listed. Click an issue → editor jumps. Click Export → CSV file is written. Trigger a BP002-style violation (deprecated `!=`); verify the orange lightbulb appears. Hold `Ctrl` and hover the squiggle — verify the popup shows rule id, problem, remediation, and Apply Fix button. Click Apply Fix — verify `!=` becomes `<>` and the squiggle clears.

**Acceptance Scenarios**:

1. **Given** a script with multiple analysis issues, **When** the user opens the Issues window, **Then** all issues are listed with columns: rule id, severity, description, line, column.
2. **Given** the window is open, **When** the user clicks an issue, **Then** the editor scrolls to and highlights the offending text within one second.
3. **Given** the window is open, **When** the user changes the script and pauses typing for ≥ 1 second, **Then** the list refreshes to match the current document state.
4. **Given** the window is open and grouped by Rule, **When** the user toggles grouping off, **Then** issues appear as a flat list and the total-count header is preserved.
5. **Given** the window was docked on the right and the user restarts SSMS, **When** the extension loads, **Then** the window re-opens in its previous docked position and size.
6. **Given** an auto-fixable squiggle is present, **When** the squiggle renders, **Then** an orange lightbulb appears in the gutter on that line. Advisory-only squiggles render a blue lightbulb.
7. **Given** the user holds `Ctrl` and hovers a squiggle, **When** the popup appears, **Then** it contains the rule id, severity, problem statement, and a remediation paragraph; auto-fixable rules also show an **Apply Fix** button.
8. **Given** the user clicks **Apply Fix**, **When** the fix succeeds, **Then** the offending text is replaced with the remediation and the squiggle clears.
9. **Given** an auto-fix depends on schema metadata not yet loaded (Phase B in progress), **When** the user clicks **Apply Fix**, **Then** the fix is queued and a status-bar message indicates "waiting for schema".

---

### User Story 4 — Right-click tab color assignment + high-contrast clamp (Priority: P1)

A developer right-clicks a query tab and sees **Tab Color (Server)**, **Tab Color (Database)**, and (when applicable) **Tab Color (Server Group)** submenus offering the defined environments (Production, Staging, Development, Local, plus any custom). Picking one immediately paints the tab and every other tab on that server/database/group, including in Windows High Contrast themes where the rendered color is clamped to maintain WCAG AA legibility. Today AKML SQL ships the rules editor inline in the Settings page (spec 014 US5 core) but has no right-click submenu — meaning users have to traverse a settings dialog to assign a color, which is a tax compared to SQL Prompt's one-click experience.

**Why this priority**: completes spec 014 US5 (right-click submenu is FR-041, which the core landing missed); turns tab coloring from "a feature you configure once" into "a feature you use ad-hoc" and pairs with the existing pre-execution safety dialog so the environment color is immediately reinforced.

**Independent Test**: right-click any open query tab. Verify three submenus appear ("Tab Color (Server)", "Tab Color (Database)", optionally "Tab Color (Server Group)"), each listing the defined environments. Pick one — verify the tab paints in that color and any other tabs bound to the same server/database/group repaint within a second. Switch Windows to a High Contrast theme — verify colors remain legible (text reads against the colored background).

**Acceptance Scenarios**:

1. **Given** an open query tab, **When** the user right-clicks the tab, **Then** three submenus ("Tab Color (Server)", "Tab Color (Database)", optionally "Tab Color (Server Group)") appear, each listing every defined environment.
2. **Given** the user picks an environment from a submenu, **When** the choice is applied, **Then** the tab paints in that environment's color and the assignment is persisted to settings.
3. **Given** an assignment is made, **When** a second tab on the same server / database / group is already open, **Then** that tab also repaints within one second without restart.
4. **Given** a server belongs to a Registered Server Group that has a color, **When** the user opens a new query against that server with no direct assignment, **Then** the tab inherits the group's color (priority resolution per Phase 10 PRD F-T4).
5. **Given** Windows is in High Contrast mode, **When** any environment-colored tab is rendered, **Then** the painted color is clamped so foreground text meets WCAG AA contrast against the tab background.

---

### User Story 5 — Last open bug: installer icon and banner (Priority: P1)

A developer downloads the AKML SQL installer and is greeted with a branded application icon in Windows Explorer and a branded header banner in the installer wizard on every page. Today the installer either uses placeholder assets or default Inno Setup chrome — assets exist in `src/AkmlSql.Installer/assets/` but the installer .iss does not reference them on every page.

**Why this priority**: only open bug from spec 015 (all other 13 user stories shipped). Polish-finishing the installer is the visible "we're done" moment for the spec 015 effort and unblocks the documentation hygiene work in User Story 1.

**Independent Test**: build the installer with `iscc src/AkmlSql.Installer/AkmlSqlSetup.iss`, run the resulting EXE on a clean Windows 11 VM. Verify the EXE icon in Windows Explorer is the AKML SQL icon (not the default Inno Setup icon). Walk through every installer page and verify a branded header banner is shown on each.

**Acceptance Scenarios**:

1. **Given** the installer EXE in Windows Explorer, **When** Explorer renders the icon, **Then** the AKML SQL icon (not the Inno Setup default) is shown.
2. **Given** the installer is launched, **When** any wizard page is shown, **Then** a branded AKML SQL banner image renders in the header area.
3. **Given** the installer is launched in silent mode (`/VERYSILENT`), **When** installation completes, **Then** no banner is required (silent mode has no UI) but the icon is still present.

---

### User Story 6 — Unified Command Palette across four sources (Priority: P2)

A developer presses `Alt+S` (in SSMS) or `Alt+P` (in Visual Studio) and a fuzzy-searchable palette opens that aggregates four result types: AKML SQL commands, AKML SQL Options settings, SSMS / VS built-in commands, and (SSMS only) database objects from the active connection. Each result is tagged with a small category badge. Typing "format" returns AKML SQL's Format Document command, the Options pages that mention formatting, and the host's own Edit → Format Document. Picking an Options result opens the Settings dialog scrolled to and highlighting the matching control. Picking a database object opens its definition. Today AKML SQL ships a palette but it only enumerates AKML SQL's own commands — the other three sources are gaps.

**Why this priority**: discoverability for the cumulatively-large feature surface AKML SQL has grown into. Once every new feature in this spec lands, no user can be expected to memorize all the chords; the palette is the single most-effective fallback.

**Independent Test**: press the palette shortcut, type "format" — verify all four result categories produce hits (commands, options, host commands, no database objects since this is a generic match). Type a partial table name like "Cust" — verify SSMS surfaces matching objects from the active connection. Pick an Options result — verify Settings opens scrolled to the matching control.

**Acceptance Scenarios**:

1. **Given** the editor has focus, **When** the user presses the palette shortcut, **Then** a modal palette opens with a search box.
2. **Given** the palette is open with an empty search box, **When** the user has previously used the palette, **Then** the 10 most-recent picks are shown first (per-host).
3. **Given** the palette is open, **When** the user types a query, **Then** results update live across all four sources, ranked by fuzzy match score, each with a category badge.
4. **Given** results include AKML SQL Options entries, **When** the user picks one, **Then** the Settings dialog opens scrolled to and highlights the matching control.
5. **Given** results include database objects, **When** the user picks one, **Then** Object Explorer navigates to that node or the object's definition is opened in a new query window.
6. **Given** no active connection, **When** the user searches, **Then** database objects are simply absent from results (no blank category).
7. **Given** the palette is open, **When** the user presses `Esc`, **Then** the palette closes and focus returns to the editor.

---

### User Story 7 — Script navigation chords + Browse Open Tabs + F1 help (Priority: P2)

When working with a long script or hovering over an unfamiliar object reference, the developer presses one of four chord combinations and gets the matching navigation move:

- `Ctrl+B, Ctrl+S` — Summarize Script outline (hierarchical list of every top-level statement, click-to-navigate).
- `F12` — Script Object as ALTER (the object under the caret is opened as an `ALTER` script in a new query window on the active connection).
- `Ctrl+F12` — Select in Object Explorer (the Object Explorer tree expands to and selects the node for the object under the caret).
- `Ctrl+B, Ctrl+F` — Find Unused Variables and Parameters (lists every declared variable and procedure/function parameter never read).

Separately, `Ctrl+Q` opens a Browse Open Tabs popup listing every open query tab across all SSMS / VS windows for the active host, with fuzzy search and Enter-to-activate. And every AKML SQL UI surface honors `F1` to open the matching documentation page.

**Why this priority**: every senior SQL author uses these dozens of times per day in SQL Prompt. They are pure productivity wins.

**Independent Test**: open a 500-line script with multiple stored-procedure definitions. Press `Ctrl+B, Ctrl+S` — verify a hierarchical outline appears showing each statement type and its line range. Click an entry — verify the editor jumps. Place the caret on a `dbo.MyProc` reference and press `F12` — verify a new query window opens with that procedure scripted as `ALTER`. Press `Ctrl+F12` — verify Object Explorer expands to and selects the node. Type a script with an unused `@p2` variable and press `Ctrl+B, Ctrl+F` — verify a panel lists `@p2` with line/column. Press `Ctrl+Q` — verify a popup lists every open query tab with fuzzy search. Press `F1` while focused on any AKML SQL UI surface — verify the matching documentation page opens.

**Acceptance Scenarios**:

1. **Given** any script with multiple statements, **When** the user presses `Ctrl+B, Ctrl+S`, **Then** the Summarize Script dialog appears showing each top-level statement grouped and indented with line numbers.
2. **Given** the caret is on an object reference, **When** the user presses `F12`, **Then** a new query window opens containing the `ALTER` script for that object on the active connection. Schema-bound objects retain their `WITH SCHEMABINDING` clause.
3. **Given** the same caret position, **When** the user presses `Ctrl+F12`, **Then** Object Explorer expands to and selects the node for that object.
4. **Given** a script with a `DECLARE @unused INT;` that is never read, **When** the user presses `Ctrl+B, Ctrl+F`, **Then** a panel lists `@unused` with line and column. Unused procedure/function parameters are also reported.
5. **Given** the user presses `Ctrl+Q`, **When** the popup opens, **Then** every open query tab across all SSMS / VS windows for the active host is listed with fuzzy search; pressing Enter activates the selected tab.
6. **Given** focus is on any AKML SQL UI surface (Options page, dialog, tool window), **When** the user presses `F1`, **Then** the matching documentation page opens. Coverage is 100% — no surface is missing an `F1` registration.

---

### User Story 8 — Find Invalid Objects across the database (Priority: P2)

After a schema migration the developer right-clicks the database in Object Explorer and picks **Find Invalid Objects**. AKML SQL scans every user object for broken references — views referencing dropped columns, procedures referencing missing tables, synonyms pointing nowhere, triggers on dropped tables — and lists them in a dockable tool window with object name, schema, type, error message, and source line number. Selecting a row and clicking **Script as ALTER** opens that object's `ALTER` script in a new query window (multi-select concatenates). Today AKML SQL has the IPC DTOs scaffolded (`FindInvalidObjectsRequest.cs`, `Response.cs`, `Record.cs`) but no engine handler or tool window.

**Why this priority**: pays for itself the first time it runs. Migrations routinely leave behind dozens of invalid views and procedures with no cheap way to find them all.

**Independent Test**: connect to a database with at least 3 known invalid objects (one view referencing a dropped column, one procedure referencing a missing table, one synonym pointing nowhere). Right-click the database in Object Explorer → **Find Invalid Objects**. Verify a dockable window lists all three. Select a row and click **Script as ALTER** — verify the matching `ALTER` script opens in a new query window.

**Acceptance Scenarios**:

1. **Given** a database with broken-reference objects, **When** the user runs Find Invalid Objects, **Then** a dockable tool window lists each invalid object with name, schema, type, error message, and line number.
2. **Given** the list is shown, **When** the user double-clicks a row, **Then** Object Explorer jumps to that object and the error message is shown in the status bar.
3. **Given** the list is shown, **When** the user clicks **Script as ALTER**, **Then** a new query window opens with the `ALTER` script for that object.
4. **Given** the user multi-selects rows and clicks **Script as ALTER**, **Then** all selected scripts are concatenated into one new query window.
5. **Given** a database with no invalid objects, **When** the scan completes, **Then** the window shows "No invalid objects found" with a refresh button.
6. **Given** the user lacks the permissions to read object metadata for some objects, **When** the scan runs, **Then** the window reports the permission error and lists only the objects it could verify.
7. **Given** a database with 5,000 user objects, **When** the scan runs, **Then** partial results stream into the window within two seconds and the full scan completes within 30 seconds on average hardware.

---

### User Story 9 — Result-grid productivity audit and completion (Priority: P2)

After running a query the developer right-clicks the result grid and picks **Copy as IN Clause** (selected rows → `('val1','val2',…)` for the next query), **Script as INSERT** (selected rows → `INSERT INTO X (cols) VALUES …` round-trippable), or **Open in Excel** with full numeric precision preserved beyond Excel's default 15-digit truncation. Today these actions exist in the shell but their behaviour against the Phase 10 PRD spec needs audit — Copy-as-IN's NULL-omission status message and Script-as-INSERT's `SET IDENTITY_INSERT` opt-in dialog need verification, and Open-in-Excel's wide-precision-as-text formatting needs end-to-end verification.

**Why this priority**: removes the most common copy-paste-and-edit dance in SQL day-to-day work. The Open in Excel precision fix is niche but high-impact for finance teams.

**Independent Test**: run `SELECT TOP 10 Id FROM Customers`. Right-click → **Copy as IN Clause**. Paste into a new query — verify `(1, 2, 3, 4, 5, 6, 7, 8, 9, 10)`. Include a NULL row in the selection — verify a status message reports the NULL omission count. Right-click → **Script as INSERT** — if the target table has an IDENTITY column, verify a dialog asks whether to wrap with `SET IDENTITY_INSERT ON/OFF`. Right-click → **Open in Excel** with a column containing `12345678901234567890.123` — verify the value in Excel is the full number, not the 15-digit truncation.

**Acceptance Scenarios**:

1. **Given** rows selected in a result grid, **When** the user picks **Copy as IN Clause**, **Then** the clipboard contains the values comma-separated, properly quoted by data type, wrapped in parentheses; NULL values are omitted and a status message reports the omission count.
2. **Given** rows selected, **When** the user picks **Script as INSERT**, **Then** the clipboard contains a `INSERT INTO <schema.table> (<cols>) VALUES (...), (...)` statement that round-trips when executed.
3. **Given** the target table has an IDENTITY column and the user picks **Script as INSERT**, **When** the dialog appears, **Then** the user can opt in to wrap the script with `SET IDENTITY_INSERT ON/OFF`.
4. **Given** the result grid contains numeric columns with > 15 significant digits, **When** the user picks **Open in Excel**, **Then** the cells in Excel contain the full original precision (wide cells formatted as text).
5. **Given** no rows are selected, **When** the user picks any of the three actions, **Then** they operate on every visible row.
6. **Given** the result grid contains binary or geography/geometry columns, **When** **Script as INSERT** runs, **Then** binary values are emitted as `0x...` literals and a warning is shown for unsupported types.

---

### User Story 10 — Refactoring chord family, Smart Rename, execution shortcuts (Priority: P3)

Three keyboard-first ergonomics improvements ship together. (1) The full SQL Prompt `Ctrl+B` chord family: today AKML SQL ships `Ctrl+B, Ctrl+Y/U/C/W/Q`; this story adds `Ctrl+B, Ctrl+B` (Brackets toggle), `Ctrl+B, Ctrl+I` (Inline Stored Procedure), `Ctrl+B, Ctrl+E` (Encapsulate as Stored Procedure). (2) Smart Rename: pressing `F2` on a database column referenced by 3 views, 2 procedures, and a trigger shows a preview dialog with Actions / Warnings / Dependencies tabs and only applies the script after explicit confirmation; the rename is transactional. (3) Execution shortcuts: `Alt+Shift+F5` executes the current batch only (between the surrounding `GO` markers); `Ctrl+Shift+F5` executes from start-of-batch to the line above the cursor. Both new execution shortcuts trigger the pre-execution safety dialog.

**Why this priority**: power-user features. Each is valuable in isolation but they collectively represent the "ergonomic luxury" tier — daily authors will adopt them within a week of being told they exist, but lack of them does not block adoption.

**Independent Test**: in any query, select identifiers and press `Ctrl+B, Ctrl+B` — verify brackets toggle. Pick a column referenced by ≥ 3 dependent objects and press `F2` — verify a dialog with Actions / Warnings / Dependencies tabs appears, click Apply, verify all dependents still parse. Open a script with three `GO`-separated batches, place cursor in batch 2, press `Alt+Shift+F5` — verify only batch 2 runs. Place cursor mid-batch, press `Ctrl+Shift+F5` — verify only lines from start-of-batch to the line above the cursor run.

**Acceptance Scenarios**:

1. **Given** a selection of identifiers, **When** the user presses `Ctrl+B, Ctrl+B`, **Then** brackets are added/removed as a toggle on every identifier in the selection.
2. **Given** a selection containing an `EXEC procName` call, **When** the user presses `Ctrl+B, Ctrl+I`, **Then** the procedure body is inlined in place of the EXEC when the procedure is simple enough to inline.
3. **Given** a selected block of SQL, **When** the user presses `Ctrl+B, Ctrl+E`, **Then** the user is prompted for a new procedure name and the selection is replaced with `EXEC newProc @params` while a new `CREATE PROCEDURE` opens in a new query window.
4. **Given** the caret is on an identifier the user wants to rename, **When** they press `F2`, **Then** a Smart Rename dialog appears with the current name and a new-name field.
5. **Given** the new name is typed, **When** the user clicks **Preview**, **Then** the dialog shows Actions / Warnings / Dependencies tabs listing every dependent object with its count and updated definition.
6. **Given** the script runs and any step fails (transient connection drop, permission error), **When** the failure is detected, **Then** the rename rolls back and the original object is unchanged.
7. **Given** a script with three `GO`-separated batches, **When** the user places the cursor in batch 2 and presses `Alt+Shift+F5`, **Then** only batch 2 runs.
8. **Given** the cursor is mid-batch, **When** the user presses `Ctrl+Shift+F5`, **Then** execution runs from start-of-batch up to the line above the cursor.
9. **Given** the about-to-run portion of either new execution shortcut contains an unsafe statement, **When** the shortcut is pressed, **Then** the pre-execution safety dialog appears the same way it does for `F5` / `Shift+F5`.

---

### User Story 11 — Completion polish, Object Definition Box, dual-instance test, format markers (Priority: P3)

Four polish items round out the completion experience. (1) Completion polish: `Ctrl+Shift+P` toggles IntelliSense on/off per session; custom commit keys configurable in Options; `Ctrl+Up/Down` cycles the category filter; `MS_Description` extended property surfaces in tooltips; parameter highlight bolds the next-expected parameter; encrypted procedures show decrypted body in the Script tab when DAC permission is held; temp-table `#temp` IntelliSense parses from same-script `CREATE TABLE` / `SELECT INTO` and offers column completions for `#temp` references later in the same script scope; `ALTER TABLE` and `INSERT INTO` template formatting is user-configurable. (2) Object Definition Box: the existing `ObjectDefinitionPanel.cs` file is audited for Summary/Script tabs, resize-persist behaviour, and `Ctrl`-transparency. (3) Dual-instance awareness regression test — verifies the per-text-view file-path lookup in `SsmsConnectionDetector` does not regress to `DTE.ActiveDocument` fallback. (4) Editor action "Disable formatting for selected text" — wraps the selection in `-- akml-format off` / `-- akml-format on` marker comments (the `NoformatScanner` already honors them; only the UI action is missing).

**Why this priority**: each item is small but together they remove the "rough edges" the higher-priority stories don't address.

**Independent Test**: press `Ctrl+Shift+P` — verify completion popup stops appearing. Press it again — verify it resumes. Configure Space as a commit key in Options — verify typing `Ord ` commits `Orders`. Inside the popup press `Ctrl+Down` — verify the category badge changes from "All" to "Tables". Hover an object with `MS_Description = 'top-level customer table'` — verify the description appears. Type `CREATE TABLE #tmp (a INT, b NVARCHAR(50))` then `INSERT INTO #tmp (` — verify `a` and `b` are suggested. Open two query windows on different servers; type `USE ` in each — verify each shows only its own server's databases. Select a hand-aligned SQL block; invoke "Disable formatting for selected text" — verify the block is wrapped in `-- akml-format off / on`; run Format Document — verify the wrapped block is preserved verbatim.

**Acceptance Scenarios**:

1. **Given** the editor has focus, **When** the user presses `Ctrl+Shift+P`, **Then** completion is suppressed for the session and a status-bar message confirms.
2. **Given** the user has enabled Space as a commit key in Options, **When** they highlight a suggestion and press Space, **Then** the suggestion is committed followed by a single space.
3. **Given** the popup is open, **When** the user presses `Ctrl+Down`, **Then** the category filter cycles through Tables → Views → Columns → Functions → Procedures → Snippets → All.
4. **Given** a hovered object has `MS_Description`, **When** the tooltip renders, **Then** the description appears beneath the object name.
5. **Given** the user has typed `CREATE TABLE #tmp (a INT, b NVARCHAR(20))` earlier in the same script, **When** they then type `INSERT INTO #tmp (`, **Then** completions for `a` and `b` appear.
6. **Given** the user has DAC permission and views an encrypted procedure's Script tab, **When** the tab renders, **Then** the decrypted body is shown with a clear "decrypted" badge.
7. **Given** two query windows on different servers, **When** the user types `USE ` in either, **Then** only that window's server's databases appear.
8. **Given** a selection in the editor, **When** the user invokes "Disable formatting for selected text", **Then** the selection is wrapped in `-- akml-format off` and `-- akml-format on` comment lines.
9. **Given** a document with `-- akml-format off / on` markers, **When** the user runs Format Document, **Then** content between markers is preserved verbatim and content outside is formatted.

---

### User Story 12 — WPF theme refresh continuation + Options Dialog Phase 3 (Priority: P3)

Spec 016 Phase 1+2 (foundational `ThemeRegistry`, `ThemeTokens`, `HostThemeWatcher`, `ThemeAwareWindow`) and Phase 4 Batch 1 (5 WPF surface migrations) have shipped. Phase 10 finishes the remaining ~15 WPF surfaces, with explicit acknowledgment that 8 WinForms dialogs (`AboutDialog`, `BulkAnalysisResultDialog`, `LogViewerDialog`, `RefactoringPreviewDialog`, `SessionRecoveryDialog`, `BulkFormatProgressDialog`, `TextToSqlInputDialog`, `CellEditDialog`) remain on pre-refresh chrome until a follow-up spec ports them to WPF. The primary remaining target is `SettingsWindow` itself (the original complaint that drove spec 016). Concurrent with this, the Options Dialog Phase 3 plan — Format Styles 3-column editor, three new built-in styles (`aligned.akmlstyle`, `verbose.akmlstyle`, `redgate-compatible.akmlstyle`), Redgate `.sqlpromptstylev2` importer warnings UI, Environment Color Editor sub-dialog, Format › Styles page slim-to-dropdown — ships.

**Why this priority**: the Options window was the original "looks unfinished" complaint that triggered spec 016. Phase 1+2 shipped the infrastructure but the Options window itself is still on legacy chrome. Phase 3 of the Options Dialog plan brings the Format Styles editor to SQL Prompt parity. Together these close the visible "polish" gap.

**Independent Test**: open AKML SQL → Options in both Dark and Light themes on at least one SSMS host and one VS host. Visually compare against `doc/SQL-PROMPT/SQL-Prompt-Option/13_options_dialog.svg`. Open every remaining WPF surface in the surface inventory and confirm none uses legacy chrome. Open Format › Styles — verify the page is slim (Active Style dropdown + Edit button). Click Edit — verify a 3-column editor opens (Style List | Categories | Options + Preview). Import a `.sqlpromptstylev2` file from a Redgate user's machine — verify a post-import dialog reports translated/unsupported option counts.

**Acceptance Scenarios**:

1. **Given** SSMS is in Dark theme, **When** the user opens AKML SQL → Options, **Then** the window background, navigation pane, content panel, inputs, headings, and buttons all use the dark palette from `ThemeTokens` and pass a visual review for legibility and consistency.
2. **Given** the Options window is open, **When** the user changes the AKML theme dropdown from Dark to Light (or vice-versa), **Then** the window re-renders into the chosen theme within one second.
3. **Given** any AKML-owned WPF surface in the surface inventory (modal dialogs, dockable tool windows, in-editor adornments), **When** opened in either theme, **Then** its chrome colors come exclusively from `ThemeTokens` — no inline hex literals remain (the static-audit script at `scripts/audit-wpf-theme.ps1` returns zero hits).
4. **Given** Format › Styles is opened, **When** the user clicks Edit, **Then** a 3-column Style Editor opens with Style List on the left, Categories tree in the middle, Options + Preview on the right.
5. **Given** the Style List is shown, **When** built-in styles are rendered, **Then** they are visually marked as read-only (lock icon) and the right-side panels are read-only for those styles.
6. **Given** three built-in styles exist in the bundled assets (`aligned`, `verbose`, `redgate-compatible`), **When** the Style List is populated, **Then** all three appear alongside any user styles.
7. **Given** the user clicks Import and selects a `.sqlpromptstylev2` file, **When** the import completes, **Then** a dialog reports `translatedCount`, `unsupportedCount`, and lists any unsupported options.
8. **Given** the Options dialog Environment Color Editor button is clicked, **When** the sub-dialog opens, **Then** the user can add/edit/remove environments with Label/Pattern/Color fields and live color preview.

---

### User Story 13 — AI keyboard shortcuts + Explain / Index / Comment-to-SQL / Auto-fix / History / Selection icon (Priority: P3)

The AI feature set lands as keyboard-first plus several new surfaces. `Alt+Z` opens the AI panel; `Shift+Alt+R` runs AI Fix on the selection; `Ctrl+Alt+Z` runs AI Optimize on the selection; `Ctrl+Alt+↑` manually triggers AI ghost-text. Right-click selection → Explain SQL returns a plain-language explanation in the AI panel within 10 seconds. Query Index Analysis runs an ML-based evaluation of candidate indexes for a `SELECT...WHERE/JOIN` and returns existing-vs-hinted plans plus a copyable `CREATE INDEX` script. After a failed execution, a non-blocking toast offers "Fix with AI" pre-filling the panel with the failing batch and error. Typing `-- generate: <natural language>` and pressing Tab replaces the comment with AI-generated SQL. The AI panel grows a History tab listing previous prompts/answers with "revert to this state". When a SQL block is selected in the editor, an AI icon appears at the right edge with Explain / Fix / Optimize hover actions. Each AI answer is followed by 1–3 follow-up prompt buttons.

**Why this priority**: every item is independently valuable but each is independently optional — the broader AI investment already shipped in spec 009. This story is the "make AI keyboard-first like SQL Prompt does".

**Independent Test**: with AI enabled in Settings, press `Alt+Z` — verify the AI panel opens. Select a SQL snippet and press `Shift+Alt+R` — verify AI Fix returns a corrected version. Right-click selection → Explain SQL — verify a plain-language explanation appears within 10s. Open a slow `SELECT...WHERE col = @p` — run Query Index Analysis — verify the panel shows existing plan + hinted plan + `CREATE INDEX` script. Run a query with a typo — verify a "Fix with AI" toast appears. Type `-- generate: list the top 10 customers by revenue` and press Tab — verify the AI generates the matching SQL. Click the History tab in the AI panel — verify previous prompts/answers list. Select a SQL block — verify the AI icon appears at the right edge.

**Acceptance Scenarios**:

1. **Given** AI is enabled, **When** the user presses `Alt+Z`, **Then** the AI chat panel opens and receives focus.
2. **Given** SQL is selected, **When** the user presses `Shift+Alt+R`, **Then** AI Fix runs against the selection and returns a revised version.
3. **Given** SQL is selected, **When** the user presses `Ctrl+Alt+Z`, **Then** AI Optimize runs.
4. **Given** the caret is in an editable position, **When** the user presses `Ctrl+Alt+↑`, **Then** an AI ghost-text completion is rendered inline; Tab accepts, Esc dismisses.
5. **Given** AI is disabled in Settings, **When** any AI shortcut is pressed, **Then** a brief status-bar message indicates AI is disabled.
6. **Given** SQL is selected, **When** the user invokes Explain SQL, **Then** the AI panel returns a plain-language explanation in under 10 seconds for selections of ≤ 500 lines.
7. **Given** a query with `WHERE` or `JOIN` is open, **When** the user invokes Query Index Analysis, **Then** the panel returns existing-vs-hinted plans, an estimated impact percentage, and a `CREATE INDEX` script in under 30 seconds for 95% of statements against tables with up to 1M rows.
8. **Given** a query has just failed with a syntax error, **When** the failure dialog closes, **Then** a non-blocking toast offers "Fix with AI"; clicking it pre-fills the AI panel.
9. **Given** the user types `-- generate: <natural language>` on a blank line and presses Tab, **When** the request returns, **Then** the comment line is replaced by AI-generated SQL with the original comment retained above.
10. **Given** the AI panel has history, **When** the user clicks the History tab, **Then** previous prompts and answers are listed in reverse chronological order with "revert to this state" actions.
11. **Given** the user selects a SQL block in the editor, **When** the selection is committed, **Then** an AI icon appears at the right edge of the selection with Explain / Fix / Optimize hover actions.
12. **Given** the AI returned an answer, **When** the panel renders, **Then** 1–3 follow-up prompt buttons are shown beneath the answer.

---

### User Story 14 — Code-audit TODO closure + refactoring debt (Priority: P3)

Fourteen code-level TODOs flagged by the 2026-05-05 codebase audit are resolved. The two skeleton MEF exports for SignatureHelp and QuickInfo are either wired via `PipeRpcClient` or deleted entirely. The three Format-on-Save/Paste/Delimiter handlers are wired (extracted into one shared `FormatRequestDispatcher`) or deleted. The `CrudGenerationCommand` gets a proper schema/table/operation dialog. The two SSMS connection-context TODOs are collapsed into one shared `SsmsConnectionContextResolver`. The `GridAccessHelper` gets the SSMS 20 fallback. The two snippet DTO placeholders (`WasFormatted = false`, `UsageCount = 0`) are either wired or the fields are deleted from the wire format. The installer T096 (restore native SSMS IntelliSense on uninstall) ships. Concurrently, the `PipeRpcServer` 55-case dispatch switch is refactored into a `Dictionary<int, IMessageHandler>` (highest-ROI refactor per the audit), and `AppSettings.cs` (961 lines, 19 nested classes) is split into per-domain files.

**Why this priority**: code health rather than user-visible features. Pays interest on every future feature touching the same files.

**Independent Test**: open the codebase audit. For each of the 14 TODOs, verify the corresponding file no longer contains a TODO marker (deleted skeleton classes count). Run the static-audit grep from `scripts/audit-wpf-theme.ps1` on `src/` — verify zero hardcoded chrome hex. Open `PipeRpcServer.cs` — verify the dispatch is a dictionary lookup, not a switch. Open `AppSettings.cs` and the new per-domain files — verify the root `AppSettings` is < 200 lines.

**Acceptance Scenarios**:

1. **Given** the codebase audit § 1 listing, **When** a code reviewer searches each cited file for the TODO marker, **Then** each TODO is either resolved (wired) or removed (skeleton deleted, fields removed).
2. **Given** `src/AkmlSql.Engine/Server/PipeRpcServer.cs`, **When** the file is opened, **Then** the dispatch is a `Dictionary<int, IMessageHandler>` lookup and the file is under 300 lines.
3. **Given** `src/AkmlSql.Core/Config/AppSettings.cs`, **When** the file is opened, **Then** the root `AppSettings` class is under 200 lines and each nested settings class is in its own file under `Config/`.
4. **Given** adding a new MessageType, **When** a contributor follows the established pattern, **Then** they register one new handler instance in the dictionary and need not modify `PipeRpcServer.cs` itself.

---

### Edge Cases

- **DELETE with subquery WHERE** (e.g. `DELETE FROM X WHERE id IN (SELECT id FROM Y)`): pre-execution safety check must recognize a WHERE clause exists and not warn.
- **MERGE without WHEN MATCHED filter**: treated as unsafe (same as DELETE / UPDATE without WHERE).
- **Dynamic SQL** inside `EXEC sp_executesql N'DELETE FROM X'`: invisible to the parser; the safety check must not crash but may not inspect it (documented limitation, same as SQL Prompt).
- **Column Picker with 500+ columns**: the picker must virtualize the list so a wide table does not freeze the UI.
- **Wildcard expansion with columns containing reserved keywords**: expanded column names must be bracketed if the name is a reserved keyword or contains spaces.
- **Command Palette while no connection is active**: database objects absent from results; commands and options still work.
- **Right-click tab color in High Contrast Windows themes**: environment colors clamp to maintain WCAG AA contrast against text.
- **Formatting markers inside string literals** (`SELECT '-- akml-format off' AS Literal`): not parsed as real markers.
- **Smart Rename on a system table or system column**: preview refuses with a clear "system objects cannot be renamed" message.
- **Smart Rename mid-script transactional failure**: rolled back, original object unchanged.
- **Copy as IN Clause on a column with NULL values**: NULL values omitted (an `IN` clause cannot match NULL); status message reports the omission count.
- **Script as INSERT for a table with an IDENTITY column**: only wraps with `SET IDENTITY_INSERT ON/OFF` if user opts in via the dialog.
- **Open in Excel with a date-only column**: Excel shows the date without spurious time components.
- **AI shortcuts while AI is rate-limited**: clear "rate limited, retry in N seconds" status; not silently ignored.
- **Comment-to-SQL inside a multi-line comment block** (`/* generate: ... */`): generation triggers only on single-line `-- generate:` comments.
- **Encrypted decryption without DAC permission**: Script tab shows encrypted placeholder and "DAC required" hint; no decryption attempt is made.
- **Execute To Cursor on the very first line of a batch**: nothing runs; status-bar message indicates "no statements before cursor".
- **Browse Open Tabs (`Ctrl+Q`) when no tabs are open**: empty popup with a "no open tabs" hint.
- **Toggle suggestions off (`Ctrl+Shift+P`) persists across editor windows**: per-session; closing all editor windows resets it on next SSMS launch.
- **Find Invalid Objects on a database with thousands of objects**: scan runs in chunks; partial results stream into the window within 2 seconds.
- **Mid-dialog theme change**: changing the AKML theme dropdown while the Options window is open must finish re-rendering without losing the user's in-flight unsaved edits.
- **Windows High Contrast active**: AKML chrome remains readable via the High Contrast palette fallback.
- **Modal dialog parented to a window in a conflicting theme**: modal uses the AKML theme preference, not the parent's.
- **Reduce-motion preference active**: schema-progress spinner falls back to static "Loading…" indicator; theme switches are instantaneous (no crossfade).

## Requirements *(mandatory)*

### Functional Requirements

**Documentation hygiene & merge (US1)**

- **FR-001**: Branch `018-options-dialog-phase2` MUST be merged into `master` via a reviewed pull request with the Options Dialog Phase 1 + Phase 2 commit history preserved.
- **FR-002**: `doc/progress.md`'s "100% SQL Prompt v11 parity" claim MUST be replaced by a pointer to the Phase 10 PRD `§3` reconciliation table.
- **FR-003**: `CLAUDE.md`'s `Active branch:` line MUST agree with `git branch --show-current` after the M0 merge.
- **FR-004**: `doc/bugs.md` MUST end with a closure note identifying it as historical (March 2026, all fixed) and pointing live bugs at spec 015 + the codebase audit.
- **FR-005**: `doc/AKML_SQL_Gap_Analysis_1.md` MUST carry a "Superseded by Phase 10 PRD §3" banner.
- **FR-006**: `specs/014-sql-prompt-parity/tasks.md` MUST be updated to mark the user stories that shipped (US1, US5) as `[X]` and to point readers to this spec for the remaining stories.

**Column Picker, Wildcard expansion (US2)**

- **FR-007**: System MUST provide an in-popup Column Picker reachable via `Ctrl+Left Arrow` from the suggestion list.
- **FR-008**: The Column Picker MUST list columns in the table's defined order by default with an option to toggle alphabetical sort, and visually mark PK and FK columns with distinctive badges.
- **FR-009**: The Column Picker MUST support multi-selection via `Space` and `Ctrl+A` (select all), and MUST insert selected columns comma-separated at the caret on `Enter` or `Tab` with table-alias qualification when multiple tables are in scope.
- **FR-010**: The Column Picker MUST be closable via `Esc` without inserting.
- **FR-011**: System MUST expand `*` or `alias.*` to the explicit column list when the user presses `Tab` with the caret immediately after the asterisk, respecting the active format style and bracketing reserved keyword column names.

**Code Analysis Issues window + lightbulb quick-fixes (US3)**

- **FR-012**: System MUST provide a dockable Issues tool window listing every analysis issue in the current script with columns for rule id, severity, description, line, column; supporting sort, group by rule/severity, CSV export, click-to-navigate; refreshing within 1 second of the user pausing typing; and persisting its docked position across SSMS restarts.
- **FR-013**: For each analysis violation, the system MUST render a gutter lightbulb: orange for auto-fixable rules, blue for advisory-only rules.
- **FR-014**: Holding `Ctrl` over a squiggle MUST show an Issue Details popup with rule id, severity, problem text, remediation paragraph, and (for auto-fixable rules) an **Apply Fix** button.
- **FR-015**: Clicking **Apply Fix** MUST replace the offending text and clear the squiggle within 1 second; auto-fixes that require schema metadata not yet loaded (Phase B in progress) MUST be queued and a status-bar message MUST indicate "waiting for schema".
- **FR-016**: The Issue Details popup MUST include a **Disable this rule** option offering both inline (`-- akml-disable RuleId`) and project-level (`.casettings`) targets.

**Right-click tab color (US4)**

- **FR-017**: Right-click on a query tab MUST provide **Tab Color (Server)**, **Tab Color (Database)**, and (when applicable) **Tab Color (Server Group)** submenus listing every defined environment.
- **FR-018**: Picking an environment from a submenu MUST persist the assignment and repaint all tabs bound to the affected scope within 1 second, without restart.
- **FR-019**: When the host is running under Windows High Contrast, environment colors MUST be clamped so foreground text meets WCAG AA contrast (4.5:1 body text, 3:1 large text and UI components) against the tab background.

**Installer branding (US5)**

- **FR-020**: The installer EXE MUST carry the AKML SQL application icon visible in Windows Explorer and the taskbar during installation.
- **FR-021**: The installer wizard MUST display a branded AKML SQL banner image on all pages.

**Command Palette across four sources (US6)**

- **FR-022**: System MUST provide a Command Palette reachable via `Alt+S` (SSMS) and `Alt+P` (Visual Studio).
- **FR-023**: The palette MUST aggregate four result sources: AKML SQL commands, AKML SQL Options settings, SSMS / VS built-in commands, and (SSMS only) database objects from the active connection, each tagged with a small category badge.
- **FR-024**: The palette MUST rank results by fuzzy match score (using the existing `FuzzyMatcher`) and MUST remember the 10 most recent selections per IDE host, surfacing them first when the search box is empty.
- **FR-025**: Selecting an Options result MUST open the Settings dialog scrolled to and highlighting the matching control.
- **FR-026**: Selecting a database-object result MUST navigate the user to that object in Object Explorer or open its definition in a new query window.

**Script navigation chords + Browse Open Tabs + F1 help (US7)**

- **FR-027**: System MUST provide **Summarize Script** (`Ctrl+B, Ctrl+S`) producing a hierarchical outline of every top-level statement with click-to-navigate behaviour.
- **FR-028**: System MUST provide **Script Object as ALTER** (`F12`) opening a new query window with the `ALTER` definition for the object under the caret on the active connection; schema-bound objects retain `WITH SCHEMABINDING`.
- **FR-029**: System MUST provide **Select in Object Explorer** (`Ctrl+F12`) expanding the Object Explorer tree to and selecting the node for the object under the caret.
- **FR-030**: System MUST provide **Find Unused Variables and Parameters** (`Ctrl+B, Ctrl+F`) listing every declared variable and procedure/function parameter never read, with line and column.
- **FR-031**: System MUST provide **Browse Open Tabs** (`Ctrl+Q`) listing every open query tab across all SSMS / VS windows for the active host, with fuzzy search and Enter-to-activate.
- **FR-032**: Every AKML SQL UI surface (Options pages, dialogs, tool windows, Settings sub-tabs) MUST honor `F1` to open the matching documentation page.

**Find Invalid Objects (US8)**

- **FR-033**: System MUST provide a **Find Invalid Objects** action on the Object Explorer database right-click menu that scans every user object for broken references and lists them in a dockable tool window with columns for object name, schema, type, error message, and source line number, supporting multi-row selection.
- **FR-034**: The Invalid Objects window MUST provide **Script as ALTER** emitting the matching `ALTER` script for the selected rows (concatenated when multiple rows are selected) in a new query window.
- **FR-035**: Double-clicking an Invalid Objects row MUST jump Object Explorer to that node and surface the error message in the status bar.
- **FR-036**: For a database with up to 5,000 user objects, the scan MUST stream partial results into the window within 2 seconds and complete within 30 seconds on average hardware.

**Result-grid productivity audit (US9)**

- **FR-037**: System MUST provide **Copy as IN Clause**, **Script as INSERT**, and **Open in Excel** result-grid actions; each MUST operate on the selected rows when a selection exists, else on every visible row.
- **FR-038**: **Copy as IN Clause** MUST emit values comma-separated with proper string quoting and parenthesis wrapping; NULL values MUST be omitted and a status message MUST report the omission count.
- **FR-039**: **Script as INSERT** MUST emit a round-trippable `INSERT INTO <schema.table> (<cols>) VALUES (...), (...)` statement; for tables with an IDENTITY column the user MUST be offered an opt-in to wrap with `SET IDENTITY_INSERT ON/OFF`.
- **FR-040**: **Open in Excel** MUST preserve full numeric precision beyond Excel's default 15-digit truncation by formatting wide-precision cells as text.

**Refactoring chord family, Smart Rename, execution shortcuts (US10)**

- **FR-041**: System MUST add `Ctrl+B, Ctrl+B` (Brackets toggle), `Ctrl+B, Ctrl+I` (Inline Stored Procedure), `Ctrl+B, Ctrl+E` (Encapsulate as Stored Procedure) to the existing `Ctrl+B, Ctrl+Y/U/C/W/Q` chord family.
- **FR-042**: System MUST provide a **Smart Rename** action (`F2` editor, Object Explorer right-click) renaming a database object/column/procedure/parameter across every dependent object in the active connection, with a preview dialog (Actions / Warnings / Dependencies tabs), transactional apply, and preservation of extended properties and object permissions.
- **FR-043**: Smart Rename MUST disable the **Apply** button when the preview detects an unresolved name collision.
- **FR-044**: System MUST bind `Alt+Shift+F5` to **Execute Current Batch** (run the batch between the surrounding `GO` markers).
- **FR-045**: System MUST bind `Ctrl+Shift+F5` to **Execute To Cursor** (run from start-of-batch up to the line above the cursor, exclusive).
- **FR-046**: Both new execution shortcuts MUST trigger the existing pre-execution safety check on the about-to-run text.

**Completion polish, Object Definition Box, dual-instance, format markers (US11)**

- **FR-047**: System MUST bind `Ctrl+Shift+P` to a session-level toggle suppressing / resuming the IntelliSense suggestion popup, with status-bar feedback. The toggle resets to "active" when SSMS is restarted.
- **FR-048**: System MUST allow the user to configure which keystrokes commit the highlighted suggestion (Space, Dot, Comma, Open Paren, Tab, Enter), with Tab+Enter as the default.
- **FR-049**: While the suggestion popup is open, `Ctrl+Up` and `Ctrl+Down` MUST cycle the category filter through Tables → Views → Columns → Functions → Procedures → Snippets → All, with a visible badge.
- **FR-050**: Object tooltips MUST surface the `MS_Description` extended property when present.
- **FR-051**: When the parameter signature popup is shown for a function call, the next-expected parameter MUST be visually emphasised (bold).
- **FR-052**: For encrypted procedures/functions, when the user has DAC permission, the Script tab in the object definition box MUST show the decrypted body with a clear "decrypted" badge; without DAC permission it MUST show the encrypted placeholder.
- **FR-053**: System MUST parse `CREATE TABLE #temp …` and `SELECT … INTO #temp …` statements within the active script and offer column completions for `#temp` references later in the same script scope.
- **FR-054**: The object definition side panel MUST be resizable by dragging, the size MUST persist across sessions, and when `Ctrl` is held both the completion popup and the definition panel MUST become semi-transparent.
- **FR-055**: Connection detection MUST use the specific text view's file path to locate the corresponding DTE document, MUST NOT fall back to `DTE.ActiveDocument` at text-view-creation time, and MUST invalidate the per-session database list cache on `ConnectionChanged`.
- **FR-056**: System MUST provide an editor action "Disable formatting for selected text" wrapping the selection in `-- akml-format off` / `-- akml-format on` marker comments; the formatter MUST skip content between markers, treat unmatched/nested markers gracefully, and MUST NOT parse markers inside string literals.

**WPF theme refresh continuation + Options Dialog Phase 3 (US12)**

- **FR-057**: Every AKML-owned WPF surface in the spec 016 surface inventory MUST consume theme tokens from `ThemeTokens` exclusively (no inline chrome hex), with the explicit exception of 8 WinForms dialogs (`AboutDialog`, `BulkAnalysisResultDialog`, `LogViewerDialog`, `RefactoringPreviewDialog`, `SessionRecoveryDialog`, `BulkFormatProgressDialog`, `TextToSqlInputDialog`, `CellEditDialog`) which remain on pre-refresh chrome until a follow-up spec ports them to WPF.
- **FR-058**: The `SettingsWindow` MUST be migrated to `ThemeTokens` and `ThemeAwareWindow` and the Options page MUST visually match the in-repo SQL Prompt Options reference (`doc/SQL-PROMPT/SQL-Prompt-Option/SQL_Prompt_Options_Dialog.md` + `13_options_dialog.svg`) re-skinned in AKML tokens.
- **FR-059**: The Format › Styles Options page MUST be slim (Active Style dropdown + Edit button); clicking Edit MUST open a 3-column Style Editor (Style List | Categories tree | Options + Preview) with built-in styles visually marked read-only and toolbar buttons for Create / Copy / Rename / Delete / Import / Export.
- **FR-060**: Three new built-in styles MUST ship in `src/AkmlSql.Engine/Formatting/Profiles/`: `aligned.akmlstyle`, `verbose.akmlstyle`, `redgate-compatible.akmlstyle`.
- **FR-061**: The Redgate `.sqlpromptstylev2` importer MUST produce a post-import dialog reporting `translatedCount`, `unsupportedCount`, and listing any unsupported options.
- **FR-062**: An Environment Color Editor sub-dialog MUST be reachable from the Tabs › Color page, supporting add/edit/remove environments with Label/Pattern/Color fields and live color preview.

**AI shortcuts + feature reach (US13)**

- **FR-063**: System MUST bind `Alt+Z` to open the AI chat panel, `Shift+Alt+R` to AI Fix on the selection, `Ctrl+Alt+Z` to AI Optimize on the selection, and `Ctrl+Alt+Up Arrow` to manual AI ghost-text trigger.
- **FR-064**: When AI is disabled in Settings, AI shortcuts MUST show a brief status-bar message and take no other action.
- **FR-065**: System MUST provide an **Explain SQL** action (right-click selection, AKML SQL menu, Command Palette) returning a plain-language explanation of the selected SQL in the AI panel within 10 seconds for selections of ≤ 500 lines.
- **FR-066**: System MUST provide a **Query Index Analysis** action returning ML-based existing-vs-hinted plan summaries plus a copyable `CREATE INDEX` script in under 30 seconds for 95% of `SELECT … WHERE`/`JOIN` statements against tables with up to 1M rows.
- **FR-067**: After a SQL execution failure, the system MUST surface a non-blocking toast offering "Fix with AI" that, when clicked, pre-fills the AI panel with the failing batch and the SQL Server error message.
- **FR-068**: System MUST provide **comment-to-SQL**: when the user types `-- generate: <natural language>` on a blank line and presses Tab, the natural-language line MUST be replaced by AI-generated SQL with the original comment retained above.
- **FR-069**: The AI panel MUST include a History tab listing previous prompts and answers in reverse chronological order with a "revert to this state" action per entry.
- **FR-070**: When a SQL block is selected in the editor, the system MUST render a small AI icon at the right edge of the selection with hover actions: Explain / Fix / Optimize.
- **FR-071**: After every AI answer, the panel MUST render 1–3 clickable follow-up prompt buttons.
- **FR-072**: When AI is unavailable (offline, rate-limited, disabled), all AI features MUST surface a clear status message and leave no partial state in the panel.

**Code-audit TODO closure + refactoring debt (US14)**

- **FR-073**: The two skeleton MEF exports in `src/AkmlSql.Shell.Shared/Editor/SignatureHelpSource.cs` and `Editor/QuickInfoSource.cs` MUST be either wired via `PipeRpcClient` (using the same pattern as `CompletionController` for `MessageTypes.Completion*`) OR deleted entirely (along with their MEF exports).
- **FR-074**: The three Format-on-Save/Paste/Delimiter handlers (`Formatting/FormatOnSaveHandler.cs`, `FormatOnPasteHandler.cs`, `FormatOnDelimiterHandler.cs`) MUST be either wired via a shared `FormatRequestDispatcher` OR deleted.
- **FR-075**: `Productivity/CrudGenerationCommand.cs` MUST surface a proper schema / table / operation dialog instead of the current word-at-caret heuristic.
- **FR-076**: The two SSMS connection-context TODOs (`Tabs/TabTooltipProvider.cs`, `Tabs/TabColoringManager.cs`) MUST be collapsed into one shared `SsmsConnectionContextResolver` consumed by both call sites.
- **FR-077**: `Productivity/Grid/GridAccessHelper.cs` MUST handle SSMS 20's different results-pane class via a version-specific fallback path.
- **FR-078**: The two snippet DTO placeholder fields (`SnippetRequestHandler.cs:66 WasFormatted = false`, `:95 UsageCount = 0`) MUST be either wired to real values OR removed from the wire format.
- **FR-079**: The installer T096 task (`AkmlSqlSetup.iss:42`) MUST ship: on uninstall, native SSMS IntelliSense is restored if AKML SQL disabled it.
- **FR-080**: `src/AkmlSql.Engine/Server/PipeRpcServer.cs` MUST replace its 55-case switch dispatch with a `Dictionary<int, IMessageHandler>` lookup; the file SHOULD be under 300 lines after the refactor, and adding a new message type SHOULD require zero changes to the server class.
- **FR-081**: `src/AkmlSql.Core/Config/AppSettings.cs` (961 lines, 19 nested settings classes) MUST be split into per-domain files under `Config/`; the root `AppSettings.cs` SHOULD be under 200 lines after the split.

### Key Entities

- **Column Picker Selection**: The transient set of columns the user has checked in the column picker, in insertion order, plus a reference to the parent table and its alias so the insert can qualify correctly.
- **Analysis Issue Display Row**: A single row in the Issues tool window — rule id, severity, description, line, column, source span, and a reference to the underlying analysis finding so click-to-navigate can resolve the editor location.
- **Lightbulb Fix Descriptor**: A descriptor attached to an analysis rule containing the rule id, a "fixable" flag, the remediation text, and a fix-routine reference (the same routines the refactoring engine already exposes).
- **Tab Color Assignment**: A mapping from a scope (server name / database name / Registered Server Group id) to an Environment, with a priority so group inherits to members and individual server assignments override the group.
- **Command Palette Entry**: A single searchable item with a display label, a category (AKML Command / AKML Option / Host Command / Database Object), a fuzzy-match score, an invoke action, and an optional icon.
- **Script Outline Node**: A single entry in the Summarize Script tree with a statement type (CREATE/ALTER/SELECT/INSERT/UPDATE/DELETE/EXEC/USE/...), a display label, a parent node id, and an editor offset for click-to-navigate.
- **Invalid Object Record**: An object found by Find Invalid Objects with object name, schema, type, error message, source line number, and a reference to the dependent object that broke (for chained breakage).
- **Smart Rename Plan**: The bundle of (target identifier, new identifier, list of dependent objects to update, generated `sp_rename` + `ALTER` script, list of warnings, list of preserved permissions/extended properties).
- **Theme Token**: A named semantic role (e.g., `Akml.Brush.Surface.Canvas`, `Akml.Brush.Text.Primary`, `Akml.Brush.Border.Default`) that resolves to a frozen `SolidColorBrush` per the active theme variant.
- **AI Conversation Turn**: A single (prompt, answer) pair in the AI panel history with timestamp, source action (Explain / Fix / Optimize / Comment-to-SQL / Manual), token count, and optional follow-up suggestions.
- **Suggestion Toggle State**: Per-session boolean (suppressed / active) controlled by `Ctrl+Shift+P`. Resets to "active" when SSMS is restarted.
- **Custom Commit Key Set**: A user-configurable set of keystrokes that commit the highlighted suggestion. Default `{Tab, Enter}`. Editable via Options.
- **Temp Table Schema**: An ephemeral schema descriptor for a `#temp` / `##temp` table parsed from the active document with column name, type, and a scope (statement / batch / file).
- **Browse Open Tabs Entry**: A single entry in the `Ctrl+Q` popup with display label (filename + connection), host (SSMS / VS), tab index, and an activate action.
- **Formatting Disable Region**: A span of text marked by `-- akml-format off` and `-- akml-format on` comments that the formatter pipeline copies verbatim from input to output.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After Phase 10 M0 lands, a reader of `doc/progress.md` finds no contradiction with the current state of `master`: every "shipped" claim ties to a commit hash; every gap matches a real code absence verified by grep.
- **SC-002**: The reconciliation table in the Phase 10 PRD § 3 reaches 0 rows in "❌ Absent" and 0 rows in "⚠️ Partial" by the end of the implementation effort.
- **SC-003**: In timed usability tests, users complete a "select 5 specific columns from a wide table" task via Column Picker in under 15 seconds (vs. > 60 seconds via one-at-a-time completion today).
- **SC-004**: In usability tests, 80% of users invoke `*` + Tab wildcard expansion after reading the keyboard-shortcut tooltip without additional instruction.
- **SC-005**: 100% of users can locate and navigate to a failing analysis rule in under 10 seconds using the Issues window (vs. the existing squiggle-only workflow where the median is 30+ seconds).
- **SC-006**: The pre-execution safety check fires for 100% of unsafe statements (DELETE / UPDATE / MERGE without WHERE, in JOIN, in proc/trigger bodies) on `F5`, `Shift+F5`, `Alt+Shift+F5`, and `Ctrl+Shift+F5`, verified by a regression suite of 30 unsafe statements.
- **SC-007**: Tab coloring is assigned to every Production server within 2 minutes of first installation, measured by the percentage of Production-tagged tabs after a 1-day shakeout across the test group.
- **SC-008**: In timed tests, users complete a "find the formatting option named X" task via the Command Palette in under 5 seconds (vs. > 20 seconds via menu navigation).
- **SC-009**: `F12` Script-as-ALTER opens the correct `ALTER` script in under 2 seconds for objects on the active connection in 95% of test cases.
- **SC-010**: Find Invalid Objects' scan completes in under 30 seconds for a database with 5,000 user objects on average hardware and produces zero false positives across a corpus of 10 known-clean databases.
- **SC-011**: Smart Rename applied to a column referenced by 20 dependent objects leaves all 20 dependents parseable and executable in 100% of test runs (zero broken dependents).
- **SC-012**: Code Analysis lightbulb Apply Fix reduces the median time-to-fix for a `BP002` (deprecated `!=`) violation from 15+ seconds (manual edit) to under 2 seconds (one click).
- **SC-013**: AI Explain SQL returns a plain-language explanation in under 10 seconds for 95% of selections of ≤ 500 lines.
- **SC-014**: AI Query Index Analysis returns a recommendation in under 30 seconds for 95% of `SELECT … WHERE`/`JOIN` statements against tables with up to 1M rows.
- **SC-015**: A static-audit grep against `src/AkmlSql.Shell.Shared/**/*.cs` (excluding `Ui/Theme/` and the 8 WinForms surfaces explicitly listed in FR-057) for `Color\.From(Rgb|Argb)`, `Brushes\.[A-Z]\w+`, and `#[0-9A-Fa-f]{6}` literals returns zero hits.
- **SC-016**: Existing test suites stay green at every milestone: Engine ≥ 867, Core ≥ 526 (post-spec-015 baseline), Formatting ≥ 458, E2E baseline.
- **SC-017**: Every user story in this spec has at least one xUnit test in `tests/AkmlSql.Engine.Tests/` or `tests/AkmlSql.Core.Tests/` per the project's implementation-first-with-test-backfill convention.
- **SC-018**: A new contributor, given only the spec 016 design-system reference doc and `ThemeTokens.cs`, can add a new theme-aware dialog without asking how to handle theme tokens, brush freezing, or live-switch behavior.
- **SC-019**: F1 contextual help opens the matching documentation page in 100% of AKML SQL UI surfaces (Options pages, dialogs, tool windows), verified against `F1HelpListener.Count`.
- **SC-020**: The number of TODO markers in `src/AkmlSql.Shell.Shared/**/*.cs` and `src/AkmlSql.Engine/**/*.cs` (excluding the three intentional `-- TODO: Replace [TableName]` strings inside `GridScriptGenerator` generated SQL output) drops from 14 to 0 by the end of implementation.
- **SC-021**: After the refactor, `PipeRpcServer.cs` is under 300 lines and `AppSettings.cs` is under 200 lines; adding a new MessageType requires zero changes to `PipeRpcServer.cs`.

## Assumptions

- **A1**: The existing IPC layer (MessageType integers, DTOs, dispatch cases) is reused as-is. The only new MessageType integers in flight are `90/190 FindInvalidObjects`, `91/191 FindUnusedVariables`, `92/192 EncryptedObjectDecryption` reserved by spec 014 Phase 2. No additional MessageType ints are reserved by this spec.
- **A2**: The existing `WildcardExpansionHandler` in the Engine handles the `*` → column-list logic for User Story 2; this spec only covers the `Ctrl+Left Arrow` Column Picker UI and the Tab-key wiring for inline expansion.
- **A3**: The existing analysis engine and refactoring engine produce the issue data and auto-fix routines consumed by User Story 3; this spec only covers the new tool window and the lightbulb popup.
- **A4**: The existing `EnvironmentDetector` and `TabColoringManager` (shipped via spec 014 US5 commit `d7069d5`) handle the runtime tab-color resolution and live re-render; this spec only covers the right-click context menu and WCAG clamp.
- **A5**: The existing `CommandPaletteWindow` is extended (not rewritten) to cover the four-source aggregation in User Story 6.
- **A6**: The existing refactoring engine supports the chord-family actions in User Story 10; this spec only covers the VSCT keyboard bindings and the corresponding command classes.
- **A7**: The schema metadata service already exposes the metadata queries needed by Find Invalid Objects (broken-reference detection via `sys.sql_expression_dependencies` and `sys.sql_modules`).
- **A8**: The existing `AiRequestHandler` and the engine-side AI request types (`AiExplainRequest/Response`, `AiIndexAnalysisRequest/Response`, `AiTextToSqlRequest/Response`) cover the transport for User Story 13; this spec only covers the shell-side surfaces and keyboard wiring.
- **A9**: The existing `NoformatScanner` parses the `-- akml-format off` / `-- akml-format on` markers; this spec only covers the editor action that inserts the markers around a selection.
- **A10**: Spec 016 Phase 1+2 foundational infrastructure (`ThemeRegistry`, `ThemeTokens`, `HostThemeWatcher`, `ThemeAwareWindow`, `FocusVisualStyles`) is in place on master; this spec only covers the remaining surface migrations.
- **A11**: 8 WinForms dialogs (`AboutDialog`, `BulkAnalysisResultDialog`, `LogViewerDialog`, `RefactoringPreviewDialog`, `SessionRecoveryDialog`, `BulkFormatProgressDialog`, `TextToSqlInputDialog`, `CellEditDialog`) are explicitly out of scope for `ThemeTokens` migration; they remain on pre-refresh chrome.
- **A12**: Implementation-first-with-test-backfill is the project convention (per spec 014 tasks.md note about commits `2c34133` and `835d662`); test tasks accompany each implementation task in the same PR, not before.
- **A13**: Settings storage in `%AppData%\AKML SQL\config.json` is the single source of truth for feature toggles. API keys are stored DPAPI-encrypted with the `dpapi:` prefix (shipped by spec 015 US13); no new persistence layer is introduced.
- **A14**: The 5 milestone roadmap in the Phase 10 PRD § 7 is the target sequencing. Internal task ordering within a milestone may be adjusted as long as the milestone scope and target dates are met.

## Out of Scope

Explicitly NOT included in this specification (deferred to a future "Phase 11" effort):

- WinForms theme adapter / port to WPF for the 8 WinForms dialogs listed in A11. These surfaces remain on pre-refresh chrome.
- Redgate Platform integration (cloud sync of snippets / styles / analysis rules).
- Full Redgate `.sqlpromptoptionsettings` importer (current scope is `.sqlpromptstylev2` only).
- Multi-project `.akmlsettings` overrides for per-project style selection.
- Localization (Phase 10 keeps English-only).
- AI model self-hosting (Phase 10 calls external providers only).
- Azure Synapse / Microsoft Fabric / SQL 2025 preview dialect extensions.
- SQL History migration from older formats.
- Command Palette recent-items cross-machine sync.
- High Contrast as a first-class third theme (Phase 10 ships safe-fallback only).
- The 4 large-file class splits in `codebase-audit § 5.3..5.6` (`SettingsWindow`, `HistoryToolWindowControl`, `AiRequestHandler`, `CompletionController`) are *opportunistically* in scope where they collide with feature work but are not separately committed.

## Dependencies

- **SSMS 20 / 21 / 22** target IDE hosts. Features may degrade in SSMS 20 where custom menu bar APIs differ.
- **Visual Studio 2019 / 2022 / 2026** secondary IDE hosts.
- **The running Engine process** — all completion, formatting, analysis, AI dispatch and schema loading is out-of-process; any new feature that needs engine data MUST add an IPC message following the existing pattern (or reuse an existing MessageType).
- **The in-flight branch `018-options-dialog-phase2`** must merge cleanly into master (Milestone M0 — User Story 1). If merge conflicts arise from concurrent work, they MUST be resolved before further Phase 10 work begins.
- **Spec 016 foundational infrastructure** is on master and consumed unchanged by Phase 10 surface migrations.
- **Spec 015 DPAPI key storage** is on master and reused for AI provider key reads in User Story 13.
