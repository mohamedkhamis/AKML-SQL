namespace AkmlSql.Installer.Tests;

/// <summary>Real setup, only after explicit disposable-VM opt-in. Never constructed by local unit tests.</summary>
public sealed class StandaloneSmokeFixture(string scenario) : IDisposable
{
    public int BridgePort { get; } = InstallerSmokeEnv.GetFreePort();
    public int IisPort { get; } = InstallerSmokeEnv.GetFreePort();
    public bool Lan => scenario == "LanStandalone";
    public bool Eligible => scenario == "IisToStandaloneSwitch" ? InstallerSmokeEnv.CanRunIis : InstallerSmokeEnv.CanRunService;
    private bool _attempted;

    public void Install()
    {
        if (!Eligible) throw new InvalidOperationException("Real installer smoke requires opt-in and an eligible disposable VM.");
        var exe = InstallerSmokeEnv.InstallerExe ?? throw new FileNotFoundException("Build Output/AKMLSQLSetup.exe first.");
        _attempted = true;
        var common = $"/VERYSILENT /SUPPRESSMSGBOXES /ACCEPTEULA /COMPONENTS=web /BRIDGE_PORT={BridgePort}";
        if (scenario == "IisToStandaloneSwitch")
        {
            var first = InstallerSmokeEnv.RunProcess(exe, common + $" /WEB_HOST=IIS /WEB_PORT={IisPort}");
            Xunit.Assert.True(first.ExitCode == 0, first.Output);
        }
        var install = InstallerSmokeEnv.RunProcess(exe, common + " /WEB_HOST=SERVICE /WEB_EXPOSURE=" + (Lan ? "LAN" : "LOCALHOST"));
        Xunit.Assert.True(install.ExitCode == 0, install.Output);
        Xunit.Assert.True(InstallerSmokeEnv.WaitForServiceRunning("AkmlSqlWebEngine"));
    }

    public void Dispose()
    {
        if (!_attempted) return;
        if (File.Exists(InstallerSmokeEnv.UninstallerExe)) InstallerSmokeEnv.RunProcess(InstallerSmokeEnv.UninstallerExe, "/VERYSILENT /SUPPRESSMSGBOXES", 120);
        InstallerSmokeEnv.BestEffortCleanup(BridgePort);
    }
}
