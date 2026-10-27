using System.Text.Json;
using Xunit;

namespace AkmlSql.Installer.Tests;

public sealed class FirewallPlanTests
{
    [Theory]
    [InlineData("Service", "Lan", "Add", "Remove")]
    [InlineData("IIS", "Lan", "Add", "Add")]
    [InlineData("None", "Lan", "Add", "Remove")]
    [InlineData("Service", "Localhost", "Remove", "Remove")]
    [InlineData("IIS", "Localhost", "Remove", "Remove")]
    [InlineData("None", "Localhost", "Remove", "Remove")]
    public void Plan_matches_host_and_exposure(string host, string exposure, string engine, string site)
    {
        var json = HostingTestTools.DotSource("web-firewall.ps1",
            $"@(Get-FirewallPlan -WebHost {host} -Exposure {exposure} -EnginePort 59417 -HttpsPort 59418) | ConvertTo-Json -Compress");
        using var document = JsonDocument.Parse(json);
        var rules = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, rules.Length);
        Assert.Equal("AKML SQL Web Engine", rules[0].GetProperty("Name").GetString());
        Assert.Equal("AKML SQL Web Site (HTTPS)", rules[1].GetProperty("Name").GetString());
        Assert.Equal(engine, rules[0].GetProperty("Action").GetString());
        Assert.Equal(site, rules[1].GetProperty("Action").GetString());
        Assert.Equal(59417, rules[0].GetProperty("Port").GetInt32());
        Assert.Equal(59418, rules[1].GetProperty("Port").GetInt32());
    }

    [Fact]
    public void Dot_sourcing_never_runs_netsh()
    {
        var result = HostingTestTools.DotSource("web-firewall.ps1", "'sourced'");
        Assert.Equal("sourced", result);
    }
}
