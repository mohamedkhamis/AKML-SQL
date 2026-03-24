# Research: SQL History & Tab Management

**Feature**: 007-sql-history-tabs | **Date**: 2026-03-24

## R1: SQLite for History Storage

**Decision**: Use `Microsoft.Data.Sqlite` (Microsoft's official ADO.NET provider) in the Engine project (net10.0) for the history database.

**Rationale**:
- The engine already runs as a self-contained .NET 10 process, so adding a NuGet dependency has no impact on shell extension compatibility.
- SQLite is the standard choice for local, single-user, file-based storage with full-text search (FTS5).
- `Microsoft.Data.Sqlite` is Microsoft's official provider, actively maintained, and supports FTS5 out of the box.
- SQLite handles concurrent readers with WAL mode; multiple SSMS instances can read simultaneously. Writes use file-level locking, which is acceptable for the low write frequency (one write per query execution).

**Alternatives considered**:
- **LiteDB**: NoSQL document database. Rejected because FTS5 full-text search is a hard requirement and LiteDB's full-text support is limited.
- **JSON flat files**: Simple but cannot support full-text search, filtering, or 100K entries efficiently.
- **SQLite via System.Data.SQLite**: Alternative provider. Rejected in favor of Microsoft's lighter-weight `Microsoft.Data.Sqlite` which doesn't bundle native binaries (uses `SQLitePCLRaw.bundle_e_sqlite3`).

## R2: SQLite WAL Mode for Concurrent Access

**Decision**: Open the history database in WAL (Write-Ahead Logging) mode with `PRAGMA journal_mode=WAL` on first creation.

**Rationale**:
- WAL mode allows concurrent readers while a write is in progress — critical for multiple SSMS instances sharing one history database.
- WAL mode improves write performance for small transactions (single INSERT per execution).
- The busy timeout (`PRAGMA busy_timeout=5000`) ensures writes don't fail immediately if another instance holds the write lock.

**Alternatives considered**:
- **Default rollback journal**: Blocks all readers during writes. Rejected for multi-instance support.
- **Separate database per SSMS instance**: Would fragment history across files, making unified search impossible.

## R3: FTS5 Full-Text Search

**Decision**: Create a separate FTS5 virtual table (`history_fts`) linked to the main `history` table via content sync, indexed on the SQL text column.

**Rationale**:
- FTS5 is built into SQLite (no external extension needed) and provides instant full-text search with ranking.
- A content-synced FTS table (`content='history'`) keeps the FTS index updated automatically on INSERT/DELETE and avoids data duplication.
- FTS5 supports `MATCH` queries with `bm25()` ranking for relevance-ordered results.

**Alternatives considered**:
- **LIKE '%keyword%'**: O(N) scan on 100K entries — too slow for the 3-second search target.
- **External search library (Lucene.NET)**: Overkill for local single-user search. Adds a large dependency.
- **FTS3/FTS4**: Older versions. FTS5 is the recommended version with better performance and features.

## R4: DPAPI for History Encryption

**Decision**: Use Windows DPAPI (`System.Security.Cryptography.ProtectedData`) to encrypt the SQLite database at the page level via SQLCipher, OR use DPAPI to encrypt/decrypt the database file on open/close.

**Revised decision**: Use **SQLite file-level encryption via DPAPI-derived key**. On first use when encryption is enabled, generate a random AES-256 key, protect it with DPAPI (`DataProtectionScope.CurrentUser`), and store the protected key in `%AppData%\AKML SQL\history\sqlhistory.key`. On database open, unprotect the key and pass it as the SQLite encryption key via `PRAGMA key`.

**Rationale**:
- DPAPI ties encryption to the Windows user account — transparent, no passphrase needed.
- `Microsoft.Data.Sqlite` supports the `Password` connection string parameter when used with `SQLitePCLRaw.bundle_e_sqlcipher` (SQLCipher).
- Alternative: Since SQLCipher adds a ~2 MB native dependency, a simpler approach is to encrypt/decrypt the entire file on engine startup/shutdown using DPAPI directly. This is simpler but means the file is unencrypted while the engine runs.

**Final approach**: Start with the simpler file-level approach (encrypt on engine shutdown, decrypt on startup). If SQLCipher is needed later for real-time encryption, it can be swapped in without changing the IPC contract.

**Alternatives considered**:
- **User passphrase**: Requires UI for entering passphrase each session. Rejected per clarification (DPAPI chosen).
- **Certificate-based**: Over-engineered for single-user desktop app.
- **No encryption**: The "encryption at rest" feature is optional (off by default), so the system works fine without it.

## R5: Session Recovery Storage Format

**Decision**: Store session snapshots as JSON files in `%AppData%\AKML SQL\sessions/`, one file per session (e.g., `session-2026-03-24T14-32-00.json`).

**Rationale**:
- JSON is human-readable for debugging and uses `System.Text.Json` already in the project.
- File-per-session makes cleanup trivial (delete oldest file when > 5 sessions).
- Session data is small (tab content + connection identifiers) — no need for a database.
- No credentials stored (per clarification: only server, database, auth type).

**Alternatives considered**:
- **SQLite**: Overkill for 5 sessions with simple read/write-all patterns.
- **Binary/MessagePack**: Faster but harder to debug. Session data volume is small enough that JSON parsing time is negligible.

## R6: Tab Coloring Implementation

**Decision**: Use `IVsWindowFrame` properties and WPF visual tree manipulation to color document tab headers in SSMS/VS.

**Rationale**:
- SSMS/VS document tabs are WPF controls. Their visual appearance can be modified by finding the tab header in the WPF visual tree and applying a background brush.
- `IVsWindowFrame` provides access to document frame properties (caption, etc.) for tooltip and title customization.
- The `IVsRunningDocumentTable` (RDT) provides events for document open/close, enabling real-time tab tracking.
- Environment detection runs in the shell (no IPC needed) — pattern matching against the server name from the connection context.

**Alternatives considered**:
- **Custom tab control replacement**: Too invasive; risks breaking SSMS UI.
- **Status bar only**: Insufficient — the key value is seeing color at a glance across multiple tabs.
- **Window frame adornments**: WPF adorners on the tab strip. More complex than direct background brush modification.

## R7: Execution Event Capture

**Decision**: Hook into SSMS query execution via the `IVsRunningDocumentTable` events and SSMS-specific COM interop (`ScriptFactory`, `QueryExecutionSettings`).

**Rationale**:
- SSMS exposes query execution through COM objects accessible from the VS SDK.
- The `TextViewCreationListener` (already in Shell.Shared) can be extended to attach execution event handlers per editor.
- Post-execution metadata (duration, row count, error) is captured from the SSMS execution result events.
- The shell sends a `HistoryRecordRequest` to the engine after each execution completes.

**Key challenge**: SSMS 20 (IsolatedShell/VS 2017) and SSMS 21/22 (VS 2022-based) expose different COM interfaces for query execution. The `ExecutionCapture` class will need conditional logic or abstraction to handle both.

**Alternatives considered**:
- **SQL Server Profiler/Extended Events**: Server-side tracing. Rejected because it requires server permissions and doesn't capture client-side context.
- **Keyboard hook only**: Would miss Execute button clicks and automated execution.

## R8: Safety Check Implementation

**Decision**: Perform SQL parsing for safety checks in the engine (reusing `TsqlParserService`) and return the warning type to the shell for dialog display.

**Rationale**:
- The engine already has `TSql170Parser` for SQL parsing — reuse it to detect DELETE/UPDATE without WHERE, DROP, TRUNCATE statements.
- Safety checks run synchronously before execution (the shell sends `SafetyCheckRequest`, waits for `SafetyCheckResponse`, then either proceeds or shows a warning dialog).
- Production server detection runs in the shell (pattern matching against environment rules) — no engine call needed.

**Alternatives considered**:
- **Shell-side regex parsing**: Unreliable for complex SQL. The engine's AST-based parser is more accurate.
- **Shell-side only**: Would require duplicating the T-SQL parser in netfx 4.7.2. The engine already has it.

## R9: Transaction Detection

**Decision**: Detect open transactions by monitoring `BEGIN TRAN` / `COMMIT` / `ROLLBACK` in executed SQL text, supplemented by querying `@@TRANCOUNT` via the active connection when possible.

**Rationale**:
- Text-based detection catches explicit transactions started by the user.
- `@@TRANCOUNT` query provides ground truth but requires an active connection and may not be available in all SSMS versions.
- The shell maintains per-tab transaction state and updates the status bar indicator.
- A periodic timer (configurable, default 5 minutes) triggers reminder popups for tabs with open transactions.

**Alternatives considered**:
- **SQL Server DMV polling**: `sys.dm_exec_sessions` could show open transactions. Rejected because it requires server permissions and a separate connection.
- **Text-only detection**: Fragile (dynamic SQL, implicit transactions). Supplementing with `@@TRANCOUNT` adds reliability.

## R10: History Panel UI Technology

**Decision**: Implement the History panel as a VS tool window (`IVsWindowPane`) with WPF `UserControl` content, following the MVVM pattern.

**Rationale**:
- VS tool windows are dockable, persistable, and integrate natively with the VS/SSMS window management.
- WPF provides the richest UI capabilities (data binding, templates, virtualization for large lists).
- The MVVM pattern is already established in the codebase (ProfileEditorViewModel, CompletionPopup).
- `VirtualizingStackPanel` ensures smooth scrolling even with 100K history entries.

**Alternatives considered**:
- **WinForms dialog**: Not dockable. The existing SettingsDialog is modal WinForms, but the History panel needs to be persistent and dockable.
- **HTML/WebView**: Adds complexity and a browser dependency. Not consistent with the existing UI patterns.
