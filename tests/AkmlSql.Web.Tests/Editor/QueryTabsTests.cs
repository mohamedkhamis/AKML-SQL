using AkmlSql.Core;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Editor;

/// <summary>
/// Spec 043 (T012, T015, research R1, R8, R11, data-model §1–§2, §5) — the editor's query tabs: names never reused, a new
/// tab inheriting the active tab's connection, the 20-tab cap, closing to the previous tab, History's open into an empty
/// tab, and the set and every tab coming back after a reload. The first load after the upgrade moves the single document
/// of before tabs into tab 1.
/// </summary>
public sealed class QueryTabsTests
{
    private readonly InMemoryIndexedDbAdapter _db = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeNotificationService _notifications = new();

    private sealed class NoDownloads : IFileDownloader
    {
        public Task DownloadTextAsync(string fileName, string mimeType, string text, bool withBom) => Task.CompletedTask;
    }

    private (QueryTabs Tabs, SqlConnectionService Sql) Open()
    {
        var sql = new SqlConnectionService(new FakeBridge(), new FakeDiagnosticsRingBuffer());
        return (new QueryTabs(new EditorSessionStore(_db), sql, _dialogs, _notifications, new NoDownloads()), sql);
    }

    private sealed class FakeBridge : IEngineBridge
    {
        public BridgeState State => BridgeState.Disconnected;
        public string[] EngineCapabilities => Array.Empty<string>();
        public string? EngineVersion => null;
        public event Action<BridgeState>? StateChanged { add { } remove { } }
        public event Action<DateTimeOffset?>? RetryScheduled { add { } remove { } }
        public event Action<TlsFingerprintMismatch>? FingerprintMismatchDetected { add { } remove { } }
        public Task<AkmlSql.Core.Ipc.Messages.HandshakeResponse> ConnectAsync(EngineConnection c, string? b, string? p, CancellationToken ct) => throw new NotSupportedException();
        public Task<TResponse> SendAsync<TRequest, TResponse>(int type, TRequest request, CancellationToken ct) where TRequest : class where TResponse : class => throw new NotSupportedException();
        public Task SendNotificationAsync<TPayload>(int type, TPayload payload, CancellationToken ct) where TPayload : class => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => default;
    }

    private static ConnectionBinding Northwind() =>
        ConnectionBinding.FromTarget(new ConnectionTarget(DatabaseKind.SqlServer, "localhost", "Northwind", WindowsAuth: true), "saved-nw");

    [Fact]
    public async Task A_fresh_editor_has_one_empty_tab_named_SQLQuery1_and_it_is_active()
    {
        var (tabs, sql) = Open();
        await tabs.LoadAsync();

        var only = Assert.Single(tabs.Tabs);
        Assert.Equal("SQLQuery1", only.Name);
        Assert.Same(only, tabs.Active);
        Assert.Same(only.Session, sql.ActiveSession);
    }

    [Fact]
    public async Task New_query_takes_the_next_unused_name_becomes_active_and_inherits_a_copy_of_the_binding()
    {
        var (tabs, sql) = Open();
        await tabs.LoadAsync();
        var first = tabs.Active!;
        first.Session.Restore(Northwind());

        var second = await tabs.NewAsync(first);

        Assert.NotNull(second);
        Assert.Equal("SQLQuery2", second!.Name);
        Assert.Same(second, tabs.Active);
        Assert.Same(second.Session, sql.ActiveSession);
        Assert.NotEqual(first.Session.SessionId, second.Session.SessionId);   // its own engine session
        Assert.Equal("Northwind", second.Session.Binding!.Database);
        Assert.NotSame(first.Session.Binding, second.Session.Binding);
        Assert.True(second.Session.NeedsReattach);   // connected by the page: at once when no password is needed
    }

    [Fact]
    public async Task Names_are_never_reused_after_a_close()
    {
        var (tabs, _) = Open();
        await tabs.LoadAsync();
        var second = (await tabs.NewAsync(tabs.Active))!;
        await tabs.CloseAsync(second);

        var third = await tabs.NewAsync(tabs.Active);

        Assert.Equal("SQLQuery3", third!.Name);
    }

    [Fact]
    public async Task The_twenty_first_tab_is_refused()
    {
        var (tabs, _) = Open();
        await tabs.LoadAsync();
        for (int i = 1; i < IQueryTabs.MaxTabs; i++) Assert.NotNull(await tabs.NewAsync(tabs.Active));
        Assert.Equal(IQueryTabs.MaxTabs, tabs.Tabs.Count);

        Assert.Null(await tabs.NewAsync(tabs.Active));
        Assert.Equal(IQueryTabs.MaxTabs, tabs.Tabs.Count);
    }

    [Fact]
    public async Task Closing_the_active_tab_activates_the_one_before_it_and_the_last_close_leaves_an_empty_tab()
    {
        var (tabs, _) = Open();
        await tabs.LoadAsync();
        var one = tabs.Active!;
        var two = (await tabs.NewAsync(one))!;
        var three = (await tabs.NewAsync(two))!;
        await tabs.ActivateAsync(two);

        await tabs.CloseAsync(two);
        Assert.Same(one, tabs.Active);

        await tabs.CloseAsync(one);
        Assert.Same(three, tabs.Active);   // the first was closed: the next one

        await tabs.CloseAsync(three);
        var fresh = Assert.Single(tabs.Tabs);
        Assert.True(fresh.IsEmpty);
        Assert.Same(fresh, tabs.Active);
    }

    [Fact]
    public async Task Order_names_text_marks_bindings_and_the_active_tab_come_back_after_a_reload()
    {
        var (tabs, _) = Open();
        await tabs.LoadAsync();
        var one = tabs.Active!;
        one.Text = "SELECT 1";
        one.Session.Restore(Northwind());
        await tabs.PersistAsync(one);
        var two = (await tabs.NewAsync(one, "SELECT 2"))!;
        await tabs.ActivateAsync(one);
        await new EditorSessionStore(_db).FlushAsync();   // what the page does on dispose
        await Task.Delay(700);

        var (reloaded, sql) = Open();
        await reloaded.LoadAsync();

        Assert.Equal(new[] { one.Id, two.Id }, reloaded.Tabs.Select(t => t.Id));
        Assert.Equal(new[] { "SQLQuery1", "SQLQuery2" }, reloaded.Tabs.Select(t => t.Name));
        Assert.Equal("SELECT 1", reloaded.Tabs[0].Text);
        Assert.Equal("SELECT 2", reloaded.Tabs[1].Text);
        Assert.Equal(one.Id, reloaded.Active!.Id);
        Assert.Equal(one.Session.SessionId, reloaded.Tabs[0].Session.SessionId);   // the engine finds the session again (R2)
        Assert.Equal("Northwind", reloaded.Tabs[0].Session.Binding!.Database);
        Assert.True(reloaded.Tabs[0].Session.NeedsReattach);
        Assert.False(reloaded.Tabs[0].Session.IsConnected);   // nothing connects at load (FR-009)
        Assert.Same(reloaded.Tabs[0].Session, sql.ActiveSession);
    }

    [Fact]
    public async Task History_opens_a_query_in_an_empty_unmodified_active_tab_and_in_a_new_tab_otherwise()
    {
        var (tabs, _) = Open();
        await tabs.LoadAsync();
        var empty = tabs.Active!;

        var reused = await tabs.OpenInNewTabAsync("SELECT 'from history'", "orders audit", autorun: true);
        Assert.Same(empty, reused);
        Assert.Equal("orders audit", reused!.Name);
        Assert.Equal("SELECT 'from history'", reused.Text);
        Assert.True(reused.AutorunPending);
        Assert.False(reused.IsModified);

        var other = await tabs.OpenInNewTabAsync("SELECT 2", null, autorun: false);
        Assert.NotSame(reused, other);
        Assert.Equal(2, tabs.Tabs.Count);
        Assert.Same(other, tabs.Active);
        Assert.Equal("SQLQuery2", other!.Name);
    }

    // ── T015: the first load after the upgrade ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_single_document_of_before_tabs_becomes_tab_1_once()
    {
        var legacy = new EditorSessionStore(_db);
        await legacy.SaveAsync(new EditorSessionRecord
        {
            DocumentText = "SELECT * FROM dbo.Orders",
            CursorOffset = 9,
            SessionKey = "history-key-1",
            DocumentName = "orders-audit",
            IsModified = true,
            NextDefaultNumber = 5,
        });
        await legacy.FlushAsync();

        var (tabs, _) = Open();
        await tabs.LoadAsync();

        var tab = Assert.Single(tabs.Tabs);
        Assert.Equal("orders-audit", tab.Name);
        Assert.Equal("SELECT * FROM dbo.Orders", tab.Text);
        Assert.Equal(9, tab.CursorOffset);
        Assert.True(tab.IsModified);
        Assert.Equal("history-key-1", tab.Document.SessionKey);   // History keeps grouping the same session
        Assert.Null(await _db.GetAsync(StoreNames.EditorSession, "current"));   // moved, not copied

        var next = await tabs.NewAsync(tab);
        Assert.Equal("SQLQuery5", next!.Name);   // the counter carried over

        var (again, _) = Open();
        await again.LoadAsync();
        Assert.Equal(2, again.Tabs.Count);   // the migration ran once
    }

    [Fact]
    public async Task A_legacy_document_without_a_name_takes_the_next_default_name()
    {
        var legacy = new EditorSessionStore(_db);
        await legacy.SaveAsync(new EditorSessionRecord { DocumentText = "SELECT 1", NextDefaultNumber = 3 });
        await legacy.FlushAsync();

        var (tabs, _) = Open();
        await tabs.LoadAsync();

        Assert.Equal("SQLQuery3", Assert.Single(tabs.Tabs).Name);
        Assert.Equal("SQLQuery4", (await tabs.NewAsync(tabs.Active))!.Name);
    }

    // ── Review of US1: two windows of the app over one browser profile ─────────────────────────────────────────────

    /// <summary>
    /// Web Locks across windows: a lease one window holds is refused to the others until it is released (or its page unloads).
    /// Each window also has its sessionStorage, which outlives its reloads. <see cref="CannotTell"/>: a browser with no way to
    /// know (no Web Locks, no BroadcastChannel).
    /// </summary>
    private sealed class SharedLeases
    {
        private readonly Dictionary<string, string> _owners = new();
        private readonly Dictionary<string, Dictionary<string, string>> _storage = new();

        public bool CannotTell { get; set; }

        public ISessionLeases For(string window) => new Window(this, window);

        /// <summary>The window's page unloads (a reload, or the window closes): the browser drops its leases, not its storage.</summary>
        public void Unload(string window)
        {
            lock (_owners)
            {
                foreach (var id in _owners.Where(o => o.Value == window).Select(o => o.Key).ToList()) _owners.Remove(id);
            }
        }

        public bool Holds(string window, string sessionId)
        {
            lock (_owners) return _owners.TryGetValue(sessionId, out var owner) && owner == window;
        }

        private sealed class Window : ISessionLeases
        {
            private readonly SharedLeases _all;
            private readonly string _me;

            public Window(SharedLeases all, string me) { _all = all; _me = me; }

            private Dictionary<string, string> Storage
            {
                get
                {
                    lock (_all._storage)
                    {
                        if (!_all._storage.TryGetValue(_me, out var s)) _all._storage[_me] = s = new();
                        return s;
                    }
                }
            }

            public Task<LeaseClaim> ClaimAsync(string sessionId, bool fresh = false)
            {
                if (_all.CannotTell) return Task.FromResult(LeaseClaim.Unavailable);
                lock (_all._owners)
                {
                    if (_all._owners.TryGetValue(sessionId, out var owner)) return Task.FromResult(owner == _me ? LeaseClaim.Held : LeaseClaim.Refused);
                    _all._owners[sessionId] = _me;
                    return Task.FromResult(LeaseClaim.Held);
                }
            }

            public Task ReleaseAsync(string sessionId)
            {
                lock (_all._owners)
                {
                    if (_all._owners.TryGetValue(sessionId, out var owner) && owner == _me) _all._owners.Remove(sessionId);
                }
                return Task.CompletedTask;
            }

            public Task<string?> WindowSessionAsync(string tabId) =>
                Task.FromResult(Storage.TryGetValue(tabId, out var id) ? id : null);

            public Task RememberWindowSessionAsync(string tabId, string sessionId)
            {
                Storage[tabId] = sessionId;
                return Task.CompletedTask;
            }

            public Task ForgetWindowSessionAsync(string tabId)
            {
                Storage.Remove(tabId);
                return Task.CompletedTask;
            }
        }
    }

    // A window: its own WASM runtime (tabs, connection service, store object) over the profile's one IndexedDB.
    private (QueryTabs Tabs, EditorSessionStore Store) OpenWindow(ISessionLeases leases)
    {
        var store = new EditorSessionStore(_db);
        var sql = new SqlConnectionService(new FakeBridge(), new FakeDiagnosticsRingBuffer());
        return (new QueryTabs(store, sql, _dialogs, _notifications, new NoDownloads(), leases: leases), store);
    }

    [Fact]
    public async Task A_second_window_never_takes_the_engine_session_a_live_window_holds()
    {
        var leases = new SharedLeases();
        var (first, _) = OpenWindow(leases.For("first"));
        await first.LoadAsync();
        var owned = first.Active!.Session.SessionId;

        var (second, secondStore) = OpenWindow(leases.For("second"));
        await second.LoadAsync();
        var mirror = Assert.Single(second.Tabs);
        Assert.Equal(first.Active.Id, mirror.Id);
        Assert.NotEqual(owned, mirror.Session.SessionId);   // a session of its own: nothing is sent for the first window's

        mirror.Text = "SELECT 'second window';";
        await second.PersistAsync(mirror);
        await secondStore.FlushAsync();
        Assert.Equal(owned, (await new EditorSessionStore(_db).RestoreTabAsync(mirror.Id))!.EngineSessionId);   // kept for its owner

        // The first window is gone (the browser dropped its leases): a reload takes the session back, as R2 wants.
        await leases.For("first").ReleaseAsync(owned);
        var (reloaded, _) = OpenWindow(leases.For("reloaded"));
        await reloaded.LoadAsync();
        Assert.Equal(owned, Assert.Single(reloaded.Tabs).Session.SessionId);
    }

    // ── Review of US1, second round ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_window_that_reloads_while_another_holds_a_tab_takes_its_own_session_for_it_back()
    {
        var leases = new SharedLeases();
        var (first, _) = OpenWindow(leases.For("first"));
        await first.LoadAsync();
        var stored = first.Active!.Session.SessionId;
        var (second, _) = OpenWindow(leases.For("second"));
        await second.LoadAsync();
        var own = Assert.Single(second.Tabs).Session.SessionId;
        Assert.NotEqual(stored, own);

        leases.Unload("second");                         // the second window reloads; the first still holds the stored session
        var (reloaded, _) = OpenWindow(leases.For("second"));
        await reloaded.LoadAsync();

        // Its own session again, not a new one each reload: the reattach releases it (R2), so nothing is left open on the engine.
        Assert.Equal(own, Assert.Single(reloaded.Tabs).Session.SessionId);
    }

    [Fact]
    public async Task Once_the_other_window_is_gone_a_reload_takes_the_stored_session_back_and_lets_its_own_go()
    {
        var leases = new SharedLeases();
        var (first, _) = OpenWindow(leases.For("first"));
        await first.LoadAsync();
        var stored = first.Active!.Session.SessionId;
        var (second, _) = OpenWindow(leases.For("second"));
        await second.LoadAsync();
        var own = Assert.Single(second.Tabs).Session.SessionId;

        leases.Unload("first");    // the first window closes
        leases.Unload("second");   // the second reloads
        var (reloaded, _) = OpenWindow(leases.For("second"));
        await reloaded.LoadAsync();

        var tab = Assert.Single(reloaded.Tabs);
        Assert.Equal(stored, tab.Session.SessionId);
        Assert.Null(await leases.For("second").WindowSessionAsync(tab.Id));   // forgotten
        Assert.False(leases.Holds("second", own));                             // and given up (its release is sent)
    }

    [Fact]
    public async Task A_browser_that_cannot_tell_shares_no_stored_session_and_each_window_keeps_its_own_across_reloads()
    {
        var leases = new SharedLeases { CannotTell = true };
        var (first, _) = OpenWindow(leases.For("first"));
        await first.LoadAsync();
        var tab1 = first.Active!.Session.SessionId;
        var tab2 = (await first.NewAsync(first.Active))!.Session.SessionId;

        var (second, _) = OpenWindow(leases.For("second"));
        await second.LoadAsync();
        Assert.Equal(2, second.Tabs.Count);
        Assert.DoesNotContain(second.Tabs, t => t.Session.SessionId == tab1 || t.Session.SessionId == tab2);

        var (firstAgain, _) = OpenWindow(leases.For("first"));   // the first window reloads
        await firstAgain.LoadAsync();
        Assert.Contains(firstAgain.Tabs, t => t.Session.SessionId == tab1);
        Assert.Contains(firstAgain.Tabs, t => t.Session.SessionId == tab2);
    }

    /// <summary>Delegates to a real store; right after this window's tab 1 is written, another window finishes the move first.</summary>
    private sealed class AnotherWindowMovesFirst : IEditorSessionStore
    {
        private readonly EditorSessionStore _inner;
        private readonly Func<Task> _otherWindow;
        private bool _done;
        public AnotherWindowMovesFirst(EditorSessionStore inner, Func<Task> otherWindow) { _inner = inner; _otherWindow = otherWindow; }
        public Task SaveAsync(EditorSessionRecord record) => _inner.SaveAsync(record);
        public async Task FlushAsync()
        {
            await _inner.FlushAsync();
            if (_done) return;
            _done = true;
            await _otherWindow();
        }
        public Task<EditorSessionRecord?> RestoreAsync() => _inner.RestoreAsync();
        public Task ClearAsync() => _inner.ClearAsync();
        public Task SaveTabAsync(string tabId, EditorSessionRecord record) => _inner.SaveTabAsync(tabId, record);
        public Task<EditorSessionRecord?> RestoreTabAsync(string tabId) => _inner.RestoreTabAsync(tabId);
        public Task DeleteTabAsync(string tabId) => _inner.DeleteTabAsync(tabId);
        public Task SaveSetAsync(EditorTabSetRecord set) => _inner.SaveSetAsync(set);
        public Task<bool> CreateSetAsync(EditorTabSetRecord set) => _inner.CreateSetAsync(set);
        public Task<EditorTabSetRecord?> RestoreSetAsync() => _inner.RestoreSetAsync();
        public Task ClearTabsAsync() => _inner.ClearTabsAsync();
        public Task<EditorSessionRecord?> RestoreLegacyAsync() => _inner.RestoreLegacyAsync();
    }

    [Fact]
    public async Task Two_windows_moving_the_single_document_at_once_leave_one_set_and_no_stray_tab()
    {
        var legacy = new EditorSessionStore(_db);
        await legacy.SaveAsync(new EditorSessionRecord { DocumentName = "orders-audit", DocumentText = "SELECT 'before tabs';", IsModified = true, SessionKey = "k" });
        await legacy.FlushAsync();
        EditorTabSetRecord? firstSet = null;
        var second = new AnotherWindowMovesFirst(new EditorSessionStore(_db),
            async () => firstSet = await EditorSessionKeys.MigrateLegacyAsync(new EditorSessionStore(_db)));

        var secondSet = await EditorSessionKeys.MigrateLegacyAsync(second);

        var reader = new EditorSessionStore(_db);
        var stored = (await reader.RestoreSetAsync())!;
        var only = Assert.Single(stored.Order);
        Assert.Equal(firstSet!.Order, stored.Order);   // the window that moved first stands
        Assert.Equal(stored.Order, secondSet.Order);   // and the other uses its set
        Assert.Equal("SELECT 'before tabs';", (await reader.RestoreTabAsync(only))!.DocumentText);
        Assert.Equal(2, _db.Count(StoreNames.EditorTabs));   // the set and that one tab: the second window's copy is gone
    }

    [Fact]
    public async Task An_older_build_window_that_keeps_typing_updates_one_copy_instead_of_adding_one_each_load()
    {
        var legacy = new EditorSessionStore(_db);
        await legacy.SaveAsync(new EditorSessionRecord { DocumentName = "orders", DocumentText = "SELECT 0;", SessionKey = "k" });
        await legacy.FlushAsync();
        var (moved, _) = Open();
        await moved.LoadAsync();   // tab 1 holds "SELECT 0;" under the key k

        async Task OldWindowTypesAsync(string text)
        {
            var old = new EditorSessionStore(_db);
            await old.SaveAsync(new EditorSessionRecord { DocumentName = "orders", DocumentText = text, IsModified = true, SessionKey = "k" });
            await old.FlushAsync();
        }
        await OldWindowTypesAsync("SELECT 1;");
        var (first, _) = Open();
        await first.LoadAsync();
        await OldWindowTypesAsync("SELECT 2;");
        var (second, _) = Open();
        await second.LoadAsync();

        Assert.Equal(2, second.Tabs.Count);
        var copy = Assert.Single(second.Tabs, t => t.ImportedFromKey == "k");
        Assert.Equal("SELECT 2;", copy.Text);
        Assert.True(copy.IsModified);
    }

    [Fact]
    public async Task A_copy_edited_here_is_not_overwritten_by_a_later_copy_from_the_older_build()
    {
        var legacy = new EditorSessionStore(_db);
        await legacy.SaveAsync(new EditorSessionRecord { DocumentName = "orders", DocumentText = "SELECT 0;", SessionKey = "k" });
        await legacy.FlushAsync();
        var (moved, _) = Open();
        await moved.LoadAsync();

        async Task OldWindowTypesAsync(string text)
        {
            var old = new EditorSessionStore(_db);
            await old.SaveAsync(new EditorSessionRecord { DocumentName = "orders", DocumentText = text, IsModified = true, SessionKey = "k" });
            await old.FlushAsync();
        }
        await OldWindowTypesAsync("SELECT 1;");
        var (first, firstStore) = OpenWindow(new SharedLeases().For("new build"));
        await first.LoadAsync();
        var copy = Assert.Single(first.Tabs, t => t.ImportedFromKey == "k");
        copy.Text = "SELECT 1; -- edited in the new build";
        await first.PersistAsync(copy);
        await firstStore.FlushAsync();

        await OldWindowTypesAsync("SELECT 2;");
        var (second, _) = Open();
        await second.LoadAsync();

        Assert.Contains(second.Tabs, t => t.Text == "SELECT 1; -- edited in the new build");   // the user's edit is kept
        var newer = Assert.Single(second.Tabs, t => t.ImportedFromKey == "k");                   // the newer copy is a tab of its own
        Assert.Equal("SELECT 2;", newer.Text);
        Assert.Equal(3, second.Tabs.Count);
    }

    [Fact]
    public async Task A_new_tab_is_not_on_the_engine_until_it_connects_and_a_restored_one_may_be()
    {
        var (tabs, _) = Open();
        await tabs.LoadAsync();
        var fresh = (await tabs.NewAsync(tabs.Active))!;
        Assert.False(fresh.Session.MayBeOnEngine);   // nothing to release or warn about when it closes

        var (reloaded, _) = Open();
        await reloaded.LoadAsync();
        Assert.All(reloaded.Tabs, t => Assert.True(t.Session.MayBeOnEngine));   // an earlier page may have left them on the engine
    }

    [Fact]
    public async Task A_tab_the_user_disconnected_comes_back_without_a_connection_after_a_reload()
    {
        var (tabs, sql) = Open();
        await tabs.LoadAsync();
        tabs.Active!.Session.Restore(Northwind());

        await sql.DisconnectSessionAsync(tabs.Active.Session, release: false, CancellationToken.None);

        var (reloaded, _) = Open();   // at once: the Disconnect is not left in a save's debounce
        await reloaded.LoadAsync();
        var session = reloaded.Active!.Session;
        Assert.Null(session.Binding);
        Assert.True(session.DisconnectedByUser);
        Assert.False(session.NeedsReattach);
    }

    [Theory]
    [InlineData(20, 0, "You have 20 query tabs open; close one to open another")]
    [InlineData(30, 0, "You have 30 query tabs open; the limit is 20, so close 11 to open another")]
    [InlineData(19, 1, "You have 19 query tabs open and 1 still closing; the limit is 20, so close one to open another")]
    public void The_limit_message_says_how_many_tabs_to_close(int open, int closing, string expected)
    {
        Assert.Equal(expected, QueryTabs.LimitMessage(open, closing));
    }

    [Fact]
    public async Task Two_windows_keep_each_others_new_tabs_in_the_set()
    {
        var leases = new SharedLeases();
        var (a, storeA) = OpenWindow(leases.For("a"));
        await a.LoadAsync();
        var (b, _) = OpenWindow(leases.For("b"));
        await b.LoadAsync();

        var x = (await a.NewAsync(a.Active))!;   // window A: [T1, X]
        var y = (await b.NewAsync(b.Active))!;   // window B never saw X
        x.Text = "SELECT 'typed in X';";
        await a.PersistAsync(x);
        await storeA.FlushAsync();

        var reader = new EditorSessionStore(_db);
        var set = (await reader.RestoreSetAsync())!;
        Assert.Contains(x.Id, set.Order);
        Assert.Contains(y.Id, set.Order);
        Assert.Equal("SELECT 'typed in X';", (await reader.RestoreTabAsync(x.Id))!.DocumentText);

        var (reloaded, _) = OpenWindow(leases.For("reloaded"));
        await reloaded.LoadAsync();
        Assert.Contains(reloaded.Tabs, t => t.Id == x.Id);
        Assert.Contains(reloaded.Tabs, t => t.Id == y.Id);
    }

    [Fact]
    public async Task A_tab_closed_in_one_window_is_not_put_back_by_the_other()
    {
        var leases = new SharedLeases();
        var (a, _) = OpenWindow(leases.For("a"));
        await a.LoadAsync();
        var x = (await a.NewAsync(a.Active))!;
        var (b, storeB) = OpenWindow(leases.For("b"));
        await b.LoadAsync();                       // B knows X
        await a.CloseAsync(x);

        var late = b.Tabs.Single(t => t.Id == x.Id);
        late.Text = "typed in the other window after the close";
        await b.PersistAsync(late);
        await b.NewAsync(b.Active);                // a set write from B
        await storeB.FlushAsync();

        var reader = new EditorSessionStore(_db);
        Assert.DoesNotContain(x.Id, (await reader.RestoreSetAsync())!.Order);
        Assert.Null(await reader.RestoreTabAsync(x.Id));
    }

    [Fact]
    public async Task A_History_re_execute_still_waiting_does_not_survive_a_reload()
    {
        var (tabs, _) = Open();
        await tabs.LoadAsync();
        var tab = (await tabs.OpenInNewTabAsync("SELECT 1;", null, autorun: true))!;
        Assert.True(tab.AutorunPending);
        Assert.Equal("SELECT 1;", tab.AutorunSql);

        var (reloaded, _) = Open();
        await reloaded.LoadAsync();

        Assert.All(reloaded.Tabs, t => Assert.False(t.AutorunPending));
    }

    [Fact]
    public async Task A_single_document_an_older_build_saved_after_the_move_comes_back_as_a_tab()
    {
        var (tabs, _) = Open();
        await tabs.LoadAsync();                    // the move to tabs has happened
        var oldBuild = new EditorSessionStore(_db);
        await oldBuild.SaveAsync(new EditorSessionRecord { DocumentName = "old-window", DocumentText = "SELECT 'typed in the old window';", IsModified = true, SessionKey = "old-key" });
        await oldBuild.FlushAsync();

        var (reloaded, _) = Open();
        await reloaded.LoadAsync();

        var recovered = Assert.Single(reloaded.Tabs, t => t.Text == "SELECT 'typed in the old window';");
        Assert.Equal("old-window", recovered.Name);
        Assert.True(recovered.IsModified);
        Assert.Null(await new EditorSessionStore(_db).RestoreLegacyAsync());
        var (again, _) = Open();
        await again.LoadAsync();
        Assert.Single(again.Tabs, t => t.Text == "SELECT 'typed in the old window';");   // once, not on every load
    }

    /// <summary>Delegates to a real store; the first flush throws (the page closed, or the write failed, half way).</summary>
    private sealed class FlushFailsOnce : IEditorSessionStore
    {
        private readonly EditorSessionStore _inner;
        private bool _failed;
        public FlushFailsOnce(EditorSessionStore inner) => _inner = inner;
        public Task SaveAsync(EditorSessionRecord record) => _inner.SaveAsync(record);
        public Task FlushAsync()
        {
            if (_failed) return _inner.FlushAsync();
            _failed = true;
            throw new InvalidOperationException("The page went away.");
        }
        public Task<EditorSessionRecord?> RestoreAsync() => _inner.RestoreAsync();
        public Task ClearAsync() => _inner.ClearAsync();
        public Task SaveTabAsync(string tabId, EditorSessionRecord record) => _inner.SaveTabAsync(tabId, record);
        public Task<EditorSessionRecord?> RestoreTabAsync(string tabId) => _inner.RestoreTabAsync(tabId);
        public Task DeleteTabAsync(string tabId) => _inner.DeleteTabAsync(tabId);
        public Task SaveSetAsync(EditorTabSetRecord set) => _inner.SaveSetAsync(set);
        public Task<EditorTabSetRecord?> RestoreSetAsync() => _inner.RestoreSetAsync();
        public Task ClearTabsAsync() => _inner.ClearTabsAsync();
        public Task<EditorSessionRecord?> RestoreLegacyAsync() => _inner.RestoreLegacyAsync();
    }

    [Fact]
    public async Task A_move_to_tabs_that_stops_half_way_is_done_again_from_the_single_document()
    {
        var legacy = new EditorSessionStore(_db);
        await legacy.SaveAsync(new EditorSessionRecord { DocumentName = "orders-audit", DocumentText = "SELECT 'before tabs';", IsModified = true, SessionKey = "k" });
        await legacy.FlushAsync();
        var sql = new SqlConnectionService(new FakeBridge(), new FakeDiagnosticsRingBuffer());
        var interrupted = new QueryTabs(new FlushFailsOnce(new EditorSessionStore(_db)), sql, _dialogs, _notifications, new NoDownloads());
        await Assert.ThrowsAsync<InvalidOperationException>(() => interrupted.LoadAsync());

        var (tabs, _) = Open();
        await tabs.LoadAsync();

        var tab = Assert.Single(tabs.Tabs);
        Assert.Equal("orders-audit", tab.Name);
        Assert.Equal("SELECT 'before tabs';", tab.Text);
    }
}
