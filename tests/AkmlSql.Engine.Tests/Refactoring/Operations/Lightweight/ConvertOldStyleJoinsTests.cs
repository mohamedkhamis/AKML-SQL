using AkmlSql.Engine.Refactoring.Operations.Lightweight;
using Xunit;

namespace AkmlSql.Engine.Tests.Refactoring.Operations.Lightweight;

/// <summary>
/// T036 — Unit tests for ConvertOldStyleJoinsOperation.
/// </summary>
public sealed class ConvertOldStyleJoinsTests
{
    private readonly ConvertOldStyleJoinsOperation _op = new();

    [Fact]
    public void ConvertOldStyleJoins_SimpleEquiJoin_ConvertsToInnerJoin()
    {
        const string sql =
            "SELECT o.Id, c.Name FROM dbo.Orders o, dbo.Customers c " +
            "WHERE o.CustomerId = c.CustomerId";

        var ctx = LightweightOperationTestHelper.CreateContext(sql);
        var (result, warnings) = _op.Apply(ctx);

        Assert.Contains("INNER JOIN", result);
        Assert.Contains("ON", result);
        Assert.Contains("o.CustomerId = c.CustomerId", result);
        // The equi-join condition should no longer appear in WHERE
        Assert.DoesNotContain("WHERE", result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ConvertOldStyleJoins_NonEquiCondition_LeftInWhereWithWarning()
    {
        const string sql =
            "SELECT o.Id FROM dbo.Orders o, dbo.Customers c " +
            "WHERE o.CustomerId = c.CustomerId AND o.OrderDate > '2024-01-01'";

        var ctx = LightweightOperationTestHelper.CreateContext(sql);
        var (result, warnings) = _op.Apply(ctx);

        Assert.Contains("INNER JOIN", result);
        // Non-equi predicate stays in WHERE
        Assert.Contains("WHERE", result);
        Assert.Contains("2024-01-01", result);
        // Should have a warning about non-equi
        Assert.NotEmpty(warnings);
        Assert.Contains(warnings, w => w.Contains("Non-equi"));
    }

    [Fact]
    public void ConvertOldStyleJoins_AnsiJoinInput_ReturnsUnchanged()
    {
        const string sql =
            "SELECT o.Id, c.Name FROM dbo.Orders o " +
            "INNER JOIN dbo.Customers c ON o.CustomerId = c.CustomerId";

        var ctx = LightweightOperationTestHelper.CreateContext(sql);
        var (result, warnings) = _op.Apply(ctx);

        // Already ANSI join — no change
        Assert.Equal(sql, result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ConvertOldStyleJoins_SingleTable_ReturnsUnchanged()
    {
        const string sql = "SELECT Id, Name FROM dbo.Orders WHERE Id = 1";
        var ctx = LightweightOperationTestHelper.CreateContext(sql);

        var (result, warnings) = _op.Apply(ctx);

        Assert.Equal(sql, result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ConvertOldStyleJoins_EmptyInput_ReturnsEmpty()
    {
        var ctx = LightweightOperationTestHelper.CreateContext(string.Empty);

        var (result, warnings) = _op.Apply(ctx);

        Assert.Equal(string.Empty, result);
        Assert.Empty(warnings);
    }
}
