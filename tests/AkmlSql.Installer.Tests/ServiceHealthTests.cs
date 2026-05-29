using Xunit;

namespace AkmlSql.Installer.Tests;

/// <summary>
/// Spec 026 (M4 closure) M5 / FR-007a: the install is only healthy if the AkmlSqlWebEngine service
/// actually reaches Running -- the installer returns exit 0 even when it does not (a non-running
/// service is only a warning). This closes the smoke-suite gap where installer exit 0 was treated as
/// success with no runtime verification, and exercises the C2 (--web/--config launch) + ordering fix
/// end-to-end. Opt-in + host-gated like the other smoke tests.
/// </summary>
[Collection("InstallerSmoke")]
[Trait("Category", "InstallerSmoke")]
public sealed class ServiceHealthTests
{
    private readonly InstallerSmokeFixture _fx;

    public ServiceHealthTests(InstallerSmokeFixture fx) => _fx = fx;

    [SkippableFact]
    public void AkmlSqlWebEngine_service_reaches_running_after_install()
    {
        Skip.IfNot(InstallerSmokeEnv.CanRun, "Requires admin + IIS");
        Assert.Null(_fx.MissingInstallerReason);                          // FR-032
        Assert.True(_fx.Installed, $"Silent install failed (exit {_fx.InstallExitCode}).");
        Assert.True(_fx.ServiceRunning,
            "AkmlSqlWebEngine did not reach Running after install -- the engine service could not " +
            "start (check the --web/--config launch and that config.json is written before sc start).");
    }
}
