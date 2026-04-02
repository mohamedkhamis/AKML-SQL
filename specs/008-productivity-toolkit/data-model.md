# Data Model: Productivity Toolkit

**Feature**: 008-productivity-toolkit | **Date**: 2026-03-24

## Entities

### 1. CommandEntry

Represents an item in the Command Palette.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | string | PK, unique | Stable command identifier (e.g., "akml.formatDocument") |
| Name | string | NOT NULL | Display name (e.g., "Format SQL Document") |
| Category | string | NOT NULL | Group (Format, Analysis, History, Refactoring, Navigation, Settings, SSMS) |
| KeyboardShortcut | string | nullable | Display hint (e.g., "Ctrl+K, Y") |
| UsageCount | int | NOT NULL, default 0 | Times invoked (for frequency ranking) |
| LastUsed | DateTime | nullable | Last invocation timestamp (for recency ranking) |

**Stored in**: In-memory registry on shell startup; `UsageCount` and `LastUsed` persisted in config.json under `commandPalette.usageCounts`.

**Lifecycle**: Registered at shell startup from all phases' OleMenuCommand instances. Usage counts updated on each invocation. Sorted by weighted score: `(0.7 * usageFrequency) + (0.3 * fuzzyMatchScore)`.

### 2. DocumentOutlineNode

Represents a structural element in the script outline tree.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Name | string | NOT NULL | Display name (e.g., "sp_GetOrders", "CTE: OrderTotals") |
| NodeType | enum | NOT NULL | Procedure, Function, CTE, TempTable, Statement, Region, Block, Trigger, View |
| StartLine | int | NOT NULL | 1-based line number in the script |
| StartOffset | int | NOT NULL | Character offset from script start |
| EndOffset | int | NOT NULL | Character offset of node end |
| NestingLevel | int | NOT NULL, default 0 | Depth in the tree hierarchy |
| Children | List | NOT NULL | Child nodes (nested blocks, statements within procedures) |

**Stored in**: Transient — rebuilt on each script parse. Sent from engine to shell via IPC as a serialized tree.

**Lifecycle**: Created by engine on DocumentOutlineRequest. Rebuilt on script edit (debounced 300ms). Discarded when tab closes.

### 3. GridExportFormat

Enum of supported export formats.

| Value | Extension | Streaming | Description |
|-------|-----------|-----------|-------------|
| Csv | .csv | Yes | Comma-separated values with headers |
| Tsv | .tsv | Yes | Tab-separated values with headers |
| Json | .json | Yes | JSON array of objects |
| Xml | .xml | Yes | XML with row elements |
| Xlsx | .xlsx | Partial | Excel workbook (ClosedXML, buffered write) |
| Html | .html | Yes | HTML table with styled headers |
| SqlInsert | .sql | Yes | INSERT INTO statements per row |
| Markdown | .md | Yes | Markdown table with aligned columns |

### 4. ConnectionAlias

A user-defined friendly name for a server connection.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| ServerName | string | PK | Actual server name (e.g., "SQL-PROD-EC-01\\INST02") |
| Alias | string | NOT NULL, unique | Friendly name (e.g., "Production East") |

**Stored in**: config.json under `navigation.connectionAliases[]`.

**Lifecycle**: Created/edited via settings dialog. Applied throughout UI by alias resolution layer. Aliases are optional — servers without aliases display raw names.

### 5. StatementRange

Identifies a single SQL statement within a script by its character offsets.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| StartOffset | int | NOT NULL | Character offset of statement start |
| EndOffset | int | NOT NULL | Character offset of statement end |
| StartLine | int | NOT NULL | 1-based line number |
| EndLine | int | NOT NULL | 1-based end line |
| StatementType | string | NOT NULL | "SELECT", "INSERT", "UPDATE", "DELETE", "CREATE", "ALTER", "EXEC", etc. |

**Stored in**: Transient — computed on demand by engine's StatementBoundaryDetector.

### 6. ObjectReference

A location where a database object is referenced.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| ReferencingObjectSchema | string | NOT NULL | Schema of the referencing object |
| ReferencingObjectName | string | NOT NULL | Name of the referencing object |
| ReferencingObjectType | string | NOT NULL | "Procedure", "View", "Function", "Trigger" |
| ReferenceLine | int | nullable | Line within the referencing object's definition |

**Stored in**: Transient — queried on demand from sys.sql_expression_dependencies.

### 7. MultiDatabaseTarget

A database selected for multi-database execution.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| DatabaseName | string | NOT NULL | Database name on the server |
| ServerName | string | NOT NULL | Server connection |
| Status | enum | NOT NULL | Pending, Executing, Success, Error |
| RowCount | long | nullable | Rows returned (on success) |
| ErrorMessage | string | nullable | Error text (on failure) |
| DurationMs | long | nullable | Execution time |

**Stored in**: In-memory during multi-database execution session.

## Configuration Schema Additions

New sections added to `AppSettings` / `config.json`:

```json
{
  "grid": {
    "findShortcut": "Ctrl+F",
    "aggregates": true,
    "nullHighlight": true,
    "rowNumbers": false,
    "freezeHeaders": true
  },
  "editorProductivity": {
    "commandPaletteShortcut": "Ctrl+Shift+P",
    "highlightOccurrences": true,
    "bracketMatching": true,
    "namedRegions": true,
    "stickyScroll": true,
    "minimap": false,
    "documentOutline": true
  },
  "executionProductivity": {
    "currentStatementShortcut": "Alt+Enter",
    "notificationThreshold": 30,
    "showExecutionTimer": true,
    "multiDatabase": true
  },
  "navigation": {
    "goToDefinition": true,
    "peekDefinition": true,
    "findReferences": true,
    "objectSearch": true,
    "connectionAliases": []
  },
  "commandPalette": {
    "usageCounts": {}
  }
}
```
