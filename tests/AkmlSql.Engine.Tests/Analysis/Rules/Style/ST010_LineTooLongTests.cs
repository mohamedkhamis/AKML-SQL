using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Style;

public sealed class ST010_LineTooLongTests
{
    [Fact]
    public void Fires_OnLineLongerThan120Chars()
    {
        // Line is 123 characters, exceeding the 120-character limit
        const string sql = "SELECT aaaaaaaaaaaaaaaaaaaaaaaaa, bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb, ccccccccccccccccccccccccccccccccccccc FROM dbo.Table1;";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "ST010");

        Assert.True(diags.Count >= 1);
    }

    [Fact]
    public void DoesNotFire_OnShortLines()
    {
        const string sql = "SELECT Id FROM dbo.T;";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "ST010");

        Assert.Empty(diags);
    }
}
