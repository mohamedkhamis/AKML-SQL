using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace AkmlSql.Web.Services;

/// <summary>
/// Phase 4 (web connection manager). IndexedDB-backed store for saved SQL-Server
/// connections. Mirrors the <see cref="IConnectionStore"/> idiom: List/Get/Add/Update/Remove
/// plus an active-id pointer (the most-recently-used saved connection).
///
/// SECURITY: a <see cref="SavedSqlConnection"/> persists only NON-SECRET metadata
/// (name/server/database/auth-mode and, for SQL auth, the login username — a username is
/// not a secret). It has NO password field. SQL-auth passwords are re-prompted at Connect
/// time and stay transient (never written to IndexedDB), matching the rest of the codebase
/// (the DPAPI SqlCredentialStore is desktop-only; web at-rest secrets are Web-Crypto-wrapped).
/// </summary>
public interface ISavedSqlConnectionStore
{
    /// <summary>Return every saved connection. Sorted by <see cref="SavedSqlConnection.Name"/>.</summary>
    Task<IReadOnlyList<SavedSqlConnection>> ListAsync();

    Task<SavedSqlConnection?> GetAsync(string id);

    /// <summary>Insert a new record. Throws if the id already exists.</summary>
    Task AddAsync(SavedSqlConnection connection);

    /// <summary>Replace an existing record. Throws if the id is unknown.</summary>
    Task UpdateAsync(SavedSqlConnection connection);

    Task RemoveAsync(string id);

    /// <summary>The id of the most-recently-used saved connection. Null until one is picked.</summary>
    Task<string?> GetActiveIdAsync();
    Task SetActiveIdAsync(string? id);
}

/// <summary>
/// One saved SQL-Server connection. Property style mirrors <see cref="EngineConnection"/>.
///
/// NO Password property — by design. Windows-auth rows reconnect in one click (the engine
/// connects under its own Windows identity, no secret needed); SQL-auth rows persist only the
/// login username and re-prompt the password at Connect.
/// </summary>
public sealed class SavedSqlConnection
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Server { get; set; } = "localhost";
    public string Database { get; set; } = "master";

    /// <summary>True = Windows authentication (no password); false = SQL auth (login below, password re-prompted).</summary>
    public bool WindowsAuth { get; set; } = true;

    /// <summary>SQL-auth login username. Persisted (a username is not a secret). Null for Windows auth.</summary>
    public string? Login { get; set; }

    public DateTimeOffset? LastConnectedAt { get; set; }

    /// <summary>
    /// Review WEB-02 (SQL Server): skip validating a remote server's certificate for THIS connection — the user's explicit
    /// choice for a self-signed server they trust. Missing on older records = validate. A server on the engine's own
    /// machine is never validated, whatever this says.
    /// </summary>
    public bool TrustServerCertificate { get; set; }

    // ── Spec 042 (PostgreSQL support, data-model §2) ───────────────────────────────────────────────────────
    // IndexedDB values are schemaless JSON: a record saved before the feature has none of these, and they read
    // back as defaults - a SQL Server connection, exactly as before. No password here either: even with
    // RememberPassword on, the password lives only in the engine's protected store.

    /// <summary>Which kind of server this is. Missing on older records = SQL Server.</summary>
    public AkmlSql.Core.DatabaseKind Kind { get; set; } = AkmlSql.Core.DatabaseKind.SqlServer;

    /// <summary>PostgreSQL port; null means <see cref="DefaultPostgresPort"/>. Ignored for SQL Server (use <c>host,port</c> there).</summary>
    public int? Port { get; set; }

    /// <summary>PostgreSQL TLS mode: <c>disable</c>, <c>prefer</c>, <c>require</c>, <c>verify-ca</c> or <c>verify-full</c>; null means <c>prefer</c>.</summary>
    public string? SslMode { get; set; }

    /// <summary>Path of the CA certificate on the ENGINE machine, for the verifying TLS modes. Not a secret.</summary>
    public string? RootCertificatePath { get; set; }

    /// <summary>
    /// The user opted in to keep the PostgreSQL password on the engine machine (FR-104). The password itself is never
    /// part of this record; the engine holds it (DPAPI) under this server and login and never sends it back.
    /// Stored under a key without the word "password": the store's tripwire test scans the whole persisted record
    /// for that word, and a flag must not weaken a check that exists to catch a real secret.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("RememberOnEngine")]
    public bool RememberPassword { get; set; }

    public const int DefaultPostgresPort = 5432;

    /// <summary>The five TLS modes the connection dialog offers, in the order it lists them.</summary>
    public static readonly string[] SslModes = ["disable", "prefer", "require", "verify-ca", "verify-full"];

    /// <summary>The port a PostgreSQL connection uses: the saved one, or 5432.</summary>
    public int EffectivePort => Port ?? DefaultPostgresPort;

    // NOTE: deliberately NO "IsRemoteAllowed" flag. The loopback/SSRF guard
    // (SqlConnectionService.ValidateTarget) is HOST-BASED and authoritative — it ignores any
    // stored boolean — so a persisted flag could never relax it, only mislead. If remote targets
    // are ever enabled, that must be paired with an ENGINE-SIDE host check, not a record field.
}

internal sealed class SavedSqlConnectionStore : ISavedSqlConnectionStore
{
    private const string ActiveIdKey = "_active";
    private readonly IIndexedDbAdapter _store;

    public SavedSqlConnectionStore(IIndexedDbAdapter store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<IReadOnlyList<SavedSqlConnection>> ListAsync()
    {
        var entries = await _store.ListAsync(StoreNames.SavedSqlConnections).ConfigureAwait(false);
        return entries
            .Where(kv => kv.Key != ActiveIdKey)
            .Select(kv => SafeDeserialize(kv.Value))
            .Where(c => c != null)
            .Cast<SavedSqlConnection>()
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<SavedSqlConnection?> GetAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var raw = await _store.GetAsync(StoreNames.SavedSqlConnections, id).ConfigureAwait(false);
        return string.IsNullOrEmpty(raw) ? null : SafeDeserialize(raw!);
    }

    public async Task AddAsync(SavedSqlConnection connection)
    {
        if (connection == null) throw new ArgumentNullException(nameof(connection));
        if (string.IsNullOrEmpty(connection.Id)) connection.Id = Guid.NewGuid().ToString();

        var existing = await _store.GetAsync(StoreNames.SavedSqlConnections, connection.Id).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(existing))
        {
            throw new InvalidOperationException($"Saved connection '{connection.Id}' already exists.");
        }

        ValidateConnection(connection);
        await _store.SetAsync(StoreNames.SavedSqlConnections, connection.Id, JsonSerializer.Serialize(connection))
            .ConfigureAwait(false);
    }

    public async Task UpdateAsync(SavedSqlConnection connection)
    {
        if (connection == null) throw new ArgumentNullException(nameof(connection));
        var existing = await _store.GetAsync(StoreNames.SavedSqlConnections, connection.Id).ConfigureAwait(false);
        if (string.IsNullOrEmpty(existing))
        {
            throw new InvalidOperationException($"Saved connection '{connection.Id}' does not exist.");
        }
        ValidateConnection(connection);
        await _store.SetAsync(StoreNames.SavedSqlConnections, connection.Id, JsonSerializer.Serialize(connection))
            .ConfigureAwait(false);
    }

    public async Task RemoveAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        await _store.DeleteAsync(StoreNames.SavedSqlConnections, id).ConfigureAwait(false);

        // Drop the active-id pointer too if it was pointing here.
        var active = await GetActiveIdAsync().ConfigureAwait(false);
        if (active == id) await SetActiveIdAsync(null).ConfigureAwait(false);
    }

    public async Task<string?> GetActiveIdAsync()
    {
        var v = await _store.GetAsync(StoreNames.SavedSqlConnections, ActiveIdKey).ConfigureAwait(false);
        return string.IsNullOrEmpty(v) ? null : v;
    }

    public Task SetActiveIdAsync(string? id) =>
        string.IsNullOrEmpty(id)
            ? _store.DeleteAsync(StoreNames.SavedSqlConnections, ActiveIdKey)
            : _store.SetAsync(StoreNames.SavedSqlConnections, ActiveIdKey, id);

    private static void ValidateConnection(SavedSqlConnection c)
    {
        // Non-empty name/server/database only. The loopback/SSRF guard is NOT enforced here —
        // it lives in SqlConnectionService.ValidateTarget and runs at Connect/Test time, so a
        // stored record can never be the SSRF vector by itself.
        if (string.IsNullOrWhiteSpace(c.Name)) throw new ArgumentException("Name is required.");
        if (string.IsNullOrWhiteSpace(c.Server)) throw new ArgumentException("Server is required.");
        if (string.IsNullOrWhiteSpace(c.Database)) throw new ArgumentException("Database is required.");

        // Spec 042: what makes a PostgreSQL connection. A SQL Server record is never held to these.
        if (c.Kind != AkmlSql.Core.DatabaseKind.PostgreSql) return;

        if (string.IsNullOrWhiteSpace(c.Login)) throw new ArgumentException("A user name is required for PostgreSQL.");
        if (c.Port is < 1 or > 65535) throw new ArgumentException("The port must be between 1 and 65535.");
        if (c.SslMode is not null && Array.IndexOf(SavedSqlConnection.SslModes, c.SslMode.ToLowerInvariant()) < 0)
            throw new ArgumentException("The TLS mode must be disable, prefer, require, verify-ca or verify-full.");
    }

    private static SavedSqlConnection? SafeDeserialize(string json)
    {
        try { return JsonSerializer.Deserialize<SavedSqlConnection>(json); }
        catch (JsonException) { return null; }
    }
}
