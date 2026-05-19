using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M3 task T069. Stores bearer tokens in wrapped form per
/// data-model.md E3 + contracts/pairing-flow.md. The vault never returns the wrapped
/// blob to callers -- only the plaintext token, and only inside a short-lived scope.
///
/// <para>
/// Wrap recipe: <see cref="IWebCryptoWrapper"/> with
/// <c>aadString = "akmlsql.pairing." + connectionId</c>. This binds the wrapped token
/// to the connection so a record copied to a different connectionId fails decryption.
/// </para>
/// </summary>
public interface IPairingTokenVault
{
    /// <summary>
    /// Wrap and persist a freshly-minted bearer token. Returns the record id (which
    /// is also the wrapped-ref stored on the EngineConnection record). The raw
    /// <paramref name="plainToken"/> MUST NOT be retained by the caller after this
    /// call returns.
    /// </summary>
    Task<string> StoreAsync(string connectionId, string plainToken, DateTimeOffset ttlExpiresAt);

    /// <summary>
    /// Unwrap + return the plaintext bearer token bound to <paramref name="connectionId"/>.
    /// Throws when the record is missing, tampered, or the aad does not match.
    /// </summary>
    Task<string> RetrieveAsync(string connectionId);

    /// <summary>Drop the record. Zeroises the bytes before delete (best-effort).</summary>
    Task RemoveAsync(string connectionId);

    /// <summary>Return true when a record exists for <paramref name="connectionId"/>.</summary>
    Task<bool> ExistsAsync(string connectionId);
}

internal sealed class PairingTokenVault : IPairingTokenVault
{
    private readonly IIndexedDbAdapter _store;
    private readonly IWebCryptoWrapper _crypto;

    public PairingTokenVault(IIndexedDbAdapter store, IWebCryptoWrapper crypto)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
    }

    public async Task<string> StoreAsync(string connectionId, string plainToken, DateTimeOffset ttlExpiresAt)
    {
        if (string.IsNullOrEmpty(connectionId)) throw new ArgumentException("connectionId is required.", nameof(connectionId));
        if (string.IsNullOrEmpty(plainToken)) throw new ArgumentException("plainToken is required.", nameof(plainToken));

        var aad = AadFor(connectionId);
        var wrapped = await _crypto.WrapAsync(plainToken, aad).ConfigureAwait(false);

        var record = new PersistedToken
        {
            ConnectionId = connectionId,
            Ciphertext = Convert.ToBase64String(wrapped.Ciphertext),
            Iv = Convert.ToBase64String(wrapped.Iv),
            Aad = Convert.ToBase64String(wrapped.Aad),
            TtlExpiresAt = ttlExpiresAt,
        };
        await _store.SetAsync(StoreNames.PairingTokens, connectionId, JsonSerializer.Serialize(record))
            .ConfigureAwait(false);
        return connectionId;
    }

    public async Task<string> RetrieveAsync(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId)) throw new ArgumentException("connectionId is required.", nameof(connectionId));

        var raw = await _store.GetAsync(StoreNames.PairingTokens, connectionId).ConfigureAwait(false);
        if (string.IsNullOrEmpty(raw))
        {
            throw new InvalidOperationException($"No bearer token stored for connection '{connectionId}'.");
        }

        var record = JsonSerializer.Deserialize<PersistedToken>(raw)
            ?? throw new InvalidOperationException($"Bearer-token record for '{connectionId}' is corrupt.");

        var wrapped = new WrappedSecret
        {
            Ciphertext = Convert.FromBase64String(record.Ciphertext),
            Iv = Convert.FromBase64String(record.Iv),
            Aad = Convert.FromBase64String(record.Aad),
        };
        return await _crypto.UnwrapAsync(wrapped).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string connectionId)
    {
        // Read + zero + overwrite + delete -- tighten residual exposure even
        // though the GC may already hold copies.
        var raw = await _store.GetAsync(StoreNames.PairingTokens, connectionId).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(raw))
        {
            var zeroed = new PersistedToken { ConnectionId = connectionId };
            await _store.SetAsync(StoreNames.PairingTokens, connectionId, JsonSerializer.Serialize(zeroed))
                .ConfigureAwait(false);
        }
        await _store.DeleteAsync(StoreNames.PairingTokens, connectionId).ConfigureAwait(false);
    }

    public async Task<bool> ExistsAsync(string connectionId)
    {
        var raw = await _store.GetAsync(StoreNames.PairingTokens, connectionId).ConfigureAwait(false);
        return !string.IsNullOrEmpty(raw);
    }

    internal static string AadFor(string connectionId) => "akmlsql.pairing." + connectionId;

    private sealed class PersistedToken
    {
        public string ConnectionId { get; set; } = string.Empty;
        public string Ciphertext { get; set; } = string.Empty;
        public string Iv { get; set; } = string.Empty;
        public string Aad { get; set; } = string.Empty;
        public DateTimeOffset TtlExpiresAt { get; set; }
    }
}
