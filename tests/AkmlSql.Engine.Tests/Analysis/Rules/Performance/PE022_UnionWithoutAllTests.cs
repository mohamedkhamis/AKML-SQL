using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Performance;

public sealed class Pe022UnionWithoutAllTests
{
    [Fact]
    public void Fires_OnUnionWithoutAll()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "SELECT Id FROM dbo.T1 UNION SELECT Id FROM dbo.T2;",
            "PE022");
        Assert.Single(diags);
    }

    [Fact]
    public void DoesNotFire_OnUnionAll()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "SELECT Id FROM dbo.T1 UNION ALL SELECT Id FROM dbo.T2;",
            "PE022");
        Assert.Empty(diags);
    }
}
