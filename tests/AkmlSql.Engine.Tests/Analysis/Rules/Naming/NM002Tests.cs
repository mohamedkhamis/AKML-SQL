using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Naming;

public sealed class NM002Tests
{
    [Fact]
    public void SpPrefixedProcedure_Fires()
    {
        const string sql = """
            CREATE PROCEDURE sp_MyProcedure AS
            BEGIN
                SELECT 1
            END
            """;
        var diags = AnalysisEngineTestHelper.Analyze(sql, "NM002");
        Assert.Single(diags);
        Assert.Equal("NM002", diags[0].RuleId);
    }

    [Fact]
    public void UspPrefixedProcedure_DoesNotFire()
    {
        const string sql = """
            CREATE PROCEDURE usp_MyProcedure AS
            BEGIN
                SELECT 1
            END
            """;
        var diags = AnalysisEngineTestHelper.Analyze(sql, "NM002");
        Assert.Empty(diags);
    }

    [Fact]
    public void NoPrefixedProcedure_DoesNotFire()
    {
        const string sql = """
            CREATE PROCEDURE GetOrders AS
            BEGIN
                SELECT 1
            END
            """;
        var diags = AnalysisEngineTestHelper.Analyze(sql, "NM002");
        Assert.Empty(diags);
    }
}
