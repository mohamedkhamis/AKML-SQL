using System;
using System.Threading.Tasks;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Cache;

/// <summary>
/// Spec 021 (web edition) -- M5 task T110 + T112. LRU eviction:
/// on a simulated QuotaExceededError, the evictor drops the oldest entries by
/// LastUsedAt until the retry-write callback succeeds, fires a single
/// EvictionOccurred event, and writes diagnostics rows.
/// </summary>
public sealed class SchemaCacheEvictorTests
{
    private static (ISchemaCacheStore cache, IDiagnosticsRingBuffer diag, SchemaCacheEvictor evictor) Build()
    {
        var db = new InMemoryIndexedDbAdapter();
        var cache = new SchemaCacheStore(db);
        var diag = new DiagnosticsRingBuffer(db);
        return (cache, diag, new SchemaCacheEvictor(cache, diag));
    }

    [Fact]
    public async Task EvictAndRetryAsync_no_op_when_retry_succeeds_immediately()
    {
        var (_, _, evictor) = Build();
        var evicted = await evictor.EvictAndRetryAsync(() => Task.CompletedTask);
        Assert.Equal(0, evicted);
    }

    [Fact]
    public async Task EvictAndRetryAsync_evicts_oldest_first()
    {
        var (cache, _, evictor) = Build();
        await cache.SetAsync(new SchemaSnapshot { ServerCanonicalIdentity = "s", DatabaseName = "oldest" });
        await Task.Delay(2);
        await cache.SetAsync(new SchemaSnapshot { ServerCanonicalIdentity = "s", DatabaseName = "middle" });
        await Task.Delay(2);
        await cache.SetAsync(new SchemaSnapshot { ServerCanonicalIdentity = "s", DatabaseName = "newest" });

        // Fail the first two retries, then succeed -- this should evict 2 entries.
        int attempts = 0;
        await evictor.EvictAndRetryAsync(() =>
        {
            attempts++;
            if (attempts <= 2) throw new InvalidOperationException("QuotaExceededError");
            return Task.CompletedTask;
        });

        var remaining = await cache.ListAsync();
        Assert.Single(remaining);
        Assert.Equal("newest", remaining[0].DatabaseName);
    }

    [Fact]
    public async Task EvictAndRetryAsync_emits_a_single_eviction_notice()
    {
        var (cache, _, evictor) = Build();
        await cache.SetAsync(new SchemaSnapshot { ServerCanonicalIdentity = "s", DatabaseName = "old1" });
        await Task.Delay(2);
        await cache.SetAsync(new SchemaSnapshot { ServerCanonicalIdentity = "s", DatabaseName = "old2" });

        var notices = new System.Collections.Generic.List<EvictionNotice>();
        evictor.EvictionOccurred += notices.Add;

        int attempts = 0;
        await evictor.EvictAndRetryAsync(() =>
        {
            attempts++;
            if (attempts == 1) throw new InvalidOperationException("QuotaExceededError");
            return Task.CompletedTask;
        });

        Assert.Single(notices);
        Assert.Equal(1, notices[0].EntriesEvicted);
        // First evicted is the oldest -- "old1" (sorted by LastUsedAt ascending).
        Assert.Equal("old1", notices[0].LastEvictedDatabaseName);
    }

    [Fact]
    public async Task EvictAndRetryAsync_stops_at_empty_cache()
    {
        var (_, _, evictor) = Build();   // no entries to evict

        // Retry always fails -- the loop must stop at the first iteration where
        // ListAsync returns empty.
        int attempts = 0;
        await evictor.EvictAndRetryAsync(() =>
        {
            attempts++;
            throw new InvalidOperationException("QuotaExceededError");
        });

        Assert.Equal(1, attempts);   // only the initial attempt; the loop never restarts.
    }

    [Fact]
    public async Task EvictAndRetryAsync_does_not_evict_when_error_is_not_quota()
    {
        var (cache, _, evictor) = Build();
        await cache.SetAsync(new SchemaSnapshot { ServerCanonicalIdentity = "s", DatabaseName = "keep" });

        // A different exception type should NOT trigger eviction -- the evictor
        // rethrows or treats it as a normal failure.
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            evictor.EvictAndRetryAsync(() => throw new NotSupportedException("unrelated")));

        // Entry preserved.
        var remaining = await cache.ListAsync();
        Assert.Single(remaining);
    }
}
