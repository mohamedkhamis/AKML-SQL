# Feature Specification: SQL Prompt Parity — Remaining Gaps

**Feature Branch**: `013-sqlprompt-parity-gaps`  
**Created**: 2026-04-03  
**Status**: Draft  
**Input**: Gap analysis from `doc/AKML_SQL_Gap_Analysis_1.md` — remaining items not yet implemented

## Context

AKML-SQL has achieved full parity with RedGate SQL Prompt in 11 of 12 feature areas. Recent work closed major SQL History gaps (3-panel layout, starring, version timeline, execution capture, open/closed filters). This specification covers the **remaining gaps** identified in the analysis that have not yet been addressed.

### Already Completed (Not In Scope)

The following gap items were resolved in prior work and are excluded from this spec:
- SQL History 3-panel layout redesign (query list, version timeline, code preview)
- Starring/favorites with retention exemption
- Version history per query with timestamped snapshots
- Open/Closed filter tabs
- ExecutionCapture DTE hooks (BeforeExecute, AfterExecute, DocumentClosing)
- Top-level "AKML SQL" menu bar placement in SSMS 21/22
- Light theme as default with dark/system options
- CamelCase filtering in IntelliSense
- Space as configurable commit key
- Snippet Tab expansion
- Type-specific commit behavior (keyword trailing space, table auto-trigger)
- Stale completion cache clearing on popup dismiss

## Clarifications

### Session 2026-04-03

- Q: Should the formatter recognize SQL Prompt's original `-- SQL Prompt formatting off/on` markers in addition to AKML markers? → A: Yes, recognize both `-- AKML formatting off/on` AND `-- SQL Prompt formatting off/on` for migration compatibility.
- Q: Should the icon color update cover all 12 object types or only the 4 listed in the gap analysis? → A: Update all 12 object types to match SQL Prompt's full color palette.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Options Dialog Color Accuracy (Priority: P1)

A developer opens the AKML SQL Options dialog and expects a polished, consistent appearance that matches the quality standard set by SQL Prompt. Currently, the colors used in the Options dialog do not strictly follow the defined hex color palette, creating a visually inconsistent experience compared to SQL Prompt's crisp, professional dialog.

**Why this priority**: The Options dialog is the most frequently accessed settings surface. Visual inconsistency undermines perceived quality and professional trust.

**Independent Test**: Open the Options dialog in both light and dark themes and visually compare colors against the defined palette. All backgrounds, borders, selection highlights, and button colors match the specification.

**Acceptance Scenarios**:

1. **Given** the user opens Options in light theme, **When** the dialog renders, **Then** the dialog background is #F0F0F0, panel backgrounds are #FFFFFF, selected items highlight with #0078D4, and primary buttons use #0078D4.
2. **Given** the user opens Options in dark theme, **When** the dialog renders, **Then** the dialog background is #2D2D3B, panel backgrounds are #1E1E2E, unselected text is #8892A8, and borders use #3A3F4E.
3. **Given** the user switches between light and dark themes, **When** reopening the dialog, **Then** all colors update correctly without restart.

---

### User Story 2 - Suggestion Popup Icon Color Accuracy (Priority: P1)

A developer using IntelliSense sees colored icon badges next to each suggestion item. The current implementation uses a color scheme that differs from SQL Prompt's established palette. Users familiar with SQL Prompt expect specific colors: yellow for tables, teal for views, blue for columns, purple for procedures.

**Why this priority**: IntelliSense is the most used feature. Correct icon colors provide instant visual recognition of object types, reducing cognitive load.

**Independent Test**: Trigger IntelliSense in a query with tables, views, columns, and procedures visible. Verify each object type badge uses the exact SQL Prompt color specification.

**Acceptance Scenarios**:

1. **Given** a completion popup is shown with mixed object types, **When** the user examines icon badges, **Then** all 12 object types display their designated SQL Prompt colors: Table (T) yellow (#E5C04B), View (V) teal (#56B6C2), Column (C) blue (#61AFEF), Procedure (P) purple (#C678DD), Keyword (K), Snippet (S), Function (F), Schema (S), Database (D), Variable (@), Alias (A), and Parameter (P) — each with color-matched 20% opacity backgrounds.
2. **Given** the user is in dark theme, **When** the completion popup shows, **Then** icon badge colors remain the same (they are theme-independent, designed for dark popup background).

---

### User Story 3 - Unformat SQL Command (Priority: P2)

A developer wants to quickly strip all formatting from a SQL block to create a compact, single-line version for use in scripts, log messages, or command-line tools. SQL Prompt provides an "Unformat" action; AKML-SQL does not currently have this.

**Why this priority**: Completes the formatting command set. Unformat is a common utility action complementing Format Document.

**Independent Test**: Select a multi-line formatted SQL block, invoke Unformat, and verify it collapses to minimal whitespace on a single logical line.

**Acceptance Scenarios**:

1. **Given** a formatted SQL block spanning multiple lines, **When** the user invokes the Unformat command, **Then** the SQL is collapsed to minimal whitespace with no unnecessary line breaks, while preserving semantic correctness.
2. **Given** a SQL selection (not full document), **When** Unformat is invoked, **Then** only the selected region is unformatted; surrounding code is untouched.
3. **Given** the user invokes Unformat on already-compact SQL, **When** the command executes, **Then** the SQL is unchanged.

---

### User Story 4 - Disable Formatting Region Directives (Priority: P2)

A developer has carefully hand-formatted a complex SQL block (e.g., a pivot table or dynamic SQL) and wants to protect it from the auto-formatter. SQL Prompt uses `-- SQL Prompt formatting off/on` comment markers. AKML-SQL currently formats everything without respecting bypass markers.

**Why this priority**: Prevents the formatter from destroying intentional manual formatting. Critical for teams using Format on Save.

**Independent Test**: Place `-- AKML formatting off` and `-- AKML formatting on` markers (or the SQL Prompt equivalents) around a code block, run Format Document, and verify the marked region is preserved verbatim.

**Acceptance Scenarios**:

1. **Given** a SQL document with `-- AKML formatting off` before a block and `-- AKML formatting on` after it, **When** Format Document is invoked, **Then** everything outside the markers is formatted normally and everything inside is preserved exactly as written.
2. **Given** a SQL document using `-- SQL Prompt formatting off/on` markers (legacy syntax), **When** Format Document is invoked, **Then** the markers are recognized identically to the AKML equivalents.
3. **Given** multiple disabled regions in one document, **When** Format Document runs, **Then** each disabled region is independently preserved.
4. **Given** a `-- AKML formatting off` marker with no corresponding `on` marker, **When** Format Document runs, **Then** everything from the `off` marker to end of document is preserved (fail-safe).
5. **Given** Format on Save is enabled, **When** the user saves, **Then** disabled regions are still respected.

---

### User Story 5 - SQL History Advanced Search Syntax (Priority: P2)

A developer searching their query history wants to use wildcards (`*`, `?`), boolean operators (`OR`, `NOT`), exact phrase matching (`"create view"`), and CamelCase word boundary matching. Currently, search supports prefix-based filtering (`server:`, `database:`, etc.) but not these advanced patterns.

**Why this priority**: Power users rely on advanced search to quickly find specific queries in large history databases. This closes the remaining SQL History search gap.

**Independent Test**: Enter search queries using wildcards, boolean operators, and exact phrases in the History search bar and verify correct results are returned.

**Acceptance Scenarios**:

1. **Given** a history with queries containing "ProductCategory" and "ProductCatalog", **When** the user searches `Product*`, **Then** both queries appear in results.
2. **Given** a history search, **When** the user types `SELECT OR DELETE`, **Then** queries containing either keyword are shown.
3. **Given** a history search, **When** the user types `NOT DROP`, **Then** queries containing "DROP" are excluded from results.
4. **Given** a history search, **When** the user types `"create view"`, **Then** only queries containing that exact phrase are matched.
5. **Given** a history search, **When** the user types `PC`, **Then** queries containing words starting with P and C at CamelCase boundaries (e.g., "ProductCategory") are matched.

---

### User Story 6 - SQL History Search Match Highlighting (Priority: P3)

When a developer searches in the SQL History window, matched text in the code preview pane should be visually highlighted with a yellow/ochre background color, making it easy to spot where the search term appears in the query.

**Why this priority**: Visual feedback during search is important for usability but is an enhancement to already-functional search.

**Independent Test**: Search for a term in History, select a result, and verify the code preview highlights all occurrences of the search term with yellow/ochre background.

**Acceptance Scenarios**:

1. **Given** a search term is entered and a query is selected, **When** the code preview renders, **Then** all occurrences of the search term are highlighted with Yellow Ochre background (#F9A825 at 30% opacity).
2. **Given** the search term appears multiple times in the preview, **When** displayed, **Then** all occurrences are highlighted, not just the first.
3. **Given** the search is cleared, **When** the preview updates, **Then** all highlighting is removed.

---

### User Story 7 - Rename Closed Queries in History (Priority: P3)

A developer wants to give descriptive names to closed queries in the History list (e.g., "Migration script for Q4 release"). Currently there is no way to rename closed queries — they appear with auto-generated names only.

**Why this priority**: Organizational convenience for users who rely on History as a query library. Low effort, high user satisfaction.

**Independent Test**: Right-click a closed query in the History panel, select Rename, enter a new name, and verify it persists across sessions.

**Acceptance Scenarios**:

1. **Given** a closed query in the History list, **When** the user right-clicks and selects "Rename", **Then** an inline text editor appears pre-filled with the current name.
2. **Given** the user types a new name and presses Enter, **When** the rename completes, **Then** the query displays with the new name in the list.
3. **Given** the user renamed a query, **When** closing and reopening the History window, **Then** the custom name persists.
4. **Given** the user renames a query, **When** searching for the custom name, **Then** the query appears in search results.

---

### User Story 8 - Tab Color Propagation to Status Bar and Floating Windows (Priority: P3)

A developer working with multiple environments (Production, Staging, Dev) expects the environment color to be visible not only on the tab but also on the SSMS status bar at the bottom of each query pane and on the border of undocked/floating query windows.

**Why this priority**: Reinforces environment awareness in all window states. Important safety feature for Production environments but lower priority since tab coloring itself already works.

**Independent Test**: Open queries connected to different environments, verify status bar color matches tab color, then undock a query window and verify the floating window has a colored border.

**Acceptance Scenarios**:

1. **Given** a query tab connected to a Production server, **When** the user looks at the SSMS status bar, **Then** it displays the Production environment color (red) as a full-width color band.
2. **Given** a query tab is undocked into a floating window, **When** the window renders, **Then** a 3px colored border matching the environment color appears around the window frame.
3. **Given** the user switches between tabs with different environment colors, **When** focus changes, **Then** the status bar color updates to match the newly focused tab.

---

### User Story 9 - Installer Silent Mode Enhancements (Priority: P3)

An IT administrator deploying AKML-SQL across enterprise workstations needs robust silent installation support with automatic logging, pre-flight checks for running SSMS instances, and repair mode.

**Why this priority**: Enterprise deployment is important for adoption but does not affect core developer functionality.

**Independent Test**: Run the installer in silent mode with `/log` flag, verify a detailed log file is created, and verify that attempting installation while SSMS is running shows an appropriate warning.

**Acceptance Scenarios**:

1. **Given** the installer is run with `/VERYSILENT /log`, **When** the installation completes, **Then** a verbose log file is created at the specified or default location.
2. **Given** SSMS is currently running, **When** the installer starts, **Then** the user is warned that SSMS should be closed before proceeding.
3. **Given** an existing AKML-SQL installation, **When** the installer is run again, **Then** it performs an in-place upgrade/repair without requiring manual uninstall.

---

### User Story 10 - SQL Prompt Style Importer During Installation (Priority: P3)

A developer migrating from RedGate SQL Prompt to AKML-SQL wants their formatting styles and snippets automatically imported during installation, reducing setup friction.

**Why this priority**: Reduces migration friction but is a one-time convenience, not ongoing functionality.

**Independent Test**: Install AKML-SQL on a machine with SQL Prompt configurations present. Verify that formatting profiles and snippets are offered for import and correctly converted.

**Acceptance Scenarios**:

1. **Given** a fresh install on a machine with SQL Prompt settings in `%LocalAppData%\Red Gate\SQL Prompt`, **When** the installer runs, **Then** it offers to import existing formatting styles.
2. **Given** the user accepts the import, **When** import completes, **Then** SQL Prompt `.sqlpromptstyle` files are converted to AKML `.akmlstyle` format and placed in the user's profile directory.
3. **Given** no SQL Prompt installation is found, **When** the installer runs, **Then** the import step is silently skipped.

---

### Edge Cases

- What happens when the formatter encounters nested `-- AKML formatting off/on` markers? Inner markers are ignored; the outermost pair controls the region.
- What happens when the Unformat command is invoked on a string literal containing significant whitespace? Whitespace inside string literals is preserved.
- What happens when search highlighting encounters very long code previews? Highlighting applies to all visible matches; scrolling reveals additional highlights.
- What happens when renaming a query to a name already used by another query? Allowed — names are descriptive labels, not unique keys.
- What happens when the SQL Prompt style importer encounters a corrupted `.sqlpromptstyle` file? The file is skipped with a log warning; other files continue importing.
- What happens when a formatting region directive appears inside a string literal or block comment? Only standalone single-line comments are recognized as directives.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Options dialog MUST use the exact color palette: Light (#F0F0F0 dialog bg, #FFFFFF panel bg, #0078D4 selection/buttons) and Dark (#2D2D3B dialog bg, #1E1E2E panel bg, #8892A8 unselected text, #3A3F4E borders).
- **FR-002**: IntelliSense suggestion icon badges MUST use the SQL Prompt color scheme for all 12 object types: Table (#E5C04B), View (#56B6C2), Column (#61AFEF), Procedure (#C678DD), Keyword, Snippet, Function, Schema, Database, Variable, Alias, and Parameter — each with its designated color and 20% opacity background. Full color palette to be derived from SQL Prompt reference documentation during planning.
- **FR-003**: System MUST provide an "Unformat" command that strips all non-essential whitespace and line breaks from SQL, accessible via keyboard shortcut and command palette.
- **FR-004**: System MUST respect `-- AKML formatting off/on` and `-- SQL Prompt formatting off/on` comment directives (both syntaxes recognized for migration compatibility), preserving the enclosed region verbatim during any formatting operation.
- **FR-005**: SQL History search MUST support wildcard patterns (`*` for multiple characters, `?` for single character).
- **FR-006**: SQL History search MUST support boolean operators (`OR` to match either term, `NOT` to exclude a term).
- **FR-007**: SQL History search MUST support exact phrase matching using double quotes (`"exact phrase"`).
- **FR-008**: SQL History search MUST support CamelCase word boundary matching (e.g., "PC" matches "ProductCategory").
- **FR-009**: SQL History code preview MUST highlight all matching search terms with Yellow Ochre (#F9A825 at 30% opacity) background.
- **FR-010**: Users MUST be able to rename closed queries in the History window via right-click context menu.
- **FR-011**: Renamed query names MUST persist across sessions and be searchable.
- **FR-012**: Environment tab color MUST propagate to the SSMS status bar as a full-width color band.
- **FR-013**: Environment tab color MUST propagate to undocked/floating query window borders as a 3px colored outline.
- **FR-014**: Installer MUST create a verbose log file when the `/log` flag is passed.
- **FR-015**: Installer MUST detect running SSMS instances and warn the user before proceeding.
- **FR-016**: Installer MUST scan for existing SQL Prompt configurations and offer to import formatting styles and snippets.

### Key Entities

- **Formatting Directive**: A comment-based marker (`-- AKML formatting off/on` or `-- SQL Prompt formatting off/on`) that controls whether a region of SQL is processed by the formatter. Both syntaxes are recognized for SQL Prompt migration compatibility.
- **Search Token**: A parsed element of a History search query — can be a literal, wildcard pattern, boolean operator, or quoted phrase.
- **Query Custom Name**: A user-assigned descriptive label for a closed History entry, stored alongside the auto-generated title.
- **Style Import Profile**: A converted SQL Prompt `.sqlpromptstyle` file translated to AKML `.akmlstyle` format.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Options dialog passes visual audit — 100% of specified hex colors match in both light and dark themes.
- **SC-002**: IntelliSense icon badges visually match SQL Prompt reference for all 8+ object types.
- **SC-003**: Unformat command reduces a 50-line formatted SQL block to minimal whitespace in under 200ms.
- **SC-004**: Formatting off/on directives preserve enclosed content byte-for-byte through Format Document.
- **SC-005**: Advanced History search returns correct results for wildcard, boolean, exact phrase, and CamelCase queries within 500ms for databases with 10,000+ entries.
- **SC-006**: Search match highlighting is visible in the History code preview for all matched occurrences.
- **SC-007**: Renamed queries persist their custom names across application restarts.
- **SC-008**: Environment color is visible on status bar and floating window borders within 200ms of focus change.
- **SC-009**: Silent installer with `/log` flag produces a detailed log file covering all installation steps.
- **SC-010**: SQL Prompt style import correctly converts at least 80% of formatting options to AKML equivalents.

## Assumptions

- The existing ThemeManager infrastructure supports adding new color definitions without architectural changes.
- The existing formatting pipeline (7-stage) can be extended with a pre-processing step for formatting directives without disrupting current behavior.
- SQL History's SQLite FTS5 integration can be extended to support wildcard and boolean search syntax.
- SSMS exposes sufficient extensibility points (DTE, window frames) to inject status bar colors and floating window borders.
- The `.sqlpromptstyle` format is reverse-engineerable from existing SQL Prompt installations.
- The existing Inno Setup installer framework supports pre-flight process detection and conditional import steps.
- Icon badge colors are theme-independent (designed for the dark completion popup background used in both themes).
