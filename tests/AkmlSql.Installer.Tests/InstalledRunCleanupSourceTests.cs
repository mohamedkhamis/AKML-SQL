using System.Text.RegularExpressions;
using Xunit;

namespace AkmlSql.Installer.Tests;

/// <summary>
/// Spec 047 installed-product run of 2026-11-22 (baseline.md › Installed-product run):
/// <list type="bullet">
/// <item>F8: SSMS ran a 2026-10-10 engine left by hand in the extension's own <c>Engine</c> folder, which the shell starts
/// before the installed <c>{app}\Engine</c>; setup never touched that folder. Setup now removes it.</item>
/// <item>F5: setup stopped installing <c>*.br</c>/<c>*.gz</c> but never removed the ones older versions left in
/// <c>{app}\Web</c>. Setup now deletes them from every folder that held them.</item>
/// </list>
/// </summary>
public sealed class InstalledRunCleanupSourceTests
{
    private static string Web => File.ReadAllText(HostingTestTools.Script("web-installer.iss"));
    private static string Setup => File.ReadAllText(HostingTestTools.Script("AkmlSqlSetup.iss"));

    private static string Section(string script, string name)
    {
        var match = Regex.Match(script, @"^\[" + Regex.Escape(name) + @"\]\r?\n(?<body>[\s\S]*?)(?=^\[|\z)", RegexOptions.Multiline);
        Assert.True(match.Success, $"no [{name}] section");
        return match.Groups["body"].Value;
    }

    [Fact]
    public void Setup_removes_an_engine_left_in_the_SSMS_extension_folder()
    {
        Assert.Contains("Type: filesandordirs; Name: \"{code:GetSSMS22ExtDir}\\Engine\"; Check: CheckSSMS22", Section(Setup, "InstallDelete"));
        // And it still installs no engine there itself: the shell must find the installed {app}\Engine.
        Assert.DoesNotMatch(@"DestDir: ""\{code:GetSSMS22ExtDir\}\\Engine", Setup);
    }

    [Theory]
    [InlineData("")]
    [InlineData("css\\")]
    [InlineData("css\\components\\")]
    [InlineData("css\\themes\\")]
    [InlineData("js\\")]
    [InlineData("lib\\codemirror\\")]
    [InlineData("lib\\icons\\")]
    [InlineData("spike-corpus\\")]
    public void Setup_deletes_the_compressed_copies_older_versions_left(string folder)
    {
        var section = Section(Web, "InstallDelete");
        foreach (var ext in new[] { "br", "gz" })
            Assert.Contains($"Type: files; Name: \"{{app}}\\Web\\{folder}*.{ext}\"; Components: web", section);
    }

    [Fact]
    public void Setup_still_does_not_install_compressed_copies()
        => Assert.Matches(@"Source: ""\.\.\\AkmlSql\.Web\\bin\\Release\\net10\.0\\publish\\wwwroot\\\*""; Excludes: ""\*\.br,\*\.gz""", Web);
}
