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
}
