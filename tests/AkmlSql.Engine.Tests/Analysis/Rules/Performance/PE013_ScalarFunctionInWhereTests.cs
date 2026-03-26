using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Performance;

public sealed class Pe013ScalarFunctionInWhereTests
{
    [Fact]
    public void FiresOnFunctionCallWrappingColumnInWhere()
    {
        const string sql = "SELECT * FROM T WHERE LEN(Name) > 5";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE013");

        Assert.Single(diags);
        Assert.Equal("PE013", diags[0].RuleId);
    }

    [Fact]
    public void FiresOnYearFunctionOnColumnInWhere()
    {
        const string sql = "SELECT * FROM T WHERE YEAR(OrderDate) = 2024";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE013");

        Assert.Single(diags);
    }

    [Fact]
    public void DoesNotFireOnLikePredicate()
    {
        const string sql = "SELECT * FROM T WHERE Name LIKE '%test%'";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE013");

        Assert.Empty(diags);
    }

    [Fact]
    public void DoesNotFireOnFunctionCalledWithLiteralArgument()
    {
        // Function on a literal, not a column reference — should not fire
        const string sql = "SELECT * FROM T WHERE Id > LEN('somestring')";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE013");

        Assert.Empty(diags);
    }

    [Fact]
    public void DoesNotFireOnDirectColumnComparison()
    {
        const string sql = "SELECT * FROM T WHERE Name = 'test'";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE013");

        Assert.Empty(diags);
    }

    [Fact]
    public void DoesNotFireOnFunctionInSelectClause()
    {
        // Function on column in SELECT, not WHERE — should not fire
        const string sql = "SELECT LEN(Name) FROM T WHERE Id = 1";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE013");

        Assert.Empty(diags);
    }
}
