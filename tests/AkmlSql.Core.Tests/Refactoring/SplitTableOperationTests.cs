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
/// Tests the SplitTableOperation via the RefactoringEngine dispatch.
/// Since SplitTable requires a schema cache with table metadata, and we can't
/// easily populate it in unit tests without a real database, we test the
/// operation's error handling and input validation paths.
/// </summary>
public class SplitTableOperationTests : IDisposable
{
    private readonly TsqlParserService _parser = new();
    private readonly SchemaCacheManager _schemaCache = new(maxDatabases: 1);
    private readonly SessionManager _sessionManager = new();
    private readonly RefactoringEngine _engine;

    public SplitTableOperationTests()
    {
        _engine = new RefactoringEngine(_parser, _schemaCache);
    }

    public void Dispose()
    {
        _schemaCache.Dispose();
    }

    private async Task<RefactorPreviewResponse> PreviewSplitTableAsync(
        string sourceTable, string newTable, string[] columnsToMove)
    {
        var request = new RefactorPreviewRequest
        {
            SessionId = "test-session",
            OperationType = (int)RefactorOperationType.SplitTable,
            DocumentText = $"SELECT * FROM {sourceTable}",
            OriginalIdentifier = sourceTable,
            NewName = newTable,
            AdditionalFilePaths = columnsToMove
        };

        return await _engine.PreviewAsync(request, _sessionManager, CancellationToken.None);
    }

    [Fact]
    public async Task SplitTable_MissingSourceTable_ReturnsError()
    {
        var response = await PreviewSplitTableAsync("", "NewTable", new[] { "Col1" });

        Assert.False(response.CanApply);
        Assert.NotEmpty(response.Errors);
    }

    [Fact]
    public async Task SplitTable_MissingNewTableName_ReturnsError()
    {
        var response = await PreviewSplitTableAsync("dbo.Orders", "", new[] { "Col1" });

        Assert.False(response.CanApply);
        Assert.NotEmpty(response.Errors);
    }

    [Fact]
    public async Task SplitTable_NoColumnsSpecified_ReturnsError()
    {
        var response = await PreviewSplitTableAsync("dbo.Orders", "OrderShipping", Array.Empty<string>());

        Assert.False(response.CanApply);
        Assert.NotEmpty(response.Errors);
    }

    [Fact]
    public async Task SplitTable_TableNotInCache_ReturnsError()
    {
        // Schema cache is empty — table won't be found
        var response = await PreviewSplitTableAsync("dbo.NonExistent", "NewTable", new[] { "Col1" });

        Assert.False(response.CanApply);
        Assert.NotEmpty(response.Errors);
        Assert.Contains(response.Errors, e =>
            e.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
            e.Contains("schema cache", StringComparison.OrdinalIgnoreCase) ||
            e.Contains("unavailable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SplitTable_OperationTypeIsCorrect()
    {
        var request = new RefactorPreviewRequest
        {
            OperationType = (int)RefactorOperationType.SplitTable
        };
        Assert.Equal(8, request.OperationType);
    }

    [Fact]
    public async Task SplitTable_EnumValueExists()
    {
        Assert.Equal(8, (int)RefactorOperationType.SplitTable);
    }
}
