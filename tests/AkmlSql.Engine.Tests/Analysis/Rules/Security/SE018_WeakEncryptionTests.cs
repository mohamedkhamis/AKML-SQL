using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis.Rules.Security;

public sealed class SE018_WeakEncryptionTests
{
    [Fact]
    public void Fires_OnEncryptByPassphrase()
    {
        const string sql = "SELECT ENCRYPTBYPASSPHRASE('secret', 'data');";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "SE018");

        Assert.Single(diags);
    }

    [Fact]
    public void Fires_OnDecryptByPassphrase()
    {
        const string sql = "SELECT DECRYPTBYPASSPHRASE('secret', @data);";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "SE018");

        Assert.Single(diags);
    }

    [Fact]
    public void DoesNotFire_OnOtherFunction()
    {
        const string sql = "SELECT HASHBYTES('SHA2_256', 'data');";

        var diags = AnalysisEngineTestHelper.Analyze(sql, "SE018");

        Assert.Empty(diags);
    }
}
