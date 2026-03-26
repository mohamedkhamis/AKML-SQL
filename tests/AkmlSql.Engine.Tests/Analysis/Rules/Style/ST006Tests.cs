using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Style;

public sealed class St006Tests
{
    [Fact]
    public void UnnecessaryBracketsOnNormalIdentifiers_Fires()
    {
        const string sql = "SELECT [Name] FROM [Orders]";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "ST006");

        Assert.NotEmpty(diags);
        Assert.All(diags, d => Assert.Equal("ST006", d.RuleId));
    }

    [Fact]
    public void NoBracketsOnNormalIdentifiers_DoesNotFire()
    {
        const string sql = "SELECT Name FROM Orders";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "ST006");

        Assert.Empty(diags);
    }

    [Fact]
    public void BracketsOnNameWithSpace_DoesNotFire()
    {
        const string sql = "SELECT [Order Date] FROM Orders";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "ST006");

        Assert.Empty(diags);
    }

    [Fact]
    public void TwoBracketedIdentifiers_FiresTwice()
    {
        const string sql = "SELECT [Name] FROM [Orders]";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "ST006");

        Assert.Equal(2, diags.Count);
    }

    [Fact]
    public void FixActionRemovesBrackets()
    {
        const string sql = "SELECT [Name] FROM Orders";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "ST006");

        Assert.Single(diags);
        Assert.Single(diags[0].FixActions);
        Assert.Equal("Name", diags[0].FixActions[0].ReplacementText);
    }
}
