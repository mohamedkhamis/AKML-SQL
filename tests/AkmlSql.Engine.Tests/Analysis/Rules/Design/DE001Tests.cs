using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Design;

public sealed class De001Tests
{
    [Fact]
    public void TableWithoutPrimaryKey_Fires()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "CREATE TABLE Orders (Id INT, Name VARCHAR(100))", "DE001");
        Assert.Single(diags);
        Assert.Equal("DE001", diags[0].RuleId);
    }

    [Fact]
    public void TableWithInlinePrimaryKey_DoesNotFire()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "CREATE TABLE Orders (Id INT PRIMARY KEY, Name VARCHAR(100))", "DE001");
        Assert.Empty(diags);
    }

    [Fact]
    public void TableWithTableLevelPrimaryKey_DoesNotFire()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "CREATE TABLE Orders (Id INT NOT NULL, Name VARCHAR(100), CONSTRAINT PK_Orders PRIMARY KEY (Id))", "DE001");
        Assert.Empty(diags);
    }
}
