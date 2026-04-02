# Data Model: SQL Prompt Core Feature Parity

**Branch**: `010-sql-prompt-core-parity`

## Entities

### Existing Entities (already implemented, referenced by new features)

#### SafetyWarningType (enum)
```
ProductionDml = 0      # Any DML on production server
ProductionDdl = 1      # Any DDL on production server
DeleteWithoutWhere = 2 # DELETE missing WHERE clause
UpdateWithoutWhere = 3 # UPDATE missing WHERE clause
DropTable = 4          # DROP TABLE detected
DropDatabase = 5       # DROP DATABASE detected
TruncateTable = 6      # TRUNCATE TABLE detected
```

#### SafetySettings (config section)
```
ProductionWarning: bool = true
DeleteWithoutWhere: bool = true
UpdateWithoutWhere: bool = true
DropConfirmation: bool = true
TruncateConfirmation: bool = true
TransactionReminder: bool = true
TransactionReminderInterval: int = 300 (seconds)
```

#### EnvironmentRule (tab coloring)
```
Order: int                 # Sort priority (lower wins)
Pattern: string            # Glob pattern(s), comma-separated
MatchTarget: string        # "serverName"
Color: string              # Hex color "#FF4444"
Label: string              # "PRODUCTION", "STAGING", "DEV", "AZURE"
```

#### Snippet (engine model)
```
Metadata: SnippetMetadata
Variables: SnippetVariable[]
Body: string[]             # Lines of SQL with placeholders
```

#### SnippetMetadata
```
Id: string (GUID)
Shortcode: string (required, trigger text)
Name: string
Description: string
Author: string
Version: string = "1.0"
Created: DateTime (UTC)
Modified: DateTime (UTC)
Category: string = "Custom"
Tags: string[]
Context: string[] = ["global"]   # "global", "after_select", "after_from", etc.
SurroundsWith: bool              # True for wrap templates
```

#### SnippetVariable
```
Name: string          # Placeholder name (e.g., "TABLE")
Default: string       # Fallback value
Tooltip: string       # Help text
SchemaAware: string?  # "tables", "columns", "procedures" for IntelliSense
```

#### SnippetSourceType (enum)
```
Personal = 1  # User's personal snippets (highest priority)
Team = 2      # Shared team snippets
BuiltIn = 3   # Included with AKML SQL (lowest priority, read-only)
```

### New/Modified Entities

#### SafetyAuditEntry (NEW — for FR-007a audit logging)
```
Timestamp: DateTime (UTC)
ServerName: string
DatabaseName: string
Environment: string          # Label from EnvironmentRule ("PRODUCTION", etc.)
EnvironmentColor: string     # Hex color
StatementType: string        # "DELETE", "UPDATE", "DROP TABLE", "TRUNCATE TABLE"
SqlText: string              # First 500 chars of the intercepted SQL
Outcome: string              # "Blocked", "Confirmed", "Bypassed"
```
- Not persisted to database; written to Serilog log file at Warning level
- Format: structured log entry for searchability

#### SafetySettings (MODIFIED — add per-environment config)
```
+ EnvironmentSeverity: Dictionary<string, string>
  # Maps environment label → severity level
  # e.g., "PRODUCTION" → "TypeServerName", "STAGING" → "SimpleConfirm", "DEV" → "Disabled"
  # Default: PRODUCTION=TypeServerName, all others=SimpleConfirm
```

#### BookmarkInfo (NEW — session-scoped)
```
LineNumber: int          # 0-based line in the editor buffer
FilePath: string?        # Associated file (for multi-tab navigation)
TextViewId: string       # Unique ID of the IWpfTextView instance
```
- Stored in-memory only (Dictionary<string, List<BookmarkInfo>> keyed by TextViewId)
- Cleared when text view is closed

#### OutlineNodeDto (EXISTS as IPC message, engine handler needs implementation)
```
Label: string            # Display text (e.g., "CREATE PROCEDURE dbo.GetOrders")
NodeType: string         # "procedure", "function", "view", "cte", "temptable", "batch"
StartOffset: int         # Character offset in document
EndOffset: int           # End of the node's span
Children: OutlineNodeDto[]  # Nested nodes (e.g., CTEs inside a procedure)
IconType: int            # Maps to completion icon types (P=proc, V=view, F=function)
```

#### GridSortState (NEW — per-grid session state)
```
ColumnIndex: int         # Currently sorted column (-1 = none)
Direction: SortDirection # Ascending, Descending, None (3-click cycle)
```

#### GridFilterState (NEW — per-grid session state)
```
ColumnIndex: int         # Filtered column
FilterText: string       # Text match pattern
IsActive: bool           # Whether filter is currently applied
OriginalRowCount: int    # Row count before filtering
```

## Relationships

```
EnvironmentRule  ──uses──>  SafetySettings.EnvironmentSeverity
                            (label matches dictionary key)

SafetyCheckRequest  ──references──>  EnvironmentRule.Label
                                     (via IsProductionServer flag)

ExecutionInterceptor  ──logs──>  SafetyAuditEntry
                                 (via Serilog structured logging)

Snippet  ──belongs to──>  SnippetSourceType
                           (Personal > Team > BuiltIn priority)

BookmarkInfo  ──scoped to──>  IWpfTextView
                               (cleared on view close)

GridSortState  ──attached to──>  DataGridView instance
GridFilterState ──attached to──>  DataGridView instance
                                  (cleared on new query execution)
```
