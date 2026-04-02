using AkmlSql.Core.Models.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Performance;

public sealed class Pe003MissingWhereTests
{
    [Fact]
    public void FiresOnDeleteWithoutWhere()
    {
        const string sql = "DELETE FROM dbo.Orders";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE003");

        Assert.Single(diags);
        Assert.Equal("PE003", diags[0].RuleId);
    }

    [Fact]
    public void DoesNotFireOnDeleteWithWhere()
    {
        const string sql = "DELETE FROM dbo.Orders WHERE Id = 1";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE003");

        Assert.Empty(diags);
    }

    [Fact]
    public void FiresOnUpdateWithoutWhere()
    {
        const string sql = "UPDATE dbo.Orders SET Status = 'Shipped'";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE003");

        Assert.Single(diags);
        Assert.Equal("PE003", diags[0].RuleId);
    }

    [Fact]
    public void DoesNotFireOnUpdateWithWhere()
    {
        const string sql = "UPDATE dbo.Orders SET Status = 'Shipped' WHERE Id = 42";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE003");

        Assert.Empty(diags);
    }

    [Fact]
    public void SeverityIsError()
    {
        const string sql = "DELETE FROM dbo.Orders";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE003");

        Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
    }

    [Fact]
    public void NoFixActionsProvided()
    {
        const string sql = "DELETE FROM dbo.Orders";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE003");

        Assert.Single(diags);
        Assert.Empty(diags[0].FixActions);
    }
}
