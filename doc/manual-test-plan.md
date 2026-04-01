# AKML SQL — Manual Test Plan

> **Note:** AKML SQL is a SSMS/Visual Studio extension (WinForms + WPF UI, .NET Framework 4.7.2 shell).
> There are no Blazor components. This test plan covers all actual UI surfaces:
> installer, menus, dialogs, editor integrations, and commands.
>
> **Version Under Test:** 1.0.0
> **Test Environments:** SSMS 20, SSMS 21, SSMS 22, VS 2019, VS 2022, VS 2026
> **Tester:** _____________________ **Date:** _____________________

---

## Test Case Format

| Field | Description |
|---|---|
| **TC-ID** | Unique identifier (TC-{area}-{seq}) |
| **Description** | What is being tested |
| **Steps** | Numbered reproduction steps |
| **Test Data** | SQL, files, or settings used |
| **Expected Result** | What should happen |
| **Actual Result** | What actually happened (fill in during test) |
| **Pass/Fail** | P / F / N/A |

---

## Area 1 — Installer

---

### TC-INS-001

| Field | Value |
|---|---|
| **TC-ID** | TC-INS-001 |
| **Description** | Wizard install — SSMS 22 detected and selected by default |
| **Steps** | 1. Download `AKMLSQLSetup.exe` to a machine with SSMS 22 installed. 2. Double-click the installer. 3. Accept UAC. 4. Observe the Environment Scan screen. |
| **Test Data** | Machine with SSMS 22 at `C:\Program Files\Microsoft SQL Server Management Studio 22\` |
| **Expected Result** | SSMS 22 listed and pre-checked on the Environment Scan screen |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-INS-002

| Field | Value |
|---|---|
| **TC-ID** | TC-INS-002 |
| **Description** | Wizard install completes and AKML SQL menu appears in SSMS 22 |
| **Steps** | 1. Complete the wizard (accept defaults). 2. Launch SSMS 22. 3. Observe the menu bar. |
| **Test Data** | Default install options |
| **Expected Result** | "AKML SQL" menu appears under Tools menu. No startup errors in ActivityLog. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-INS-003

| Field | Value |
|---|---|
| **TC-ID** | TC-INS-003 |
| **Description** | Silent install via command line |
| **Steps** | 1. Open Command Prompt as Administrator. 2. Run: `AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /TARGETS=22`. 3. Wait for process to exit. 4. Launch SSMS 22. |
| **Test Data** | `AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /TARGETS=22` |
| **Expected Result** | No UI shown. Exit code 0. AKML SQL menu visible in SSMS 22 on next launch. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-INS-004

| Field | Value |
|---|---|
| **TC-ID** | TC-INS-004 |
| **Description** | config.json not overwritten on re-install |
| **Steps** | 1. Install AKML SQL. 2. Open `%AppData%\AKML SQL\config.json`, modify a setting, save. 3. Re-run installer. 4. Inspect config.json after install. |
| **Test Data** | Modify `"autoUpdate": false` in config.json before re-install |
| **Expected Result** | `config.json` retains the user-modified value; not overwritten. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-INS-005

| Field | Value |
|---|---|
| **TC-ID** | TC-INS-005 |
| **Description** | Uninstall removes extension from host |
| **Steps** | 1. Open Windows Settings → Apps → "AKML SQL" → Uninstall. 2. Confirm uninstall. 3. Launch SSMS 22. |
| **Test Data** | N/A |
| **Expected Result** | AKML SQL menu no longer present. SSMS starts without errors. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-INS-006

| Field | Value |
|---|---|
| **TC-ID** | TC-INS-006 |
| **Description** | Install with no supported host detected shows warning |
| **Steps** | 1. On a machine with no SSMS or VS installed, run `AKMLSQLSetup.exe`. 2. Observe the Environment Scan screen. |
| **Test Data** | Clean machine without SSMS/VS |
| **Expected Result** | Warning "No supported SQL host found." Install button disabled. |
| **Actual Result** | |
| **Pass/Fail** | |

---

## Area 2 — Extension Loading & Menu

---

### TC-EXT-001

| Field | Value |
|---|---|
| **TC-ID** | TC-EXT-001 |
| **Description** | AKML SQL menu visible in SSMS 22 under Tools |
| **Steps** | 1. Launch SSMS 22 after install. 2. Click the Tools menu. |
| **Test Data** | N/A |
| **Expected Result** | "AKML SQL" sub-menu visible under Tools containing: Format Document, Format Selection, Run Code Analysis, Snippet Manager, Refresh Schema, Edit Profile, Options, Check for Updates, View Logs, Send Feedback, About. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-EXT-002

| Field | Value |
|---|---|
| **TC-ID** | TC-EXT-002 |
| **Description** | Extension loads without errors in SSMS 20 (x86, .NET 4.7.2) |
| **Steps** | 1. Launch SSMS 20. 2. Open `%AppData%\Microsoft\SQL Server Management Studio\20.0_IsoShell\ActivityLog.xml`. 3. Search for "AkmlSql" in the log. |
| **Test Data** | N/A |
| **Expected Result** | ActivityLog shows successful package load. No error or exception entries for AkmlSql. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-EXT-003

| Field | Value |
|---|---|
| **TC-ID** | TC-EXT-003 |
| **Description** | Format Document command disabled when no editor is open |
| **Steps** | 1. Launch SSMS 22 with no query window open. 2. Click Tools → AKML SQL → Format Document. |
| **Test Data** | N/A |
| **Expected Result** | "Format Document" menu item is greyed out (disabled). |
| **Actual Result** | |
| **Pass/Fail** | |

---

## Area 3 — About Dialog

---

### TC-ABT-001

| Field | Value |
|---|---|
| **TC-ID** | TC-ABT-001 |
| **Description** | About dialog opens and displays correct version |
| **Steps** | 1. Click Tools → AKML SQL → About. |
| **Test Data** | N/A |
| **Expected Result** | Dialog titled "About AKML SQL" opens. Shows: Product Name "AKML SQL", Version "1.0.0", Build Date, .NET runtime description, OS description. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-ABT-002

| Field | Value |
|---|---|
| **TC-ID** | TC-ABT-002 |
| **Description** | About dialog closes with OK button |
| **Steps** | 1. Open About dialog. 2. Click OK. |
| **Test Data** | N/A |
| **Expected Result** | Dialog closes. Host is responsive. |
| **Actual Result** | |
| **Pass/Fail** | |

---

## Area 4 — Check for Updates

---

### TC-UPD-001

| Field | Value |
|---|---|
| **TC-ID** | TC-UPD-001 |
| **Description** | "Up to date" message shown when no update available |
| **Steps** | 1. Delete `%AppData%\AKML SQL\update-available.json` if it exists. 2. Click Tools → AKML SQL → Check for Updates. 3. Wait for result. |
| **Test Data** | No pre-existing update result file |
| **Expected Result** | MessageBox shows "AKML SQL v1.0.0 is up to date." with OK button. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-UPD-002

| Field | Value |
|---|---|
| **TC-ID** | TC-UPD-002 |
| **Description** | Update available prompt opens browser on Yes |
| **Steps** | 1. Manually create `%AppData%\AKML SQL\update-available.json` with `{"available":true,"version":"1.1.0","downloadUrl":"https://example.com/AKMLSQLSetup.exe"}`. 2. Click Tools → AKML SQL → Check for Updates. 3. Click Yes in the dialog. |
| **Test Data** | `{"available":true,"version":"1.1.0","downloadUrl":"https://example.com/AKMLSQLSetup.exe"}` |
| **Expected Result** | MessageBox "A new version (1.1.0) is available. Would you like to download it?" appears. Clicking Yes opens the browser to the download URL. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-UPD-003

| Field | Value |
|---|---|
| **TC-ID** | TC-UPD-003 |
| **Description** | Update URL with http:// (non-https) is rejected |
| **Steps** | 1. Create update-available.json with `"downloadUrl":"http://evil.com/malware.exe"`. 2. Run Check for Updates. 3. Click Yes (if dialog appears). |
| **Test Data** | `{"available":true,"version":"9.9.9","downloadUrl":"http://evil.com/malware.exe"}` |
| **Expected Result** | Browser not opened. "AKML SQL v1.0.0 is up to date." shown (URL validation fails, treated as not-available). |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-UPD-004

| Field | Value |
|---|---|
| **TC-ID** | TC-UPD-004 |
| **Description** | Error message shown when updater cannot be found |
| **Steps** | 1. Rename or delete `AkmlSql.Updater.exe` from the install directory. 2. Click Tools → AKML SQL → Check for Updates. |
| **Test Data** | Updater binary removed |
| **Expected Result** | MessageBox: "Unable to launch update checker. The updater was not found." |
| **Actual Result** | |
| **Pass/Fail** | |

---

## Area 5 — View Logs (Log Viewer Dialog)

---

### TC-LOG-001

| Field | Value |
|---|---|
| **TC-ID** | TC-LOG-001 |
| **Description** | Log Viewer dialog opens and loads latest log file |
| **Steps** | 1. Click Tools → AKML SQL → View Logs. |
| **Test Data** | AKML SQL must have been used (log entries exist in `%AppData%\AKML SQL\logs\`) |
| **Expected Result** | LogViewerDialog opens. Dropdown shows available log files. Grid populated with timestamped entries. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-LOG-002

| Field | Value |
|---|---|
| **TC-ID** | TC-LOG-002 |
| **Description** | Log level filter shows only Error entries |
| **Steps** | 1. Open Log Viewer. 2. In the Level dropdown, select "ERR". 3. Observe the grid. |
| **Test Data** | Log file with mixed-level entries |
| **Expected Result** | Grid shows only rows with Level = "ERR". INF/DBG/WRN rows hidden. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-LOG-003

| Field | Value |
|---|---|
| **TC-ID** | TC-LOG-003 |
| **Description** | Text search filters log entries |
| **Steps** | 1. Open Log Viewer. 2. Type "Format" in the search box. |
| **Test Data** | Log containing entries with "Format" keyword |
| **Expected Result** | Grid filtered to rows containing "Format" (case-insensitive). |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-LOG-004

| Field | Value |
|---|---|
| **TC-ID** | TC-LOG-004 |
| **Description** | Selecting a row shows full entry in detail pane |
| **Steps** | 1. Open Log Viewer. 2. Click any row. |
| **Test Data** | Log entry with stack trace |
| **Expected Result** | Full raw text (including exception/stack trace if present) shown in the RichTextBox detail panel. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-LOG-005

| Field | Value |
|---|---|
| **TC-ID** | TC-LOG-005 |
| **Description** | Copy button copies full entry to clipboard |
| **Steps** | 1. Open Log Viewer. 2. Select a row. 3. Click Copy button. 4. Paste into Notepad. |
| **Test Data** | Any log entry |
| **Expected Result** | Full log entry text (timestamp + level + message + stack if any) pasted in Notepad. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-LOG-006

| Field | Value |
|---|---|
| **TC-ID** | TC-LOG-006 |
| **Description** | Pagination works for large log files (> 200 entries) |
| **Steps** | 1. Open Log Viewer with a log file containing > 200 entries. 2. Click the Next button. |
| **Test Data** | Log file with 500+ entries |
| **Expected Result** | Page 2 of entries loads. Page indicator updates. Previous button enabled. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-LOG-007

| Field | Value |
|---|---|
| **TC-ID** | TC-LOG-007 |
| **Description** | Log Viewer opens even when log directory is empty |
| **Steps** | 1. Delete all files in `%AppData%\AKML SQL\logs\`. 2. Click Tools → AKML SQL → View Logs. |
| **Test Data** | Empty logs directory |
| **Expected Result** | Dialog opens without error. Empty grid shown. No exception thrown. |
| **Actual Result** | |
| **Pass/Fail** | |

---

## Area 6 — Send Feedback

---

### TC-FBK-001

| Field | Value |
|---|---|
| **TC-ID** | TC-FBK-001 |
| **Description** | Send Feedback opens GitHub issues page in browser |
| **Steps** | 1. Click Tools → AKML SQL → Send Feedback. |
| **Test Data** | N/A |
| **Expected Result** | Default browser opens to `https://github.com/AkmlSql/feedback`. No dialog or error shown. |
| **Actual Result** | |
| **Pass/Fail** | |

---

## Area 7 — Settings Dialog (Options)

---

### TC-SET-001

| Field | Value |
|---|---|
| **TC-ID** | TC-SET-001 |
| **Description** | Settings dialog opens with 7 tabs |
| **Steps** | 1. Click Tools → AKML SQL → Options. |
| **Test Data** | N/A |
| **Expected Result** | Dialog titled "AKML SQL Options" opens with tabs: General, IntelliSense, Cache, Formatter, Snippets, Code Analysis, Refactoring. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-SET-002

| Field | Value |
|---|---|
| **TC-ID** | TC-SET-002 |
| **Description** | Current settings load correctly into controls |
| **Steps** | 1. Open Options. 2. On General tab, note Auto Update checkbox state. 3. Cancel. 4. Open Options again. |
| **Test Data** | Existing config.json |
| **Expected Result** | Controls reflect values from `config.json`. State consistent across open/close cycles. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-SET-003

| Field | Value |
|---|---|
| **TC-ID** | TC-SET-003 |
| **Description** | OK saves settings to config.json atomically |
| **Steps** | 1. Open Options → IntelliSense tab. 2. Change Trigger Delay to 300ms. 3. Click OK. 4. Open `%AppData%\AKML SQL\config.json`. |
| **Test Data** | Set `nudTriggerDelay = 300` |
| **Expected Result** | config.json updated with new trigger delay value. No partial-write corruption (file is valid JSON). |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-SET-004

| Field | Value |
|---|---|
| **TC-ID** | TC-SET-004 |
| **Description** | Cancel discards changes |
| **Steps** | 1. Open Options. 2. Change Auto Update checkbox. 3. Click Cancel. 4. Open Options again. |
| **Test Data** | N/A |
| **Expected Result** | The checkbox shows its original value. config.json unchanged. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-SET-005

| Field | Value |
|---|---|
| **TC-ID** | TC-SET-005 |
| **Description** | IntelliSense tab — Disable Native IntelliSense checkbox |
| **Steps** | 1. Open Options → IntelliSense tab. 2. Check "Disable native IntelliSense". 3. Click OK. 4. Re-open Options. |
| **Test Data** | N/A |
| **Expected Result** | Checkbox remains checked after round-trip. Setting persisted in config.json. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-SET-006

| Field | Value |
|---|---|
| **TC-ID** | TC-SET-006 |
| **Description** | Code Analysis tab — Rules grid renders with rule IDs |
| **Steps** | 1. Open Options → Code Analysis tab. 2. Observe the rules DataGridView. |
| **Test Data** | N/A |
| **Expected Result** | Grid shows rows with Rule ID, Description, and Severity columns. At minimum PE003, SE001, SE002, BP004 visible. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-SET-007

| Field | Value |
|---|---|
| **TC-ID** | TC-SET-007 |
| **Description** | Formatter tab — Format on Save checkbox persists |
| **Steps** | 1. Open Options → Formatter tab. 2. Check "Format on Save". 3. Click OK. 4. Open Options. |
| **Test Data** | N/A |
| **Expected Result** | "Format on Save" still checked. Config updated. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-SET-008

| Field | Value |
|---|---|
| **TC-ID** | TC-SET-008 |
| **Description** | Refactoring tab — Rename scope combobox populates |
| **Steps** | 1. Open Options → Refactoring tab. 2. Click the Rename Scope ComboBox. |
| **Test Data** | N/A |
| **Expected Result** | Dropdown shows scope options (e.g., Current Script, All Open Files, Project). |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-SET-009

| Field | Value |
|---|---|
| **TC-ID** | TC-SET-009 |
| **Description** | Settings dialog is resizable and respects MinimumSize |
| **Steps** | 1. Open Options. 2. Try to resize below minimum (540×500). |
| **Test Data** | N/A |
| **Expected Result** | Dialog cannot be resized below minimum size (540×500). Content remains accessible. |
| **Actual Result** | |
| **Pass/Fail** | |

---

## Area 8 — Profile Editor Dialog

---

### TC-PRF-001

| Field | Value |
|---|---|
| **TC-ID** | TC-PRF-001 |
| **Description** | Profile Editor opens via menu command |
| **Steps** | 1. Click Tools → AKML SQL → Edit Profile. |
| **Test Data** | N/A |
| **Expected Result** | "AKML SQL - Profile Editor" WPF dialog opens at 1100×750. Left pane shows category TreeView; right pane shows before/after SQL preview. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-PRF-002

| Field | Value |
|---|---|
| **TC-ID** | TC-PRF-002 |
| **Description** | Selecting a category in the tree loads its options |
| **Steps** | 1. Open Profile Editor. 2. Click "Casing" in the left category TreeView. |
| **Test Data** | N/A |
| **Expected Result** | Options panel populates with casing-specific controls (Reserved Keywords dropdown, Built-in Functions dropdown, etc.). |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-PRF-003

| Field | Value |
|---|---|
| **TC-ID** | TC-PRF-003 |
| **Description** | Live preview updates when an option changes |
| **Steps** | 1. Open Profile Editor. 2. Select Casing category. 3. Change Reserved Keywords to "lowercase". 4. Observe the After preview pane. |
| **Test Data** | N/A |
| **Expected Result** | After preview immediately shows SQL with lowercase keywords (e.g., `select`, `from`). |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-PRF-004

| Field | Value |
|---|---|
| **TC-ID** | TC-PRF-004 |
| **Description** | Search box filters options within a category |
| **Steps** | 1. Open Profile Editor. 2. Type "indent" in the search box. |
| **Test Data** | Search query: "indent" |
| **Expected Result** | Options panel filtered to only show indentation-related controls. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-PRF-005

| Field | Value |
|---|---|
| **TC-ID** | TC-PRF-005 |
| **Description** | Save & Apply writes profile and formats active document |
| **Steps** | 1. Open a query window with lowercase SQL. 2. Open Profile Editor. 3. Ensure Keywords = UPPERCASE. 4. Click "Save & Apply". |
| **Test Data** | Active editor with: `select id from dbo.orders where id = 1` |
| **Expected Result** | Profile saved. Active document formatted immediately with UPPERCASE keywords. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-PRF-006

| Field | Value |
|---|---|
| **TC-ID** | TC-PRF-006 |
| **Description** | Reset Category button restores category defaults |
| **Steps** | 1. Open Profile Editor → Casing. 2. Change all values to "lowercase". 3. Click "Reset Category". |
| **Test Data** | N/A |
| **Expected Result** | Casing options revert to factory defaults (UPPERCASE). Preview updates. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-PRF-007

| Field | Value |
|---|---|
| **TC-ID** | TC-PRF-007 |
| **Description** | Cancel discards changes to profile |
| **Steps** | 1. Open Profile Editor. 2. Change Indent Size to 8. 3. Click Cancel. 4. Reopen Profile Editor. |
| **Test Data** | N/A |
| **Expected Result** | Indent Size reverts to previous value (e.g., 4). No profile file change on disk. |
| **Actual Result** | |
| **Pass/Fail** | |

---

## Area 9 — Format Document Command

---

### TC-FMT-001

| Field | Value |
|---|---|
| **TC-ID** | TC-FMT-001 |
| **Description** | Format Document uppercases keywords |
| **Steps** | 1. Open a new query window. 2. Type SQL. 3. Click Tools → AKML SQL → Format Document. |
| **Test Data** | `select id, name from dbo.orders where id = 1` |
| **Expected Result** | Document changes to `SELECT Id, Name FROM dbo.Orders WHERE Id = 1` (keywords uppercased per default profile). |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-FMT-002

| Field | Value |
|---|---|
| **TC-ID** | TC-FMT-002 |
| **Description** | Format Document is idempotent (second format produces same result) |
| **Steps** | 1. Open query window with SQL. 2. Format Document. 3. Format Document again. 4. Compare results. |
| **Test Data** | `SELECT Id, Name FROM dbo.Orders WHERE Id > 0 ORDER BY Id` |
| **Expected Result** | Second format produces identical text to first format. No additional changes. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-FMT-003

| Field | Value |
|---|---|
| **TC-ID** | TC-FMT-003 |
| **Description** | Format Document — noformat region preserved |
| **Steps** | 1. Create query with noformat markers. 2. Format Document. |
| **Test Data** | `SELECT 1\n-- noformat\nSELECT   weird   spacing\n-- endnoformat\nSELECT 2` |
| **Expected Result** | `SELECT 1` and `SELECT 2` formatted. Content between markers (`SELECT   weird   spacing`) unchanged. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-FMT-004

| Field | Value |
|---|---|
| **TC-ID** | TC-FMT-004 |
| **Description** | Format Document — empty document does not throw |
| **Steps** | 1. Open query window. 2. Clear all content. 3. Run Format Document. |
| **Test Data** | Empty document |
| **Expected Result** | No error. No change to document. Status bar shows no error. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-FMT-005

| Field | Value |
|---|---|
| **TC-ID** | TC-FMT-005 |
| **Description** | Format on Save triggers automatically |
| **Steps** | 1. Options → Formatter → enable "Format on Save". 2. Open query window, type lowercase SQL. 3. Press Ctrl+S to save. |
| **Test Data** | `select * from dbo.t` saved to `test.sql` |
| **Expected Result** | Document formatted (keywords uppercased) before or after save. Saved file contains formatted SQL. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-FMT-006

| Field | Value |
|---|---|
| **TC-ID** | TC-FMT-006 |
| **Description** | Format Selection formats only selected text |
| **Steps** | 1. Open query window with two SQL statements. 2. Select only the second statement. 3. Click Tools → AKML SQL → Format Selection. |
| **Test Data** | `SELECT 1;\nselect 2 from dbo.t` — select only `select 2 from dbo.t` |
| **Expected Result** | Only the second statement is formatted (`SELECT 2 FROM dbo.T`). First statement unchanged. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-FMT-007

| Field | Value |
|---|---|
| **TC-ID** | TC-FMT-007 |
| **Description** | Format on Paste triggers when pasting SQL |
| **Steps** | 1. Options → Formatter → enable "Format on Paste". 2. Copy lowercase SQL to clipboard. 3. Paste into a new query window. |
| **Test Data** | Clipboard: `select id from dbo.orders where status = 'active'` |
| **Expected Result** | Pasted content is immediately formatted with uppercase keywords. |
| **Actual Result** | |
| **Pass/Fail** | |

---

## Area 10 — Bulk Format Wizard

---

### TC-BFT-001

| Field | Value |
|---|---|
| **TC-ID** | TC-BFT-001 |
| **Description** | Bulk Format Wizard opens and accepts file selection |
| **Steps** | 1. Click Tools → AKML SQL → Bulk Format Files. 2. Click "Add Files". 3. Select 2 SQL files. |
| **Test Data** | Two SQL files: `q1.sql` (`select 1 from dbo.t`) and `q2.sql` (`select 2 from dbo.t`) |
| **Expected Result** | Both files appear in the file list. Start button is enabled. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-BFT-002

| Field | Value |
|---|---|
| **TC-ID** | TC-BFT-002 |
| **Description** | Bulk Format creates .bak files when enabled |
| **Steps** | 1. Open Bulk Format Wizard. 2. Add a SQL file. 3. Ensure "Create Backups" is checked. 4. Click Start. |
| **Test Data** | `test.sql` with `select 1` |
| **Expected Result** | After formatting, `test.sql.bak` file exists with original content. `test.sql` contains formatted SQL. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-BFT-003

| Field | Value |
|---|---|
| **TC-ID** | TC-BFT-003 |
| **Description** | Preview Only mode does not write files |
| **Steps** | 1. Open Bulk Format Wizard. 2. Add SQL files. 3. Check "Preview Only". 4. Click Start. |
| **Test Data** | `preview.sql` with `select 1 from dbo.t` |
| **Expected Result** | File content unchanged on disk. Wizard reports what would have changed. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-BFT-004

| Field | Value |
|---|---|
| **TC-ID** | TC-BFT-004 |
| **Description** | Add Folder recursively finds .sql files |
| **Steps** | 1. Open Bulk Format Wizard. 2. Click "Add Folder". 3. Select a folder with .sql files in subfolders. |
| **Test Data** | Folder with 3 SQL files: `root.sql`, `sub/a.sql`, `sub/b.sql` |
| **Expected Result** | All 3 files (including in subdirectory) appear in the file list. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-BFT-005

| Field | Value |
|---|---|
| **TC-ID** | TC-BFT-005 |
| **Description** | Cancel button closes wizard without starting |
| **Steps** | 1. Open Bulk Format Wizard. 2. Add files. 3. Click Cancel. |
| **Test Data** | N/A |
| **Expected Result** | Dialog closes. No files modified. |
| **Actual Result** | |
| **Pass/Fail** | |

---

## Area 11 — IntelliSense (Completion Popup)

---

### TC-INT-001

| Field | Value |
|---|---|
| **TC-ID** | TC-INT-001 |
| **Description** | Completion list appears after typing partial table name |
| **Steps** | 1. Open query window connected to a database. 2. Type `dbo.Or`. 3. Wait 150 ms or press Ctrl+Space. |
| **Test Data** | Database with a table `dbo.Orders` |
| **Expected Result** | Completion list appears showing `Orders` (and other dbo.Or* objects). |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-INT-002

| Field | Value |
|---|---|
| **TC-ID** | TC-INT-002 |
| **Description** | Tab accepts completion item |
| **Steps** | 1. Open completion list (see TC-INT-001). 2. Press Tab or Enter. |
| **Test Data** | `Orders` selected in completion list |
| **Expected Result** | `dbo.Orders` inserted at cursor. Completion list dismissed. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-INT-003

| Field | Value |
|---|---|
| **TC-ID** | TC-INT-003 |
| **Description** | Escape dismisses completion without insertion |
| **Steps** | 1. Open completion list. 2. Press Escape. |
| **Test Data** | N/A |
| **Expected Result** | Completion list closes. No text inserted. Cursor position unchanged. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-INT-004

| Field | Value |
|---|---|
| **TC-ID** | TC-INT-004 |
| **Description** | Column completion after alias dot |
| **Steps** | 1. Type `SELECT o. FROM dbo.Orders o`. 2. Place cursor after `o.`. 3. Wait for completion or press Ctrl+Space. |
| **Test Data** | Database with `dbo.Orders` table having columns `Id`, `Status`, `CustomerId` |
| **Expected Result** | Completion list shows column names from `dbo.Orders`: `Id`, `Status`, `CustomerId`, etc. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-INT-005

| Field | Value |
|---|---|
| **TC-ID** | TC-INT-005 |
| **Description** | Quick Info tooltip appears on hover over table name |
| **Steps** | 1. Type `SELECT * FROM dbo.Orders`. 2. Hover cursor over `Orders` for 500 ms. |
| **Test Data** | `dbo.Orders` in schema cache |
| **Expected Result** | Tooltip appears showing schema, row count, and column list summary. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-INT-006

| Field | Value |
|---|---|
| **TC-ID** | TC-INT-006 |
| **Description** | Signature help appears after opening parenthesis of a procedure |
| **Steps** | 1. Type `EXEC dbo.usp_GetOrder(`. 2. Observe tooltip. |
| **Test Data** | Procedure `dbo.usp_GetOrder` with parameter `@Id INT` in schema |
| **Expected Result** | Signature help tooltip shows `@Id INT` parameter with type. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-INT-007

| Field | Value |
|---|---|
| **TC-ID** | TC-INT-007 |
| **Description** | Refresh Schema sends request and updates status bar |
| **Steps** | 1. Click Tools → AKML SQL → Refresh Schema. 2. Observe status bar. |
| **Test Data** | Connected database |
| **Expected Result** | Status bar briefly shows "Refreshing schema…" then "Schema ready". Completion list updated. |
| **Actual Result** | |
| **Pass/Fail** | |

---

## Area 12 — Static Code Analysis

---

### TC-ANA-001

| Field | Value |
|---|---|
| **TC-ID** | TC-ANA-001 |
| **Description** | PE003 squiggle appears on DELETE without WHERE |
| **Steps** | 1. Open query window. 2. Type `DELETE FROM dbo.Orders`. 3. Wait for analysis. |
| **Test Data** | `DELETE FROM dbo.Orders` |
| **Expected Result** | Red squiggle under `DELETE FROM dbo.Orders`. Hover shows rule PE003 message. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-ANA-002

| Field | Value |
|---|---|
| **TC-ID** | TC-ANA-002 |
| **Description** | SE002 squiggle on hardcoded password variable |
| **Steps** | 1. Open query window. 2. Type `DECLARE @password VARCHAR(50) = 'secret123'`. 3. Wait for analysis. |
| **Test Data** | `DECLARE @password VARCHAR(50) = 'secret123'` |
| **Expected Result** | Red (Error severity) squiggle. SE002 rule ID in hover tooltip. Issue appears in Error List. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-ANA-003

| Field | Value |
|---|---|
| **TC-ID** | TC-ANA-003 |
| **Description** | BP004 squiggle on equality comparison with NULL |
| **Steps** | 1. Open query window. 2. Type `SELECT 1 WHERE Col = NULL`. 3. Wait. |
| **Test Data** | `SELECT 1 WHERE Col = NULL` |
| **Expected Result** | Warning squiggle with BP004 rule. "Use IS NULL instead of = NULL" message. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-ANA-004

| Field | Value |
|---|---|
| **TC-ID** | TC-ANA-004 |
| **Description** | Issues appear in VS Error List (Warning/Error only) |
| **Steps** | 1. Open Error List (View → Error List). 2. Type `DELETE FROM dbo.Orders` in query window. |
| **Test Data** | `DELETE FROM dbo.Orders` |
| **Expected Result** | PE003 appears as a Warning entry in the Error List with file path and line number. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-ANA-005

| Field | Value |
|---|---|
| **TC-ID** | TC-ANA-005 |
| **Description** | Inline suppression hides diagnostic |
| **Steps** | 1. Type `DELETE FROM dbo.Orders -- akml-disable-line PE003`. 2. Wait for analysis. |
| **Test Data** | `DELETE FROM dbo.Orders -- akml-disable-line PE003` |
| **Expected Result** | No squiggle on that line. PE003 not listed in Error List for this line. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-ANA-006

| Field | Value |
|---|---|
| **TC-ID** | TC-ANA-006 |
| **Description** | Analysis disabled globally shows no squiggles |
| **Steps** | 1. Options → Code Analysis → uncheck "Enabled". 2. Click OK. 3. Type `DELETE FROM dbo.Orders`. |
| **Test Data** | N/A |
| **Expected Result** | No squiggles. Error List empty for AKML SQL entries. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-ANA-007

| Field | Value |
|---|---|
| **TC-ID** | TC-ANA-007 |
| **Description** | Light bulb appears for fixable diagnostics |
| **Steps** | 1. Type `SELECT 1 WHERE Col = NULL`. 2. Click the yellow squiggle. 3. Look for light bulb icon. |
| **Test Data** | `SELECT 1 WHERE Col = NULL` triggering BP004 |
| **Expected Result** | Light bulb (or Ctrl+.) shows "Fix: use IS NULL" quick action. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-ANA-008

| Field | Value |
|---|---|
| **TC-ID** | TC-ANA-008 |
| **Description** | DEP001 fires on deprecated `text` data type |
| **Steps** | 1. Type `CREATE TABLE dbo.T (Notes text)`. |
| **Test Data** | `CREATE TABLE dbo.T (Notes text)` |
| **Expected Result** | DEP001 warning squiggle. Message recommends `NVARCHAR(MAX)`. |
| **Actual Result** | |
| **Pass/Fail** | |

---

## Area 13 — Bulk Analysis Command & Results Dialog

---

### TC-BAN-001

| Field | Value |
|---|---|
| **TC-ID** | TC-BAN-001 |
| **Description** | Bulk Analysis command opens folder picker |
| **Steps** | 1. Click Tools → AKML SQL → Run Code Analysis. |
| **Test Data** | N/A |
| **Expected Result** | FolderBrowserDialog opens with "Select a folder to analyze" description. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-BAN-002

| Field | Value |
|---|---|
| **TC-ID** | TC-BAN-002 |
| **Description** | No .sql files found shows informational message |
| **Steps** | 1. Click Run Code Analysis. 2. Select a folder with no .sql files. |
| **Test Data** | Empty folder or folder with only .txt files |
| **Expected Result** | MessageBox: "No .sql files found in the selected directory." |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-BAN-003

| Field | Value |
|---|---|
| **TC-ID** | TC-BAN-003 |
| **Description** | Results dialog shows summary strip with counts |
| **Steps** | 1. Run Code Analysis on a folder with known-bad SQL files. 2. Wait for dialog. |
| **Test Data** | Folder with: `bad.sql` containing `DELETE FROM dbo.T` and `DECLARE @password VARCHAR(50) = 'x'` |
| **Expected Result** | Results dialog opens. Summary strip shows counts: Errors: 1, Warnings: 1, Info: N. Total files analyzed shown. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-BAN-004

| Field | Value |
|---|---|
| **TC-ID** | TC-BAN-004 |
| **Description** | Results grid is sortable by column |
| **Steps** | 1. Open Results dialog with multiple issues. 2. Click the "Rule" column header. |
| **Test Data** | Multiple issues of different rule IDs |
| **Expected Result** | Grid rows re-sorted alphabetically by Rule ID. Clicking again reverses sort. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-BAN-005

| Field | Value |
|---|---|
| **TC-ID** | TC-BAN-005 |
| **Description** | Double-clicking a result row navigates to that file/line |
| **Steps** | 1. Open Results dialog. 2. Double-click a row for `bad.sql`, line 1. |
| **Test Data** | Analysis result pointing to a real file on disk |
| **Expected Result** | The file opens in the editor, cursor positioned at the reported line. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-BAN-006

| Field | Value |
|---|---|
| **TC-ID** | TC-BAN-006 |
| **Description** | Pagination works in Results dialog (> 200 issues) |
| **Steps** | 1. Run analysis on a folder with > 200 issues. 2. Click Next in the Results dialog. |
| **Test Data** | Folder with 50+ SQL files containing multiple issues each |
| **Expected Result** | Next page loads. Page label updates (e.g., "Page 2 of 3"). Previous button enabled. |
| **Actual Result** | |
| **Pass/Fail** | |

---

## Area 14 — Refactoring Commands

---

### TC-REF-001

| Field | Value |
|---|---|
| **TC-ID** | TC-REF-001 |
| **Description** | Expand Wildcards command replaces SELECT * |
| **Steps** | 1. Open query window connected to DB. 2. Type `SELECT * FROM dbo.Orders`. 3. Click Tools → AKML SQL → Refactor → Expand SELECT *. |
| **Test Data** | `SELECT * FROM dbo.Orders` with Orders table having columns Id, Status, CustomerId |
| **Expected Result** | `SELECT *` replaced with `SELECT o.Id, o.Status, o.CustomerId` (or similar). |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-REF-002

| Field | Value |
|---|---|
| **TC-ID** | TC-REF-002 |
| **Description** | Extract to CTE wraps selection in WITH clause |
| **Steps** | 1. Select a subquery in the editor. 2. Click Tools → AKML SQL → Refactor → Extract to CTE. |
| **Test Data** | Selected: `SELECT Id FROM dbo.Orders WHERE Status = 'Active'` |
| **Expected Result** | Selection replaced with `WITH cte AS (SELECT Id FROM dbo.Orders WHERE Status = 'Active') SELECT * FROM cte`. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-REF-003

| Field | Value |
|---|---|
| **TC-ID** | TC-REF-003 |
| **Description** | Qualify Names adds schema prefix to unqualified objects |
| **Steps** | 1. Type `SELECT Id FROM Orders`. 2. Click Tools → AKML SQL → Refactor → Qualify Names. |
| **Test Data** | `SELECT Id FROM Orders` where Orders exists in dbo schema |
| **Expected Result** | Document updated to `SELECT Id FROM dbo.Orders`. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-REF-004

| Field | Value |
|---|---|
| **TC-ID** | TC-REF-004 |
| **Description** | Convert Old-Style Joins transforms *= to INNER JOIN |
| **Steps** | 1. Type T-SQL with old-style join. 2. Click Tools → AKML SQL → Refactor → Convert Old-Style Joins. |
| **Test Data** | `SELECT o.Id, c.Name FROM dbo.Orders o, dbo.Customers c WHERE o.CustomerId = c.Id` |
| **Expected Result** | Converted to ANSI JOIN syntax: `SELECT o.Id, c.Name FROM dbo.Orders o INNER JOIN dbo.Customers c ON o.CustomerId = c.Id`. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-REF-005

| Field | Value |
|---|---|
| **TC-ID** | TC-REF-005 |
| **Description** | Replace Deprecated Syntax updates TEXT to NVARCHAR(MAX) |
| **Steps** | 1. Type `CREATE TABLE T (Notes text)`. 2. Click Refactor → Replace Deprecated Syntax. |
| **Test Data** | `CREATE TABLE T (Notes text)` |
| **Expected Result** | `text` replaced with `NVARCHAR(MAX)`. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-REF-006

| Field | Value |
|---|---|
| **TC-ID** | TC-REF-006 |
| **Description** | Toggle Brackets adds/removes square brackets around identifiers |
| **Steps** | 1. Select `Orders` in editor. 2. Click Refactor → Toggle Brackets. 3. Then again. |
| **Test Data** | `dbo.Orders` |
| **Expected Result** | First toggle: `dbo.[Orders]`. Second toggle: `dbo.Orders`. |
| **Actual Result** | |
| **Pass/Fail** | |

---

## Area 15 — Formatting Utility Commands

---

### TC-FU-001

| Field | Value |
|---|---|
| **TC-ID** | TC-FU-001 |
| **Description** | Insert Semicolons adds statement terminators |
| **Steps** | 1. Type multi-statement SQL without semicolons. 2. Click Tools → AKML SQL → Insert Semicolons. |
| **Test Data** | `SELECT 1\nSELECT 2\nSELECT 3` |
| **Expected Result** | Each statement ends with `;`: `SELECT 1;\nSELECT 2;\nSELECT 3;` |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-FU-002

| Field | Value |
|---|---|
| **TC-ID** | TC-FU-002 |
| **Description** | Remove Semicolons removes statement terminators |
| **Steps** | 1. Type SQL with semicolons. 2. Click Tools → AKML SQL → Remove Semicolons. |
| **Test Data** | `SELECT 1; SELECT 2; SELECT 3;` |
| **Expected Result** | Semicolons removed: `SELECT 1 SELECT 2 SELECT 3` |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-FU-003

| Field | Value |
|---|---|
| **TC-ID** | TC-FU-003 |
| **Description** | Casing Only applies keyword casing without layout changes |
| **Steps** | 1. Type mixed-case SQL. 2. Click Tools → AKML SQL → Apply Casing Only. |
| **Test Data** | `SeLeCt Id FrOm dbo.Orders WHERE Id = 1` |
| **Expected Result** | Keywords uppercased. Indentation/layout NOT changed. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-FU-004

| Field | Value |
|---|---|
| **TC-ID** | TC-FU-004 |
| **Description** | Toggle AS adds/removes AS keyword in aliases |
| **Steps** | 1. Type `SELECT Id OrderId FROM dbo.Orders`. 2. Click Toggle AS. 3. Then again. |
| **Test Data** | `SELECT Id OrderId FROM dbo.Orders` |
| **Expected Result** | Toggle on: `SELECT Id AS OrderId FROM dbo.Orders`. Toggle off: original. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-FU-005

| Field | Value |
|---|---|
| **TC-ID** | TC-FU-005 |
| **Description** | Encapsulate BEGIN/END wraps IF body in BEGIN/END block |
| **Steps** | 1. Type an IF without BEGIN/END. 2. Click Tools → AKML SQL → Encapsulate BEGIN/END. |
| **Test Data** | `IF @Id > 0\n    SELECT 1` |
| **Expected Result** | `IF @Id > 0\nBEGIN\n    SELECT 1\nEND` |
| **Actual Result** | |
| **Pass/Fail** | |

---

## Area 16 — Theme & DPI

---

### TC-UI-001

| Field | Value |
|---|---|
| **TC-ID** | TC-UI-001 |
| **Description** | Dialogs respect VS dark theme |
| **Steps** | 1. Switch Visual Studio to dark theme (Tools → Options → Environment → Color Theme → Dark). 2. Open AKML SQL Options dialog. |
| **Test Data** | Dark theme active |
| **Expected Result** | Dialog background is dark. Text is light. Consistent with host theme. No white flash. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-UI-002

| Field | Value |
|---|---|
| **TC-ID** | TC-UI-002 |
| **Description** | Completion popup renders correctly at 150% DPI |
| **Steps** | 1. Set Windows display scaling to 150%. 2. Launch SSMS/VS. 3. Trigger completion popup. |
| **Test Data** | Windows 150% scaling |
| **Expected Result** | Completion popup text readable, not blurry. No overflow or clipping. |
| **Actual Result** | |
| **Pass/Fail** | |

---

## Area 17 — Engine Process & IPC

---

### TC-IPC-001

| Field | Value |
|---|---|
| **TC-ID** | TC-IPC-001 |
| **Description** | Engine process starts on extension load |
| **Steps** | 1. Launch SSMS 22. 2. Open Task Manager → Details. |
| **Test Data** | N/A |
| **Expected Result** | `AkmlSql.Engine.exe` process visible in Task Manager after extension loads. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-IPC-002

| Field | Value |
|---|---|
| **TC-ID** | TC-IPC-002 |
| **Description** | Extension recovers when engine crashes |
| **Steps** | 1. Launch SSMS. 2. Kill `AkmlSql.Engine.exe` in Task Manager. 3. Trigger Format Document. |
| **Test Data** | Engine forcibly killed |
| **Expected Result** | Engine restarts automatically. Format Document executes successfully after brief delay. No unhandled exception in host. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-IPC-003

| Field | Value |
|---|---|
| **TC-ID** | TC-IPC-003 |
| **Description** | Engine exits when SSMS closes |
| **Steps** | 1. Launch SSMS. Wait for engine to start. 2. Close SSMS. 3. Check Task Manager. |
| **Test Data** | N/A |
| **Expected Result** | `AkmlSql.Engine.exe` process no longer visible after SSMS closes. No orphaned process. |
| **Actual Result** | |
| **Pass/Fail** | |

---

## Area 18 — Configuration Files

---

### TC-CFG-001

| Field | Value |
|---|---|
| **TC-ID** | TC-CFG-001 |
| **Description** | config.json created on first load |
| **Steps** | 1. Delete `%AppData%\AKML SQL\config.json`. 2. Launch SSMS with extension installed. |
| **Test Data** | No pre-existing config.json |
| **Expected Result** | `%AppData%\AKML SQL\config.json` created with default settings. Extension functions normally. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-CFG-002

| Field | Value |
|---|---|
| **TC-ID** | TC-CFG-002 |
| **Description** | Corrupted config.json is handled gracefully |
| **Steps** | 1. Open `%AppData%\AKML SQL\config.json`. 2. Replace content with `{invalid json`. 3. Launch SSMS. |
| **Test Data** | `{invalid json` in config.json |
| **Expected Result** | Extension loads with defaults. No unhandled exception. Error logged to AKML SQL log file. |
| **Actual Result** | |
| **Pass/Fail** | |

---

### TC-CFG-003

| Field | Value |
|---|---|
| **TC-ID** | TC-CFG-003 |
| **Description** | .casettings file per-project overrides global severity |
| **Steps** | 1. Create `.casettings` in a folder alongside .sql files with `{"rules":{"PE003":{"severity":"none"}}}`. 2. Open a .sql file in that folder. 3. Type `DELETE FROM dbo.T`. |
| **Test Data** | `.casettings`: `{"rules":{"PE003":{"severity":"none"}}}` |
| **Expected Result** | No PE003 squiggle. Rule silenced by project-level settings. |
| **Actual Result** | |
| **Pass/Fail** | |

---

## Regression & Cross-Host Matrix

> Execute TC-FMT-001, TC-ANA-001, TC-INT-001, TC-SET-001 on each supported host.

| Test Case | SSMS 20 | SSMS 21 | SSMS 22 | VS 2019 | VS 2022 | VS 2026 |
|---|---|---|---|---|---|---|
| TC-FMT-001 | P/F | P/F | P/F | P/F | P/F | P/F |
| TC-ANA-001 | P/F | P/F | P/F | P/F | P/F | P/F |
| TC-INT-001 | P/F | P/F | P/F | P/F | P/F | P/F |
| TC-SET-001 | P/F | P/F | P/F | P/F | P/F | P/F |
| TC-EXT-001 | P/F | P/F | P/F | P/F | P/F | P/F |
| TC-LOG-001 | P/F | P/F | P/F | P/F | P/F | P/F |

---

## Test Summary

| Area | Total | Pass | Fail | N/A |
|---|---|---|---|---|
| 1 — Installer | 6 | | | |
| 2 — Extension Loading | 3 | | | |
| 3 — About Dialog | 2 | | | |
| 4 — Check for Updates | 4 | | | |
| 5 — Log Viewer | 7 | | | |
| 6 — Send Feedback | 1 | | | |
| 7 — Settings Dialog | 9 | | | |
| 8 — Profile Editor | 7 | | | |
| 9 — Format Document | 7 | | | |
| 10 — Bulk Format Wizard | 5 | | | |
| 11 — IntelliSense | 7 | | | |
| 12 — Static Analysis | 8 | | | |
| 13 — Bulk Analysis | 6 | | | |
| 14 — Refactoring | 6 | | | |
| 15 — Formatting Utilities | 5 | | | |
| 16 — Theme & DPI | 2 | | | |
| 17 — Engine / IPC | 3 | | | |
| 18 — Configuration | 3 | | | |
| **Total** | **90** | | | |

---

*Generated: 2026-03-24 | AKML SQL v1.0.0 | 90 test cases across 18 areas*
