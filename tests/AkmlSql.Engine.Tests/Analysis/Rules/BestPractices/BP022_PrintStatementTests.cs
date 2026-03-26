using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.BestPractices;

public sealed class BP022_PrintStatementTests
{
    [Fact]
    public void Fires_OnPrint()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "PRINT 'hello';",
            "BP022");
        Assert.Single(diags);
    }

    [Fact]
    public void DoesNotFire_WithoutPrint()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "SELECT 1;",
            "BP022");
        Assert.Empty(diags);
    }
}
