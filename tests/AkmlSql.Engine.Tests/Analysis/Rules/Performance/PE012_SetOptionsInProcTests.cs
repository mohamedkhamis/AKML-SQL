using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Performance;

public sealed class PE012_SetOptionsInProcTests
{
    [Fact]
    public void FiresOnSetAnsiNullsInsideProc()
    {
        // The rule inspects StatementList.Statements directly (not recursively),
        // so SET options must appear at the top level of the proc body, not inside BEGIN/END.
        const string sql = "CREATE PROCEDURE dbo.TestProc AS SET ANSI_NULLS ON";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE012");

        Assert.Single(diags);
        Assert.Equal("PE012", diags[0].RuleId);
    }

    [Fact]
    public void FiresOnSetQuotedIdentifierInsideProc()
    {
        const string sql = "CREATE PROCEDURE dbo.TestProc AS SET QUOTED_IDENTIFIER ON";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE012");

        Assert.Single(diags);
        Assert.Equal("PE012", diags[0].RuleId);
    }

    [Fact]
    public void FiresOnSetAnsiWarningsInsideProc()
    {
        const string sql = "CREATE PROCEDURE dbo.TestProc AS SET ANSI_WARNINGS ON";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE012");

        Assert.Single(diags);
        Assert.Equal("PE012", diags[0].RuleId);
    }

    [Fact]
    public void DoesNotFireOnProcWithNoSetOptions()
    {
        const string sql = """
            CREATE PROCEDURE dbo.TestProc AS
            BEGIN
                SELECT 1
            END
            """;

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE012");

        Assert.Empty(diags);
    }

    [Fact]
    public void DoesNotFireOnSetOptionsOutsideProc()
    {
        const string sql = "SET ANSI_NULLS ON";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE012");

        Assert.Empty(diags);
    }

    [Fact]
    public void FiresOnSetConcatNullYieldsNullInsideProc()
    {
        const string sql = "CREATE PROCEDURE dbo.TestProc AS SET CONCAT_NULL_YIELDS_NULL ON";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE012");

        Assert.Single(diags);
    }

    [Fact]
    public void FiresOnAlterProcWithSetOptions()
    {
        const string sql = "ALTER PROCEDURE dbo.TestProc AS SET ANSI_NULLS ON";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "PE012");

        Assert.Single(diags);
        Assert.Equal("PE012", diags[0].RuleId);
    }
}
