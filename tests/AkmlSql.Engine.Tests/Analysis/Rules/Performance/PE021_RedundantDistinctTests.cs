using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Performance;

public sealed class PE021_RedundantDistinctTests
{
    [Fact]
    public void Fires_WhenDistinctWithGroupBy()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "SELECT DISTINCT col1 FROM dbo.T GROUP BY col1;",
            "PE021");
        Assert.Single(diags);
    }

    [Fact]
    public void DoesNotFire_WhenDistinctWithoutGroupBy()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "SELECT DISTINCT col1 FROM dbo.T;",
            "PE021");
        Assert.Empty(diags);
    }
}
