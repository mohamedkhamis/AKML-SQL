using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M3 task T067. IndexedDB-backed store for
/// <see cref="EngineConnection"/> records (data-model.md E2). Surfaces connection
/// metadata to the picker UI; the wrapped bearer token itself lives in
/// <see cref="IPairingTokenVault"/>.
/// </summary>
public interface IConnectionStore
{
    /// <summary>Return every stored connection. Sorted by <see cref="EngineConnection.Name"/>.</summary>
    Task<IReadOnlyList<EngineConnection>> ListAsync();

    Task<EngineConnection?> GetAsync(string id);

    /// <summary>Insert a new record. Throws if the id already exists.</summary>
    Task AddAsync(EngineConnection connection);

    /// <summary>Replace an existing record. Throws if the id is unknown.</summary>
    Task UpdateAsync(EngineConnection connection);

    /// <summary>Drop the record. Caller is responsible for also dropping the bearer-token vault entry.</summary>
    Task RemoveAsync(string id);

    /// <summary>The id of the currently active connection. Null until the user picks one.</summary>
    Task<string?> GetActiveIdAsync();
    Task SetActiveIdAsync(string? id);
}

/// <summary>One paired engine. Per data-model.md E2.</summary>
public sealed class EngineConnection
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Local engine";
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 5081;
    public bool IsLocalhost { get; set; } = true;

    /// <summary>Id of the wrapped record in <see cref="IPairingTokenVault"/>. Null for localhost.</summary>
    public string? BearerTokenWrappedRef { get; set; }

    /// <summary>Hex SHA-256 of the engine's TLS cert. Pinned after first connect for LAN.</summary>
    public string? TlsFingerprint { get; set; }

    public DateTimeOffset? LastConnectedAt { get; set; }
    public string? LastKnownEngineVersion { get; set; }
    public string[] LastKnownCapabilities { get; set; } = Array.Empty<string>();
}

internal sealed class ConnectionStore : IConnectionStore
{
    private const string ActiveIdKey = "_active";
    private readonly IIndexedDbAdapter _store;

    public ConnectionStore(IIndexedDbAdapter store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<IReadOnlyList<EngineConnection>> ListAsync()
    {
        var entries = await _store.ListAsync(StoreNames.Connections).ConfigureAwait(false);
        return entries
            .Where(kv => kv.Key != ActiveIdKey)
            .Select(kv => SafeDeserialize(kv.Value))
            .Where(c => c != null)
            .Cast<EngineConnection>()
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<EngineConnection?> GetAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var raw = await _store.GetAsync(StoreNames.Connections, id).ConfigureAwait(false);
        return string.IsNullOrEmpty(raw) ? null : SafeDeserialize(raw!);
    }

    public async Task AddAsync(EngineConnection connection)
    {
        if (connection == null) throw new ArgumentNullException(nameof(connection));
        if (string.IsNullOrEmpty(connection.Id)) connection.Id = Guid.NewGuid().ToString();

        var existing = await _store.GetAsync(StoreNames.Connections, connection.Id).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(existing))
        {
            throw new InvalidOperationException($"Connection '{connection.Id}' already exists.");
        }

        ValidateConnection(connection);
        await _store.SetAsync(StoreNames.Connections, connection.Id, JsonSerializer.Serialize(connection))
            .ConfigureAwait(false);
    }

    public async Task UpdateAsync(EngineConnection connection)
    {
        if (connection == null) throw new ArgumentNullException(nameof(connection));
        var existing = await _store.GetAsync(StoreNames.Connections, connection.Id).ConfigureAwait(false);
        if (string.IsNullOrEmpty(existing))
        {
            throw new InvalidOperationException($"Connection '{connection.Id}' does not exist.");
        }
        ValidateConnection(connection);
        await _store.SetAsync(StoreNames.Connections, connection.Id, JsonSerializer.Serialize(connection))
            .ConfigureAwait(false);
    }

    public async Task RemoveAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        await _store.DeleteAsync(StoreNames.Connections, id).ConfigureAwait(false);

        // Drop the active-id pointer too if it was pointing here.
        var active = await GetActiveIdAsync().ConfigureAwait(false);
        if (active == id) await SetActiveIdAsync(null).ConfigureAwait(false);
    }

    public async Task<string?> GetActiveIdAsync()
    {
        var v = await _store.GetAsync(StoreNames.Connections, ActiveIdKey).ConfigureAwait(false);
        return string.IsNullOrEmpty(v) ? null : v;
    }

    public Task SetActiveIdAsync(string? id) =>
        string.IsNullOrEmpty(id)
            ? _store.DeleteAsync(StoreNames.Connections, ActiveIdKey)
            : _store.SetAsync(StoreNames.Connections, ActiveIdKey, id);

    private static void ValidateConnection(EngineConnection c)
    {
        if (string.IsNullOrEmpty(c.Host)) throw new ArgumentException("Host is required.");
        if (c.Port is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(c.Port), "Port must be 1024..65535.");

        // Per data-model.md E2 validation: bearerTokenWrappedRef MUST be non-null when !isLocalhost.
        if (!c.IsLocalhost && string.IsNullOrEmpty(c.BearerTokenWrappedRef))
        {
            throw new InvalidOperationException(
                "Non-localhost connections must reference a wrapped bearer token (BearerTokenWrappedRef).");
        }
    }

    private static EngineConnection? SafeDeserialize(string json)
    {
        try { return JsonSerializer.Deserialize<EngineConnection>(json); }
        catch (JsonException) { return null; }
    }
}
