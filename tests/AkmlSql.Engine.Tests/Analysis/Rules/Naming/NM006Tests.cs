using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Naming;

public sealed class Nm006Tests
{
    [Fact]
    public void SingleLetterTableAlias_Fires()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "SELECT o.Id FROM Orders o", "NM006");
        Assert.Single(diags);
        Assert.Equal("NM006", diags[0].RuleId);
    }

    [Fact]
    public void MultiLetterTableAlias_DoesNotFire()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "SELECT ord.Id FROM Orders ord", "NM006");
        Assert.Empty(diags);
    }
}
