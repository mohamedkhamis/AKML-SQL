using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
#pragma warning disable IL2026

namespace AkmlSql.Engine.Schema;

/// <summary>
/// Manages the collection of per-database <see cref="DatabaseCache"/> instances across all active connections.
/// Responsibilities:
/// <list type="bullet">
///   <item>Create or retrieve caches keyed by <c>server:database</c></item>
///   <item>LRU eviction when the count exceeds <paramref name="maxDatabases"/></item>
///   <item>Periodic change detection via <see cref="ChangeDetector"/></item>
///   <item>Optional disk persistence of cache snapshots across sessions</item>
/// </list>
/// Thread-safe: uses <see cref="ConcurrentDictionary"/> for lock-free reads.
/// </summary>
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public class SchemaCacheManager(int maxDatabases = 10) : IDisposable
{
    private readonly ConcurrentDictionary<string, DatabaseCache> _caches = new(StringComparer.OrdinalIgnoreCase);
    private readonly ChangeDetector _changeDetector = new();
    private Timer? _periodicRefreshTimer;
    private readonly ConcurrentDictionary<string, string> _connectionStrings = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _populating = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <summary>
    /// Run in the background when <see cref="GetCache"/> finds no cache, at most once at a time per
    /// cache: the engine sets it to load an open tab's evicted cache again. Caches are per tab and
    /// the shell never says a tab closed, so <see cref="EvictLru"/> can drop the cache of a tab that
    /// is still open; without this it stayed without a schema for the rest of its life.
    /// </summary>
    public Func<string, string, Task>? ReloadMissing { get; set; }

    /// <summary>
    /// JSON serializer options for disk cache persistence (T076).
    /// </summary>
    private static readonly JsonSerializerOptions CacheJsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string BuildCacheKey(string serverName, string databaseName)
    {
        return $"{serverName}:{databaseName}";
    }

    /// <summary>
    /// Returns the existing cache for <paramref name="serverName"/>/<paramref name="databaseName"/>,
    /// or creates a new empty one. Callers should follow up with <c>SchemaMetadataService.PopulatePhaseAAsync</c>
    /// when <c>Phase == NotLoaded</c>.
    /// </summary>
    public DatabaseCache GetOrCreateCache(string serverName, string databaseName)
    {
        var key = BuildCacheKey(serverName, databaseName);
        var cache = _caches.GetOrAdd(key, k => new DatabaseCache { CacheKey = k });
        cache.LastUsedUtc = DateTime.UtcNow;
        return cache;
    }

    /// <summary>Returns the cache for the given server/database, or <c>null</c> if it has not been created yet.</summary>
    public DatabaseCache? GetCache(string serverName, string databaseName)
    {
        var key = BuildCacheKey(serverName, databaseName);
        if (_caches.TryGetValue(key, out var cache))
        {
            cache.LastUsedUtc = DateTime.UtcNow;
            return cache;
        }

        var reload = ReloadMissing;
        if (reload != null && TryClaimPopulation(key))
        {
            _ = Task.Run(async () =>
            {
                try { await reload(serverName, databaseName).ConfigureAwait(false); }
                catch (Exception ex) { Log.Warning(ex, "Reloading the schema cache for {Key} failed", key); }
                finally { ReleasePopulation(key); }
            });
        }
        return null;
    }

    /// <summary>
    /// Claims the right to populate the cache <paramref name="cacheKey"/>; false while another
    /// population of it runs. Pair with <see cref="ReleasePopulation"/>.
    /// </summary>
    public bool TryClaimPopulation(string cacheKey) => _populating.TryAdd(cacheKey, 0);

    /// <summary>Ends a population claimed with <see cref="TryClaimPopulation"/>.</summary>
    public void ReleasePopulation(string cacheKey) => _populating.TryRemove(cacheKey, out _);

    /// <summary>
    /// Registers a connection string for a cache key, used by periodic refresh (T079).
    /// </summary>
    public void RegisterConnectionString(string serverName, string databaseName, string connectionString)
    {
        _connectionStrings[BuildCacheKey(serverName, databaseName)] = connectionString;
    }

    /// <summary>
    /// Removes the least-recently-used caches until the count is at or below <c>maxDatabases</c>:
    /// those whose last lookup or refresh, whichever is later, is oldest. Caches are per editor
    /// session, so ordering by refresh alone evicted the first tab's schema once ten more tabs
    /// opened, however much it was still used. Called after each Phase A population.
    /// </summary>
    public void EvictLru()
    {
        if (_caches.Count <= maxDatabases)
        {
            return;
        }

        var oldest = _caches.Values
            .OrderBy(c => c.LastUsedUtc > c.LastFullRefresh ? c.LastUsedUtc : c.LastFullRefresh)
            .Take(_caches.Count - maxDatabases)
            .ToList();

        foreach (var cache in oldest)
        {
            _caches.TryRemove(cache.CacheKey, out _);
            Log.Information("Evicted cache for {Key}", cache.CacheKey);
        }
    }

    public int CacheCount => _caches.Count;

    // ========================================================================
    // T076: Disk cache persistence
    // ========================================================================

    /// <summary>
    /// Serializes a DatabaseCache to a JSON file on disk.
    /// File path: {persistPath}/{cacheKey-hash}.json
    /// </summary>
    public static async Task SaveCacheAsync(DatabaseCache cache, string persistPath)
    {
        try
        {
            Directory.CreateDirectory(persistPath);
            var filePath = GetCacheFilePath(cache.CacheKey, persistPath);
            var tempPath = filePath + ".tmp";

            var json = JsonSerializer.SerializeToUtf8Bytes(cache, CacheJsonOptions);
            await File.WriteAllBytesAsync(tempPath, json);

            // Atomic rename
            File.Move(tempPath, filePath, overwrite: true);
            Log.Debug("Saved cache to disk: {Path}", filePath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to save cache to disk for {Key}", cache.CacheKey);
        }
    }

    /// <summary>
    /// Loads a DatabaseCache from a JSON file on disk.
    /// Returns null if the file does not exist or is corrupted.
    /// </summary>
    public static async Task<DatabaseCache?> LoadCacheAsync(string cacheKey, string persistPath)
    {
        try
        {
            var filePath = GetCacheFilePath(cacheKey, persistPath);
            if (!File.Exists(filePath))
            {
                Log.Debug("No disk cache found for {Key}", cacheKey);
                return null;
            }

            var json = await File.ReadAllBytesAsync(filePath);
            var cache = JsonSerializer.Deserialize<DatabaseCache>(json, CacheJsonOptions);

            if (cache != null)
            {
                Log.Information("Loaded cache from disk for {Key}: {Count} schemas",
                    cacheKey, cache.Schemas.Count);
            }

            return cache;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load cache from disk for {Key}", cacheKey);
            return null;
        }
    }

    private static string GetCacheFilePath(string cacheKey, string persistPath)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cacheKey)))[..16];
        return Path.Combine(persistPath, $"{hash}.json");
    }

    // ========================================================================
    // T079: Background periodic refresh timer
    // ========================================================================

    /// <summary>
    /// Starts a background timer that periodically checks all cached databases for schema changes.
    /// Default interval: 60 seconds.
    /// </summary>
    public void StartPeriodicRefresh(TimeSpan? interval = null)
    {
        var refreshInterval = interval ?? TimeSpan.FromSeconds(60);
        _periodicRefreshTimer = new Timer(OnPeriodicRefresh, null, refreshInterval, refreshInterval);
        Log.Information("Periodic schema refresh started (interval={Interval}s)", refreshInterval.TotalSeconds);
    }

    /// <summary>
    /// Stops the background periodic refresh timer.
    /// </summary>
    public void StopPeriodicRefresh()
    {
        _periodicRefreshTimer?.Dispose();
        _periodicRefreshTimer = null;
        Log.Information("Periodic schema refresh stopped");
    }

    private void OnPeriodicRefresh(object? state)
    {
        // Delegate to Task.Run to avoid async void — exceptions are caught per-iteration
        _ = Task.Run(async () =>
        {
            foreach (var kvp in _caches)
            {
                var cache = kvp.Value;

                // Skip caches that a previous Phase A already flagged as
                // permission-denied — probing them again would log the same 4060
                // every minute forever.
                if (cache.PermissionDenied)
                {
                    continue;
                }

                if (!_connectionStrings.TryGetValue(cache.CacheKey, out var connectionString))
                {
                    continue;
                }

                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    var changed = await _changeDetector.CheckForChangesAsync(connectionString, cache, cts.Token);
                    if (changed)
                    {
                        Log.Information("Periodic refresh: changes detected for {Key}", cache.CacheKey);
                        cache.IsStale = true;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Periodic refresh check failed for {Key}", cache.CacheKey);
                }
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _periodicRefreshTimer?.Dispose();
    }
}
