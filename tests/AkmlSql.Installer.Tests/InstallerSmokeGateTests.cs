using Xunit;

namespace AkmlSql.Installer.Tests;

public sealed class InstallerSmokeGateTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("true")]
    public void Smoke_tests_require_explicit_opt_in(string? value)
    {
        var previous = Environment.GetEnvironmentVariable("AKML_INSTALLER_SMOKE");
        try
        {
            Environment.SetEnvironmentVariable("AKML_INSTALLER_SMOKE", value);
            Assert.False(InstallerSmokeEnv.CanRun);
            Assert.False(InstallerSmokeEnv.CanRunService);
            Assert.False(InstallerSmokeEnv.CanRunIis);
            Assert.Equal(
                "installer smoke tests run the real installer; set AKML_INSTALLER_SMOKE=1 on a disposable VM",
                InstallerSmokeEnv.SkipReason);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AKML_INSTALLER_SMOKE", previous);
        }
    }

    [Fact]
    public void Opt_in_still_requires_an_eligible_host()
    {
        var previous = Environment.GetEnvironmentVariable("AKML_INSTALLER_SMOKE");
        try
        {
            Environment.SetEnvironmentVariable("AKML_INSTALLER_SMOKE", "1");
            Assert.True(InstallerSmokeEnv.IsOptedIn);
            Assert.Equal(InstallerSmokeEnv.IsAdministrator() && InstallerSmokeEnv.IsIisReady(),
                InstallerSmokeEnv.CanRun);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AKML_INSTALLER_SMOKE", previous);
        }
    }
}
