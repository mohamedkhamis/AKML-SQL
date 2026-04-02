using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Refactoring;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using Xunit;

namespace AkmlSql.Core.Tests.Refactoring;

/// <summary>
/// Tests the engine-side SafeRenameOperation preview to verify it correctly
/// identifies all references to a renamed identifier in SQL text.
/// The generated RefactorChangeInfo[] array is what feeds into the
/// shell-side RenameScriptGenerator (tested manually via SSMS integration).
/// </summary>
public class SafeRenameOperationTests : IDisposable
{
    private readonly TsqlParserService _parser = new();
    private readonly SchemaCacheManager _schemaCache = new(maxDatabases: 1);
    private readonly SessionManager _sessionManager = new();
    private readonly RefactoringEngine _engine;

    public SafeRenameOperationTests()
    {
        _engine = new RefactoringEngine(_parser, _schemaCache);
    }

    public void Dispose()
    {
        _schemaCache.Dispose();
    }

    private async Task<RefactorPreviewResponse> PreviewRenameAsync(
        string sql, string originalIdentifier, string newName, int selectionStart = -1)
    {
        // If selectionStart not specified, find the first occurrence
        if (selectionStart < 0)
            selectionStart = sql.IndexOf(originalIdentifier);

        var request = new RefactorPreviewRequest
        {
            SessionId = "test-session",
            OperationType = 0, // SafeRename
            DocumentText = sql,
            SelectionStart = selectionStart,
            SelectionLength = originalIdentifier.Length,
            NewName = newName,
            OriginalIdentifier = originalIdentifier
        };

        return await _engine.PreviewAsync(request, _sessionManager, CancellationToken.None);
    }

    [Fact]
    public async Task RenameColumn_FindsAllReferences()
    {
        var sql = @"
SELECT OrderDate, CustomerID
FROM dbo.Orders
WHERE OrderDate > '2024-01-01'
ORDER BY OrderDate
";
        var response = await PreviewRenameAsync(sql, "OrderDate", "OrderPlacedDate");

        Assert.NotNull(response);
        Assert.True(response.CanApply);
        // Should find 3 references to OrderDate (SELECT, WHERE, ORDER BY)
        Assert.True(response.Changes.Length >= 3,
            $"Expected at least 3 changes, got {response.Changes.Length}");
        Assert.All(response.Changes, c =>
        {
            Assert.Equal("OrderDate", c.OldText);
            Assert.Equal("OrderPlacedDate", c.NewText);
        });
    }

    [Fact]
    public async Task RenameTable_FindsReference()
    {
        var sql = @"
SELECT * FROM dbo.Orders WHERE OrderID = 1
";
        var response = await PreviewRenameAsync(sql, "Orders", "SalesOrders");

        Assert.NotNull(response);
        Assert.True(response.Changes.Length >= 1,
            $"Expected at least 1 change, got {response.Changes.Length}");
        Assert.Contains(response.Changes, c => c.OldText == "Orders" && c.NewText == "SalesOrders");
    }

    [Fact]
    public async Task RenameAlias_FindsAllUsages()
    {
        var sql = @"
SELECT o.OrderID, o.OrderDate
FROM dbo.Orders o
WHERE o.Status = 'Active'
";
        var response = await PreviewRenameAsync(sql, "o", "ord",
            selectionStart: sql.IndexOf("Orders o") + "Orders ".Length);

        Assert.NotNull(response);
        // Should find alias usages: o.OrderID, o.OrderDate, o (declaration), o.Status
        Assert.True(response.Changes.Length >= 3,
            $"Expected at least 3 alias changes, got {response.Changes.Length}");
    }

    [Fact]
    public async Task RenameWithNoMatches_ReturnsEmptyChanges()
    {
        var sql = "SELECT 1 AS Result";
        var response = await PreviewRenameAsync(sql, "NonExistent", "NewName", selectionStart: 0);

        Assert.NotNull(response);
        Assert.Empty(response.Changes);
    }

    [Fact]
    public async Task RenamePreview_IncludesLineNumbers()
    {
        var sql = @"SELECT OrderDate
FROM dbo.Orders
WHERE OrderDate > '2024-01-01'";
        var response = await PreviewRenameAsync(sql, "OrderDate", "OrderPlacedDate");

        Assert.NotNull(response);
        Assert.All(response.Changes, c =>
        {
            Assert.True(c.Line > 0, "Line number should be 1-based");
        });
    }

    [Fact]
    public async Task RenamePreview_SetsChangeCategory()
    {
        var sql = "SELECT OrderDate FROM dbo.Orders";
        var response = await PreviewRenameAsync(sql, "OrderDate", "OrderPlacedDate");

        Assert.NotNull(response);
        Assert.All(response.Changes, c =>
        {
            Assert.Equal("rename", c.ChangeCategory);
        });
    }
}
