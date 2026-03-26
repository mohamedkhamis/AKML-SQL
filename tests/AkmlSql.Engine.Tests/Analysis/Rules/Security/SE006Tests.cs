using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Security;

public sealed class SE006Tests
{
    [Fact]
    public void HashbytesWithMd5_Fires()
    {
        const string sql = "SELECT HASHBYTES('MD5', 'password')";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "SE006");

        Assert.Single(diags);
        Assert.Equal("SE006", diags[0].RuleId);
    }

    [Fact]
    public void HashbytesWithSha2_256_DoesNotFire()
    {
        const string sql = "SELECT HASHBYTES('SHA2_256', 'password')";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "SE006");

        Assert.Empty(diags);
    }

    [Fact]
    public void HashbytesWithSha1_Fires()
    {
        const string sql = "SELECT HASHBYTES('SHA1', 'mydata')";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "SE006");

        Assert.Single(diags);
    }

    [Fact]
    public void HashbytesWithSha_Fires()
    {
        const string sql = "SELECT HASHBYTES('SHA', 'mydata')";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "SE006");

        Assert.Single(diags);
    }

    [Fact]
    public void HashbytesWithSha2_512_DoesNotFire()
    {
        const string sql = "SELECT HASHBYTES('SHA2_512', 'password')";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "SE006");

        Assert.Empty(diags);
    }
}
