using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Performance;

public sealed class Pe017NonSargableExpressionTests
{
    [Fact]
    public void FiresOnIsNullWrappingColumnInWhere()
    {
        const string sql = "SELECT * FROM T WHERE ISNULL(Name, '') = 'test'";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE017");

        Assert.Single(diags);
        Assert.Equal("PE017", diags[0].RuleId);
    }

    [Fact]
    public void FiresOnUpperWrappingColumnInWhere()
    {
        const string sql = "SELECT * FROM T WHERE UPPER(Name) = 'TEST'";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE017");

        Assert.Single(diags);
    }

    [Fact]
    public void FiresOnYearWrappingColumnInWhere()
    {
        const string sql = "SELECT * FROM T WHERE YEAR(OrderDate) = 2024";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE017");

        Assert.Single(diags);
    }

    [Fact]
    public void FiresOnCastWrappingColumnInWhere()
    {
        const string sql = "SELECT * FROM T WHERE CAST(Id AS VARCHAR(10)) = '42'";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE017");

        Assert.Single(diags);
    }

    [Fact]
    public void FiresOnConvertWrappingColumnInWhere()
    {
        const string sql = "SELECT * FROM T WHERE CONVERT(VARCHAR(10), Id) = '42'";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE017");

        Assert.Single(diags);
    }

    [Fact]
    public void DoesNotFireOnDirectColumnComparison()
    {
        const string sql = "SELECT * FROM T WHERE Name = 'test'";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE017");

        Assert.Empty(diags);
    }

    [Fact]
    public void DoesNotFireOnFunctionInSelectNotWhere()
    {
        const string sql = "SELECT ISNULL(Name, '') FROM T WHERE Id = 1";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE017");

        Assert.Empty(diags);
    }

    [Fact]
    public void DoesNotFireOnLtrimWithLiteralArgument()
    {
        // Function applied to a literal, not a column reference — should not fire
        const string sql = "SELECT * FROM T WHERE Name = LTRIM(' test ')";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE017");

        Assert.Empty(diags);
    }
}
