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

    // Spec 043 follow-up (review, round 4): a population claim belongs to the cache object being loaded, not to its key. Keyed by
    // the "{session}:{db}" string, a load still running on a cache that RemoveSession had discarded held the key: the new connect's
    // load of the new cache under the same key found it claimed and gave up, the old load finished into the discarded cache, and
    // the tab had no schema until its next ConnectionChanged; and the old load's release would have freed a claim it never made.
    private readonly ConcurrentDictionary<DatabaseCache, byte> _populating = new(ReferenceEqualityComparer.Instance);

    // The keys a ReloadMissing reload is running for: there is no cache object yet to claim, so this only keeps a burst of lookups of
    // a missing cache to one reload. The reload claims the cache it creates like any other load (EngineComposition.ReloadSessionCacheAsync).
    private readonly ConcurrentDictionary<string, byte> _reloading = new(StringComparer.OrdinalIgnoreCase);
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

    /// <summary>
    /// Spec 042 (FR-004, data-model §6): the kind of server a session is connected to. The first argument of every lookup here is
    /// a session id, so the key can carry the kind without any caller changing: the two <c>northwind</c> databases of one session
    /// that moved from SQL Server to PostgreSQL are two caches, in memory and on disk. Null means every session is SQL Server.
    /// </summary>
    public Func<string, AkmlSql.Core.DatabaseKind>? KindOf { get; set; }

    private AkmlSql.Core.DatabaseKind KindFor(string serverName) => KindOf?.Invoke(serverName) ?? AkmlSql.Core.DatabaseKind.SqlServer;

    private string BuildCacheKey(string serverName, string databaseName) =>
        KindFor(serverName) == AkmlSql.Core.DatabaseKind.PostgreSql ? $"pg|{serverName}:{databaseName}" : $"{serverName}:{databaseName}";

    /// <summary>
    /// Returns the existing cache for <paramref name="serverName"/>/<paramref name="databaseName"/>,
    /// or creates a new empty one. Callers should follow up with <c>SchemaMetadataService.PopulatePhaseAAsync</c>
    /// when <c>Phase == NotLoaded</c>.
    /// </summary>
    public DatabaseCache GetOrCreateCache(string serverName, string databaseName)
    {
        var key = BuildCacheKey(serverName, databaseName);
        var kind = KindFor(serverName);
        var cache = _caches.GetOrAdd(key, k => new DatabaseCache { CacheKey = k, Kind = kind });
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
        if (reload != null && _reloading.TryAdd(key, 0))
        {
            _ = Task.Run(async () =>
            {
                try { await reload(serverName, databaseName).ConfigureAwait(false); }
                catch (Exception ex) { Log.Warning(ex, "Reloading the schema cache for {Key} failed", key); }
                finally { _reloading.TryRemove(key, out _); }
            });
        }
        return null;
    }

    /// <summary>
    /// Claims the right to populate <paramref name="cache"/>; false while another population of that same cache object runs.
    /// Pair with <see cref="ReleasePopulation"/>. Spec 043 follow-up (review, round 4): the claim is the object's, not its key's,
    /// so a cache created under the key of one <see cref="RemoveSession"/> discarded can be loaded while the old one's load ends.
    /// </summary>
    public bool TryClaimPopulation(DatabaseCache cache) => _populating.TryAdd(cache, 0);

    /// <summary>Ends a population of <paramref name="cache"/> claimed with <see cref="TryClaimPopulation"/>; another cache's claim is untouched.</summary>
    public void ReleasePopulation(DatabaseCache cache) => _populating.TryRemove(cache, out _);

    /// <summary>True while a population of <paramref name="cache"/> is claimed: a load is running on it.</summary>
    public bool IsPopulating(DatabaseCache cache) => _populating.ContainsKey(cache);

    /// <summary>The cache for the server/database if it exists; nothing else (no reload is started, no use is recorded).</summary>
    public DatabaseCache? FindCache(string serverName, string databaseName) =>
        _caches.TryGetValue(BuildCacheKey(serverName, databaseName), out var cache) ? cache : null;

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

    /// <summary>
    /// Spec 043 (research R5): forget every cache of one session — every database it used, either kind — and their refresh
    /// connection strings. Called when the web edition closes the query tab that owned the session
    /// (<c>SessionDisconnect</c> with <c>Release</c>). Keys are <c>{session}:{database}</c> or <c>pg|{session}:{database}</c>
    /// (<see cref="BuildCacheKey"/>); nothing is persisted to disk, so memory is all there is. Returns how many were removed.
    /// A load still running on a removed cache keeps its claim on that object only (<see cref="TryClaimPopulation"/>): it ends
    /// into the discarded cache, and a cache created afterwards under the same key is claimed and loaded on its own.
    /// </summary>
    public int RemoveSession(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return 0;
        bool Owned(string key) =>
            key.StartsWith(sessionId + ":", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("pg|" + sessionId + ":", StringComparison.OrdinalIgnoreCase);

        int removed = 0;
        foreach (var key in _caches.Keys.Where(Owned).ToList())
        {
            if (_caches.TryRemove(key, out _)) removed++;
        }
        foreach (var key in _connectionStrings.Keys.Where(Owned).ToList()) _connectionStrings.TryRemove(key, out _);
        return removed;
    }

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
