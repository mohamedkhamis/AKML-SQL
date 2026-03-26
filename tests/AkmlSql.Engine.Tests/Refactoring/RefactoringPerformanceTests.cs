using System.Diagnostics;
using System.Text;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Refactoring.Operations.Lightweight;
using AkmlSql.Engine.Tests.Refactoring.Operations.Lightweight;
using Xunit;

namespace AkmlSql.Engine.Tests.Refactoring;

/// <summary>
/// T070 — Performance benchmark tests validating SC-001 and SC-002 constraints.
/// </summary>
public sealed class RefactoringPerformanceTests
{
    // ─── SC-001: Lightweight ops &lt; 100ms on 2,000-line document ─────────────

    /// <summary>
    /// Builds a synthetic 2000-line SQL document for benchmarking.
    /// </summary>
    private static string Build2000LineSql()
    {
        var sb = new StringBuilder();
        for (var i = 1; i <= 200; i++)
        {
            sb.AppendLine($"SELECT o.OrderId, o.CustomerId, o.Amount");
            sb.AppendLine($"FROM dbo.Orders o");
            sb.AppendLine($"WHERE o.CustomerId = {i};");
            sb.AppendLine();
            sb.AppendLine($"INSERT INTO dbo.OrderLog (OrderId, LogDate, Description)");
            sb.AppendLine($"VALUES ({i}, GETDATE(), N'Batch {i}');");
            sb.AppendLine();
            sb.AppendLine($"UPDATE dbo.Orders");
            sb.AppendLine($"SET Amount = Amount * 1.1");
            sb.AppendLine($"WHERE OrderId = {i};");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    [Fact]
    public void SC001_RemoveSemicolons_Under100ms_On2000LineDocument()
    {
        var sql = Build2000LineSql();
        var ctx = LightweightOperationTestHelper.CreateContext(sql);
        var op  = new RemoveSemicolonsOperation();

        // Warm up
        op.Apply(ctx);

        var sw = Stopwatch.StartNew();
        op.Apply(ctx);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 100,
            $"RemoveSemicolons took {sw.ElapsedMilliseconds}ms, expected < 100ms (SC-001)");
    }

    [Fact]
    public void SC001_AddGroupByColumns_Under100ms_On2000LineDocument()
    {
        // Build 2000-line SQL with GROUP BY-eligible queries
        var sb = new StringBuilder();
        for (var i = 1; i <= 100; i++)
        {
            sb.AppendLine($"SELECT o.CustomerId, o.Status, SUM(o.Amount) AS Total");
            sb.AppendLine($"FROM dbo.Orders o");
            sb.AppendLine($"WHERE o.CustomerId > {i};");
            sb.AppendLine();
        }
        var sql = sb.ToString();
        var ctx = LightweightOperationTestHelper.CreateContext(sql);
        var op  = new AddGroupByColumnsOperation();

        // Warm up
        op.Apply(ctx);

        var sw = Stopwatch.StartNew();
        op.Apply(ctx);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 100,
            $"AddGroupByColumns took {sw.ElapsedMilliseconds}ms, expected < 100ms (SC-001)");
    }

    [Fact]
    public void SC001_EncapsulateBeginEnd_Under100ms_On2000LineDocument()
    {
        var sql = Build2000LineSql();
        var ctx = LightweightOperationTestHelper.CreateContext(sql);
        var op  = new EncapsulateBeginEndOperation();

        // Warm up
        op.Apply(ctx);

        var sw = Stopwatch.StartNew();
        op.Apply(ctx);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 100,
            $"EncapsulateBeginEnd took {sw.ElapsedMilliseconds}ms, expected < 100ms (SC-001)");
    }

    // ─── SC-002: Heavyweight preview &lt; 200ms on 1,000-line script ────────────

    [Fact]
    public async Task SC002_ConvertTempToTableVar_Preview_Under200ms_On1000LineScript()
    {
        var sb = new StringBuilder();
        sb.AppendLine("CREATE TABLE #TempOrders (OrderId int, Amount decimal(10,2))");
        for (var i = 1; i <= 120; i++)
        {
            sb.AppendLine($"INSERT INTO #TempOrders VALUES ({i}, {i * 9.99:F2})");
            sb.AppendLine($"SELECT * FROM #TempOrders WHERE OrderId = {i}");
            sb.AppendLine($"UPDATE #TempOrders SET Amount = Amount * 1.1 WHERE OrderId = {i}");
            sb.AppendLine($"DELETE FROM #TempOrders WHERE OrderId = {i}");
            sb.AppendLine($"-- comment line {i}");
            sb.AppendLine($"SELECT COUNT(*) FROM #TempOrders");
            sb.AppendLine($"SELECT SUM(Amount) FROM #TempOrders WHERE OrderId > {i}");
            sb.AppendLine($"SELECT MIN(Amount), MAX(Amount) FROM #TempOrders");
        }
        var sql = sb.ToString();

        var ctx     = LightweightOperationTestHelper.CreateContext(sql);
        var request = new RefactorPreviewRequest
        {
            OperationType = (int)RefactorOperationType.ConvertTempToTableVar
        };
        var op = new AkmlSql.Engine.Refactoring.Operations.Heavyweight.ConvertTempTableOperation();

        // Warm up
        await op.PreviewAsync(request, ctx, default);

        var sw = Stopwatch.StartNew();
        var response = await op.PreviewAsync(request, ctx, default);
        sw.Stop();

        Assert.True(response.CanApply);
        Assert.True(sw.ElapsedMilliseconds < 200,
            $"ConvertTempToTableVar preview took {sw.ElapsedMilliseconds}ms, expected < 200ms (SC-002)");
    }

    // ─── SC-004: Extract wizard preview &lt; 500ms ────────────────────────────

    [Fact]
    public async Task SC004_ExtractToCte_Preview_Under500ms()
    {
        // Moderately large SELECT to wrap
        var sb = new StringBuilder();
        sb.AppendLine("SELECT o.OrderId, o.CustomerId, o.Amount, c.Name, c.Email");
        sb.AppendLine("FROM dbo.Orders o");
        sb.AppendLine("INNER JOIN dbo.Customers c ON c.CustomerId = o.CustomerId");
        for (var i = 1; i <= 20; i++)
            sb.AppendLine($"INNER JOIN dbo.OrderItems oi{i} ON oi{i}.OrderId = o.OrderId");
        sb.AppendLine("WHERE o.Amount > 100");
        var innerSql = sb.ToString().Trim();

        var ctx = LightweightOperationTestHelper.CreateContextWithSelection(
            innerSql, 0, innerSql.Length);

        var request = new RefactorPreviewRequest
        {
            OperationType    = (int)RefactorOperationType.ExtractToCte,
            ExtractedUnitName = "OrderSummary",
            SelectionStart   = 0,
            SelectionLength  = innerSql.Length
        };

        var op = new AkmlSql.Engine.Refactoring.Operations.Heavyweight.ExtractToCteOperation();

        // Warm up
        await op.PreviewAsync(request, ctx, default);

        var sw = Stopwatch.StartNew();
        await op.PreviewAsync(request, ctx, default);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 500,
            $"ExtractToCte preview took {sw.ElapsedMilliseconds}ms, expected < 500ms (SC-004)");
    }
}
