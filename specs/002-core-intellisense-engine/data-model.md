# Data Model: Core IntelliSense Engine

**Branch**: `002-core-intellisense-engine` | **Date**: 2026-03-19

## Entity Relationship Overview

```
Engine 1──* Session 1──1 SchemaCache 1──* DatabaseCache
                │                              │
                │                              ├──* SchemaEntry
                │                              │       └──* DatabaseObject (Table/View/Proc/Func)
                │                              │               └──* Column
                │                              │               └──* Parameter
                │                              │               └──* Index
                │                              │
                │                              └──* ForeignKey
                │
                └──1 DocumentState
                        ├──1 ParsedDocument (AST + TokenStream)
                        └──* CursorContext
```

## Core Entities

### Engine

Singleton process managing all sessions and caches.

- **ProcessId**: OS process ID
- **PipeName**: Named pipe identifier (`akmlsql-engine-{userSid}-{parentPid}`)
- **ParentPid**: IDE process ID (for orphan protection)
- **Status**: Starting | Ready | ShuttingDown
- **Sessions**: Collection of active sessions
- **StartedAt**: Timestamp

### Session

Represents one connected editor window. Multiple sessions per engine.

- **SessionId**: Unique identifier (assigned on ConnectionChanged)
- **ConnectionString**: Active SQL connection (server, database, auth)
- **ServerVersion**: SQL Server major version (13-17) for parser/dictionary selection
- **EngineEdition**: On-prem (1-4) | Azure SQL DB (5) | Azure MI (8)
- **DatabaseName**: Current database context
- **PermissionLevel**: Full | NoDmv | InformationSchemaOnly | PublicOnly
- **DocumentState**: Current document parse state
- **SchemaCacheRef**: Reference to shared DatabaseCache for this connection
- **IsConnected**: Boolean

### DatabaseCache

Per-database schema metadata. Shared across sessions connecting to the same database on the same server.

- **CacheKey**: `{serverName}:{databaseName}` (case-insensitive)
- **PopulationPhase**: NotLoaded | PhaseA (names) | PhaseB (columns/FKs) | Complete
- **Schemas**: Collection of SchemaEntry
- **ForeignKeys**: Collection of ForeignKey (database-wide)
- **LastFullRefresh**: Timestamp
- **LastChangeChecksum**: Integer (CHECKSUM_AGG result)
- **IsStale**: Boolean (connection lost, change detected but not yet refreshed)
- **PersistedPath**: Disk cache file path (nullable, for session persistence)

**State transitions**:
```
NotLoaded → PhaseA (on connection: load databases/schemas/table names, <500ms)
PhaseA → PhaseB (background: load columns for recent tables, FKs, 1-5s)
PhaseB → Complete (lazy: remaining columns loaded on-demand)
Complete → Complete (periodic refresh via CHECKSUM_AGG poll)
Complete → PhaseA (manual refresh: full rebuild)
Any → Stale (connection lost or permission error)
```

### SchemaEntry

Groups objects within a database schema (e.g., `dbo`, `sales`).

- **SchemaName**: String
- **Objects**: Collection of DatabaseObject

### DatabaseObject

A table, view, stored procedure, function, synonym, or sequence.

- **ObjectId**: Integer (from sys.objects.object_id)
- **SchemaName**: String
- **ObjectName**: String
- **ObjectType**: Table | View | Procedure | ScalarFunction | TableFunction | InlineFunction | Synonym | Sequence
- **ModifyDate**: Timestamp (for change detection)
- **ApproxRowCount**: Long (tables/views only, from dm_db_partition_stats)
- **Description**: String (from extended properties, nullable)
- **Columns**: Collection of Column (lazy-loaded)
- **Parameters**: Collection of Parameter (procedures/functions only, lazy-loaded)
- **Indexes**: Collection of Index (tables only, lazy-loaded)
- **ColumnsLoaded**: Boolean (tracks lazy-load state)

### Column

A column within a table or view.

- **ColumnId**: Integer (ordinal from sys.columns.column_id)
- **ColumnName**: String
- **TypeName**: String (e.g., "int", "nvarchar", "decimal")
- **MaxLength**: Integer (-1 for MAX)
- **Precision**: Integer (for decimal/numeric)
- **Scale**: Integer (for decimal/numeric)
- **IsNullable**: Boolean
- **IsIdentity**: Boolean
- **IsComputed**: Boolean
- **ComputedDefinition**: String (nullable)
- **DefaultValue**: String (nullable, from default constraint)
- **IsPrimaryKey**: Boolean
- **Description**: String (from extended properties, nullable)

**Ranking order** (static heuristics): PK columns first, then FK columns, then by ColumnId (ordinal position).

### Parameter

A parameter of a stored procedure or function.

- **ParameterId**: Integer (ordinal)
- **ParameterName**: String (includes @ prefix)
- **TypeName**: String
- **MaxLength**: Integer
- **Precision**: Integer
- **Scale**: Integer
- **IsOutput**: Boolean
- **HasDefault**: Boolean
- **DefaultValue**: Object (nullable)

### ForeignKey

A foreign key relationship between two tables.

- **FkName**: String
- **ParentSchema**: String
- **ParentTable**: String
- **ParentColumns**: Ordered list of column names
- **ReferencedSchema**: String
- **ReferencedTable**: String
- **ReferencedColumns**: Ordered list of column names (matching order)
- **IsDisabled**: Boolean
- **DeleteAction**: NoAction | Cascade | SetNull | SetDefault
- **UpdateAction**: NoAction | Cascade | SetNull | SetDefault

### Index

An index on a table.

- **IndexName**: String
- **IndexType**: Clustered | Nonclustered | Heap | Columnstore
- **IsPrimaryKey**: Boolean
- **IsUnique**: Boolean
- **Columns**: Ordered list of column names with sort direction

## Parser Entities

### DocumentState

Current parse state for an editor document.

- **FullText**: String (current document content)
- **Version**: Integer (incremented on each change)
- **TokenStream**: List of TSqlParserToken (updated on every keystroke, ~60ms)
- **Ast**: TSqlScript (updated on debounced timer, ~300ms)
- **AstVersion**: Integer (version when AST was last computed)
- **Batches**: List of BatchScope (parsed from GO separators)

### BatchScope

A single batch within a document (separated by GO).

- **StartOffset**: Integer
- **EndOffset**: Integer
- **Aliases**: Dictionary of alias → TableReference
- **TempTables**: Dictionary of #name → List of Column
- **Variables**: Dictionary of @name → TypeName
- **CteDefinitions**: Dictionary of cteName → List of Column

### CursorContext

Determined at completion request time from the current cursor position.

- **CursorOffset**: Integer (character position in document)
- **CurrentBatch**: BatchScope reference
- **ClauseType**: Select | From | Where | JoinOn | GroupBy | Having | OrderBy | InsertColumns | InsertValues | UpdateSet | Delete | Create | Alter | Exec | With | Unknown
- **PrecedingToken**: Token type immediately before cursor
- **PrecedingDot**: Boolean (cursor is after a `.`)
- **DotPrefix**: String (text before the dot, e.g., "o" or "dbo" or "db.schema")
- **PartialText**: String (text being typed at cursor, for filtering)
- **InComment**: Boolean
- **InString**: Boolean
- **InSqlcmdDirective**: Boolean
- **AvailableAliases**: Dictionary from BatchScope
- **AvailableCtes**: Dictionary from BatchScope
- **AvailableTempTables**: Dictionary from BatchScope
- **AvailableVariables**: Dictionary from BatchScope

## Completion Entities

### CompletionItem

A single suggestion returned to the UI.

- **DisplayText**: String (shown in popup)
- **InsertText**: String (inserted on accept, may differ from display)
- **ObjectType**: Table | View | Column | Keyword | Snippet | Function | Procedure | Schema | Database | Variable | Alias | Parameter
- **SecondaryText**: String (data type, nullability, PK/FK badge — shown on right side)
- **SourceObject**: String (e.g., "dbo.Orders" for columns — shown in status bar)
- **MatchScore**: Integer (for ranking within filtered results)
- **IconType**: Enum matching ObjectType for UI rendering
- **SortPriority**: Integer (static heuristic rank: PK first, then FK, then ordinal)

### CompletionResult

Response to a completion request.

- **RequestId**: Integer (correlates with request)
- **Items**: List of CompletionItem
- **IsIncomplete**: Boolean (cache still loading, results may be partial)
- **FilterText**: String (the text used to filter)

### SignatureInfo

Parameter help for a function or procedure.

- **FunctionName**: String
- **Overloads**: List of SignatureOverload
- **ActiveOverload**: Integer (index)
- **ActiveParameter**: Integer (index of current param being typed)

### SignatureOverload

One signature variant.

- **Label**: String (full signature text, e.g., "CONVERT(data_type, expression [, style])")
- **Documentation**: String
- **Parameters**: List of ParameterInfo

### ParameterInfo

- **Name**: String
- **Type**: String
- **Documentation**: String
- **IsOptional**: Boolean

### QuickInfoResult

Hover tooltip data.

- **ObjectType**: String (e.g., "Table", "Column", "Variable")
- **Header**: String (e.g., "dbo.Orders (Table)")
- **Details**: List of key-value pairs (e.g., "Rows: ~1,234,567", "Columns: 12")
- **Description**: String (from extended properties, nullable)

## Communication Messages

### Shell → Engine

- **ConnectionChanged**: SessionId, ConnectionString, ServerVersion, EngineEdition, DatabaseName
- **DocumentChanged**: SessionId, ChangeType (Full | Incremental), FullText | Changes (list of {offset, oldLength, newText})
- **RequestCompletion**: SessionId, RequestId, CursorOffset, TriggerKind (Auto | Manual | AfterDot)
- **RequestSignatureHelp**: SessionId, RequestId, CursorOffset
- **RequestQuickInfo**: SessionId, RequestId, CursorOffset
- **SchemaRefreshRequest**: SessionId, RequestId

### Engine → Shell

- **CompletionResult**: RequestId, Items, IsIncomplete, FilterText
- **SignatureHelpResult**: RequestId, SignatureInfo
- **QuickInfoResult**: RequestId, QuickInfoResult
- **SchemaRefreshComplete**: RequestId, Success, ObjectCount
- **EngineStatus**: HeartbeatId, MemoryUsageMB, CachedDatabases, ActiveSessions

## Configuration Entities

### IntelliSenseSettings

Stored in `%AppData%/AKML SQL/config.json` under an `intellisense` key.

- **Enabled**: Boolean (default: true)
- **AutoTrigger**: Boolean (default: true)
- **TriggerDelayMs**: Integer (default: 100, range 50-500)
- **AfterDot**: Boolean (default: true, immediate trigger after `.`)
- **MaxSuggestions**: Integer (default: 50)
- **FuzzyMatch**: Boolean (default: true)
- **ShowDataTypes**: Boolean (default: true)
- **ShowNullability**: Boolean (default: true)
- **ShowPkFk**: Boolean (default: true)
- **AutoAlias**: Boolean (default: true)
- **JoinAssist**: Boolean (default: true)
- **KeywordCase**: Upper | Lower | PascalCase | AsIs (default: Upper)
- **DisableNativeIntelliSense**: Boolean (default: true)

### CacheSettings

Stored in `%AppData%/AKML SQL/config.json` under a `cache` key.

- **AutoRefresh**: Boolean (default: true)
- **RefreshIntervalSeconds**: Integer (default: 300, range 60-3600)
- **DetectDDL**: Boolean (default: true)
- **MaxDatabases**: Integer (default: 10)
- **LazyLoadColumns**: Boolean (default: true)
- **PersistToDisk**: Boolean (default: true)
- **PersistPath**: String (default: `%LocalAppData%/AKML SQL/cache/`)
