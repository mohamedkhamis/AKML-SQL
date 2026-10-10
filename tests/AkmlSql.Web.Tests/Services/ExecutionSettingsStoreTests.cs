using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Services;

/// <summary>
/// Spec 041 (T131, research R52, R47, FR-052) — Settings › Queries owns the saved query defaults; the toolbar's
/// quick control is a session-only override (never written); saving a default clears it; Restore defaults
/// deletes only the <c>executionSettings</c> key of the shared store.
/// </summary>
public sealed class ExecutionSettingsStoreTests
{
    private static (ExecutionSettingsStore Store, RecordingIndexedDbAdapter Db) Build()
    {
        var db = new RecordingIndexedDbAdapter();
        return (new ExecutionSettingsStore(db), db);
    }

    [Fact]
    public async Task The_effective_values_are_the_saved_ones_until_the_toolbar_overrides_them()
    {
        var (store, _) = Build();
        await store.SetAsync(new ExecutionSettings { MaxRows = 500, CommandTimeoutSeconds = 60 });

        var effective = await store.GetEffectiveAsync();
        Assert.Equal(500, effective.MaxRows);
        Assert.Equal(60, effective.CommandTimeoutSeconds);

        store.SessionOverride = new SessionOverride { MaxRows = 50 };

        effective = await store.GetEffectiveAsync();
        Assert.Equal(50, effective.MaxRows);
        Assert.Equal(60, effective.CommandTimeoutSeconds);   // an override is per value
        Assert.Equal(500, (await store.GetAsync()).MaxRows);  // the saved default is untouched
    }

    [Fact]
    public async Task The_session_override_is_never_written()
    {
        var (store, db) = Build();
        await store.GetAsync();

        store.SessionOverride = new SessionOverride { MaxRows = 50, CommandTimeoutSeconds = 5 };
        await store.GetEffectiveAsync();

        Assert.Equal(0, db.ChangeCount);
    }

    [Fact]
    public async Task The_effective_values_carry_the_grid_options_from_the_saved_defaults()
    {
        var (store, _) = Build();
        await store.SetAsync(new ExecutionSettings { ShowColumnTypes = true, RetainLineBreaksOnCopy = true });
        store.SessionOverride = new SessionOverride { MaxRows = 10 };

        var effective = await store.GetEffectiveAsync();

        Assert.True(effective.ShowColumnTypes);
        Assert.True(effective.RetainLineBreaksOnCopy);
    }

    [Fact]
    public async Task Saving_a_default_clears_the_override()
    {
        var (store, _) = Build();
        store.SessionOverride = new SessionOverride { MaxRows = 50 };

        await store.SetAsync(new ExecutionSettings { MaxRows = 500 });

        Assert.Null(store.SessionOverride);
        Assert.Equal(500, (await store.GetEffectiveAsync()).MaxRows);
    }

    [Fact]
    public async Task Restore_defaults_deletes_only_its_own_key_and_drops_the_override()
    {
        var (store, db) = Build();
        db.Seed(StoreNames.AnalysisSettings, "current", "{}");
        db.Seed(StoreNames.AnalysisSettings, "editorSettings", "{}");
        await store.SetAsync(new ExecutionSettings { MaxRows = 500, CommandTimeoutSeconds = 90 });
        store.SessionOverride = new SessionOverride { MaxRows = 50 };
        db.ResetLog();

        await store.ResetAsync();

        Assert.Equal(new[] { (StoreNames.AnalysisSettings, "executionSettings") }, db.Deletes);
        Assert.Empty(db.Clears);
        Assert.Empty(db.Writes);
        Assert.Null(store.SessionOverride);
        var saved = await store.GetAsync();
        Assert.Equal(1000, saved.MaxRows);
        Assert.Equal(30, saved.CommandTimeoutSeconds);
        Assert.NotNull(await db.GetAsync(StoreNames.AnalysisSettings, "current"));
        Assert.NotNull(await db.GetAsync(StoreNames.AnalysisSettings, "editorSettings"));
    }

    [Fact]
    public async Task Reload_reads_what_another_tab_saved()
    {
        var db = new RecordingIndexedDbAdapter();
        var first = new ExecutionSettingsStore(db);
        Assert.Equal(1000, (await first.GetAsync()).MaxRows);   // cached

        await new ExecutionSettingsStore(db).SetAsync(new ExecutionSettings { MaxRows = 777 });
        Assert.Equal(1000, (await first.GetAsync()).MaxRows);   // still the cached value

        await first.ReloadAsync();

        Assert.Equal(777, (await first.GetAsync()).MaxRows);
    }
}
