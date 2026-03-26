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
