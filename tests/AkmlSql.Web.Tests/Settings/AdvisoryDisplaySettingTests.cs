using System.Text.Json;
using AkmlSql.Web.Services;
using AkmlSql.Web.Services.Settings;
using Bunit;
using Xunit;

namespace AkmlSql.Web.Tests.Settings;

/// <summary>
/// Spec 047 (UX-001, contracts/analysis-display.md §3) — the web edition's "Advisory findings" setting: "Margin marker
/// only" (<c>marker</c>, the default) or "Underline in the text" (<c>underline</c>), for Information and Hint findings such
/// as a missing semicolon. A record saved before the setting existed reads <c>marker</c>; the setting is part of the
/// Code analysis section's export, import and Restore defaults.
/// </summary>
public sealed class AdvisoryDisplaySettingTests : IDisposable
{
    private readonly SettingsPageHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task The_setting_defaults_to_the_marker_and_an_older_record_reads_it_too()
    {
        Assert.Equal("marker", new WebAnalysisSettings().AdvisoryDisplay);

        // A spec 041 record, saved before the member existed.
        await _host.Stores.Db.SetAsync(StoreNames.AnalysisSettings, "current", "{\"Enabled\":true,\"AutoAnalyseOnFormat\":false}");

        var stored = await new AnalysisSettingsStore(_host.Stores.Db).GetAsync();
        Assert.Equal("marker", stored.AdvisoryDisplay);
        Assert.False(stored.AutoAnalyseOnFormat);
    }

    [Fact]
    public async Task The_settings_row_saves_underline()
    {
        var cut = _host.Open("settings/code-analysis");

        var select = cut.Find("#setting-analysis-advisory-display-control");
        Assert.Equal(new[] { "marker", "underline" }, select.QuerySelectorAll("option").Select(o => o.GetAttribute("value")).ToArray());
        Assert.Equal(new[] { "Margin marker only", "Underline in the text" }, select.QuerySelectorAll("option").Select(o => o.TextContent.Trim()).ToArray());
        select.Change("underline");

        var stored = await new AnalysisSettingsStore(_host.Stores.Db).GetAsync();
        Assert.Equal("underline", stored.AdvisoryDisplay);
    }

    [Fact]
    public async Task Export_import_and_Restore_defaults_carry_it()
    {
        var store = _host.Stores.Analysis;
        var settings = await store.GetAsync();
        settings.AdvisoryDisplay = "underline";
        await store.SetAsync(settings);
        var section = new CodeAnalysisSettingsSection(store);

        var exported = (CodeAnalysisSettingsSection.CodeAnalysisDto)await section.ExportAsync();
        Assert.Equal("underline", exported.AdvisoryDisplay);

        await section.ResetAsync();
        Assert.Equal("marker", (await store.GetAsync()).AdvisoryDisplay);

        var unknown = await section.ImportAsync(JsonSerializer.SerializeToElement(exported, SettingsJson.Options));
        Assert.Equal(0, unknown);
        Assert.Equal("underline", (await store.GetAsync()).AdvisoryDisplay);
    }
}
