using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Style;

public sealed class ST001Tests
{
    [Fact]
    public void LowercaseKeywords_Fires()
    {
        const string sql = "select * from Orders";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "ST001");

        Assert.NotEmpty(diags);
        Assert.All(diags, d => Assert.Equal("ST001", d.RuleId));
    }

    [Fact]
    public void UppercaseKeywords_DoesNotFire()
    {
        const string sql = "SELECT * FROM Orders";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "ST001");

        Assert.Empty(diags);
    }

    [Fact]
    public void MixedCaseKeyword_Fires()
    {
        const string sql = "Select * From Orders";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "ST001");

        Assert.NotEmpty(diags);
    }

    [Fact]
    public void LowercaseFromKeyword_FiresOnce()
    {
        const string sql = "SELECT * from Orders";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "ST001");

        Assert.Single(diags);
        Assert.Contains("from", diags[0].Message);
    }
}
