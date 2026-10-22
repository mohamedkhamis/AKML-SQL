using Xunit;
using AkmlSql.Engine.Schema;

namespace AkmlSql.Engine.Tests.Schema;

public class SchemaCacheManagerTests
{
    // ── GetOrCreateCache ──────────────────────────────────────────────────

    [Fact]
    public void GetOrCreateCache_NewKey_CreatesCache()
    {
        using var mgr = new SchemaCacheManager();

        var cache = mgr.GetOrCreateCache("srv1", "db1");

        Assert.NotNull(cache);
        Assert.Equal("srv1:db1", cache.CacheKey);
    }

    [Fact]
    public void GetOrCreateCache_SameKey_ReturnsSameInstance()
    {
        using var mgr = new SchemaCacheManager();

        var c1 = mgr.GetOrCreateCache("srv", "db");
        var c2 = mgr.GetOrCreateCache("srv", "db");

        Assert.Same(c1, c2);
    }

    // ── GetCache ──────────────────────────────────────────────────────────

    [Fact]
    public void GetCache_AfterGetOrCreate_ReturnsSame()
    {
        using var mgr = new SchemaCacheManager();
        var created = mgr.GetOrCreateCache("srv", "db");

        var retrieved = mgr.GetCache("srv", "db");

        Assert.Same(created, retrieved);
    }

    [Fact]
    public void GetCache_Missing_ReturnsNull()
    {
        using var mgr = new SchemaCacheManager();

        var cache = mgr.GetCache("srv", "never-added");

        Assert.Null(cache);
    }

    // ── EvictLru ──────────────────────────────────────────────────────────

    [Fact]
    public void EvictLru_BelowMax_NoEviction()
    {
        using var mgr = new SchemaCacheManager(maxDatabases: 5);
        mgr.GetOrCreateCache("s", "db1");
        mgr.GetOrCreateCache("s", "db2");

        mgr.EvictLru();

        Assert.Equal(2, mgr.CacheCount);
    }

    [Fact]
    public void EvictLru_AboveMax_EvictsOldest()
    {
        using var mgr = new SchemaCacheManager(maxDatabases: 2);
        var c1 = mgr.GetOrCreateCache("s", "db1");
        c1.LastFullRefresh = DateTime.UtcNow.AddHours(-2); // oldest

        var c2 = mgr.GetOrCreateCache("s", "db2");
        c2.LastFullRefresh = DateTime.UtcNow.AddHours(-1);

        var c3 = mgr.GetOrCreateCache("s", "db3");
        c3.LastFullRefresh = DateTime.UtcNow;

        mgr.EvictLru();

        Assert.Equal(2, mgr.CacheCount);
        // db1 should have been evicted
        Assert.Null(mgr.GetCache("s", "db1"));
    }

    [Fact]
    public void EvictLru_keeps_a_cache_still_in_use_however_old_its_refresh()
    {
        // Caches are per editor tab: the first tab's schema was refreshed first, so opening more
        // tabs evicted it while it was being typed in.
        using var mgr = new SchemaCacheManager(maxDatabases: 2);
        var first = mgr.GetOrCreateCache("tab1", "Northwind");
        first.LastFullRefresh = DateTime.UtcNow.AddHours(-2);
        var second = mgr.GetOrCreateCache("tab2", "Northwind");
        second.LastFullRefresh = DateTime.UtcNow.AddHours(-1);
        second.LastUsedUtc = DateTime.UtcNow.AddHours(-1);
        first.LastUsedUtc = DateTime.UtcNow.AddHours(-1);

        Assert.NotNull(mgr.GetCache("tab1", "Northwind"));   // typed in just now
        var third = mgr.GetOrCreateCache("tab3", "Northwind");
        third.LastFullRefresh = DateTime.UtcNow;

        mgr.EvictLru();

        Assert.NotNull(mgr.GetCache("tab1", "Northwind"));
        Assert.Null(mgr.GetCache("tab2", "Northwind"));
        Assert.NotNull(mgr.GetCache("tab3", "Northwind"));
    }

    [Fact]
    public async Task A_missing_cache_is_reloaded_once_however_often_it_is_asked_for()
    {
        // An open tab whose cache was evicted gets it back on its next lookup.
        using var mgr = new SchemaCacheManager();
        var release = new TaskCompletionSource();
        var reloads = 0;
        mgr.ReloadMissing = async (session, db) =>
        {
            Interlocked.Increment(ref reloads);
            await release.Task;
            mgr.GetOrCreateCache(session, db);
        };

        Assert.Null(mgr.GetCache("tab1", "Northwind"));
        Assert.Null(mgr.GetCache("tab1", "Northwind"));   // while the first reload runs
        Assert.Null(mgr.GetCache("tab1", "Northwind"));
        release.SetResult();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (mgr.GetCache("tab1", "Northwind") == null && DateTime.UtcNow < deadline) await Task.Delay(20);

        var reloaded = mgr.GetCache("tab1", "Northwind");
        Assert.NotNull(reloaded);
        Assert.Equal(1, reloads);
        Assert.True(mgr.TryClaimPopulation(reloaded!));   // the reload marker is not a claim on the cache it created
        mgr.ReleasePopulation(reloaded!);

        // The marker goes when the reload ends (just after it created the cache): a cache that goes missing again is reloaded again.
        mgr.RemoveSession("tab1");
        deadline = DateTime.UtcNow.AddSeconds(5);
        while (Volatile.Read(ref reloads) < 2 && DateTime.UtcNow < deadline)
        {
            mgr.GetCache("tab1", "Northwind");
            await Task.Delay(20);
        }
        Assert.Equal(2, Volatile.Read(ref reloads));
    }

    [Fact]
    public void A_load_of_a_cache_removed_with_its_session_neither_blocks_nor_frees_the_claim_on_its_successor()
    {
        // Spec 043 follow-up (review, round 4): the claim was keyed by "{session}:{db}". A load still running on the cache that
        // RemoveSession discarded (a tab moved to another server) held the key, so the new connect's load of the new cache gave
        // up and the tab had no schema; and the old load's release would have freed the new load's claim.
        using var mgr = new SchemaCacheManager();
        var old = mgr.GetOrCreateCache("tab1", "AppDb");
        Assert.True(mgr.TryClaimPopulation(old));                  // the old server's load is running

        Assert.Equal(1, mgr.RemoveSession("tab1"));
        var fresh = mgr.GetOrCreateCache("tab1", "AppDb");
        Assert.NotSame(old, fresh);

        Assert.True(mgr.TryClaimPopulation(fresh));                // the new connect's load is not stopped
        Assert.False(mgr.TryClaimPopulation(fresh));               // and holds its claim like any other

        mgr.ReleasePopulation(old);                                // the old load ends into the discarded cache
        Assert.True(mgr.IsPopulating(fresh));                      // the new load's claim is untouched
        Assert.False(mgr.TryClaimPopulation(fresh));
        Assert.False(mgr.IsPopulating(old));

        mgr.ReleasePopulation(fresh);
        Assert.False(mgr.IsPopulating(fresh));
    }

    [Fact]
    public async Task A_reload_after_eviction_claims_the_cache_it_loads_and_waits_its_turn_behind_a_running_load()
    {
        // Spec 043 follow-up (review, round 4): the reload took the key's claim in GetCache; with claims on the cache object it
        // claims the cache it is about to load itself, so it never loads a cache a connect is loading, and releases it after.
        var sessions = new AkmlSql.Engine.Server.SessionManager();
        sessions.UpdateSession(new AkmlSql.Core.Ipc.Messages.ConnectionInfo
        {
            SessionId = "tab1", ConnectionString = "Host=x;Database=d;Username=u;Password=p", DatabaseName = "d",
            Kind = (int)AkmlSql.Core.DatabaseKind.PostgreSql,
        });
        using var mgr = new SchemaCacheManager { KindOf = sessions.KindOf };
        var provider = new CountingProvider();
        var ctx = new AkmlSql.Engine.RpcContext
        {
            Sessions = sessions,
            SchemaCache = mgr,
            Logger = Serilog.Log.Logger,
            SettingsLoader = () => new AkmlSql.Core.Config.AppSettings(),
            PostgresSchemaMetadata = provider,
        };
        var cache = mgr.GetOrCreateCache("tab1", "d");
        Assert.True(mgr.TryClaimPopulation(cache));                // a connect's load is running on it

        await AkmlSql.Engine.EngineComposition.ReloadSessionCacheAsync(ctx, "tab1", "d");
        Assert.Equal(0, provider.PhaseACalls);
        Assert.True(mgr.IsPopulating(cache));                      // the reload did not take or free the connect's claim

        mgr.ReleasePopulation(cache);
        await AkmlSql.Engine.EngineComposition.ReloadSessionCacheAsync(ctx, "tab1", "d");
        Assert.Equal(1, provider.PhaseACalls);
        Assert.Equal(PopulationPhase.Complete, cache.Phase);
        Assert.False(mgr.IsPopulating(cache));                     // released when it ended
    }

    private sealed class CountingProvider : ISchemaMetadataProvider
    {
        private int _phaseA;
        public int PhaseACalls => Volatile.Read(ref _phaseA);
        public AkmlSql.Core.DatabaseKind Kind => AkmlSql.Core.DatabaseKind.PostgreSql;

        public Task PopulatePhaseAAsync(DatabaseCache cache, string connectionString, CancellationToken ct)
        {
            Interlocked.Increment(ref _phaseA);
            cache.Phase = PopulationPhase.PhaseA;
            return Task.CompletedTask;
        }

        public Task PopulatePhaseBAsync(DatabaseCache cache, string connectionString, CancellationToken ct)
        {
            cache.Phase = PopulationPhase.Complete;
            return Task.CompletedTask;
        }

        public Task<List<string>> ListDatabasesAsync(string connectionString, CancellationToken ct) => Task.FromResult(new List<string>());
    }

    [Theory]
    [InlineData("tab-gone", "Northwind")]   // the tab closed: no session
    [InlineData("tab1", "master")]          // another database than the session's own
    public async Task Only_an_open_sessions_own_database_is_reloaded(string sessionId, string database)
    {
        var sessions = new AkmlSql.Engine.Server.SessionManager();
        sessions.UpdateSession(new AkmlSql.Core.Ipc.Messages.ConnectionInfo
        {
            SessionId = "tab1",
            ConnectionString = "Data Source=(local);Initial Catalog=Northwind;Integrated Security=true",
            DatabaseName = "Northwind",
        });
        using var mgr = new SchemaCacheManager();
        var ctx = new AkmlSql.Engine.RpcContext
        {
            Sessions = sessions,
            SchemaCache = mgr,
            Logger = Serilog.Log.Logger,
            SettingsLoader = () => new AkmlSql.Core.Config.AppSettings(),
            SchemaMetadata = new SchemaMetadataService(),
        };

        await AkmlSql.Engine.EngineComposition.ReloadSessionCacheAsync(ctx, sessionId, database);

        Assert.Equal(0, mgr.CacheCount);
    }

    // ── CacheCount ────────────────────────────────────────────────────────

    [Fact]
    public void CacheCount_IncrementsOnCreate()
    {
        using var mgr = new SchemaCacheManager();
        Assert.Equal(0, mgr.CacheCount);

        mgr.GetOrCreateCache("srv", "db1");
        Assert.Equal(1, mgr.CacheCount);

        mgr.GetOrCreateCache("srv", "db2");
        Assert.Equal(2, mgr.CacheCount);
    }

    // ── RegisterConnectionString ──────────────────────────────────────────

    [Fact]
    public void RegisterConnectionString_NoThrow()
    {
        using var mgr = new SchemaCacheManager();

        var ex = Record.Exception(() =>
            mgr.RegisterConnectionString("srv", "db", "Server=srv;Database=db;"));

        Assert.Null(ex);
    }

    // ── Dispose ───────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_NoThrow()
    {
        var mgr = new SchemaCacheManager();
        var ex = Record.Exception(() => mgr.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_CalledTwice_NoThrow()
    {
        var mgr = new SchemaCacheManager();
        mgr.Dispose();
        var ex = Record.Exception(() => mgr.Dispose());
        Assert.Null(ex);
    }
}
