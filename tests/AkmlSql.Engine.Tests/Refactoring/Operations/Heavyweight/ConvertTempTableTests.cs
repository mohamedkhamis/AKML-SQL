using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Refactoring.Operations.Heavyweight;
using AkmlSql.Engine.Tests.Refactoring.Operations.Lightweight;
using Xunit;

namespace AkmlSql.Engine.Tests.Refactoring.Operations.Heavyweight;

/// <summary>
/// T066 — Unit tests for ConvertTempTableOperation.
/// </summary>
public sealed class ConvertTempTableTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────────

    private static RefactorPreviewRequest MakeRequest(RefactorOperationType direction)
    {
        return new RefactorPreviewRequest { OperationType = (int)direction };
    }

    private static ConvertTempTableOperation Op => new();

    // ─── ConvertTempToTableVar ───────────────────────────────────────────────

    [Fact]
    public async Task ConvertTempToTableVar_Basic_ReplacesCreateTable()
    {
        const string sql = "CREATE TABLE #TempOrders (OrderId int, Amount decimal(10,2))";

        var ctx      = LightweightOperationTestHelper.CreateContext(sql);
        var request  = MakeRequest(RefactorOperationType.ConvertTempToTableVar);
        var response = await Op.PreviewAsync(request, ctx, default);

        Assert.True(response.CanApply, string.Join("; ", response.Errors));
        // The CREATE TABLE change should replace #TempOrders with DECLARE @TempOrders TABLE
        var createChange = response.Changes.FirstOrDefault(c => c.OldText.Contains("CREATE TABLE"));
        Assert.NotNull(createChange);
        Assert.Contains("DECLARE @TempOrders TABLE", createChange.NewText);
    }

    [Fact]
    public async Task ConvertTempToTableVar_AllReferences_Updated()
    {
        const string sql =
            "CREATE TABLE #TempOrders (OrderId int)\n" +
            "INSERT INTO #TempOrders VALUES (1)\n" +
            "SELECT * FROM #TempOrders\n" +
            "DROP TABLE #TempOrders";

        var ctx      = LightweightOperationTestHelper.CreateContext(sql);
        var request  = MakeRequest(RefactorOperationType.ConvertTempToTableVar);
        var response = await Op.PreviewAsync(request, ctx, default);

        Assert.True(response.CanApply, string.Join("; ", response.Errors));

        // All changes that replace #TempOrders references should point to @TempOrders
        var refChanges = response.Changes
            .Where(c => c.OldText.Contains("#TempOrders"))
            .ToArray();

        Assert.True(refChanges.Length >= 3,
            $"Expected at least 3 reference changes, got {refChanges.Length}");

        foreach (var ch in refChanges)
            Assert.Contains("@TempOrders", ch.NewText);
    }

    [Fact]
    public async Task ConvertTempToTableVar_StatisticsWarning_Present()
    {
        const string sql = "CREATE TABLE #TempOrders (OrderId int)";

        var ctx      = LightweightOperationTestHelper.CreateContext(sql);
        var request  = MakeRequest(RefactorOperationType.ConvertTempToTableVar);
        var response = await Op.PreviewAsync(request, ctx, default);

        Assert.True(response.CanApply);
        Assert.NotEmpty(response.Warnings);
        Assert.Contains(response.Warnings, w => w.Contains("statistics"));
    }

    [Fact]
    public async Task ConvertTempToTableVar_NameCollision_CanApplyFalse()
    {
        const string sql =
            "DECLARE @TempOrders TABLE (OrderId int)\n" +
            "CREATE TABLE #TempOrders (OrderId int)";

        var ctx      = LightweightOperationTestHelper.CreateContext(sql);
        var request  = MakeRequest(RefactorOperationType.ConvertTempToTableVar);
        var response = await Op.PreviewAsync(request, ctx, default);

        Assert.False(response.CanApply);
        Assert.NotEmpty(response.Errors);
    }

    // ─── ConvertTableVarToTemp ───────────────────────────────────────────────

    [Fact]
    public async Task ConvertTableVarToTemp_Basic_ReplacesDeclaration()
    {
        const string sql =
            "DECLARE @TempOrders TABLE (OrderId int, Amount decimal(10,2))\n" +
            "INSERT INTO @TempOrders VALUES (1, 9.99)\n" +
            "SELECT * FROM @TempOrders";

        var ctx      = LightweightOperationTestHelper.CreateContext(sql);
        var request  = MakeRequest(RefactorOperationType.ConvertTableVarToTemp);
        var response = await Op.PreviewAsync(request, ctx, default);

        Assert.True(response.CanApply, string.Join("; ", response.Errors));

        // The DECLARE TABLE change should become CREATE TABLE #TempOrders
        var declareChange = response.Changes.FirstOrDefault(c => c.OldText.Contains("DECLARE"));
        Assert.NotNull(declareChange);
        Assert.Contains("CREATE TABLE #TempOrders", declareChange.NewText);

        // Reference changes should point to #TempOrders
        var refChanges = response.Changes
            .Where(c => c.OldText.Contains("@TempOrders") && !c.OldText.Contains("DECLARE"))
            .ToArray();

        foreach (var ch in refChanges)
            Assert.Contains("#TempOrders", ch.NewText);
    }

    [Fact]
    public async Task ConvertTableVarToTemp_NoDeclaration_CanApplyFalse()
    {
        const string sql = "SELECT * FROM dbo.Orders";

        var ctx      = LightweightOperationTestHelper.CreateContext(sql);
        var request  = MakeRequest(RefactorOperationType.ConvertTableVarToTemp);
        var response = await Op.PreviewAsync(request, ctx, default);

        Assert.False(response.CanApply);
        Assert.NotEmpty(response.Errors);
    }
}
