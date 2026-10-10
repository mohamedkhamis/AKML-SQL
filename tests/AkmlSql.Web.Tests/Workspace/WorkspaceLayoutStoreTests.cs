using System.Text.Json;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Workspace;

/// <summary>
/// Spec 041 (T111, research R36, FR-045, FR-047) — the remembered workspace layout: one record per field, so a
/// browser tab writes only the field the user changed; loading never writes; a layout of another version is
/// ignored; Reset clears it.
/// </summary>
public sealed class WorkspaceLayoutStoreTests
{
    /// <summary>The in-memory adapter, recording every write.</summary>
    private sealed class RecordingAdapter : IIndexedDbAdapter
    {
        private readonly InMemoryIndexedDbAdapter _inner = new();

        public List<(string Store, string Key, string Value)> Writes { get; } = new();
        public List<string> Clears { get; } = new();

        public Task<string?> GetAsync(string store, string key) => _inner.GetAsync(store, key);

        public Task SetAsync(string store, string key, string valueJson)
        {
            Writes.Add((store, key, valueJson));
            return _inner.SetAsync(store, key, valueJson);
        }

        public Task DeleteAsync(string store, string key) => _inner.DeleteAsync(store, key);

        public Task<IReadOnlyList<KeyValuePair<string, string>>> ListAsync(string store) => _inner.ListAsync(store);

        public Task ClearAsync(string store)
        {
            Clears.Add(store);
            return _inner.ClearAsync(store);
        }

        public Task Seed(string key, object value) => _inner.SetAsync(StoreNames.WorkspaceLayout, key, JsonSerializer.Serialize(value));
    }

    [Fact]
    public async Task An_empty_store_loads_the_default_layout_and_writes_nothing()
    {
        var adapter = new RecordingAdapter();
        var store = new WorkspaceLayoutStore(adapter);

        var layout = await store.LoadAsync();

        Assert.Equal(WorkspaceLayout.Default, layout);
        Assert.Empty(adapter.Writes);
    }

    [Fact]
    public async Task Known_fields_are_read_and_unknown_ones_ignored()
    {
        var adapter = new RecordingAdapter();
        await adapter.Seed(WorkspaceLayoutFields.LayoutVersion, 1);
        await adapter.Seed(WorkspaceLayoutFields.SchemaVisible, false);
        await adapter.Seed(WorkspaceLayoutFields.SchemaWidth, 300);
        await adapter.Seed(WorkspaceLayoutFields.AiVisible, true);
        await adapter.Seed(WorkspaceLayoutFields.AiTab, "chat");
        await adapter.Seed(WorkspaceLayoutFields.ResultsHeight, 222);
        await adapter.Seed(WorkspaceLayoutFields.ResultsTab, "messages");
        await adapter.Seed("someFutureField", 42);

        var layout = await new WorkspaceLayoutStore(adapter).LoadAsync();

        Assert.False(layout.SchemaVisible);
        Assert.Equal(300, layout.SchemaWidth);
        Assert.True(layout.AiVisible);
        Assert.Equal("chat", layout.AiTab);
        Assert.Equal(222, layout.ResultsHeight);
        Assert.Equal("messages", layout.ResultsTab);
        Assert.Empty(adapter.Writes);
    }

    [Fact]
    public async Task A_layout_of_another_version_is_discarded()
    {
        var adapter = new RecordingAdapter();
        await adapter.Seed(WorkspaceLayoutFields.LayoutVersion, 99);
        await adapter.Seed(WorkspaceLayoutFields.SchemaVisible, false);

        var layout = await new WorkspaceLayoutStore(adapter).LoadAsync();

        Assert.Equal(WorkspaceLayout.Default, layout);
        Assert.Empty(adapter.Writes);
    }

    [Fact]
    public async Task A_bad_value_is_ignored_and_the_default_kept()
    {
        var adapter = new RecordingAdapter();
        await adapter.Seed(WorkspaceLayoutFields.LayoutVersion, 1);
        await adapter.Seed(WorkspaceLayoutFields.SchemaWidth, "wide");
        await adapter.Seed(WorkspaceLayoutFields.AiTab, "elsewhere");

        var layout = await new WorkspaceLayoutStore(adapter).LoadAsync();

        Assert.Equal(WorkspaceLayout.Default.SchemaWidth, layout.SchemaWidth);
        Assert.Equal(WorkspaceLayout.AiTabActions, layout.AiTab);
    }

    [Fact]
    public async Task A_change_writes_only_its_field_and_the_version_with_the_first_change()
    {
        var adapter = new RecordingAdapter();
        var store = new WorkspaceLayoutStore(adapter);
        await store.LoadAsync();

        await store.SetAsync(WorkspaceLayoutFields.SchemaWidth, 310);
        await store.SetAsync(WorkspaceLayoutFields.ResultsVisible, false);

        Assert.Equal(new[] { WorkspaceLayoutFields.LayoutVersion, WorkspaceLayoutFields.SchemaWidth, WorkspaceLayoutFields.ResultsVisible },
            adapter.Writes.Select(w => w.Key));
        Assert.All(adapter.Writes, w => Assert.Equal(StoreNames.WorkspaceLayout, w.Store));
        Assert.Equal("310", adapter.Writes[1].Value);
        Assert.Equal(310, store.Current.SchemaWidth);
        Assert.False(store.Current.ResultsVisible);
    }

    [Fact]
    public async Task A_store_that_already_has_the_version_does_not_write_it_again()
    {
        var adapter = new RecordingAdapter();
        await adapter.Seed(WorkspaceLayoutFields.LayoutVersion, 1);
        var store = new WorkspaceLayoutStore(adapter);
        await store.LoadAsync();

        await store.SetAsync(WorkspaceLayoutFields.NavVisible, false);

        Assert.Equal(new[] { WorkspaceLayoutFields.NavVisible }, adapter.Writes.Select(w => w.Key));
    }

    [Fact]
    public async Task A_remembered_layout_with_every_region_hidden_loads_with_the_default_regions_and_writes_nothing()
    {
        var adapter = new RecordingAdapter();
        await adapter.Seed(WorkspaceLayoutFields.LayoutVersion, 1);
        await adapter.Seed(WorkspaceLayoutFields.NavVisible, false);
        await adapter.Seed(WorkspaceLayoutFields.SchemaVisible, false);
        await adapter.Seed(WorkspaceLayoutFields.ResultsVisible, false);
        var store = new WorkspaceLayoutStore(adapter);

        var layout = await store.LoadAsync();

        Assert.True(layout.NavVisible);
        Assert.True(layout.SchemaVisible);
        Assert.True(layout.ResultsVisible);
        Assert.Empty(adapter.Writes);
    }

    [Fact]
    public async Task During_a_session_every_region_can_be_hidden()
    {
        var store = new WorkspaceLayoutStore(new RecordingAdapter());
        await store.LoadAsync();

        await store.SetAsync(WorkspaceLayoutFields.NavVisible, false);
        await store.SetAsync(WorkspaceLayoutFields.SchemaVisible, false);
        await store.SetAsync(WorkspaceLayoutFields.ResultsVisible, false);

        Assert.False(store.Current.NavVisible);
        Assert.False(store.Current.SchemaVisible);
        Assert.False(store.Current.ResultsVisible);
    }

    [Fact]
    public async Task Forgetting_the_results_height_goes_back_to_the_default_share()
    {
        var adapter = new RecordingAdapter();
        var store = new WorkspaceLayoutStore(adapter);
        await store.SetAsync(WorkspaceLayoutFields.ResultsHeight, 300);
        Assert.Equal(300, store.Current.ResultsHeight);

        await store.SetAsync<int?>(WorkspaceLayoutFields.ResultsHeight, null);

        Assert.Null(store.Current.ResultsHeight);
        var reloaded = new WorkspaceLayoutStore(adapter);
        Assert.Null((await reloaded.LoadAsync()).ResultsHeight);
    }

    [Fact]
    public async Task Reset_clears_the_store_and_raises_Changed()
    {
        var adapter = new RecordingAdapter();
        var store = new WorkspaceLayoutStore(adapter);
        await store.SetAsync(WorkspaceLayoutFields.SchemaVisible, false);
        int changed = 0;
        store.Changed += () => changed++;

        await store.ResetAsync();

        Assert.Equal(new[] { StoreNames.WorkspaceLayout }, adapter.Clears);
        Assert.Equal(WorkspaceLayout.Default, store.Current);
        Assert.Equal(1, changed);
        Assert.Empty(await adapter.ListAsync(StoreNames.WorkspaceLayout));
    }
}
