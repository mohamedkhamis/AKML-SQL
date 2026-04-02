# IPC Contracts: SQL History

**Feature**: 007-sql-history-tabs | **Date**: 2026-03-24

All messages use MessagePack serialization over the existing named pipe IPC infrastructure. Message type constants are added to `RpcMessage.cs` in the 40-49 (shell→engine) and 140-149 (engine→shell) ranges.

## Message Types

| Constant | Value | Direction | Description |
|----------|-------|-----------|-------------|
| `HistoryRecord` | 40 | Shell → Engine | Record a new execution in history |
| `HistorySearch` | 41 | Shell → Engine | Search/filter history entries |
| `HistoryAction` | 42 | Shell → Engine | Perform action on history entry (favorite, delete, export) |
| `HistoryRecordResult` | 140 | Engine → Shell | Confirmation of recording |
| `HistorySearchResult` | 141 | Engine → Shell | Search results |
| `HistoryActionResult` | 142 | Engine → Shell | Action result |

## HistoryRecord (40): Shell → Engine

Sent after each SQL execution completes. Fire-and-forget (RequestId = 0 acceptable for non-blocking recording, but RequestId > 0 if caller needs confirmation).

```csharp
[MessagePackObject]
public class HistoryRecordRequest
{
    [Key(0)] public string SqlText { get; set; }       // Full executed text (truncated at 1 MB)
    [Key(1)] public bool Truncated { get; set; }        // True if text was truncated
    [Key(2)] public string? Server { get; set; }        // SQL Server instance name
    [Key(3)] public string? Database { get; set; }      // Database context
    [Key(4)] public string? Username { get; set; }      // Auth username
    [Key(5)] public long DurationMs { get; set; }       // Execution duration
    [Key(6)] public long RowCount { get; set; }         // Rows affected/returned
    [Key(7)] public int Status { get; set; }            // 0=Success, 1=Error, 2=Cancelled
    [Key(8)] public string? ErrorMessage { get; set; }  // Error text (when Status=1)
    [Key(9)] public string? Source { get; set; }        // File path or "Unsaved Query"
    [Key(10)] public string? TabTitle { get; set; }     // Tab title at execution time
}
```

## HistoryRecordResult (140): Engine → Shell

```csharp
[MessagePackObject]
public class HistoryRecordResponse
{
    [Key(0)] public bool Success { get; set; }
    [Key(1)] public long EntryId { get; set; }          // Assigned history entry ID
    [Key(2)] public string? Error { get; set; }         // Error message if !Success
}
```

## HistorySearch (41): Shell → Engine

```csharp
[MessagePackObject]
public class HistorySearchRequest
{
    [Key(0)] public string? SearchText { get; set; }    // FTS5 search query (null = no text filter)
    [Key(1)] public string? Server { get; set; }        // Filter by server (null = all)
    [Key(2)] public string? Database { get; set; }      // Filter by database (null = all)
    [Key(3)] public int? Status { get; set; }           // Filter by status (null = all)
    [Key(4)] public string? DateFrom { get; set; }      // ISO 8601 UTC (null = no lower bound)
    [Key(5)] public string? DateTo { get; set; }        // ISO 8601 UTC (null = no upper bound)
    [Key(6)] public bool FavoritesOnly { get; set; }    // Filter to favorites only
    [Key(7)] public bool Deduplicate { get; set; }      // Group by ContentHash
    [Key(8)] public int Offset { get; set; }            // Pagination offset
    [Key(9)] public int Limit { get; set; }             // Max results (default 100)
}
```

## HistorySearchResult (141): Engine → Shell

```csharp
[MessagePackObject]
public class HistorySearchResponse
{
    [Key(0)] public bool Success { get; set; }
    [Key(1)] public HistoryEntryDto[] Entries { get; set; }
    [Key(2)] public int TotalCount { get; set; }        // Total matching entries (for pagination)
    [Key(3)] public string? Error { get; set; }
}

[MessagePackObject]
public class HistoryEntryDto
{
    [Key(0)] public long Id { get; set; }
    [Key(1)] public string SqlText { get; set; }        // First 500 chars for list view
    [Key(2)] public string? Server { get; set; }
    [Key(3)] public string? Database { get; set; }
    [Key(4)] public string? Username { get; set; }
    [Key(5)] public string ExecutedAt { get; set; }     // ISO 8601 UTC
    [Key(6)] public long DurationMs { get; set; }
    [Key(7)] public long RowCount { get; set; }
    [Key(8)] public int Status { get; set; }
    [Key(9)] public string? ErrorMessage { get; set; }
    [Key(10)] public string? Source { get; set; }
    [Key(11)] public string? TabTitle { get; set; }
    [Key(12)] public bool IsFavorite { get; set; }
    [Key(13)] public int ExecutionCount { get; set; }   // > 1 when deduplicated
    [Key(14)] public string? ContentHash { get; set; }  // For dedup grouping
}
```

## HistoryAction (42): Shell → Engine

```csharp
[MessagePackObject]
public class HistoryActionRequest
{
    [Key(0)] public int Action { get; set; }            // See HistoryActionType enum
    [Key(1)] public long[] EntryIds { get; set; }       // Target entry IDs
    [Key(2)] public int? ExportFormat { get; set; }     // 0=CSV, 1=JSON, 2=SQL (for Export action)
    [Key(3)] public string? ExportPath { get; set; }    // Output file path (for Export action)
}

// HistoryActionType enum values:
// 0 = GetFullSql       - Get full SQL text for an entry (list view shows truncated)
// 1 = ToggleFavorite   - Toggle favorite status
// 2 = Delete           - Delete entries
// 3 = Export           - Export filtered entries to file
// 4 = GetDiff          - Get two entries for side-by-side comparison
// 5 = DeleteAll        - Delete all non-favorite entries (manual cleanup)
```

## HistoryActionResult (142): Engine → Shell

```csharp
[MessagePackObject]
public class HistoryActionResponse
{
    [Key(0)] public bool Success { get; set; }
    [Key(1)] public string? FullSqlText { get; set; }   // For GetFullSql action
    [Key(2)] public string? DiffLeftSql { get; set; }   // For GetDiff action
    [Key(3)] public string? DiffRightSql { get; set; }  // For GetDiff action
    [Key(4)] public string? ExportPath { get; set; }    // For Export action (confirmed path)
    [Key(5)] public string? Error { get; set; }
}
```
