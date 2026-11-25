using System.Text.Json;
using AkmlSql.Core.Config;
using Xunit;

namespace AkmlSql.Core.Tests.Config;

/// <summary>
/// Owner request of 2026-11-23 ("automatic like SSMS"): <c>bridge.windowsSignInAsBrowserUser</c> — Windows authentication from
/// a browser on the engine's computer signs in as that browser's Windows user. On by default, so an install's existing
/// config (which has no such key) gets the new behaviour; <c>false</c> keeps the engine's own account.
/// </summary>
public sealed class BridgeWindowsSignInTests
{
    [Fact]
    public void It_is_on_by_default_and_an_existing_config_without_the_key_gets_it()
    {
        Assert.True(new BridgeOptions().WindowsSignInAsBrowserUser);
        Assert.True(JsonSerializer.Deserialize<BridgeOptions>("{\"enabled\":true,\"port\":59417}")!.WindowsSignInAsBrowserUser);
    }

    [Fact]
    public void It_is_named_windowsSignInAsBrowserUser_and_false_round_trips()
    {
        var json = JsonSerializer.Serialize(new BridgeOptions { WindowsSignInAsBrowserUser = false });

        Assert.Contains("\"windowsSignInAsBrowserUser\":false", json);
        Assert.False(JsonSerializer.Deserialize<BridgeOptions>(json)!.WindowsSignInAsBrowserUser);
    }
}
