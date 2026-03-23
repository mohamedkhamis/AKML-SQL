using AkmlSql.Engine.Refactoring.Operations.Lightweight;
using Xunit;

namespace AkmlSql.Engine.Tests.Refactoring.Operations.Lightweight;

/// <summary>
/// T036 — Unit tests for AddGroupByColumnsOperation.
/// </summary>
public sealed class AddGroupByColumnsTests
{
    private readonly AddGroupByColumnsOperation _op = new();

    [Fact]
    public void AddGroupByColumns_NonAggregatedColumns_AppendsGroupBy()
    {
        const string sql = "SELECT CategoryId, Name FROM dbo.Products";
        var ctx = LightweightOperationTestHelper.CreateContext(sql);

        var (result, warnings) = _op.Apply(ctx);

        Assert.Contains("GROUP BY", result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void AddGroupByColumns_AllAggregates_NoGroupByAdded()
    {
        const string sql = "SELECT COUNT(*), SUM(Amount) FROM dbo.Orders";
        var ctx = LightweightOperationTestHelper.CreateContext(sql);

        var (result, warnings) = _op.Apply(ctx);

        Assert.DoesNotContain("GROUP BY", result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void AddGroupByColumns_GroupByAlreadyExists_NoChange()
    {
        const string sql = "SELECT CategoryId, COUNT(*) FROM dbo.Products GROUP BY CategoryId";
        var ctx = LightweightOperationTestHelper.CreateContext(sql);

        var (result, warnings) = _op.Apply(ctx);

        // Only one GROUP BY should be present
        var count = CountOccurrences(result, "GROUP BY");
        Assert.Equal(1, count);
        Assert.Empty(warnings);
    }

    [Fact]
    public void AddGroupByColumns_EmptyInput_ReturnsEmpty()
    {
        var ctx = LightweightOperationTestHelper.CreateContext(string.Empty);

        var (result, warnings) = _op.Apply(ctx);

        Assert.Equal(string.Empty, result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void AddGroupByColumns_MixedAggAndNonAgg_GroupByContainsNonAggOnly()
    {
        const string sql = "SELECT CategoryId, COUNT(*) FROM dbo.Products";
        var ctx = LightweightOperationTestHelper.CreateContext(sql);

        var (result, warnings) = _op.Apply(ctx);

        Assert.Contains("GROUP BY", result);
        Assert.Contains("CategoryId", result.Substring(result.IndexOf("GROUP BY")));
        Assert.Empty(warnings);
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int idx   = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            idx += pattern.Length;
        }
        return count;
    }
}
