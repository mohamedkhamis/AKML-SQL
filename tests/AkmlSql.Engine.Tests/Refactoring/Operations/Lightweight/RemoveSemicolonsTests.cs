using AkmlSql.Engine.Refactoring.Operations.Lightweight;
using Xunit;

namespace AkmlSql.Engine.Tests.Refactoring.Operations.Lightweight;

/// <summary>
/// T035 — Unit tests for RemoveSemicolonsOperation.
/// </summary>
public sealed class RemoveSemicolonsTests
{
    private readonly RemoveSemicolonsOperation _op = new();

    [Fact]
    public void RemoveSemicolons_FullDocument_RemovesAllSemicolons()
    {
        const string sql = "SELECT 1; SELECT 2;";
        var ctx = LightweightOperationTestHelper.CreateContext(sql);

        var (result, warnings) = _op.Apply(ctx);

        Assert.DoesNotContain(";", result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void RemoveSemicolons_EmptyInput_ReturnsEmpty()
    {
        var ctx = LightweightOperationTestHelper.CreateContext(string.Empty);

        var (result, warnings) = _op.Apply(ctx);

        Assert.Equal(string.Empty, result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void RemoveSemicolons_NoSemicolons_ReturnsUnchanged()
    {
        const string sql = "SELECT 1 SELECT 2";
        var ctx = LightweightOperationTestHelper.CreateContext(sql);

        var (result, warnings) = _op.Apply(ctx);

        Assert.Equal(sql, result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void RemoveSemicolons_SelectionOnly_RemovesOnlySemicolonsInSelection()
    {
        // First semicolon at offset 8, second at offset 18
        const string sql = "SELECT 1; SELECT 2;";
        // Select only the second statement (offset 10 onward)
        var ctx = LightweightOperationTestHelper.CreateContextWithSelection(sql, 10, sql.Length - 10);

        var (result, warnings) = _op.Apply(ctx);

        // First semicolon at offset 8 should remain
        Assert.Contains(";", result);
        // Second semicolon should be removed
        Assert.Equal("SELECT 1; SELECT 2", result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void RemoveSemicolons_MultipleSemicolons_AllRemoved()
    {
        const string sql = "BEGIN; SELECT 1; SELECT 2; END;";
        var ctx = LightweightOperationTestHelper.CreateContext(sql);

        var (result, warnings) = _op.Apply(ctx);

        Assert.DoesNotContain(";", result);
        Assert.Empty(warnings);
    }
}
