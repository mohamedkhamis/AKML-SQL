using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Performance;

public sealed class PE016_CorrelatedSubqueryTests
{
    [Fact]
    public void FiresOnScalarSubqueryInWhere()
    {
        const string sql = "SELECT * FROM T WHERE (SELECT COUNT(*) FROM T2 WHERE T2.Id = T.Id) > 0";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE016");

        Assert.Single(diags);
        Assert.Equal("PE016", diags[0].RuleId);
    }

    [Fact]
    public void FiresOnExistsSubqueryInWhere()
    {
        // EXISTS (SELECT ...) also contains a ScalarSubquery node in the ScriptDom AST,
        // so the rule fires on EXISTS predicates in WHERE as well.
        const string sql = "SELECT * FROM T WHERE EXISTS (SELECT 1 FROM T2 WHERE T2.Id = T.Id)";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE016");

        Assert.Single(diags);
    }

    [Fact]
    public void DoesNotFireOnSimpleWherePredicate()
    {
        const string sql = "SELECT * FROM T WHERE Id = 1";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE016");

        Assert.Empty(diags);
    }

    [Fact]
    public void DoesNotFireOnSubqueryInSelectClause()
    {
        // Scalar subquery in SELECT, not WHERE — should not fire
        const string sql = "SELECT Id, (SELECT Name FROM T2 WHERE T2.Id = T.Id) AS Name FROM T";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE016");

        Assert.Empty(diags);
    }

    [Fact]
    public void FiresOnSecondScalarSubqueryInWhere()
    {
        const string sql = "SELECT * FROM T WHERE (SELECT MAX(Val) FROM T3 WHERE T3.Id = T.Id) < 100";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE016");

        Assert.Single(diags);
        Assert.Equal("PE016", diags[0].RuleId);
    }
}
