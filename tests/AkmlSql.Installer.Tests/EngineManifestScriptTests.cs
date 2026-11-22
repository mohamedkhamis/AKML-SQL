using System.Text;
using System.Text.Json;
using Xunit;

namespace AkmlSql.Installer.Tests;

/// <summary>
/// Spec 047 (WEB-001, contracts/engine-pairing.md §1): web-engine-manifest.ps1 publishes the engine's bridge port next to
/// the web bundle (akml-engine.json), because a page hosted by IIS cannot otherwise learn which port setup chose.
/// </summary>
public sealed class EngineManifestScriptTests
{
    private static (int ExitCode, string Output) Run(params string[] arguments) =>
        HostingTestTools.Run(
            Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
            new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
                    HostingTestTools.Script("web-engine-manifest.ps1") }.Concat(arguments).ToArray());

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "akml-engine-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Theory]
    [InlineData("ws")]
    [InlineData("wss")]
    public void Writes_the_port_and_scheme_as_JSON_without_a_BOM(string scheme)
    {
        var root = NewRoot();
        try
        {
            var result = Run("-WebRoot", root, "-Port", "59417", "-Scheme", scheme);
            Assert.True(result.ExitCode == 0, result.Output);

            var path = Path.Combine(root, "akml-engine.json");
            var bytes = File.ReadAllBytes(path);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "akml-engine.json must not start with a BOM");
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(bytes));
            Assert.Equal(1, document.RootElement.GetProperty("manifestVersion").GetInt32());
            Assert.Equal(59417, document.RootElement.GetProperty("bridgePort").GetInt32());
            Assert.Equal(scheme, document.RootElement.GetProperty("scheme").GetString());
            Assert.Equal(3, document.RootElement.EnumerateObject().Count());
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_second_run_replaces_the_file()
    {
        var root = NewRoot();
        try
        {
            Assert.Equal(0, Run("-WebRoot", root, "-Port", "59417", "-Scheme", "ws").ExitCode);
            var result = Run("-WebRoot", root, "-Port", "59419", "-Scheme", "wss");
            Assert.True(result.ExitCode == 0, result.Output);
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "akml-engine.json")));
            Assert.Equal(59419, document.RootElement.GetProperty("bridgePort").GetInt32());
            Assert.Equal("wss", document.RootElement.GetProperty("scheme").GetString());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("80")]
    [InlineData("1023")]
    [InlineData("70000")]
    public void A_port_outside_1024_to_65535_is_refused(string port)
    {
        var root = NewRoot();
        try
        {
            var result = Run("-WebRoot", root, "-Port", port, "-Scheme", "ws");
            Assert.Equal(1, result.ExitCode);
            Assert.False(File.Exists(Path.Combine(root, "akml-engine.json")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_missing_web_root_is_refused()
    {
        var missing = Path.Combine(Path.GetTempPath(), "akml-engine-manifest-missing-" + Guid.NewGuid().ToString("N"));
        var result = Run("-WebRoot", missing, "-Port", "59417", "-Scheme", "ws");
        Assert.Equal(1, result.ExitCode);
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public void A_web_root_that_is_a_junction_is_refused()
    {
        var target = NewRoot();
        var junction = target + "-junction";
        try
        {
            HostingTestTools.PowerShell($"New-Item -ItemType Junction -Path '{junction}' -Target '{target}' | Out-Null");
            var result = Run("-WebRoot", junction, "-Port", "59417", "-Scheme", "ws");
            Assert.Equal(1, result.ExitCode);
            Assert.False(File.Exists(Path.Combine(target, "akml-engine.json")));
        }
        finally
        {
            if (Directory.Exists(junction)) Directory.Delete(junction);   // removes the link, never the target's content
            Directory.Delete(target, recursive: true);
        }
    }

    [Fact]
    public void Remove_deletes_the_file_and_succeeds_when_it_is_already_gone()
    {
        var root = NewRoot();
        try
        {
            Assert.Equal(0, Run("-WebRoot", root, "-Port", "59417", "-Scheme", "ws").ExitCode);
            var removed = Run("-WebRoot", root, "-Remove");
            Assert.True(removed.ExitCode == 0, removed.Output);
            Assert.False(File.Exists(Path.Combine(root, "akml-engine.json")));

            var again = Run("-WebRoot", root, "-Remove");
            Assert.True(again.ExitCode == 0, again.Output);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Dot_sourcing_writes_nothing()
    {
        var root = NewRoot();
        try
        {
            HostingTestTools.DotSource("web-engine-manifest.ps1",
                $"Get-EngineManifestJson -Port 59417 -Scheme ws | Out-Null");
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
