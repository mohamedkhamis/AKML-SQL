using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.BestPractices;

public sealed class Bp015IfWithoutBeginEndTests
{
    [Fact]
    public void FiresOnIfWithoutBeginEnd()
    {
        const string sql = "IF 1=1 SELECT 1";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP015");

        Assert.Single(diags);
        Assert.Equal("BP015", diags[0].RuleId);
    }

    [Fact]
    public void DoesNotFireOnIfWithBeginEnd()
    {
        const string sql = "IF 1=1 BEGIN SELECT 1 END";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP015");

        Assert.Empty(diags);
    }

    [Fact]
    public void DoesNotFireOnPlainSelect()
    {
        const string sql = "SELECT 1";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP015");

        Assert.Empty(diags);
    }

    [Fact]
    public void FiresOnIfWithDeleteBodyAndNoBeginEnd()
    {
        const string sql = "IF @flag = 1 DELETE FROM T WHERE Id = 1";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP015");

        Assert.Single(diags);
    }

    [Fact]
    public void FixActionWrapsBodyWithBeginEnd()
    {
        const string sql = "IF 1=1 SELECT 1";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP015");

        Assert.Single(diags);
        Assert.NotEmpty(diags[0].FixActions);
        Assert.Contains("BEGIN", diags[0].FixActions[0].ReplacementText);
        Assert.Contains("END", diags[0].FixActions[0].ReplacementText);
    }

    [Fact]
    public void FiresTwiceForTwoIfStatementsWithoutBeginEnd()
    {
        const string sql = """
            IF 1=1 SELECT 1;
            IF 2=2 SELECT 2
            """;

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP015");

        Assert.Equal(2, diags.Count);
    }
}
