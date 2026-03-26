using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.BestPractices;

public sealed class BP018_DeepNestedIfTests
{
    [Fact]
    public void Fires_AtDepthFour()
    {
        const string sql = """
            IF 1=1 BEGIN
              IF 1=1 BEGIN
                IF 1=1 BEGIN
                  IF 1=1 PRINT 'deep';
                END
              END
            END
            """;

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP018");
        Assert.Single(diags);
    }

    [Fact]
    public void DoesNotFire_AtDepthThree()
    {
        const string sql = """
            IF 1=1 BEGIN
              IF 1=1 BEGIN
                IF 1=1 PRINT 'ok';
              END
            END
            """;

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP018");
        Assert.Empty(diags);
    }
}
