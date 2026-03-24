# Data Model: SQL History & Tab Management

**Feature**: 007-sql-history-tabs | **Date**: 2026-03-24

## Entities

### 1. HistoryEntry

Represents a single SQL execution event captured by the system.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | long | PK, auto-increment | Unique identifier |
| SqlText | string | NOT NULL, max 1 MB | Full SQL text of the executed batch |
| SqlTextTruncated | bool | NOT NULL, default false | True if original text exceeded 1 MB |
| Server | string | nullable | SQL Server instance name (null if disconnected) |
| Database | string | nullable | Database context at execution time |
| Username | string | nullable | Windows/SQL Auth username |
| ExecutedAt | DateTime (UTC) | NOT NULL, indexed | Execution timestamp |
| DurationMs | long | NOT NULL | Execution duration in milliseconds |
| RowCount | long | NOT NULL, default 0 | Number of rows affected/returned |
| Status | ExecutionStatus enum | NOT NULL | Success, Error, Cancelled |
| ErrorMessage | string | nullable | Error message (when Status = Error) |
| Source | string | nullable | File path or "Unsaved Query" |
| TabTitle | string | nullable | SSMS tab title at execution time |
| ContentHash | string | NOT NULL, indexed | SHA-256 hash of normalized SQL text (for deduplication) |
| IsFavorite | bool | NOT NULL, default false | Exempt from retention cleanup when true |

**Indexes**:
- `IX_history_executed_at` on `ExecutedAt DESC` (for chronological browse)
- `IX_history_content_hash` on `ContentHash` (for deduplication grouping)
- `IX_history_server_database` on `Server, Database` (for filter queries)
- FTS5 virtual table `history_fts` on `SqlText` (for full-text search)

**State transitions**: Created (on execution) → optionally Favorited → Purged (by retention, unless favorited)

**Validation rules**:
- SqlText is truncated at 1 MB with `SqlTextTruncated = true` if original exceeds limit
- ContentHash is computed from whitespace-normalized, case-folded SQL text
- ExecutedAt is always stored as UTC
- DurationMs ≥ 0

**Deduplication**: Entries with the same ContentHash are logically grouped. When deduplication view is enabled, the UI shows one entry per unique ContentHash with `COUNT(*)` as execution count and `MAX(ExecutedAt)` as last execution time.

### 2. EnvironmentRule

A pattern-matching rule that maps server names to visual tab indicators.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Order | int | NOT NULL | Priority order (first match wins) |
| Pattern | string | NOT NULL | Glob pattern(s), comma-separated (e.g., `*PROD*,*LIVE*`) |
| MatchTarget | string | NOT NULL, default "serverName" | What to match against (currently only "serverName") |
| Color | string | NOT NULL | Hex color code (e.g., `#FF4444`) |
| Label | string | NOT NULL | Display label (e.g., "PRODUCTION") |

**Stored in**: `config.json` under `tabs.coloringRules[]` (not in SQLite).

**Validation rules**:
- Pattern must be non-empty
- Color must be valid hex format (#RRGGBB or #AARRGGBB)
- Label must be non-empty
- Order is 0-based; rules are evaluated in ascending Order

**Default rules** (shipped with install):

| Order | Pattern | Color | Label |
|-------|---------|-------|-------|
| 0 | `*PROD*,*LIVE*` | `#FF4444` | PRODUCTION |
| 1 | `*STG*,*UAT*,*STAGING*` | `#FFB800` | STAGING |
| 2 | `*DEV*,*LOCAL*,localhost,(local)` | `#44BB44` | DEV |
| 3 | `*.database.windows.net` | `#4488FF` | AZURE |

### 3. SessionSnapshot

A point-in-time capture of all open SSMS tabs for crash recovery.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| SessionId | string | PK (GUID) | Unique session identifier |
| CapturedAt | DateTime (UTC) | NOT NULL | Timestamp of capture |
| SsmsProcessId | int | NOT NULL | SSMS process ID (for crash detection) |
| IsNormalShutdown | bool | NOT NULL, default false | True if SSMS shut down cleanly |
| Tabs | List\<TabSnapshot\> | NOT NULL | List of open tab states |

**TabSnapshot** (nested):

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| TabIndex | int | NOT NULL | Tab order position |
| Title | string | nullable | Tab title/caption |
| Content | string | nullable | Full editor text content |
| FilePath | string | nullable | File path (null if unsaved) |
| Server | string | nullable | Connected server name |
| Database | string | nullable | Active database |
| AuthType | string | nullable | "Windows" or "SQL" |
| CursorLine | int | NOT NULL, default 0 | Cursor line position |
| CursorColumn | int | NOT NULL, default 0 | Cursor column position |
| IsPinned | bool | NOT NULL, default false | Whether tab was pinned |

**Stored in**: JSON files at `%AppData%\AKML SQL\sessions\session-{SessionId}.json`

**Lifecycle**:
1. Created: On auto-save interval tick (overwriting current session file)
2. Finalized: On clean SSMS shutdown (`IsNormalShutdown = true`)
3. Offered for recovery: On next SSMS startup if previous session has `IsNormalShutdown = false`
4. Purged: When more than 5 session files exist (oldest deleted first)

**Validation rules**:
- No credentials stored — only connection identifiers (Server, Database, AuthType)
- Content may be large (no size limit — it's the actual editor text)
- SessionId is a GUID generated on SSMS startup

### 4. ClosedTabEntry

A record of a recently closed tab for Ctrl+Shift+T restoration.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Content | string | NOT NULL | Editor text at close time |
| FilePath | string | nullable | File path (null if unsaved) |
| Server | string | nullable | Connected server name |
| Database | string | nullable | Active database |
| AuthType | string | nullable | "Windows" or "SQL" |
| ClosedAt | DateTime (UTC) | NOT NULL | When the tab was closed |
| TabTitle | string | nullable | Tab title at close time |

**Stored in**: In-memory LIFO stack in `ClosedTabStack` class (not persisted across SSMS sessions).

**Capacity**: Maximum 20 entries (configurable via `tabs.maxClosedTabs`). When capacity is reached, the oldest entry is evicted.

**Validation rules**:
- Content must be non-empty (empty tabs are not recorded)
- Stack is cleared on SSMS shutdown (not part of session recovery)

### 5. TransactionState

Tracks open transactions per query tab for the transaction reminder feature.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| TabId | string | PK | Unique tab/document identifier |
| StartedAt | DateTime (UTC) | NOT NULL | When BEGIN TRAN was detected |
| LastReminderAt | DateTime (UTC) | nullable | When last reminder was shown |
| TranCount | int | NOT NULL, default 1 | Estimated nesting level |

**Stored in**: In-memory dictionary in `TransactionMonitor` class (not persisted).

**State transitions**:
- Created: When `BEGIN TRAN` / `BEGIN TRANSACTION` detected in executed SQL
- Updated: TranCount incremented on nested `BEGIN TRAN`, decremented on `COMMIT`
- Cleared: When TranCount reaches 0 (all transactions committed/rolled back) or on `ROLLBACK`
- Cleared: When tab is closed (with warning if TranCount > 0)

## Relationships

```text
HistoryEntry (many) ──── grouped by ContentHash ───→ DeduplicatedGroup (virtual)

EnvironmentRule (many) ──── first-match-wins ───→ Tab Color Assignment

SessionSnapshot (1) ──── contains ───→ TabSnapshot (many)

ClosedTabEntry (stack, max 20) ──── LIFO ───→ Restore via Ctrl+Shift+T

TransactionState (1 per tab) ──── monitors ───→ Active query tab
```

## SQLite Schema (History Database)

```sql
-- Main history table
CREATE TABLE IF NOT EXISTS history (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    sql_text    TEXT    NOT NULL,
    truncated   INTEGER NOT NULL DEFAULT 0,
    server      TEXT,
    database_name TEXT,
    username    TEXT,
    executed_at TEXT    NOT NULL,  -- ISO 8601 UTC
    duration_ms INTEGER NOT NULL,
    row_count   INTEGER NOT NULL DEFAULT 0,
    status      INTEGER NOT NULL,  -- 0=Success, 1=Error, 2=Cancelled
    error_msg   TEXT,
    source      TEXT,
    tab_title   TEXT,
    content_hash TEXT   NOT NULL,
    is_favorite INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS IX_history_executed_at ON history(executed_at DESC);
CREATE INDEX IF NOT EXISTS IX_history_content_hash ON history(content_hash);
CREATE INDEX IF NOT EXISTS IX_history_server_db ON history(server, database_name);
CREATE INDEX IF NOT EXISTS IX_history_status ON history(status);

-- FTS5 full-text search index (content-synced with history table)
CREATE VIRTUAL TABLE IF NOT EXISTS history_fts USING fts5(
    sql_text,
    content='history',
    content_rowid='id'
);

-- Triggers to keep FTS5 in sync
CREATE TRIGGER IF NOT EXISTS history_ai AFTER INSERT ON history BEGIN
    INSERT INTO history_fts(rowid, sql_text) VALUES (new.id, new.sql_text);
END;

CREATE TRIGGER IF NOT EXISTS history_ad AFTER DELETE ON history BEGIN
    INSERT INTO history_fts(history_fts, rowid, sql_text) VALUES('delete', old.id, old.sql_text);
END;

-- Metadata table for schema version and settings
CREATE TABLE IF NOT EXISTS metadata (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

-- Initial metadata
INSERT OR IGNORE INTO metadata(key, value) VALUES('schema_version', '1');
INSERT OR IGNORE INTO metadata(key, value) VALUES('created_at', datetime('now'));
```

## Configuration Schema Additions

New sections added to `AppSettings` / `config.json`:

```json
{
  "history": {
    "enabled": true,
    "retentionDays": 90,
    "maxEntries": 100000,
    "encryptAtRest": false,
    "recordFailures": true,
    "deduplication": true,
    "shortcut": "Ctrl+Alt+H"
  },
  "tabs": {
    "coloringEnabled": true,
    "coloringRules": [
      { "order": 0, "pattern": "*PROD*,*LIVE*", "matchTarget": "serverName", "color": "#FF4444", "label": "PRODUCTION" },
      { "order": 1, "pattern": "*STG*,*UAT*,*STAGING*", "matchTarget": "serverName", "color": "#FFB800", "label": "STAGING" },
      { "order": 2, "pattern": "*DEV*,*LOCAL*,localhost,(local)", "matchTarget": "serverName", "color": "#44BB44", "label": "DEV" },
      { "order": 3, "pattern": "*.database.windows.net", "matchTarget": "serverName", "color": "#4488FF", "label": "AZURE" }
    ],
    "sessionRecovery": true,
    "autoSaveInterval": 60,
    "restoreOnStartup": "prompt",
    "maxClosedTabs": 20,
    "customWindowTitle": "{server} - {database} - SSMS"
  },
  "safety": {
    "productionWarning": true,
    "deleteWithoutWhere": true,
    "updateWithoutWhere": true,
    "dropConfirmation": true,
    "truncateConfirmation": true,
    "transactionReminder": true,
    "transactionReminderInterval": 300
  }
}
```
