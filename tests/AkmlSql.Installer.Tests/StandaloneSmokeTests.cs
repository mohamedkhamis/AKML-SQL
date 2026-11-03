using Xunit;

namespace AkmlSql.Installer.Tests;

[Trait("Category", "InstallerSmoke")]
public sealed class StandaloneSmokeTests
{
    [SkippableTheory]
    [InlineData("Standalone")]
    [InlineData("LanStandalone")]
    [InlineData("IisToStandaloneSwitch")]
    public async Task The_standalone_host_answers_with_automatic_start_and_only_its_firewall_rule(string scenario)
    {
        Skip.IfNot(InstallerSmokeEnv.CanRunService, InstallerSmokeEnv.SkipReason);
        if (scenario == "IisToStandaloneSwitch") Skip.IfNot(InstallerSmokeEnv.CanRunIis, InstallerSmokeEnv.SkipReason);
        using var fixture = new StandaloneSmokeFixture(scenario);
        fixture.Install();
        using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        var url = $"{(fixture.Lan ? "https" : "http")}://localhost:{fixture.BridgePort}/";
        using var response = await http.GetAsync(url);
        Assert.Equal("engine", Assert.Single(response.Headers.GetValues("X-AKML-Host")));
        Assert.Contains("_framework/blazor.webassembly.js", await response.Content.ReadAsStringAsync());
        var settings = InstallerSmokeEnv.RunPowerShell("$s=Get-ItemProperty HKLM:\\SYSTEM\\CurrentControlSet\\Services\\AkmlSqlWebEngine; Write-Output ($s.Start.ToString()+'|'+$s.DelayedAutostart)");
        Assert.Equal("2|0", settings.Output.Trim());
        var query = InstallerSmokeEnv.RunProcess("sc.exe", "qc AkmlSqlWebEngine");
        Assert.Equal(0, query.ExitCode);
        Assert.DoesNotContain("(DELAYED)", query.Output);
        var acl = HostingTestTools.DotSource("web-hosting-repair.ps1", "$a=Get-Acl -LiteralPath '" + InstallerSmokeEnv.CommonAppDataWeb.Replace("'", "''") + "'; $bad=@($a.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier]) | Where-Object { Test-UnsafeHostingWriteAce $_.IdentityReference.Value $_.FileSystemRights $_.AccessControlType $_.PropagationFlags }); $a.GetOwner([Security.Principal.SecurityIdentifier]).Value+'|'+$bad.Count");
        Assert.Equal("S-1-5-32-544|0", acl);
        var rules = InstallerSmokeEnv.RunPowerShell("Get-NetFirewallRule -DisplayName 'AKML SQL Web*' -ErrorAction SilentlyContinue | ForEach-Object { $_.DisplayName+'|'+(($_ | Get-NetFirewallPortFilter).LocalPort) }");
        Assert.DoesNotContain("AKML SQL Web Site (HTTPS)", rules.Output);
        if (fixture.Lan) Assert.Contains("AKML SQL Web Engine|" + fixture.BridgePort, rules.Output);
        else Assert.DoesNotContain("AKML SQL Web Engine|", rules.Output);
    }
}
