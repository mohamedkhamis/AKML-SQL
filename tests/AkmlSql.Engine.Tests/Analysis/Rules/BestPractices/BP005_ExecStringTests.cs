using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.BestPractices;

public sealed class Bp005ExecStringTests
{
    [Fact]
    public void FiresOnExecWithStringLiteral()
    {
        const string sql = "EXEC('SELECT * FROM T')";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP005");

        Assert.Single(diags);
        Assert.Equal("BP005", diags[0].RuleId);
    }

    [Fact]
    public void FiresOnExecuteWithStringLiteral()
    {
        const string sql = "EXECUTE('SELECT 1')";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP005");

        Assert.Single(diags);
    }

    [Fact]
    public void DoesNotFireOnExecSpExecuteSql()
    {
        const string sql = "EXEC sp_executesql N'SELECT 1'";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP005");

        Assert.Empty(diags);
    }

    [Fact]
    public void DoesNotFireOnExecStoredProcedure()
    {
        const string sql = "EXEC dbo.MyProcedure @param = 1";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP005");

        Assert.Empty(diags);
    }

    [Fact]
    public void DoesNotFireOnPlainSelect()
    {
        const string sql = "SELECT 1";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP005");

        Assert.Empty(diags);
    }
}
