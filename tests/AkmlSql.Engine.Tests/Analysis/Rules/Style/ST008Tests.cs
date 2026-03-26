using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Style;

public sealed class ST008Tests
{
    [Fact]
    public void MixedTabAndSpaceIndentation_Fires()
    {
        // Line 2 starts with a tab followed by spaces — mixed indentation
        var sql = "SELECT 1\n\t  SELECT 2";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "ST008");

        Assert.Single(diags);
        Assert.Equal("ST008", diags[0].RuleId);
    }

    [Fact]
    public void NoLeadingWhitespace_DoesNotFire()
    {
        const string sql = "SELECT 1\nSELECT 2";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "ST008");

        Assert.Empty(diags);
    }

    [Fact]
    public void SpacesOnlyIndentation_DoesNotFire()
    {
        const string sql = "SELECT 1\n    SELECT 2";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "ST008");

        Assert.Empty(diags);
    }

    [Fact]
    public void TabsOnlyIndentation_DoesNotFire()
    {
        const string sql = "SELECT 1\n\t\tSELECT 2";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "ST008");

        Assert.Empty(diags);
    }

    [Fact]
    public void MultipleMixedLines_FiresForEach()
    {
        var sql = "SELECT 1\n\t  SELECT 2\n  \tSELECT 3";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "ST008");

        Assert.Equal(2, diags.Count);
    }
}
