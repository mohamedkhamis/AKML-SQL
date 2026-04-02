using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Deprecated;

public sealed class Dep003Tests
{
    [Fact]
    public void SetFmtonlyOn_Fires()
    {
        const string sql = "SET FMTONLY ON";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "DEP003");

        Assert.Single(diags);
        Assert.Equal("DEP003", diags[0].RuleId);
    }

    [Fact]
    public void SetNocount_DoesNotFire()
    {
        const string sql = "SET NOCOUNT ON";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "DEP003");

        Assert.Empty(diags);
    }

    [Fact]
    public void SetFmtonlyOff_DoesNotFire()
    {
        const string sql = "SET FMTONLY OFF";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "DEP003");

        Assert.Empty(diags);
    }
}
