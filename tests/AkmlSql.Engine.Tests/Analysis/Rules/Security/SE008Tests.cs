using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Security;

public sealed class SE008Tests
{
    [Fact]
    public void XpCmdshell_Fires()
    {
        const string sql = "EXEC xp_cmdshell 'dir C:\\'";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "SE008");

        Assert.Single(diags);
        Assert.Equal("SE008", diags[0].RuleId);
    }

    [Fact]
    public void OtherStoredProcedure_DoesNotFire()
    {
        const string sql = "EXEC sp_help 'Orders'";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "SE008");

        Assert.Empty(diags);
    }

    [Fact]
    public void XpCmdshellMixedCase_Fires()
    {
        const string sql = "EXEC XP_CMDSHELL 'whoami'";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "SE008");

        Assert.Single(diags);
    }
}
