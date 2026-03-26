using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.BestPractices;

public sealed class BP007_EmptyCatchBlockTests
{
    [Fact]
    public void FiresOnEmptyCatchBlock()
    {
        const string sql = """
            BEGIN TRY
                SELECT 1/0
            END TRY
            BEGIN CATCH
            END CATCH
            """;

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP007");

        Assert.Single(diags);
        Assert.Equal("BP007", diags[0].RuleId);
    }

    [Fact]
    public void DoesNotFireOnCatchBlockWithThrow()
    {
        const string sql = """
            BEGIN TRY
                SELECT 1/0
            END TRY
            BEGIN CATCH
                THROW
            END CATCH
            """;

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP007");

        Assert.Empty(diags);
    }

    [Fact]
    public void DoesNotFireOnCatchBlockWithRaiserror()
    {
        const string sql = """
            BEGIN TRY
                SELECT 1/0
            END TRY
            BEGIN CATCH
                RAISERROR('Error', 16, 1)
            END CATCH
            """;

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP007");

        Assert.Empty(diags);
    }

    [Fact]
    public void DoesNotFireWhenNoCatchExists()
    {
        // Plain statement with no TRY/CATCH at all — should not fire
        const string sql = "SELECT 1";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP007");

        Assert.Empty(diags);
    }

    [Fact]
    public void FiresTwiceForTwoEmptyCatchBlocks()
    {
        const string sql = """
            BEGIN TRY SELECT 1 END TRY BEGIN CATCH END CATCH;
            BEGIN TRY SELECT 2 END TRY BEGIN CATCH END CATCH
            """;

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP007");

        Assert.Equal(2, diags.Count);
    }
}
