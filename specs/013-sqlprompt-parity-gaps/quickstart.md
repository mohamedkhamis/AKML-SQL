# Quickstart: SQL Prompt Parity — Remaining Gaps

**Date**: 2026-04-03  
**Feature**: `013-sqlprompt-parity-gaps`

## Verification Scenarios

### Scenario 1: Options Dialog Color Audit (US1)

1. Open SSMS 22 with AKML-SQL installed
2. Open Settings (AKML SQL → Options)
3. In Light theme: verify dialog bg=#F0F0F0, panel bg=#FFFFFF, selected=#0078D4
4. Switch to Dark theme in settings
5. Reopen dialog: verify dialog bg=#2D2D3B, panel bg=#1E1E2E, text=#8892A8, border=#3A3F4E
6. **Pass criteria**: All colors match within ±2 hex values

### Scenario 2: IntelliSense Icon Colors (US2)

1. Open a SQL query tab connected to a database
2. Type `SELECT ` and trigger Ctrl+Space
3. Verify icon badges:
   - Table (T) = yellow #E5C04B with 20% opacity bg
   - Column (C) = blue #61AFEF with 20% opacity bg
   - Keyword (K) = silver #ABB2BF with 15% opacity bg
4. Type `FROM dbo.` to trigger column list
5. Verify Column (C) badges use #61AFEF
6. **Pass criteria**: All 12 object types use SQL Prompt palette

### Scenario 3: Unformat Command (US3)

1. Open a SQL query with multi-line formatted code:
   ```sql
   SELECT
       CustomerID,
       FirstName,
       LastName
   FROM
       dbo.Customers
   WHERE
       IsActive = 1
   ORDER BY
       LastName ASC;
   ```
2. Select all, invoke Unformat (Ctrl+B, Ctrl+U or Command Palette)
3. Verify output: `SELECT CustomerID, FirstName, LastName FROM dbo.Customers WHERE IsActive = 1 ORDER BY LastName ASC;`
4. Verify string literals with spaces are preserved
5. **Pass criteria**: Single line, minimal whitespace, semantically identical

### Scenario 4: Formatting Region Directives (US4)

1. Open a SQL document with:
   ```sql
   SELECT col1, col2
   FROM dbo.Table1
   -- AKML formatting off
   SELECT     col1,     col2
       FROM   dbo.HandFormatted
   -- AKML formatting on
   SELECT col3 FROM dbo.Table2
   ```
2. Invoke Format Document (Ctrl+K, Y)
3. Verify: lines 1-2 and 7 are formatted; lines 4-5 are preserved exactly
4. Replace markers with `-- SQL Prompt formatting off/on` and repeat
5. Verify same behavior with legacy syntax
6. **Pass criteria**: Hand-formatted region preserved byte-for-byte

### Scenario 5: History Advanced Search (US5)

1. Execute several queries to populate history
2. Open SQL History window
3. Test wildcard: type `Product*` → verify prefix matches
4. Test boolean: type `SELECT OR DELETE` → verify either-term results
5. Test NOT: type `NOT DROP` → verify exclusion
6. Test exact phrase: type `"create view"` → verify phrase match
7. Test CamelCase: type `PC` → verify ProductCategory matches
8. **Pass criteria**: All 5 search types return correct results

### Scenario 6: Search Match Highlighting (US6)

1. Search for "SELECT" in History
2. Select a result query
3. Verify all "SELECT" occurrences in the code preview have Yellow Ochre (#F9A825, 30% opacity) background
4. Clear search, verify highlighting removed
5. **Pass criteria**: All match occurrences highlighted, cleared on empty search

### Scenario 7: Tab Color on Status Bar (US8)

1. Connect to a server matching PROD pattern
2. Verify tab shows red color
3. Verify SSMS status bar shows red color band
4. Undock the query window
5. Verify floating window has 3px red border
6. Switch to a DEV connection tab
7. Verify status bar updates to green
8. **Pass criteria**: Color visible on tab, status bar, and floating window

### Scenario 8: Silent Install with Logging (US9)

1. Run: `AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /log=install.log`
2. Verify install.log is created with detailed step-by-step output
3. Start SSMS, run installer again
4. Verify warning about running SSMS instance
5. **Pass criteria**: Log file created, SSMS detection works
