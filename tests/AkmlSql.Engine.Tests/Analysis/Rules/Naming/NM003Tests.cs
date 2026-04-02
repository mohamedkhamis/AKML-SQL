using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Naming;

public sealed class Nm003Tests
{
    [Fact]
    public void TblPrefixedTable_Fires()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "CREATE TABLE tblOrders (Id INT PRIMARY KEY)", "NM003");
        Assert.Single(diags);
        Assert.Equal("NM003", diags[0].RuleId);
    }

    [Fact]
    public void VwPrefixedView_Fires()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "CREATE VIEW vwActiveOrders AS SELECT 1 AS Id", "NM003");
        Assert.Single(diags);
        Assert.Equal("NM003", diags[0].RuleId);
    }

    [Fact]
    public void UnprefixedTable_DoesNotFire()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "CREATE TABLE Orders (Id INT PRIMARY KEY)", "NM003");
        Assert.Empty(diags);
    }
}
