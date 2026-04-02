# IPC Contracts: Editor Productivity

**Feature**: 008-productivity-toolkit | **Date**: 2026-03-24

Message type constants in the 64-66 (shell→engine) and 164-166 (engine→shell) ranges.

## Message Types

| Constant | Value | Direction | Description |
|----------|-------|-----------|-------------|
| `DocumentOutline` | 64 | Shell → Engine | Request script structure outline |
| `StatementBoundary` | 65 | Shell → Engine | Find statement at cursor offset |
| `CrudGeneration` | 66 | Shell → Engine | Generate CRUD procedures for a table |
| `ScriptAs` | 67 | Shell → Engine | Generate script template for a table |
| `DocumentOutlineResult` | 164 | Engine → Shell | Outline tree response |
| `StatementBoundaryResult` | 165 | Engine → Shell | Statement range response |
| `CrudGenerationResult` | 166 | Engine → Shell | Generated CRUD SQL |
| `ScriptAsResult` | 167 | Engine → Shell | Generated script |

## DocumentOutline (64)

```csharp
[MessagePackObject]
public class DocumentOutlineRequest
{
    [Key(0)] public string SessionId { get; set; }
    [Key(1)] public string SqlText { get; set; }
}
```

```csharp
[MessagePackObject]
public class DocumentOutlineResponse
{
    [Key(0)] public bool Success { get; set; }
    [Key(1)] public OutlineNodeDto[] RootNodes { get; set; }
    [Key(2)] public string? Error { get; set; }
}

[MessagePackObject]
public class OutlineNodeDto
{
    [Key(0)] public string Name { get; set; }
    [Key(1)] public string NodeType { get; set; }        // "Procedure", "Function", "CTE", "TempTable", "Statement", "Region", "Block"
    [Key(2)] public int StartLine { get; set; }
    [Key(3)] public int StartOffset { get; set; }
    [Key(4)] public int EndOffset { get; set; }
    [Key(5)] public OutlineNodeDto[] Children { get; set; }
}
```

## StatementBoundary (65)

```csharp
[MessagePackObject]
public class StatementBoundaryRequest
{
    [Key(0)] public string SessionId { get; set; }
    [Key(1)] public string SqlText { get; set; }
    [Key(2)] public int CursorOffset { get; set; }       // Character offset of cursor
    [Key(3)] public bool AllStatements { get; set; }      // true = return all boundaries (for Ctrl+PageUp/Down)
}
```

```csharp
[MessagePackObject]
public class StatementBoundaryResponse
{
    [Key(0)] public bool Success { get; set; }
    [Key(1)] public StatementRangeDto? CurrentStatement { get; set; }  // Statement at cursor
    [Key(2)] public StatementRangeDto[]? AllStatements { get; set; }   // All statement ranges (if requested)
    [Key(3)] public string? Error { get; set; }
}

[MessagePackObject]
public class StatementRangeDto
{
    [Key(0)] public int StartOffset { get; set; }
    [Key(1)] public int EndOffset { get; set; }
    [Key(2)] public int StartLine { get; set; }
    [Key(3)] public int EndLine { get; set; }
    [Key(4)] public string StatementType { get; set; }
}
```

## CrudGeneration (66) / ScriptAs (67)

```csharp
[MessagePackObject]
public class CrudGenerationRequest
{
    [Key(0)] public string SessionId { get; set; }
    [Key(1)] public string SchemaName { get; set; }
    [Key(2)] public string TableName { get; set; }
}

[MessagePackObject]
public class CrudGenerationResponse
{
    [Key(0)] public bool Success { get; set; }
    [Key(1)] public string? GeneratedSql { get; set; }   // All 4 procedures in one script
    [Key(2)] public string? Error { get; set; }
}

[MessagePackObject]
public class ScriptAsRequest
{
    [Key(0)] public string SessionId { get; set; }
    [Key(1)] public string SchemaName { get; set; }
    [Key(2)] public string TableName { get; set; }
    [Key(3)] public string ScriptType { get; set; }      // "CREATE", "INSERT", "SELECT", "MERGE", "BCP"
}

[MessagePackObject]
public class ScriptAsResponse
{
    [Key(0)] public bool Success { get; set; }
    [Key(1)] public string? GeneratedScript { get; set; }
    [Key(2)] public string? Error { get; set; }
}
```

## Note: Shell-Only Features (No IPC)

These editor features run entirely in the shell with no engine communication:
- Command Palette (in-memory command registry + WPF popup)
- Highlight Occurrences (ITagger scanning buffer text)
- Bracket Matching (ITagger scanning for paired keywords)
- Named Regions (ITagger scanning for --region/--endregion comments)
- Sticky Scroll (IAdornmentLayer reading visible line context)
- Minimap (IAdornmentLayer rendering compact view)
- Grid Find, Aggregates, Export, Copy As (DataGridView interaction)
- Execution Timer (status bar update)
- Completion Notifications (Windows toast API)
- Multi-Database Execution (parallel SqlConnections)
