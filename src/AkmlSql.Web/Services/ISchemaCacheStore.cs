using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M5 task T107. IndexedDB-backed persistence of schema
/// snapshots per data-model.md E7 and contracts/schema-cache-shape.md. The composite
/// primary key is <c>(serverCanonicalIdentity, databaseName)</c> -- two distinct DNS
/// aliases of the same SQL Server collapse to one entry.
/// </summary>
public interface ISchemaCacheStore
{
    /// <summary>Return null when no entry exists for the composite key.</summary>
    Task<SchemaSnapshot?> GetAsync(string serverCanonicalIdentity, string databaseName);

    /// <summary>Insert or replace. Updates <c>LastUsedAt</c> to <see cref="DateTimeOffset.UtcNow"/>.</summary>
    Task SetAsync(SchemaSnapshot snapshot);

    /// <summary>Touch <c>LastUsedAt</c> without rewriting the rest of the snapshot.</summary>
    Task TouchAsync(string serverCanonicalIdentity, string databaseName);

    /// <summary>
    /// Drop the snapshot. The cache survives a connection being unpaired (the entries
    /// are keyed by server identity, not connection), so this is exposed only for
    /// explicit user-driven clear (FR-028).
    /// </summary>
    Task RemoveAsync(string serverCanonicalIdentity, string databaseName);

    /// <summary>Enumerate every snapshot. Sorted by <c>LastUsedAt</c> ascending (oldest first) so callers driving LRU eviction can scan in order.</summary>
    Task<IReadOnlyList<SchemaSnapshot>> ListAsync();

    /// <summary>Drop every snapshot. Settings -> Clear schema cache (FR-028).</summary>
    Task ClearAllAsync();

    /// <summary>
    /// Combined size estimate in bytes. Best-effort -- the browser's
    /// <c>StorageManager.estimate</c> is the source of truth; this returns the sum of
    /// serialised lengths so the UI can show per-entry sizes.
    /// </summary>
    Task<long> EstimatedSizeAsync(string serverCanonicalIdentity, string databaseName);
}

/// <summary>One schema snapshot keyed by (server, db). See data-model.md E7.</summary>
public sealed class SchemaSnapshot
{
    public string ServerCanonicalIdentity { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;

    /// <summary>Opaque MessagePack-of-Phase-A bytes (or null when not yet populated).</summary>
    public byte[]? PhaseA { get; set; }
    /// <summary>Opaque MessagePack-of-Phase-B bytes (or null when not yet populated).</summary>
    public byte[]? PhaseB { get; set; }
    /// <summary>Opaque MessagePack-of-fk-index bytes (or null).</summary>
    public byte[]? FkIndex { get; set; }

    /// <summary>Engine's CHECKSUM_AGG result -- used to detect drift.</summary>
    public string Checksum { get; set; } = string.Empty;

    public DateTimeOffset FetchedAt { get; set; }
    public DateTimeOffset LastUsedAt { get; set; }

    public string? SourceConnectionId { get; set; }

    /// <summary>The composite key used inside IndexedDB. Two distinct host strings pointing at the same SQL Server collapse to one key.</summary>
    public string CompositeKey => MakeKey(ServerCanonicalIdentity, DatabaseName);

    internal static string MakeKey(string server, string db)
    {
        if (string.IsNullOrEmpty(server)) throw new ArgumentException("server is required.", nameof(server));
        if (string.IsNullOrEmpty(db)) throw new ArgumentException("db is required.", nameof(db));
        return server + "" + db;   //  = ASCII Unit Separator, never appears in identifiers
    }
}

internal sealed class SchemaCacheStore : ISchemaCacheStore
{
    private readonly IIndexedDbAdapter _store;

    public SchemaCacheStore(IIndexedDbAdapter store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<SchemaSnapshot?> GetAsync(string serverCanonicalIdentity, string databaseName)
    {
        var raw = await _store.GetAsync(StoreNames.SchemaEntries, SchemaSnapshot.MakeKey(serverCanonicalIdentity, databaseName))
            .ConfigureAwait(false);
        return string.IsNullOrEmpty(raw) ? null : SafeDeserialize(raw!);
    }

    public Task SetAsync(SchemaSnapshot snapshot)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        snapshot.LastUsedAt = DateTimeOffset.UtcNow;
        return _store.SetAsync(StoreNames.SchemaEntries, snapshot.CompositeKey, JsonSerializer.Serialize(snapshot));
    }

    public async Task TouchAsync(string serverCanonicalIdentity, string databaseName)
    {
        var existing = await GetAsync(serverCanonicalIdentity, databaseName).ConfigureAwait(false);
        if (existing == null) return;
        existing.LastUsedAt = DateTimeOffset.UtcNow;
        await _store.SetAsync(StoreNames.SchemaEntries, existing.CompositeKey, JsonSerializer.Serialize(existing))
            .ConfigureAwait(false);
    }

    public Task RemoveAsync(string serverCanonicalIdentity, string databaseName) =>
        _store.DeleteAsync(StoreNames.SchemaEntries, SchemaSnapshot.MakeKey(serverCanonicalIdentity, databaseName));

    public async Task<IReadOnlyList<SchemaSnapshot>> ListAsync()
    {
        var entries = await _store.ListAsync(StoreNames.SchemaEntries).ConfigureAwait(false);
        return entries
            .Select(kv => SafeDeserialize(kv.Value))
            .Where(s => s != null)
            .Cast<SchemaSnapshot>()
            .OrderBy(s => s.LastUsedAt)
            .ToList();
    }

    public Task ClearAllAsync() => _store.ClearAsync(StoreNames.SchemaEntries);

    public async Task<long> EstimatedSizeAsync(string serverCanonicalIdentity, string databaseName)
    {
        var raw = await _store.GetAsync(StoreNames.SchemaEntries, SchemaSnapshot.MakeKey(serverCanonicalIdentity, databaseName))
            .ConfigureAwait(false);
        return raw == null ? 0 : raw.Length;
    }

    private static SchemaSnapshot? SafeDeserialize(string json)
    {
        try { return JsonSerializer.Deserialize<SchemaSnapshot>(json); }
        catch (JsonException) { return null; }
    }
}
