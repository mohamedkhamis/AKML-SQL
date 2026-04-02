# Feature Specification: SQL Prompt Core Parity — Remaining Gaps

**Feature Branch**: `011-core-parity-remaining-gaps`  
**Created**: 2026-04-02  
**Status**: Draft  
**Input**: Fill all remaining gaps between AKML SQL and SQL Prompt Core features. Preserves all existing AKML SQL features and adds only the missing ones identified by gap analysis.

## Gap Summary

After completing spec 010 (8 major features), a detailed comparison of `doc/progress.md` against `doc/SQL-Prompt-Features/SQL_Prompt_Features_Core.md` identified 7 remaining gaps:

| Gap | SQL Prompt Reference | Priority | Impact |
|-----|---------------------|----------|--------|
| INSERT metadata comments | Core 1.8 | P1 | Users expect column type/default info in INSERT completions |
| Convert sp_executesql to SQL | Core 6.4 | P1 | Debugging dynamic SQL is a daily workflow for DBAs |
| Copy with headers toggle | Core 8 | P2 | Common grid copy need when pasting into emails/docs |
| Completion popup Ctrl transparency | Core 1.1 | P3 | See code behind popup without dismissing |
| Tab color gradient option | Core 5.1 | P3 | Visual polish matching SQL Prompt's tab appearance |
| Excel 15+ digit precision | Core 8 | P3 | Prevents Excel from rounding large numbers |
| Split Table refactoring | Core 6.3 | P4 | Advanced normalization refactoring (rare use case) |

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - INSERT Metadata Comments (Priority: P1)

A developer types `INSERT INTO dbo.Products` and accepts the completion suggestion. The system expands it with an explicit column list where each column is followed by an inline comment showing the data type, nullability, and default value (if any).

**Why this priority**: SQL Prompt's INSERT completion with metadata comments is one of its most-loved features. It eliminates the need to look up column types separately. The Expand Insert Columns feature already exists in AKML SQL but lacks the metadata comment annotations.

**Independent Test**: Type `INSERT INTO dbo.Products` in an editor connected to a database, accept the expansion, and verify each column has a trailing comment like `-- nvarchar(40), not null`.

**Acceptance Scenarios**:

1. **Given** the user types `INSERT INTO dbo.Products` and triggers Expand Insert Columns, **When** the expansion completes, **Then** each column in the generated column list has a trailing comment showing the data type and nullability (e.g., `ProductName, -- nvarchar(40), not null`).

2. **Given** a column has a default constraint, **When** the INSERT expansion generates the comment, **Then** the default value is included (e.g., `UnitPrice, -- money, null, default ((0))`).

3. **Given** the user has disabled "Include data types as comments" in settings, **When** the expansion occurs, **Then** no metadata comments are added (plain column list only).

4. **Given** a column is an identity column, **When** the expansion occurs, **Then** the identity column is excluded from the column list (or marked with `-- IDENTITY, auto-generated`).

---

### User Story 2 - Convert sp_executesql to Static SQL (Priority: P1)

A developer is debugging a stored procedure that contains dynamic SQL built with `sp_executesql`. They select the `EXEC sp_executesql` statement, open the Actions List (lightbulb), and choose "Convert to Static SQL". The system extracts the SQL template string, substitutes parameter values, and produces a runnable static SQL statement.

**Why this priority**: Debugging dynamic SQL is a daily activity for DBAs and developers. Converting sp_executesql calls to static SQL for testing is manual and error-prone. This is a frequently requested SQL Prompt feature.

**Independent Test**: Write a `EXEC sp_executesql N'SELECT * FROM dbo.Orders WHERE OrderID = @id', N'@id int', @id = 5` statement, select it, invoke the action, and verify the output is `SELECT * FROM dbo.Orders WHERE OrderID = 5`.

**Acceptance Scenarios**:

1. **Given** the user selects an `EXEC sp_executesql` statement with parameters, **When** they invoke "Convert to Static SQL" from the Actions List, **Then** the dynamic SQL template is extracted, parameters are substituted with their values, and the resulting static SQL replaces the selection.

2. **Given** the sp_executesql call uses named parameters with explicit types, **When** the conversion occurs, **Then** each `@paramName` in the template is replaced with the corresponding value from the parameter list.

3. **Given** the sp_executesql call has string parameters, **When** values are substituted, **Then** string values retain their quotes (e.g., `@name = N'Smith'` becomes `'Smith'` in the output).

4. **Given** the selected text is not a valid sp_executesql call, **When** the user invokes the action, **Then** an informational message appears: "Selection is not a valid sp_executesql statement."

---

### User Story 3 - Copy Grid Results with Headers (Priority: P2)

A developer runs a query and wants to paste the results into an email or document. They select cells in the results grid, right-click, and choose "Copy with Headers". The copied data includes column header names as the first row, followed by the selected data rows.

**Why this priority**: Copying results with column headers is a basic productivity need. Without it, users must manually type column names when pasting into emails, spreadsheets, or documentation.

**Independent Test**: Run `SELECT OrderID, CustomerName, Total FROM dbo.Orders`, select several rows, right-click "Copy with Headers", paste into a text editor, and verify the first line contains the column names.

**Acceptance Scenarios**:

1. **Given** the user selects cells in the results grid, **When** they choose "Copy with Headers" from the context menu, **Then** the clipboard contains column names as the first row followed by the data rows, tab-delimited.

2. **Given** the user selects a single column of cells, **When** they copy with headers, **Then** only that column's header and values are copied.

3. **Given** the user selects all cells (Ctrl+A), **When** they copy with headers, **Then** all column headers and all rows are included.

---

### User Story 4 - Completion Popup Ctrl Transparency (Priority: P3)

A developer is browsing the autocomplete list and needs to see the code hidden behind the popup. They hold the Ctrl key, and the popup becomes semi-transparent, allowing them to read the code underneath without dismissing the popup.

**Why this priority**: This is a standard SQL Prompt UX feature that improves the completion experience. It is a polish feature with low implementation complexity.

**Independent Test**: Trigger autocomplete, hold Ctrl, and verify the popup becomes semi-transparent. Release Ctrl and verify it returns to full opacity.

**Acceptance Scenarios**:

1. **Given** the completion popup is visible, **When** the user holds the Ctrl key, **Then** the popup opacity decreases to approximately 30% (semi-transparent).

2. **Given** the completion popup is semi-transparent due to Ctrl being held, **When** the user releases Ctrl, **Then** the popup returns to full opacity immediately.

3. **Given** the user is typing to filter the popup, **When** they press Ctrl+Space (to manually trigger), **Then** the Ctrl key does not trigger transparency during the shortcut chord.

---

### User Story 5 - Tab Color Gradient Option (Priority: P3)

A user prefers the SQL Prompt-style gradient coloring on tab headers. They open Settings > Tabs & UI and enable the "Use gradient colors" toggle. Tab header color bars now show a gradient (lighter at top, darker at bottom) instead of a flat solid color.

**Why this priority**: Visual polish that matches SQL Prompt's appearance. Low effort, improves aesthetics.

**Independent Test**: Enable gradient in settings, open a tab connected to a colored environment, verify the tab header shows a gradient.

**Acceptance Scenarios**:

1. **Given** "Use gradient colors" is enabled in settings, **When** a tab has an environment color assigned, **Then** the tab header color bar shows a gradient from lighter (top) to the base color (bottom).

2. **Given** "Use gradient colors" is disabled, **When** a tab has an environment color, **Then** the tab header shows a flat solid color (current behavior).

---

### User Story 6 - Excel 15+ Digit Precision Export (Priority: P3)

A developer exports query results containing large numeric IDs (like 16-digit identity values) to Excel. With the "Save 15+ digit numbers as text" option enabled, the export preserves the exact value rather than allowing Excel to round it to scientific notation.

**Why this priority**: This is a known Excel limitation that causes data corruption when pasting or exporting large numbers. SQL Prompt offers this as a grid export option.

**Independent Test**: Export a result set containing a 16+ digit number to Excel. Verify the value is preserved exactly, not rounded or converted to scientific notation.

**Acceptance Scenarios**:

1. **Given** "Save 15+ digit numbers as text" is enabled in Grid settings, **When** the user exports results to Excel containing a 16-digit number, **Then** the cell is formatted as text and the exact value is preserved.

2. **Given** the option is disabled, **When** the export occurs, **Then** Excel's default numeric formatting applies (may round large numbers).

---

### User Story 7 - Split Table Refactoring (Priority: P4)

A developer wants to normalize a wide table by splitting some columns into a new related table. They right-click the table, choose "Split Table", select which columns move to the new table, and the system generates: new table DDL, foreign key, data migration script, and updates to dependent objects.

**Why this priority**: This is an advanced refactoring that supports database normalization. It is a rare use case compared to the other gaps and has high implementation complexity, so it is lowest priority.

**Independent Test**: Select columns to split from a table, verify the generated script creates the new table, adds FK, migrates data, and updates dependent procedures.

**Acceptance Scenarios**:

1. **Given** the user invokes Split Table on a table, **When** they select columns to move, **Then** a preview shows: CREATE TABLE DDL for the new table, ALTER TABLE for FK constraint, INSERT INTO for data migration, and ALTER statements for dependent objects.

2. **Given** the user confirms the split, **When** the script is generated, **Then** it is opened in a new editor tab (not executed directly) for review.

3. **Given** a selected column is referenced by a foreign key from another table, **When** the split is previewed, **Then** the system warns about FK dependencies that will need manual adjustment.

---

### Edge Cases

- What happens when INSERT expansion encounters a table with no columns (computed-only or all-identity)? Show an empty column list with a comment.
- What happens when sp_executesql conversion encounters a NULL parameter value? Substitute with the literal `NULL`.
- What happens when sp_executesql has nested quotes in string parameters? Properly escape/unescape the quotes.
- What happens when Copy with Headers is used on a grid with no selection? Copy all rows with headers.
- What happens when Excel export encounters a mix of numeric and text in the same column? Apply text formatting only to cells exceeding 15 digits.

## Requirements *(mandatory)*

### Functional Requirements

**INSERT Metadata Comments:**
- **FR-001**: Expand Insert Columns MUST generate trailing comments on each column showing the data type and nullability (e.g., `-- nvarchar(40), not null`).
- **FR-002**: If a column has a default constraint, the comment MUST include the default value.
- **FR-003**: Identity columns MUST be excluded from the generated column list (or clearly marked as auto-generated).
- **FR-004**: A setting MUST exist to disable metadata comments (plain column list only).

**Convert sp_executesql:**
- **FR-005**: System MUST support converting `EXEC sp_executesql` statements to static SQL by substituting parameter values into the template.
- **FR-006**: The conversion MUST handle named parameters with explicit types (int, nvarchar, datetime, etc.).
- **FR-007**: String parameter values MUST retain proper quoting in the output.
- **FR-008**: The action MUST be available from the Actions List (lightbulb) when the cursor is on an sp_executesql call.

**Copy with Headers:**
- **FR-009**: The results grid context menu MUST include a "Copy with Headers" option.
- **FR-010**: Copied data MUST include column header names as the first row, followed by data rows.
- **FR-011**: The copy format MUST be tab-delimited (matching standard grid copy behavior).

**Ctrl Transparency:**
- **FR-012**: Holding the Ctrl key while the completion popup is visible MUST make the popup semi-transparent.
- **FR-013**: Releasing Ctrl MUST restore the popup to full opacity immediately.

**Tab Gradient:**
- **FR-014**: A "Use gradient colors" toggle MUST exist in Tab settings.
- **FR-015**: When enabled, tab color bars MUST render with a vertical gradient (lighter top, base color bottom).

**Excel Precision:**
- **FR-016**: A "Save 15+ digit numbers as text" toggle MUST exist in Grid settings.
- **FR-017**: When enabled, Excel exports MUST format cells containing 15+ digit numbers as text to prevent rounding.

**Split Table:**
- **FR-018**: System MUST allow the user to select columns from a table to move to a new table.
- **FR-019**: The generated script MUST include: new table DDL, foreign key, data migration INSERT, and dependent object updates.
- **FR-020**: The script MUST be opened in a new editor tab for review (no direct execution).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: INSERT expansion with metadata comments shows accurate data type and nullability for 100% of columns in the target table.
- **SC-002**: sp_executesql conversion produces runnable static SQL that executes identically to the original dynamic SQL.
- **SC-003**: Copy with Headers includes correct column names 100% of the time, matching the query result set's actual column order.
- **SC-004**: Ctrl transparency activates within 50ms of key press with no visible flicker.
- **SC-005**: Tab gradient renders smoothly with no visible banding artifacts.
- **SC-006**: Excel export preserves exact numeric values for all numbers exceeding 15 digits when the precision option is enabled.
- **SC-007**: Split Table generates a syntactically correct script that, when executed, produces the expected table structure and migrates all data.

## Assumptions

- INSERT metadata comments will use the schema cache (Phase A columns data) which already contains data types, nullability, and default values.
- sp_executesql conversion is a text transformation — it does not execute or validate the resulting SQL.
- Copy with Headers uses the same clipboard format as the existing Copy-As feature, just with a header row prepended.
- Tab gradient rendering uses standard WPF LinearGradientBrush — no custom rendering needed.
- Excel precision formatting uses the existing ClosedXML export path with cell format override.
- Split Table is a heavyweight refactoring using the existing preview dialog pattern.

## Scope Boundaries

**In Scope:**
- INSERT metadata comments in Expand Insert Columns
- Convert sp_executesql to static SQL (Actions List)
- Copy with Headers in grid context menu
- Ctrl transparency on completion popup
- Tab color gradient toggle
- Excel 15+ digit precision export
- Split Table refactoring (preview + script generation)

**Out of Scope:**
- AI features (separate phase)
- Team settings sync / Redgate Platform integration
- Any features already implemented in AKML SQL (preserving all existing functionality)
