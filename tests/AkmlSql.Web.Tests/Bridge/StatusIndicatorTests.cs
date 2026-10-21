using System;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Bridge;

/// <summary>
/// Spec 027 (M5 offline closure) T029 (US5). The status indicator derives a cache-aware
/// availability state (Live / Cached / Offline / Disconnected) from BOTH the bridge state
/// and whether a schema snapshot is cached for the active (server, db). Reuses the shared
/// FakeEngineBridge / FakeSchemaSync fakes + real SchemaCacheStore / ConnectionStore over the
/// in-memory IndexedDB adapter.
/// </summary>
public sealed class StatusIndicatorTests : BunitContext
{
    private const string Host = "127.0.0.1";
    private const int Portn = 5081;
    private const string Server = "127.0.0.1:5081";

    private readonly FakeEngineBridge _bridge = new();
    private readonly FakeSchemaSync _sync = new();
    private readonly SchemaCacheStore _cache;
    private readonly ConnectionStore _connections;

    public StatusIndicatorTests()
    {
        var adapter = new InMemoryIndexedDbAdapter();
        _cache = new SchemaCacheStore(adapter);
        _connections = new ConnectionStore(adapter);

        Services.AddSingleton<IEngineBridge>(_bridge);
        Services.AddSingleton<ISchemaSync>(_sync);
        Services.AddSingleton<ISchemaCacheStore>(_cache);
        Services.AddSingleton<IConnectionStore>(_connections);
        // Phase 4: StatusBar now hosts the SQL-connection indicator (the third connection-manager
        // entry point). A default SqlConnectionService reports IsConnected=false (renders "Connect")
        // — these tests assert the bridge/cache PILL, not the connection chip.
        Services.AddSingleton<ISqlConnectionService>(new SqlConnectionService(_bridge, new NoopDiagnostics()));
        // Spec 032 (FR-032): StatusBar injects the saved-SQL-connection store for the boot-time
        // auto-restore; an empty store means "nothing to restore" in these tests.
        Services.AddSingleton<ISavedSqlConnectionStore>(new SavedSqlConnectionStore(adapter));
        Services.AddSingleton<IConnectionManagerController>(new ConnectionManagerController());
        // The status bar now also reports WHY the bridge is down and offers a retry. These tests
        // are about the availability pill, so a stub that never reports a failure keeps them
        // focused — the auto-connect behaviour has its own tests.
        Services.AddSingleton<IEngineAutoConnect>(new NoFailureAutoConnect());
    }

    private sealed class NoopDiagnostics : IDiagnosticsRingBuffer
    {
        public void Log(DiagnosticLevel level, string source, string message, object? data = null) { }
        public System.Collections.Generic.IReadOnlyList<DiagnosticEntry> Snapshot() => Array.Empty<DiagnosticEntry>();
        public void Clear() { }
        public Task FlushAsync() => Task.CompletedTask;
        public Task RestoreAsync() => Task.CompletedTask;
    }

    private async Task SeedActiveConnectionAsync()
    {
        var conn = new EngineConnection { Id = "c1", Host = Host, Port = Portn };
        await _connections.AddAsync(conn);
        await _connections.SetActiveIdAsync("c1");
    }

    // Spec 041 (T182): the cache is probed under the SQL connection's (server, database) — the key the schema sync
    // writes under — not the engine pairing's host:port.
    private const string SqlServer = "localhost";
    private const string SqlDatabase = "Sales";

    private async Task ConnectSqlAsync()
    {
        var before = _bridge.State;
        _bridge.SetState(BridgeState.Open);
        var (ok, error) = await Services.GetRequiredService<ISqlConnectionService>().ConnectAsync(SqlServer, SqlDatabase, true, null, null, CancellationToken.None);
        Assert.True(ok, error);
        _bridge.SetState(before);
    }

    private async Task SeedCacheAsync()
    {
        await _cache.SetAsync(new SchemaSnapshot
        {
            // Re-review REV-01: the cache key is the server scoped to the engine that reaches it (this bridge has no URL).
            ServerCanonicalIdentity = SqlConnectionService.ScopeToEngine(SqlServer, null),
            DatabaseName = SqlDatabase,
            PhaseA = new byte[] { 1, 2, 3 },   // non-empty ⇒ "cache present"
            Checksum = "x",
        });
    }

    [Fact]
    public async Task Open_without_sql_session_shows_BridgeOnly()
    {
        // Spec 032 FR-032 (campaign finding 5): after a reload the bridge auto-reconnects but
        // the SQL session is gone — the pill must NOT claim full "Live" IntelliSense then.
        await SeedActiveConnectionAsync();
        _bridge.SetState(BridgeState.Open);

        var cut = Render<StatusBar>();
        cut.WaitForAssertion(() =>
            Assert.Equal("Live · no SQL", cut.Find("[data-testid='status-pill']").TextContent));
    }

    [Fact]
    public async Task Open_with_sql_session_shows_Live()
    {
        await SeedActiveConnectionAsync();
        _bridge.SetState(BridgeState.Open);
        var sqlConn = (SqlConnectionService)Services.GetRequiredService<ISqlConnectionService>();
        await sqlConn.ConnectAsync("localhost", "Northwind_AutoTest", windowsAuth: true, null, null, default);

        var cut = Render<StatusBar>();
        cut.WaitForAssertion(() =>
            Assert.Equal("Live", cut.Find("[data-testid='status-pill']").TextContent));
    }

    [Fact]
    public async Task Open_autorestores_saved_windows_auth_connection()
    {
        // Spec 032 FR-032 auto-restore: a most-recently-used Windows-auth saved connection
        // reconnects silently on boot, flipping the pill to full Live.
        await SeedActiveConnectionAsync();
        var savedStore = Services.GetRequiredService<ISavedSqlConnectionStore>();
        var saved = new SavedSqlConnection { Name = "dev", Server = "localhost", Database = "Northwind_AutoTest", WindowsAuth = true };
        await savedStore.AddAsync(saved);
        await savedStore.SetActiveIdAsync(saved.Id);
        _bridge.SetState(BridgeState.Open);

        var cut = Render<StatusBar>();
        cut.WaitForAssertion(() =>
            Assert.Equal("Live", cut.Find("[data-testid='status-pill']").TextContent));
    }

    [Fact]
    public async Task Disconnected_with_cache_shows_Cached()
    {
        await SeedActiveConnectionAsync();
        await ConnectSqlAsync();
        await SeedCacheAsync();

        var cut = Render<StatusBar>();
        _bridge.SetState(BridgeState.Disconnected);

        cut.WaitForAssertion(() =>
            Assert.Equal("Cached", cut.Find("[data-testid='status-pill']").TextContent));
    }

    [Fact]
    public async Task Disconnected_without_cache_shows_Offline()
    {
        await SeedActiveConnectionAsync();   // active connection, but NO snapshot cached
        await ConnectSqlAsync();

        var cut = Render<StatusBar>();
        _bridge.SetState(BridgeState.Disconnected);

        cut.WaitForAssertion(() =>
            Assert.Equal("Offline", cut.Find("[data-testid='status-pill']").TextContent));
    }

    [Fact]
    public async Task Reconnecting_with_cache_holds_Cached_no_flicker()
    {
        await SeedActiveConnectionAsync();
        await ConnectSqlAsync();
        await SeedCacheAsync();

        var cut = Render<StatusBar>();
        _bridge.SetState(BridgeState.Reconnecting);

        cut.WaitForAssertion(() =>
            Assert.Equal("Cached", cut.Find("[data-testid='status-pill']").TextContent));
    }

    [Fact]
    public async Task Drift_after_cache_seeded_flips_Offline_to_Cached_in_place()
    {
        await SeedActiveConnectionAsync();
        await ConnectSqlAsync();
        var cut = Render<StatusBar>();
        _bridge.SetState(BridgeState.Disconnected);
        cut.WaitForAssertion(() =>
            Assert.Equal("Offline", cut.Find("[data-testid='status-pill']").TextContent));

        // Cache lands, then a checksum drift fires — the indicator re-probes and flips.
        await SeedCacheAsync();
        await _sync.RaiseChecksumDriftedAsync(new ChecksumDriftNotice(SqlServer, SqlDatabase, "x"));

        cut.WaitForAssertion(() =>
            Assert.Equal("Cached", cut.Find("[data-testid='status-pill']").TextContent));
    }

    // ── Spec 043 (R19, FR-009): the one-time restore after a reload, with query tabs ─────────────────────────────

    private sealed class NoDownloads : IFileDownloader
    {
        public Task DownloadTextAsync(string fileName, string mimeType, string text, bool withBom) => Task.CompletedTask;
    }

    private async Task<SavedSqlConnection> SeedSavedNorthwindAsync()
    {
        var savedStore = Services.GetRequiredService<ISavedSqlConnectionStore>();
        var saved = new SavedSqlConnection { Name = "dev", Server = "localhost", Database = "Northwind_AutoTest", WindowsAuth = true };
        await savedStore.AddAsync(saved);
        await savedStore.SetActiveIdAsync(saved.Id);
        return saved;
    }

    [Fact]
    public async Task Open_reconnects_a_restored_tab_to_its_own_connection_and_not_to_the_saved_one()
    {
        var tabStore = new EditorSessionStore(new InMemoryIndexedDbAdapter());
        await tabStore.SaveTabAsync("t1", new EditorSessionRecord
        {
            TabId = "t1", DocumentName = "SQLQuery1", SessionKey = "k1", EngineSessionId = "tab-session-1",
            Binding = ConnectionBinding.FromTarget(new ConnectionTarget(AkmlSql.Core.DatabaseKind.SqlServer, "localhost", "TabDb", WindowsAuth: true), null),
        });
        await tabStore.SaveSetAsync(new EditorTabSetRecord { Order = new System.Collections.Generic.List<string> { "t1" }, ActiveId = "t1", NextDefaultNumber = 2 });
        await tabStore.FlushAsync();
        Services.AddSingleton<IEditorSessionStore>(tabStore);
        Services.AddSingleton<IQueryTabs>(sp => new QueryTabs(tabStore, sp.GetRequiredService<ISqlConnectionService>(),
            new FakeDialogService(), new FakeNotificationService(), new NoDownloads()));
        await SeedActiveConnectionAsync();
        await SeedSavedNorthwindAsync();
        _bridge.SetState(BridgeState.Open);

        var cut = Render<StatusBar>();

        // Whatever page the reload landed on, the tab comes back on its own connection.
        cut.WaitForAssertion(() => Assert.Contains("TabDb", cut.Find("[data-testid='status-connection']").TextContent));
        Assert.Equal("tab-session-1", Services.GetRequiredService<ISqlConnectionService>().SessionId);
    }

    [Fact]
    public async Task Open_leaves_a_tab_the_user_disconnected_without_a_connection()
    {
        var tabStore = new EditorSessionStore(new InMemoryIndexedDbAdapter());
        await tabStore.SaveTabAsync("t1", new EditorSessionRecord
        {
            TabId = "t1", DocumentName = "SQLQuery1", SessionKey = "k1", EngineSessionId = "tab-session-1", DisconnectedByUser = true,
        });
        await tabStore.SaveSetAsync(new EditorTabSetRecord { Order = new System.Collections.Generic.List<string> { "t1" }, ActiveId = "t1", NextDefaultNumber = 2 });
        await tabStore.FlushAsync();
        Services.AddSingleton<IEditorSessionStore>(tabStore);
        Services.AddSingleton<IQueryTabs>(sp => new QueryTabs(tabStore, sp.GetRequiredService<ISqlConnectionService>(),
            new FakeDialogService(), new FakeNotificationService(), new NoDownloads()));
        await SeedActiveConnectionAsync();
        await SeedSavedNorthwindAsync();
        _bridge.SetState(BridgeState.Open);

        var cut = Render<StatusBar>();
        await Task.Delay(300);

        var tab = Services.GetRequiredService<IQueryTabs>().Active!;
        Assert.False(tab.Session.IsConnected);   // the reload does not undo the user's Disconnect
        Assert.DoesNotContain("Northwind_AutoTest", cut.Find("[data-testid='status-connection']").TextContent);
    }

    [Fact]
    public async Task Open_restores_the_saved_connection_into_the_active_tab_only_when_it_has_none()
    {
        var tabStore = new EditorSessionStore(new InMemoryIndexedDbAdapter());   // a fresh profile: one empty tab, no connection
        Services.AddSingleton<IEditorSessionStore>(tabStore);
        Services.AddSingleton<IQueryTabs>(sp => new QueryTabs(tabStore, sp.GetRequiredService<ISqlConnectionService>(),
            new FakeDialogService(), new FakeNotificationService(), new NoDownloads()));
        await SeedActiveConnectionAsync();
        var saved = await SeedSavedNorthwindAsync();
        _bridge.SetState(BridgeState.Open);

        var cut = Render<StatusBar>();

        cut.WaitForAssertion(() => Assert.Contains("Northwind_AutoTest", cut.Find("[data-testid='status-connection']").TextContent));
        var tab = Services.GetRequiredService<IQueryTabs>().Active!;
        Assert.Equal(saved.Id, tab.Session.Binding!.SavedConnectionId);   // the tab remembers which saved row it came from
        Assert.True(tab.Session.IsConnected);
    }
}

/// <summary>
/// A status bar that never has an engine problem to report, so the availability-pill tests above
/// stay about the pill. The auto-connect loop's own behaviour is covered by
/// <c>EngineAutoConnectTests</c>.
/// </summary>
internal sealed class NoFailureAutoConnect : IEngineAutoConnect
{
    public EngineConnectFailure? LastFailure => null;
    public event Action? FailureChanged { add { } remove { } }
    public Task StartAsync() => Task.CompletedTask;
    public Task RetryNowAsync() => Task.CompletedTask;
}
