using System.Linq;
using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Performance;

public sealed class PE011_OrderByInInsertTests
{
    [Fact]
    public void FiresOnInsertSelectWithOrderByAndNoTop()
    {
        // The ScriptDom parser attaches ORDER BY to the QuerySpecification when embedded
        // inside an INSERT … SELECT, which is the pattern PE011 detects.
        const string sql = "INSERT INTO T (col) SELECT col FROM Src ORDER BY col";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE011");

        Assert.Single(diags);
        Assert.Equal("PE011", diags[0].RuleId);
    }

    [Fact]
    public void DoesNotFireOnInsertSelectWithTop()
    {
        const string sql = "INSERT INTO T (col) SELECT TOP 10 col FROM Src ORDER BY col";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE011");

        Assert.Empty(diags);
    }

    [Fact]
    public void DoesNotFireOnPlainSelect()
    {
        var diags = AnalysisEngineTestHelper.Analyze("SELECT col FROM Src ORDER BY col", "PE011");

        Assert.Empty(diags);
    }

    [Fact]
    public void DoesNotFireOnInsertWithValues()
    {
        var diags = AnalysisEngineTestHelper.Analyze("INSERT INTO T (Id) VALUES (1)", "PE011");

        Assert.Empty(diags);
    }
}
