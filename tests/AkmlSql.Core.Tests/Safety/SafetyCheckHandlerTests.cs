using System.Collections.Generic;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Models.Safety;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Safety;
using MessagePack;
using Xunit;

namespace AkmlSql.Core.Tests.Safety;

public class SafetyCheckHandlerTests
{
    private readonly SafetyCheckHandler _handler;

    public SafetyCheckHandlerTests()
    {
        var parser = new TsqlParserService();
        _handler = new SafetyCheckHandler(parser);
    }

    private async Task<SafetyCheckResponse> AnalyzeAsync(string sql, bool isProduction = false, string? server = null)
    {
        var request = new SafetyCheckRequest
        {
            SqlText = sql,
            Server = server ?? "TestServer",
            IsProductionServer = isProduction
        };
        var payload = MessagePackSerializer.Serialize(request);
        var rpcMessage = new RpcMessage
        {
            MessageType = MessageTypes.SafetyCheck,
            RequestId = 1,
            Payload = payload
        };

        var result = await _handler.HandleAsync(rpcMessage);
        Assert.NotNull(result);
        Assert.NotNull(result.Payload);
        return MessagePackSerializer.Deserialize<SafetyCheckResponse>(result.Payload);
    }

    [Fact]
    public async Task DeleteWithoutWhere_DetectsWarning()
    {
        var response = await AnalyzeAsync("DELETE FROM dbo.Orders");

        Assert.True(response.RequiresConfirmation);
        Assert.Contains(response.Warnings, w => w.WarningType == (int)SafetyWarningType.DeleteWithoutWhere);
    }

    [Fact]
    public async Task DeleteWithWhere_NoWarning()
    {
        var response = await AnalyzeAsync("DELETE FROM dbo.Orders WHERE OrderID = 5");

        // Should not have a DeleteWithoutWhere warning
        Assert.DoesNotContain(response.Warnings,
            w => w.WarningType == (int)SafetyWarningType.DeleteWithoutWhere);
    }

    [Fact]
    public async Task UpdateWithoutWhere_DetectsWarning()
    {
        var response = await AnalyzeAsync("UPDATE dbo.Orders SET Status = 'Cancelled'");

        Assert.True(response.RequiresConfirmation);
        Assert.Contains(response.Warnings, w => w.WarningType == (int)SafetyWarningType.UpdateWithoutWhere);
    }

    [Fact]
    public async Task UpdateWithWhere_NoWarning()
    {
        var response = await AnalyzeAsync("UPDATE dbo.Orders SET Status = 'Cancelled' WHERE OrderID = 5");

        Assert.DoesNotContain(response.Warnings,
            w => w.WarningType == (int)SafetyWarningType.UpdateWithoutWhere);
    }

    [Fact]
    public async Task DropTable_DetectsWarning()
    {
        var response = await AnalyzeAsync("DROP TABLE dbo.Orders");

        Assert.True(response.RequiresConfirmation);
        Assert.Contains(response.Warnings, w => w.WarningType == (int)SafetyWarningType.DropTable);
    }

    [Fact]
    public async Task DropDatabase_DetectsWarning()
    {
        var response = await AnalyzeAsync("DROP DATABASE TestDB");

        Assert.True(response.RequiresConfirmation);
        Assert.Contains(response.Warnings, w => w.WarningType == (int)SafetyWarningType.DropDatabase);
    }

    [Fact]
    public async Task TruncateTable_DetectsWarning()
    {
        var response = await AnalyzeAsync("TRUNCATE TABLE dbo.Orders");

        Assert.True(response.RequiresConfirmation);
        Assert.Contains(response.Warnings, w => w.WarningType == (int)SafetyWarningType.TruncateTable);
    }

    [Fact]
    public async Task SafeSelect_NoWarning()
    {
        var response = await AnalyzeAsync("SELECT * FROM dbo.Orders WHERE OrderID = 5");

        Assert.False(response.RequiresConfirmation);
        Assert.Empty(response.Warnings);
    }

    [Fact]
    public async Task ProductionServer_DmlWarning()
    {
        var response = await AnalyzeAsync(
            "INSERT INTO dbo.Orders (CustomerID) VALUES (1)",
            isProduction: true,
            server: "SQLPROD01");

        Assert.True(response.RequiresConfirmation);
        Assert.Contains(response.Warnings, w => w.WarningType == (int)SafetyWarningType.ProductionDml);
    }

    [Fact]
    public async Task ProductionServer_DdlWarning()
    {
        var response = await AnalyzeAsync(
            "ALTER TABLE dbo.Orders ADD NewColumn INT",
            isProduction: true,
            server: "SQLPROD01");

        Assert.True(response.RequiresConfirmation);
        Assert.Contains(response.Warnings, w => w.WarningType == (int)SafetyWarningType.ProductionDdl);
    }

    [Fact]
    public async Task MultipleDestructive_AllDetected()
    {
        var sql = @"
            DELETE FROM dbo.Orders
            GO
            DROP TABLE dbo.Customers
        ";
        var response = await AnalyzeAsync(sql);

        Assert.True(response.RequiresConfirmation);
        Assert.Contains(response.Warnings, w => w.WarningType == (int)SafetyWarningType.DeleteWithoutWhere);
        Assert.Contains(response.Warnings, w => w.WarningType == (int)SafetyWarningType.DropTable);
    }

    [Fact]
    public async Task EmptyQuery_NoWarning()
    {
        var response = await AnalyzeAsync("");

        Assert.False(response.RequiresConfirmation);
        Assert.Empty(response.Warnings);
    }

    [Fact]
    public async Task NonProductionServer_NoProdWarning()
    {
        var response = await AnalyzeAsync(
            "INSERT INTO dbo.Orders (CustomerID) VALUES (1)",
            isProduction: false);

        Assert.False(response.RequiresConfirmation);
        Assert.Empty(response.Warnings);
    }

    [Fact]
    public async Task DeleteWithoutWhere_IncludesTableName()
    {
        var response = await AnalyzeAsync("DELETE FROM dbo.Orders");

        var warning = Assert.Single(response.Warnings,
            w => w.WarningType == (int)SafetyWarningType.DeleteWithoutWhere);
        Assert.Contains("Orders", warning.Message);
    }

    [Fact]
    public async Task DropTable_IncludesObjectName()
    {
        var response = await AnalyzeAsync("DROP TABLE dbo.Customers");

        var warning = Assert.Single(response.Warnings,
            w => w.WarningType == (int)SafetyWarningType.DropTable);
        Assert.NotNull(warning.ObjectName);
        Assert.Contains("Customers", warning.ObjectName);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Spec 014, US1 — new detection patterns (FR-002, FR-003)
    // ─────────────────────────────────────────────────────────────────────

    // ── MERGE without WHEN MATCHED (FR-002) ──

    [Fact]
    public async Task MergeWithoutWhenMatched_DetectsWarning()
    {
        var sql = @"
            MERGE INTO dbo.Target AS t
            USING dbo.Source AS s ON t.Id = s.Id
            WHEN NOT MATCHED THEN INSERT (Id) VALUES (s.Id);";

        var response = await AnalyzeAsync(sql);
        Assert.True(response.RequiresConfirmation);
        Assert.Contains(response.Warnings,
            w => w.WarningType == (int)SafetyWarningType.MergeWithoutFilter);
    }

    [Fact]
    public async Task MergeWithWhenMatched_NoMergeWarning()
    {
        var sql = @"
            MERGE INTO dbo.Target AS t
            USING dbo.Source AS s ON t.Id = s.Id
            WHEN MATCHED THEN UPDATE SET t.Name = s.Name
            WHEN NOT MATCHED THEN INSERT (Id, Name) VALUES (s.Id, s.Name);";

        var response = await AnalyzeAsync(sql);
        Assert.DoesNotContain(response.Warnings,
            w => w.WarningType == (int)SafetyWarningType.MergeWithoutFilter);
    }

    // ── DELETE/UPDATE inside INNER JOIN without WHERE (FR-002) ──

    [Fact]
    public async Task DeleteWithJoinNoWhere_DetectsJoinWarning()
    {
        var sql = "DELETE t FROM dbo.Orders t INNER JOIN dbo.Customers c ON t.CustomerId = c.Id";

        var response = await AnalyzeAsync(sql);
        Assert.True(response.RequiresConfirmation);
        Assert.Contains(response.Warnings,
            w => w.WarningType == (int)SafetyWarningType.DmlInsideJoinWithoutWhere);
    }

    [Fact]
    public async Task UpdateWithJoinNoWhere_DetectsJoinWarning()
    {
        var sql = "UPDATE t SET t.Status = 'X' FROM dbo.Orders t INNER JOIN dbo.Customers c ON t.CustomerId = c.Id";

        var response = await AnalyzeAsync(sql);
        Assert.True(response.RequiresConfirmation);
        Assert.Contains(response.Warnings,
            w => w.WarningType == (int)SafetyWarningType.DmlInsideJoinWithoutWhere);
    }

    [Fact]
    public async Task DeleteWithJoinAndWhere_NoJoinWarning()
    {
        var sql = "DELETE t FROM dbo.Orders t INNER JOIN dbo.Customers c ON t.CustomerId = c.Id WHERE c.IsDeleted = 1";

        var response = await AnalyzeAsync(sql);
        Assert.DoesNotContain(response.Warnings,
            w => w.WarningType == (int)SafetyWarningType.DmlInsideJoinWithoutWhere);
        Assert.DoesNotContain(response.Warnings,
            w => w.WarningType == (int)SafetyWarningType.DeleteWithoutWhere);
    }

    // ── Unsafe DML inside CREATE/ALTER PROCEDURE and TRIGGER (FR-003) ──

    [Fact]
    public async Task CreateProcWithDeleteNoWhere_DetectsProcWarning()
    {
        var sql = @"
            CREATE PROCEDURE dbo.ClearOrders
            AS
            BEGIN
                DELETE FROM dbo.Orders
            END";

        var response = await AnalyzeAsync(sql);
        Assert.True(response.RequiresConfirmation);
        Assert.Contains(response.Warnings,
            w => w.WarningType == (int)SafetyWarningType.UnsafeDmlInProcOrTrigger);
        var warning = Assert.Single(response.Warnings,
            w => w.WarningType == (int)SafetyWarningType.UnsafeDmlInProcOrTrigger);
        Assert.Contains("ClearOrders", warning.Message);
    }

    [Fact]
    public async Task AlterProcWithUpdateNoWhere_DetectsProcWarning()
    {
        var sql = @"
            ALTER PROCEDURE dbo.ResetStatus
            AS
            BEGIN
                UPDATE dbo.Orders SET Status = 'New'
            END";

        var response = await AnalyzeAsync(sql);
        Assert.Contains(response.Warnings,
            w => w.WarningType == (int)SafetyWarningType.UnsafeDmlInProcOrTrigger);
    }

    [Fact]
    public async Task CreateTriggerWithDeleteNoWhere_DetectsTriggerWarning()
    {
        var sql = @"
            CREATE TRIGGER dbo.trg_Cleanup ON dbo.Customers
            AFTER DELETE
            AS
            BEGIN
                DELETE FROM dbo.Audit
            END";

        var response = await AnalyzeAsync(sql);
        Assert.Contains(response.Warnings,
            w => w.WarningType == (int)SafetyWarningType.UnsafeDmlInProcOrTrigger);
        var warning = Assert.Single(response.Warnings,
            w => w.WarningType == (int)SafetyWarningType.UnsafeDmlInProcOrTrigger);
        Assert.Contains("trg_Cleanup", warning.Message);
    }

    [Fact]
    public async Task CreateProcWithSafeDelete_NoProcWarning()
    {
        var sql = @"
            CREATE PROCEDURE dbo.DeleteOrder @Id INT
            AS
            BEGIN
                DELETE FROM dbo.Orders WHERE OrderId = @Id
            END";

        var response = await AnalyzeAsync(sql);
        Assert.DoesNotContain(response.Warnings,
            w => w.WarningType == (int)SafetyWarningType.UnsafeDmlInProcOrTrigger);
    }

    // ── Edge cases from spec ──

    [Fact]
    public async Task DeleteWithSubqueryWhere_NoWarning()
    {
        // DELETE FROM X WHERE id IN (SELECT ...) — has a WHERE clause, should not warn.
        var sql = "DELETE FROM dbo.Orders WHERE OrderId IN (SELECT OrderId FROM dbo.OldOrders)";

        var response = await AnalyzeAsync(sql);
        Assert.DoesNotContain(response.Warnings,
            w => w.WarningType == (int)SafetyWarningType.DeleteWithoutWhere);
    }

    [Fact]
    public async Task DynamicSql_NoWarning_NoCrash()
    {
        // Dynamic SQL: the parser cannot see inside sp_executesql. Should not crash, should not warn.
        var sql = "EXEC sp_executesql N'DELETE FROM dbo.Orders'";

        var response = await AnalyzeAsync(sql);
        // No DeleteWithoutWhere warning because the parser doesn't see the inner text.
        Assert.DoesNotContain(response.Warnings,
            w => w.WarningType == (int)SafetyWarningType.DeleteWithoutWhere);
    }

    [Theory]
    [InlineData("DELETE FROM dbo.A", (int)SafetyWarningType.DeleteWithoutWhere)]
    [InlineData("UPDATE dbo.A SET X = 1", (int)SafetyWarningType.UpdateWithoutWhere)]
    [InlineData("DELETE t FROM dbo.A t JOIN dbo.B b ON t.Id = b.Id", (int)SafetyWarningType.DmlInsideJoinWithoutWhere)]
    public async Task VariousUnsafePatterns_AllDetected(string sql, int expectedWarningType)
    {
        var response = await AnalyzeAsync(sql);
        Assert.True(response.RequiresConfirmation);
        Assert.Contains(response.Warnings, w => w.WarningType == expectedWarningType);
    }
}
