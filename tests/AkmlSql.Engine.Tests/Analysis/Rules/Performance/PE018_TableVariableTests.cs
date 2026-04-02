using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Performance;

public sealed class Pe018TableVariableTests
{
    [Fact]
    public void FiresOnDeclareTableVariable()
    {
        const string sql = "DECLARE @t TABLE (Id INT, Name NVARCHAR(100))";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE018");

        Assert.Single(diags);
        Assert.Equal("PE018", diags[0].RuleId);
    }

    [Fact]
    public void FiresOnDeclareTableVariableWithInsert()
    {
        const string sql = """
            DECLARE @t TABLE (Id INT);
            INSERT INTO @t VALUES (1)
            """;

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE018");

        Assert.Single(diags);
    }

    [Fact]
    public void DoesNotFireOnPlainSelect()
    {
        const string sql = "SELECT 1";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE018");

        Assert.Empty(diags);
    }

    [Fact]
    public void DoesNotFireOnDeclareScalarVariable()
    {
        const string sql = "DECLARE @id INT = 1";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE018");

        Assert.Empty(diags);
    }

    [Fact]
    public void FiresTwiceForTwoTableVariableDeclarations()
    {
        const string sql = """
            DECLARE @t1 TABLE (Id INT);
            DECLARE @t2 TABLE (Name NVARCHAR(100))
            """;

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE018");

        Assert.Equal(2, diags.Count);
    }
}
