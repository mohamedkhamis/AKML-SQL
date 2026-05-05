# Feature Specification: Multi-Area Bug Fixes and UI Polish

**Feature Branch**: `015-bug-fixes-polish`  
**Created**: 2026-04-14  
**Status**: Draft  
**Input**: AKML SQL Issue List — Installer, Query Page, SQL History, SQL Options

## Clarifications

### Session 2026-04-14

- Q: What is the exact version scheme format for the date-stamped build number? → A: `Major.YY.MMDDHHmm` — e.g., `1.26.04140511` where `26` = 2-digit year, `0414` = April 14, `0511` = 05:11.
- Q: How should AI provider API keys be stored? → A: Windows Credential Manager (DPAPI) — OS-encrypted per-user credential store; keys must not appear in `config.json`.
- Q: Should Document Outline update in real-time as the user types, or only on demand? → A: On-demand — outline reflects document state when opened or when the user clicks a Refresh button; no background re-parsing on keystrokes.
- Q: What is the maximum number of SQL History entries to retain? → A: 1,000 entries, rolling — oldest entry evicted automatically when the limit is reached.
- Q: How should AI Assistance usage instructions be presented in SQL Options? → A: Inline help text — a short guidance paragraph shown directly below each provider's API key field in the Options panel; no external links required.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - IntelliSense Autocomplete for UPDATE and ALTER TABLE (Priority: P1)

A developer typing an `UPDATE` statement wants column name suggestions to appear after `SET` so they can select the correct column without memorising schema. Similarly, when typing `ALTER TABLE … ALTER COLUMN`, they expect to see existing column names so they can pick the right one. Currently neither scenario produces any completions.

**Why this priority**: IntelliSense completion is the core daily-use feature. Broken completion for two of the most common DML/DDL verbs degrades every editing session.

**Independent Test**: Open a query window connected to a database, type `UPDATE Users SET ` and verify that a completion list showing column names of `Users` appears. Then type `ALTER TABLE Users ALTER COLUMN ` and verify that column names appear.

**Acceptance Scenarios**:

1. **Given** a connected query window with a schema-loaded database, **When** the user types `UPDATE <table> SET `, **Then** the completion list shows all column names for that table.
2. **Given** a connected query window, **When** the user types `UPDATE <table> SET <col> = @val, `, **Then** additional column completions appear for the second assignment.
3. **Given** a connected query window, **When** the user types `ALTER TABLE <table> ALTER COLUMN `, **Then** column names of that table appear in the completion list.
4. **Given** a table name that does not exist in the schema cache, **When** the user triggers completion, **Then** the list is empty (no crash, no stale data).

---

### User Story 2 - Analysis Button Produces Visible Results and Logs (Priority: P2)

A developer clicks the "Analysis" button in the query toolbar and expects the code analysis rules to run against the open SQL document, displaying findings. Currently clicking "Analysis" produces no visible output and generates no log entries.

**Why this priority**: Analysis is a core feature. Being completely silent breaks trust and makes the feature unusable.

**Independent Test**: Click "Analysis" on a query containing a known rule violation (e.g., `SELECT *`). A results panel or inline indicators must appear, and the log file must contain an entry for the analysis request.

**Acceptance Scenarios**:

1. **Given** a query document with a rule violation, **When** the user clicks "Analysis", **Then** findings appear in a results panel (or inline) within 5 seconds.
2. **Given** the user clicks "Analysis", **When** the operation runs, **Then** at least one log entry is written recording the analysis attempt.
3. **Given** a syntactically invalid query, **When** "Analysis" is clicked, **Then** a descriptive error message is shown rather than silent failure.
4. **Given** a clean query with no violations, **When** "Analysis" is clicked, **Then** a "No issues found" message is displayed.

---

### User Story 3 - Search Uses Active Connection (Priority: P3)

A developer with an active database connection opens the Search panel from the query toolbar and types a search term. Instead of searching, the panel shows "No active database connection for this session" even though a connection is established. The search must correctly detect and use the active connection.

**Why this priority**: The Search feature is completely non-functional when a connection exists, making it inaccessible in the primary workflow.

**Independent Test**: Open a query window, connect to a database, click "Search", type a table or column name — results must appear using that connection.

**Acceptance Scenarios**:

1. **Given** a query window with an active database connection, **When** the user opens Search and types a term, **Then** search results are returned using the active connection.
2. **Given** a query window with no connection, **When** the user opens Search, **Then** the "No active database connection" message is shown (correct behavior in this case).
3. **Given** a connection that is lost mid-search, **When** results cannot be retrieved, **Then** a clear reconnection prompt is shown rather than a stale error.

---

### User Story 4 - Delete Warning Triggers for Table Deletion (Priority: P4)

A developer executes a `DROP TABLE` statement and expects the safety warning dialog to appear before execution. The warning currently does not appear for table deletions, creating a risk of accidental data loss.

**Why this priority**: Safety warnings prevent destructive, irreversible operations. A missing warning for table deletion is a functional regression in a critical safety feature.

**Independent Test**: Execute `DROP TABLE dbo.Users` — the safety confirmation dialog must appear before the statement runs.

**Acceptance Scenarios**:

1. **Given** a query containing `DROP TABLE`, **When** the user executes the query, **Then** the safety warning dialog appears before execution proceeds.
2. **Given** the safety dialog is shown, **When** the user clicks "Cancel", **Then** the DROP statement is not executed.
3. **Given** the safety dialog is shown, **When** the user types the confirmation phrase and clicks "Execute", **Then** the DROP statement runs.
4. **Given** a query containing only `SELECT` statements, **When** executed, **Then** no safety dialog appears.

---

### User Story 5 - Star Badge Count in SQL History (Priority: P5)

A developer stars queries in SQL History to bookmark important ones. The star icon on individual queries toggles correctly, and the "Starred" filter shows the right results, but the badge count on the "All" view does not update to reflect how many queries are starred. The badge must stay in sync.

**Why this priority**: An incorrect badge breaks trust in the history feature and makes it hard to know at a glance how many queries are bookmarked.

**Independent Test**: Star three queries. The badge count on the history header or "All" tab must show "3". Un-star one; the badge must show "2".

**Acceptance Scenarios**:

1. **Given** no starred queries, **When** the user stars a query, **Then** the starred badge count increments by 1 immediately.
2. **Given** 3 starred queries, **When** the user un-stars one, **Then** the badge count decrements to 2.
3. **Given** starred queries, **When** the history panel is closed and reopened, **Then** the badge count is correct on reload.

---

### User Story 6 - Advanced Search in SQL History (Priority: P6)

A developer uses the Advanced Search feature in SQL History to filter by date range, database, or keyword. Currently Advanced Search produces no results or does nothing. The feature must return matching history entries.

**Why this priority**: Advanced Search is the primary way to find past queries in large history sets. Non-functional search makes history unusable at scale.

**Independent Test**: Run a query, then use Advanced Search with a keyword from that query — the query must appear in results.

**Acceptance Scenarios**:

1. **Given** history entries containing keyword "SELECT", **When** the user types "SELECT" in Advanced Search, **Then** all matching entries are returned.
2. **Given** a date-range filter applied, **When** the user searches, **Then** only queries executed within that range are shown.
3. **Given** no matching results, **When** the user searches, **Then** an empty-state message is shown (no error, no hang).

---

### User Story 7 - Schema Progress as Bottom-Right Notification Box (Priority: P7)

A developer loading schema sees a spinner at line 1 of the query document, which is distracting and visually intrusive. The schema-load progress indicator must move to a non-intrusive notification box in the bottom-right corner of the SQL query document area.

**Why this priority**: The current placement interrupts editing and obscures code. A notification-style indicator is a standard IDE pattern that preserves the editing experience.

**Independent Test**: Connect to a database and observe schema loading. A small notification box with a spinner must appear in the bottom-right corner of the editor; no indicator must appear at line 1.

**Acceptance Scenarios**:

1. **Given** schema is loading, **When** the editor is open, **Then** a notification box with a spinner appears in the bottom-right corner of the document.
2. **Given** schema loading completes, **When** the indicator disappears, **Then** the notification box fades out smoothly.
3. **Given** a large schema taking more than 5 seconds, **When** the notification is visible, **Then** it shows a status label (e.g., "Loading schema…") in addition to the spinner.
4. **Given** the notification box is visible, **When** the user types or scrolls, **Then** the notification does not block or overlap the text cursor or selection.

---

### User Story 8 - Options Dark Theme: Readable Text in Dropdowns and Buttons (Priority: P8)

A developer using the Dark theme in SQL Options notices that dropdown menu text (e.g., the "Dark" label in the theme dropdown) is faded and hard to read. Additionally, OK, Cancel, Import, and Export button labels become faded when hovered. All text must remain high-contrast and legible in all states.

**Why this priority**: Unreadable text in the settings dialog blocks users from configuring the tool and degrades accessibility.

**Independent Test**: Open SQL Options with Dark theme active. Hover over each dropdown option and each button. All text must remain clearly legible.

**Acceptance Scenarios**:

1. **Given** Dark theme is selected, **When** the user opens a dropdown in Options, **Then** all option labels are rendered in high-contrast text, not faded.
2. **Given** Dark theme is active, **When** the user hovers over OK, Cancel, Import, or Export buttons, **Then** the button label remains fully readable.
3. **Given** a theme switch from Light to Dark while Options is open, **When** the theme updates, **Then** all text immediately re-renders at correct contrast.

---

### User Story 9 - Query Rename in SQL History (Priority: P9)

A developer wants to give a meaningful name to a saved query in SQL History (similar to how Redgate SQL Prompt lets users label saved queries). Currently the query name label is not user-friendly and cannot be edited. Users must be able to rename queries with descriptive names.

**Why this priority**: Named queries are the foundation of an organised history. Without rename support, all queries appear with auto-generated or timestamp-only labels.

**Independent Test**: Right-click or double-click a query name in History and rename it to "My Test Query". Verify the new name persists after closing and reopening the history panel.

**Acceptance Scenarios**:

1. **Given** a query in History, **When** the user triggers rename (double-click or context menu), **Then** an inline edit field appears showing the current name.
2. **Given** the edit field is active, **When** the user types a new name and confirms, **Then** the query displays the new name.
3. **Given** a renamed query, **When** the history panel is closed and reopened, **Then** the custom name is preserved.
4. **Given** the user presses Escape during rename, **When** the edit is cancelled, **Then** the original name is restored.

---

### User Story 10 - Document Outline Shows SQL Structure (Priority: P10)

A developer clicks "Document Outline" in the query toolbar and sees an empty window. The Document Outline must parse the open SQL document and display a structured list of named objects: stored procedure definitions, function definitions, CTE names, batch separators (GO), and top-level statement types. This gives developers a navigable map of complex SQL files.

**Why this priority**: The feature is already surfaced in the UI; an empty window is worse than no feature. Fixing it turns dead UI into genuine navigation value.

**Independent Test**: Open a SQL file containing at least one CTE and one stored procedure definition. Click "Document Outline" to open the panel. The outline must show the CTE name and procedure name as clickable nodes. Edit the document to add another CTE, click Refresh — the new CTE must appear in the outline.

**Acceptance Scenarios**:

1. **Given** a SQL document with a named CTE (`WITH MyData AS ...`), **When** Document Outline is opened, **Then** "MyData" appears as a node in the outline.
2. **Given** a SQL document with `CREATE PROCEDURE dbo.GetOrders`, **When** Document Outline is opened, **Then** "dbo.GetOrders" appears as a node.
3. **Given** a node is clicked in the outline, **When** the user clicks it, **Then** the editor scrolls to and highlights that definition.
4. **Given** an empty or plain SELECT document, **When** Document Outline is opened, **Then** the panel shows an empty-state message (not a blank window).

---

### User Story 11 - Installer: Remove Desktop Shortcut Option (Priority: P11)

A developer running the AKML SQL installer is presented with a checkbox to create a desktop shortcut. Since AKML SQL is a VS/SSMS extension (not a standalone app), a desktop shortcut has no purpose and confuses users. The installer must not offer this option.

**Why this priority**: Removing a confusing option improves installer clarity and reduces post-install clutter.

**Independent Test**: Run the installer — no "Create desktop shortcut" checkbox must appear on any page.

**Acceptance Scenarios**:

1. **Given** the installer is launched, **When** the user walks through all pages, **Then** no desktop shortcut checkbox is present.
2. **Given** a silent install with any previous shortcut flags, **When** installation completes, **Then** no desktop shortcut is created.

---

### User Story 12 - Version Scheme: Date-Based Build Number (Priority: P12)

The current version number `1.0.0` is not descriptive. The version scheme must change to `Major.YY.MMDDHHmm` so users and support staff can immediately identify the build age without consulting a changelog. For example, a build on 2026-04-14 at 05:11 produces version `1.26.04140511`.

**Why this priority**: Build-stamped versions make debugging and support communication faster.

**Independent Test**: Install the extension and check the version in the About dialog and in the vsixmanifest. The version must follow the date-stamped format corresponding to the build date.

**Acceptance Scenarios**:

1. **Given** the extension is built on April 14 at 05:11, **When** the About dialog is shown, **Then** the version reads in the agreed date-stamped format.
2. **Given** two builds from different dates, **When** their versions are compared, **Then** the later build has a numerically greater version number.
3. **Given** the installer package, **When** inspected, **Then** the vsixmanifest version matches the About dialog version.

---

### User Story 13 - AI Assistance Documentation (Priority: P13)

A developer opens the AI Assistance section of SQL Options and has no guidance on how to configure it or what providers are supported. The section must include clear, concise instructions showing how to connect Claude and Gemini, including where to obtain API keys and example usage patterns.

**Why this priority**: Without documentation, AI features are inaccessible even when correctly wired up.

**Independent Test**: Open SQL Options → AI Assistance. Instructions for at least two providers (Claude, Gemini) with example configuration steps must be visible.

**Acceptance Scenarios**:

1. **Given** the AI Assistance settings panel, **When** opened, **Then** inline help text is shown below each provider's API key field, including where to obtain the key and an example model name — all within the panel, no browser required.
2. **Given** the inline help is shown, **When** a user follows the steps, **Then** they can configure a provider without leaving the application.
3. **Given** an invalid API key, **When** the user saves, **Then** a validation message indicates the key format is incorrect.

---

### User Story 14 - Installer: Icon and Banner Design (Priority: P14)

The installer currently uses placeholder or default icons and banners. A branded icon (displayed in the Windows installer wizard sidebar) and a header banner must be designed and integrated so the installer looks professional.

**Why this priority**: First impressions matter; a polished installer builds confidence in the product.

**Independent Test**: Run the installer — the wizard displays the AKML SQL branded icon and banner on all pages.

**Acceptance Scenarios**:

1. **Given** the installer is launched, **When** the wizard opens, **Then** a branded banner image appears in the header area of each page.
2. **Given** the installer is shown in Windows Explorer, **When** the EXE icon is visible, **Then** the AKML SQL icon (not a default or blank icon) is displayed.

---

### Edge Cases

- What happens when schema is not yet loaded and the user triggers UPDATE/ALTER TABLE completion — no crash; empty list with a "Schema loading…" hint.
- What happens when a query is starred and then the history is cleared — badge resets to 0.
- What happens if the Document Outline SQL parser encounters a syntax error — the outline shows what was parsed successfully before the error.
- What happens if the AI provider API key is empty — the field should be highlighted with a validation message on save.
- What happens when the dark-theme option dialog is opened before any theme is applied — defaults to the system/current theme and all text is legible.

## Requirements *(mandatory)*

### Functional Requirements

**IntelliSense / Autocomplete**

- **FR-001**: The system MUST provide column-name completions for the `SET` clause of `UPDATE <table> SET ` statements when the table is present in the schema cache.
- **FR-002**: The system MUST provide column-name completions after `ALTER TABLE <table> ALTER COLUMN ` when the table is present in the schema cache.
- **FR-003**: Column completions for UPDATE and ALTER TABLE MUST include column name, data type, and nullable indicator in the completion item detail.

**Analysis**

- **FR-004**: Clicking the "Analysis" toolbar button MUST trigger code analysis of the active query document and display findings (or a "No issues found" message) within 5 seconds.
- **FR-005**: Every analysis invocation MUST produce at least one log entry (start, completion, or error) in the application log file.

**Search**

- **FR-006**: The Search panel MUST detect the active database connection for the current query window and use it for all search queries.
- **FR-007**: The Search panel MUST display an "No active connection" message only when no connection is genuinely established for that window.

**Safety / Delete Warning**

- **FR-008**: The safety warning dialog MUST appear when a query containing `DROP TABLE` is executed, matching the existing behavior for other destructive statements.
- **FR-009**: If the user cancels the safety dialog, the `DROP TABLE` statement MUST NOT be executed.

**SQL History — Star Badge**

- **FR-010**: The starred-query badge count MUST update immediately when a query is starred or un-starred, without requiring a panel refresh.
- **FR-011**: The badge count MUST persist correctly across panel close/reopen cycles.

**SQL History — Advanced Search**

- **FR-012**: Advanced Search MUST filter history entries by keyword (matching query text), date range, and database name, individually or combined.
- **FR-013**: Advanced Search with no results MUST display an empty-state message.
- **FR-013a**: SQL History MUST retain a maximum of 1,000 entries. When the limit is reached, the oldest entry (by execution timestamp) is evicted automatically to make room for the new entry. Starred entries are NOT exempt from eviction.

**SQL History — Query Rename**

- **FR-014**: Users MUST be able to rename any history entry via double-click or a context-menu "Rename" action.
- **FR-015**: Custom query names MUST be persisted across session restarts.

**Schema Progress Indicator**

- **FR-016**: The schema-loading indicator MUST appear as a notification box in the bottom-right corner of the active SQL editor document, not at line 1 of the document margin.
- **FR-017**: The notification box MUST include a spinner and a status label (e.g., "Loading schema…").
- **FR-018**: The notification box MUST not overlap the text cursor and MUST fade out on schema load completion.

**Document Outline**

- **FR-019**: The Document Outline panel MUST parse the active SQL document and display named structural elements: CTEs, stored procedure/function definitions, batch separators, and top-level statement types. Parsing is triggered on panel open and on explicit Refresh (not on every keystroke).
- **FR-019a**: The Document Outline panel MUST include a visible Refresh button; clicking it re-parses the current document state and updates the outline.
- **FR-020**: Clicking a node in the Document Outline MUST scroll the editor to and highlight the corresponding code location.
- **FR-021**: An empty or unstructured document MUST show a descriptive empty-state message in the outline panel (not a blank window).

**Options — Dark Theme**

- **FR-022**: In the Dark theme, all dropdown option labels in the SQL Options dialog MUST render at full contrast (not faded) in both normal and focused states.
- **FR-023**: OK, Cancel, Import, and Export button labels MUST remain fully readable on hover in all supported themes.

**Installer — Desktop Shortcut**

- **FR-024**: The installer MUST NOT present a "Create desktop shortcut" option on any installer page.
- **FR-025**: No desktop shortcut MUST be created during silent or attended installation.

**Installer — Version Scheme**

- **FR-026**: The product version MUST follow the format `Major.YY.MMDDHHmm` (e.g., `1.26.04140511` for a build on 2026-04-14 at 05:11), enabling build identification without a changelog. The scheme must be numerically monotonic so newer builds always have a greater version number than older builds.
- **FR-027**: The version displayed in the About dialog, the vsixmanifest, and the installer must be identical.

**Installer — Icon and Banner**

- **FR-028**: The installer wizard MUST display a branded AKML SQL banner image on all pages.
- **FR-029**: The installer executable MUST carry the AKML SQL application icon (visible in Windows Explorer and the taskbar during installation).

**AI Assistance Documentation**

- **FR-030**: The AI Assistance section of SQL Options MUST display inline help text directly below each provider's API key field. The help text MUST include: the provider name, where to obtain an API key (URL or navigation path), and an example model name. No external browser link is required — all guidance is shown within the panel.
- **FR-031**: The AI Assistance section MUST validate that a provided API key is non-empty before saving; an invalid-format key MUST show an inline validation message.
- **FR-032**: API keys for AI providers MUST be stored in the Windows Credential Manager (DPAPI-encrypted, per-user). Keys MUST NOT be written to `config.json` or any other plain-text file. On retrieval, the key is read from the credential store and held in memory only for the duration of the request.

### Key Entities

- **History Entry**: A recorded query execution — has text, execution timestamp, database name, connection info, custom name (optional), and starred flag. Maximum 1,000 entries retained (rolling); oldest evicted first when limit is reached.
- **Star Badge**: A count display attached to the history panel showing total starred entries; must stay in sync with the starred flag on each History Entry.
- **Schema Cache**: The in-memory representation of tables, columns, and their metadata — drives completion for UPDATE, ALTER TABLE, and other schema-aware features.
- **Analysis Finding**: A single rule-violation result from the Analysis engine — has rule ID, severity, message, and source location.
- **AI Provider Configuration**: A settings record containing provider name (Claude / Gemini), optional model/endpoint override stored in `config.json`, and an API key stored exclusively in the Windows Credential Manager (never in config files).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: UPDATE SET and ALTER TABLE column completions appear within 500ms of the cursor reaching the completion trigger position when schema is cached.
- **SC-002**: Clicking "Analysis" produces visible results or a "No issues found" state within 5 seconds on documents up to 2,000 lines.
- **SC-003**: Search returns results within 3 seconds for any text query when connected to a database with up to 500 tables.
- **SC-004**: The safety warning dialog appears 100% of the time before any `DROP TABLE` execution (zero missed warnings).
- **SC-005**: Star badge count reflects the correct starred count immediately (within one UI render cycle) after any star/un-star action.
- **SC-006**: Advanced Search returns accurate results for keyword, date-range, and database-name filters, individually and combined, across up to 1,000 history entries within 2 seconds.
- **SC-007**: The schema progress notification renders in the bottom-right corner and does not overlap the text area on any standard monitor resolution (1080p and above).
- **SC-008**: Document Outline correctly identifies and lists all named CTEs and procedure/function definitions in a document containing at least 5 such elements.
- **SC-009**: All dropdown labels and button labels in SQL Options remain legible (meets WCAG AA contrast ratio of 4.5:1) in Dark theme, in all interaction states.
- **SC-010**: A developer can rename a history query, close and reopen the history panel, and see the custom name persist — 100% of the time.
- **SC-011**: The installer completes without presenting a desktop shortcut option in attended or silent modes.
- **SC-012**: The version shown in the About dialog matches the installer EXE and vsixmanifest exactly.

## Assumptions

- Version scheme confirmed (Q1 clarification): `Major.YY.MMDDHHmm` — e.g., `1.26.04140511` for a 2026-04-14 05:11 build.
- "Delete warning" refers specifically to `DROP TABLE`; other DDL statements (`DROP DATABASE`, `TRUNCATE TABLE`) already have working warnings or will be addressed in a follow-up.
- Document Outline parses the active document in memory (no disk save required); SQL files with `#include` or multi-file dependencies are out of scope for this iteration.
- The AI Assistance section documentation is inline help text within the Options panel (confirmed Q5); no external links or browser navigation required.
- Installer icon and banner assets will be provided by the design owner; this spec covers integration requirements, not design deliverables.
- Query rename applies to manually saved history entries; auto-truncated labels for entries without explicit saves follow existing naming rules unless renamed.
