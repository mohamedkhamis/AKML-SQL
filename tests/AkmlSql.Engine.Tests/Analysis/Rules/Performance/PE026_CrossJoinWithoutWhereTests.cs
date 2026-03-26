using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Performance;

public sealed class PE026_CrossJoinWithoutWhereTests
{
    [Fact]
    public void Fires_OnCrossJoin()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "SELECT a.Id, b.Id FROM dbo.T1 AS a CROSS JOIN dbo.T2 AS b;",
            "PE026");
        Assert.Single(diags);
    }

    [Fact]
    public void DoesNotFire_OnInnerJoin()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "SELECT a.Id FROM dbo.T1 AS a INNER JOIN dbo.T2 AS b ON b.Id = a.Id;",
            "PE026");
        Assert.Empty(diags);
    }
}
