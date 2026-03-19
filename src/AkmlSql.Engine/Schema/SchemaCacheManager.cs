using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace AkmlSql.Engine.Schema;

public class SchemaCacheManager : IDisposable
{
    private readonly ConcurrentDictionary<string, DatabaseCache> _caches = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _maxDatabases;
    private readonly ChangeDetector _changeDetector = new();
    private Timer? _periodicRefreshTimer;
    private readonly ConcurrentDictionary<string, string> _connectionStrings = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

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

    public SchemaCacheManager(int maxDatabases = 10)
    {
        _maxDatabases = maxDatabases;
    }

    public DatabaseCache GetOrCreateCache(string serverName, string databaseName)
    {
        var key = $"{serverName}:{databaseName}";
        return _caches.GetOrAdd(key, k => new DatabaseCache { CacheKey = k });
    }

    public DatabaseCache? GetCache(string serverName, string databaseName)
    {
        var key = $"{serverName}:{databaseName}";
        _caches.TryGetValue(key, out var cache);
        return cache;
    }

    /// <summary>
    /// Registers a connection string for a cache key, used by periodic refresh (T079).
    /// </summary>
    public void RegisterConnectionString(string serverName, string databaseName, string connectionString)
    {
        var key = $"{serverName}:{databaseName}";
        _connectionStrings[key] = connectionString;
    }

    public void EvictLru()
    {
        if (_caches.Count <= _maxDatabases)
            return;

        var oldest = _caches.Values
            .OrderBy(c => c.LastFullRefresh)
            .Take(_caches.Count - _maxDatabases)
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
                if (!_connectionStrings.TryGetValue(cache.CacheKey, out var connectionString))
                    continue;

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
        if (_disposed) return;
        _disposed = true;
        _periodicRefreshTimer?.Dispose();
    }
}
