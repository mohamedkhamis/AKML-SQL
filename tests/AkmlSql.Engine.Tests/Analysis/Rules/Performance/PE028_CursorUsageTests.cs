using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Performance;

public sealed class Pe028CursorUsageTests
{
    [Fact]
    public void Fires_OnDeclareCursor()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "DECLARE myCursor CURSOR FOR SELECT Id FROM dbo.T;",
            "PE028");
        Assert.Single(diags);
    }

    [Fact]
    public void DoesNotFire_WithNoCursor()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "SELECT Id FROM dbo.T;",
            "PE028");
        Assert.Empty(diags);
    }
}
