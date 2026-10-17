using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace AkmlSql.Installer.Tests;

/// <summary>
/// Re-review REV-03: the site's HTTPS certificate binding lives on an IP:port in HTTP.sys, shared by every site and program on
/// the machine. <c>web-https-binding.ps1</c> decides when AKML may bind or replace it and when it may remove it; these run its
/// pure functions in Windows PowerShell (no administrator rights, nothing on the machine is touched). The binding itself is
/// verified only by an install on a disposable IIS host, which these do not replace.
/// </summary>
public sealed class HttpsBindingDecisionTests
{
    private const string Ours = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string OurOld = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    private const string Foreign = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";

    private static string Script => Path.Combine(FindRepoRoot(), "src", "AkmlSql.Installer", "web-https-binding.ps1");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    /// <summary>Dot-sources the script and runs <paramref name="body"/>; returns what it writes, trimmed.</summary>
    private static string Run(string body)
    {
        var command = $". '{Script.Replace("'", "''")}'; {body}";
        var psi = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command }) psi.ArgumentList.Add(arg);
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        var error = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"PowerShell failed ({p.ExitCode}): {error}");
        return output.Trim();
    }

    private static string Decide(string existing, string? recordThumb = null, int recordPort = 443, string[]? otherSites = null, int port = 443)
    {
        var record = recordThumb == null ? "$null" : $"@{{ Port = {recordPort}; Thumbprint = '{recordThumb}' }}";
        var sites = otherSites == null || otherSites.Length == 0 ? "@()" : "@(" + string.Join(",", otherSites.Select(s => $"'{s}'")) + ")";
        return Run($"(Get-HttpsBindingDecision -ExistingHash '{existing}' -OurThumbprint '{Ours}' -Record {record} -OtherSites {sites} -Port {port}) | ConvertTo-Json -Compress");
    }

    private static string ActionOf(string json) => JsonDocument.Parse(json).RootElement.GetProperty("Action").GetString()!;

    [Fact]
    public void A_free_port_is_bound()
        => Assert.Equal("Bind", ActionOf(Decide(existing: "")));

    [Fact]
    public void A_port_carrying_our_certificate_is_rebound()
        => Assert.Equal("Rebind", ActionOf(Decide(existing: Ours)));

    [Fact]
    public void A_port_carrying_the_certificate_we_recorded_binding_earlier_is_rebound()
        => Assert.Equal("Rebind", ActionOf(Decide(existing: OurOld, recordThumb: OurOld)));

    [Fact]
    public void Another_applications_certificate_on_the_port_is_left_alone()
    {
        var json = Decide(existing: Foreign);
        Assert.Equal("Refuse", ActionOf(json));
        Assert.Contains("another secure site port", json);
    }

    [Fact]
    public void A_record_for_another_port_does_not_make_a_foreign_binding_ours()
        => Assert.Equal("Refuse", ActionOf(Decide(existing: Foreign, recordThumb: Foreign, recordPort: 8443)));

    [Fact]
    public void Another_IIS_site_with_an_https_binding_on_the_port_refuses_even_a_free_certificate_slot()
    {
        var json = Decide(existing: "", otherSites: new[] { "Default Web Site" });
        Assert.Equal("Refuse", ActionOf(json));
        Assert.Contains("Default Web Site", json);
    }

    [Theory]
    [InlineData(OurOld, OurOld, true)]     // still the binding AKML made
    [InlineData(OurOld, Foreign, false)]   // another application took the port since
    [InlineData(OurOld, "", false)]        // nothing bound any more
    public void Only_a_binding_that_is_still_ours_may_be_removed(string recorded, string current, bool removable)
    {
        var result = Run($"Test-OwnedBindingRemovable -Record @{{ Port = 443; Thumbprint = '{recorded}' }} -CurrentHash '{current}'");
        Assert.Equal(removable.ToString(), result, ignoreCase: true);
    }

    [Fact]
    public void Without_a_record_nothing_is_ever_removed()
        => Assert.Equal("False", Run($"Test-OwnedBindingRemovable -Record $null -CurrentHash '{Foreign}'"), ignoreCase: true);

    [Theory]
    [InlineData("    IP:port                      : 0.0.0.0:443\r\n    Certificate Hash             : c0ffee0123456789abcdef0123456789abcdef01\r\n    Application ID               : {4dc3e181-e14b-4a21-b022-59fc669b0914}", "C0FFEE0123456789ABCDEF0123456789ABCDEF01")]
    [InlineData("    IP:Port                      : 0.0.0.0:443\r\n    Zertifikathash               : c0ffee0123456789abcdef0123456789abcdef01", "C0FFEE0123456789ABCDEF0123456789ABCDEF01")]
    [InlineData("The system cannot find the file specified.", "")]
    [InlineData("", "")]
    public void The_bound_hash_is_read_from_netsh_whatever_the_language(string netsh, string expected)
    {
        var escaped = netsh.Replace("'", "''");
        Assert.Equal(expected, Run($"ConvertFrom-NetshSslCert ('{escaped}')"));
    }
}
