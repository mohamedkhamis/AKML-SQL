using System.Text.Json;
using Xunit;

namespace AkmlSql.Installer.Tests;

public sealed class WebConfigBridgeTests
{
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Windows_PowerShell_entry_point_creates_and_replaces_config(bool existingConfig)
    {
        Skip.IfNot(Environment.IsPrivilegedProcess, "The configuration script requires administrator rights; all test state is isolated.");
        var directory = Path.Combine(Path.GetTempPath(), "akml-config-write-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var configPath = Path.Combine(directory, "config.json");
        var webRoot = Path.Combine(directory, "Public Web");
        try
        {
            if (existingConfig)
                File.WriteAllText(configPath, "{\"custom\":{\"keep\":17},\"bridge\":{\"bindAddress\":\"0.0.0.0\",\"port\":47291}}");

            // Exercise the real disk write under the host used by setup, including a second run.
            // HostingTestTools isolates ProgramData; this never runs the product installer.
            foreach (var port in new[] { 59417, 59419 })
            {
                var result = HostingTestTools.Run(
                    Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                    "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
                    HostingTestTools.Script("web-config-bridge.ps1"), "-Port", port.ToString(),
                    "-Mode", "Localhost", "-WebRoot", webRoot, "-ConfigPath", configPath);
                Assert.True(result.ExitCode == 0, result.Output);
                using var document = JsonDocument.Parse(File.ReadAllText(configPath));
                var bridge = document.RootElement.GetProperty("bridge");
                Assert.Equal(port, bridge.GetProperty("port").GetInt32());
                Assert.Equal("127.0.0.1", bridge.GetProperty("bindAddress").GetString());
                Assert.Equal(webRoot, bridge.GetProperty("webRoot").GetString());
                Assert.Equal("", bridge.GetProperty("tlsCertPath").GetString());
                if (existingConfig)
                    Assert.Equal(17, document.RootElement.GetProperty("custom").GetProperty("keep").GetInt32());
                Assert.False(File.Exists(configPath + ".tmp"));
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("Localhost", "127.0.0.1", @"C:\Program Files\AKML SQL\Web")]
    [InlineData("Lan", "0.0.0.0", "")]
    public void Bridge_is_replaced_other_settings_survive_and_web_root_is_always_written(
        string mode, string bind, string webRoot)
    {
        var json = HostingTestTools.DotSource("web-config-bridge.ps1",
            $"Get-BridgeConfigJson -ExistingJson '{{\"custom\":{{\"keep\":17}},\"bridge\":{{\"obsolete\":true}}}}' " +
            $"-Port 59417 -Mode {mode} -WebRoot '{webRoot}'");
        using var document = JsonDocument.Parse(json);
        Assert.Equal(17, document.RootElement.GetProperty("custom").GetProperty("keep").GetInt32());
        var bridge = document.RootElement.GetProperty("bridge");
        Assert.Equal(bind, bridge.GetProperty("bindAddress").GetString());
        Assert.Equal(webRoot, bridge.GetProperty("webRoot").GetString());
        Assert.Equal(59417, bridge.GetProperty("port").GetInt32());
        Assert.True(bridge.GetProperty("enabled").GetBoolean());
        Assert.Equal(90, bridge.GetProperty("tokenTtlDays").GetInt32());
        Assert.Equal(JsonValueKind.Null, bridge.GetProperty("tlsCertPasswordRef").ValueKind);
        Assert.False(bridge.TryGetProperty("obsolete", out _));
        Assert.Equal(8, bridge.EnumerateObject().Count());
        Assert.EndsWith(@"AKML SQL Web\tokens.json", bridge.GetProperty("tokenStorePath").GetString());
        Assert.Equal(mode == "Lan", bridge.GetProperty("tlsCertPath").GetString()!.Length > 0);
    }

    [Fact]
    public void Dot_sourcing_creates_no_config_or_log()
    {
        var directory = Path.Combine(Path.GetTempPath(), "akml-config-source-" + Guid.NewGuid().ToString("N"));
        try
        {
            HostingTestTools.DotSource("web-config-bridge.ps1",
                $"$env:ProgramData='{directory}'; . '{HostingTestTools.Script("web-config-bridge.ps1")}'");
            Assert.False(Directory.Exists(directory));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
