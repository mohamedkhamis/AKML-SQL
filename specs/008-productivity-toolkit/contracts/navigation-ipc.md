# IPC Contracts: Navigation

**Feature**: 008-productivity-toolkit | **Date**: 2026-03-24

Message type constants in the 60-63 (shell→engine) and 160-163 (engine→shell) ranges.

## Message Types

| Constant | Value | Direction | Description |
|----------|-------|-----------|-------------|
| `GetObjectDefinition` | 60 | Shell → Engine | Retrieve CREATE script for F12/Alt+F12 |
| `FindReferences` | 61 | Shell → Engine | Find all references (Shift+F12) |
| `ObjectSearch` | 62 | Shell → Engine | Quick search objects (Ctrl+T) |
| `GetObjectDefinitionResult` | 160 | Engine → Shell | CREATE script response |
| `FindReferencesResult` | 161 | Engine → Shell | Reference list response |
| `ObjectSearchResult` | 162 | Engine → Shell | Search results response |

## GetObjectDefinition (60)

```csharp
[MessagePackObject]
public class GetObjectDefinitionRequest
{
    [Key(0)] public string SessionId { get; set; }
    [Key(1)] public string ObjectName { get; set; }      // e.g., "dbo.Orders"
    [Key(2)] public string? SchemaName { get; set; }      // e.g., "dbo"
    [Key(3)] public bool PeekOnly { get; set; }           // true for Alt+F12 (truncated preview)
}
```

```csharp
[MessagePackObject]
public class GetObjectDefinitionResponse
{
    [Key(0)] public bool Success { get; set; }
    [Key(1)] public string? Definition { get; set; }      // Full CREATE script
    [Key(2)] public string? ObjectType { get; set; }      // "Table", "View", "Procedure", etc.
    [Key(3)] public string? Error { get; set; }
}
```

## FindReferences (61)

```csharp
[MessagePackObject]
public class FindReferencesRequest
{
    [Key(0)] public string SessionId { get; set; }
    [Key(1)] public string ObjectName { get; set; }
    [Key(2)] public string? SchemaName { get; set; }
}
```

```csharp
[MessagePackObject]
public class FindReferencesResponse
{
    [Key(0)] public bool Success { get; set; }
    [Key(1)] public ObjectReferenceDto[] References { get; set; }
    [Key(2)] public string? Error { get; set; }
}

[MessagePackObject]
public class ObjectReferenceDto
{
    [Key(0)] public string SchemaName { get; set; }
    [Key(1)] public string ObjectName { get; set; }
    [Key(2)] public string ObjectType { get; set; }       // "Procedure", "View", "Function", "Trigger"
    [Key(3)] public int? ReferenceLine { get; set; }
}
```

## ObjectSearch (62)

```csharp
[MessagePackObject]
public class ObjectSearchRequest
{
    [Key(0)] public string SessionId { get; set; }
    [Key(1)] public string SearchText { get; set; }       // Partial name for fuzzy match
    [Key(2)] public int MaxResults { get; set; }           // Default 50
}
```

```csharp
[MessagePackObject]
public class ObjectSearchResponse
{
    [Key(0)] public bool Success { get; set; }
    [Key(1)] public ObjectSearchResultDto[] Results { get; set; }
    [Key(2)] public string? Error { get; set; }
}

[MessagePackObject]
public class ObjectSearchResultDto
{
    [Key(0)] public string SchemaName { get; set; }
    [Key(1)] public string ObjectName { get; set; }
    [Key(2)] public string ObjectType { get; set; }       // "Table", "View", "Procedure", etc.
}
```
