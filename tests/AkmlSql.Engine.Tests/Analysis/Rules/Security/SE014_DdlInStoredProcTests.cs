using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Security;

public sealed class Se014DdlInStoredProcTests
{
    [Fact]
    public void Fires_OnCreateTableInsideProc()
    {
        var diags = AnalysisEngineTestHelper.Analyze("""
            CREATE PROCEDURE dbo.usp_Test AS
            BEGIN
                CREATE TABLE #tmp (Id INT);
            END
            """,
            "SE014");
        Assert.NotEmpty(diags);
    }

    [Fact]
    public void DoesNotFire_OnStandaloneCreateTable()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "CREATE TABLE dbo.Products (Id INT NOT NULL PRIMARY KEY);",
            "SE014");
        Assert.Empty(diags);
    }
}
