using AkmlSql.Engine.Refactoring.Operations.Lightweight;
using Xunit;

namespace AkmlSql.Engine.Tests.Refactoring.Operations.Lightweight;

/// <summary>
/// T036 — Unit tests for ExpandInsertColumnsOperation.
/// </summary>
public sealed class ExpandInsertColumnsTests
{
    private readonly ExpandInsertColumnsOperation _op = new();

    [Fact]
    public void ExpandInsertColumns_CacheMiss_ReturnsWarning()
    {
        // No schema cache → should warn but not throw
        const string sql = "INSERT INTO dbo.Orders VALUES (1, 'Test', GETDATE())";
        var ctx = LightweightOperationTestHelper.CreateContext(sql);

        var (result, warnings) = _op.Apply(ctx);

        // Text unchanged because we can't expand without schema info
        Assert.NotEmpty(warnings);
        Assert.Contains(warnings, w => w.Contains("Could not resolve columns"));
    }

    [Fact]
    public void ExpandInsertColumns_NoInsertStatement_ReturnsUnchanged()
    {
        const string sql = "SELECT Id, Name FROM dbo.Orders";
        var ctx = LightweightOperationTestHelper.CreateContext(sql);

        var (result, warnings) = _op.Apply(ctx);

        Assert.Equal(sql, result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ExpandInsertColumns_EmptyInput_ReturnsEmpty()
    {
        var ctx = LightweightOperationTestHelper.CreateContext(string.Empty);

        var (result, warnings) = _op.Apply(ctx);

        Assert.Equal(string.Empty, result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ExpandInsertColumns_InsertWithExistingColumns_ReturnsUnchanged()
    {
        // INSERT with a column list already present — no expansion needed
        const string sql = "INSERT INTO dbo.Orders (Id, Name) VALUES (1, 'Test')";
        var ctx = LightweightOperationTestHelper.CreateContext(sql);

        var (result, warnings) = _op.Apply(ctx);

        Assert.Equal(sql, result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ExpandInsertColumns_NullSchemaCache_WarnForEachInsert()
    {
        const string sql =
            "INSERT INTO dbo.Orders VALUES (1);\r\n" +
            "INSERT INTO dbo.Products VALUES (2)";
        var ctx = LightweightOperationTestHelper.CreateContext(sql);

        var (result, warnings) = _op.Apply(ctx);

        // One warning per unresolvable INSERT
        Assert.Equal(2, warnings.Length);
    }
}
