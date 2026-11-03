using Xunit;

namespace AkmlSql.Installer.Tests;

public sealed class HostingResolutionTests
{
    [Fact]
    public void Legacy_action_detection_finds_UTF16_paths_at_either_byte_alignment()
    {
        var path = string.Join(" + ", @"\is-123.tmp\web-firewall.ps1".Select(c => $"#{(int)c} + #0"));
        Assert.Equal(new[] { "True", "True", "False", "False" }, WebHostingProbeTests.Probe(
            $"B(HasLegacyTempScriptActions({path}))", $"B(HasLegacyTempScriptActions('x' + {path}))",
            "B(HasLegacyTempScriptActions('C:\\Program Files\\AKML SQL\\Support\\web-firewall.ps1'))", "B(HasLegacyTempScriptActions(''))"));
    }

    [Fact]
    public void Address_changes_name_both_origins_and_preserve_engine_history()
    {
        foreach (var pair in new[] { ("http://localhost:8083/", "http://localhost:47291/"), ("http://localhost:47291/", "https://PC:47291/"), ("http://localhost:47291/", "http://localhost:59417/") })
            Assert.Equal($"The web edition will move from {pair.Item1} to {pair.Item2}. Connections, tabs and other data saved in the browser at {pair.Item1} will not appear at {pair.Item2}. Query history is kept by the engine. To keep your settings, use Settings › Export… first.",
                Assert.Single(WebHostingProbeTests.Probe($"AddressChangeNotice('{pair.Item1}', '{pair.Item2}')")));
        Assert.Equal(new[] { "", "", "" }, WebHostingProbeTests.Probe("AddressChangeNotice('', 'http://localhost:47291/')", "AddressChangeNotice('http://localhost:47291/', '')", "AddressChangeNotice('http://localhost:47291/', 'http://localhost:47291/')"));
    }

    [Theory]
    [InlineData("OTHER", "", "", true, false, "Stop:Unknown /WEB_HOST value: OTHER")]
    [InlineData("NONE", "LAN", "", true, false, "Stop:/WEB_HOST=NONE with /WEB_EXPOSURE=LAN is invalid")]
    [InlineData("IIS", "", "", false, false, "Stop:not ready")]
    [InlineData("IIS", "", "", true, true, "IIS")]
    [InlineData("SERVICE", "", "", true, true, "Stop:/WEB_PORT and /WEB_HTTPS_PORT apply to IIS hosting only")]
    [InlineData("NONE", "", "", true, true, "Stop:/WEB_PORT and /WEB_HTTPS_PORT apply to IIS hosting only")]
    [InlineData("SERVICE", "", "IIS", false, false, "Service")]
    [InlineData("NONE", "", "IIS", true, false, "None")]
    [InlineData("", "", "IIS", true, true, "IIS")]
    [InlineData("", "", "IIS", false, true, "Service")]
    [InlineData("", "", "None", false, true, "Stop:/WEB_PORT and /WEB_HTTPS_PORT apply to IIS hosting only")]
    [InlineData("", "", "", false, false, "Service")]
    [InlineData("", "", "None", false, false, "None")]
    [InlineData("", "", "Service", false, false, "Service")]
    public void Silent_resolution_checks_the_contract_rows_in_order(string flag, string exposure, string previous, bool ready, bool ports, string expected)
    {
        var values = WebHostingProbeTests.Probe($"ResolveSilentHost('{flag}', '{exposure}', '{previous}', {ready}, {ports}, 'not ready', 'http://localhost:47291/', Reason)", "Reason");
        Assert.Equal(expected, values[0]);
        if (flag == "" && previous == "IIS" && !ready) Assert.Contains("not ready", values[1]);
    }

    [Fact]
    public void Only_an_explicit_or_retained_IIS_candidate_needs_the_management_probe()
        => Assert.Equal(new[] { "True", "True", "False", "False", "False" }, WebHostingProbeTests.Probe(
            "B(SilentNeedsIisProbe('iis', ''))", "B(SilentNeedsIisProbe('', 'IIS'))", "B(SilentNeedsIisProbe('SERVICE', 'IIS'))", "B(SilentNeedsIisProbe('NONE', 'IIS'))", "B(SilentNeedsIisProbe('', 'Service'))"));
}
