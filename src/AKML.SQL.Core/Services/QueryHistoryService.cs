using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using AKML.SQL.Shared;

namespace AKML.SQL.Core.Services;

/// <summary>
/// Service for managing query execution history.
/// Implements Story 8.1: Query History Management.
/// </summary>
public class QueryHistoryService : IQueryHistoryService
{
    private readonly ILogger<QueryHistoryService> _logger;
    private readonly ConcurrentDictionary<long, QueryHistoryEntry> _entries;
    private readonly string _historyFilePath;
    private long _nextId;
    private readonly int _maxEntries;
    private readonly object _saveLock = new();

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public QueryHistoryService(ILogger<QueryHistoryService> logger, int maxEntries = 1000)
    {
        _logger = logger;
        _maxEntries = maxEntries;
        _entries = new ConcurrentDictionary<long, QueryHistoryEntry>();
        _historyFilePath = Path.Combine(AkmlConstants.Paths.AppDataFolder, "query-history.json");
        _nextId = 1;

        LoadHistory();
    }

    /// <summary>
    /// Adds a new query to history.
    /// </summary>
    public QueryHistoryEntry AddEntry(QueryHistoryEntry entry)
    {
        if (entry == null)
            throw new ArgumentNullException(nameof(entry));

        if (string.IsNullOrWhiteSpace(entry.Query))
            throw new ArgumentException("Query cannot be empty", nameof(entry));

        entry.Id = Interlocked.Increment(ref _nextId);
        entry.ExecutedAt = entry.ExecutedAt == default ? DateTime.UtcNow : entry.ExecutedAt;

        _entries[entry.Id] = entry;
        _logger.LogDebug("Added query history entry {Id}", entry.Id);

        // Trim old entries if over limit
        TrimOldEntries();

        // Save asynchronously
        _ = Task.Run(SaveHistoryAsync);

        return entry;
    }

    /// <summary>
    /// Gets all history entries.
    /// </summary>
    public IReadOnlyList<QueryHistoryEntry> GetAll(int? limit = null, int skip = 0)
    {
        var entries = _entries.Values
            .OrderByDescending(e => e.ExecutedAt)
            .Skip(skip);

        if (limit.HasValue)
            entries = entries.Take(limit.Value);

        return entries.ToList();
    }

    /// <summary>
    /// Searches history by query text.
    /// </summary>
    public IReadOnlyList<QueryHistoryEntry> Search(string searchText, int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(searchText))
            return GetAll(limit);

        var searchUpper = searchText.ToUpperInvariant();

        return _entries.Values
            .Where(e => e.Query.ToUpperInvariant().Contains(searchUpper))
            .OrderByDescending(e => e.ExecutedAt)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Gets history filtered by server and/or database.
    /// </summary>
    public IReadOnlyList<QueryHistoryEntry> GetByConnection(string? serverName, string? databaseName, int limit = 50)
    {
        var entries = _entries.Values.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(serverName))
            entries = entries.Where(e => e.ServerName.Equals(serverName, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(databaseName))
            entries = entries.Where(e => e.DatabaseName.Equals(databaseName, StringComparison.OrdinalIgnoreCase));

        return entries
            .OrderByDescending(e => e.ExecutedAt)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Gets favorite queries.
    /// </summary>
    public IReadOnlyList<QueryHistoryEntry> GetFavorites(int limit = 50)
    {
        return _entries.Values
            .Where(e => e.IsFavorite)
            .OrderByDescending(e => e.ExecutedAt)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Gets an entry by ID.
    /// </summary>
    public QueryHistoryEntry? GetById(long id)
    {
        _entries.TryGetValue(id, out var entry);
        return entry;
    }

    /// <summary>
    /// Toggles favorite status.
    /// </summary>
    public bool ToggleFavorite(long id)
    {
        if (_entries.TryGetValue(id, out var entry))
        {
            entry.IsFavorite = !entry.IsFavorite;
            _ = Task.Run(SaveHistoryAsync);
            return entry.IsFavorite;
        }
        return false;
    }

    /// <summary>
    /// Updates tags for an entry.
    /// </summary>
    public void UpdateTags(long id, string? tags)
    {
        if (_entries.TryGetValue(id, out var entry))
        {
            entry.Tags = tags;
            _ = Task.Run(SaveHistoryAsync);
        }
    }

    /// <summary>
    /// Deletes an entry.
    /// </summary>
    public bool Delete(long id)
    {
        var removed = _entries.TryRemove(id, out _);
        if (removed)
        {
            _logger.LogDebug("Deleted query history entry {Id}", id);
            _ = Task.Run(SaveHistoryAsync);
        }
        return removed;
    }

    /// <summary>
    /// Clears all history (optionally keeping favorites).
    /// </summary>
    public void Clear(bool keepFavorites = true)
    {
        if (keepFavorites)
        {
            var toRemove = _entries.Where(kvp => !kvp.Value.IsFavorite).Select(kvp => kvp.Key).ToList();
            foreach (var id in toRemove)
                _entries.TryRemove(id, out _);
        }
        else
        {
            _entries.Clear();
        }

        _logger.LogInformation("Cleared query history (keepFavorites: {KeepFavorites})", keepFavorites);
        _ = Task.Run(SaveHistoryAsync);
    }

    /// <summary>
    /// Gets unique server names from history.
    /// </summary>
    public IReadOnlyList<string> GetServerNames()
    {
        return _entries.Values
            .Select(e => e.ServerName)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s)
            .ToList();
    }

    /// <summary>
    /// Gets unique database names from history.
    /// </summary>
    public IReadOnlyList<string> GetDatabaseNames(string? serverName = null)
    {
        var entries = _entries.Values.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(serverName))
            entries = entries.Where(e => e.ServerName.Equals(serverName, StringComparison.OrdinalIgnoreCase));

        return entries
            .Select(e => e.DatabaseName)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s)
            .ToList();
    }

    /// <summary>
    /// Gets total entry count.
    /// </summary>
    public int Count => _entries.Count;

    private void TrimOldEntries()
    {
        if (_entries.Count <= _maxEntries)
            return;

        var toRemove = _entries.Values
            .Where(e => !e.IsFavorite)
            .OrderBy(e => e.ExecutedAt)
            .Take(_entries.Count - _maxEntries)
            .Select(e => e.Id)
            .ToList();

        foreach (var id in toRemove)
            _entries.TryRemove(id, out _);

        _logger.LogDebug("Trimmed {Count} old history entries", toRemove.Count);
    }

    private void LoadHistory()
    {
        try
        {
            if (!File.Exists(_historyFilePath))
                return;

            var json = File.ReadAllText(_historyFilePath);
            var entries = JsonSerializer.Deserialize<List<QueryHistoryEntry>>(json, _jsonOptions);

            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    _entries[entry.Id] = entry;
                    if (entry.Id >= _nextId)
                        _nextId = entry.Id + 1;
                }
            }

            _logger.LogInformation("Loaded {Count} query history entries", _entries.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading query history");
        }
    }

    private async Task SaveHistoryAsync()
    {
        try
        {
            var entries = _entries.Values.OrderByDescending(e => e.ExecutedAt).ToList();
            var json = JsonSerializer.Serialize(entries, _jsonOptions);

            lock (_saveLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_historyFilePath)!);
                File.WriteAllText(_historyFilePath, json);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving query history");
        }
    }
}

/// <summary>
/// Interface for query history service.
/// </summary>
public interface IQueryHistoryService
{
    QueryHistoryEntry AddEntry(QueryHistoryEntry entry);
    IReadOnlyList<QueryHistoryEntry> GetAll(int? limit = null, int skip = 0);
    IReadOnlyList<QueryHistoryEntry> Search(string searchText, int limit = 50);
    IReadOnlyList<QueryHistoryEntry> GetByConnection(string? serverName, string? databaseName, int limit = 50);
    IReadOnlyList<QueryHistoryEntry> GetFavorites(int limit = 50);
    QueryHistoryEntry? GetById(long id);
    bool ToggleFavorite(long id);
    void UpdateTags(long id, string? tags);
    bool Delete(long id);
    void Clear(bool keepFavorites = true);
    IReadOnlyList<string> GetServerNames();
    IReadOnlyList<string> GetDatabaseNames(string? serverName = null);
    int Count { get; }
}

/// <summary>
/// Query history entry.
/// </summary>
public class QueryHistoryEntry
{
    public long Id { get; set; }
    public string Query { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public DateTime ExecutedAt { get; set; }
    public long ExecutionTimeMs { get; set; }
    public int RowsAffected { get; set; }
    public bool WasSuccessful { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public bool IsFavorite { get; set; }
    public string? Tags { get; set; }
}
