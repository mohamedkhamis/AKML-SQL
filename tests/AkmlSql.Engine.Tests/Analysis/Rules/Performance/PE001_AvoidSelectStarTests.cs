using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Performance;

public sealed class Pe001AvoidSelectStarTests
{
    [Fact]
    public void FiresOnSelectStarInsideProcedure()
    {
        const string sql = """
            CREATE PROCEDURE dbo.GetOrders AS
            BEGIN
                SELECT * FROM dbo.Orders
            END
            """;

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE001");

        Assert.Single(diags);
        Assert.Equal("PE001", diags[0].RuleId);
    }

    [Fact]
    public void DoesNotFireOnSelectStarInAdHocQuery()
    {
        const string sql = "SELECT * FROM dbo.Orders";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE001");

        Assert.Empty(diags);
    }

    [Fact]
    public void DoesNotFireWhenExplicitColumnsUsed()
    {
        const string sql = """
            CREATE PROCEDURE dbo.GetOrders AS
            BEGIN
                SELECT Id, Name FROM dbo.Orders
            END
            """;

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE001");

        Assert.Empty(diags);
    }

    [Fact]
    public void HasSuppressFixAction()
    {
        const string sql = """
            CREATE PROCEDURE dbo.GetOrders AS
            BEGIN
                SELECT * FROM dbo.Orders
            END
            """;

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE001");

        Assert.Single(diags);
        Assert.NotEmpty(diags[0].FixActions);
        Assert.Equal("PE001", diags[0].FixActions[0].SuppressRuleId);
    }

    [Fact]
    public void FiresOnSelectStarInAlterProcedure()
    {
        const string sql = """
            ALTER PROCEDURE dbo.GetOrders AS
            BEGIN
                SELECT * FROM dbo.Orders
            END
            """;

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE001");

        Assert.Single(diags);
    }
}
