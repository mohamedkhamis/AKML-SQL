using System;
using System.Text.RegularExpressions;
using Xunit;

namespace AkmlSql.Installer.Tests;

/// <summary>
/// Spec 026 (M4 closure) US5 / FR-030 (d)–(e). Asserts the LAN-mode TLS + firewall state the
/// install provisioned: the netsh sslcert binding on the bridge port matches the thumbprint in
/// INSTALL-SUMMARY.txt, and the "AKML SQL Web Engine" firewall rule exists.
/// </summary>
[Collection("InstallerSmoke")]
[Trait("Category", "InstallerSmoke")]
public sealed class LanTlsTests
{
    private readonly InstallerSmokeFixture _fx;

    public LanTlsTests(InstallerSmokeFixture fx) => _fx = fx;

    [SkippableFact]
    public void Sslcert_binding_thumbprint_matches_install_summary()
    {
        Skip.IfNot(InstallerSmokeEnv.CanRun, "Requires admin + IIS");
        Assert.True(_fx.Installed, $"Silent install failed (exit {_fx.InstallExitCode}).");

        var (_, netsh) = InstallerSmokeEnv.RunProcess(
            "netsh.exe", $"http show sslcert ipport=0.0.0.0:{_fx.BridgePort}");
        var netshThumb = FirstHex40(netsh);
        Assert.False(string.IsNullOrEmpty(netshThumb),
            $"No netsh sslcert binding found on 0.0.0.0:{_fx.BridgePort}.");                    // FR-030 (d)

        var summaryNoSpaces = _fx.SummaryText.Replace(" ", string.Empty);
        Assert.True(summaryNoSpaces.IndexOf(netshThumb!, StringComparison.OrdinalIgnoreCase) >= 0,
            "Bridge-port sslcert thumbprint is not present in INSTALL-SUMMARY.txt.");
    }

    [SkippableFact]
    public void Firewall_rule_exists()
    {
        Skip.IfNot(InstallerSmokeEnv.CanRun, "Requires admin + IIS");
        Assert.True(_fx.Installed, $"Silent install failed (exit {_fx.InstallExitCode}).");

        var (_, output) = InstallerSmokeEnv.RunPowerShell(
            "(Get-NetFirewallRule -DisplayName 'AKML SQL Web Engine' -ErrorAction SilentlyContinue)" +
            " | Select-Object -First 1 -ExpandProperty DisplayName");

        Assert.True(output.Contains("AKML SQL Web Engine"),
            "Expected a firewall rule named 'AKML SQL Web Engine'; got none.");                 // FR-030 (e)
    }

    private static string? FirstHex40(string s)
    {
        var m = Regex.Match(s, "[0-9A-Fa-f]{40}");
        return m.Success ? m.Value : null;
    }
}
