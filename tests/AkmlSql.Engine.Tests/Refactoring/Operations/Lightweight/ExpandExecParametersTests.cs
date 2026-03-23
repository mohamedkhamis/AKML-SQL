using AkmlSql.Engine.Refactoring.Operations.Lightweight;
using Xunit;

namespace AkmlSql.Engine.Tests.Refactoring.Operations.Lightweight;

/// <summary>
/// T036 — Unit tests for ExpandExecParametersOperation.
/// </summary>
public sealed class ExpandExecParametersTests
{
    private readonly ExpandExecParametersOperation _op = new();

    [Fact]
    public void ExpandExecParameters_CacheMiss_ReturnsWarning()
    {
        // No schema cache → should warn about unresolvable procedure
        const string sql = "EXEC dbo.usp_GetOrders 1, 'active'";
        var ctx = LightweightOperationTestHelper.CreateContext(sql);

        var (result, warnings) = _op.Apply(ctx);

        Assert.NotEmpty(warnings);
        Assert.Contains(warnings, w => w.Contains("Could not resolve parameters"));
    }

    [Fact]
    public void ExpandExecParameters_NoExecStatement_ReturnsUnchanged()
    {
        const string sql = "SELECT Id, Name FROM dbo.Orders";
        var ctx = LightweightOperationTestHelper.CreateContext(sql);

        var (result, warnings) = _op.Apply(ctx);

        Assert.Equal(sql, result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ExpandExecParameters_EmptyInput_ReturnsEmpty()
    {
        var ctx = LightweightOperationTestHelper.CreateContext(string.Empty);

        var (result, warnings) = _op.Apply(ctx);

        Assert.Equal(string.Empty, result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ExpandExecParameters_ExecWithNoParams_ReturnsUnchanged()
    {
        // EXEC with no parameters — nothing to expand
        const string sql = "EXEC dbo.usp_GetAllOrders";
        var ctx = LightweightOperationTestHelper.CreateContext(sql);

        var (result, warnings) = _op.Apply(ctx);

        // No parameters to expand, no cache miss needed
        Assert.Equal(sql, result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ExpandExecParameters_NullSchemaCache_WarnForEachExec()
    {
        const string sql =
            "EXEC dbo.usp_Proc1 1;\r\n" +
            "EXEC dbo.usp_Proc2 2";
        var ctx = LightweightOperationTestHelper.CreateContext(sql);

        var (result, warnings) = _op.Apply(ctx);

        // One warning per unresolvable EXEC
        Assert.Equal(2, warnings.Length);
    }
}
