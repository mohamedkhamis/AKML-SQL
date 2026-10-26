using System.Text.Json;
using Xunit;

namespace AkmlSql.Installer.Tests;

public sealed class WebConfigBridgeTests
{
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
