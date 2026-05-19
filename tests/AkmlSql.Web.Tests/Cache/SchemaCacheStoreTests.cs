using System;
using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Cache;

/// <summary>
/// Spec 021 (web edition) -- M5 task T112. Schema cache:
/// identity-key dedup (two host aliases of one SQL Server share an entry), CRUD,
/// LRU ordering on ListAsync, online -> offline state matrix.
/// </summary>
public sealed class SchemaCacheStoreTests
{
    private static ISchemaCacheStore Build() => new SchemaCacheStore(new InMemoryIndexedDbAdapter());

    [Fact]
    public async Task SetAsync_then_GetAsync_returns_the_snapshot()
    {
        var cache = Build();
        var snap = new SchemaSnapshot
        {
            ServerCanonicalIdentity = "PROD-DB01\\SQL2022",
            DatabaseName = "AdventureWorks",
            PhaseA = new byte[] { 1, 2, 3 },
            Checksum = "abc",
            FetchedAt = DateTimeOffset.UtcNow,
        };
        await cache.SetAsync(snap);

        var fetched = await cache.GetAsync("PROD-DB01\\SQL2022", "AdventureWorks");

        Assert.NotNull(fetched);
        Assert.Equal("AdventureWorks", fetched!.DatabaseName);
        Assert.Equal("abc", fetched.Checksum);
    }

    [Fact]
    public async Task GetAsync_returns_null_for_unknown_key()
    {
        var cache = Build();
        Assert.Null(await cache.GetAsync("server", "missing"));
    }

    [Fact]
    public async Task Composite_key_collapses_two_aliases_into_one_entry()
    {
        // Two snapshots written under the SAME (server, db) but with different hosts
        // in their connection-string -- they MUST share one IndexedDB row.
        var cache = Build();
        var snap1 = new SchemaSnapshot
        {
            ServerCanonicalIdentity = "PROD-DB01\\SQL2022",
            DatabaseName = "AdventureWorks",
            SourceConnectionId = "conn-A",
            Checksum = "first",
        };
        var snap2 = new SchemaSnapshot
        {
            ServerCanonicalIdentity = "PROD-DB01\\SQL2022",
            DatabaseName = "AdventureWorks",
            SourceConnectionId = "conn-B",  // different connection ...
            Checksum = "second",             // ... but same canonical key
        };

        await cache.SetAsync(snap1);
        await cache.SetAsync(snap2);

        var list = await cache.ListAsync();
        Assert.Single(list);
        Assert.Equal("second", list[0].Checksum);     // last write wins
    }

    [Fact]
    public async Task ListAsync_returns_entries_sorted_by_LastUsedAt_ascending()
    {
        var cache = Build();
        await cache.SetAsync(new SchemaSnapshot { ServerCanonicalIdentity = "s", DatabaseName = "db1" });
        await Task.Delay(2);   // separate timestamps
        await cache.SetAsync(new SchemaSnapshot { ServerCanonicalIdentity = "s", DatabaseName = "db2" });
        await Task.Delay(2);
        await cache.SetAsync(new SchemaSnapshot { ServerCanonicalIdentity = "s", DatabaseName = "db3" });

        var list = await cache.ListAsync();

        Assert.Equal(new[] { "db1", "db2", "db3" }, list.Select(s => s.DatabaseName));
    }

    [Fact]
    public async Task TouchAsync_updates_LastUsedAt_without_changing_payload()
    {
        var cache = Build();
        await cache.SetAsync(new SchemaSnapshot
        {
            ServerCanonicalIdentity = "s",
            DatabaseName = "db",
            Checksum = "original",
        });
        var before = (await cache.GetAsync("s", "db"))!.LastUsedAt;
        await Task.Delay(5);

        await cache.TouchAsync("s", "db");

        var after = await cache.GetAsync("s", "db");
        Assert.NotNull(after);
        Assert.True(after!.LastUsedAt > before);
        Assert.Equal("original", after.Checksum);
    }

    [Fact]
    public async Task RemoveAsync_drops_the_entry()
    {
        var cache = Build();
        await cache.SetAsync(new SchemaSnapshot { ServerCanonicalIdentity = "s", DatabaseName = "db" });

        await cache.RemoveAsync("s", "db");

        Assert.Null(await cache.GetAsync("s", "db"));
        Assert.Empty(await cache.ListAsync());
    }

    [Fact]
    public async Task ClearAllAsync_drops_every_entry()
    {
        var cache = Build();
        await cache.SetAsync(new SchemaSnapshot { ServerCanonicalIdentity = "s", DatabaseName = "db1" });
        await cache.SetAsync(new SchemaSnapshot { ServerCanonicalIdentity = "s", DatabaseName = "db2" });

        await cache.ClearAllAsync();

        Assert.Empty(await cache.ListAsync());
    }

    [Fact]
    public async Task EstimatedSizeAsync_returns_non_zero_for_a_known_entry()
    {
        var cache = Build();
        await cache.SetAsync(new SchemaSnapshot
        {
            ServerCanonicalIdentity = "s",
            DatabaseName = "db",
            PhaseA = new byte[1024],
        });

        var size = await cache.EstimatedSizeAsync("s", "db");
        Assert.True(size > 0);
    }

    [Fact]
    public void Composite_key_rejects_empty_server_or_db()
    {
        Assert.Throws<ArgumentException>(() => SchemaSnapshot.MakeKey("", "db"));
        Assert.Throws<ArgumentException>(() => SchemaSnapshot.MakeKey("s", ""));
    }
}
