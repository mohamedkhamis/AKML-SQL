# Feature Specification: SQL History & Tab Management

**Feature Branch**: `007-sql-history-tabs`
**Created**: 2026-03-24
**Status**: Draft
**Input**: Phase 7 PRD — SQL History & Tab Management for SSMS

## Clarifications

### Session 2026-03-24

- Q: When a user executes a batch containing multiple SQL statements via F5, should each statement be recorded individually or as one entry? → A: One history entry per execution action (the full editor/selection text is recorded as a single entry, regardless of how many statements it contains).
- Q: Should session recovery files store saved credentials for seamless reconnection, or only connection identifiers? → A: Store only connection identifiers (server, database, auth type). Never store passwords or credentials in session recovery files. On restore, Windows Auth connections reconnect automatically; SQL Auth connections prompt the user to re-authenticate.
- Q: How should the AES-256 encryption key for the history database be managed? → A: Windows DPAPI — encryption is transparent, tied to the Windows user account, requires no user interaction or passphrase.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Automatic SQL Execution Recording (Priority: P1)

As a database developer, every SQL statement I execute is automatically captured with full execution context (server, database, username, duration, row count, success/failure status) so that I never lose track of what I ran, when, and where.

**Why this priority**: This is the foundational capability. Without automatic capture, no other history feature works. Users' most common frustration is "I ran a query earlier but can't remember what it was" — this eliminates that problem entirely.

**Independent Test**: Can be fully tested by executing several SQL statements and verifying each is recorded with all context fields populated. Delivers immediate value as a persistent execution log.

**Acceptance Scenarios**:

1. **Given** a user executes a SELECT statement via F5 on a development server, **When** the statement completes successfully, **Then** the history records the full SQL text, server name, database, username, timestamp, duration in milliseconds, row count, and "Success" status.
2. **Given** a user executes an INSERT statement that fails with a constraint violation, **When** the execution returns an error, **Then** the history records the SQL text, all context fields, "Error" status, and the full error message.
3. **Given** a user executes a query from a saved file, **When** the execution completes, **Then** the history records the file path as the source. If the query is from an unsaved tab, the source is recorded as "Unsaved Query."
4. **Given** a user cancels a long-running query, **When** the cancellation completes, **Then** the history records the SQL text with "Cancelled" status and the duration up to the cancellation point.
5. **Given** the history database has reached the configured maximum entries, **When** a new statement is executed, **Then** the oldest non-favorited entry is automatically purged to make room.

---

### User Story 2 - Search and Browse Execution History (Priority: P1)

As a database developer, I can open a History panel and search through all my past SQL executions using full-text search, and filter by server, database, status, or date range, so I can quickly find a query I ran previously.

**Why this priority**: Capture without retrieval is useless. Search and browse is the primary way users interact with their history and is co-equal in importance with recording.

**Independent Test**: Can be tested by populating history with diverse entries and verifying search returns correct results across all filter combinations. Delivers value as a searchable query archive.

**Acceptance Scenarios**:

1. **Given** the history panel is open, **When** a user types a keyword into the search bar, **Then** matching entries appear instantly with the search term highlighted in the SQL text.
2. **Given** history contains entries from multiple servers, **When** the user selects a specific server from the server filter, **Then** only entries from that server are displayed.
3. **Given** history contains both successful and failed queries, **When** the user filters by "Error" status, **Then** only failed executions are shown, each displaying its error message.
4. **Given** history contains entries spanning several months, **When** the user selects a custom date range, **Then** only entries within that range are shown, grouped by day.
5. **Given** identical queries were executed multiple times, **When** viewing history with deduplication enabled, **Then** duplicate entries are grouped showing the execution count and the most recent execution timestamp.

---

### User Story 3 - Restore and Re-execute from History (Priority: P2)

As a database developer, I can select any history entry and open it in a new query tab, copy its SQL to the clipboard, or re-execute it against the current connection, so I can quickly reuse past work.

**Why this priority**: This turns history from a read-only log into an actionable productivity tool. Users frequently need to re-run or adapt previous queries.

**Independent Test**: Can be tested by selecting a history entry and verifying each action (open, copy, re-execute) works correctly with the expected connection context. Delivers value as query reuse.

**Acceptance Scenarios**:

1. **Given** a user selects a history entry and clicks "Open in New Tab," **When** the new tab opens, **Then** it contains the exact SQL text from the history entry.
2. **Given** a user selects a history entry and clicks "Copy SQL," **When** the clipboard is checked, **Then** it contains the full SQL text.
3. **Given** a user selects a history entry and clicks "Re-execute," **When** the query runs, **Then** it executes against the current active connection, and a new history entry is created for this execution.
4. **Given** a user selects two history entries and clicks "Compare," **When** the comparison view opens, **Then** a side-by-side diff of the two SQL texts is displayed with differences highlighted.

---

### User Story 4 - Tab Coloring by Server Environment (Priority: P2)

As a database developer, my SSMS query tabs are automatically color-coded based on the connected server's environment (production = red, staging = yellow, development = green, etc.) so I can visually distinguish which environment I'm working in and avoid accidental production changes.

**Why this priority**: Tab coloring is the single most impactful safety feature — a visual indicator that prevents the most dangerous class of mistake (running queries against the wrong environment). Ranked P2 only because it's independent of history.

**Independent Test**: Can be tested by connecting tabs to different server environments and verifying each tab displays the correct color and environment label. Delivers immediate visual safety value.

**Acceptance Scenarios**:

1. **Given** a user opens a query tab connected to a server whose name contains "PROD," **When** the tab renders, **Then** its background is colored red and displays a "PRODUCTION" environment label.
2. **Given** a user opens a query tab connected to a server whose name contains "DEV," **When** the tab renders, **Then** its background is colored green and displays a "DEV" environment label.
3. **Given** a user connects a tab to a server matching an Azure pattern (*.database.windows.net), **When** the tab renders, **Then** its background is colored blue and displays an "AZURE" label.
4. **Given** a user has configured custom environment rules, **When** a tab connects to a server matching a custom pattern, **Then** the tab displays the user-defined color and label.
5. **Given** a user connects to a server that matches no environment pattern, **When** the tab renders, **Then** it displays with the default (no color) appearance.

---

### User Story 5 - Session Recovery After Crash (Priority: P2)

As a database developer, all my open query tabs are periodically auto-saved so that if SSMS crashes or is accidentally closed, I can recover my entire session (all open tabs with their content and connection info) on next startup.

**Why this priority**: Data loss from SSMS crashes is the second most common frustration. Session recovery directly addresses "I accidentally closed SSMS and lost all my unsaved queries."

**Independent Test**: Can be tested by opening multiple tabs with content, simulating an abnormal termination, and verifying all tabs are offered for restoration on next startup. Delivers value as crash protection.

**Acceptance Scenarios**:

1. **Given** SSMS has multiple open query tabs with unsaved content, **When** the auto-save interval elapses, **Then** all tab contents and connection metadata are persisted to the session recovery store.
2. **Given** SSMS was terminated abnormally (crash or forced close), **When** SSMS restarts, **Then** a recovery dialog appears listing all tabs from the previous session with their titles and timestamps.
3. **Given** the recovery dialog is displayed, **When** the user selects specific tabs and confirms, **Then** only the selected tabs are restored with their original content and connection information.
4. **Given** session recovery is configured to "always" restore, **When** SSMS starts after any termination, **Then** all previous tabs are automatically restored without a dialog prompt.
5. **Given** the maximum retained sessions is 5, **When** a 6th session is saved, **Then** the oldest session is automatically removed.

---

### User Story 6 - Restore Recently Closed Tabs (Priority: P3)

As a database developer, I can press Ctrl+Shift+T to reopen the most recently closed tab (like in a web browser), or view a list of recently closed tabs to selectively restore any of them.

**Why this priority**: Complements session recovery with in-session tab restoration. Lower priority because it addresses a less catastrophic scenario than full crash recovery.

**Independent Test**: Can be tested by closing tabs and verifying Ctrl+Shift+T reopens them in reverse order, and the recently-closed list shows correct entries. Delivers value as undo-close.

**Acceptance Scenarios**:

1. **Given** a user closes a query tab, **When** they press Ctrl+Shift+T, **Then** the most recently closed tab reopens with its original content and connection.
2. **Given** multiple tabs have been closed, **When** the user presses Ctrl+Shift+T repeatedly, **Then** tabs reopen in reverse chronological order (most recent first).
3. **Given** the user opens the "Recently Closed" list, **When** they select a specific tab from the list, **Then** that tab is restored and removed from the list.
4. **Given** 20 tabs have been closed (the configured maximum), **When** a 21st tab is closed, **Then** the oldest entry is removed from the recently-closed list to make room.

---

### User Story 7 - Execution Safety Warnings (Priority: P2)

As a database developer, I receive warning dialogs before executing dangerous operations — such as DML/DDL on production servers, DELETE/UPDATE without WHERE, DROP statements, or TRUNCATE — so that I am protected from accidental destructive actions.

**Why this priority**: Critical safety feature that prevents data loss. Ranked alongside tab coloring as a core safety mechanism.

**Independent Test**: Can be tested by attempting each dangerous operation and verifying the appropriate warning dialog appears and blocks execution until confirmed. Delivers value as a safety net.

**Acceptance Scenarios**:

1. **Given** a user is connected to a production server, **When** they attempt to execute a DELETE statement, **Then** a modal confirmation dialog appears stating "You are about to execute on PRODUCTION server [server-name]. Proceed?" and execution is blocked until confirmed or cancelled.
2. **Given** a user writes `DELETE FROM dbo.Orders` without a WHERE clause, **When** they execute, **Then** an error-level warning appears requiring explicit confirmation before proceeding.
3. **Given** a user writes `UPDATE dbo.Customers SET Status = 'Active'` without a WHERE clause, **When** they execute, **Then** the same error-level warning appears as for DELETE without WHERE.
4. **Given** a user writes `DROP TABLE dbo.Orders`, **When** they execute, **Then** a confirmation dialog appears requiring the user to type the object name "dbo.Orders" to confirm the action.
5. **Given** a user writes `TRUNCATE TABLE dbo.Logs`, **When** they execute, **Then** a confirmation dialog appears before execution proceeds.

---

### User Story 8 - Transaction Reminder (Priority: P3)

As a database developer, when I have an open (uncommitted) transaction in a query tab, the system visually reminds me with a status bar indicator showing elapsed time and periodically prompts me to commit or rollback, so I don't accidentally leave transactions open and cause locking issues.

**Why this priority**: Important for preventing blocking and locking issues, but affects fewer users than the core safety warnings. Valuable for production database work.

**Independent Test**: Can be tested by opening a BEGIN TRAN without committing and verifying the status bar indicator appears with a timer, and periodic reminders display at the configured interval. Delivers value as transaction awareness.

**Acceptance Scenarios**:

1. **Given** a user executes `BEGIN TRANSACTION` without a subsequent COMMIT or ROLLBACK, **When** 30 seconds pass, **Then** the status bar shows "OPEN TRANSACTION (30s)" with elapsed time updating continuously.
2. **Given** a tab has an open transaction, **When** the configured reminder interval elapses (default 5 minutes), **Then** a reminder popup appears asking the user to commit or rollback.
3. **Given** a tab has an open transaction, **When** the user attempts to close the tab, **Then** a warning dialog appears: "This tab has an uncommitted transaction. Commit or Rollback?"
4. **Given** a tab has an open transaction and the user issues a COMMIT, **When** the commit succeeds, **Then** the status bar indicator and reminders are cleared.

---

### User Story 9 - History Favorites and Export (Priority: P3)

As a database developer, I can star important queries as favorites (which are never auto-deleted by retention policies) and export my filtered history to CSV, JSON, or SQL script format for sharing or archival.

**Why this priority**: Enhances the history feature with power-user capabilities but is not required for core functionality.

**Independent Test**: Can be tested by marking entries as favorites, verifying they survive retention cleanup, and exporting history in each format. Delivers value as history curation and portability.

**Acceptance Scenarios**:

1. **Given** a user stars a history entry as a favorite, **When** the retention policy runs and deletes old entries, **Then** the favorited entry is preserved regardless of its age.
2. **Given** a user has filtered history to show only entries from a specific server, **When** they click "Export to CSV," **Then** a CSV file is generated containing only the filtered entries with all context fields.
3. **Given** a user exports history as a SQL script, **When** the file is opened, **Then** each entry appears as the original SQL text with a comment header containing the execution context (server, database, timestamp, status).

---

### User Story 10 - Custom Window Title and Tab Enhancements (Priority: P3)

As a database developer, I can customize the SSMS window title to include server, database, and username information, and I can pin important tabs, duplicate tabs, and close all unmodified tabs at once.

**Why this priority**: Quality-of-life improvements that enhance productivity but are not critical path features.

**Independent Test**: Can be tested by configuring a custom window title format and verifying it renders correctly, then testing pin/duplicate/close-unmodified operations individually. Delivers value as workspace management.

**Acceptance Scenarios**:

1. **Given** a user configures the window title format as `{server} - {database} - SSMS`, **When** they connect to SQL-PROD-01 / AdventureWorks, **Then** the SSMS window title displays "SQL-PROD-01 - AdventureWorks - SSMS."
2. **Given** a user pins a query tab, **When** they use "Close All Tabs" or other bulk close operations, **Then** the pinned tab remains open.
3. **Given** a user clicks "Duplicate Tab," **When** the new tab opens, **Then** it contains the same SQL content and is connected to the same server and database as the original.
4. **Given** a user has 10 open tabs where 6 are unmodified, **When** they click "Close All Unmodified," **Then** only the 6 unmodified tabs are closed and the 4 modified tabs remain.

---

### Edge Cases

- What happens when the history database file becomes corrupted? The system detects corruption on startup, backs up the corrupted file, and creates a fresh database with a notification to the user.
- What happens when SSMS is running but has no active connection when a query is executed? The history records the execution with empty server/database fields and notes the disconnected state.
- What happens when a user executes a SQL script larger than 1 MB? The history truncates the SQL text at 1 MB with a "..." indicator and stores the full file path as a reference if the script was saved.
- What happens when multiple SSMS instances are running simultaneously? Each instance writes to the shared history database using proper concurrency controls to prevent data corruption.
- What happens when the auto-save for session recovery fails (e.g., disk full)? The system logs the failure and shows a one-time warning to the user without interrupting their workflow.
- What happens when a tab coloring rule conflicts (server name matches multiple patterns)? The first matching rule in the configured order takes precedence.
- What happens when a user configures zero retention days? The system treats this as "unlimited" retention and never auto-deletes entries.
- What happens when the user disables history recording? Existing history remains searchable and browsable, but no new entries are captured until re-enabled.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST automatically capture one history entry per execution action (F5, Ctrl+E, or Execute button). The full editor text or selected text is recorded as a single entry, regardless of how many SQL statements the batch contains.
- **FR-002**: System MUST record the following context for each execution: SQL text, server name, database name, username, timestamp, duration (ms), row count, execution status (Success/Error/Cancelled), source file path or "Unsaved Query," tab title, and a content hash.
- **FR-003**: System MUST store history in a local database at `%AppData%\AKML SQL\history\sqlhistory.db` with full-text search indexing on SQL text.
- **FR-004**: System MUST support configurable retention (30, 60, 90, 180, 365 days, or unlimited) with a default of 90 days, and a configurable maximum entry count (default 100,000).
- **FR-005**: System MUST truncate SQL text exceeding 1 MB per entry with a truncation indicator.
- **FR-006**: System MUST provide a History panel accessible via menu and keyboard shortcut (default Ctrl+Alt+H) that supports full-text search, and filtering by server, database, status, and date range.
- **FR-007**: System MUST allow users to open a history entry in a new query tab, copy SQL to clipboard, or re-execute against the current connection.
- **FR-008**: System MUST support selecting two history entries and displaying a side-by-side diff comparison.
- **FR-009**: System MUST allow users to mark history entries as favorites, which are exempt from automatic retention cleanup.
- **FR-010**: System MUST group identical queries (by content hash) showing execution count and last execution time when deduplication is enabled.
- **FR-011**: System MUST support exporting filtered history to CSV, JSON, or SQL script format.
- **FR-012**: System MUST support optional AES-256 encryption of the history database at rest, using Windows DPAPI for transparent key management (tied to the Windows user account, no passphrase required).
- **FR-013**: System MUST color-code query tabs based on the connected server's environment, using configurable pattern-matching rules against server names.
- **FR-014**: System MUST display an environment label (e.g., "PRODUCTION," "STAGING," "DEV") on colored tabs or the status bar.
- **FR-015**: System MUST support user-configurable environment detection rules with pattern, match target, color, and label fields.
- **FR-016**: System MUST support customizable SSMS window titles using format tokens ({server}, {database}, {user}).
- **FR-017**: System MUST provide extended tab tooltips showing server, database, user, and connection time.
- **FR-018**: System MUST auto-save all open tab contents and connection metadata at a configurable interval (default 60 seconds, range 30–300 seconds).
- **FR-019**: System MUST detect abnormal SSMS termination on next startup and offer to restore the previous session's tabs via a recovery dialog.
- **FR-020**: System MUST retain the last 5 sessions for recovery purposes.
- **FR-021**: System MUST support reopening the most recently closed tab via Ctrl+Shift+T, maintaining a configurable list of up to 20 recently closed tabs.
- **FR-022**: System MUST display a modal confirmation dialog before executing any DML or DDL on servers matching production patterns.
- **FR-023**: System MUST display an error-level warning requiring explicit confirmation for DELETE or UPDATE statements without a WHERE clause.
- **FR-024**: System MUST require the user to type the object name to confirm DROP TABLE or DROP DATABASE statements.
- **FR-025**: System MUST display a confirmation dialog before TRUNCATE TABLE statements.
- **FR-026**: System MUST display a persistent status bar indicator showing elapsed time when a query tab has an uncommitted transaction.
- **FR-027**: System MUST display periodic reminder popups (configurable interval, default 5 minutes) for uncommitted transactions.
- **FR-028**: System MUST warn users when closing a tab that has an uncommitted transaction, offering to commit or rollback.
- **FR-029**: System MUST support pinning tabs to prevent accidental closure via bulk close operations.
- **FR-030**: System MUST support duplicating a tab with its content and connection context.
- **FR-031**: System MUST support closing all unmodified tabs in a single action.
- **FR-032**: System MUST allow all history, tab management, and safety features to be individually enabled or disabled via configuration.

### Key Entities

- **History Entry**: Represents a single SQL execution event. Key attributes: SQL text, server, database, username, timestamp, duration, row count, status, source, tab title, content hash, favorite flag. Grouped by content hash for deduplication.
- **Environment Rule**: A pattern-matching rule that maps server names to visual indicators. Key attributes: pattern (glob), match target (server name), color, label. Rules are ordered; first match wins.
- **Session Snapshot**: A point-in-time capture of all open SSMS tabs. Key attributes: timestamp, list of tab entries (each with content, file path, connection identifiers [server, database, auth type — no passwords], cursor position). Up to 5 snapshots retained.
- **Closed Tab Entry**: A record of a recently closed tab for Ctrl+Shift+T restoration. Key attributes: content, connection info, close timestamp. Up to 20 entries retained in a LIFO stack.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can find any previously executed query within 3 seconds using the history search, regardless of how many entries exist (up to 100,000).
- **SC-002**: 100% of SQL executions (via F5, Ctrl+E, or Execute button) are captured in history with complete context — zero silent failures.
- **SC-003**: After an abnormal SSMS termination, users can recover all open tabs within 30 seconds of restarting SSMS, with no content loss beyond the auto-save interval.
- **SC-004**: Users can visually identify the environment (production, staging, development) of any query tab within 1 second of it opening, without reading connection details.
- **SC-005**: Dangerous operations (DELETE/UPDATE without WHERE, DROP, TRUNCATE) on production servers are blocked by a confirmation dialog 100% of the time when safety features are enabled.
- **SC-006**: History recording adds no perceptible delay to query execution (recording overhead is imperceptible to the user during normal workflow).
- **SC-007**: Session auto-save operates without interrupting the user's workflow or causing noticeable UI pauses.
- **SC-008**: Users can reopen a recently closed tab via Ctrl+Shift+T in under 1 second, matching the browser experience they already know.
- **SC-009**: Users with open uncommitted transactions are reminded within the configured interval 100% of the time, preventing accidental lock escalation.
- **SC-010**: All features are individually configurable — users can enable or disable any feature without affecting others.

## Assumptions

- Users primarily work in SSMS 20, 21, or 22 and execute SQL via the standard execution methods (F5, Ctrl+E, Execute button).
- The local file system has sufficient space for the history database and session recovery files (estimated < 500 MB for 100,000 entries at maximum SQL size).
- Multiple SSMS instances on the same machine are a supported scenario and must not corrupt shared data.
- The existing AKML SQL extension architecture (shell + engine, named pipe IPC) is the delivery mechanism for all features.
- Default color assignments (red = production, yellow = staging, green = dev, blue = Azure) align with industry conventions and user expectations.
- Users expect Ctrl+Shift+T behavior to match web browser conventions (reopen last closed, repeatable).
- The DROP confirmation requiring typed object name matches the pattern used by cloud platforms (AWS, Azure) for destructive operations and will be familiar to users.

## Scope Boundaries

**In scope:**
- SQL History recording, storage, search, browse, favorites, deduplication, comparison, and export
- Tab coloring with environment detection rules
- Session recovery (auto-save and restore after crash)
- Restore recently closed tabs (Ctrl+Shift+T)
- Execution safety warnings (production guard, DELETE/UPDATE without WHERE, DROP confirmation, TRUNCATE confirmation)
- Transaction reminders
- Custom window titles, tab tooltips, pin tabs, duplicate tabs, close unmodified tabs
- All settings configurable per the configuration table in the PRD

**Out of scope:**
- Tab grouping by server or database (noted as future in PRD)
- Cloud sync of history across machines
- History sharing between team members
- Integration with source control for history entries
- Mobile or web-based access to history
