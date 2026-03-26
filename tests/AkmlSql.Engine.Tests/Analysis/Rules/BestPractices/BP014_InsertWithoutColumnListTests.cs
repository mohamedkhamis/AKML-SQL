using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.BestPractices;

public sealed class BP014_InsertWithoutColumnListTests
{
    [Fact]
    public void FiresOnInsertValuesWithoutColumnList()
    {
        const string sql = "INSERT INTO T VALUES (1, 'test')";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP014");

        Assert.Single(diags);
        Assert.Equal("BP014", diags[0].RuleId);
    }

    [Fact]
    public void FiresOnInsertSelectWithoutColumnList()
    {
        const string sql = "INSERT INTO T SELECT Id, Name FROM Src";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP014");

        Assert.Single(diags);
    }

    [Fact]
    public void DoesNotFireOnInsertWithExplicitColumnList()
    {
        const string sql = "INSERT INTO T (Id, Name) VALUES (1, 'test')";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP014");

        Assert.Empty(diags);
    }

    [Fact]
    public void DoesNotFireOnInsertSelectWithExplicitColumnList()
    {
        const string sql = "INSERT INTO T (Id, Name) SELECT Id, Name FROM Src";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP014");

        Assert.Empty(diags);
    }

    [Fact]
    public void DoesNotFireOnPlainSelect()
    {
        const string sql = "SELECT Id FROM T";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP014");

        Assert.Empty(diags);
    }

    [Fact]
    public void FiresTwiceForTwoInsertWithoutColumnLists()
    {
        const string sql = """
            INSERT INTO T VALUES (1);
            INSERT INTO U VALUES (2)
            """;

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP014");

        Assert.Equal(2, diags.Count);
    }
}
