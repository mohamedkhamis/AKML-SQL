using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Design;

public sealed class De003Tests
{
    [Fact]
    public void ExplicitlyNullableColumnInPrimaryKey_Fires()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "CREATE TABLE T (Id INT NULL, CONSTRAINT PK_T PRIMARY KEY (Id))", "DE003");
        Assert.Single(diags);
        Assert.Equal("DE003", diags[0].RuleId);
    }

    [Fact]
    public void NotNullColumnInPrimaryKey_DoesNotFire()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "CREATE TABLE T (Id INT NOT NULL PRIMARY KEY)", "DE003");
        Assert.Empty(diags);
    }
}
