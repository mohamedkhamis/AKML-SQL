# Wildcard Expansion (SELECT * -> Column List) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Tab on `*` or `alias.*` in a SELECT statement shows a SQL Prompt-style checkbox popup with all columns from FROM-clause tables, allowing the user to select which columns to expand.

**Architecture:** New IPC message pair (27/127) carries `WildcardExpansionRequest`/`Response` between shell and engine. Engine reuses existing `AliasResolver` + `TokenBasedAliasExtractor` + `DatabaseCache` to resolve tables and fetch columns. Shell adds a new code-only WPF `WildcardExpansionPopup` (checkbox list) hosted in `CompletionPopupAdornment`, triggered by Tab-on-`*` detection in `CompletionController`.

**Tech Stack:** C# / .NET Framework 4.7.2 (shell), .NET 10 (engine), MessagePack IPC, WPF code-only controls, TSqlParser for tokenization

---

## File Map

### New Files

| File | Responsibility |
|------|---------------|
| `src/AkmlSql.Core/Ipc/Messages/WildcardExpansionRequest.cs` | MessagePack request DTO |
| `src/AkmlSql.Core/Ipc/Messages/WildcardExpansionResponse.cs` | MessagePack response DTO with table groups + columns |
| `src/AkmlSql.Engine/Completion/WildcardExpansionHandler.cs` | Parses SQL, resolves FROM tables, fetches columns from cache |
| `src/AkmlSql.Shell.Shared/Editor/Completion/WildcardExpansionPopup.cs` | Dark-themed WPF checkbox popup control |
| `tests/AkmlSql.Engine.Tests/Completion/WildcardExpansionHandlerTests.cs` | Unit tests for the handler |

### Modified Files

| File | Change |
|------|--------|
| `src/AkmlSql.Core/Ipc/RpcMessage.cs` | Add message type constants 27/127 |
| `src/AkmlSql.Engine/Server/PipeRpcServer.cs` | Add dispatch case for message type 27 |
| `src/AkmlSql.Shell.Shared/Editor/Completion/CompletionController.cs` | Tab-on-`*` detection + wildcard expansion flow |
| `src/AkmlSql.Shell.Shared/Editor/Completion/CompletionPopupAdornment.cs` | Host + position the new wildcard popup |

---

## Task 1: IPC Message Types (Core)

**Files:**
- Modify: `src/AkmlSql.Core/Ipc/RpcMessage.cs:89-94`
- Create: `src/AkmlSql.Core/Ipc/Messages/WildcardExpansionRequest.cs`
- Create: `src/AkmlSql.Core/Ipc/Messages/WildcardExpansionResponse.cs`

- [ ] **Step 1: Add message type constants**

In `src/AkmlSql.Core/Ipc/RpcMessage.cs`, add after line 91 (`AnalysisSettingsChanged = 26`):

```csharp
        // Shell -> Engine (Wildcard Expansion)
        public const int WildcardExpansion = 27;
```

And after line 94 (`AnalysisResult = 125`):

```csharp
        // Engine -> Shell (Wildcard Expansion)
        public const int WildcardExpansionResult = 127;
```

- [ ] **Step 2: Create WildcardExpansionRequest.cs**

Create `src/AkmlSql.Core/Ipc/Messages/WildcardExpansionRequest.cs`:

```csharp
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages;

[MessagePackObject]
public class WildcardExpansionRequest
{
    /// <summary>Session ID for schema cache lookup.</summary>
    [Key(0)]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Cursor position in the document (at or near the *).</summary>
    [Key(1)]
    public int CursorOffset { get; set; }

    /// <summary>Full document text (sent directly to avoid session sync timing issues).</summary>
    [Key(2)]
    public string DocumentText { get; set; } = string.Empty;

    /// <summary>
    /// Qualifier before the wildcard. null for bare *, "o" for o.*.
    /// </summary>
    [Key(3)]
    public string? Qualifier { get; set; }
}
```

- [ ] **Step 3: Create WildcardExpansionResponse.cs**

Create `src/AkmlSql.Core/Ipc/Messages/WildcardExpansionResponse.cs`:

```csharp
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages;

[MessagePackObject]
public class WildcardExpansionResponse
{
    [Key(0)]
    public bool Success { get; set; }

    [Key(1)]
    public WildcardTableGroup[] Tables { get; set; } = [];

    [Key(2)]
    public string? ErrorMessage { get; set; }
}

[MessagePackObject]
public class WildcardTableGroup
{
    /// <summary>Display name for the table header (e.g., "Orders").</summary>
    [Key(0)]
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Prefix for columns in the expansion text.
    /// Alias if defined, table name if not.
    /// </summary>
    [Key(1)]
    public string Qualifier { get; set; } = string.Empty;

    [Key(2)]
    public WildcardColumn[] Columns { get; set; } = [];
}

[MessagePackObject]
public class WildcardColumn
{
    [Key(0)]
    public string ColumnName { get; set; } = string.Empty;

    /// <summary>Type display string, e.g., "int, NOT NULL, PK".</summary>
    [Key(1)]
    public string TypeDisplay { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Build Core project to verify compilation**

Run:
```bash
dotnet build src/AkmlSql.Core/AkmlSql.Core.csproj -c Release -v quiet
```
Expected: Build succeeded.

---

## Task 2: Engine Handler (with TDD)

**Files:**
- Create: `src/AkmlSql.Engine/Completion/WildcardExpansionHandler.cs`
- Test: `tests/AkmlSql.Engine.Tests/Completion/WildcardExpansionHandlerTests.cs`
- Modify: `src/AkmlSql.Engine/Server/PipeRpcServer.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/AkmlSql.Engine.Tests/Completion/WildcardExpansionHandlerTests.cs`:

```csharp
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;

namespace AkmlSql.Engine.Tests.Completion;

public class WildcardExpansionHandlerTests
{
    private readonly WildcardExpansionHandler _handler;
    private readonly DatabaseCache _cache;

    public WildcardExpansionHandlerTests()
    {
        var parserService = new TsqlParserService();
        _handler = new WildcardExpansionHandler(parserService);

        // Build a test cache with Orders(OrderId, CustomerName, OrderDate)
        _cache = new DatabaseCache();
        var schema = new SchemaEntry { SchemaName = "dbo" };
        schema.Objects.Add(new DatabaseObject
        {
            SchemaName = "dbo",
            ObjectName = "Orders",
            ObjectType = DbObjectType.Table,
            ColumnsLoaded = true,
            Columns =
            [
                new Column { ColumnId = 1, ColumnName = "OrderId", TypeName = "int", IsPrimaryKey = true },
                new Column { ColumnId = 2, ColumnName = "CustomerName", TypeName = "nvarchar", MaxLength = 100, IsNullable = true },
                new Column { ColumnId = 3, ColumnName = "OrderDate", TypeName = "datetime" }
            ]
        });
        schema.Objects.Add(new DatabaseObject
        {
            SchemaName = "dbo",
            ObjectName = "OrderDetails",
            ObjectType = DbObjectType.Table,
            ColumnsLoaded = true,
            Columns =
            [
                new Column { ColumnId = 1, ColumnName = "DetailId", TypeName = "int", IsPrimaryKey = true },
                new Column { ColumnId = 2, ColumnName = "OrderId", TypeName = "int" },
                new Column { ColumnId = 3, ColumnName = "ProductId", TypeName = "int" },
                new Column { ColumnId = 4, ColumnName = "Quantity", TypeName = "int" }
            ]
        });
        _cache.Schemas["dbo"] = schema;
    }

    [Fact]
    public void BareWildcard_SingleTable_ReturnsAllColumns()
    {
        var sql = "SELECT * FROM Orders";
        var result = _handler.Handle(sql, cursorOffset: 9, qualifier: null, _cache);

        Assert.True(result.Success);
        Assert.Single(result.Tables);
        Assert.Equal("Orders", result.Tables[0].TableName);
        Assert.Equal(3, result.Tables[0].Columns.Length);
        Assert.Equal("OrderId", result.Tables[0].Columns[0].ColumnName);
        Assert.Equal("CustomerName", result.Tables[0].Columns[1].ColumnName);
        Assert.Equal("OrderDate", result.Tables[0].Columns[2].ColumnName);
    }

    [Fact]
    public void BareWildcard_SingleTableNoAlias_QualifierIsTableName()
    {
        var sql = "SELECT * FROM Orders";
        var result = _handler.Handle(sql, cursorOffset: 9, qualifier: null, _cache);

        Assert.True(result.Success);
        Assert.Equal("Orders", result.Tables[0].Qualifier);
    }

    [Fact]
    public void BareWildcard_AliasedTable_QualifierIsAlias()
    {
        var sql = "SELECT * FROM Orders o";
        var result = _handler.Handle(sql, cursorOffset: 9, qualifier: null, _cache);

        Assert.True(result.Success);
        Assert.Equal("o", result.Tables[0].Qualifier);
    }

    [Fact]
    public void BareWildcard_MultipleTables_ReturnsAllTableGroups()
    {
        var sql = "SELECT * FROM Orders o JOIN OrderDetails od ON o.OrderId = od.OrderId";
        var result = _handler.Handle(sql, cursorOffset: 9, qualifier: null, _cache);

        Assert.True(result.Success);
        Assert.Equal(2, result.Tables.Length);
    }

    [Fact]
    public void QualifiedWildcard_ReturnsOnlyQualifiedTable()
    {
        var sql = "SELECT o.* FROM Orders o JOIN OrderDetails od ON o.OrderId = od.OrderId";
        var result = _handler.Handle(sql, cursorOffset: 11, qualifier: "o", _cache);

        Assert.True(result.Success);
        Assert.Single(result.Tables);
        Assert.Equal("Orders", result.Tables[0].TableName);
        Assert.Equal("o", result.Tables[0].Qualifier);
    }

    [Fact]
    public void ColumnsNotLoaded_ReturnsFailure()
    {
        var cacheNoColumns = new DatabaseCache();
        var schema = new SchemaEntry { SchemaName = "dbo" };
        schema.Objects.Add(new DatabaseObject
        {
            SchemaName = "dbo",
            ObjectName = "Orders",
            ObjectType = DbObjectType.Table,
            ColumnsLoaded = false
        });
        cacheNoColumns.Schemas["dbo"] = schema;

        var sql = "SELECT * FROM Orders";
        var result = _handler.Handle(sql, cursorOffset: 9, qualifier: null, cacheNoColumns);

        Assert.False(result.Success);
    }

    [Fact]
    public void NullCache_ReturnsFailure()
    {
        var sql = "SELECT * FROM Orders";
        var result = _handler.Handle(sql, cursorOffset: 9, qualifier: null, cache: null);

        Assert.False(result.Success);
    }

    [Fact]
    public void TableNotInCache_ReturnsFailure()
    {
        var emptyCache = new DatabaseCache();
        emptyCache.Schemas["dbo"] = new SchemaEntry { SchemaName = "dbo" };

        var sql = "SELECT * FROM UnknownTable";
        var result = _handler.Handle(sql, cursorOffset: 9, qualifier: null, emptyCache);

        Assert.False(result.Success);
    }

    [Fact]
    public void TypeDisplay_FormatsCorrectly()
    {
        var sql = "SELECT * FROM Orders";
        var result = _handler.Handle(sql, cursorOffset: 9, qualifier: null, _cache);

        Assert.True(result.Success);
        // OrderId: int, NOT NULL, PK
        Assert.Contains("PK", result.Tables[0].Columns[0].TypeDisplay);
        // CustomerName: nvarchar(100), NULL
        Assert.Contains("NULL", result.Tables[0].Columns[1].TypeDisplay);
    }

    [Fact]
    public void PkColumnsFirst_ThenByOrdinal()
    {
        var sql = "SELECT * FROM Orders";
        var result = _handler.Handle(sql, cursorOffset: 9, qualifier: null, _cache);

        Assert.True(result.Success);
        // PK column should be first
        Assert.True(result.Tables[0].Columns[0].ColumnName == "OrderId");
    }

    [Fact]
    public void SchemaQualifiedTable_ResolvesCorrectly()
    {
        var sql = "SELECT * FROM dbo.Orders";
        var result = _handler.Handle(sql, cursorOffset: 9, qualifier: null, _cache);

        Assert.True(result.Success);
        Assert.Single(result.Tables);
        Assert.Equal("Orders", result.Tables[0].TableName);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:
```bash
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "FullyQualifiedName~WildcardExpansionHandlerTests" -v minimal
```
Expected: Build error — `WildcardExpansionHandler` class does not exist.

- [ ] **Step 3: Implement WildcardExpansionHandler**

Create `src/AkmlSql.Engine/Completion/WildcardExpansionHandler.cs`:

```csharp
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using Serilog;

namespace AkmlSql.Engine.Completion;

/// <summary>
/// Handles wildcard expansion requests. Parses SQL to find FROM-clause tables,
/// resolves aliases, fetches columns from schema cache, returns grouped column data.
/// </summary>
public class WildcardExpansionHandler
{
    private readonly TsqlParserService _parserService;
    private readonly AliasResolver _aliasResolver = new();

    public WildcardExpansionHandler(TsqlParserService parserService)
    {
        _parserService = parserService;
    }

    /// <summary>
    /// Resolve FROM-clause tables and return their columns grouped by table.
    /// </summary>
    /// <param name="documentText">Full SQL document text.</param>
    /// <param name="cursorOffset">Cursor position (at or near the *).</param>
    /// <param name="qualifier">null for bare *, alias name for qualified (e.g., "o" for o.*).</param>
    /// <param name="cache">Schema cache with column data.</param>
    public WildcardExpansionResponse Handle(string documentText, int cursorOffset, string? qualifier, DatabaseCache? cache)
    {
        if (cache == null)
        {
            return new WildcardExpansionResponse { Success = false, ErrorMessage = "No schema cache available" };
        }

        // Resolve aliases: try AST first, fall back to token-based
        var aliases = ResolveAliases(documentText, cursorOffset);
        if (aliases.Count == 0)
        {
            return new WildcardExpansionResponse { Success = false, ErrorMessage = "No tables found in FROM clause" };
        }

        // Filter by qualifier if specified
        Dictionary<string, string> targetAliases;
        if (!string.IsNullOrEmpty(qualifier))
        {
            if (aliases.TryGetValue(qualifier, out var fullName))
            {
                targetAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { qualifier, fullName }
                };
            }
            else
            {
                return new WildcardExpansionResponse { Success = false, ErrorMessage = $"Qualifier '{qualifier}' not found" };
            }
        }
        else
        {
            targetAliases = aliases;
        }

        // Build table groups with columns
        var tableGroups = new List<WildcardTableGroup>();

        foreach (var (aliasOrTable, fullTableName) in targetAliases)
        {
            var parts = fullTableName.Split('.');
            var schemaName = parts.Length >= 2 ? parts[0] : "dbo";
            var tableName = parts.Length >= 2 ? parts[1] : parts[0];

            // Skip derived tables
            if (tableName.StartsWith("(derived:"))
                continue;

            var dbObject = cache.FindObject(schemaName, tableName);
            if (dbObject == null)
            {
                Log.Debug("WildcardExpansion: table {Schema}.{Table} not in cache", schemaName, tableName);
                continue;
            }

            if (!dbObject.ColumnsLoaded || dbObject.Columns.Count == 0)
            {
                Log.Debug("WildcardExpansion: columns not loaded for {Table}", dbObject.FullName);
                continue;
            }

            // Order: PK first, then by ordinal (ColumnId)
            var orderedColumns = dbObject.Columns
                .OrderByDescending(c => c.IsPrimaryKey)
                .ThenBy(c => c.ColumnId)
                .ToList();

            var columns = orderedColumns.Select(c => new WildcardColumn
            {
                ColumnName = c.ColumnName,
                TypeDisplay = FormatTypeDisplay(c)
            }).ToArray();

            tableGroups.Add(new WildcardTableGroup
            {
                TableName = tableName,
                Qualifier = aliasOrTable,
                Columns = columns
            });
        }

        if (tableGroups.Count == 0)
        {
            return new WildcardExpansionResponse { Success = false, ErrorMessage = "No columns available for resolved tables" };
        }

        return new WildcardExpansionResponse
        {
            Success = true,
            Tables = tableGroups.ToArray()
        };
    }

    private Dictionary<string, string> ResolveAliases(string documentText, int cursorOffset)
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Try AST-based resolution first
        var script = _parserService.ParseWithSuffix(documentText, out _);
        if (script != null)
        {
            var resolved = _aliasResolver.ResolveAliases(script, cursorOffset);
            foreach (var (alias, tableRef) in resolved)
                aliases[alias] = tableRef.FullName;
        }

        // Fallback to token-based if AST produced nothing
        if (aliases.Count == 0)
        {
            var tokens = _parserService.GetTokenStream(documentText);
            var fallback = TokenBasedAliasExtractor.Extract(tokens, cursorOffset);
            foreach (var (alias, fullName) in fallback)
                aliases[alias] = fullName;
        }

        return aliases;
    }

    private static string FormatTypeDisplay(Column column)
    {
        var parts = new List<string>(4) { column.TypeDisplay };
        parts.Add(column.IsNullable ? "NULL" : "NOT NULL");
        if (column.IsPrimaryKey) parts.Add("PK");
        if (column.IsIdentity) parts.Add("IDENTITY");
        if (column.IsComputed) parts.Add("COMPUTED");
        return string.Join(", ", parts);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run:
```bash
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "FullyQualifiedName~WildcardExpansionHandlerTests" -v minimal
```
Expected: All 11 tests PASS.

- [ ] **Step 5: Register handler in PipeRpcServer**

In `src/AkmlSql.Engine/Server/PipeRpcServer.cs`:

Add field after line 38 (`private readonly CompletionEngine _completionEngine;`):

```csharp
    private readonly WildcardExpansionHandler _wildcardHandler;
```

Add initialization in constructor after line 63 (`_completionEngine = new CompletionEngine(_parserService);`):

```csharp
        _wildcardHandler = new WildcardExpansionHandler(_parserService);
```

Add dispatch case in `DispatchAsync()` method, after the `RequestCompletion` case (after line 230):

```csharp
                case MessageTypes.WildcardExpansion:
                    if (message.Payload == null)
                    {
                        return Task.FromResult(CreateErrorResponse("Payload required", message.RequestId));
                    }

                    var wcReq = MessagePackSerializer.Deserialize<WildcardExpansionRequest>(message.Payload);
                    var wcSession = _sessionManager.GetSession(wcReq.SessionId);
                    var wcCache = wcSession != null
                        ? _schemaCacheManager.GetCache(wcReq.SessionId, wcSession.DatabaseName)
                        : null;
                    var wcResp = _wildcardHandler.Handle(
                        wcReq.DocumentText, wcReq.CursorOffset, wcReq.Qualifier, wcCache);
                    return Task.FromResult(CreateResponse(MessageTypes.WildcardExpansionResult, message.RequestId, wcResp));
```

- [ ] **Step 6: Build Engine project to verify compilation**

Run:
```bash
dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -v quiet
```
Expected: Build succeeded.

---

## Task 3: WPF Checkbox Popup (Shell.Shared)

**Files:**
- Create: `src/AkmlSql.Shell.Shared/Editor/Completion/WildcardExpansionPopup.cs`

- [ ] **Step 1: Create WildcardExpansionPopup**

Create `src/AkmlSql.Shell.Shared/Editor/Completion/WildcardExpansionPopup.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AkmlSql.Shell.Shared.Editor.Completion
{
    /// <summary>
    /// SQL Prompt-style checkbox popup for wildcard expansion.
    /// Dark themed, code-only WPF (no XAML). Shows columns grouped by table
    /// with checkboxes for selecting which columns to include in the expansion.
    /// </summary>
    internal sealed class WildcardExpansionPopup : Border
    {
        private readonly StackPanel _root;
        private readonly StackPanel _itemsPanel;
        private readonly TextBlock _footer;
        private bool _isOpen;

        private readonly List<ColumnRow> _columnRows = new List<ColumnRow>();
        private int _selectedIndex = -1;

        // Table group data for building expansion text
        private List<TableGroupData> _tableGroups = new List<TableGroupData>();

        private const double PopupWidth = 420;
        private const double ItemHeight = 22;
        private const int MaxVisibleItems = 18;

        // SQL Prompt dark theme colors (same as AkmlCompletionPopup)
        private static readonly SolidColorBrush BgBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)));
        private static readonly SolidColorBrush BorderBrush_ = Freeze(new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)));
        private static readonly SolidColorBrush SelectedBg = Freeze(new SolidColorBrush(Color.FromRgb(0x09, 0x47, 0x71)));
        private static readonly SolidColorBrush TextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4)));
        private static readonly SolidColorBrush DimTextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x6A)));
        private static readonly SolidColorBrush SecondaryBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)));
        private static readonly SolidColorBrush FooterBg = Freeze(new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)));
        private static readonly SolidColorBrush HeaderBg = Freeze(new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)));
        private static readonly SolidColorBrush HoverBg = Freeze(new SolidColorBrush(Color.FromRgb(0x2A, 0x2D, 0x2E)));
        private static readonly SolidColorBrush ColumnBadgeBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF9, 0xA8, 0x25))); // Gold
        private static readonly SolidColorBrush CheckMarkBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x53))); // Green check

        public WildcardExpansionPopup()
        {
            _itemsPanel = new StackPanel();

            var scrollViewer = new ScrollViewer
            {
                Content = _itemsPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = MaxVisibleItems * ItemHeight,
                Focusable = false,
                Background = BgBrush
            };

            _footer = new TextBlock
            {
                Foreground = SecondaryBrush,
                FontSize = 11,
                Padding = new Thickness(8, 3, 8, 3),
                Background = FooterBg,
                Text = "Space: toggle | Tab/Enter: expand | Esc: cancel"
            };

            _root = new StackPanel();
            _root.Children.Add(scrollViewer);
            _root.Children.Add(_footer);

            Background = BgBrush;
            BorderBrush = BorderBrush_;
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(3);
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 12,
                ShadowDepth = 4,
                Opacity = 0.5,
                Color = Colors.Black
            };
            Child = _root;
            Width = PopupWidth;
            Focusable = false;
        }

        public bool IsOpen => _isOpen;

        /// <summary>
        /// Populate the popup with table groups and their columns.
        /// All columns are checked by default.
        /// </summary>
        public void SetData(IEnumerable<TableGroupData> groups)
        {
            _tableGroups = groups.ToList();
            _columnRows.Clear();
            _itemsPanel.Children.Clear();
            _selectedIndex = -1;

            bool multiTable = _tableGroups.Count > 1;

            foreach (var group in _tableGroups)
            {
                // Table header (only show for multi-table)
                if (multiTable)
                {
                    var header = CreateTableHeader(group.TableName);
                    _itemsPanel.Children.Add(header);
                }

                foreach (var col in group.Columns)
                {
                    var row = new ColumnRow
                    {
                        IsChecked = true,
                        ColumnName = col.ColumnName,
                        TypeDisplay = col.TypeDisplay,
                        Qualifier = group.Qualifier
                    };
                    row.Visual = CreateColumnVisual(row);
                    _columnRows.Add(row);
                    _itemsPanel.Children.Add(row.Visual);
                }
            }

            if (_columnRows.Count > 0)
            {
                _selectedIndex = 0;
                UpdateSelection();
            }

            UpdateFooter();
            _isOpen = true;
        }

        /// <summary>Move selection up (-1) or down (+1). Wraps at boundaries.</summary>
        public void MoveSelection(int delta)
        {
            if (_columnRows.Count == 0) return;
            _selectedIndex += delta;
            if (_selectedIndex < 0) _selectedIndex = _columnRows.Count - 1;
            if (_selectedIndex >= _columnRows.Count) _selectedIndex = 0;
            UpdateSelection();
        }

        /// <summary>Toggle checkbox on the currently selected row.</summary>
        public void ToggleSelected()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _columnRows.Count) return;
            var row = _columnRows[_selectedIndex];
            row.IsChecked = !row.IsChecked;
            UpdateRowVisual(row);
            UpdateFooter();
        }

        /// <summary>Check all columns.</summary>
        public void CheckAll()
        {
            foreach (var row in _columnRows)
            {
                row.IsChecked = true;
                UpdateRowVisual(row);
            }
            UpdateFooter();
        }

        /// <summary>Uncheck all columns.</summary>
        public void UncheckAll()
        {
            foreach (var row in _columnRows)
            {
                row.IsChecked = false;
                UpdateRowVisual(row);
            }
            UpdateFooter();
        }

        /// <summary>
        /// Get the checked columns as qualifier.column pairs, preserving table group order.
        /// Returns null if no columns are checked.
        /// </summary>
        public List<QualifiedColumn> GetCheckedColumns()
        {
            var result = new List<QualifiedColumn>();
            foreach (var row in _columnRows)
            {
                if (row.IsChecked)
                {
                    result.Add(new QualifiedColumn
                    {
                        Qualifier = row.Qualifier,
                        ColumnName = row.ColumnName
                    });
                }
            }
            return result.Count > 0 ? result : null;
        }

        /// <summary>Hide the popup and reset state.</summary>
        public void Hide()
        {
            _isOpen = false;
            _columnRows.Clear();
            _itemsPanel.Children.Clear();
            _selectedIndex = -1;
        }

        private UIElement CreateTableHeader(string tableName)
        {
            var header = new Border
            {
                Background = HeaderBg,
                Padding = new Thickness(8, 3, 8, 3),
                Child = new TextBlock
                {
                    Text = tableName,
                    Foreground = TextBrush,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    FontFamily = new FontFamily("Consolas")
                }
            };
            return header;
        }

        private Border CreateColumnVisual(ColumnRow row)
        {
            // Checkbox indicator
            var checkBox = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(2),
                BorderBrush = SecondaryBrush,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(6, 0, 4, 0),
                Background = BgBrush,
                Child = new TextBlock
                {
                    Text = "\u2713",  // checkmark
                    Foreground = CheckMarkBrush,
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Visibility = row.IsChecked ? Visibility.Visible : Visibility.Collapsed
                },
                Tag = "checkbox"
            };

            // Column badge (gold "C")
            var badge = new Border
            {
                Width = 18,
                Height = 16,
                CornerRadius = new CornerRadius(2),
                Background = ColumnBadgeBrush,
                Margin = new Thickness(2, 0, 6, 0),
                Child = new TextBlock
                {
                    Text = "C",
                    Foreground = Brushes.White,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            // Column name
            var nameText = new TextBlock
            {
                Text = row.ColumnName,
                Foreground = row.IsChecked ? TextBrush : DimTextBrush,
                FontSize = 12,
                FontFamily = new FontFamily("Consolas"),
                VerticalAlignment = VerticalAlignment.Center,
                Tag = "name"
            };

            // Type info
            var typeText = new TextBlock
            {
                Text = row.TypeDisplay,
                Foreground = SecondaryBrush,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(8, 0, 4, 0),
                Tag = "type"
            };

            var grid = new Grid { Height = ItemHeight };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // checkbox
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // badge
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // name
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // type

            Grid.SetColumn(checkBox, 0);
            Grid.SetColumn(badge, 1);
            Grid.SetColumn(nameText, 2);
            Grid.SetColumn(typeText, 3);

            grid.Children.Add(checkBox);
            grid.Children.Add(badge);
            grid.Children.Add(nameText);
            grid.Children.Add(typeText);

            var container = new Border
            {
                Child = grid,
                Background = Brushes.Transparent,
                Padding = new Thickness(0)
            };

            return container;
        }

        private void UpdateRowVisual(ColumnRow row)
        {
            if (row.Visual == null) return;
            var grid = (Grid)row.Visual.Child;

            // Update checkbox visibility
            var checkBorder = (Border)grid.Children[0];
            var checkMark = (TextBlock)checkBorder.Child;
            checkMark.Visibility = row.IsChecked ? Visibility.Visible : Visibility.Collapsed;

            // Update name text color (dim if unchecked)
            var nameText = (TextBlock)grid.Children[2];
            nameText.Foreground = row.IsChecked ? TextBrush : DimTextBrush;
        }

        private void UpdateSelection()
        {
            for (int i = 0; i < _columnRows.Count; i++)
            {
                var row = _columnRows[i];
                if (row.Visual != null)
                {
                    row.Visual.Background = (i == _selectedIndex) ? SelectedBg : Brushes.Transparent;
                }
            }

            // Scroll into view
            if (_selectedIndex >= 0 && _selectedIndex < _columnRows.Count)
            {
                var visual = _columnRows[_selectedIndex].Visual;
                visual?.BringIntoView();
            }
        }

        private void UpdateFooter()
        {
            int total = _columnRows.Count;
            int checkedCount = _columnRows.Count(r => r.IsChecked);
            _footer.Text = $"{checkedCount}/{total} columns selected | Space: toggle | Tab: expand";
        }

        private static SolidColorBrush Freeze(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }

        // Internal data types
        internal class ColumnRow
        {
            public bool IsChecked;
            public string ColumnName = string.Empty;
            public string TypeDisplay = string.Empty;
            public string Qualifier = string.Empty;
            public Border Visual;
        }

        internal class QualifiedColumn
        {
            public string Qualifier = string.Empty;
            public string ColumnName = string.Empty;
        }

        internal class TableGroupData
        {
            public string TableName = string.Empty;
            public string Qualifier = string.Empty;
            public ColumnData[] Columns = Array.Empty<ColumnData>();
        }

        internal class ColumnData
        {
            public string ColumnName = string.Empty;
            public string TypeDisplay = string.Empty;
        }
    }
}
```

- [ ] **Step 2: Build one shell project to verify compilation**

Run:
```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"
"$MSBUILD" "src/AkmlSql.VS2022/AkmlSql.VS2022.csproj" -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" "src/AkmlSql.VS2022/AkmlSql.VS2022.csproj" -t:Build -p:Configuration=Release -v:minimal
```
Expected: Build succeeded.

---

## Task 4: CompletionPopupAdornment — Host Wildcard Popup

**Files:**
- Modify: `src/AkmlSql.Shell.Shared/Editor/Completion/CompletionPopupAdornment.cs`

- [ ] **Step 1: Add wildcard popup to CompletionPopupAdornment**

In `src/AkmlSql.Shell.Shared/Editor/Completion/CompletionPopupAdornment.cs`:

Add a new field after line 20 (`private readonly Popup _popup;`):

```csharp
        private readonly WildcardExpansionPopup _wildcardContent;
        private readonly Popup _wildcardPopup;
```

Add public accessor after line 22 (`public AkmlCompletionPopup Popup => _popupContent;`):

```csharp
        public WildcardExpansionPopup WildcardPopup => _wildcardContent;
```

Add initialization at the end of the constructor (before line 48, the event subscriptions):

```csharp
            // Wildcard expansion popup (checkbox list)
            _wildcardContent = new WildcardExpansionPopup();
            _wildcardPopup = new Popup
            {
                Child = _wildcardContent,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.None,
                Placement = PlacementMode.Custom,
                CustomPopupPlacementCallback = PlacePopup,
                StaysOpen = true,
                Focusable = false,
                IsOpen = false
            };
            _wildcardContent.Visibility = Visibility.Visible;
```

Add Show/Hide/Reposition methods for the wildcard popup after the `Reposition()` method (after line 73):

```csharp
        /// <summary>Show the wildcard expansion popup at the current caret position.</summary>
        public void ShowWildcard()
        {
            _wildcardPopup.PlacementTarget = _textView.VisualElement;
            _wildcardPopup.IsOpen = true;
        }

        /// <summary>Hide the wildcard expansion popup.</summary>
        public void HideWildcard()
        {
            _wildcardPopup.IsOpen = false;
            _wildcardContent.Hide();
        }

        /// <summary>Reposition the wildcard popup at the current caret.</summary>
        public void RepositionWildcard()
        {
            if (_wildcardPopup.IsOpen)
            {
                _wildcardPopup.HorizontalOffset += 0.01;
                _wildcardPopup.HorizontalOffset -= 0.01;
            }
        }

        /// <summary>True if the wildcard expansion popup is currently showing.</summary>
        public bool IsWildcardOpen => _wildcardPopup.IsOpen && _wildcardContent.IsOpen;
```

Update `OnLayoutChanged` (line 133-137) to also reposition wildcard:

```csharp
        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            if (_popup.IsOpen)
                Reposition();
            if (_wildcardPopup.IsOpen)
                RepositionWildcard();
        }
```

Update `OnClosed` (line 139-144) to also hide wildcard:

```csharp
        private void OnClosed(object sender, EventArgs e)
        {
            _textView.LayoutChanged -= OnLayoutChanged;
            _textView.Closed -= OnClosed;
            Hide();
            HideWildcard();
        }
```

Also update the `LostAggregateFocus` handler in the constructor (line 47) to dismiss wildcard:

```csharp
            _textView.LostAggregateFocus += (s, e) => { Hide(); HideWildcard(); };
```

---

## Task 5: CompletionController — Tab Detection + Expansion Flow

**Files:**
- Modify: `src/AkmlSql.Shell.Shared/Editor/Completion/CompletionController.cs`

- [ ] **Step 1: Add wildcard expansion state fields**

In `src/AkmlSql.Shell.Shared/Editor/Completion/CompletionController.cs`, add after line 28 (`private System.Windows.Threading.DispatcherTimer _suppressTimer;`):

```csharp
        private bool _wildcardPending;
```

- [ ] **Step 2: Add Tab-on-star detection in the Exec method**

Replace the Tab/Enter case (lines 110-121):

```csharp
                    case VSConstants.VSStd2KCmdID.RETURN:
                    case VSConstants.VSStd2KCmdID.TAB:
                        // Wildcard expansion popup is open — commit checked columns
                        if (_adornment.IsWildcardOpen)
                        {
                            CommitWildcardExpansion();
                            return VSConstants.S_OK;
                        }
                        // Completion popup is open — commit selected item
                        if (_adornment.Popup.IsOpen)
                        {
                            var item = _adornment.Popup.GetSelectedItem();
                            if (item != null)
                            {
                                CommitItem(item);
                                return VSConstants.S_OK;
                            }
                        }
                        // Tab only: check for wildcard at cursor
                        if (cmdId == VSConstants.VSStd2KCmdID.TAB)
                        {
                            var wildcardInfo = DetectWildcardAtCursor();
                            if (wildcardInfo != null)
                            {
                                TriggerWildcardExpansion(wildcardInfo.Value.starPos, wildcardInfo.Value.qualifier);
                                return VSConstants.S_OK;
                            }
                        }
                        break;
```

- [ ] **Step 3: Add Space/Escape handling for wildcard popup**

In the CANCEL case (lines 123-128), add wildcard dismiss:

```csharp
                    case VSConstants.VSStd2KCmdID.CANCEL:
                        if (_adornment.IsWildcardOpen)
                        {
                            DismissWildcardPopup();
                            return VSConstants.S_OK;
                        }
                        if (_adornment.Popup.IsOpen)
                        {
                            DismissPopup();
                            return VSConstants.S_OK;
                        }
                        break;
```

In the UP case (lines 131-135), add wildcard navigation:

```csharp
                    case VSConstants.VSStd2KCmdID.UP:
                        if (_adornment.IsWildcardOpen)
                        {
                            _adornment.WildcardPopup.MoveSelection(-1);
                            return VSConstants.S_OK;
                        }
                        if (_adornment.Popup.IsOpen)
                        {
                            _adornment.Popup.MoveSelection(-1);
                            return VSConstants.S_OK;
                        }
                        break;
```

In the DOWN case (lines 139-145), add wildcard navigation:

```csharp
                    case VSConstants.VSStd2KCmdID.DOWN:
                        if (_adornment.IsWildcardOpen)
                        {
                            _adornment.WildcardPopup.MoveSelection(1);
                            return VSConstants.S_OK;
                        }
                        if (_adornment.Popup.IsOpen)
                        {
                            _adornment.Popup.MoveSelection(1);
                            return VSConstants.S_OK;
                        }
                        break;
```

- [ ] **Step 4: Handle Space for checkbox toggle and Ctrl+A/Ctrl+D**

In the TYPECHAR handler (line 94), add Space handling when wildcard popup is open. Insert before the `// Let VS insert the character` line:

```csharp
                        // Space toggles checkbox in wildcard popup
                        if (typedChar == ' ' && _adornment.IsWildcardOpen)
                        {
                            _adornment.WildcardPopup.ToggleSelected();
                            return VSConstants.S_OK; // Don't insert space
                        }
```

For Ctrl+A and Ctrl+D, add handling in the Exec method after the TYPECHAR block. These come as separate command group entries. Add before the `var finalResult` line (line 177):

```csharp
            // Handle Ctrl+A (Select All) / Ctrl+D (Deselect All) for wildcard popup
            if (_adornment.IsWildcardOpen && pguidCmdGroup == VSConstants.VSStd2K)
            {
                var cmdId2k = (VSConstants.VSStd2KCmdID)nCmdId;
                if (cmdId2k == VSConstants.VSStd2KCmdID.SELECTALL)
                {
                    _adornment.WildcardPopup.CheckAll();
                    return VSConstants.S_OK;
                }
            }
            if (_adornment.IsWildcardOpen && pguidCmdGroup == VSConstants.GUID_VSStandardCommandSet97)
            {
                // Ctrl+D may arrive as VSStd97 command
                var cmdId97 = (VSConstants.VSStd97CmdID)nCmdId;
                if (cmdId97 == VSConstants.VSStd97CmdID.SelectAll)
                {
                    _adornment.WildcardPopup.CheckAll();
                    return VSConstants.S_OK;
                }
            }
```

- [ ] **Step 5: Add DetectWildcardAtCursor method**

Add after the `IsIdentifierChar` method (line 581):

```csharp
        /// <summary>
        /// Detects if the cursor is at a SELECT wildcard (* or alias.*).
        /// Returns the star position and optional qualifier, or null if not a wildcard.
        /// </summary>
        private (int starPos, string qualifier)? DetectWildcardAtCursor()
        {
            try
            {
                var snapshot = _textView.TextBuffer.CurrentSnapshot;
                var caretPos = _textView.Caret.Position.BufferPosition.Position;
                int length = snapshot.Length;

                // Find the * character at or adjacent to cursor
                int starPos = -1;
                if (caretPos > 0 && caretPos <= length && snapshot[caretPos - 1] == '*')
                {
                    starPos = caretPos - 1; // Cursor right after *
                }
                else if (caretPos < length && snapshot[caretPos] == '*')
                {
                    starPos = caretPos; // Cursor right before *
                }

                if (starPos < 0) return null;

                // Check for qualified wildcard: identifier.* 
                string qualifier = null;
                if (starPos >= 2 && snapshot[starPos - 1] == '.')
                {
                    int idEnd = starPos - 2;
                    int idStart = idEnd;
                    while (idStart > 0 && IsIdentifierChar(snapshot[idStart - 1]))
                        idStart--;

                    if (idStart <= idEnd)
                    {
                        qualifier = snapshot.GetText(idStart, idEnd - idStart + 1);
                    }
                }

                // Verify SELECT context: scan backwards for SELECT keyword
                if (!IsInSelectContext(snapshot, starPos))
                    return null;

                return (starPos, qualifier);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Verify that the * at starPos is in a SELECT context (not arithmetic).
        /// Scans backwards skipping whitespace, DISTINCT, TOP N to find SELECT keyword.
        /// </summary>
        private static bool IsInSelectContext(Microsoft.VisualStudio.Text.ITextSnapshot snapshot, int starPos)
        {
            int pos = starPos - 1;

            // Skip qualifier.* prefix if present
            if (pos >= 0 && snapshot[pos] == '.')
            {
                pos--;
                while (pos >= 0 && IsIdentifierChar(snapshot[pos]))
                    pos--;
            }

            // Skip whitespace and commas (handles "SELECT col1, *")
            while (pos >= 0 && (snapshot[pos] == ' ' || snapshot[pos] == '\t' ||
                                snapshot[pos] == '\r' || snapshot[pos] == '\n' ||
                                snapshot[pos] == ','))
                pos--;

            // Now extract the word at this position
            int wordEnd = pos;
            while (pos >= 0 && char.IsLetter(snapshot[pos]))
                pos--;
            pos++;

            if (pos > wordEnd) return false;
            var word = snapshot.GetText(pos, wordEnd - pos + 1).ToUpperInvariant();

            // Direct SELECT before the *
            if (word == "SELECT") return true;

            // DISTINCT or ALL after SELECT
            if (word == "DISTINCT" || word == "ALL")
            {
                return HasSelectBefore(snapshot, pos);
            }

            // TOP N — check for SELECT before TOP
            if (word == "TOP")
            {
                return HasSelectBefore(snapshot, pos);
            }

            // Could be after a comma in the select list (SELECT col1, *)
            // In this case we need to find SELECT further back
            // The word we found might be a column name — walk further back
            // to find SELECT or FROM. If we find FROM first, it's not a select wildcard.
            return FindSelectBeforePosition(snapshot, pos);
        }

        private static bool HasSelectBefore(Microsoft.VisualStudio.Text.ITextSnapshot snapshot, int pos)
        {
            pos--;
            while (pos >= 0 && char.IsWhiteSpace(snapshot[pos]))
                pos--;

            // Skip a number (TOP 10)
            while (pos >= 0 && char.IsDigit(snapshot[pos]))
                pos--;
            while (pos >= 0 && char.IsWhiteSpace(snapshot[pos]))
                pos--;

            int wordEnd = pos;
            while (pos >= 0 && char.IsLetter(snapshot[pos]))
                pos--;
            pos++;

            if (pos > wordEnd) return false;
            var word = snapshot.GetText(pos, wordEnd - pos + 1).ToUpperInvariant();
            if (word == "SELECT") return true;
            if (word == "TOP") return HasSelectBefore(snapshot, pos);
            if (word == "DISTINCT" || word == "ALL") return HasSelectBefore(snapshot, pos);
            return false;
        }

        /// <summary>
        /// Walk backwards from pos to find SELECT, skipping identifiers and commas.
        /// Returns false if FROM/WHERE/JOIN is encountered first.
        /// </summary>
        private static bool FindSelectBeforePosition(Microsoft.VisualStudio.Text.ITextSnapshot snapshot, int pos)
        {
            int current = pos - 1;
            int maxScan = 2000; // Limit backwards scan
            int scanned = 0;

            while (current >= 0 && scanned < maxScan)
            {
                scanned++;
                char c = snapshot[current];

                if (char.IsWhiteSpace(c) || c == ',' || c == '.' || c == '*' ||
                    c == '(' || c == ')' || c == '[' || c == ']' || c == '"' ||
                    char.IsDigit(c))
                {
                    current--;
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    int wordEnd = current;
                    while (current >= 0 && (char.IsLetterOrDigit(snapshot[current]) || snapshot[current] == '_'))
                        current--;
                    current++;

                    var word = snapshot.GetText(current, wordEnd - current + 1).ToUpperInvariant();

                    if (word == "SELECT") return true;
                    if (word == "FROM" || word == "WHERE" || word == "JOIN" ||
                        word == "ON" || word == "SET" || word == "INTO" ||
                        word == "UPDATE" || word == "DELETE" || word == "INSERT")
                        return false;

                    current--;
                    continue;
                }

                current--;
            }

            return false;
        }
```

- [ ] **Step 6: Add TriggerWildcardExpansion method**

Add after `DetectWildcardAtCursor`:

```csharp
        /// <summary>
        /// Send WildcardExpansionRequest to the engine and show the checkbox popup.
        /// </summary>
        private void TriggerWildcardExpansion(int starPos, string qualifier)
        {
            var docText = _textView.TextBuffer.CurrentSnapshot.GetText();

            _wildcardPending = true;

            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var client = Ipc.EngineLifecycle.Manager?.Client;
                    if (client == null || !client.IsConnected) return;

                    var response = await client.SendRequestAsync<
                        AkmlSql.Core.Ipc.Messages.WildcardExpansionResponse,
                        AkmlSql.Core.Ipc.Messages.WildcardExpansionRequest>(
                        AkmlSql.Core.Ipc.MessageTypes.WildcardExpansion,
                        new AkmlSql.Core.Ipc.Messages.WildcardExpansionRequest
                        {
                            SessionId = _sessionId,
                            CursorOffset = starPos,
                            DocumentText = docText,
                            Qualifier = qualifier
                        },
                        timeoutMs: 5000);

                    if (response?.Success == true && response.Tables != null && response.Tables.Length > 0)
                    {
                        _textView.VisualElement.Dispatcher.Invoke(() =>
                        {
                            if (!_wildcardPending) return;
                            _wildcardPending = false;

                            var groups = new List<WildcardExpansionPopup.TableGroupData>();
                            foreach (var t in response.Tables)
                            {
                                groups.Add(new WildcardExpansionPopup.TableGroupData
                                {
                                    TableName = t.TableName,
                                    Qualifier = t.Qualifier,
                                    Columns = t.Columns.Select(c =>
                                        new WildcardExpansionPopup.ColumnData
                                        {
                                            ColumnName = c.ColumnName,
                                            TypeDisplay = c.TypeDisplay
                                        }).ToArray()
                                });
                            }

                            _adornment.WildcardPopup.SetData(groups);
                            _adornment.ShowWildcard();
                            _adornment.RepositionWildcard();
                            SuppressNativeIntelliSense();
                        });
                    }
                    else
                    {
                        _textView.VisualElement.Dispatcher.Invoke(() => _wildcardPending = false);
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Wildcard expansion RPC failed");
                    try
                    {
                        _textView.VisualElement.Dispatcher.Invoke(() => _wildcardPending = false);
                    }
                    catch { }
                }
            });
        }
```

- [ ] **Step 7: Add CommitWildcardExpansion method**

Add after `TriggerWildcardExpansion`:

```csharp
        /// <summary>
        /// Replace * or alias.* with the checked columns, formatted multi-line.
        /// </summary>
        private void CommitWildcardExpansion()
        {
            try
            {
                var columns = _adornment.WildcardPopup.GetCheckedColumns();
                if (columns == null)
                {
                    // No columns checked — just dismiss
                    DismissWildcardPopup();
                    return;
                }

                var snapshot = _textView.TextBuffer.CurrentSnapshot;
                var caretPos = _textView.Caret.Position.BufferPosition.Position;

                // Find the * position and replacement span
                int starPos = -1;
                if (caretPos > 0 && caretPos <= snapshot.Length && snapshot[caretPos - 1] == '*')
                    starPos = caretPos - 1;
                else if (caretPos < snapshot.Length && snapshot[caretPos] == '*')
                    starPos = caretPos;

                if (starPos < 0)
                {
                    DismissWildcardPopup();
                    return;
                }

                // Determine replacement span start (includes qualifier.* if present)
                int spanStart = starPos;
                if (starPos >= 2 && snapshot[starPos - 1] == '.')
                {
                    int idEnd = starPos - 2;
                    int idStart = idEnd;
                    while (idStart > 0 && IsIdentifierChar(snapshot[idStart - 1]))
                        idStart--;
                    spanStart = idStart;
                }

                int spanLength = starPos - spanStart + 1; // +1 for the * itself

                // Calculate indentation: number of characters from line start to spanStart
                var line = snapshot.GetLineFromPosition(spanStart);
                int indentChars = spanStart - line.Start.Position;
                string indent = new string(' ', indentChars);

                // Determine if columns need qualifier prefix
                // Use qualifier only when there are multiple tables or qualifier was explicit
                bool useQualifier = columns.Select(c => c.Qualifier).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1
                                    || (spanStart < starPos); // qualifier.* was explicit

                // Build expansion text
                var parts = new List<string>();
                foreach (var col in columns)
                {
                    string colText = useQualifier ? $"{col.Qualifier}.{col.ColumnName}" : col.ColumnName;
                    parts.Add(colText);
                }

                string expansion;
                if (parts.Count == 1)
                {
                    expansion = parts[0];
                }
                else
                {
                    // First column on same line, rest indented
                    var sb = new System.Text.StringBuilder();
                    sb.Append(parts[0]);
                    for (int i = 1; i < parts.Count; i++)
                    {
                        sb.Append(",\r\n");
                        sb.Append(indent);
                        sb.Append(parts[i]);
                    }
                    expansion = sb.ToString();
                }

                var span = new Span(spanStart, spanLength);
                _textView.TextBuffer.Replace(span, expansion);

                DismissWildcardPopup();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to commit wildcard expansion");
                DismissWildcardPopup();
            }
        }

        private void DismissWildcardPopup()
        {
            _adornment.HideWildcard();
            _wildcardPending = false;
        }
```

- [ ] **Step 8: Add using statements**

Add at the top of `CompletionController.cs` (after the existing usings):

```csharp
using System.Collections.Generic;
using System.Linq;
```

- [ ] **Step 9: Build a shell project to verify compilation**

Run:
```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"
"$MSBUILD" "src/AkmlSql.VS2022/AkmlSql.VS2022.csproj" -t:Build -p:Configuration=Release -v:minimal
```
Expected: Build succeeded.

---

## Task 6: Run All Tests

**Files:**
- Test: `tests/AkmlSql.Engine.Tests/`

- [ ] **Step 1: Run all engine tests to verify no regressions**

Run:
```bash
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj -v minimal
```
Expected: All tests PASS, including the new `WildcardExpansionHandlerTests`.

- [ ] **Step 2: Run Core tests if they exist**

Run:
```bash
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj -v minimal
```
Expected: All tests PASS.

---

## Summary of Changes

| Area | What changes |
|------|-------------|
| **IPC** | New message types 27/127, new DTOs for request/response with table groups |
| **Engine** | New `WildcardExpansionHandler` reusing `AliasResolver` + `TokenBasedAliasExtractor` + `DatabaseCache` |
| **Shell** | Tab-on-`*` detection in `CompletionController`, new `WildcardExpansionPopup` with checkboxes, expansion text formatting with multi-line alignment |
| **Tests** | 11 unit tests covering bare/qualified wildcards, multi-table, missing cache, column ordering |
