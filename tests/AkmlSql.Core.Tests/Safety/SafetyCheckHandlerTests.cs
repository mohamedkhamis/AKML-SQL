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
}
