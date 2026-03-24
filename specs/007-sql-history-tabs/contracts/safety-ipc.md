# IPC Contracts: Execution Safety

**Feature**: 007-sql-history-tabs | **Date**: 2026-03-24

Message type constants in the 55-56 (shell→engine) and 155-156 (engine→shell) ranges.

## Message Types

| Constant | Value | Direction | Description |
|----------|-------|-----------|-------------|
| `SafetyCheck` | 55 | Shell → Engine | Pre-execution safety analysis |
| `SafetyCheckResult` | 155 | Engine → Shell | Warning details |

## SafetyCheck (55): Shell → Engine

Sent before query execution when safety features are enabled. The shell waits synchronously for the response before allowing execution to proceed.

```csharp
[MessagePackObject]
public class SafetyCheckRequest
{
    [Key(0)] public string SqlText { get; set; }          // SQL text about to be executed
    [Key(1)] public string? Server { get; set; }          // Connected server name
    [Key(2)] public bool IsProductionServer { get; set; } // Pre-determined by shell (environment rules)
}
```

## SafetyCheckResult (155): Engine → Shell

```csharp
[MessagePackObject]
public class SafetyCheckResponse
{
    [Key(0)] public bool RequiresConfirmation { get; set; }
    [Key(1)] public SafetyWarningDto[] Warnings { get; set; }
}

[MessagePackObject]
public class SafetyWarningDto
{
    [Key(0)] public int WarningType { get; set; }     // See SafetyWarningType enum
    [Key(1)] public string Message { get; set; }       // Human-readable warning text
    [Key(2)] public string? ObjectName { get; set; }   // For DROP: object name to type for confirmation
    [Key(3)] public int Severity { get; set; }         // 0=Info, 1=Warning, 2=Error
}

// SafetyWarningType enum values:
// 0 = ProductionDml         - DML on production server
// 1 = ProductionDdl         - DDL on production server
// 2 = DeleteWithoutWhere    - DELETE without WHERE clause
// 3 = UpdateWithoutWhere    - UPDATE without WHERE clause
// 4 = DropTable             - DROP TABLE statement
// 5 = DropDatabase          - DROP DATABASE statement
// 6 = TruncateTable         - TRUNCATE TABLE statement
```

## Safety Check Flow

```text
User presses F5
    │
    ▼
Shell: Is safety enabled? ──No──→ Execute normally
    │ Yes
    ▼
Shell: Is production server? (check environment rules)
    │
    ▼
Shell: Send SafetyCheckRequest to engine
    │
    ▼
Engine: Parse SQL with TSql170Parser
Engine: Check for DELETE/UPDATE without WHERE
Engine: Check for DROP TABLE/DATABASE
Engine: Check for TRUNCATE TABLE
Engine: Return SafetyCheckResponse
    │
    ▼
Shell: RequiresConfirmation? ──No──→ Execute normally
    │ Yes
    ▼
Shell: Show SafetyWarningDialog
    │
    ├── DropTable/DropDatabase → Type-to-confirm dialog (user types object name)
    ├── DeleteWithoutWhere/UpdateWithoutWhere → Error-level confirmation
    └── ProductionDml/Ddl → Modal "Proceed on PRODUCTION?" dialog
    │
    ▼
User confirms? ──No──→ Cancel execution
    │ Yes
    ▼
Execute query → Record in history
```

## Note: Transaction Monitoring (No IPC)

Transaction detection and reminders are **shell-side only**:
- The shell monitors executed SQL text for `BEGIN TRAN` / `COMMIT` / `ROLLBACK` keywords
- Per-tab `TransactionState` is maintained in memory by `TransactionMonitor`
- Status bar indicator and periodic reminders are shell-side UI operations
- No engine communication needed for transaction tracking
