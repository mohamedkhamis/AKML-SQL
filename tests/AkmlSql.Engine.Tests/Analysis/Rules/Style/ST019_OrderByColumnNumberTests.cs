using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Style;

public sealed class St019OrderByColumnNumberTests
{
    [Fact]
    public void Fires_OnOrderByColumnNumber()
    {
        const string sql = "SELECT Id, Name FROM dbo.T ORDER BY 1;";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "ST019");

        Assert.Single(diags);
    }

    [Fact]
    public void DoesNotFire_OnOrderByColumnName()
    {
        const string sql = "SELECT Id, Name FROM dbo.T ORDER BY Name;";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "ST019");

        Assert.Empty(diags);
    }
}
