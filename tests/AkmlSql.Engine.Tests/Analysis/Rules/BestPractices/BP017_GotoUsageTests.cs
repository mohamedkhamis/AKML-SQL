using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.BestPractices;

public sealed class BP017_GotoUsageTests
{
    [Fact]
    public void Fires_OnGoto()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "GOTO label1;\nlabel1: PRINT 'done';",
            "BP017");
        Assert.Single(diags);
    }

    [Fact]
    public void DoesNotFire_WithoutGoto()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "PRINT 'done';",
            "BP017");
        Assert.Empty(diags);
    }
}
