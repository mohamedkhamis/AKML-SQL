# Tasks: SQL History & Tab Management

**Input**: Design documents from `/specs/007-sql-history-tabs/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add new dependencies and create directory structure for Phase 7 components

- [x] T001 Add `Microsoft.Data.Sqlite` package reference to `src/AkmlSql.Engine/AkmlSql.Engine.csproj`
- [x] T002 [P] Add `System.Security.Cryptography.ProtectedData` package reference to `src/AkmlSql.Core/AkmlSql.Core.csproj` (conditional on netstandard2.0)
- [x] T003 [P] Create directory structure: `src/AkmlSql.Core/Models/History/`, `src/AkmlSql.Core/Models/Tabs/`, `src/AkmlSql.Core/Models/Safety/` for new model classes
- [x] T004 [P] Create directory structure: `src/AkmlSql.Engine/History/`, `src/AkmlSql.Engine/Sessions/`, `src/AkmlSql.Engine/Safety/` for new engine handlers
- [x] T005 [P] Create directory structure: `src/AkmlSql.Shell.Shared/History/`, `src/AkmlSql.Shell.Shared/Tabs/`, `src/AkmlSql.Shell.Shared/Sessions/`, `src/AkmlSql.Shell.Shared/Safety/` for new shell components

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T006 Add new IPC message type constants to `src/AkmlSql.Core/Ipc/RpcMessage.cs`: History (40-42, 140-142), Sessions (50-52, 150-152), Safety (55, 155) per contracts
- [x] T007 Add `HistorySettings` nested class to `src/AkmlSql.Core/Config/AppSettings.cs` with properties: Enabled (true), RetentionDays (90), MaxEntries (100000), EncryptAtRest (false), RecordFailures (true), Deduplication (true), Shortcut ("Ctrl+Alt+H") per data-model.md config schema
- [x] T008 [P] Add `TabSettings` nested class to `src/AkmlSql.Core/Config/AppSettings.cs` with properties: ColoringEnabled (true), ColoringRules (default 4 rules), SessionRecovery (true), AutoSaveInterval (60), RestoreOnStartup ("prompt"), MaxClosedTabs (20), CustomWindowTitle ("{server} - {database} - SSMS") per data-model.md config schema
- [x] T009 [P] Add `SafetySettings` nested class to `src/AkmlSql.Core/Config/AppSettings.cs` with properties: ProductionWarning (true), DeleteWithoutWhere (true), UpdateWithoutWhere (true), DropConfirmation (true), TruncateConfirmation (true), TransactionReminder (true), TransactionReminderInterval (300) per data-model.md config schema
- [x] T010 Add `History`, `Tabs`, `Safety` properties (using the new settings classes) to the root `AppSettings` class in `src/AkmlSql.Core/Config/AppSettings.cs`
- [x] T011 [P] Create `ExecutionStatus` enum (Success=0, Error=1, Cancelled=2) in `src/AkmlSql.Core/Models/History/ExecutionStatus.cs`
- [x] T012 [P] Create `SafetyWarningType` enum (ProductionDml=0, ProductionDdl=1, DeleteWithoutWhere=2, UpdateWithoutWhere=3, DropTable=4, DropDatabase=5, TruncateTable=6) in `src/AkmlSql.Core/Models/Safety/SafetyWarningType.cs`
- [x] T013 [P] Create `ExportFormat` enum (Csv=0, Json=1, Sql=2) in `src/AkmlSql.Core/Models/History/ExportFormat.cs`
- [x] T014 Register new command IDs in `src/AkmlSql.Shell.Shared/PackageGuids.cs` for: HistoryPanel, RestoreClosedTab, CloseUnmodified, DuplicateTab, PinTab
- [x] T015 Add VSCT entries (Buttons, Groups, KeyBindings) to all 6 target VSCT files (`src/AkmlSql.Ssms20/AkmlSql*.vsct`, `src/AkmlSql.Ssms21/AkmlSql*.vsct`, `src/AkmlSql.Ssms22/AkmlSql*.vsct`, `src/AkmlSql.VS2019/AkmlSql*.vsct`, `src/AkmlSql.VS2022/AkmlSql*.vsct`, `src/AkmlSql.VS2026/AkmlSql*.vsct`) for Ctrl+Alt+H (History) and Ctrl+Shift+T (Restore Closed Tab) and menu items for Close Unmodified, Duplicate Tab, Pin Tab

**Checkpoint**: Foundation ready — user story implementation can now begin in parallel

---

## Phase 3: User Story 1 — Automatic SQL Execution Recording (Priority: P1) 🎯 MVP

**Goal**: Every SQL execution is automatically captured with full context (server, database, username, duration, row count, status) and stored persistently

**Independent Test**: Execute several SQL statements via F5/Ctrl+E in SSMS and verify each is recorded with all context fields in the history database

### Implementation for User Story 1

- [x] T016 [P] [US1] Create `HistoryEntry` model class in `src/AkmlSql.Core/Models/History/HistoryEntry.cs` with all fields per data-model.md (Id, SqlText, SqlTextTruncated, Server, Database, Username, ExecutedAt, DurationMs, RowCount, Status, ErrorMessage, Source, TabTitle, ContentHash, IsFavorite)
- [x] T017 [P] [US1] Create `HistoryRecordRequest` MessagePack POCO in `src/AkmlSql.Core/Ipc/Messages/HistoryRecordRequest.cs` with Keys 0-10 per contracts/history-ipc.md
- [x] T018 [P] [US1] Create `HistoryRecordResponse` MessagePack POCO in `src/AkmlSql.Core/Ipc/Messages/HistoryRecordResponse.cs` with Keys 0-2 (Success, EntryId, Error)
- [x] T019 [US1] Create `HistoryDatabase` class in `src/AkmlSql.Engine/History/HistoryDatabase.cs` — initialize SQLite DB at `%AppData%\AKML SQL\history\sqlhistory.db` with WAL mode, busy_timeout=5000, create tables (history, history_fts, metadata) and triggers per data-model.md SQLite schema
- [x] T020 [US1] Implement `InsertEntryAsync` method in `HistoryDatabase` — insert history row, compute SHA-256 content hash from normalized SQL text, handle 1 MB truncation, FTS5 sync via trigger
- [x] T021 [US1] Implement `PurgeExpiredEntriesAsync` method in `HistoryDatabase` — delete entries older than configured retention days where IsFavorite=false, enforce max entry count by deleting oldest non-favorite entries
- [x] T022 [US1] Create `HistoryRetentionService` class in `src/AkmlSql.Engine/History/HistoryRetentionService.cs` — run retention cleanup on engine startup and periodically (every 24 hours)
- [x] T023 [US1] Create `HistoryRequestHandler` class in `src/AkmlSql.Engine/History/HistoryRequestHandler.cs` — handle MessageType.HistoryRecord (40): deserialize HistoryRecordRequest, call HistoryDatabase.InsertEntryAsync, return HistoryRecordResponse
- [x] T024 [US1] Register HistoryRecord (40) message handler in the dispatch switch of `src/AkmlSql.Engine/Server/PipeRpcServer.cs` routing to HistoryRequestHandler
- [x] T025 [US1] Create `ExecutionCapture` class in `src/AkmlSql.Shell.Shared/History/ExecutionCapture.cs` — hook into SSMS query execution events (ScriptFactory/QueryExecutionSettings COM interop), capture post-execution metadata (duration, row count, error), send HistoryRecordRequest to engine via PipeRpcClient
- [x] T026 [US1] Wire `ExecutionCapture.Initialize()` call in the package initialization sequence in `src/AkmlSql.Shell.Shared/` AkmlSqlPackage (after command registration, in the non-critical init section)

**Checkpoint**: At this point, every SQL execution in SSMS is automatically recorded in the SQLite history database with full context. The MVP is functional.

---

## Phase 4: User Story 2 — Search and Browse Execution History (Priority: P1)

**Goal**: Users can open a History panel, search across all past SQL executions with full-text search, and filter by server, database, status, or date range

**Independent Test**: Open History panel (Ctrl+Alt+H), type a keyword, verify matching entries appear. Apply server/status/date filters and verify results narrow correctly.

### Implementation for User Story 2

- [x] T027 [P] [US2] Create `HistoryFilter` model class in `src/AkmlSql.Core/Models/History/HistoryFilter.cs` with properties: SearchText, Server, Database, Status, DateFrom, DateTo, FavoritesOnly, Deduplicate, Offset, Limit
- [x] T028 [P] [US2] Create `HistorySearchRequest` MessagePack POCO in `src/AkmlSql.Core/Ipc/Messages/HistorySearchRequest.cs` with Keys 0-9 per contracts/history-ipc.md
- [x] T029 [P] [US2] Create `HistorySearchResponse` MessagePack POCO in `src/AkmlSql.Core/Ipc/Messages/HistorySearchResponse.cs` with Keys 0-3 (Success, Entries[], TotalCount, Error)
- [x] T030 [P] [US2] Create `HistoryEntryDto` MessagePack POCO in `src/AkmlSql.Core/Ipc/Messages/HistorySearchResponse.cs` (nested or separate file) with Keys 0-14 per contracts/history-ipc.md
- [x] T031 [US2] Implement `SearchAsync` method in `src/AkmlSql.Engine/History/HistoryDatabase.cs` — build dynamic SQL query from HistoryFilter: FTS5 MATCH for text search, WHERE clauses for server/database/status/date filters, GROUP BY content_hash when deduplication enabled, ORDER BY executed_at DESC, with OFFSET/LIMIT pagination
- [x] T032 [US2] Implement `GetDistinctServersAsync` and `GetDistinctDatabasesAsync` methods in `HistoryDatabase` for populating filter dropdowns
- [x] T033 [US2] Add HistorySearch (41) handler to `src/AkmlSql.Engine/History/HistoryRequestHandler.cs` — deserialize HistorySearchRequest, call HistoryDatabase.SearchAsync, return HistorySearchResponse
- [x] T034 [US2] Register HistorySearch (41) message handler in the dispatch switch of `src/AkmlSql.Engine/Server/PipeRpcServer.cs`
- [x] T035 [US2] Create `HistoryViewModel` class in `src/AkmlSql.Shell.Shared/History/HistoryViewModel.cs` — INotifyPropertyChanged MVVM ViewModel with properties: SearchText, SelectedServer, SelectedDatabase, SelectedStatus, DateFrom, DateTo, Entries (ObservableCollection), TotalCount; commands: Search, ClearFilters, LoadMore (pagination)
- [x] T036 [US2] Create `HistoryToolWindowControl.xaml` WPF UserControl in `src/AkmlSql.Shell.Shared/History/HistoryToolWindowControl.xaml` — search bar with text input and filter dropdowns, VirtualizingStackPanel ListView for history entries (showing time, status icon, truncated SQL, server > database > username, duration, row count), date range picker, grouped by day
- [x] T037 [US2] Create `HistoryToolWindowControl.xaml.cs` code-behind in `src/AkmlSql.Shell.Shared/History/HistoryToolWindowControl.xaml.cs` — wire DataContext to HistoryViewModel, handle keyboard shortcuts within the panel
- [x] T038 [US2] Create `HistoryToolWindow` class in `src/AkmlSql.Shell.Shared/History/HistoryToolWindow.cs` — implement IVsWindowPane (or ToolWindowPane base class) to host HistoryToolWindowControl as dockable VS tool window with GUID and caption "SQL History"
- [x] T039 [US2] Create `HistoryPanelCommand` class in `src/AkmlSql.Shell.Shared/Commands/HistoryPanelCommand.cs` — OleMenuCommand with static Initialize(), toggles visibility of HistoryToolWindow, following existing command pattern (e.g., AboutCommand)
- [x] T040 [US2] Register `HistoryPanelCommand.Initialize()` call in AkmlSqlPackage initialization sequence in `src/AkmlSql.Shell.Shared/`

**Checkpoint**: Users can open the History panel, search past executions with full-text search, and filter by multiple criteria. Both P1 stories are now complete.

---

## Phase 5: User Story 3 — Restore and Re-execute from History (Priority: P2)

**Goal**: Users can select a history entry and open it in a new tab, copy SQL to clipboard, re-execute against the current connection, or compare two entries side-by-side

**Independent Test**: Select a history entry, click "Open in New Tab" and verify SQL appears in a new editor. Select two entries and click "Compare" to verify diff view.

### Implementation for User Story 3

- [x] T041 [P] [US3] Create `HistoryActionRequest` MessagePack POCO in `src/AkmlSql.Core/Ipc/Messages/HistoryActionRequest.cs` with Keys 0-3 (Action, EntryIds, ExportFormat, ExportPath) per contracts/history-ipc.md
- [x] T042 [P] [US3] Create `HistoryActionResponse` MessagePack POCO in `src/AkmlSql.Core/Ipc/Messages/HistoryActionResponse.cs` with Keys 0-5 (Success, FullSqlText, DiffLeftSql, DiffRightSql, ExportPath, Error)
- [x] T043 [US3] Implement `GetFullSqlAsync(long entryId)` method in `src/AkmlSql.Engine/History/HistoryDatabase.cs` — retrieve full SQL text for a single entry (list view shows truncated preview)
- [x] T044 [US3] Implement `GetEntriesForDiffAsync(long id1, long id2)` method in `src/AkmlSql.Engine/History/HistoryDatabase.cs` — retrieve full SQL text for two entries for side-by-side comparison
- [x] T045 [US3] Add HistoryAction (42) handler to `src/AkmlSql.Engine/History/HistoryRequestHandler.cs` — dispatch on Action type: GetFullSql (0), GetDiff (4); call corresponding HistoryDatabase methods
- [x] T046 [US3] Register HistoryAction (42) message handler in the dispatch switch of `src/AkmlSql.Engine/Server/PipeRpcServer.cs`
- [x] T047 [US3] Add action commands to `src/AkmlSql.Shell.Shared/History/HistoryViewModel.cs` — OpenInNewTab (requests full SQL, opens new editor via DTE.ItemOperations.NewFile), CopySql (requests full SQL, copies to clipboard), ReExecute (requests full SQL, executes via active connection), Compare (requests diff for two selected entries)
- [x] T048 [US3] Add action buttons to `src/AkmlSql.Shell.Shared/History/HistoryToolWindowControl.xaml` — "Open in New Tab", "Copy SQL", "Re-execute", "Compare" buttons bound to ViewModel commands, enable Compare only when exactly 2 entries selected
- [x] T049 [US3] Create simple diff view in `src/AkmlSql.Shell.Shared/History/HistoryToolWindowControl.xaml` — side-by-side text comparison panel (shown inline or as a popup) using DiffPlex or simple text comparison for Compare action

**Checkpoint**: History panel is now fully interactive — users can search, browse, open, copy, re-execute, and compare past queries.

---

## Phase 6: User Story 4 — Tab Coloring by Server Environment (Priority: P2)

**Goal**: SSMS query tabs are automatically color-coded based on server environment (production=red, staging=yellow, dev=green, Azure=blue) with configurable rules

**Independent Test**: Connect tabs to servers matching PROD/DEV/STG patterns and verify each tab displays the correct background color and environment label.

### Implementation for User Story 4

- [x] T050 [P] [US4] Create `EnvironmentRule` model class in `src/AkmlSql.Core/Models/Tabs/EnvironmentRule.cs` with properties: Order, Pattern, MatchTarget, Color, Label per data-model.md
- [x] T051 [US4] Create `EnvironmentDetector` class in `src/AkmlSql.Shell.Shared/Tabs/EnvironmentDetector.cs` — load EnvironmentRule[] from TabSettings.ColoringRules config, implement `Match(string serverName)` method using glob pattern matching (comma-separated patterns, case-insensitive), return first matching rule or null
- [x] T052 [US4] Create `TabColoringManager` class in `src/AkmlSql.Shell.Shared/Tabs/TabColoringManager.cs` — subscribe to document window open/activation events via IVsRunningDocumentTable or DTE.Events.WindowEvents, detect connected server name from active connection context, apply background color brush to WPF tab header by walking the visual tree of the document tab, display environment label
- [x] T053 [US4] Create `TabTooltipProvider` class in `src/AkmlSql.Shell.Shared/Tabs/TabTooltipProvider.cs` — extend tab tooltips to show server, database, username, and connection time by modifying the WPF tooltip of document tab headers
- [x] T054 [US4] Wire `TabColoringManager.Initialize()` and `TabTooltipProvider.Initialize()` in AkmlSqlPackage initialization in `src/AkmlSql.Shell.Shared/` (non-critical init section, guarded by TabSettings.ColoringEnabled)

**Checkpoint**: Tabs are visually color-coded by environment. Users can identify production vs. dev at a glance.

---

## Phase 7: User Story 5 — Session Recovery After Crash (Priority: P2)

**Goal**: All open tabs are periodically auto-saved. After an abnormal SSMS termination, a recovery dialog offers to restore the previous session's tabs.

**Independent Test**: Open multiple tabs with content, force-kill SSMS, restart, and verify the recovery dialog lists all tabs for restoration.

### Implementation for User Story 5

- [x] T055 [P] [US5] Create `SessionSnapshot` and `TabSnapshot` model classes in `src/AkmlSql.Core/Models/Tabs/SessionSnapshot.cs` with all fields per data-model.md (SessionId, CapturedAt, SsmsProcessId, IsNormalShutdown, Tabs list; TabSnapshot: TabIndex, Title, Content, FilePath, Server, Database, AuthType, CursorLine, CursorColumn, IsPinned)
- [x] T056 [P] [US5] Create `SessionSaveRequest` and `SessionSaveResponse` MessagePack POCOs in `src/AkmlSql.Core/Ipc/Messages/SessionSaveRequest.cs` and `SessionSaveResponse.cs` per contracts/tabs-ipc.md
- [x] T057 [P] [US5] Create `SessionRestoreRequest` and `SessionRestoreResponse` MessagePack POCOs in `src/AkmlSql.Core/Ipc/Messages/SessionRestoreRequest.cs` and `SessionRestoreResponse.cs` per contracts/tabs-ipc.md
- [x] T058 [P] [US5] Create `TabSnapshotDto` and `RecoverableSessionDto` MessagePack POCOs in `src/AkmlSql.Core/Ipc/Messages/SessionRestoreResponse.cs` per contracts/tabs-ipc.md
- [x] T059 [P] [US5] Create `SessionDeleteRequest` and `SessionDeleteResponse` MessagePack POCOs in `src/AkmlSql.Core/Ipc/Messages/SessionDeleteRequest.cs` and `SessionDeleteResponse.cs` per contracts/tabs-ipc.md
- [x] T060 [US5] Create `SessionStorage` class in `src/AkmlSql.Engine/Sessions/SessionStorage.cs` — save/load SessionSnapshot as JSON files in `%AppData%\AKML SQL\sessions/`, implement SaveAsync (atomic write via temp file + rename), LoadAllAsync (read all session files), DeleteAsync (remove session file), PurgeOldSessions (keep max 5, delete oldest)
- [x] T061 [US5] Create `SessionRequestHandler` class in `src/AkmlSql.Engine/Sessions/SessionRequestHandler.cs` — handle SessionSave (50): call SessionStorage.SaveAsync; SessionRestore (51): call SessionStorage.LoadAllAsync, filter to sessions with IsNormalShutdown=false; SessionDelete (52): call SessionStorage.DeleteAsync
- [x] T062 [US5] Register SessionSave (50), SessionRestore (51), SessionDelete (52) message handlers in the dispatch switch of `src/AkmlSql.Engine/Server/PipeRpcServer.cs`
- [x] T063 [US5] Create `SessionAutoSave` class in `src/AkmlSql.Shell.Shared/Sessions/SessionAutoSave.cs` — start a timer at configured interval (default 60s), on tick: enumerate all open document windows via DTE.Documents or IVsRunningDocumentTable, capture content and connection identifiers (no passwords) for each tab, send SessionSaveRequest to engine; on clean SSMS shutdown: send final save with IsNormalShutdown=true
- [x] T064 [US5] Create `SessionRecoveryDialog` class in `src/AkmlSql.Shell.Shared/Sessions/SessionRecoveryDialog.cs` — WinForms dialog shown on SSMS startup when recoverable sessions exist, list tabs with checkboxes (title, file path, server, capture time), OK restores selected tabs by opening new editors with saved content, Cancel dismisses
- [x] T065 [US5] Wire session recovery check in AkmlSqlPackage initialization in `src/AkmlSql.Shell.Shared/` — after engine starts, send SessionRestoreRequest, if HasRecoverableSessions: show SessionRecoveryDialog (or auto-restore per RestoreOnStartup setting), then start SessionAutoSave timer

**Checkpoint**: Sessions are auto-saved and recoverable after crashes. Users never lose open tab content.

---

## Phase 8: User Story 7 — Execution Safety Warnings (Priority: P2)

**Goal**: Modal confirmation dialogs block dangerous operations — DML/DDL on production, DELETE/UPDATE without WHERE, DROP (typed confirmation), TRUNCATE

**Independent Test**: Execute `DELETE FROM dbo.Orders` without WHERE and verify error-level warning. Execute `DROP TABLE dbo.Test` and verify type-to-confirm dialog.

### Implementation for User Story 7

- [x] T066 [P] [US7] Create `SafetyCheckRequest` MessagePack POCO in `src/AkmlSql.Core/Ipc/Messages/SafetyCheckRequest.cs` with Keys 0-2 (SqlText, Server, IsProductionServer) per contracts/safety-ipc.md
- [x] T067 [P] [US7] Create `SafetyCheckResponse` and `SafetyWarningDto` MessagePack POCOs in `src/AkmlSql.Core/Ipc/Messages/SafetyCheckResponse.cs` with Keys per contracts/safety-ipc.md
- [x] T068 [US7] Create `SafetyCheckHandler` class in `src/AkmlSql.Engine/Safety/SafetyCheckHandler.cs` — parse SQL with TsqlParserService, detect: DELETE/UPDATE statements without WHERE clause (walk AST for WhereClause == null), DROP TABLE/DROP DATABASE statements (extract object name), TRUNCATE TABLE statements; combine with IsProductionServer flag for production DML/DDL warnings; return SafetyCheckResponse with appropriate SafetyWarningDto array
- [x] T069 [US7] Register SafetyCheck (55) message handler in the dispatch switch of `src/AkmlSql.Engine/Server/PipeRpcServer.cs`
- [x] T070 [US7] Create `SafetyWarningDialog` class in `src/AkmlSql.Shell.Shared/Safety/SafetyWarningDialog.cs` — WinForms modal dialog with multiple display modes: (a) simple confirmation for production DML/DDL and TRUNCATE, (b) error-level warning for DELETE/UPDATE without WHERE (red warning icon, explicit confirm button), (c) type-to-confirm for DROP (text input must match object name before OK is enabled)
- [x] T071 [US7] Create `ExecutionInterceptor` class in `src/AkmlSql.Shell.Shared/Safety/ExecutionInterceptor.cs` — hook into the pre-execution path (before the query is sent to SQL Server), determine IsProductionServer using EnvironmentDetector from US4, send SafetyCheckRequest to engine, if RequiresConfirmation: show SafetyWarningDialog, block execution until user confirms or cancels
- [x] T072 [US7] Wire `ExecutionInterceptor.Initialize()` in AkmlSqlPackage initialization in `src/AkmlSql.Shell.Shared/` (guarded by SafetySettings flags)

**Checkpoint**: Dangerous operations are blocked by appropriate confirmation dialogs. Users are protected from accidental data loss.

---

## Phase 9: User Story 6 — Restore Recently Closed Tabs (Priority: P3)

**Goal**: Ctrl+Shift+T reopens the most recently closed tab (browser-style). A recently-closed list allows selective restoration.

**Independent Test**: Close 3 tabs, press Ctrl+Shift+T three times, verify tabs reopen in reverse order with original content and connection.

### Implementation for User Story 6

- [x] T073 [P] [US6] Create `ClosedTabEntry` model class in `src/AkmlSql.Core/Models/Tabs/ClosedTabEntry.cs` with properties: Content, FilePath, Server, Database, AuthType, ClosedAt, TabTitle per data-model.md
- [x] T074 [US6] Create `ClosedTabStack` class in `src/AkmlSql.Shell.Shared/Tabs/ClosedTabStack.cs` — thread-safe LIFO stack with configurable max capacity (default 20 from TabSettings.MaxClosedTabs), Push(ClosedTabEntry) evicts oldest when full, Pop() returns most recent, GetAll() returns list for the recently-closed menu, Clear() resets on shutdown
- [x] T075 [US6] Hook document close events in `src/AkmlSql.Shell.Shared/Tabs/ClosedTabStack.cs` or a separate `TabCloseMonitor` — subscribe to IVsRunningDocumentTable or DTE.Events.DocumentEvents.DocumentClosing, on close: capture tab content and connection info, push to ClosedTabStack (skip if content is empty)
- [x] T076 [US6] Create `RestoreClosedTabCommand` class in `src/AkmlSql.Shell.Shared/Commands/RestoreClosedTabCommand.cs` — OleMenuCommand bound to Ctrl+Shift+T, on execute: pop from ClosedTabStack, open new editor with restored content (via DTE.ItemOperations.NewFile and text insertion), BeforeQueryStatus disables command when stack is empty
- [x] T077 [US6] Register `RestoreClosedTabCommand.Initialize()` call in AkmlSqlPackage initialization in `src/AkmlSql.Shell.Shared/`

**Checkpoint**: Ctrl+Shift+T reopens closed tabs in browser-style LIFO order.

---

## Phase 10: User Story 8 — Transaction Reminder (Priority: P3)

**Goal**: Open transactions trigger a persistent status bar indicator with elapsed time and periodic reminder popups

**Independent Test**: Execute `BEGIN TRANSACTION`, verify status bar shows "OPEN TRANSACTION (Xs)" with updating timer. Wait for reminder interval and verify popup appears.

### Implementation for User Story 8

- [x] T078 [P] [US8] Create `TransactionState` model class in `src/AkmlSql.Core/Models/Safety/TransactionState.cs` with properties: TabId, StartedAt, LastReminderAt, TranCount per data-model.md
- [x] T079 [US8] Create `TransactionMonitor` class in `src/AkmlSql.Shell.Shared/Safety/TransactionMonitor.cs` — maintain ConcurrentDictionary<string, TransactionState> keyed by tab/document ID; subscribe to execution events (share hook with ExecutionCapture), parse executed SQL for BEGIN TRAN/COMMIT/ROLLBACK keywords to track TranCount per tab; start/stop per-tab monitoring
- [x] T080 [US8] Extend `src/AkmlSql.Shell.Shared/StatusBar/StatusBarManager.cs` — add `SetTransactionIndicator(string text)` and `ClearTransactionIndicator()` methods using IVsStatusbar; update status text with "OPEN TRANSACTION ({elapsed})" every second for tabs with TranCount > 0
- [x] T081 [US8] Add transaction reminder timer to `TransactionMonitor` — periodic check (every 30 seconds), for each tab with open transaction: if elapsed since LastReminderAt exceeds SafetySettings.TransactionReminderInterval, show a non-modal reminder popup ("Tab '{title}' has an open transaction for {elapsed}. Commit or Rollback?"), update LastReminderAt
- [x] T082 [US8] Add tab close interception for open transactions in `TransactionMonitor` — when a tab with TranCount > 0 is being closed, show modal warning "This tab has an uncommitted transaction. Commit or Rollback?" with Commit/Rollback/Cancel buttons
- [x] T083 [US8] Wire `TransactionMonitor.Initialize()` in AkmlSqlPackage initialization in `src/AkmlSql.Shell.Shared/` (guarded by SafetySettings.TransactionReminder)

**Checkpoint**: Users are actively reminded about open transactions, preventing accidental lock escalation.

---

## Phase 11: User Story 9 — History Favorites and Export (Priority: P3)

**Goal**: Users can star queries as favorites (immune to retention cleanup) and export filtered history to CSV, JSON, or SQL script

**Independent Test**: Star a history entry, run retention cleanup, verify it survives. Export filtered history to CSV and verify file contents.

### Implementation for User Story 9

- [x] T084 [US9] Implement `ToggleFavoriteAsync(long entryId)` method in `src/AkmlSql.Engine/History/HistoryDatabase.cs` — toggle IsFavorite flag on the specified entry
- [x] T085 [US9] Implement `DeleteEntriesAsync(long[] entryIds)` method in `src/AkmlSql.Engine/History/HistoryDatabase.cs` — delete specified entries (handle FTS5 sync via trigger)
- [x] T086 [US9] Implement `ExportAsync(HistoryFilter filter, ExportFormat format, string outputPath)` method in `src/AkmlSql.Engine/History/HistoryDatabase.cs` — query filtered entries, write to file: CSV (header row + all context fields), JSON (array of entry objects), SQL (each entry as SQL text with comment header containing context)
- [x] T087 [US9] Add ToggleFavorite (1), Delete (2), Export (3), DeleteAll (5) action handling to the HistoryAction (42) handler in `src/AkmlSql.Engine/History/HistoryRequestHandler.cs`
- [x] T088 [US9] Add Favorite toggle and Delete commands to `src/AkmlSql.Shell.Shared/History/HistoryViewModel.cs` — ToggleFavorite sends HistoryActionRequest with Action=1, Delete sends Action=2, both refresh the entry list after completion
- [x] T089 [US9] Add Export command to `src/AkmlSql.Shell.Shared/History/HistoryViewModel.cs` — show SaveFileDialog with format filter (CSV/JSON/SQL), send HistoryActionRequest with Action=3 and selected format/path
- [x] T090 [US9] Add Favorite star icon, Delete button, and Export button to `src/AkmlSql.Shell.Shared/History/HistoryToolWindowControl.xaml` — favorite star toggles on click, delete requires confirmation, export shows save dialog

**Checkpoint**: Users can curate their history with favorites and export for sharing.

---

## Phase 12: User Story 10 — Custom Window Title and Tab Enhancements (Priority: P3)

**Goal**: Customizable SSMS window title with server/database/user tokens. Pin tabs, duplicate tabs, close all unmodified tabs.

**Independent Test**: Configure custom window title format, verify it renders on connection. Pin a tab, use Close All, verify pinned tab survives.

### Implementation for User Story 10

- [x] T091 [US10] Create `WindowTitleManager` class in `src/AkmlSql.Shell.Shared/Tabs/WindowTitleManager.cs` — subscribe to active document/connection change events, format SSMS window title by replacing tokens ({server}, {database}, {user}) in TabSettings.CustomWindowTitle template, apply via DTE.MainWindow.Caption or IVsWindowFrame
- [x] T092 [US10] Create `PinTabCommand` class in `src/AkmlSql.Shell.Shared/Commands/PinTabCommand.cs` — OleMenuCommand to toggle pin state on the active document tab, maintain pinned tab set in-memory (HashSet<string> by document path/identity), BeforeQueryStatus updates check text ("Pin Tab" / "Unpin Tab")
- [x] T093 [US10] Create `DuplicateTabCommand` class in `src/AkmlSql.Shell.Shared/Commands/DuplicateTabCommand.cs` — OleMenuCommand to duplicate the active tab: read current editor content via IVsTextView, get connection context, open new editor via DTE.ItemOperations.NewFile, insert content, apply same connection
- [x] T094 [US10] Create `CloseUnmodifiedCommand` class in `src/AkmlSql.Shell.Shared/Commands/CloseUnmodifiedCommand.cs` — OleMenuCommand to close all tabs that are not modified (IVsPersistDocData.IsDocDataDirty == false) and not pinned, iterate DTE.Documents collection
- [x] T095 [US10] Register `WindowTitleManager.Initialize()`, `PinTabCommand.Initialize()`, `DuplicateTabCommand.Initialize()`, `CloseUnmodifiedCommand.Initialize()` in AkmlSqlPackage initialization in `src/AkmlSql.Shell.Shared/`

**Checkpoint**: All tab productivity features are working — custom titles, pin, duplicate, close unmodified.

---

## Phase 13: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories

- [x] T096 [P] Create `HistoryEncryption` class in `src/AkmlSql.Engine/History/HistoryEncryption.cs` — implement optional AES-256 encryption at rest using DPAPI: generate random key on first enable, protect with DataProtectionScope.CurrentUser, store protected key in `%AppData%\AKML SQL\history\sqlhistory.key`, encrypt DB file on engine shutdown, decrypt on startup (per research.md R4)
- [x] T097 [P] Add History, Tabs, and Safety settings tabs to the existing `src/AkmlSql.Shell.Shared/Dialogs/SettingsDialog.cs` — History tab: enable/disable, retention days, max entries, encryption toggle, deduplication; Tabs tab: coloring enabled, rule editor grid, session recovery, auto-save interval, restore mode, max closed tabs, window title format; Safety tab: all warning toggles, transaction reminder interval
- [x] T098 [P] Add logging (Serilog) to all new components — HistoryDatabase (open/close/error), HistoryRequestHandler (record/search), SessionStorage (save/load/purge), SafetyCheckHandler (warnings triggered), ExecutionCapture (capture events), TabColoringManager (rule matches), TransactionMonitor (state changes)
- [x] T099 Verify all new features respect their individual enable/disable config flags — ensure HistorySettings.Enabled, TabSettings.ColoringEnabled, TabSettings.SessionRecovery, SafetySettings.* flags correctly skip initialization and operation when disabled
- [x] T100 Handle SQLite database corruption gracefully in `src/AkmlSql.Engine/History/HistoryDatabase.cs` — on open, if database is corrupt: back up corrupted file with .corrupt suffix, create fresh database, log warning
- [x] T101 Run manual testing per quickstart.md checklist: history recording, history search, tab coloring, session recovery, Ctrl+Shift+T, safety warnings, transaction reminder

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories
- **User Stories (Phases 3–12)**: All depend on Foundational phase completion
  - US1 (Phase 3) and US4 (Phase 6) can start in parallel (no shared dependencies)
  - US2 (Phase 4) depends on US1 (needs HistoryDatabase and recording to have data)
  - US3 (Phase 5) depends on US2 (needs HistoryToolWindow and ViewModel)
  - US7 (Phase 8) depends on US4 (uses EnvironmentDetector for production detection)
  - US5 (Phase 7) is independent of all other stories
  - US6 (Phase 9) is independent of all other stories
  - US8 (Phase 10) is independent (but benefits from ExecutionCapture from US1)
  - US9 (Phase 11) depends on US2 (extends HistoryDatabase and HistoryViewModel)
  - US10 (Phase 12) is independent of all other stories
- **Polish (Phase 13)**: Depends on all user stories being complete

### User Story Dependencies

- **US1 (P1)**: Can start after Foundational — No story dependencies
- **US2 (P1)**: Depends on US1 (HistoryDatabase must exist)
- **US3 (P2)**: Depends on US2 (HistoryToolWindow and ViewModel must exist)
- **US4 (P2)**: Can start after Foundational — No story dependencies
- **US5 (P2)**: Can start after Foundational — No story dependencies
- **US6 (P3)**: Can start after Foundational — No story dependencies
- **US7 (P2)**: Depends on US4 (EnvironmentDetector needed for IsProductionServer)
- **US8 (P3)**: Can start after Foundational — No story dependencies (shares execution hook pattern with US1 but is independently implementable)
- **US9 (P3)**: Depends on US2 (extends HistoryDatabase and HistoryViewModel)
- **US10 (P3)**: Can start after Foundational — No story dependencies

### Within Each User Story

- Models and IPC POCOs before engine handlers
- Engine handlers before shell-side consumers
- Core implementation before UI integration
- Wire initialization last

### Parallel Opportunities

- All Setup tasks (T001–T005) marked [P] can run in parallel
- Foundational config tasks (T007–T009, T011–T013) marked [P] can run in parallel
- After Foundational completes: US1, US4, US5, US6, US8, US10 can all start in parallel
- Within each story, tasks marked [P] (IPC POCOs, models) can run in parallel
- Polish tasks (T096–T098) marked [P] can run in parallel

---

## Parallel Example: User Story 1

```text
# Launch all models and IPC POCOs in parallel:
Task: T016 [P] Create HistoryEntry model in src/AkmlSql.Core/Models/History/HistoryEntry.cs
Task: T017 [P] Create HistoryRecordRequest in src/AkmlSql.Core/Ipc/Messages/HistoryRecordRequest.cs
Task: T018 [P] Create HistoryRecordResponse in src/AkmlSql.Core/Ipc/Messages/HistoryRecordResponse.cs

# Then sequentially: Database → Handler → Capture → Wire
Task: T019 HistoryDatabase.cs (depends on T016)
Task: T020 InsertEntryAsync (depends on T019)
Task: T023 HistoryRequestHandler (depends on T020, T017, T018)
Task: T025 ExecutionCapture (depends on T023)
Task: T026 Wire initialization (depends on T025)
```

## Parallel Example: Independent Stories After Foundational

```text
# These stories have no cross-dependencies and can run in parallel:
Agent A: US1 (T016–T026) → US2 (T027–T040) → US3 (T041–T049) → US9 (T084–T090)
Agent B: US4 (T050–T054) → US7 (T066–T072)
Agent C: US5 (T055–T065)
Agent D: US6 (T073–T077) + US8 (T078–T083) + US10 (T091–T095)
```

---

## Implementation Strategy

### MVP First (User Stories 1 + 2 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories)
3. Complete Phase 3: User Story 1 — History Recording
4. Complete Phase 4: User Story 2 — History Search & Browse UI
5. **STOP and VALIDATE**: Execute queries, open History panel, search and filter
6. Deploy/demo if ready — users already have the most valuable feature

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. US1 + US2 → History recording + search (**MVP!**)
3. US4 + US7 → Tab coloring + safety warnings (visual safety layer)
4. US5 → Session recovery (crash protection)
5. US3 → History actions (open/copy/re-execute/compare)
6. US6 + US8 + US9 + US10 → P3 features (closed tabs, transactions, favorites, tab tools)
7. Polish → Encryption, settings UI, logging

### Parallel Team Strategy

With multiple developers:

1. Team completes Setup + Foundational together
2. Once Foundational is done:
   - Developer A: US1 → US2 → US3 → US9 (History chain)
   - Developer B: US4 → US7 (Tab coloring → Safety)
   - Developer C: US5 (Session recovery)
   - Developer D: US6 + US8 + US10 (Tabs + Transactions + Enhancements)
3. Polish phase after all stories merge

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- No test tasks generated (not explicitly requested in spec)
- Each user story is independently completable and testable at its checkpoint
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- VSCT changes (T015) must be replicated across all 6 target VSCT files
