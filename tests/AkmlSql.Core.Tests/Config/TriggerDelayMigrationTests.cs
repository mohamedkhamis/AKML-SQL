using System.Text.Json;
using AkmlSql.Core.Config;
using Xunit;

namespace AkmlSql.Core.Tests.Config;

/// <summary>
/// Spec 040 (OPT-01) — "Trigger delay" did nothing before spec 040, and every config carries its
/// old default of 100. Honoured as written it delayed every suggestion; the one-time migration
/// makes that 0 (at once, as before) and keeps any value the user chose.
/// </summary>
public sealed class TriggerDelayMigrationTests
{
    // As config.json is written (ConfigManager: camelCase).
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static AppSettings Read(string intelliSenseJson) =>
        JsonSerializer.Deserialize<AppSettings>("{\"intelliSense\":" + intelliSenseJson + "}", Options)!;

    [Fact]
    public void An_older_configs_untouched_100_becomes_immediate_once()
    {
        var settings = Read("{\"triggerDelayMs\":100}");

        ConfigManager.MigrateTriggerDelay(settings);

        Assert.Equal(0, settings.IntelliSense.TriggerDelayMs);
        Assert.Equal(1, settings.IntelliSense.TriggerDelayVersion);
    }

    [Fact]
    public void Another_value_in_an_older_config_is_kept()
    {
        var settings = Read("{\"triggerDelayMs\":300}");

        ConfigManager.MigrateTriggerDelay(settings);

        Assert.Equal(300, settings.IntelliSense.TriggerDelayMs);
        Assert.Equal(1, settings.IntelliSense.TriggerDelayVersion);
    }

    [Fact]
    public void A_100_chosen_after_the_move_is_kept()
    {
        var settings = Read("{\"triggerDelayMs\":100,\"triggerDelayVersion\":1}");

        ConfigManager.MigrateTriggerDelay(settings);

        Assert.Equal(100, settings.IntelliSense.TriggerDelayMs);
    }

    [Fact]
    public void The_marker_survives_a_save()
    {
        var settings = Read("{\"triggerDelayMs\":100}");
        ConfigManager.MigrateTriggerDelay(settings);

        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, Options), Options)!;

        Assert.Equal(1, back.IntelliSense.TriggerDelayVersion);
        Assert.Equal(0, back.IntelliSense.TriggerDelayMs);
    }
}
