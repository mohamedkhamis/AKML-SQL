using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Security;

public sealed class Se010Tests
{
    [Fact]
    public void ConnectionStringWithoutEncrypt_Fires()
    {
        const string sql = "DECLARE @cs VARCHAR(200) = 'Server=myserver;Database=mydb;User Id=sa;Password=pass;'";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "SE010");

        Assert.Single(diags);
        Assert.Equal("SE010", diags[0].RuleId);
    }

    [Fact]
    public void ConnectionStringWithEncryptTrue_DoesNotFire()
    {
        const string sql = "DECLARE @cs VARCHAR(200) = 'Server=myserver;Database=mydb;Encrypt=True;'";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "SE010");

        Assert.Empty(diags);
    }

    [Fact]
    public void PlainStringLiteral_DoesNotFire()
    {
        const string sql = "DECLARE @msg NVARCHAR(100) = 'Hello World'";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "SE010");

        Assert.Empty(diags);
    }

    [Fact]
    public void ConnectionStringWithEncryptYes_DoesNotFire()
    {
        const string sql = "DECLARE @cs VARCHAR(200) = 'Data Source=myserver;Encrypt=yes;'";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "SE010");

        Assert.Empty(diags);
    }

    [Fact]
    public void DataSourceWithoutEncrypt_Fires()
    {
        const string sql = "DECLARE @cs VARCHAR(200) = 'Data Source=myserver;Initial Catalog=mydb;'";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "SE010");

        Assert.Single(diags);
    }
}
