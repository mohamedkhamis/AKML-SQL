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
        Assert.False(mgr.TryClaimPopulation("tab1:Northwind"));
        release.SetResult();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (mgr.GetCache("tab1", "Northwind") == null && DateTime.UtcNow < deadline) await Task.Delay(20);

        Assert.NotNull(mgr.GetCache("tab1", "Northwind"));
        Assert.Equal(1, reloads);
        Assert.True(mgr.TryClaimPopulation("tab1:Northwind"));   // released when the reload ended
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
