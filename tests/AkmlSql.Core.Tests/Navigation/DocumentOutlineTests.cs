#nullable enable
using AkmlSql.Engine.Navigation;
using AkmlSql.Engine.Parser;
using Xunit;

namespace AkmlSql.Core.Tests.Navigation;

/// <summary>
/// Tests for <see cref="DocumentOutlineHandler"/> which parses SQL and builds
/// an outline tree of structural elements (procedures, functions, views, CTEs, etc.).
/// Uses <see cref="TsqlParserService"/> directly (no mocks needed).
/// </summary>
public class DocumentOutlineTests
{
    private readonly DocumentOutlineHandler _handler;

    public DocumentOutlineTests()
    {
        var parser = new TsqlParserService();
        _handler = new DocumentOutlineHandler(parser);
    }

    [Fact]
    public void ProcedureDetection_CreateProcedure_FindsProcedureNode()
    {
        var sql = @"
CREATE PROCEDURE dbo.GetOrders
AS
BEGIN
    SELECT * FROM dbo.Orders
END";

        var nodes = _handler.BuildOutline(sql);

        Assert.NotEmpty(nodes);
        var procNode = Assert.Single(nodes, n => n.NodeType == "Procedure");
        Assert.Contains("GetOrders", procNode.Name);
    }

    [Fact]
    public void FunctionDetection_CreateFunction_FindsFunctionNode()
    {
        var sql = @"
CREATE FUNCTION dbo.CalculateTotal(@OrderId INT)
RETURNS DECIMAL(18,2)
AS
BEGIN
    DECLARE @Total DECIMAL(18,2)
    SELECT @Total = SUM(Amount) FROM dbo.OrderItems WHERE OrderId = @OrderId
    RETURN @Total
END";

        var nodes = _handler.BuildOutline(sql);

        Assert.NotEmpty(nodes);
        var funcNode = Assert.Single(nodes, n => n.NodeType == "Function");
        Assert.Contains("CalculateTotal", funcNode.Name);
    }

    [Fact]
    public void ViewDetection_CreateView_FindsViewNode()
    {
        var sql = @"
CREATE VIEW dbo.ActiveOrders
AS
SELECT OrderId, CustomerName, OrderDate
FROM dbo.Orders
WHERE Status = 'Active'";

        var nodes = _handler.BuildOutline(sql);

        Assert.NotEmpty(nodes);
        var viewNode = Assert.Single(nodes, n => n.NodeType == "View");
        Assert.Contains("ActiveOrders", viewNode.Name);
    }

    [Fact]
    public void CteExtraction_WithCte_FindsCteNode()
    {
        var sql = @"
WITH OrderTotals AS (
    SELECT OrderId, SUM(Amount) AS Total
    FROM dbo.OrderItems
    GROUP BY OrderId
)
SELECT * FROM OrderTotals";

        var nodes = _handler.BuildOutline(sql);

        Assert.NotEmpty(nodes);
        var cteNode = Assert.Single(nodes, n => n.NodeType == "CTE");
        Assert.Contains("OrderTotals", cteNode.Name);
    }

    [Fact]
    public void EmptyDocument_ReturnsEmptyArray()
    {
        var nodes = _handler.BuildOutline("");

        Assert.Empty(nodes);
    }

    [Fact]
    public void NullDocument_ReturnsEmptyArray()
    {
        var nodes = _handler.BuildOutline(null!);

        Assert.Empty(nodes);
    }

    [Fact]
    public void WhitespaceDocument_ReturnsEmptyArray()
    {
        var nodes = _handler.BuildOutline("   \n\n   ");

        Assert.Empty(nodes);
    }

    [Fact]
    public void MultipleBatchesWithGo_FindsAllObjects()
    {
        var sql = @"
CREATE PROCEDURE dbo.InsertOrder
AS
BEGIN
    INSERT INTO dbo.Orders (CustomerName) VALUES ('Test')
END
GO

CREATE VIEW dbo.RecentOrders
AS
SELECT * FROM dbo.Orders WHERE OrderDate > DATEADD(DAY, -30, GETDATE())
GO

CREATE FUNCTION dbo.GetOrderCount()
RETURNS INT
AS
BEGIN
    DECLARE @Count INT
    SELECT @Count = COUNT(*) FROM dbo.Orders
    RETURN @Count
END";

        var nodes = _handler.BuildOutline(sql);

        Assert.True(nodes.Length >= 3, $"Expected at least 3 nodes, got {nodes.Length}");
        Assert.Contains(nodes, n => n.NodeType == "Procedure" && n.Name.Contains("InsertOrder"));
        Assert.Contains(nodes, n => n.NodeType == "View" && n.Name.Contains("RecentOrders"));
        Assert.Contains(nodes, n => n.NodeType == "Function" && n.Name.Contains("GetOrderCount"));
    }

    [Fact]
    public void TriggerDetection_CreateTrigger_FindsTriggerNode()
    {
        var sql = @"
CREATE TRIGGER dbo.trg_OrderInsert
ON dbo.Orders
AFTER INSERT
AS
BEGIN
    INSERT INTO dbo.AuditLog (TableName, Action) VALUES ('Orders', 'INSERT')
END";

        var nodes = _handler.BuildOutline(sql);

        Assert.NotEmpty(nodes);
        var triggerNode = Assert.Single(nodes, n => n.NodeType == "Trigger");
        Assert.Contains("trg_OrderInsert", triggerNode.Name);
    }

    [Fact]
    public void TempTableDetection_CreateTempTable_FindsTempTableNode()
    {
        var sql = @"
CREATE TABLE #TempOrders (
    OrderId INT,
    CustomerName NVARCHAR(100)
)";

        var nodes = _handler.BuildOutline(sql);

        Assert.NotEmpty(nodes);
        var tempNode = Assert.Single(nodes, n => n.NodeType == "TempTable");
        Assert.Contains("#TempOrders", tempNode.Name);
    }

    [Fact]
    public void MultipleCtes_WrappedInBlock()
    {
        var sql = @"
WITH CTE1 AS (
    SELECT 1 AS Val
),
CTE2 AS (
    SELECT 2 AS Val
)
SELECT * FROM CTE1 UNION ALL SELECT * FROM CTE2";

        var nodes = _handler.BuildOutline(sql);

        Assert.NotEmpty(nodes);
        // Multiple CTEs should be wrapped in a Block node
        var blockNode = Assert.Single(nodes, n => n.NodeType == "Block");
        Assert.Equal(2, blockNode.Children.Length);
        Assert.All(blockNode.Children, c => Assert.Equal("CTE", c.NodeType));
    }

    [Fact]
    public void RegionDetection_FindsRegionNodes()
    {
        var sql = @"-- region Reports
SELECT * FROM dbo.Orders
-- endregion";

        var nodes = _handler.BuildOutline(sql);

        Assert.Contains(nodes, n => n.NodeType == "Region");
    }

    [Fact]
    public void AlterProcedure_FindsProcedureNode()
    {
        var sql = @"
ALTER PROCEDURE dbo.UpdateOrder
AS
BEGIN
    UPDATE dbo.Orders SET Status = 'Updated'
END";

        var nodes = _handler.BuildOutline(sql);

        Assert.NotEmpty(nodes);
        var procNode = Assert.Single(nodes, n => n.NodeType == "Procedure");
        Assert.Contains("UpdateOrder", procNode.Name);
    }

    [Fact]
    public void ProcedureWithChildStatements_HasChildren()
    {
        var sql = @"
CREATE PROCEDURE dbo.ComplexProc
AS
BEGIN
    BEGIN TRY
        INSERT INTO dbo.Orders (CustomerName) VALUES ('Test')
    END TRY
    BEGIN CATCH
        SELECT ERROR_MESSAGE()
    END CATCH
END";

        var nodes = _handler.BuildOutline(sql);

        Assert.NotEmpty(nodes);
        var procNode = Assert.Single(nodes, n => n.NodeType == "Procedure");
        // The procedure should have child nodes (TRY...CATCH block)
        Assert.NotEmpty(procNode.Children);
    }

    [Fact]
    public void NodeOffsets_AreValid()
    {
        var sql = @"CREATE VIEW dbo.TestView AS SELECT 1 AS Value";

        var nodes = _handler.BuildOutline(sql);

        Assert.NotEmpty(nodes);
        var viewNode = nodes[0];
        Assert.True(viewNode.StartOffset >= 0, "StartOffset should be non-negative");
        Assert.True(viewNode.EndOffset > viewNode.StartOffset, "EndOffset should be after StartOffset");
        Assert.True(viewNode.StartLine >= 0, "StartLine should be non-negative");
    }
}
