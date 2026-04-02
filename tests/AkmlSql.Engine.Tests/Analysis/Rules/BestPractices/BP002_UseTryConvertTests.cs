using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.BestPractices;

public sealed class Bp002UseTryConvertTests
{
    [Fact]
    public void FiresOnIsNumericUsage()
    {
        const string sql = "SELECT * FROM T WHERE ISNUMERIC(Col) = 1";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP002");

        Assert.Single(diags);
        Assert.Equal("BP002", diags[0].RuleId);
    }

    [Fact]
    public void FiresOnIsNumericInSelectClause()
    {
        const string sql = "SELECT ISNUMERIC(Col) FROM T";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP002");

        Assert.Single(diags);
    }

    [Fact]
    public void DoesNotFireOnTryConvertIsNotNull()
    {
        const string sql = "SELECT * FROM T WHERE TRY_CONVERT(decimal(18,2), Col) IS NOT NULL";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP002");

        Assert.Empty(diags);
    }

    [Fact]
    public void DoesNotFireOnOtherFunctions()
    {
        const string sql = "SELECT * FROM T WHERE LEN(Col) > 0";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP002");

        Assert.Empty(diags);
    }

    [Fact]
    public void FixActionSuggestsTryConvert()
    {
        const string sql = "SELECT ISNUMERIC(Col) FROM T";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP002");

        Assert.Single(diags);
        Assert.NotEmpty(diags[0].FixActions);
        Assert.Contains("TRY_CONVERT", diags[0].FixActions[0].ReplacementText);
        Assert.Contains("IS NOT NULL", diags[0].FixActions[0].ReplacementText);
    }

    [Fact]
    public void FiresTwiceForTwoIsNumericCalls()
    {
        const string sql = "SELECT ISNUMERIC(A), ISNUMERIC(B) FROM T";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP002");

        Assert.Equal(2, diags.Count);
    }
}
