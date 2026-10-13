using System.Threading.Tasks;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Services;

/// <summary>
/// Spec 021 (web edition) -- M2 task T044. Round-trips for analyser settings.
/// </summary>
public sealed class AnalysisSettingsStoreTests
{
    [Fact]
    public async Task GetAsync_returns_defaults_on_a_fresh_store()
    {
        var store = new InMemoryIndexedDbAdapter();
        var settings = new AnalysisSettingsStore(store);

        var current = await settings.GetAsync();

        Assert.True(current.Enabled);
        Assert.True(current.AutoAnalyseOnFormat);
        Assert.Empty(current.RuleOverrides);
    }

    [Fact]
    public async Task SetAsync_then_GetAsync_in_a_new_instance_returns_the_persisted_record()
    {
        var store = new InMemoryIndexedDbAdapter();
        var firstSettings = new AnalysisSettingsStore(store);
        var write = new WebAnalysisSettings { Enabled = false, AutoAnalyseOnFormat = false };
        write.RuleOverrides["PE001"] = "off";
        await firstSettings.SetAsync(write);

        var second = new AnalysisSettingsStore(store);
        var read = await second.GetAsync();

        Assert.False(read.Enabled);
        Assert.False(read.AutoAnalyseOnFormat);
        Assert.Equal("off", read.RuleOverrides["PE001"]);
    }

    [Fact]
    public async Task Corrupt_persisted_record_falls_back_to_defaults()
    {
        var store = new InMemoryIndexedDbAdapter();
        store.Seed(StoreNames.AnalysisSettings, "current", "{not-valid-json");

        var settings = new AnalysisSettingsStore(store);
        var current = await settings.GetAsync();

        Assert.True(current.Enabled);   // back to default
    }

    // ── Spec 041 (T131, research R47, R51) ───────────────────────────────────────────────────────

    [Fact]
    public async Task The_default_Problems_filter_shows_every_severity()
    {
        var current = await new AnalysisSettingsStore(new InMemoryIndexedDbAdapter()).GetAsync();

        Assert.NotNull(current.ProblemsFilter);
        Assert.True(current.ProblemsFilter.ShowInfo);
        Assert.True(current.ProblemsFilter.ShowWarning);
        Assert.True(current.ProblemsFilter.ShowError);
    }

    [Fact]
    public async Task A_record_written_before_the_filter_existed_still_reads()
    {
        var store = new InMemoryIndexedDbAdapter();
        store.Seed(StoreNames.AnalysisSettings, "current", "{\"Enabled\":false,\"AutoAnalyseOnFormat\":true,\"RuleOverrides\":{\"PE001\":\"off\"}}");

        var current = await new AnalysisSettingsStore(store).GetAsync();

        Assert.False(current.Enabled);
        Assert.Equal("off", current.RuleOverrides["PE001"]);
        Assert.True(current.ProblemsFilter.ShowWarning);
    }

    [Fact]
    public async Task Restore_defaults_deletes_only_the_current_key()
    {
        var db = new RecordingIndexedDbAdapter();
        db.Seed(StoreNames.AnalysisSettings, "executionSettings", "{}");
        var settings = new AnalysisSettingsStore(db);
        await settings.SetAsync(new WebAnalysisSettings { Enabled = false });
        db.ResetLog();

        await settings.ResetAsync();

        Assert.Equal(new[] { (StoreNames.AnalysisSettings, "current") }, db.Deletes);
        Assert.Empty(db.Clears);
        Assert.True((await settings.GetAsync()).Enabled);
        Assert.NotNull(await db.GetAsync(StoreNames.AnalysisSettings, "executionSettings"));
    }

    [Fact]
    public async Task Reload_reads_what_another_tab_saved()
    {
        var store = new InMemoryIndexedDbAdapter();
        var first = new AnalysisSettingsStore(store);
        await first.GetAsync();
        await new AnalysisSettingsStore(store).SetAsync(new WebAnalysisSettings { Enabled = false });

        await first.ReloadAsync();

        Assert.False((await first.GetAsync()).Enabled);
    }
}
