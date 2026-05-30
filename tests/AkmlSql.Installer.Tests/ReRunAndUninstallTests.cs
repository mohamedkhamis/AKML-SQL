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
    [SkippableFact]
    public void Install_summary_is_well_formed()
    {
        Skip.IfNot(InstallerSmokeEnv.CanRun, "Requires admin + IIS");
        Skip.IfNot(File.Exists(InstallerSmokeEnv.InstallSummaryPath),
            "No INSTALL-SUMMARY.txt present (run a web-edition install first).");

        var summary = InstallerSmokeEnv.ReadSummaryOrEmpty();
        Assert.False(string.IsNullOrWhiteSpace(summary));                                       // FR-030 (f)
        Assert.Contains("URL:", summary);
    }

    [SkippableFact]
    public void Plugin_state_preserved_across_install_uninstall_reinstall()
    {
        Skip.IfNot(InstallerSmokeEnv.CanRun, "Requires admin + IIS");
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
