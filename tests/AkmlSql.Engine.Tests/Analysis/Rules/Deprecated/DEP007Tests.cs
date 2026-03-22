using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Deprecated;

public sealed class DEP007Tests
{
    /// <summary>
    /// DEP007 scans the raw token stream for "GROUP BY ALL" regardless of whether
    /// ScriptDom accepts GROUP BY ALL syntax at TSql160 dialect level.
    /// </summary>
    [Fact]
    public void GroupByAll_Fires()
    {
        // Token-scan based — fires even if ScriptDom rejects the statement.
        const string sql = "SELECT Id FROM Orders GROUP BY ALL Id";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "DEP007");

        Assert.Single(diags);
        Assert.Equal("DEP007", diags[0].RuleId);
    }

    [Fact]
    public void GroupByWithoutAll_DoesNotFire()
    {
        const string sql = "SELECT Id FROM Orders GROUP BY Id";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "DEP007");

        Assert.Empty(diags);
    }

    [Fact]
    public void GroupByAllCaseInsensitive_Fires()
    {
        const string sql = "SELECT Id FROM Orders group by all Id";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "DEP007");

        Assert.Single(diags);
    }
}
