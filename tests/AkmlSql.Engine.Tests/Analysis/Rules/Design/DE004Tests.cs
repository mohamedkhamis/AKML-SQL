using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Design;

public sealed class De004Tests
{
    [Fact]
    public void VarcharOne_Fires()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "CREATE TABLE T (Flag VARCHAR(1))", "DE004");
        Assert.Single(diags);
        Assert.Equal("DE004", diags[0].RuleId);
    }

    [Fact]
    public void VarcharTwo_Fires()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "CREATE TABLE T (Code VARCHAR(2))", "DE004");
        Assert.Single(diags);
        Assert.Equal("DE004", diags[0].RuleId);
    }

    [Fact]
    public void VarcharLargerSize_DoesNotFire()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "CREATE TABLE T (Flag VARCHAR(50))", "DE004");
        Assert.Empty(diags);
    }
}
