using System.Text.RegularExpressions;
using Xunit;

namespace AkmlSql.Installer.Tests;

public sealed class WebInstallerSourceTests
{
    private static string Web => File.ReadAllText(HostingTestTools.Script("web-installer.iss"));
    private static string Setup => File.ReadAllText(HostingTestTools.Script("AkmlSqlSetup.iss"));

    [Fact]
    public void Hosting_is_code_owned_and_helpers_are_permanent()
    {
        Assert.DoesNotContain("Components: web\\iis", Web);
        Assert.DoesNotContain("Components: web\\service", Web);
        Assert.DoesNotContain("dism", Web, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IsIisInstalled", Web);
        Assert.DoesNotContain("{tmp}\\web-firewall.ps1", Web);
        Assert.DoesNotContain("{tmp}\\web-config-bridge.ps1", Web);
        foreach (var file in new[] { "web-config-bridge.ps1", "web-firewall.ps1", "web-hosting-check.ps1", "web-iis-setup.ps1", "web-https-binding.ps1", "web-iis-repair.ps1", "web-iis-remove.ps1", "web-hosting-repair.ps1" })
            Assert.Matches("Source: \"" + Regex.Escape(file) + "\"; DestDir: \"\\{app\\}\\\\Support\";[^\r\n]*Components: web", Web);
        Assert.True(Setup.IndexOf("#include \"web-hosting.iss\"", StringComparison.Ordinal) < Setup.IndexOf("#include \"web-installer.iss\"", StringComparison.Ordinal));
        Assert.Contains("Check: WebHostIsIis", Web);
        Assert.Contains("Check: IsWebSelected", Web);
        Assert.Contains("Standalone Windows service (recommended)", Web);
        Assert.Contains("Don't host (files only)", Web.Replace("''", "'"));
    }

    [Theory]
    [InlineData("WebHostActivate")]
    [InlineData("WebHostClick")]
    [InlineData("WebPortsActivate")]
    public void Page_handlers_cannot_clobber_silent_flags(string name)
        => Assert.Matches(@"procedure " + name + @"\([^)]*\);[\s\S]*?begin\s+if WizardSilent then Exit;", Web);

    [Fact]
    public void Setup_writes_resolved_settings_and_proves_which_host_answers()
    {
        foreach (var value in new[] { "Host", "Exposure", "BridgePort", "WebRoot" }) Assert.Contains("'" + value + "'", Web);
        Assert.Contains("ResolveSilentHost(", Web);
        Assert.DoesNotContain("WebSilentDontHost", Web);
        Assert.Contains("Result := Exposure = 'Lan'", Web);
        Assert.Contains("-ExpectEngine", Web);
        Assert.Contains("-ExpectBundle", Web);
        Assert.Contains("DelayedAutostart", Web);
        Assert.Contains("WebHostHasHost", Setup);
        Assert.Contains("HostingAddress(WebHost", Setup);
        Assert.Contains("AddressChangeNotice(", Web);
        Assert.Contains("AddressChangeNotice(", Setup);
        Assert.Contains("Type: filesandordirs; Name: \"{app}\\Web\\_framework\"; Components: web", Web);
        foreach (var part in new[] { "MemoUserInfoInfo", "MemoDirInfo", "MemoTypeInfo", "MemoComponentsInfo", "MemoGroupInfo", "MemoTasksInfo" })
            Assert.Contains("AppendReadyPart(Result, " + part + ")", Setup);
        Assert.Contains("/setowner *S-1-5-32-544", Web + Setup);
        Assert.True(Setup.IndexOf("Result := Web_PrepareFolders", StringComparison.Ordinal) < Setup.IndexOf("  CheckRunningIDEs;", Setup.IndexOf("function PrepareToInstall", StringComparison.Ordinal), StringComparison.Ordinal));
    }

    [Fact]
    public void Repair_and_uninstall_do_not_replay_legacy_temp_scripts_or_show_silent_prompts()
    {
        Assert.DoesNotContain("[UninstallRun]", Web);
        Assert.Contains("Check: WebHostHasHost", Web);
        Assert.Contains("Type: filesandordirs; Name: \"{app}\\Web\"", Web);
        Assert.Contains("if UninstallSilent then", Web);
        Assert.Contains("if UninstallSilent then", Setup);
        Assert.Contains("RetireUnsafeUninstallLog", Setup);
        Assert.Contains("FinishUnsafeUninstallLog", Setup);
        Assert.Contains("This computer does not host the web edition on IIS.", File.ReadAllText(HostingTestTools.Script("web-iis-repair.ps1")));
    }

    [Fact]
    public void IIS_SQL_and_LAN_summaries_match_the_host_contract()
    {
        var iis = File.ReadAllText(HostingTestTools.Script("web-iis-setup.ps1"));
        Assert.Matches("'\\.sql'\\s*=\\s*'text/plain'", iis);
        Assert.DoesNotContain("32-bit", iis, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("management scripting tools", Web, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("every computer, this one included", Web);
        Assert.Contains("The firewall rule applies to every network profile, including Public. Use a Private or Domain network.", Web);
    }
}
