using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Execution;

public sealed class Ex004Tests
{
    // The rule inspects CreateProcedureStatement.StatementList directly.
    // When a procedure body is written without BEGIN...END the statement list
    // is flat; with BEGIN...END the block is a single BeginEndBlockStatement.
    // These tests use the flat form so the rule can see adjacent statements.

    [Fact]
    public void StatementAfterReturn_Fires()
    {
        const string sql = "CREATE PROCEDURE P AS RETURN 0; SELECT 'unreachable'";
        var diags = AnalysisEngineTestHelper.Analyze(sql, "EX004");
        Assert.Single(diags);
        Assert.Equal("EX004", diags[0].RuleId);
    }

    [Fact]
    public void ReturnAtEnd_DoesNotFire()
    {
        const string sql = "CREATE PROCEDURE P AS SELECT 1; RETURN 0";
        var diags = AnalysisEngineTestHelper.Analyze(sql, "EX004");
        Assert.Empty(diags);
    }

    [Fact]
    public void StatementAfterThrow_Fires()
    {
        const string sql = "CREATE PROCEDURE P AS THROW 50000, 'error', 1; SELECT 1";
        var diags = AnalysisEngineTestHelper.Analyze(sql, "EX004");
        Assert.Single(diags);
        Assert.Equal("EX004", diags[0].RuleId);
    }
}
