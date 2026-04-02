using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Execution;

public sealed class Ex006Tests
{
    [Fact]
    public void AlwaysTrueIntegerLiterals_Fires()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "SELECT * FROM T WHERE 1 = 1", "EX006");
        Assert.Single(diags);
        Assert.Equal("EX006", diags[0].RuleId);
    }

    [Fact]
    public void AlwaysTrueStringLiterals_Fires()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "SELECT * FROM T WHERE 'a' = 'a'", "EX006");
        Assert.Single(diags);
        Assert.Equal("EX006", diags[0].RuleId);
    }

    [Fact]
    public void ColumnComparedToLiteral_DoesNotFire()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "SELECT * FROM T WHERE Id = 1", "EX006");
        Assert.Empty(diags);
    }

    [Fact]
    public void DifferentLiterals_DoesNotFire()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "SELECT * FROM T WHERE 1 = 2", "EX006");
        Assert.Empty(diags);
    }
}
