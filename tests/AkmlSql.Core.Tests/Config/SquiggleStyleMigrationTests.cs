using System.Text.Json;
using AkmlSql.Core.Config;
using Xunit;

namespace AkmlSql.Core.Tests.Config;

/// <summary>
/// Spec 047 (UX-001, SSMS-001, contracts/analysis-display.md §2) — <c>codeAnalysis.squiggleStyle</c> did nothing before
/// this feature, and every config carries its old default, <c>"underline"</c>. The new default is <c>"marker"</c>
/// (advisory findings get a margin dot only), so a config that has not been through the one-time migration moves from
/// the untouched old default to <c>"marker"</c>; any other value was chosen and is kept. In memory only: the next save
/// persists it, with <c>squiggleStyleVersion</c> so it never runs twice.
/// </summary>
public sealed class SquiggleStyleMigrationTests
{
    // As config.json is written (ConfigManager: camelCase).
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static AppSettings Read(string codeAnalysisJson) =>
        JsonSerializer.Deserialize<AppSettings>("{\"codeAnalysis\":" + codeAnalysisJson + "}", Options)!;

    [Fact]
    public void An_older_configs_untouched_underline_becomes_marker_once()
    {
        var settings = Read("{\"squiggleStyle\":\"underline\"}");

        ConfigManager.MigrateSquiggleStyle(settings);

        Assert.Equal("marker", settings.CodeAnalysis.SquiggleStyle);
        Assert.Equal(1, settings.CodeAnalysis.SquiggleStyleVersion);
    }

    [Fact]
    public void Another_value_in_an_older_config_is_kept()
    {
        var settings = Read("{\"squiggleStyle\":\"dotted\"}");

        ConfigManager.MigrateSquiggleStyle(settings);

        Assert.Equal("dotted", settings.CodeAnalysis.SquiggleStyle);
        Assert.Equal(1, settings.CodeAnalysis.SquiggleStyleVersion);
    }

    [Fact]
    public void An_underline_chosen_after_the_move_is_kept()
    {
        var settings = Read("{\"squiggleStyle\":\"underline\",\"squiggleStyleVersion\":1}");

        ConfigManager.MigrateSquiggleStyle(settings);

        Assert.Equal("underline", settings.CodeAnalysis.SquiggleStyle);
    }

    [Fact]
    public void New_settings_show_advisory_findings_as_a_marker()
    {
        Assert.Equal("marker", new AppSettings().CodeAnalysis.SquiggleStyle);
    }

    [Fact]
    public void A_migration_that_fails_leaves_the_settings_as_read()
    {
        var settings = new AppSettings { CodeAnalysis = null! };

        var thrown = Record.Exception(() => ConfigManager.MigrateSquiggleStyle(settings));

        Assert.Null(thrown);
        Assert.Null(settings.CodeAnalysis);
    }

    [Fact]
    public void The_marker_survives_a_save()
    {
        var settings = Read("{\"squiggleStyle\":\"underline\"}");
        ConfigManager.MigrateSquiggleStyle(settings);

        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings, Options), Options)!;

        Assert.Equal(1, back.CodeAnalysis.SquiggleStyleVersion);
        Assert.Equal("marker", back.CodeAnalysis.SquiggleStyle);
    }
}
