using System;
using System.Threading.Tasks;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M5 task T110. LRU eviction (FR-027). Called by
/// <see cref="SchemaCacheStore.SetAsync"/> when an IndexedDB write throws
/// <c>QuotaExceededError</c>: evict the oldest entries by <c>LastUsedAt</c> until
/// the write succeeds, append a <c>changeLog</c> diagnostics row, and emit a
/// single non-blocking notice to the user.
/// </summary>
public interface ISchemaCacheEvictor
{
    /// <summary>
    /// Evict entries in <c>LastUsedAt</c>-ascending order until <paramref name="retryWrite"/>
    /// succeeds or the cache is empty. Returns the count evicted.
    /// </summary>
    Task<int> EvictAndRetryAsync(Func<Task> retryWrite);

    /// <summary>Raised once per bulk-eviction so the UI can display the non-blocking notice.</summary>
    event Action<EvictionNotice>? EvictionOccurred;
}

public sealed record EvictionNotice(int EntriesEvicted, string LastEvictedDatabaseName);

internal sealed class SchemaCacheEvictor : ISchemaCacheEvictor
{
    private readonly ISchemaCacheStore _cache;
    private readonly IDiagnosticsRingBuffer _diagnostics;

    public SchemaCacheEvictor(ISchemaCacheStore cache, IDiagnosticsRingBuffer diagnostics)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public event Action<EvictionNotice>? EvictionOccurred;

    public async Task<int> EvictAndRetryAsync(Func<Task> retryWrite)
    {
        if (retryWrite == null) throw new ArgumentNullException(nameof(retryWrite));

        // Try the write -- if it goes through, no eviction needed.
        try
        {
            await retryWrite().ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex) when (IsQuotaError(ex))
        {
            // Fall through to eviction.
            _diagnostics.Log(DiagnosticLevel.Warn, "schema-cache",
                "QuotaExceeded on write -- evicting LRU entries.");
        }

        int evicted = 0;
        string? lastEvictedDb = null;
        while (true)
        {
            var entries = await _cache.ListAsync().ConfigureAwait(false);
            if (entries.Count == 0) break;

            // ListAsync returns sorted by LastUsedAt ascending, so [0] is the oldest.
            var oldest = entries[0];
            await _cache.RemoveAsync(oldest.ServerCanonicalIdentity, oldest.DatabaseName).ConfigureAwait(false);
            evicted++;
            lastEvictedDb = oldest.DatabaseName;
            _diagnostics.Log(DiagnosticLevel.Info, "schema-cache",
                $"Evicted {oldest.ServerCanonicalIdentity}/{oldest.DatabaseName}");

            try
            {
                await retryWrite().ConfigureAwait(false);
                break;
            }
            catch (Exception ex) when (IsQuotaError(ex))
            {
                continue;   // keep evicting
            }
        }

        if (evicted > 0)
        {
            EvictionOccurred?.Invoke(new EvictionNotice(evicted, lastEvictedDb ?? "(unknown)"));
        }
        return evicted;
    }

    /// <summary>
    /// Detect the browser's QuotaExceededError. In Blazor WASM the JS-interop layer
    /// surfaces these as <see cref="Microsoft.JSInterop.JSException"/> with a message
    /// containing "QuotaExceededError". The in-memory test adapter never throws this,
    /// so the test fixture injects a wrapper adapter that throws on demand.
    /// </summary>
    private static bool IsQuotaError(Exception ex) =>
        ex.Message.Contains("QuotaExceededError", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("quota", StringComparison.OrdinalIgnoreCase);
}
