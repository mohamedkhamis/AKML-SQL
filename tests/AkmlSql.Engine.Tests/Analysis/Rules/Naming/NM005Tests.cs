using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Naming;

public sealed class NM005Tests
{
    [Fact]
    public void HyphenInBracketedIdentifier_Fires()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "SELECT [Order-Id] FROM T", "NM005");
        Assert.Single(diags);
        Assert.Equal("NM005", diags[0].RuleId);
    }

    [Fact]
    public void SpaceInBracketedIdentifier_Fires()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "SELECT [First Name] FROM T", "NM005");
        Assert.Single(diags);
        Assert.Equal("NM005", diags[0].RuleId);
    }

    [Fact]
    public void AlphanumericBracketedIdentifier_DoesNotFire()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "SELECT [OrderId] FROM T", "NM005");
        Assert.Empty(diags);
    }
}
