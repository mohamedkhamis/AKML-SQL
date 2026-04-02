# IPC Contracts: Grid Features

**Feature**: 008-productivity-toolkit | **Date**: 2026-03-24

## Grid Features — No IPC Required

All grid features (Find in Grid, Aggregates, Copy As, Export, Cell Edit, Column Statistics, Transpose, Null Highlight, Row Numbers, Frozen Headers) operate directly on the SSMS DataGridView in the shell process. No engine communication is needed for these features.

**Grid data access pattern**: The shell hooks into the SSMS results grid DataGridView via the DTE document window hierarchy. Selected cells, column headers, and data values are read directly from the DataGridView.DataSource.

**Export to file**: For CSV, JSON, XML, Markdown, and SQL INSERT formats, the shell generates the output directly from the grid data. For Excel (.xlsx), the shell sends the data to the engine (which has the ClosedXML library) or generates the file directly in the shell using a lightweight .xlsx writer.

## Grid Export (Optional Engine-Side)

If .xlsx export is delegated to the engine:

| Constant | Value | Direction | Description |
|----------|-------|-----------|-------------|
| `GridExport` | 68 | Shell → Engine | Export grid data to .xlsx file |
| `GridExportResult` | 168 | Engine → Shell | Export completion |

```csharp
[MessagePackObject]
public class GridExportRequest
{
    [Key(0)] public string OutputPath { get; set; }
    [Key(1)] public string[] ColumnHeaders { get; set; }
    [Key(2)] public string[][] Rows { get; set; }         // String representation of each cell
    [Key(3)] public string[] ColumnTypes { get; set; }     // "string", "int", "decimal", "datetime", "null"
    [Key(4)] public int Format { get; set; }               // GridExportFormat enum value
}

[MessagePackObject]
public class GridExportResponse
{
    [Key(0)] public bool Success { get; set; }
    [Key(1)] public string? OutputPath { get; set; }
    [Key(2)] public int RowCount { get; set; }
    [Key(3)] public string? Error { get; set; }
}
```

Note: For large result sets (> 10,000 rows), the shell should stream data to the engine in chunks rather than sending the entire grid in one message (16 MB frame limit).
