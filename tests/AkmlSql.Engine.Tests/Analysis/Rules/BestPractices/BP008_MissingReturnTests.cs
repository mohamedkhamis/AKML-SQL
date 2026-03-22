using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.BestPractices;

public sealed class BP008_MissingReturnTests
{
    // The BP008 rule checks whether the last statement in StatementList.Statements is a
    // ReturnStatement. When the proc body uses BEGIN/END, the last top-level statement is
    // a BeginEndBlockStatement — so both fire and no-fire tests use bodies without BEGIN/END
    // to keep the statement layout flat and unambiguous.

    [Fact]
    public void FiresOnProcWithNoReturnStatement()
    {
        const string sql = "CREATE PROCEDURE dbo.P AS SELECT 1";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP008");

        Assert.Single(diags);
        Assert.Equal("BP008", diags[0].RuleId);
    }

    [Fact]
    public void DoesNotFireOnProcEndingWithReturn()
    {
        const string sql = "CREATE PROCEDURE dbo.P AS SELECT 1; RETURN 0";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP008");

        Assert.Empty(diags);
    }

    [Fact]
    public void FiresOnAlterProcWithNoReturn()
    {
        const string sql = "ALTER PROCEDURE dbo.P AS SELECT 1";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP008");

        Assert.Single(diags);
    }

    [Fact]
    public void DoesNotFireOnAlterProcWithReturn()
    {
        const string sql = "ALTER PROCEDURE dbo.P AS SELECT 1; RETURN 0";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP008");

        Assert.Empty(diags);
    }

    [Fact]
    public void FixActionInsertsReturnStatement()
    {
        const string sql = "CREATE PROCEDURE dbo.P AS SELECT 1";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP008");

        Assert.Single(diags);
        Assert.NotEmpty(diags[0].FixActions);
        Assert.Contains("RETURN 0", diags[0].FixActions[0].ReplacementText);
    }
}
