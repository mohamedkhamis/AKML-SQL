using AkmlSql.Core;
using AkmlSql.Engine.Schema;
using Xunit;

namespace AkmlSql.Engine.Tests.Schema;

/// <summary>
/// Spec 042 (task T074, FR-004, data-model §6): one session that moves from SQL Server to PostgreSQL, or back, must not see the
/// other kind's database of the same name. The cache key and the file it persists to carry the kind.
/// </summary>
public sealed class SchemaCacheManagerKindTests : IDisposable
{
    private readonly Dictionary<string, DatabaseKind> _kinds = new();
    private readonly SchemaCacheManager _manager;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "akml-cache-kind-" + Guid.NewGuid().ToString("N"));

    public SchemaCacheManagerKindTests()
    {
        _manager = new SchemaCacheManager { KindOf = id => _kinds.TryGetValue(id, out var kind) ? kind : DatabaseKind.SqlServer };
    }

    public void Dispose()
    {
        _manager.Dispose();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void A_SQL_Server_cache_keeps_the_key_it_always_had()
    {
        var cache = _manager.GetOrCreateCache("session-1", "northwind");

        Assert.Equal("session-1:northwind", cache.CacheKey);
        Assert.Equal(DatabaseKind.SqlServer, cache.Kind);
    }

    [Fact]
    public void A_PostgreSQL_cache_has_its_own_key_and_remembers_its_kind()
    {
        _kinds["session-1"] = DatabaseKind.PostgreSql;

        var cache = _manager.GetOrCreateCache("session-1", "northwind");

        Assert.Equal("pg|session-1:northwind", cache.CacheKey);
        Assert.Equal(DatabaseKind.PostgreSql, cache.Kind);
    }

    [Fact]
    public void The_two_northwinds_of_one_session_are_two_caches_and_each_comes_back()
    {
        var sqlServer = _manager.GetOrCreateCache("session-1", "northwind");
        sqlServer.Phase = PopulationPhase.PhaseB;

        _kinds["session-1"] = DatabaseKind.PostgreSql;
        var postgres = _manager.GetOrCreateCache("session-1", "northwind");

        Assert.NotSame(sqlServer, postgres);
        Assert.Equal(PopulationPhase.NotLoaded, postgres.Phase);   // not the SQL Server one's populated state

        _kinds["session-1"] = DatabaseKind.SqlServer;
        Assert.Same(sqlServer, _manager.GetOrCreateCache("session-1", "northwind"));
        Assert.Same(sqlServer, _manager.GetCache("session-1", "northwind"));

        _kinds["session-1"] = DatabaseKind.PostgreSql;
        Assert.Same(postgres, _manager.GetCache("session-1", "northwind"));
    }

    [Fact]
    public void Other_sessions_are_not_affected_by_one_sessions_kind()
    {
        _kinds["a"] = DatabaseKind.PostgreSql;

        Assert.Equal("session-b:db", _manager.GetOrCreateCache("session-b", "db").CacheKey);
        Assert.Equal("pg|a:db", _manager.GetOrCreateCache("a", "db").CacheKey);
    }

    [Fact]
    public async Task The_two_kinds_persist_to_different_files()
    {
        var sqlServer = _manager.GetOrCreateCache("session-1", "northwind");
        _kinds["session-1"] = DatabaseKind.PostgreSql;
        var postgres = _manager.GetOrCreateCache("session-1", "northwind");

        await SchemaCacheManager.SaveCacheAsync(sqlServer, _folder);
        await SchemaCacheManager.SaveCacheAsync(postgres, _folder);

        Assert.Equal(2, Directory.GetFiles(_folder, "*.json").Length);
        var loadedSql = await SchemaCacheManager.LoadCacheAsync(sqlServer.CacheKey, _folder);
        var loadedPg = await SchemaCacheManager.LoadCacheAsync(postgres.CacheKey, _folder);
        Assert.Equal(DatabaseKind.SqlServer, loadedSql!.Kind);
        Assert.Equal(DatabaseKind.PostgreSql, loadedPg!.Kind);
    }

    [Fact]
    public void Without_a_kind_lookup_every_session_is_SQL_Server()
    {
        using var plain = new SchemaCacheManager();

        Assert.Equal("any:db", plain.GetOrCreateCache("any", "db").CacheKey);
    }
}
