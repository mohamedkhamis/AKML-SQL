using System.Text.Json;
using AkmlSql.Core;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Editor;

/// <summary>
/// Spec 043 (T014, research R8, data-model §5) — the query tabs' records: one per tab (<c>tab:{id}</c>) and the tab set
/// (<c>set</c>) in the <c>editorTabs</c> store. A tab's text is saved after a 500 ms pause, one pending write PER TAB (the
/// single-document store had one slot, so a second tab typed within the pause would have dropped the first one's text).
/// A pending save for a tab no longer in the set writes nothing: a tab closed in another window must not come back.
/// </summary>
public sealed class EditorTabStoreTests
{
    private readonly InMemoryIndexedDbAdapter _db = new();
    private readonly EditorSessionStore _store;

    public EditorTabStoreTests() => _store = new EditorSessionStore(_db);

    private static EditorSessionRecord Tab(string id, string text, ConnectionBinding? binding = null) => new()
    {
        TabId = id,
        DocumentText = text,
        DocumentName = "SQLQuery1",
        SessionKey = "hist-" + id,
        EngineSessionId = "engine-" + id,
        Binding = binding,
        Kind = (int)(binding?.Kind ?? DatabaseKind.SqlServer),
    };

    private Task SetAsync(params string[] order) =>
        _store.SaveSetAsync(new EditorTabSetRecord { Order = order.ToList(), ActiveId = order.FirstOrDefault(), NextDefaultNumber = order.Length + 1 });

    [Fact]
    public async Task Two_tabs_typed_within_the_pause_both_persist()
    {
        await SetAsync("a", "b");

        await _store.SaveTabAsync("a", Tab("a", "SELECT 1"));
        await _store.SaveTabAsync("b", Tab("b", "SELECT 2"));
        await Task.Delay(800);

        var fresh = new EditorSessionStore(_db);   // as after a reload
        Assert.Equal("SELECT 1", (await fresh.RestoreTabAsync("a"))?.DocumentText);
        Assert.Equal("SELECT 2", (await fresh.RestoreTabAsync("b"))?.DocumentText);
    }

    [Fact]
    public async Task A_restore_for_a_tab_returns_its_own_pending_save_and_no_other()
    {
        await SetAsync("a", "b");
        await _store.SaveTabAsync("a", Tab("a", "typed a moment ago"));

        Assert.Equal("typed a moment ago", (await _store.RestoreTabAsync("a"))?.DocumentText);
        Assert.Null(await _store.RestoreTabAsync("b"));
    }

    [Fact]
    public async Task Flush_writes_every_pending_tab_at_once()
    {
        await SetAsync("a", "b");
        await _store.SaveTabAsync("a", Tab("a", "x"));
        await _store.SaveTabAsync("b", Tab("b", "y"));

        await _store.FlushAsync();

        var fresh = new EditorSessionStore(_db);
        Assert.Equal("x", (await fresh.RestoreTabAsync("a"))?.DocumentText);
        Assert.Equal("y", (await fresh.RestoreTabAsync("b"))?.DocumentText);
    }

    [Fact]
    public async Task The_set_is_written_at_once_and_read_back()
    {
        await _store.SaveSetAsync(new EditorTabSetRecord { Order = new() { "b", "a" }, ActiveId = "a", NextDefaultNumber = 7 });

        var set = await new EditorSessionStore(_db).RestoreSetAsync();
        Assert.NotNull(set);
        Assert.Equal(new[] { "b", "a" }, set!.Order);
        Assert.Equal("a", set.ActiveId);
        Assert.Equal(7, set.NextDefaultNumber);
    }

    [Fact]
    public async Task A_debounced_save_for_a_tab_closed_meanwhile_writes_nothing()
    {
        await SetAsync("a", "b");
        await _store.SaveTabAsync("a", Tab("a", "typed in another window"));
        await SetAsync("b");   // tab a was closed (here, or in another window)

        await Task.Delay(800);
        await _store.FlushAsync();

        Assert.Null(await new EditorSessionStore(_db).RestoreTabAsync("a"));
    }

    [Fact]
    public async Task Deleting_a_tab_drops_its_pending_save_and_its_record()
    {
        await SetAsync("a");
        await _store.SaveTabAsync("a", Tab("a", "x"));
        await _store.FlushAsync();
        await _store.SaveTabAsync("a", Tab("a", "y"));

        await _store.DeleteTabAsync("a");
        await Task.Delay(800);

        Assert.Null(await new EditorSessionStore(_db).RestoreTabAsync("a"));
    }

    [Fact]
    public async Task A_tab_record_keeps_its_binding_and_holds_no_password_anywhere()
    {
        await SetAsync("a");
        var binding = ConnectionBinding.FromTarget(new ConnectionTarget(DatabaseKind.SqlServer, "db1.example.com", "sales", WindowsAuth: false,
            User: "reporting", Password: "Sup3r-secret", TrustServerCertificate: true), "saved-1");
        await _store.SaveTabAsync("a", Tab("a", "SELECT 1", binding));
        await _store.FlushAsync();

        var raw = await _db.GetAsync(StoreNames.EditorTabs, "tab:a");
        Assert.NotNull(raw);
        Assert.DoesNotContain("Sup3r-secret", raw);
        Assert.DoesNotContain("password", raw, StringComparison.OrdinalIgnoreCase);   // the store's tripwire

        var back = await new EditorSessionStore(_db).RestoreTabAsync("a");
        Assert.Equal("db1.example.com", back!.Binding!.Server);
        Assert.True(back.Binding.TrustServerCertificate);
        Assert.Equal("engine-a", back.EngineSessionId);
    }

    [Fact]
    public async Task With_tabs_the_single_document_read_is_the_active_tabs_record()
    {
        // The offline quick info and signature help read "the" document through RestoreAsync.
        await _store.SaveSetAsync(new EditorTabSetRecord { Order = new() { "a", "b" }, ActiveId = "b" });
        await _store.SaveTabAsync("a", Tab("a", "SELECT 1"));
        await _store.SaveTabAsync("b", Tab("b", "SELECT 2"));

        Assert.Equal("SELECT 2", (await _store.RestoreAsync())?.DocumentText);
    }

    [Fact]
    public async Task Clearing_the_tabs_removes_every_tab_record_the_set_and_the_old_single_document()
    {
        await SetAsync("a", "b");
        await _store.SaveTabAsync("a", Tab("a", "x"));
        await _store.SaveTabAsync("b", Tab("b", "y"));
        await _store.FlushAsync();
        await _db.SetAsync(StoreNames.EditorSession, "current", JsonSerializer.Serialize(new EditorSessionRecord { DocumentText = "old" }));

        await _store.ClearTabsAsync();

        Assert.Empty(await _db.ListAsync(StoreNames.EditorTabs));
        Assert.Null(await _db.GetAsync(StoreNames.EditorSession, "current"));
    }

    // ── Review of US1: a save already past its pause when the tab goes ──────────────────────────────────────────────

    /// <summary>Holds the first read of the tab set until released: a debounced save is then past its pause and in its write.</summary>
    private sealed class GatedAdapter : IIndexedDbAdapter
    {
        private readonly IIndexedDbAdapter _inner;
        public GatedAdapter(IIndexedDbAdapter inner) => _inner = inner;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Armed { get; set; }

        public async Task<string?> GetAsync(string store, string key)
        {
            if (Armed && store == StoreNames.EditorTabs && key == EditorSessionStore.SetKey)
            {
                Armed = false;
                Entered.TrySetResult();
                await Release.Task;
            }
            return await _inner.GetAsync(store, key);
        }

        public Task SetAsync(string store, string key, string valueJson) => _inner.SetAsync(store, key, valueJson);
        public Task DeleteAsync(string store, string key) => _inner.DeleteAsync(store, key);
        public Task<IReadOnlyList<KeyValuePair<string, string>>> ListAsync(string store) => _inner.ListAsync(store);
        public Task ClearAsync(string store) => _inner.ClearAsync(store);
    }

    [Fact]
    public async Task A_save_already_writing_when_its_tab_is_deleted_leaves_no_record_behind()
    {
        var gated = new GatedAdapter(_db);
        var store = new EditorSessionStore(gated);
        await store.SaveSetAsync(new EditorTabSetRecord { Order = new List<string> { "a" }, ActiveId = "a" });
        gated.Armed = true;
        await store.SaveTabAsync("a", Tab("a", "typed just before the close"));
        await gated.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));   // the save read the set: it is past its pause

        await store.DeleteTabAsync("a");
        gated.Release.SetResult();
        await Task.Delay(300);

        Assert.Null(await new EditorSessionStore(_db).RestoreTabAsync("a"));
    }

    [Fact]
    public async Task A_save_already_writing_when_every_tab_is_cleared_leaves_no_record_behind()
    {
        var gated = new GatedAdapter(_db);
        var store = new EditorSessionStore(gated);
        await store.SaveSetAsync(new EditorTabSetRecord { Order = new List<string> { "a" }, ActiveId = "a" });
        gated.Armed = true;
        await store.SaveTabAsync("a", Tab("a", "typed just before the reset"));
        await gated.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await store.ClearTabsAsync();
        gated.Release.SetResult();
        await Task.Delay(300);

        Assert.Null(await new EditorSessionStore(_db).RestoreTabAsync("a"));
    }
}
