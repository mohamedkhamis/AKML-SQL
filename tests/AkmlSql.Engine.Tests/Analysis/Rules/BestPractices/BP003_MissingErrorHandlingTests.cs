using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.BestPractices;

public sealed class Bp003MissingErrorHandlingTests
{
    // The BP003 rule inspects StatementList.Statements directly (not recursively).
    // Proc bodies wrapped in BEGIN/END present a single BeginEndBlockStatement at
    // the top level, so DML and TRY/CATCH statements must be at the top level of
    // the proc body to be detected.

    [Fact]
    public void FiresOnProcWithDeleteAndNoTryCatch()
    {
        const string sql = "CREATE PROCEDURE dbo.P AS DELETE FROM T WHERE Id = 1";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP003");

        Assert.Single(diags);
        Assert.Equal("BP003", diags[0].RuleId);
    }

    [Fact]
    public void FiresOnProcWithInsertAndNoTryCatch()
    {
        const string sql = "CREATE PROCEDURE dbo.P AS INSERT INTO T (Id) VALUES (1)";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP003");

        Assert.Single(diags);
    }

    [Fact]
    public void FiresOnProcWithUpdateAndNoTryCatch()
    {
        const string sql = "CREATE PROCEDURE dbo.P AS UPDATE T SET Name = 'x' WHERE Id = 1";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP003");

        Assert.Single(diags);
    }

    [Fact]
    public void DoesNotFireOnProcWithTryCatch()
    {
        const string sql = """
            CREATE PROCEDURE dbo.P AS
            BEGIN TRY
                DELETE FROM T WHERE Id = 1
            END TRY
            BEGIN CATCH
                THROW
            END CATCH
            """;

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP003");

        Assert.Empty(diags);
    }

    [Fact]
    public void DoesNotFireOnProcWithOnlySelect()
    {
        const string sql = """
            CREATE PROCEDURE dbo.P AS
            BEGIN
                SELECT 1
            END
            """;

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP003");

        Assert.Empty(diags);
    }

    [Fact]
    public void FiresOnAlterProcWithDmlAndNoTryCatch()
    {
        const string sql = "ALTER PROCEDURE dbo.P AS DELETE FROM T WHERE Id = 1";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP003");

        Assert.Single(diags);
    }
}
