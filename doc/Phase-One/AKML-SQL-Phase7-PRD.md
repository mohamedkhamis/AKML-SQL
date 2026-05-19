# AKML SQL — Phase 7: SQL History & Tab Management

> **Version:** 1.0 | **Date:** March 2026 | **Author:** Mohamed Khamis
> **Status:** Ready for Implementation | **Classification:** Confidential
> **Depends on:** Phase 2 (IntelliSense engine) — uses the same editor hooks and named pipe infrastructure
> **Branch prefix:** `007-sql-history-tabs`

---

## 1. Executive Summary

Phase 7 delivers two tightly coupled features: **SQL History** (a searchable, persistent log of every SQL statement you execute) and **Tab Management** (visual tab coloring, session recovery, and tab productivity tools). Together they solve two of the most common frustrations with SSMS: "I accidentally closed a tab with important SQL" and "I accidentally ran a query against the wrong server."

SQL Prompt's SQL History is a beloved feature that auto-saves every executed query and lets you search/restore them. SSMSBoost is valued primarily for its tab coloring and DML guard. AKML SQL combines both feature sets with a unique addition: **execution context capture** — every history entry records not just the SQL, but the server, database, username, execution time, row count, and whether it succeeded or failed.

---

## 2. SQL History

### 2.1 What Gets Recorded

Every SQL statement executed via F5, Ctrl+E, or Execute button is automatically captured:

| Field | Description |
|---|---|
| SQL Text | Full text of the executed statement(s) |
| Server | SQL Server instance name |
| Database | Database context at execution time |
| Username | Windows/SQL auth username |
| Timestamp | Execution date and time |
| Duration | Execution duration in milliseconds |
| Row Count | Number of rows affected/returned |
| Status | Success, Error (with error message), Cancelled |
| Source | File path (if saved) or "Unsaved Query" |
| SSMS Tab | Tab title at execution time |
| Hash | Content hash for deduplication |

### 2.2 History Storage

| Property | Value |
|---|---|
| Storage format | SQLite database (`%AppData%\AKML SQL\history\sqlhistory.db`) |
| Retention | 90 days default (configurable: 30, 60, 90, 180, 365 days, unlimited) |
| Max entries | 100,000 default (configurable) |
| Max SQL size | 1MB per entry (larger scripts truncated with "..." indicator) |
| Encryption | Optional AES-256 encryption at rest |
| Full-text search | SQLite FTS5 index on SQL text |

### 2.3 History Panel UI

Accessible via AKML SQL menu → SQL History, or `Ctrl+Alt+H`:

```
┌──────────────────────────────────────────────────────────────────┐
│  SQL History                                       [⚙] [X]      │
├──────────────────────────────────────────────────────────────────┤
│  🔍 [Search SQL, server, database...]                [Filters ▼] │
│                                                                  │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │ 📅 Today                                                   │  │
│  │                                                            │  │
│  │ 14:32  ✅ SELECT TOP 100 * FROM dbo.Orders WHERE...       │  │
│  │        SQL-PROD-01 > AdventureWorks > akhamis | 234ms     │  │
│  │                                                            │  │
│  │ 14:28  ❌ INSERT INTO dbo.OrderLog (OrderID, Action)...    │  │
│  │        SQL-PROD-01 > AdventureWorks > akhamis | 12ms      │  │
│  │        Error: Cannot insert NULL into column 'Action'      │  │
│  │                                                            │  │
│  │ 14:15  ✅ EXEC dbo.sp_GetCustomerOrders @CustID = 42     │  │
│  │        SQL-DEV-02 > TestDB > akhamis | 1,847ms | 2,341 rows│  │
│  │                                                            │  │
│  │ 📅 Yesterday                                               │  │
│  │ ...                                                        │  │
│  └────────────────────────────────────────────────────────────┘  │
│                                                                  │
│  [Open in New Tab]  [Copy SQL]  [Re-execute]  [Delete Entry]     │
└──────────────────────────────────────────────────────────────────┘
```

### 2.4 History Features

| Feature | Description |
|---|---|
| **Full-text search** | Search across SQL text with instant results |
| **Filter by server** | Filter entries by SQL Server instance |
| **Filter by database** | Filter entries by database name |
| **Filter by status** | Show only successful, failed, or cancelled queries |
| **Filter by date range** | Date picker for custom time ranges |
| **Open in new tab** | Open a history entry in a new query editor tab |
| **Re-execute** | Execute a history entry against the original (or current) connection |
| **Copy SQL** | Copy SQL text to clipboard |
| **Compare** | Select two entries and diff them side-by-side |
| **Favorites** | Star important queries for quick access (never auto-deleted) |
| **Deduplication** | Identical queries executed multiple times grouped (show count + last execution) |
| **Export** | Export filtered history to CSV, JSON, or SQL script file |

---

## 3. Tab Management

### 3.1 Tab Coloring

Color-code SSMS query tabs based on the connected server environment:

| Environment | Default Color | Configurable |
|---|---|---|
| Production | 🔴 Red background | Yes |
| Staging / UAT | 🟡 Yellow background | Yes |
| Development | 🟢 Green background | Yes |
| Local / LocalDB | 🔵 Blue background | Yes |
| Unknown / Other | ⚪ Default (no color) | Yes |

### 3.2 Environment Detection Rules

```json
{
  "tabColoring": {
    "rules": [
      { "pattern": "*PROD*", "match": "serverName", "color": "#FF4444", "label": "PRODUCTION" },
      { "pattern": "*STG*,*UAT*,*STAGING*", "match": "serverName", "color": "#FFB800", "label": "STAGING" },
      { "pattern": "*DEV*,*LOCAL*,localhost,(local)", "match": "serverName", "color": "#44BB44", "label": "DEV" },
      { "pattern": "*.database.windows.net", "match": "serverName", "color": "#4488FF", "label": "AZURE" }
    ]
  }
}
```

### 3.3 Tab Features

| Feature | Description |
|---|---|
| **Tab coloring** | Color-coded tabs by server environment |
| **Environment label** | Show "PRODUCTION" / "STAGING" / "DEV" label on tab or status bar |
| **Custom window title** | Customize SSMS window title with server/database/user info |
| **Tab tooltip** | Extended tooltip showing server, database, user, connection time |
| **Session recovery** | Auto-save all open tabs; restore entire session after SSMS crash |
| **Restore closed tabs** | Ctrl+Shift+T reopens the last closed tab (up to 20 tabs) |
| **Recently closed list** | Menu showing recently closed documents for selective restoration |
| **Close all unmodified** | Close all tabs that haven't been changed |
| **Tab duplication** | Duplicate current tab with same content and connection |
| **Pin tabs** | Pin important tabs so they're never accidentally closed |
| **Tab grouping** | Group tabs by server or database (future) |

### 3.4 Session Recovery

| Property | Value |
|---|---|
| Auto-save interval | Every 60 seconds (configurable: 30–300 seconds) |
| Storage | `%AppData%\AKML SQL\sessions\` |
| Recovery trigger | On SSMS startup, detect previous abnormal termination |
| Recovery UI | Dialog listing all recovered tabs with timestamps |
| Max sessions | Last 5 sessions retained |

---

## 4. Execution Safety Features

### 4.1 Execution Warnings

| Warning | Trigger | Action |
|---|---|---|
| **Production server guard** | Executing any DML/DDL on a server matching production pattern | Modal confirmation dialog: "You are about to execute on PRODUCTION server [SQL-PROD-01]. Proceed?" |
| **DELETE without WHERE** | `DELETE FROM table` without WHERE clause | Error-level warning, requires explicit confirmation |
| **UPDATE without WHERE** | `UPDATE table SET ...` without WHERE clause | Same as above |
| **DROP TABLE/DATABASE** | Any DROP statement | Confirmation with object name typed to confirm |
| **TRUNCATE TABLE** | TRUNCATE statement | Confirmation dialog |
| **Large result set** | SELECT without TOP returning > 10,000 rows | Optional warning (configurable threshold) |
| **Uncommitted transaction** | Tab has an open BEGIN TRAN without COMMIT/ROLLBACK | Status bar indicator + periodic reminder |

### 4.2 Transaction Reminder

When a query tab has an uncommitted transaction:
- Status bar shows "⚠ OPEN TRANSACTION (12 min)" with elapsed time
- Reminder popup every N minutes (configurable, default 5 min)
- Tab background flashes amber periodically
- Warning on tab close: "This tab has an uncommitted transaction. Commit or Rollback?"

---

## 5. Configuration

| Setting | Default | Description |
|---|---|---|
| `history.enabled` | `true` | Enable SQL History recording |
| `history.retentionDays` | `90` | Days to retain history entries |
| `history.maxEntries` | `100000` | Maximum history entries |
| `history.encryptAtRest` | `false` | Encrypt history database |
| `history.recordFailures` | `true` | Record failed queries |
| `history.deduplication` | `true` | Group identical queries |
| `history.shortcut` | `Ctrl+Alt+H` | Shortcut to open History panel |
| `tabs.coloringEnabled` | `true` | Enable tab coloring |
| `tabs.sessionRecovery` | `true` | Enable session auto-save and recovery |
| `tabs.autoSaveInterval` | `60` | Seconds between session auto-saves |
| `tabs.restoreOnStartup` | `prompt` | `prompt`, `always`, `never` |
| `tabs.maxClosedTabs` | `20` | Number of closed tabs remembered |
| `tabs.customWindowTitle` | `{server} - {database} - SSMS` | Window title format string |
| `safety.productionWarning` | `true` | Warn before executing on production |
| `safety.deleteWithoutWhere` | `true` | Warn on DELETE without WHERE |
| `safety.updateWithoutWhere` | `true` | Warn on UPDATE without WHERE |
| `safety.dropConfirmation` | `true` | Require typed confirmation for DROP |
| `safety.transactionReminder` | `true` | Remind about open transactions |
| `safety.transactionReminderInterval` | `300` | Seconds between transaction reminders |

---

## 6. Timeline & Milestones

| Week | Milestone | Deliverable |
|---|---|---|
| 1–2 | SQL History engine | SQLite storage, FTS5 indexing, execution capture hooks, auto-save pipeline |
| 3–4 | SQL History UI | History panel, search, filters, favorites, deduplication, export |
| 5–6 | Tab management | Tab coloring, environment rules, custom window title, tooltips |
| 7 | Session recovery & safety | Session auto-save/restore, execution warnings, transaction reminders |
| 8 | QA & integration | Full test matrix, performance benchmarks, SSMSBoost migration guide |

**Total estimated duration: 8 weeks** (2 months).

---

## 7. Competitive Comparison

| Feature | SQL Prompt | SSMSBoost | dbForge | AKML SQL Phase 7 |
|---|---|---|---|---|
| SQL History | ✔ | ✔ (basic) | ✔ | **✔ (with execution context)** |
| Full-text search | ✔ | No | Partial | **✔** |
| Execution context capture | No | No | No | **✔ (server, DB, user, duration, rows, status)** |
| History favorites | No | No | No | **✔** |
| History comparison | No | No | No | **✔** |
| Tab coloring | ✔ | ✔ | ✔ | **✔** |
| Custom window title | No | ✔ | ✔ | **✔** |
| Session recovery | Partial | ✔ | ✔ | **✔** |
| Restore closed tabs | No | ✔ | ✔ | **✔ (Ctrl+Shift+T)** |
| Production server guard | ✔ | ✔ | ✔ | **✔** |
| DELETE/UPDATE without WHERE | ✔ | ✔ | ✔ | **✔** |
| DROP confirmation (typed) | No | No | No | **✔** |
| Transaction reminder | No | No | ✔ | **✔** |
| History export | No | No | No | **✔** |
| History encryption | No | No | No | **✔** |
| Pin tabs | No | No | No | **✔** |

---

*End of Phase 7 PRD — AKML SQL v1.0*
