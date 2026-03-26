using System.Linq;
using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Performance;

public sealed class PE015_LargeInListTests
{
    [Fact]
    public void FiresOnInListWithMoreThan100Items()
    {
        var sql = "SELECT * FROM T WHERE Id IN (" + string.Join(",", Enumerable.Range(1, 101)) + ")";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE015");

        Assert.Single(diags);
        Assert.Equal("PE015", diags[0].RuleId);
    }

    [Fact]
    public void DoesNotFireOnInListWithFewItems()
    {
        const string sql = "SELECT * FROM T WHERE Id IN (1, 2, 3)";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE015");

        Assert.Empty(diags);
    }

    [Fact]
    public void DoesNotFireOnExactly100Items()
    {
        var sql = "SELECT * FROM T WHERE Id IN (" + string.Join(",", Enumerable.Range(1, 100)) + ")";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE015");

        Assert.Empty(diags);
    }

    [Fact]
    public void FiresOnceForSingleLargeInList()
    {
        var sql = "SELECT * FROM T WHERE Id IN (" + string.Join(",", Enumerable.Range(1, 200)) + ")";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE015");

        Assert.Single(diags);
    }

    [Fact]
    public void FiresTwiceForTwoLargeInLists()
    {
        var list = string.Join(",", Enumerable.Range(1, 101));
        var sql = $"SELECT * FROM T WHERE Id IN ({list}) OR Code IN ({list})";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE015");

        Assert.Equal(2, diags.Count);
    }
}
