using AkmlSql.Core.Models.Analysis;
using AkmlSql.Engine.Analysis;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Engine.Tests.Analysis;

/// <summary>
/// T101 — False-positive sweep: verifies that well-written, best-practice SQL does not
/// trigger PE/BP/ST rules. Each test represents a clean stored procedure or statement that
/// should produce zero (or only expected) diagnostics.
/// </summary>
public sealed class FalsePositiveSweepTests(ITestOutputHelper output)
{
    // Severity levels that constitute false-positive violations
    private static readonly DiagnosticSeverity MinFpSeverity = DiagnosticSeverity.Warning;

    private IReadOnlyList<AnalysisDiagnostic> AnalyzeForFalsePositives(string sql)
    {
        var all = AnalysisEngineTestHelper.Analyze(sql);
        return all.Where(d => d.Severity >= MinFpSeverity).ToList();
    }

    private void AssertNoFalsePositives(string sql)
    {
        var diags = AnalyzeForFalsePositives(sql);
        if (diags.Count > 0)
        {
            var details = string.Join("\n", diags.Select(d => $"  {d.RuleId} line {d.Line}: {d.Message}"));
            output.WriteLine($"Unexpected diagnostics:\n{details}");
        }
        Assert.Empty(diags);
    }

    [Fact]
    public void CleanSelectWithExplicitColumns_NoFalsePositives()
    {
        AssertNoFalsePositives("""
            SELECT o.OrderId, o.CustomerId, o.OrderDate
            FROM dbo.Orders AS o
            INNER JOIN dbo.Customers AS cust ON cust.CustomerId = o.CustomerId
            WHERE o.OrderDate >= '2024-01-01';
            """);
    }

    [Fact]
    public void SimpleInsertWithColumnList_NoFalsePositives()
    {
        AssertNoFalsePositives("""
            INSERT INTO dbo.Orders (CustomerId, OrderDate, TotalAmount)
            VALUES (1, GETDATE(), 99.99);
            """);
    }

    [Fact]
    public void WellWrittenStoredProcedure_NoFalsePositives()
    {
        AssertNoFalsePositives("""
            CREATE PROCEDURE dbo.usp_GetOrderById
                @OrderId INT
            AS
            SET NOCOUNT ON;
            BEGIN TRY
                SELECT o.OrderId, o.CustomerId, o.OrderDate, o.TotalAmount
                FROM dbo.Orders AS o
                WHERE o.OrderId = @OrderId;
                RETURN 0;
            END TRY
            BEGIN CATCH
                THROW;
            END CATCH
            """);
    }

    [Fact]
    public void StoredProcWithTransaction_NoFalsePositives()
    {
        AssertNoFalsePositives("""
            CREATE PROCEDURE dbo.usp_PlaceOrder
                @CustomerId INT,
                @ProductId  INT,
                @Quantity   INT
            AS
            SET NOCOUNT ON;
            SET XACT_ABORT ON;
            BEGIN TRY
                BEGIN TRANSACTION;
                    INSERT INTO dbo.Orders (CustomerId, OrderDate)
                    VALUES (@CustomerId, GETDATE());

                    UPDATE dbo.Inventory
                    SET QuantityOnHand = QuantityOnHand - @Quantity
                    WHERE ProductId = @ProductId;
                COMMIT TRANSACTION;
                RETURN 0;
            END TRY
            BEGIN CATCH
                IF XACT_STATE() <> 0
                    ROLLBACK TRANSACTION;
                THROW;
            END CATCH
            """);
    }

    [Fact]
    public void DeleteWithWhereClause_NoFalsePositives()
    {
        AssertNoFalsePositives("""
            DELETE FROM dbo.Orders WHERE OrderId = 42;
            """);
    }

    [Fact]
    public void UpdateWithWhereClause_NoFalsePositives()
    {
        AssertNoFalsePositives("""
            UPDATE dbo.Orders
            SET TotalAmount = 199.99, UpdatedAt = GETDATE()
            WHERE OrderId = 42;
            """);
    }

    [Fact]
    public void InListWithFewItems_NoFalsePositives()
    {
        AssertNoFalsePositives("""
            SELECT ProductId, ProductName
            FROM dbo.Products
            WHERE CategoryId IN (1, 2, 3, 4, 5);
            """);
    }

    [Fact]
    public void NullComparisonWithIsNull_NoFalsePositives()
    {
        AssertNoFalsePositives("""
            SELECT CustomerId, Name
            FROM dbo.Customers
            WHERE DeletedAt IS NULL;
            """);
    }

    [Fact]
    public void ScopeIdentityUsage_NoFalsePositives()
    {
        AssertNoFalsePositives("""
            INSERT INTO dbo.Orders (CustomerId) VALUES (1);
            SELECT SCOPE_IDENTITY() AS NewOrderId;
            """);
    }

    [Fact]
    public void SpExecuteSqlParameterized_NoFalsePositives()
    {
        AssertNoFalsePositives("""
            DECLARE @sql NVARCHAR(500);
            DECLARE @orderId INT = 42;
            SET @sql = N'SELECT * FROM dbo.Orders WHERE OrderId = @id';
            EXEC sp_executesql @sql, N'@id INT', @id = @orderId;
            """);
    }

    [Fact]
    public void HashBytesWithStrongAlgorithm_NoFalsePositives()
    {
        AssertNoFalsePositives("""
            SELECT HASHBYTES('SHA2_256', CONVERT(NVARCHAR(100), UserId))
            FROM dbo.Users;
            """);
    }

    [Fact]
    public void AnsiJoinSyntax_NoFalsePositives()
    {
        AssertNoFalsePositives("""
            SELECT o.OrderId, c.Name, p.ProductName
            FROM dbo.Orders AS o
            INNER JOIN dbo.Customers AS c ON c.CustomerId = o.CustomerId
            INNER JOIN dbo.OrderItems AS oi ON oi.OrderId = o.OrderId
            INNER JOIN dbo.Products AS p ON p.ProductId = oi.ProductId
            WHERE o.OrderDate > DATEADD(MONTH, -3, GETDATE());
            """);
    }

    [Fact]
    public void TryCatchWithThrow_NoFalsePositives()
    {
        AssertNoFalsePositives("""
            BEGIN TRY
                DELETE FROM dbo.Orders WHERE OrderId = 99;
            END TRY
            BEGIN CATCH
                DECLARE @msg NVARCHAR(2048) = ERROR_MESSAGE();
                THROW 50001, @msg, 1;
            END CATCH
            """);
    }

    [Fact]
    public void CreateTableWithPk_NoFalsePositives()
    {
        AssertNoFalsePositives("""
            CREATE TABLE dbo.Products (
                ProductId   INT          NOT NULL IDENTITY(1,1),
                ProductName NVARCHAR(200) NOT NULL,
                Price       DECIMAL(18,2) NOT NULL,
                CreatedAt   DATETIME2    NOT NULL DEFAULT GETUTCDATE(),
                CONSTRAINT PK_Products PRIMARY KEY (ProductId)
            );
            """);
    }

    [Fact]
    public void IfWithBeginEnd_NoFalsePositives()
    {
        AssertNoFalsePositives("""
            IF EXISTS (SELECT 1 FROM dbo.Orders WHERE OrderId = 42)
            BEGIN
                PRINT 'Order exists';
            END
            """);
    }
}
