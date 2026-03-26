using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Deprecated;

public sealed class DEP002Tests
{
    [Fact]
    public void SpAddlogin_Fires()
    {
        const string sql = "EXEC sp_addlogin 'testuser', 'password'";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "DEP002");

        Assert.Single(diags);
        Assert.Equal("DEP002", diags[0].RuleId);
    }

    [Fact]
    public void NonDeprecatedProcedure_DoesNotFire()
    {
        const string sql = "EXEC sp_help 'Orders'";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "DEP002");

        Assert.Empty(diags);
    }

    [Fact]
    public void SpDroplogin_Fires()
    {
        const string sql = "EXEC sp_droplogin 'testuser'";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "DEP002");

        Assert.Single(diags);
    }

    [Fact]
    public void SpAdduser_Fires()
    {
        const string sql = "EXEC sp_adduser 'testuser'";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "DEP002");

        Assert.Single(diags);
    }

    [Fact]
    public void SpPassword_Fires()
    {
        const string sql = "EXEC sp_password NULL, 'newpassword', 'testuser'";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "DEP002");

        Assert.Single(diags);
    }
}
