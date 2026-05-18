using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M6 task T125. Stores AI provider API keys in wrapped
/// form per contracts/ai-key-wrapping.md. Uses <see cref="IWebCryptoWrapper"/> with
/// <c>aad = "akmlsql.aikey." + providerId</c>, so a record copied to a different
/// providerId fails decryption (contract Invariant 5).
///
/// <para>
/// Plaintext keys are returned ONLY by <see cref="UnwrapForCallAsync"/> and MUST be
/// used inside a short-lived scope. Per the contract: never persist the unwrapped
/// value, never put it in a long-lived field, never log it.
/// </para>
/// </summary>
public interface IAiKeyVault
{
    /// <summary>
    /// Wrap and persist a freshly-entered API key for <paramref name="providerId"/>.
    /// </summary>
    Task SetKeyAsync(string providerId, AiProviderConfig config, string apiKeyPlain);

    /// <summary>
    /// Read the persisted (non-secret) provider config -- model, endpoint, etc.
    /// The wrapped key is NOT returned here.
    /// </summary>
    Task<AiProviderConfig?> GetConfigAsync(string providerId);

    /// <summary>List every configured provider id.</summary>
    Task<IReadOnlyList<AiProviderConfig>> ListAsync();

    /// <summary>True iff a wrapped key is stored for this provider.</summary>
    Task<bool> HasKeyAsync(string providerId);

    /// <summary>
    /// Unwrap the API key for a single provider call. The returned <see cref="UnwrappedKey"/>
    /// MUST be disposed promptly -- it overwrites its internal byte buffer on Dispose
    /// (best-effort against memory dumps).
    /// </summary>
    Task<UnwrappedKey> UnwrapForCallAsync(string providerId);

    /// <summary>Remove the wrapped key + provider config. Best-effort zeroise before delete.</summary>
    Task RemoveAsync(string providerId);
}

/// <summary>
/// Per-provider configuration. Mirrors data-model.md E8 (AiProviderConfig). The
/// wrapped key fields live alongside on the persistence side; the public surface
/// of <see cref="IAiKeyVault"/> never exposes them.
/// </summary>
public sealed class AiProviderConfig
{
    public string ProviderId { get; set; } = string.Empty;   // "openai", "anthropic", "gemini", "azure", "ollama", "lmstudio"
    public string DisplayName { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string? Endpoint { get; set; }    // required for Azure, Ollama, LM Studio
    public bool HasKey { get; set; }         // true iff a wrapped key is stored
}

/// <summary>
/// Short-lived plaintext API key. <see cref="Dispose"/> zeroises the internal
/// buffer. Use within a <c>using</c> block at the provider-call site only.
/// </summary>
public sealed class UnwrappedKey : IDisposable
{
    private byte[]? _bytes;

    public UnwrappedKey(string plain)
    {
        _bytes = System.Text.Encoding.UTF8.GetBytes(plain ?? string.Empty);
    }

    public string Value =>
        _bytes == null ? string.Empty : System.Text.Encoding.UTF8.GetString(_bytes);

    public void Dispose()
    {
        if (_bytes != null) Array.Clear(_bytes, 0, _bytes.Length);
        _bytes = null;
    }
}

internal sealed class AiKeyVault : IAiKeyVault
{
    private readonly IIndexedDbAdapter _store;
    private readonly IWebCryptoWrapper _crypto;

    public AiKeyVault(IIndexedDbAdapter store, IWebCryptoWrapper crypto)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
    }

    public async Task SetKeyAsync(string providerId, AiProviderConfig config, string apiKeyPlain)
    {
        if (string.IsNullOrEmpty(providerId)) throw new ArgumentException("providerId is required.", nameof(providerId));

        config.ProviderId = providerId;

        PersistedConfig record;
        if (string.IsNullOrEmpty(apiKeyPlain))
        {
            // Local providers (Ollama, LM Studio) don't use an API key. Persist the
            // config without wrapping anything; HasKey stays false.
            config.HasKey = false;
            record = new PersistedConfig
            {
                Config = config,
                Ciphertext = string.Empty,
                Iv = string.Empty,
                Aad = string.Empty,
            };
        }
        else
        {
            var aad = AadFor(providerId);
            var wrapped = await _crypto.WrapAsync(apiKeyPlain, aad).ConfigureAwait(false);
            config.HasKey = true;
            record = new PersistedConfig
            {
                Config = config,
                Ciphertext = Convert.ToBase64String(wrapped.Ciphertext),
                Iv = Convert.ToBase64String(wrapped.Iv),
                Aad = Convert.ToBase64String(wrapped.Aad),
            };
        }

        await _store.SetAsync(StoreNames.AiKeys, providerId, JsonSerializer.Serialize(record))
            .ConfigureAwait(false);
    }

    public async Task<AiProviderConfig?> GetConfigAsync(string providerId)
    {
        if (string.IsNullOrEmpty(providerId)) return null;
        var raw = await _store.GetAsync(StoreNames.AiKeys, providerId).ConfigureAwait(false);
        if (string.IsNullOrEmpty(raw)) return null;
        var record = SafeDeserialize(raw!);
        return record?.Config;
    }

    public async Task<IReadOnlyList<AiProviderConfig>> ListAsync()
    {
        var entries = await _store.ListAsync(StoreNames.AiKeys).ConfigureAwait(false);
        return entries
            .Select(kv => SafeDeserialize(kv.Value)?.Config)
            .Where(c => c != null)
            .Cast<AiProviderConfig>()
            .OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<bool> HasKeyAsync(string providerId)
    {
        var config = await GetConfigAsync(providerId).ConfigureAwait(false);
        return config?.HasKey ?? false;
    }

    public async Task<UnwrappedKey> UnwrapForCallAsync(string providerId)
    {
        var raw = await _store.GetAsync(StoreNames.AiKeys, providerId).ConfigureAwait(false);
        if (string.IsNullOrEmpty(raw))
        {
            throw new InvalidOperationException($"No AI key stored for provider '{providerId}'.");
        }
        var record = SafeDeserialize(raw!)
            ?? throw new InvalidOperationException($"AI key record for '{providerId}' is corrupt.");

        var wrapped = new WrappedSecret
        {
            Ciphertext = Convert.FromBase64String(record.Ciphertext),
            Iv = Convert.FromBase64String(record.Iv),
            Aad = Convert.FromBase64String(record.Aad),
        };
        var plain = await _crypto.UnwrapAsync(wrapped).ConfigureAwait(false);
        return new UnwrappedKey(plain);
    }

    public async Task RemoveAsync(string providerId)
    {
        if (string.IsNullOrEmpty(providerId)) return;

        // Best-effort zeroisation: overwrite the ciphertext fields with empty values
        // before delete to tighten the residual exposure window. (The GC may still
        // hold copies of the original bytes; this is a defence-in-depth measure
        // matching contracts/ai-key-wrapping.md "Remove key".)
        var raw = await _store.GetAsync(StoreNames.AiKeys, providerId).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(raw))
        {
            var zeroed = new PersistedConfig
            {
                Config = new AiProviderConfig { ProviderId = providerId },
                Ciphertext = string.Empty,
                Iv = string.Empty,
                Aad = string.Empty,
            };
            await _store.SetAsync(StoreNames.AiKeys, providerId, JsonSerializer.Serialize(zeroed))
                .ConfigureAwait(false);
        }
        await _store.DeleteAsync(StoreNames.AiKeys, providerId).ConfigureAwait(false);
    }

    internal static string AadFor(string providerId) => "akmlsql.aikey." + providerId;

    private static PersistedConfig? SafeDeserialize(string json)
    {
        try { return JsonSerializer.Deserialize<PersistedConfig>(json); }
        catch (JsonException) { return null; }
    }

    private sealed class PersistedConfig
    {
        public AiProviderConfig Config { get; set; } = new();
        public string Ciphertext { get; set; } = string.Empty;
        public string Iv { get; set; } = string.Empty;
        public string Aad { get; set; } = string.Empty;
    }
}
