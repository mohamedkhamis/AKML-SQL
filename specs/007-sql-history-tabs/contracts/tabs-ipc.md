# IPC Contracts: Tab Management & Session Recovery

**Feature**: 007-sql-history-tabs | **Date**: 2026-03-24

Message type constants in the 50-54 (shell→engine) and 150-154 (engine→shell) ranges.

## Message Types

| Constant | Value | Direction | Description |
|----------|-------|-----------|-------------|
| `SessionSave` | 50 | Shell → Engine | Save current session snapshot |
| `SessionRestore` | 51 | Shell → Engine | Request available sessions for recovery |
| `SessionDelete` | 52 | Shell → Engine | Delete a specific session |
| `SessionSaveResult` | 150 | Engine → Shell | Save confirmation |
| `SessionRestoreResult` | 151 | Engine → Shell | Available sessions data |
| `SessionDeleteResult` | 152 | Engine → Shell | Delete confirmation |

## SessionSave (50): Shell → Engine

Sent periodically by the auto-save timer and on clean SSMS shutdown.

```csharp
[MessagePackObject]
public class SessionSaveRequest
{
    [Key(0)] public string SessionId { get; set; }       // GUID, stable across auto-saves
    [Key(1)] public int SsmsProcessId { get; set; }      // For crash detection
    [Key(2)] public bool IsNormalShutdown { get; set; }   // True on clean exit
    [Key(3)] public TabSnapshotDto[] Tabs { get; set; }   // All open tabs
}

[MessagePackObject]
public class TabSnapshotDto
{
    [Key(0)] public int TabIndex { get; set; }
    [Key(1)] public string? Title { get; set; }
    [Key(2)] public string? Content { get; set; }
    [Key(3)] public string? FilePath { get; set; }
    [Key(4)] public string? Server { get; set; }
    [Key(5)] public string? Database { get; set; }
    [Key(6)] public string? AuthType { get; set; }       // "Windows" or "SQL"
    [Key(7)] public int CursorLine { get; set; }
    [Key(8)] public int CursorColumn { get; set; }
    [Key(9)] public bool IsPinned { get; set; }
}
```

## SessionSaveResult (150): Engine → Shell

```csharp
[MessagePackObject]
public class SessionSaveResponse
{
    [Key(0)] public bool Success { get; set; }
    [Key(1)] public string? Error { get; set; }
}
```

## SessionRestore (51): Shell → Engine

Sent on SSMS startup to check for recoverable sessions.

```csharp
[MessagePackObject]
public class SessionRestoreRequest
{
    // No fields needed — engine returns all recoverable sessions
}
```

## SessionRestoreResult (151): Engine → Shell

```csharp
[MessagePackObject]
public class SessionRestoreResponse
{
    [Key(0)] public bool HasRecoverableSessions { get; set; }
    [Key(1)] public RecoverableSessionDto[] Sessions { get; set; }
    [Key(2)] public string? Error { get; set; }
}

[MessagePackObject]
public class RecoverableSessionDto
{
    [Key(0)] public string SessionId { get; set; }
    [Key(1)] public string CapturedAt { get; set; }      // ISO 8601 UTC
    [Key(2)] public int TabCount { get; set; }
    [Key(3)] public TabSnapshotDto[] Tabs { get; set; }   // Full tab data for recovery
}
```

## SessionDelete (52): Shell → Engine

```csharp
[MessagePackObject]
public class SessionDeleteRequest
{
    [Key(0)] public string SessionId { get; set; }        // Session to delete
}
```

## SessionDeleteResult (152): Engine → Shell

```csharp
[MessagePackObject]
public class SessionDeleteResponse
{
    [Key(0)] public bool Success { get; set; }
    [Key(1)] public string? Error { get; set; }
}
```

## Note: Tab Coloring (No IPC)

Tab coloring, environment detection, closed tab stack, pin/duplicate/close operations, custom window titles, and tab tooltips are **shell-side only** — they do not require engine communication. The shell reads `config.json` for environment rules and applies visual changes directly to the SSMS/VS UI.
