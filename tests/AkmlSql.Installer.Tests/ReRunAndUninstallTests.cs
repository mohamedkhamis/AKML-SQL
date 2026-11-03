using System.IO;
using Xunit;

namespace AkmlSql.Installer.Tests;

/// <summary>
/// Spec 026 (M4 closure) US5 / FR-030 (f)–(g) / FR-031 / SC-007. Self-contained (NOT in the shared
/// "InstallerSmoke" collection — it drives its own install/uninstall/re-install cycle): asserts the
/// install summary is well-formed, and that the IDE-plugin config at %AppData%/AKML SQL/config.json
/// is byte-for-byte unchanged across an install -> uninstall -> re-install cycle.
/// </summary>
[Trait("Category", "InstallerSmoke")]
public sealed class ReRunAndUninstallTests
{
    [SkippableTheory]
    [InlineData("/VERYSILENT")]
    [InlineData("/VERYSILENT /SUPPRESSMSGBOXES")]
    public void Silent_uninstall_keeps_data_and_removes_all_owned_hosting_artifacts(string flags)
    {
        Skip.IfNot(InstallerSmokeEnv.CanRunService, InstallerSmokeEnv.SkipReason);
        using var fixture = new StandaloneSmokeFixture("LanStandalone");
        fixture.Install();
        var uninstaller = InstallerSmokeEnv.UninstallerExe;
        var dat = Path.ChangeExtension(uninstaller, ".dat");
        Assert.True(File.Exists(dat));
        var bytes = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(dat)).ToLowerInvariant();
        var prefix = System.Text.Encoding.Latin1.GetString(System.Text.Encoding.Unicode.GetBytes(@"\is-"));
        var suffix = System.Text.Encoding.Latin1.GetString(System.Text.Encoding.Unicode.GetBytes(@".tmp\"));
        var start = bytes.IndexOf(prefix, StringComparison.Ordinal);
        Assert.False(start >= 0 && bytes.IndexOf(suffix, start, StringComparison.Ordinal) >= 0, "The fresh uninstall log still records elevated temporary-folder script actions.");
        var result = InstallerSmokeEnv.RunProcess(uninstaller, flags, 120);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.True(Directory.Exists(InstallerSmokeEnv.CommonAppDataWeb));
        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(uninstaller)!, "Web")));
        Assert.NotEqual(0, InstallerSmokeEnv.RunProcess("sc.exe", "query AkmlSqlWebEngine").ExitCode);
        var leftovers = InstallerSmokeEnv.RunPowerShell("@(Get-NetFirewallRule -DisplayName 'AKML SQL Web*' -ErrorAction SilentlyContinue).Count; @(Get-ChildItem Cert:\\LocalMachine\\My | Where-Object Subject -eq 'CN=AKML SQL Web Engine').Count; @(Get-Item 'HKLM:\\Software\\AKML SQL\\Web','HKLM:\\Software\\WOW6432Node\\AKML SQL\\Web' -ErrorAction SilentlyContinue).Count");
        Assert.Equal(new[] { "0", "0", "0" }, leftovers.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
    }

    [SkippableFact]
    public void Install_summary_is_well_formed()
    {
        Skip.IfNot(InstallerSmokeEnv.CanRun, InstallerSmokeEnv.SkipReason);
        Skip.IfNot(File.Exists(InstallerSmokeEnv.InstallSummaryPath),
            "No INSTALL-SUMMARY.txt present (run a web-edition install first).");

        var summary = InstallerSmokeEnv.ReadSummaryOrEmpty();
        Assert.False(string.IsNullOrWhiteSpace(summary));                                       // FR-030 (f)
        Assert.Contains("URL:", summary);
    }

    [SkippableFact]
    public void Plugin_state_preserved_across_install_uninstall_reinstall()
    {
        Skip.IfNot(InstallerSmokeEnv.CanRun, InstallerSmokeEnv.SkipReason);
        var exe = InstallerSmokeEnv.InstallerExe;
        Skip.If(exe == null, "Output/AKMLSQLSetup.exe not found -- build the installer first (FR-032).");

        var iisPort = InstallerSmokeEnv.GetFreePort();
        var bridgePort = InstallerSmokeEnv.GetFreePort(iisPort);
        var args =
            "/VERYSILENT /SUPPRESSMSGBOXES /ACCEPTEULA " +
            $"/WEB_HOST=IIS /WEB_EXPOSURE=LOCALHOST /WEB_PORT={iisPort} /BRIDGE_PORT={bridgePort}";

        var beforeHash = InstallerSmokeEnv.HashFileOrNull(InstallerSmokeEnv.PluginConfig);

        // install
        Assert.Equal(0, InstallerSmokeEnv.RunProcess(exe!, args).ExitCode);
        var afterInstallHash = InstallerSmokeEnv.HashFileOrNull(InstallerSmokeEnv.PluginConfig);

        // uninstall
        if (File.Exists(InstallerSmokeEnv.UninstallerExe))
            InstallerSmokeEnv.RunProcess(InstallerSmokeEnv.UninstallerExe, "/VERYSILENT /SUPPRESSMSGBOXES");
        var afterUninstallHash = InstallerSmokeEnv.HashFileOrNull(InstallerSmokeEnv.PluginConfig);

        // FR-030 (g) / SC-007: the IDE-plugin config is byte-for-byte unchanged by the web cycle.
        Assert.Equal(beforeHash, afterInstallHash);
        Assert.Equal(beforeHash, afterUninstallHash);

        // FR-031: re-install must succeed, then clean up.
        Assert.Equal(0, InstallerSmokeEnv.RunProcess(exe!, args).ExitCode);
        if (File.Exists(InstallerSmokeEnv.UninstallerExe))
            InstallerSmokeEnv.RunProcess(InstallerSmokeEnv.UninstallerExe, "/VERYSILENT /SUPPRESSMSGBOXES");
    }
}
