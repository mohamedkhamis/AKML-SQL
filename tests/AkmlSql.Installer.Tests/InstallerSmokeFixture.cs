using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

// Installer smoke tests mutate shared machine state (IIS sites, the Windows service, firewall
// rules). They MUST NOT run in parallel with each other.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace AkmlSql.Installer.Tests;

/// <summary>
/// Spec 026 (M4 closure) US5 / FR-029 / FR-032. Shared fixture: on an admin + IIS host it runs one
/// silent LAN install (capturing the IIS/bridge ports, the install summary, and the pre-install
/// plugin-config hash) and silent-uninstalls on dispose (capturing the post-uninstall hash). On a
/// host that cannot run (non-admin / no IIS), <see cref="InitializeAsync"/> is a no-op and every
/// test Skip.IfNot's out. If the prebuilt installer is missing, <see cref="MissingInstallerReason"/>
/// is set so tests fail with a clear "build the installer first" message (FR-032).
/// </summary>
public sealed class InstallerSmokeFixture : IAsyncLifetime
{
    public int IisPort { get; private set; }
    public int BridgePort { get; private set; }
    public bool Installed { get; private set; }

    /// <summary>
    /// Spec 026 (M4 closure) M5: true only when the AkmlSqlWebEngine service actually reached
    /// Running after the install. The installer returns exit 0 even when the service fails to start
    /// (it only writes a warning), so <see cref="Installed"/> alone is insufficient — provisioning
    /// tests assert this to avoid passing on a half-provisioned machine.
    /// </summary>
    public bool ServiceRunning { get; private set; }

    public int InstallExitCode { get; private set; } = -1;
    public string SummaryText { get; private set; } = string.Empty;
    public string? PreInstallPluginHash { get; private set; }
    public string? PostUninstallPluginHash { get; private set; }
    public string? MissingInstallerReason { get; private set; }

    public Task InitializeAsync()
    {
        if (!InstallerSmokeEnv.CanRun) return Task.CompletedTask;   // tests Skip.IfNot; no install attempted

        var exe = InstallerSmokeEnv.InstallerExe;
        if (exe == null)
        {
            MissingInstallerReason =
                "Build the installer first via ISCC.exe AkmlSqlSetup.iss (Output/AKMLSQLSetup.exe not found).";
            return Task.CompletedTask;
        }

        PreInstallPluginHash = InstallerSmokeEnv.HashFileOrNull(InstallerSmokeEnv.PluginConfig);
        IisPort = InstallerSmokeEnv.GetFreePort();
        BridgePort = InstallerSmokeEnv.GetFreePort(IisPort);

        var log = Path.Combine(Path.GetTempPath(), $"akml-install-{Guid.NewGuid():N}.log");
        var args =
            "/VERYSILENT /SUPPRESSMSGBOXES /ACCEPTEULA " +
            $"/WEB_HOST=IIS /WEB_EXPOSURE=LAN /WEB_PORT={IisPort} /BRIDGE_PORT={BridgePort} " +
            $"/LOG=\"{log}\"";
        var (exit, _) = InstallerSmokeEnv.RunProcess(exe, args);
        InstallExitCode = exit;
        Installed = exit == 0;
        // M5: exit 0 != healthy. Verify the engine service actually reached Running.
        if (Installed)
            ServiceRunning = InstallerSmokeEnv.WaitForServiceRunning("AkmlSqlWebEngine");
        SummaryText = InstallerSmokeEnv.ReadSummaryOrEmpty();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Installed && File.Exists(InstallerSmokeEnv.UninstallerExe))
            InstallerSmokeEnv.RunProcess(InstallerSmokeEnv.UninstallerExe, "/VERYSILENT /SUPPRESSMSGBOXES");
        // M5: even if the uninstaller was absent or the install half-failed, never leak the service /
        // IIS site to the next run.
        if (InstallerSmokeEnv.CanRun)
            InstallerSmokeEnv.BestEffortCleanup();
        PostUninstallPluginHash = InstallerSmokeEnv.HashFileOrNull(InstallerSmokeEnv.PluginConfig);
        return Task.CompletedTask;
    }
}

/// <summary>Shares one install across the read-only assertion classes (IIS / LAN-TLS).</summary>
[CollectionDefinition("InstallerSmoke")]
public sealed class InstallerSmokeCollection : ICollectionFixture<InstallerSmokeFixture> { }
