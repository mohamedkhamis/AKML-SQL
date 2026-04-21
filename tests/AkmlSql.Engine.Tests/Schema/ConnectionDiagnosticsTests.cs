using AkmlSql.Engine.Schema;
using Xunit;

namespace AkmlSql.Engine.Tests.Schema;

public class ConnectionDiagnosticsTests
{
    [Fact]
    public void Describe_NullString_ReturnsPlaceholder()
    {
        Assert.Equal("(null connection string)", ConnectionDiagnostics.Describe(null));
        Assert.Equal("(null connection string)", ConnectionDiagnostics.Describe(""));
        Assert.Equal("(null connection string)", ConnectionDiagnostics.Describe("   "));
    }

    [Fact]
    public void Describe_MalformedString_DoesNotThrow()
    {
        // SqlConnectionStringBuilder is permissive — it accepts unknown keywords
        // silently, so this doesn't throw. What matters is that the helper never
        // bubbles up an exception into our log pipeline and produces *something*
        // usable.
        var desc = ConnectionDiagnostics.Describe("=bogus=;;;");
        Assert.False(string.IsNullOrWhiteSpace(desc));
    }

    [Fact]
    public void Describe_UnparseableString_ReturnsInvalidPlaceholder()
    {
        // A string that actually causes SqlConnectionStringBuilder to throw
        // (mismatched quotes). We should return the placeholder, not raise.
        var desc = ConnectionDiagnostics.Describe("Data Source=srv;Password=\"unterminated");
        Assert.Equal("(invalid connection string)", desc);
    }

    [Fact]
    public void Describe_IntegratedSecurity_LabelsAuth()
    {
        var cs = "Data Source=server1;Initial Catalog=MyDb;Integrated Security=true;Connect Timeout=5";
        var desc = ConnectionDiagnostics.Describe(cs);
        Assert.Contains("server='server1'", desc);
        Assert.Contains("catalog='MyDb'", desc);
        Assert.Contains("auth='IntegratedSecurity'", desc);
        Assert.Contains("timeout=5s", desc);
        Assert.DoesNotContain("password", desc, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Describe_AzureAdIntegrated_LabelsAuth()
    {
        var cs = "Data Source=aad.database.windows.net;Initial Catalog=Prod;" +
                 "Authentication=Active Directory Integrated;Encrypt=true;Connect Timeout=5";
        var desc = ConnectionDiagnostics.Describe(cs);
        Assert.Contains("auth='ActiveDirectoryIntegrated'", desc);
        Assert.Contains("server='aad.database.windows.net'", desc);
        Assert.Contains("catalog='Prod'", desc);
    }

    [Fact]
    public void Describe_SqlAuth_ReportsUserButNoPassword()
    {
        var cs = "Data Source=srv;Initial Catalog=MyDb;User ID=myuser;Password=topsecret;Encrypt=false";
        var desc = ConnectionDiagnostics.Describe(cs);
        Assert.Contains("auth='SqlPassword (user='myuser')'", desc);
        Assert.DoesNotContain("topsecret", desc);
    }

    [Fact]
    public void Describe_EmptyServerAndCatalog_ShowsNone()
    {
        var cs = "Integrated Security=true";
        var desc = ConnectionDiagnostics.Describe(cs);
        Assert.Contains("server='(none)'", desc);
        Assert.Contains("catalog='(none)'", desc);
    }
}
