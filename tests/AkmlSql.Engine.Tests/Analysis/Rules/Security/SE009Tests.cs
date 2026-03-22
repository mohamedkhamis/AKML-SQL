using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Security;

public sealed class SE009Tests
{
    [Fact]
    public void Openrowset_Fires()
    {
        const string sql = "SELECT * FROM OPENROWSET('SQLNCLI', 'Server=remote;Trusted_Connection=yes;', 'SELECT 1')";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "SE009");

        Assert.Single(diags);
        Assert.Equal("SE009", diags[0].RuleId);
    }

    [Fact]
    public void RegularTableReference_DoesNotFire()
    {
        const string sql = "SELECT * FROM dbo.Orders";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "SE009");

        Assert.Empty(diags);
    }
}
