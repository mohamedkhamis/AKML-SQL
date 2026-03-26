using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Execution;

public sealed class Ex001Tests
{
    [Fact]
    public void DivisionByLiteralZero_Fires()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "SELECT 10 / 0", "EX001");
        Assert.Single(diags);
        Assert.Equal("EX001", diags[0].RuleId);
    }

    [Fact]
    public void DivisionByNonZeroLiteral_DoesNotFire()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "SELECT 10 / 2", "EX001");
        Assert.Empty(diags);
    }

    [Fact]
    public void DivisionByVariable_DoesNotFire()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "DECLARE @d INT = 5; SELECT 10 / @d", "EX001");
        Assert.Empty(diags);
    }
}
