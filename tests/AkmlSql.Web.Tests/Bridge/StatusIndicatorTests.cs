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
public sealed class StatusIndicatorTests : TestContext
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

    private async Task SeedCacheAsync()
    {
        await _cache.SetAsync(new SchemaSnapshot
        {
            ServerCanonicalIdentity = Server,
            DatabaseName = "master",
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

        var cut = RenderComponent<StatusBar>();
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

        var cut = RenderComponent<StatusBar>();
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

        var cut = RenderComponent<StatusBar>();
        cut.WaitForAssertion(() =>
            Assert.Equal("Live", cut.Find("[data-testid='status-pill']").TextContent));
    }

    [Fact]
    public async Task Disconnected_with_cache_shows_Cached()
    {
        await SeedActiveConnectionAsync();
        await SeedCacheAsync();

        var cut = RenderComponent<StatusBar>();
        _bridge.SetState(BridgeState.Disconnected);

        cut.WaitForAssertion(() =>
            Assert.Equal("Cached", cut.Find("[data-testid='status-pill']").TextContent));
    }

    [Fact]
    public async Task Disconnected_without_cache_shows_Offline()
    {
        await SeedActiveConnectionAsync();   // active connection, but NO snapshot cached

        var cut = RenderComponent<StatusBar>();
        _bridge.SetState(BridgeState.Disconnected);

        cut.WaitForAssertion(() =>
            Assert.Equal("Offline", cut.Find("[data-testid='status-pill']").TextContent));
    }

    [Fact]
    public async Task Reconnecting_with_cache_holds_Cached_no_flicker()
    {
        await SeedActiveConnectionAsync();
        await SeedCacheAsync();

        var cut = RenderComponent<StatusBar>();
        _bridge.SetState(BridgeState.Reconnecting);

        cut.WaitForAssertion(() =>
            Assert.Equal("Cached", cut.Find("[data-testid='status-pill']").TextContent));
    }

    [Fact]
    public async Task Drift_after_cache_seeded_flips_Offline_to_Cached_in_place()
    {
        await SeedActiveConnectionAsync();
        var cut = RenderComponent<StatusBar>();
        _bridge.SetState(BridgeState.Disconnected);
        cut.WaitForAssertion(() =>
            Assert.Equal("Offline", cut.Find("[data-testid='status-pill']").TextContent));

        // Cache lands, then a checksum drift fires — the indicator re-probes and flips.
        await SeedCacheAsync();
        await _sync.RaiseChecksumDriftedAsync(new ChecksumDriftNotice(Server, "master", "x"));

        cut.WaitForAssertion(() =>
            Assert.Equal("Cached", cut.Find("[data-testid='status-pill']").TextContent));
    }
}
