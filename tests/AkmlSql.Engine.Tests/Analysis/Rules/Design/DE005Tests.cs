using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Design;

public sealed class DE005Tests
{
    [Fact]
    public void FloatColumnWithFinancialName_Fires()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "CREATE TABLE T (Price FLOAT)", "DE005");
        Assert.Single(diags);
        Assert.Equal("DE005", diags[0].RuleId);
    }

    [Fact]
    public void RealColumnWithFinancialName_Fires()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "CREATE TABLE T (TotalAmount REAL)", "DE005");
        Assert.Single(diags);
        Assert.Equal("DE005", diags[0].RuleId);
    }

    [Fact]
    public void DecimalColumnWithFinancialName_DoesNotFire()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "CREATE TABLE T (Price DECIMAL(18,2))", "DE005");
        Assert.Empty(diags);
    }

    [Fact]
    public void FloatColumnWithNonFinancialName_DoesNotFire()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "CREATE TABLE T (Ratio FLOAT)", "DE005");
        Assert.Empty(diags);
    }
}
