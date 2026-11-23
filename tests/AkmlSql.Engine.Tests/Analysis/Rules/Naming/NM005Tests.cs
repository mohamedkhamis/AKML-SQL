using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Naming;

/// <summary>
/// NM005 reports a name with spaces or special characters where the script gives it, once. Installed run 2026-11-23: it also
/// reported every reference to a name that already exists — Northwind's [Order Details] wherever it was queried, which the
/// script cannot rename — and, reading the whole document's tokens for each batch, every quoted name once per batch.
/// </summary>
public sealed class Nm005Tests
{
    [Theory]
    [InlineData("CREATE TABLE dbo.[Order-Lines] (Id int);", "Order-Lines")]
    [InlineData("CREATE TABLE dbo.Contacts ([First Name] nvarchar(40));", "First Name")]
    [InlineData("ALTER TABLE dbo.Customers ADD [Second Phone] nvarchar(24);", "Second Phone")]
    [InlineData("CREATE TABLE dbo.Contacts (Id int CONSTRAINT [pk contacts] PRIMARY KEY);", "pk contacts")]
    [InlineData("CREATE INDEX [ix by name] ON dbo.Customers (CompanyName);", "ix by name")]
    [InlineData("CREATE VIEW dbo.[Big Orders] AS SELECT OrderID FROM dbo.Orders;", "Big Orders")]
    [InlineData("CREATE OR ALTER VIEW dbo.[Big Orders] AS SELECT OrderID FROM dbo.Orders;", "Big Orders")]
    [InlineData("CREATE PROCEDURE dbo.[Get Orders] AS SELECT 1 AS One;", "Get Orders")]
    [InlineData("CREATE FUNCTION dbo.[Order Total] () RETURNS int AS BEGIN RETURN 1; END;", "Order Total")]
    [InlineData("DECLARE @t TABLE ([Unit Price] money);", "Unit Price")]
    [InlineData("SELECT * INTO dbo.[Orders Copy] FROM dbo.Orders;", "Orders Copy")]
    [InlineData("SELECT CustomerID AS [Customer Id] FROM dbo.Customers;", "Customer Id")]
    [InlineData("SELECT [Customer Id] = CustomerID FROM dbo.Customers;", "Customer Id")]
    [InlineData("SELECT [o d].OrderID FROM dbo.Orders AS [o d];", "o d")]
    [InlineData("SELECT t.a FROM (SELECT 1 AS a) AS [t-1] (a);", "t-1")]
    [InlineData("WITH [Big Orders] AS (SELECT OrderID FROM dbo.Orders) SELECT * FROM [Big Orders];", "Big Orders")]
    [InlineData("WITH totals ([Order Total]) AS (SELECT 1) SELECT * FROM totals;", "Order Total")]
    public void A_name_the_script_gives_is_reported_once(string sql, string name)
    {
        var diags = AnalysisEngineTestHelper.Analyze(sql, "NM005");

        var diag = Assert.Single(diags);
        Assert.Equal("NM005", diag.RuleId);
        Assert.Contains($"'{name}'", diag.Message);
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.[Order Details];")]
    [InlineData("SELECT [First Name] FROM T;")]
    [InlineData("SELECT [Order-Id] FROM T;")]
    [InlineData("SELECT od.[Unit Price] FROM dbo.[Order Details] AS od;")]
    [InlineData("UPDATE dbo.[Order Details] SET Quantity = 1 WHERE OrderID = 10248;")]
    [InlineData("INSERT INTO dbo.[Order Details] (OrderID, ProductID, [Unit Price], Quantity, Discount) VALUES (1, 1, 1, 1, 0);")]
    [InlineData("EXEC dbo.[Ten Most Expensive Products];")]
    [InlineData("ALTER VIEW dbo.[Order Details Extended] AS SELECT 1 AS One;")]
    [InlineData("GRANT VIEW DEFINITION TO [NT AUTHORITY\\SYSTEM];")]
    [InlineData("CREATE USER [CONTOSO\\jane doe] FOR LOGIN [CONTOSO\\jane doe];")]
    public void A_name_the_script_only_uses_is_not_reported(string sql)
    {
        Assert.Empty(AnalysisEngineTestHelper.Analyze(sql, "NM005"));
    }

    [Fact]
    public void A_name_is_reported_once_in_a_script_of_several_batches()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "CREATE TABLE dbo.[Order Lines] (Id int);\nGO\nSELECT * FROM dbo.[Order Lines];\nGO\nSELECT 2 AS Two;\n", "NM005");

        var diag = Assert.Single(diags);
        Assert.Equal(1, diag.Line);
    }

    [Fact]
    public void AlphanumericBracketedIdentifier_DoesNotFire()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "CREATE TABLE dbo.[Orders2] ([OrderId] int);", "NM005");
        Assert.Empty(diags);
    }
}
