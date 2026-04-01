# AKML SQL — Structured Use Cases

> **Version:** 1.0 | **Date:** March 2026 | **Author:** Derived from Phase 1–9 PRDs
> **Coverage:** All features across Phase 1 (Foundation & Installer) through Phase 9 (AI Assistance)

---

## Notation

| Field | Description |
|---|---|
| **Use Case ID** | Unique identifier (`UC-P{phase}-{seq}`) |
| **Title** | Short imperative name |
| **Actor** | Who initiates or benefits (Developer, DBA, Enterprise Admin, CI/CD System) |
| **Preconditions** | What must be true before the use case begins |
| **Main Flow** | The happy-path numbered steps |
| **Alternative Flows** | Branch conditions, edge cases, and error paths |
| **Expected Result** | Observable outcome when the use case succeeds |

---

## Phase 1 — Foundation & Windows EXE Installer

### UC-P1-001: Install AKML SQL on a Developer Machine (Wizard)

| Field | Value |
|---|---|
| **Use Case ID** | UC-P1-001 |
| **Title** | Install AKML SQL via Next-Next wizard |
| **Actor** | Developer / DBA (local administrator) |
| **Preconditions** | (1) `AKMLSQLSetup.exe` is downloaded to the machine. (2) At least one supported SSMS or Visual Studio instance is installed. (3) No prior version of AKML SQL is installed. |

**Main Flow:**

1. User double-clicks `AKMLSQLSetup.exe`. Windows prompts UAC elevation; user accepts.
2. **Welcome screen** appears with AKML SQL logo and version. User clicks **Next**.
3. **EULA screen** displays the license agreement. User selects "I accept the agreement" and clicks **Next**.
4. **Environment Scan screen** shows auto-detected SSMS and Visual Studio installations in a tree view with checkboxes. All compatible targets are pre-checked. User reviews and clicks **Next**.
5. **Installation Directory screen** shows default path `C:\Program Files\AKML SQL\`. User accepts default and clicks **Next**.
6. **Additional Options screen** shows checkboxes for auto-update, telemetry, desktop shortcut, and Start Menu. User keeps defaults and clicks **Next**.
7. **Ready to Install screen** shows a summary of selected targets, directory, and options. User clicks **Install**.
8. **Installing screen** shows progress bar with per-step labels (copying binaries, installing per IDE, writing config, clearing MEF cache). Completes within 60 seconds.
9. **Finish screen** shows green checkmarks for each successfully installed target. "Launch SSMS 22 now" is checked by default. User clicks **Finish**.
10. SSMS 22 launches. An "AKML SQL" menu appears in the menu bar.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | User clicks **Back** on any screen | Wizard returns to the previous screen, all prior selections preserved |
| AF-2 | A target SSMS/VS instance is currently running | Yellow warning banner appears on step 4: "Close these apps before installing." Option "Close them for me" terminates processes automatically |
| AF-3 | VS instance present but SSDT workload missing | VS entry shown grayed-out with warning icon and tooltip; checkbox unchecked; installer proceeds without that target |
| AF-4 | No SSMS or VS instances found | Error panel appears on step 4 with download links to SSMS 22 and Visual Studio 2026 |
| AF-5 | UAC elevation denied | Installer exits with message "Administrator rights required" |

**Expected Result:** All selected SSMS and VS instances load AKML SQL on next startup. "AKML SQL" top-level menu is visible. About dialog shows correct version and IDE info. Extension adds less than 200ms to IDE startup time.

---

### UC-P1-002: Silently Deploy AKML SQL Across an Enterprise

| Field | Value |
|---|---|
| **Use Case ID** | UC-P1-002 |
| **Title** | Silent enterprise deployment via command line |
| **Actor** | Enterprise Administrator / IT deployment system |
| **Preconditions** | (1) `AKMLSQLSetup.exe` is available on a network share. (2) Target machines have SSMS 21 and SSMS 22 installed. (3) Admin has UAC elevation or runs as SYSTEM. |

**Main Flow:**

1. Admin executes: `AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /TARGETS="ssms21,ssms22" /FORCECLOSEAPPS /LOG="C:\Logs\akmlsql.log"`
2. Installer runs without any visible UI. Detects SSMS 21 and SSMS 22 paths from the registry.
3. If SSMS instances are running, `/FORCECLOSEAPPS` terminates them.
4. Extension DLLs are deployed to each IDE's Extensions folder.
5. MEF cache is cleared for both targets.
6. Default `config.json` is written to `%AppData%\AKML SQL\` (only if absent).
7. Installer exits with code 0. Log written to `C:\Logs\akmlsql.log`.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | A target IDE is not found | That target is skipped; installer continues; log records the skip |
| AF-2 | Installer exits non-zero | `%TEMP%\AKMLSQLSetup.log` contains details; target IDEs are unchanged |

**Expected Result:** AKML SQL is installed on all machines matching the target pattern. Log file confirms success. No windows, prompts, or UAC dialogs appear.

---

### UC-P1-003: Check for Updates Manually

| Field | Value |
|---|---|
| **Use Case ID** | UC-P1-003 |
| **Title** | Manually check for a newer AKML SQL version |
| **Actor** | Developer / DBA |
| **Preconditions** | (1) AKML SQL is installed and loaded in SSMS or VS. (2) Machine has internet access. |

**Main Flow:**

1. User opens SSMS. AKML SQL menu is visible.
2. User selects **AKML SQL → Check for Updates**.
3. The extension spawns `AkmlSql.Updater.exe` (non-blocking).
4. Updater queries the version manifest. A newer version is available.
5. Updater writes `update-available.json` to `%AppData%\AKML SQL\`.
6. A non-modal notification bar appears in SSMS: "AKML SQL v2.0 is available. [Download Update]."
7. User clicks **Download Update**. Default browser opens to the download page.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | Current version is the latest | Notification: "AKML SQL is up to date (v1.0.0)" |
| AF-2 | No internet access | Notification: "Could not check for updates. Check your network connection." |
| AF-3 | Update is a mandatory security patch | Notification is marked urgent; "mandatory update" badge shown |

**Expected Result:** User is informed of the available update and can navigate to the download page in one click. SSMS does not freeze or show any modal dialog.

---

### UC-P1-004: Uninstall AKML SQL

| Field | Value |
|---|---|
| **Use Case ID** | UC-P1-004 |
| **Title** | Uninstall AKML SQL via Windows Settings |
| **Actor** | Developer / DBA / IT Admin |
| **Preconditions** | (1) AKML SQL is installed. (2) User has administrator rights. |

**Main Flow:**

1. User opens **Windows Settings → Apps → Installed Apps** and locates "AKML SQL". Clicks **Uninstall**.
2. Uninstaller wizard prompts: "Remove AKML SQL from all IDE targets? [Yes] [No]"
3. User clicks **Yes**.
4. Uninstaller optionally prompts: "Remove user data (%AppData%\AKML SQL\)? [Yes] [Keep]"
5. Extension DLLs are removed from all SSMS and VS Extensions folders.
6. Core binaries removed from `C:\Program Files\AKML SQL\`.
7. Shortcuts and Start Menu entries removed.
8. Registry entries removed.
9. MEF cache cleared for each target IDE.
10. Uninstaller completes. "AKML SQL has been removed successfully" is shown.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | SSMS or VS is running at uninstall time | Warning: "Close SSMS/VS before uninstalling." Option to close automatically or defer to reboot |
| AF-2 | User chooses "Keep" for user data | `%AppData%\AKML SQL\` is preserved; only installation files are removed |

**Expected Result:** No orphaned files remain in any IDE Extensions folder. No registry entries remain. Re-launching SSMS shows no AKML SQL menu.

---

## Phase 2 — Core IntelliSense Engine

### UC-P2-001: Column Completion After Table Alias

| Field | Value |
|---|---|
| **Use Case ID** | UC-P2-001 |
| **Title** | Receive column completions after typing a table alias dot |
| **Actor** | Developer / DBA |
| **Preconditions** | (1) AKML SQL is loaded in SSMS. (2) A database connection is active. (3) Schema cache has been populated (Phase A complete). |

**Main Flow:**

1. Developer types: `SELECT o. FROM dbo.Orders o`
2. After typing `o.`, within 100ms a completion popup appears.
3. The popup lists all columns of `dbo.Orders` with data type annotations (e.g., `OrderID int PK`, `CustomerID int FK → Customers`, `OrderDate datetime`).
4. Developer uses arrow keys to select `CustomerID` and presses **Tab** to accept.
5. The editor inserts `CustomerID`. Query now reads `SELECT o.CustomerID FROM dbo.Orders o`.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | Multiple tables in FROM with aliases | Popup shows columns from all referenced tables, grouped by alias (e.g., "o.OrderID", "c.CompanyName") |
| AF-2 | User presses `Ctrl+Space` on unqualified position | Popup shows all objects in current schema + dbo, ranked by usage frequency |
| AF-3 | Schema cache not yet loaded for this table | Spinner shows briefly, then columns load from Phase B or Phase C lazy-load |
| AF-4 | Column name contains reserved word | Column shown with brackets suggested: `[Status]` |
| AF-5 | User types `cu` inside popup | Fuzzy filter narrows list to columns matching `cu` (e.g., `CustomerID`, `CustomerId`) |

**Expected Result:** Completion popup appears within 100ms (p95). Correct columns for the referenced table are displayed with data type and PK/FK information.

---

### UC-P2-002: FK-Based JOIN Completion

| Field | Value |
|---|---|
| **Use Case ID** | UC-P2-002 |
| **Title** | Auto-suggest JOIN tables based on foreign key relationships |
| **Actor** | Developer |
| **Preconditions** | (1) Schema cache Phase B is complete (FK metadata loaded). (2) A table is already in the FROM clause. |

**Main Flow:**

1. Developer has: `SELECT * FROM dbo.Orders o` and types `JOIN `.
2. After typing `JOIN `, the completion popup appears immediately.
3. The popup shows tables with FK relationships to `dbo.Orders`, with auto-generated ON clauses:
   - `dbo.OrderDetails od ON od.OrderID = o.OrderID`
   - `dbo.Customers c ON c.CustomerID = o.CustomerID`
4. Developer selects `dbo.OrderDetails od ON od.OrderID = o.OrderID` and presses **Enter**.
5. The full JOIN clause is inserted.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | Composite FK (multi-column) | ON clause includes all FK columns: `ON a.Col1 = b.Col1 AND a.Col2 = b.Col2` |
| AF-2 | No FK relationships for the current table | Popup shows all tables ranked by usage frequency (no specific FK ordering) |
| AF-3 | User types a partial table name | Fuzzy filter narrows the FK-based suggestions |

**Expected Result:** JOIN suggestions appear instantly after typing `JOIN `. Selecting a suggestion inserts the complete `table alias ON condition` clause. The ON clause correctly reflects the FK column mapping.

---

### UC-P2-003: Function Parameter Signature Help

| Field | Value |
|---|---|
| **Use Case ID** | UC-P2-003 |
| **Title** | View parameter signatures while calling a function or stored procedure |
| **Actor** | Developer |
| **Preconditions** | (1) IntelliSense is active. (2) Schema cache is loaded. |

**Main Flow:**

1. Developer types `CONVERT(`.
2. Immediately (within 100ms), a parameter signature tooltip appears:
   ```
   CONVERT(data_type, expression [, style])
   ↑ Param 1: data_type (sysname) — Target data type
   ```
3. Developer types `nvarchar(50),` and presses a key.
4. The tooltip advances to highlight parameter 2: `expression (sql_variant)`.
5. As the developer types the second argument, parameter 3 becomes available.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | User-defined stored procedure | Parameters loaded from schema cache; shows name, type, default value, direction (IN/OUT) |
| AF-2 | Developer presses `Escape` | Signature tooltip dismisses; reappears on next `(` or `,` |
| AF-3 | Overloaded function (multiple signatures) | Arrow buttons allow cycling through available signatures |

**Expected Result:** Signature tooltip appears on `(` and tracks the current parameter position as the developer types commas. Parameter names, types, and optional/default indicators are shown.

---

### UC-P2-004: Manual Schema Cache Refresh

| Field | Value |
|---|---|
| **Use Case ID** | UC-P2-004 |
| **Title** | Manually refresh the schema cache after a DDL change |
| **Actor** | DBA / Developer |
| **Preconditions** | (1) AKML SQL is connected to a database. (2) A new table `dbo.ProductReviews` was just created via a DDL statement. |

**Main Flow:**

1. Developer executes a `CREATE TABLE dbo.ProductReviews (...)` statement in SSMS.
2. AKML SQL detects the DDL execution and automatically triggers an incremental Phase A refresh.
3. Within 5 seconds, `dbo.ProductReviews` appears in completion suggestions.
4. Alternatively, developer presses `Ctrl+Shift+R` (or AKML SQL → Refresh Schema Cache).
5. A progress indicator appears in the status bar: "Refreshing schema cache...".
6. On completion, status bar shows "Schema cache updated (1,247 objects)".

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | DDL detection is off | Developer must manually press `Ctrl+Shift+R` to see new objects |
| AF-2 | Refresh triggered in a large database | Background refresh continues; partial results available immediately; spinner indicates in-progress state |

**Expected Result:** Newly created or altered objects appear in completions without restarting the IDE or reconnecting.

---

### UC-P2-005: Disable SSMS Native IntelliSense to Avoid Conflicts

| Field | Value |
|---|---|
| **Use Case ID** | UC-P2-005 |
| **Title** | Disable SSMS native IntelliSense on first load |
| **Actor** | Developer |
| **Preconditions** | (1) AKML SQL has just been installed and loads for the first time. (2) SSMS native IntelliSense is currently enabled. |

**Main Flow:**

1. SSMS loads. AKML SQL detects that SSMS built-in IntelliSense is active.
2. A one-time dialog appears: "AKML SQL provides its own IntelliSense. Disable SSMS's built-in IntelliSense for the best experience? [Yes] [No] [Don't ask again]"
3. Developer clicks **Yes**.
4. AKML SQL programmatically disables SSMS IntelliSense via the VS Shell settings API.
5. Dialog closes. AKML SQL IntelliSense is now the sole active completion engine.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | Developer clicks **No** | AKML SQL continues to function; a warning indicator in the status bar notes potential conflicts |
| AF-2 | Developer clicks **Don't ask again** | Dialog never appears again; setting saved in `config.json` |
| AF-3 | Uninstall is performed | SSMS native IntelliSense is automatically re-enabled if it was disabled by AKML SQL |

**Expected Result:** Only one IntelliSense engine is active at a time. No double popups, no keystroke conflicts.

---

### UC-P2-006: Quick Info Hover Tooltip

| Field | Value |
|---|---|
| **Use Case ID** | UC-P2-006 |
| **Title** | Hover over a table or column name to view metadata |
| **Actor** | Developer |
| **Preconditions** | (1) AKML SQL is loaded. (2) Schema cache is populated. (3) Developer is in a query editor. |

**Main Flow:**

1. Developer hovers the mouse cursor over `dbo.Orders` in the query editor.
2. Within 100ms, a Quick Info tooltip appears showing:
   - Object type: Table
   - Schema: dbo
   - Row count estimate: ~12,500 rows
   - Column count: 12
   - Description: "Stores customer order headers" (from `MS_Description` extended property, if present)
3. Developer moves the mouse away; tooltip disappears.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | Hover over a column name | Shows: data type, nullability, default value, description |
| AF-2 | Hover over a stored procedure | Shows: parameter list, return type, description |
| AF-3 | Hover over `@variable` | Shows: declared type, assigned value (if statically detectable) |
| AF-4 | Developer presses `Ctrl+K, Ctrl+I` | Quick Info appears for the identifier under the cursor |

**Expected Result:** Tooltip contains accurate metadata. For tables: row count, column count, description. For columns: type, nullability. No IDE freeze.

---

## Phase 3 — SQL Formatter

### UC-P3-001: Format an Entire SQL Document

| Field | Value |
|---|---|
| **Use Case ID** | UC-P3-001 |
| **Title** | Format entire SQL document with one keystroke |
| **Actor** | Developer / DBA |
| **Preconditions** | (1) AKML SQL is loaded. (2) An active formatting profile is selected. (3) A query editor is open with unformatted SQL. |

**Main Flow:**

1. Developer has a query editor open containing 500 lines of inconsistently formatted legacy SQL.
2. Developer presses **Ctrl+K, Y** (or AKML SQL → Format SQL).
3. A `FormatRequest` is sent via named pipe to the out-of-process engine.
4. The engine parses the SQL (ScriptDom), applies the active profile's 250+ formatting rules, and validates the result for semantic equivalence.
5. Within 200ms, the formatted SQL replaces the editor content.
6. SQL is now consistently indented, keywords are uppercase, one column per line, aligned JOINs.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | SQL contains syntax errors | Formatter formats what it can; erroneous regions are left unchanged; warning shown in status bar |
| AF-2 | SQL contains `--noformat` / `--endnoformat` | Content within noformat tags is preserved verbatim; surrounding code is formatted normally |
| AF-3 | Semantic validation fails (formatted ≠ original semantics) | Original SQL is returned unchanged; error logged: "Formatting aborted — semantic validation failed" |
| AF-4 | Script is 50,000 lines | Formatting still completes within 2 seconds (out-of-process, non-blocking for SSMS UI) |

**Expected Result:** SQL is reformatted according to the active profile. The semantic meaning of every statement is unchanged. The operation is undoable with `Ctrl+Z`.

---

### UC-P3-002: Format a Selected Code Block

| Field | Value |
|---|---|
| **Use Case ID** | UC-P3-002 |
| **Title** | Format only a selected portion of the SQL document |
| **Actor** | Developer |
| **Preconditions** | (1) AKML SQL is loaded. (2) Text is selected in the query editor. |

**Main Flow:**

1. Developer selects 20 lines of a subquery in a 500-line script.
2. Developer presses **Ctrl+K, F** (or right-click → Format Selection).
3. Only the selected region is formatted according to the active profile.
4. The rest of the document is untouched.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | Selection crosses a `--noformat` tag | Content within noformat region inside the selection is preserved verbatim |
| AF-2 | Selected SQL is syntactically invalid | Warning shown; original selection preserved |

**Expected Result:** Only the selected lines are reformatted. The surrounding document content is identical to before.

---

### UC-P3-003: Create and Apply a Custom Formatting Profile

| Field | Value |
|---|---|
| **Use Case ID** | UC-P3-003 |
| **Title** | Create a team-specific formatting profile and apply it |
| **Actor** | Team Lead / Developer |
| **Preconditions** | (1) AKML SQL is loaded. (2) The "Default" built-in profile exists. |

**Main Flow:**

1. Developer opens AKML SQL → Edit Formatting Profiles.
2. Developer clicks **New Profile**, names it "TeamStandard", and starts from "Default" as a base.
3. Developer changes:
   - `whitespace.lineBreakBeforeComma` → `true` (leading commas)
   - `casing.reservedKeywords` → `UPPERCASE`
   - `lists.oneItemPerLine` → `true`
4. The live preview pane on the right immediately shows the effect of each change.
5. Developer clicks **Save**.
6. Developer switches the active profile dropdown to "TeamStandard".
7. Developer presses **Ctrl+K, Y** to format the current document.
8. SQL now uses leading commas, uppercase keywords, one column per line.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | Developer exports the profile | Profile is saved as a JSON file for sharing with the team |
| AF-2 | Developer imports a shared profile | Profile JSON is loaded and becomes available in the dropdown |

**Expected Result:** "TeamStandard" profile is saved and immediately usable. Applying it produces SQL matching the team's agreed style.

---

### UC-P3-004: Bulk Format All SQL Files in a Directory

| Field | Value |
|---|---|
| **Use Case ID** | UC-P3-004 |
| **Title** | Format all .sql files in a project directory at once |
| **Actor** | Developer / Team Lead |
| **Preconditions** | (1) AKML SQL CLI tool is installed. (2) A directory contains 200 `.sql` files. |

**Main Flow:**

1. Developer opens a terminal and runs:
   `akmlsql-format.exe --directory "scripts/" --recursive --profile "TeamStandard"`
2. CLI reads all `.sql` files recursively.
3. Each file is formatted using the "TeamStandard" profile.
4. Files are written in-place. A summary is printed:
   ```
   Formatted: 198 files
   Skipped (noformat):  2 files
   Errors:  0
   ```
5. Developer commits the formatted files to source control.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | `--check` flag is used | No files are modified; CLI exits with code 1 if any file would change (for CI/CD gate) |
| AF-2 | A file is locked | CLI skips it with a warning and continues formatting the rest |

**Expected Result:** All `.sql` files in the directory are consistently formatted. Output summary reports the file count. No file's SQL semantics are changed.

---

## Phase 4 — Snippet Manager

### UC-P4-001: Expand a Built-in Code Snippet

| Field | Value |
|---|---|
| **Use Case ID** | UC-P4-001 |
| **Title** | Expand a snippet shortcode to a full code template |
| **Actor** | Developer |
| **Preconditions** | (1) AKML SQL is loaded. (2) Built-in snippets are available. |

**Main Flow:**

1. Developer is in a query editor at the beginning of a batch.
2. Developer types `ct` and the snippet `ct — Create Table` appears in the completion popup with a `{}` icon.
3. Developer presses **Tab** to expand.
4. The snippet body is inserted:
   ```sql
   CREATE TABLE [dbo].[NewTable]
   (
       [Id] int IDENTITY(1, 1) NOT NULL,

       CONSTRAINT [PK_NewTable] PRIMARY KEY CLUSTERED ([Id])
   );
   GO
   ```
5. The first placeholder `dbo` is highlighted. Developer types `Sales` to change the schema.
6. Developer presses **Tab** to jump to the next placeholder `NewTable` and types `ProductCategories`.
7. Developer presses **Tab** again to reach `$CURSOR$` — the final cursor position inside the column list.
8. Developer begins typing additional column definitions.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | Multiple snippets match the shortcode | Completion popup shows all matches; developer selects the desired one |
| AF-2 | Developer presses `Shift+Tab` | Cursor moves back to the previous placeholder |
| AF-3 | `format-on-expand` is enabled | Formatter applies the active profile to the inserted snippet body |

**Expected Result:** Snippet is expanded with all placeholders editable via Tab-key navigation. The active formatting profile is applied if configured.

---

### UC-P4-002: Create a Custom Snippet from Selected Code

| Field | Value |
|---|---|
| **Use Case ID** | UC-P4-002 |
| **Title** | Create a personal snippet from selected editor text |
| **Actor** | Developer |
| **Preconditions** | (1) AKML SQL is loaded. (2) Developer has a frequently-used query pattern in the editor. |

**Main Flow:**

1. Developer selects a frequently-used query block (e.g., a TRY/CATCH wrapper with transaction).
2. Developer right-clicks → **Create Snippet from Selection**.
3. The Snippet Manager dialog opens with the selected text pre-loaded in the code editor.
4. Developer sets:
   - **Name**: "Transaction with Error Handling"
   - **Shortcode**: `tct2`
   - **Category**: Control Flow
   - **Tags**: `transaction, try-catch`
5. Developer adds a `$CURSOR$` placeholder at the appropriate position inside the body.
6. Developer clicks **Save & Close**.
7. Snippet is saved to `%AppData%\AKML SQL\snippets\`.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | Shortcode already exists in personal snippets | Warning: "Shortcode 'tct2' is already in use. Overwrite or choose another?" |
| AF-2 | Developer uses a schema-aware variable | Variable is configured with `schemaAware: "tables"` to offer IntelliSense during expansion |

**Expected Result:** Custom snippet is available immediately in the completion popup. Typing `tct2` and pressing Tab expands the template.

---

### UC-P4-003: Surround Selected Code with a Snippet

| Field | Value |
|---|---|
| **Use Case ID** | UC-P4-003 |
| **Title** | Wrap selected SQL with a surround-with snippet |
| **Actor** | Developer |
| **Preconditions** | (1) AKML SQL is loaded. (2) Text is selected in the editor. |

**Main Flow:**

1. Developer selects a block of 10 SQL statements.
2. Developer presses **Ctrl+K, Ctrl+S** (surround-with shortcut).
3. A filtered list of surround-with snippets appears in the completion popup (showing only `stc`, `stran`, `sbe`, `scomment`, etc.).
4. Developer selects `stran — Surround Transaction`.
5. The selected block is wrapped:
   ```sql
   BEGIN TRANSACTION;
   BEGIN TRY
       -- (original selected code)
   END TRY
   BEGIN CATCH
       ROLLBACK TRANSACTION;
       THROW;
   END CATCH;
   COMMIT TRANSACTION;
   ```
6. `$SELECTEDTEXT$` in the snippet is replaced by the original selected code.

**Expected Result:** Selected code is wrapped inside the transaction/TRY-CATCH template. `$SELECTEDTEXT$` correctly contains all originally selected lines.

---

### UC-P4-004: Import SQL Prompt Snippets

| Field | Value |
|---|---|
| **Use Case ID** | UC-P4-004 |
| **Title** | Migrate SQL Prompt snippet library to AKML SQL |
| **Actor** | Developer migrating from SQL Prompt |
| **Preconditions** | (1) SQL Prompt has been or is installed. (2) `.sqlpromptsnippet` files exist in `%LocalAppData%\Red Gate\SQL Prompt *\Snippets\`. |

**Main Flow:**

1. Developer opens AKML SQL → Snippet Manager.
2. Developer clicks **Import**.
3. Import dialog auto-detects the SQL Prompt snippet folder and displays it pre-filled.
4. Developer sees a list of 45 snippets from SQL Prompt with a checkbox for each.
5. Developer checks all and clicks **Import All**.
6. AKML SQL converts each `.sqlpromptsnippet` XML file to `.akmlsnippet` JSON format, mapping SQL Prompt variables to AKML equivalents (`$DBNAME$` → `$DATABASE$`).
7. Import summary: "Imported 45 snippets. 0 errors. 2 shortcode conflicts (resolved by suffix)."
8. Snippets appear in the Personal source tree in Snippet Manager.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | SQL Prompt folder not auto-detected | Developer clicks **Browse** to manually locate the folder |
| AF-2 | A snippet's shortcode conflicts with a built-in snippet | The personal snippet takes priority (highest priority source); user is notified |

**Expected Result:** All SQL Prompt snippets are available in AKML SQL with their shortcodes and body intact. The migration requires zero manual editing for standard SQL Prompt snippets.

---

## Phase 5 — Static Code Analysis

### UC-P5-001: Real-Time Code Analysis with Squiggles

| Field | Value |
|---|---|
| **Use Case ID** | UC-P5-001 |
| **Title** | Detect SQL issues in real-time as the developer types |
| **Actor** | Developer |
| **Preconditions** | (1) AKML SQL code analysis is enabled. (2) A query editor is open. |

**Main Flow:**

1. Developer types: `DELETE FROM dbo.Orders`.
2. Within 20ms, AKML SQL analysis runs rule PE003 ("Missing WHERE on DELETE").
3. A red squiggle appears under the DELETE statement.
4. An error icon appears in the Error List panel: `PE003: DELETE without WHERE clause affects all rows — dbo.Orders`.
5. Developer hovers over the squiggle; a tooltip reads: "PE003: DELETE without WHERE clause — this will delete all rows."

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | Developer adds a WHERE clause | Squiggle disappears within 20ms of the WHERE clause being parsed |
| AF-2 | Analysis is set to run on save only | No real-time squiggles; analysis runs when developer saves the file |
| AF-3 | PE003 is disabled in CAsettings | Squiggle does not appear; rule is suppressed globally |

**Expected Result:** The DELETE statement is flagged immediately. The error appears in the Error List panel. The IDE remains fully responsive.

---

### UC-P5-002: Apply an Auto-Fix from a Lightbulb

| Field | Value |
|---|---|
| **Use Case ID** | UC-P5-002 |
| **Title** | Apply a one-click auto-fix to a flagged code issue |
| **Actor** | Developer |
| **Preconditions** | (1) Code analysis has flagged an issue with an auto-fix action available. (2) PE001 ("Avoid SELECT *") is flagged on a query. |

**Main Flow:**

1. Rule PE001 flags `SELECT * FROM dbo.Orders` with a warning squiggle.
2. Developer clicks on the squiggle (or presses `Ctrl+.`).
3. A lightbulb menu appears with fix options:
   - "Expand to explicit column list (8 columns)"
   - "Suppress PE001 for this line"
   - "Suppress PE001 for this file"
   - "Disable PE001 globally"
4. Developer selects "Expand to explicit column list".
5. AKML SQL queries the schema cache for `dbo.Orders` columns.
6. The `SELECT *` is replaced with `SELECT OrderID, CustomerID, OrderDate, TotalAmount, ...` (all columns).
7. The squiggle disappears.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | Developer selects "Suppress PE001 for this line" | `-- noqa: PE001` comment is inserted above the statement; squiggle disappears for that line only |
| AF-2 | Schema cache not loaded for the table | Auto-fix "Expand column list" is disabled; only suppress options are available |
| AF-3 | Developer selects "Fix all PE001 in file" | All occurrences of PE001 in the current file are fixed in one operation |

**Expected Result:** The code issue is resolved. The fix is applied in-place. The operation is undoable with `Ctrl+Z`.

---

### UC-P5-003: Run Bulk Code Analysis on a Directory

| Field | Value |
|---|---|
| **Use Case ID** | UC-P5-003 |
| **Title** | Analyze all SQL files in a project directory and generate a report |
| **Actor** | Developer / DBA / CI/CD Pipeline |
| **Preconditions** | (1) AKML SQL CLI is installed. (2) A directory of `.sql` scripts exists. (3) A `team-casettings.json` file defines rule settings. |

**Main Flow:**

1. CI/CD pipeline runs:
   `akmlsql-analyze.exe --directory "scripts/" --recursive --check --severity error --settings "team-casettings.json" --report "analysis-report.json"`
2. Analyzer scans all `.sql` files in the directory recursively.
3. Each file is analyzed in parallel against the 200+ rules configured in the settings file.
4. 23 errors are found (e.g., PE003, BP013, SE001).
5. CLI prints a summary and exits with code **1** (errors found).
6. `analysis-report.json` contains file paths, line numbers, rule IDs, severities, and messages.
7. CI/CD pipeline marks the build as failed and links to the report.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | No errors found | CLI exits with code 0; CI/CD build continues |
| AF-2 | `--severity warning` flag used | Exit code 1 if any warnings or errors exist |
| AF-3 | A file uses inline suppression `-- noqa: PE003` | That occurrence is suppressed; not counted in the report |

**Expected Result:** Report JSON contains all issues with file locations. CLI exit code signals success or failure to the CI/CD pipeline. Analysis of 100 files completes in under 30 seconds.

---

### UC-P5-004: Configure Team Code Analysis Settings

| Field | Value |
|---|---|
| **Use Case ID** | UC-P5-004 |
| **Title** | Create a shared CAsettings file to enforce team standards |
| **Actor** | Team Lead / DBA |
| **Preconditions** | (1) AKML SQL is installed. (2) Team has agreed on rule severity levels. |

**Main Flow:**

1. Team lead opens AKML SQL → Code Analysis Settings.
2. Team lead navigates the 8-category rule tree and adjusts:
   - `PE008` (NOLOCK hint): severity → `ignore` (team policy allows NOLOCK in read-heavy queries)
   - `BP013` (non-parameterized dynamic SQL): severity → `error`
   - `NM006` (single-letter alias): `enabled` → `false`
3. Team lead clicks **Export Settings** and saves `team-casettings.json`.
4. File is committed to source control in the repo root.
5. AKML SQL automatically discovers `.casettings` searching upward from the current file's directory.
6. All team members' IDEs now apply the team settings without manual configuration.

**Expected Result:** Shared CAsettings file is applied consistently across all team members' IDEs and the CI/CD pipeline. Rule overrides take effect immediately.

---

## Phase 6 — Code Refactoring

### UC-P6-001: Expand SELECT * to Explicit Column List

| Field | Value |
|---|---|
| **Use Case ID** | UC-P6-001 |
| **Title** | Replace SELECT * with an explicit column list from the schema |
| **Actor** | Developer |
| **Preconditions** | (1) Schema cache is loaded. (2) The query contains `SELECT *` referencing a known table. |

**Main Flow:**

1. Developer has: `SELECT * FROM dbo.Orders o`
2. Developer positions cursor on `*` and presses **Ctrl+B, W** (Expand Wildcards).
3. AKML SQL queries the schema cache for all columns of `dbo.Orders`.
4. Within 100ms, `SELECT *` is replaced with the explicit column list:
   ```sql
   SELECT
       o.OrderID,
       o.CustomerID,
       o.OrderDate,
       o.TotalAmount,
       o.Notes,
       o.IsActive,
       o.CreatedDate
   FROM dbo.Orders o
   ```
5. The operation is undoable.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | Multiple tables in FROM | Columns are prefixed with the correct alias for each table |
| AF-2 | Schema cache not loaded | Warning: "Connect to a database to expand wildcards" |
| AF-3 | View contains computed columns | All columns are included, including computed ones |

**Expected Result:** `SELECT *` is replaced with all column names prefixed by the table alias. The SQL remains syntactically valid. Total time under 100ms.

---

### UC-P6-002: Safe Rename an Identifier Across the Current Script

| Field | Value |
|---|---|
| **Use Case ID** | UC-P6-002 |
| **Title** | Rename a column alias consistently across the entire SQL script |
| **Actor** | Developer |
| **Preconditions** | (1) AKML SQL is loaded. (2) The query editor contains a multi-statement script referencing an identifier multiple times. |

**Main Flow:**

1. Developer right-clicks on the alias `OrderDate` (which appears 12 times in the script) and selects **AKML SQL → Safe Rename**.
2. A rename dialog appears pre-filled with "OrderDate". Developer types the new name: `OrderPlacedDate`.
3. Developer sees a preview panel listing all 12 occurrences with context snippets:
   ```
   - SELECT o.OrderDate, ...
   + SELECT o.OrderPlacedDate, ...

   - WHERE o.OrderDate >= @StartDate
   + WHERE o.OrderPlacedDate >= @StartDate
   ```
4. Developer confirms all changes are correct and clicks **Apply Selected**.
5. All 12 references are updated atomically. A single undo step (`Ctrl+Z`) reverts all changes.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | New name already exists in the scope | Error shown: "Name collision: 'OrderPlacedDate' already exists in this scope." Apply is blocked. |
| AF-2 | Developer unchecks some occurrences | Only checked occurrences are renamed; unchecked ones remain `OrderDate` |
| AF-3 | Scope is set to "Project/Directory" | Rename scans all `.sql` files in the directory and shows affected files with checkboxes |

**Expected Result:** All approved references are renamed atomically. The script remains semantically valid. One undo step reverts all changes.

---

### UC-P6-003: Extract a Subquery to a CTE

| Field | Value |
|---|---|
| **Use Case ID** | UC-P6-003 |
| **Title** | Extract a selected subquery into a named Common Table Expression |
| **Actor** | Developer |
| **Preconditions** | (1) A query contains an inline subquery. (2) The developer selects the subquery text. |

**Main Flow:**

1. Developer selects the subquery:
   ```sql
   SELECT OrderID, SUM(Quantity) AS TotalQty FROM dbo.OrderDetails GROUP BY OrderID
   ```
2. Developer selects AKML SQL → Refactor → Extract to CTE.
3. A preview dialog appears. Name field pre-filled as "CteQuery". Developer changes it to "OrderTotals".
4. Preview shows:
   ```sql
   WITH OrderTotals AS (
       SELECT OrderID, SUM(Quantity) AS TotalQty FROM dbo.OrderDetails GROUP BY OrderID
   )
   SELECT o.OrderDate, ot.TotalQty
   FROM dbo.Orders o
   JOIN OrderTotals ot ON ot.OrderID = o.OrderID
   ```
5. Developer clicks **Apply**.
6. The subquery is moved to a CTE block at the top of the query; the original inline position is replaced with the CTE name.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | Selection is not a valid standalone query | Error: "Selection is not a valid standalone query" |
| AF-2 | CTE name already exists | Warning: suggested alternative name shown |

**Expected Result:** The subquery is correctly extracted into a named CTE. The query logic is unchanged. The operation is undoable.

---

### UC-P6-004: Extract Selected Code to a Stored Procedure

| Field | Value |
|---|---|
| **Use Case ID** | UC-P6-004 |
| **Title** | Extract a code block into a new stored procedure with auto-generated parameters |
| **Actor** | Developer |
| **Preconditions** | (1) A block of SQL statements is selected in the editor. (2) The block references variables declared outside it. |

**Main Flow:**

1. Developer selects a 30-line data-processing block that references `@StartDate`, `@EndDate`.
2. Developer selects AKML SQL → Refactor → Extract to Stored Procedure.
3. A wizard dialog appears with:
   - Procedure name field: developer types `dbo.sp_ProcessOrders`
   - Parameters auto-generated: `@StartDate DATE`, `@EndDate DATE` (inferred from the referenced variables)
4. Preview shows the complete `CREATE PROCEDURE` script and the EXEC call that will replace the original block.
5. Developer confirms and clicks **Apply**.
6. The original code block is replaced with `EXEC dbo.sp_ProcessOrders @StartDate = @StartDate, @EndDate = @EndDate`.
7. A new editor tab opens with the generated stored procedure script ready to execute.

**Expected Result:** Selected code is encapsulated in a procedure. Input variables become parameters. The original location is replaced with an EXEC call. The new procedure script is ready to deploy.

---

### UC-P6-005: Parameterize Hard-Coded Literal Values

| Field | Value |
|---|---|
| **Use Case ID** | UC-P6-005 |
| **Title** | Replace hard-coded literal values with declared variables |
| **Actor** | Developer / DBA |
| **Preconditions** | (1) A query contains literal values in WHERE/ON/HAVING clauses. |

**Main Flow:**

1. Developer has:
   ```sql
   SELECT * FROM dbo.Orders WHERE CustomerID = 42 AND OrderDate >= '2026-01-01'
   ```
2. Developer selects AKML SQL → Refactor → Parameterize Literal Values.
3. Preview shows:
   ```
   Changes:
   + DECLARE @CustomerID int = 42
   + DECLARE @OrderDate date = '2026-01-01'

   - WHERE CustomerID = 42 AND OrderDate >= '2026-01-01'
   + WHERE CustomerID = @CustomerID AND OrderDate >= @OrderDate
   ```
4. Developer reviews and clicks **Apply**.
5. Variable declarations are inserted at the top of the script; literals are replaced with variable references.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | Developer unchecks a specific literal | That literal remains hard-coded; no variable is generated for it |
| AF-2 | Literal is a date string | Inferred type is `date` |
| AF-3 | Literal is a string that doesn't match date pattern | Inferred type is `nvarchar(max)` |

**Expected Result:** Hard-coded values become parameterized variables. The query produces identical results. Variable names are derived from the column they compare against.

---

### UC-P6-006: Convert Old-Style Joins to ANSI JOIN Syntax

| Field | Value |
|---|---|
| **Use Case ID** | UC-P6-006 |
| **Title** | Convert legacy comma-separated JOIN syntax to ANSI JOIN syntax |
| **Actor** | Developer / DBA |
| **Preconditions** | (1) The query uses old-style comma joins in the FROM clause. |

**Main Flow:**

1. Developer has:
   ```sql
   SELECT o.OrderID, c.CompanyName
   FROM dbo.Orders o, dbo.Customers c
   WHERE o.CustomerID = c.CustomerID AND o.IsActive = 1
   ```
2. Developer presses **Ctrl+B, J** (Convert old-style JOINs).
3. Within 100ms, the query is transformed:
   ```sql
   SELECT o.OrderID, c.CompanyName
   FROM dbo.Orders o
   INNER JOIN dbo.Customers c ON c.CustomerID = o.CustomerID
   WHERE o.IsActive = 1
   ```
4. The join condition is moved from WHERE to the ON clause.

**Expected Result:** Comma-separated FROM list is converted to ANSI JOIN syntax. The WHERE clause retains only non-join filter conditions. Semantics are preserved.

---

## Phase 7 — SQL History & Tab Management

### UC-P7-001: View and Restore a Previously Executed Query

| Field | Value |
|---|---|
| **Use Case ID** | UC-P7-001 |
| **Title** | Find and restore a query executed earlier in the day |
| **Actor** | Developer |
| **Preconditions** | (1) SQL History is enabled. (2) The developer executed queries earlier in the session. |

**Main Flow:**

1. Developer realizes they need to re-run a query executed an hour ago.
2. Developer presses **Ctrl+Alt+H** to open the SQL History panel.
3. History panel shows grouped entries by time (Today, Yesterday, etc.) with server, database, user, duration, and row count.
4. Developer types `customers` in the search box. The list filters to queries referencing "customers".
5. Developer locates the desired query (14:15, SQL-DEV-02, 2,341 rows).
6. Developer double-clicks it → clicks **Open in New Tab**.
7. A new query editor tab opens with the full SQL text and the original connection context.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | Developer filters by server | Dropdown filters history to entries from SQL-PROD-01 only |
| AF-2 | Developer clicks **Re-execute** | Query is executed immediately against the current (or original) connection |
| AF-3 | Developer stars an entry | Entry is saved as a Favorite and never auto-deleted |
| AF-4 | Developer selects two entries and clicks **Compare** | Side-by-side diff shows the differences between the two SQL texts |

**Expected Result:** Query from history is opened in a new tab with its full text. The developer can immediately inspect and re-execute it.

---

### UC-P7-002: Tab Color-Coding for Server Environment

| Field | Value |
|---|---|
| **Use Case ID** | UC-P7-002 |
| **Title** | Automatically color-code query tabs based on connected server environment |
| **Actor** | Developer / DBA |
| **Preconditions** | (1) Tab coloring rules are configured. (2) Developer has tabs connected to multiple servers. |

**Main Flow:**

1. Developer opens a new query tab connected to `SQL-PROD-01`.
2. The tab header turns red with a "PRODUCTION" label badge.
3. Developer opens another tab connected to `SQL-DEV-02`.
4. That tab header turns green with a "DEV" label.
5. Developer is now visually aware of the environment context for each active tab.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | Server name doesn't match any rule | Tab uses default color (no color); no label shown |
| AF-2 | Developer customizes a rule | New color/label takes effect for future tabs matching that pattern |

**Expected Result:** Tabs are consistently color-coded. Red tabs visually signal production environments. The developer cannot accidentally run destructive queries without noticing the red tab.

---

### UC-P7-003: Production Server Execution Guard

| Field | Value |
|---|---|
| **Use Case ID** | UC-P7-003 |
| **Title** | Receive a confirmation prompt before executing DML on a production server |
| **Actor** | Developer / DBA |
| **Preconditions** | (1) Safety execution warnings are enabled. (2) The active tab is connected to a server matching the production pattern. |

**Main Flow:**

1. Developer is on a tab connected to `SQL-PROD-01` (red tab, "PRODUCTION" label).
2. Developer types `UPDATE dbo.Orders SET IsActive = 0 WHERE OrderDate < '2020-01-01'`.
3. Developer presses **F5** to execute.
4. A modal confirmation dialog appears:
   ```
   ⚠ You are about to execute on PRODUCTION server [SQL-PROD-01].
   Database: AdventureWorks

   Are you sure you want to proceed?
   [Cancel]  [Execute]
   ```
5. Developer clicks **Execute** (after reviewing carefully).
6. Query executes on the production server.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | Query is `SELECT` only | No confirmation dialog; read-only queries bypass the production guard |
| AF-2 | `DELETE FROM table` without WHERE | Two-step confirmation: production guard dialog + separate "No WHERE clause" error warning |
| AF-3 | Developer clicks **Cancel** | Query is not executed; editor is unchanged |
| AF-4 | `DROP TABLE` statement | Requires the developer to type the object name to confirm (e.g., "Type 'dbo.Orders' to confirm DROP") |

**Expected Result:** Dangerous DML/DDL operations on production require explicit confirmation. The confirmation dialog clearly identifies the server and database. SELECT queries pass through without interruption.

---

### UC-P7-004: Recover Tabs After SSMS Crash

| Field | Value |
|---|---|
| **Use Case ID** | UC-P7-004 |
| **Title** | Restore all open unsaved query tabs after an unexpected SSMS crash |
| **Actor** | Developer |
| **Preconditions** | (1) Session recovery is enabled. (2) SSMS crashed while the developer had 5 unsaved query tabs open. |

**Main Flow:**

1. SSMS restarts after an unexpected termination.
2. AKML SQL detects the abnormal previous termination by checking the session store.
3. A recovery dialog appears:
   ```
   AKML SQL Session Recovery

   5 unsaved tabs from your previous session were found (2026-03-24 14:23):
   ☑ Unsaved Query 1  — SQL-PROD-01 > AdventureWorks
   ☑ Unsaved Query 2  — SQL-DEV-02 > TestDB
   ☑ GetOrdersReport.sql — SQL-DEV-02 > TestDB
   ☑ Unsaved Query 4  — (local) > Northwind
   ☐ Unsaved Query 5  — SQL-DEV-02 > TestDB (similar to current session)

   [Restore Selected]  [Ignore All]
   ```
4. Developer checks all 5 tabs and clicks **Restore Selected**.
5. All 5 tabs are opened with their content at the time of the last auto-save (up to 60 seconds before crash).

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | Developer clicks **Ignore All** | Session is discarded; normal SSMS startup proceeds |
| AF-2 | SSMS was closed normally | No recovery dialog appears on next startup |

**Expected Result:** All auto-saved tab contents are restored. The developer loses at most 60 seconds of work (the auto-save interval).

---

## Phase 8 — Productivity Toolkit

### UC-P8-001: Search Within a Results Grid

| Field | Value |
|---|---|
| **Use Case ID** | UC-P8-001 |
| **Title** | Find and highlight a value within the query results grid |
| **Actor** | Developer / DBA |
| **Preconditions** | (1) A query has been executed and results are displayed in the grid. (2) Results contain hundreds of rows. |

**Main Flow:**

1. Developer executes a query returning 5,000 rows across 12 columns.
2. Developer presses **Ctrl+F** while the results grid has focus.
3. A search bar appears above the grid with a text field and options: `Regex`, `Match Case`, `Whole Cell`.
4. Developer types "Contoso". All cells containing "Contoso" are highlighted in yellow.
5. Developer presses **F3** to jump to the next match.
6. Status bar shows "Match 3 of 12".

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | Regex option is enabled | Developer can search with patterns like `^\d{4}-\d{2}$` |
| AF-2 | No matches found | Status bar: "No results found for 'Contoso'" |

**Expected Result:** Matching cells are highlighted. Developer can navigate between matches with F3/Shift+F3. Grid rows not containing the match are still visible (grid is filtered, not hidden by default).

---

### UC-P8-002: Export Query Results to Excel

| Field | Value |
|---|---|
| **Use Case ID** | UC-P8-002 |
| **Title** | Export the full result set to a formatted Excel file |
| **Actor** | Developer / Business Analyst |
| **Preconditions** | (1) A query has been executed with results in the grid. |

**Main Flow:**

1. Developer right-clicks on the results grid.
2. Selects **Export → Export to Excel (.xlsx)**.
3. A Save dialog prompts for file path and name.
4. Developer selects the location and clicks **Save**.
5. An `.xlsx` file is created with:
   - Row 1: Bold headers with column names
   - Rows 2–N: Data rows
   - Auto-fitted column widths
   - Data types preserved (dates as Excel dates, numbers as numbers)
6. Excel opens automatically if "Open after export" is checked.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | Developer selects **Copy As → JSON** | Copies the current selection as JSON array to the clipboard |
| AF-2 | Developer selects **Export → SQL INSERT scripts** | Generates INSERT INTO statements for selected rows |
| AF-3 | Result set is very large (1M+ rows) | Progress dialog shown; export runs asynchronously |

**Expected Result:** Excel file is created with properly formatted headers and data. Dates and numbers use native Excel types. File opens cleanly in Excel.

---

### UC-P8-003: Navigate to Object Definition with Go to Definition

| Field | Value |
|---|---|
| **Use Case ID** | UC-P8-003 |
| **Title** | Jump directly to the CREATE definition of a stored procedure or table |
| **Actor** | Developer |
| **Preconditions** | (1) Schema cache is loaded. (2) Developer is editing a query that references a stored procedure. |

**Main Flow:**

1. Developer has `EXEC dbo.sp_GetCustomerOrders @CustID = 42` in the editor.
2. Developer right-clicks on `sp_GetCustomerOrders` and selects **Go to Definition** (or presses **F12**).
3. AKML SQL retrieves the stored procedure definition from the database using `sp_helptext` or `sys.sql_modules`.
4. A new query editor tab opens with the full `CREATE PROCEDURE dbo.sp_GetCustomerOrders ...` script.
5. The cursor is positioned at the `CREATE PROCEDURE` declaration.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | Developer presses **Alt+F12** | Inline "peek" panel opens within the current tab without navigating away |
| AF-2 | Object is a table | A new tab opens with the table's `CREATE TABLE` script including all constraints and indexes |
| AF-3 | Object not found in database | Error: "Definition not found for 'dbo.sp_GetCustomerOrders'" |

**Expected Result:** The object's CREATE definition appears in a new tab. The developer can read the implementation without leaving the current context.

---

### UC-P8-004: Execute Only the Current SQL Statement

| Field | Value |
|---|---|
| **Use Case ID** | UC-P8-004 |
| **Title** | Execute the single SQL statement at the cursor position |
| **Actor** | Developer |
| **Preconditions** | (1) AKML SQL is loaded. (2) The editor contains a multi-statement script separated by semicolons or GO. |

**Main Flow:**

1. Developer has a 200-line script with 15 SQL statements.
2. Developer positions the cursor inside the 7th statement (a SELECT query).
3. Developer presses **Alt+Enter** (Execute Current Statement).
4. Only the statement at the cursor is sent for execution. The other 14 statements are not executed.
5. Results appear in the results grid.

**Expected Result:** Only the statement touching the cursor is executed. This prevents accidental execution of DDL or DML statements elsewhere in the script.

---

### UC-P8-005: Open the Command Palette

| Field | Value |
|---|---|
| **Use Case ID** | UC-P8-005 |
| **Title** | Use the Command Palette to discover and invoke AKML SQL features |
| **Actor** | Developer |
| **Preconditions** | (1) AKML SQL is loaded in SSMS or VS. |

**Main Flow:**

1. Developer presses **Ctrl+Shift+P**.
2. The Command Palette appears as a floating search box.
3. Developer types "format". The palette shows:
   - `Format SQL — Ctrl+K, Y`
   - `Format Selection — Ctrl+K, F`
   - `Edit Formatting Profiles`
   - `Switch Formatting Profile → Compact`
4. Developer selects "Format SQL" and presses Enter.
5. The current document is formatted. The palette closes.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | Developer types a partial command word | Fuzzy matching surfaces relevant commands (e.g., "snip" → "Snippet Manager") |
| AF-2 | Developer doesn't know the keyboard shortcut | Palette shows all shortcuts inline; pressing a command teaches the shortcut |

**Expected Result:** Any AKML SQL feature is accessible within 3 keystrokes via the palette. Keyboard shortcuts are discoverable without visiting menus.

---

## Phase 9 — AI-Powered SQL Assistance

### UC-P9-001: Generate a Query from Natural Language (Text-to-SQL)

| Field | Value |
|---|---|
| **Use Case ID** | UC-P9-001 |
| **Title** | Generate a SQL query by typing a natural language description |
| **Actor** | Developer / Business Analyst |
| **Preconditions** | (1) AI features are enabled in AKML SQL settings. (2) An AI provider (e.g., Claude, GPT-4o) is configured. (3) A database connection is active and schema cache is loaded. |

**Main Flow:**

1. Developer types in the editor: `--ai: show me the top 10 customers by total order amount this year`
2. Developer presses **Ctrl+Shift+G** (or AKML SQL → AI → Generate SQL).
3. AKML SQL prepares a context payload: the natural language prompt + compressed schema snapshot (table names, columns, types, FK relationships relevant to "customers" and "orders").
4. Payload is sent to the configured AI provider.
5. Within 5 seconds, the AI returns a SQL query.
6. A diff-style preview panel appears showing the generated SQL side-by-side with the `--ai:` comment.
7. Developer reviews the SQL, verifies it matches the intent.
8. Developer clicks **Accept** (or presses Tab). The generated SQL replaces the `--ai:` comment in the editor.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | Developer clicks **Edit** | The generated SQL is inserted into the editor but not finalized; developer can modify it |
| AF-2 | Developer clicks **Reject** | The `--ai:` comment is restored; no change to the editor |
| AF-3 | AI provider API is unavailable | Error notification; fallback to local Ollama model if configured |
| AF-4 | Privacy mode is `schemaOnly` | Table and column names are sent; no data values or PII are transmitted |
| AF-5 | AI generates syntactically invalid SQL | Warning badge in the preview panel; AKML SQL code analysis flags errors |

**Expected Result:** A complete, schema-aware SQL query is generated in under 5 seconds. The query is shown in a diff preview before being applied. No SQL is auto-executed without explicit user confirmation.

---

### UC-P9-002: Explain Complex SQL in Plain English

| Field | Value |
|---|---|
| **Use Case ID** | UC-P9-002 |
| **Title** | Receive a plain-English explanation of an unfamiliar SQL query |
| **Actor** | Developer / Junior DBA |
| **Preconditions** | (1) AI features are enabled. (2) Developer has selected a complex query in the editor. |

**Main Flow:**

1. Developer has selected a 50-line stored procedure containing window functions, CTEs, and a PIVOT.
2. Developer presses **Ctrl+Shift+E** (or right-click → AI Explain).
3. Selected SQL + schema context is sent to the AI provider.
4. Within 3 seconds, the AI Chat panel opens (or a tooltip appears) with an explanation:
   ```
   Purpose: Calculates monthly sales totals per product category using a rolling 3-month average.

   Step by step:
   1. The CTE `MonthlySales` aggregates order line items by year, month, and product category.
   2. The window function `AVG(...) OVER (PARTITION BY ... ORDER BY ... ROWS BETWEEN ...)` computes the 3-month rolling average.
   3. The PIVOT converts the month-based rows into columns (Jan, Feb, Mar...).

   Key details:
   - The NULLIF in line 12 prevents division by zero when category count is 0.
   - Performance: This query does a full table scan on dbo.OrderDetails; consider an index on (CategoryID, OrderDate).
   ```

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | No text selected | AI explains the entire current statement at the cursor |
| AF-2 | Developer asks a follow-up question | The AI Chat panel retains conversation context for multi-turn dialogue |

**Expected Result:** A structured plain-English explanation appears in under 3 seconds. The explanation includes purpose, step-by-step breakdown, and performance notes.

---

### UC-P9-003: AI Fix a Query That Failed with an Error

| Field | Value |
|---|---|
| **Use Case ID** | UC-P9-003 |
| **Title** | Automatically get a suggested fix for a failed SQL execution |
| **Actor** | Developer |
| **Preconditions** | (1) AI features are enabled. (2) A query just failed with a SQL Server error. |

**Main Flow:**

1. Developer executes a query that fails:
   ```
   Msg 207, Level 16, State 1, Line 3
   Invalid column name 'CustumerID'.
   ```
2. AKML SQL displays a notification banner: "Query failed. [Fix with AI] [View Error]"
3. Developer clicks **Fix with AI**.
4. AKML SQL sends the failing SQL + the error message + schema context to the AI.
5. Within 5 seconds, a diff panel appears:
   ```
   - WHERE o.CustumerID = @ID   -- typo
   + WHERE o.CustomerID = @ID   -- corrected
   ```
   with an annotation: "Fixed: column name 'CustumerID' was a typo for 'CustomerID' (verified against schema)."
6. Developer clicks **Accept Fix**. The corrected query is in the editor.
7. Developer re-executes successfully.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | Auto-offer fix on error is enabled | Diff panel appears automatically without the developer clicking "Fix with AI" |
| AF-2 | AI cannot determine a fix | AI explains the error in plain English but does not suggest a specific code change |
| AF-3 | Schema verification disproves the fix | Warning badge: "AI suggested column 'CustomerID' but this column does not exist in the schema" |

**Expected Result:** A diff showing the corrected SQL appears within 5 seconds of clicking "Fix with AI". The correction is grounded in the actual schema, not a guess. No SQL is auto-executed.

---

### UC-P9-004: Optimize a Slow Query with AI Assistance

| Field | Value |
|---|---|
| **Use Case ID** | UC-P9-004 |
| **Title** | Receive AI-generated optimization suggestions for a slow query |
| **Actor** | Developer / DBA |
| **Preconditions** | (1) AI features are enabled. (2) A slow query is in the editor. |

**Main Flow:**

1. Developer selects a query known to be slow.
2. Developer presses **Ctrl+Shift+O** (AI Optimize).
3. AKML SQL sends the query + schema (including existing indexes and FKs) to the AI.
4. Within 8 seconds, the AI Chat panel shows categorized suggestions:
   - **Safe changes:** "Remove redundant `DISTINCT` — the query already uses `GROUP BY` on all columns."
   - **Review changes:** "Change `WHERE YEAR(OrderDate) = 2026` to `WHERE OrderDate >= '2026-01-01' AND OrderDate < '2027-01-01'` for a SARGable seek."
   - **Index suggestion:** "CREATE INDEX IX_Orders_OrderDate ON dbo.Orders (OrderDate) INCLUDE (CustomerID, TotalAmount)" — with estimated improvement and impact.
5. Developer clicks **Apply Safe Changes** for the DISTINCT removal.
6. Developer reviews the index suggestion and clicks **Copy Script** to deploy it manually.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | Developer provides an execution plan XML | AI analyzes the plan for actual vs. estimated row mismatches, costly operators |
| AF-2 | No safe optimizations found | AI explains why the query is already optimal or flags it as complex to analyze |

**Expected Result:** Actionable optimization suggestions appear within 8 seconds. Safe transformations can be applied with one click. Index suggestions include scripts ready to copy-paste to a deployment query.

---

### UC-P9-005: Interactive AI Chat for Schema Questions

| Field | Value |
|---|---|
| **Use Case ID** | UC-P9-005 |
| **Title** | Ask multi-turn questions about the database schema via the AI Chat panel |
| **Actor** | Developer / Junior DBA |
| **Preconditions** | (1) AI features are enabled. (2) AI Chat panel is open. (3) Schema cache is loaded. |

**Main Flow:**

1. Developer opens the AI Chat panel: **Ctrl+Shift+A**.
2. Developer types: "How can I improve the performance of my GetOrdersByDate procedure?"
3. AI receives the message + current schema context (including the procedure's definition from the schema cache).
4. AI responds with specific, schema-grounded recommendations:
   - Suggests rewriting the WHERE clause to be SARGable
   - Proposes a covering index with an **[Apply Index]** button
   - Suggests adding `SET NOCOUNT ON` with an **[Apply Fix]** button
5. Developer clicks **[Apply Fix]** next to the `SET NOCOUNT ON` suggestion.
6. AKML SQL applies the fix to the procedure in the editor.
7. Developer asks a follow-up: "What tables does this procedure join?"
8. AI answers using schema metadata — no hallucination because it has access to the actual procedure definition.

**Alternative Flows:**

| Alt # | Condition | Steps |
|---|---|---|
| AF-1 | Developer asks about a table not in the schema | AI clarifies it doesn't see that table and asks the developer to confirm the table name |
| AF-2 | Developer switches databases mid-conversation | AI detects the connection change and reloads schema context for the new database |
| AF-3 | Privacy mode is `anonymous` | Table and column names are hashed before being sent; AI still provides structural guidance |

**Expected Result:** Multi-turn conversation maintains context. Schema-specific questions receive grounded, accurate answers. Suggested code changes can be applied directly from the chat panel. No SQL is auto-executed without user confirmation.

---

## Summary of Use Cases

| Phase | Phase Name | Use Cases | IDs |
|---|---|---|---|
| 1 | Foundation & Installer | 4 | UC-P1-001 to UC-P1-004 |
| 2 | Core IntelliSense Engine | 6 | UC-P2-001 to UC-P2-006 |
| 3 | SQL Formatter | 4 | UC-P3-001 to UC-P3-004 |
| 4 | Snippet Manager | 4 | UC-P4-001 to UC-P4-004 |
| 5 | Static Code Analysis | 4 | UC-P5-001 to UC-P5-004 |
| 6 | Code Refactoring | 6 | UC-P6-001 to UC-P6-006 |
| 7 | SQL History & Tab Management | 4 | UC-P7-001 to UC-P7-004 |
| 8 | Productivity Toolkit | 5 | UC-P8-001 to UC-P8-005 |
| 9 | AI-Powered SQL Assistance | 5 | UC-P9-001 to UC-P9-005 |
| **Total** | | **42** | |

---

## Actors Reference

| Actor | Description |
|---|---|
| **Developer** | SQL developer using SSMS or Visual Studio with SSDT for day-to-day query authoring and stored procedure development |
| **DBA** | Database Administrator managing production databases, monitoring performance, and running maintenance scripts |
| **Team Lead** | Senior developer or architect responsible for establishing team coding standards and shared configurations |
| **Business Analyst** | Non-developer user who needs to write or understand SQL queries for data exploration |
| **Enterprise Administrator** | IT or DevOps role responsible for mass deployment of AKML SQL across organization machines |
| **CI/CD System** | Automated pipeline (GitHub Actions, Azure DevOps, etc.) running the AKML SQL CLI for format-check or analysis gating |

---

*End of AKML SQL Use Cases — v1.0*
