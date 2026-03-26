using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Performance;

public sealed class PE033_LockHintUsageTests
{
    [Fact]
    public void Fires_OnNoLock()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "SELECT Id FROM dbo.T WITH (NOLOCK);",
            "PE033");
        Assert.Single(diags);
    }

    [Fact]
    public void DoesNotFire_WithoutHint()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "SELECT Id FROM dbo.T;",
            "PE033");
        Assert.Empty(diags);
    }
}
