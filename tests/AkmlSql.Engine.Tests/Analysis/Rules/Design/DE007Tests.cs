using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Design;

public sealed class De007Tests
{
    [Fact]
    public void IdentityOnDecimal_Fires()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "CREATE TABLE T (Id DECIMAL(10,0) IDENTITY(1,1))", "DE007");
        Assert.Single(diags);
        Assert.Equal("DE007", diags[0].RuleId);
    }

    [Fact]
    public void IdentityOnInt_DoesNotFire()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "CREATE TABLE T (Id INT IDENTITY(1,1))", "DE007");
        Assert.Empty(diags);
    }

    [Fact]
    public void IdentityOnBigInt_DoesNotFire()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "CREATE TABLE T (Id BIGINT IDENTITY(1,1))", "DE007");
        Assert.Empty(diags);
    }
}
