using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.BestPractices;

public sealed class BP027_UpdateWithJoinTests
{
    [Fact]
    public void Fires_OnUpdateWithFromAndJoin()
    {
        const string sql = """
            UPDATE t SET t.Name = s.Name
            FROM dbo.Target AS t
            INNER JOIN dbo.Source AS s ON s.Id = t.Id;
            """;

        var diags = AnalysisEngineTestHelper.Analyze(sql, "BP027");
        Assert.Single(diags);
    }

    [Fact]
    public void DoesNotFire_OnSimpleUpdate()
    {
        var diags = AnalysisEngineTestHelper.Analyze(
            "UPDATE dbo.T SET Name = 'x' WHERE Id = 1;",
            "BP027");
        Assert.Empty(diags);
    }
}
