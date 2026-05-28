using Xunit;

namespace AkmlSql.Installer.Tests;

/// <summary>
/// Spec 026 (M4 closure) US5 / FR-030 (a)–(c). Asserts the IIS site the install provisioned:
/// the site is bound on the chosen IIS port, the five MIME types are registered, and the CSP
/// header is served. Opt-in (<c>[Trait("Category","InstallerSmoke")]</c>) + host-gated
/// (<c>Skip.IfNot(admin &amp;&amp; IIS)</c>).
/// </summary>
[Collection("InstallerSmoke")]
[Trait("Category", "InstallerSmoke")]
public sealed class IisProvisioningTests
{
    private readonly InstallerSmokeFixture _fx;

    public IisProvisioningTests(InstallerSmokeFixture fx) => _fx = fx;

    [SkippableFact]
    public void AkmlSqlWeb_site_is_bound_on_the_iis_port()
    {
        Skip.IfNot(InstallerSmokeEnv.CanRun, "Requires admin + IIS");
        Assert.Null(_fx.MissingInstallerReason);                       // FR-032
        Assert.True(_fx.Installed, $"Silent install failed (exit {_fx.InstallExitCode}).");

        var (exit, output) = InstallerSmokeEnv.RunPowerShell(
            "Import-Module WebAdministration; " +
            "(Get-Website -Name AkmlSqlWeb).bindings.Collection.bindingInformation");

        Assert.Equal(0, exit);
        Assert.True(output.Contains(":" + _fx.IisPort + ":"),
            $"Expected AkmlSqlWeb bound on port {_fx.IisPort}; got binding info: {output}");   // FR-030 (a)
    }

    [SkippableFact]
    public void Five_mime_types_are_registered()
    {
        Skip.IfNot(InstallerSmokeEnv.CanRun, "Requires admin + IIS");
        Assert.True(_fx.Installed, $"Silent install failed (exit {_fx.InstallExitCode}).");

        var (_, output) = InstallerSmokeEnv.RunPowerShell(
            "Import-Module WebAdministration; " +
            "(Get-WebConfiguration -Filter 'system.webServer/staticContent/mimeMap' " +
            "-PSPath 'MACHINE/WEBROOT/APPHOST/AkmlSqlWeb').fileExtension");

        foreach (var ext in new[] { ".wasm", ".dat", ".blat", ".br", ".dll" })
            Assert.True(output.Contains(ext),
                $"Expected {ext} MIME type registered on AkmlSqlWeb; got: {output}");          // FR-030 (b)
    }

    [SkippableFact]
    public void Csp_header_is_served()
    {
        Skip.IfNot(InstallerSmokeEnv.CanRun, "Requires admin + IIS");
        Assert.True(_fx.Installed, $"Silent install failed (exit {_fx.InstallExitCode}).");

        var (_, output) = InstallerSmokeEnv.RunPowerShell(
            $"(Invoke-WebRequest -UseBasicParsing -Method Head -Uri http://localhost:{_fx.IisPort}/)" +
            ".Headers['Content-Security-Policy']");

        Assert.False(string.IsNullOrWhiteSpace(output),
            "Expected a Content-Security-Policy header on the IIS response; got none.");        // FR-030 (c)
    }
}
