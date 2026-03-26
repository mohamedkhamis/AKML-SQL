using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Design;

public sealed class De006Tests
{
    [Fact]
    public void SqlVariantColumn_Fires()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "CREATE TABLE T (Data SQL_VARIANT)", "DE006");
        Assert.Single(diags);
        Assert.Equal("DE006", diags[0].RuleId);
    }

    [Fact]
    public void NvarcharMaxColumn_DoesNotFire()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "CREATE TABLE T (Data NVARCHAR(MAX))", "DE006");
        Assert.Empty(diags);
    }
}
