using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Security;

public sealed class SE002Tests
{
    [Fact]
    public void HardcodedPassword_Fires()
    {
        const string sql = "DECLARE @password NVARCHAR(100) = 'secret123'";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "SE002");

        Assert.Single(diags);
        Assert.Equal("SE002", diags[0].RuleId);
    }

    [Fact]
    public void NonCredentialVariable_DoesNotFire()
    {
        const string sql = "DECLARE @username NVARCHAR(100) = 'admin'";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "SE002");

        Assert.Empty(diags);
    }

    [Fact]
    public void HardcodedToken_Fires()
    {
        const string sql = "DECLARE @apiToken NVARCHAR(200) = 'eyJhbGciOiJIUzI1NiJ9'";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "SE002");

        Assert.Single(diags);
    }

    [Fact]
    public void SetVariableWithCredentialName_Fires()
    {
        const string sql = "DECLARE @pwd NVARCHAR(50); SET @pwd = 'hunter2'";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "SE002");

        Assert.Single(diags);
    }

    [Fact]
    public void EmptyStringLiteral_DoesNotFire()
    {
        const string sql = "DECLARE @password NVARCHAR(100) = ''";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "SE002");

        Assert.Empty(diags);
    }
}
