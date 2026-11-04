using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M6 task T125. Stores AI agents — a provider, model, endpoint and
/// API key under a name — with the key in wrapped form per contracts/ai-key-wrapping.md. Uses
/// <see cref="IWebCryptoWrapper"/> with <c>aad = "akmlsql.aikey." + agentId</c>, so a record copied
/// to a different id fails decryption (contract Invariant 5).
///
/// <para>
/// 2026-11 master–detail redesign: a record is one AGENT, keyed by its own id, so one provider
/// can carry several agents ("GPT-4.1" and "GPT-4o mini", both OpenAI). Records saved before
/// that were keyed by their provider id ("openai"); that key simply stays their agent id — the
/// wrapped key, its AAD and the active choice all keep working with no migration step.
/// </para>
///
/// <para>
/// Plaintext keys are returned ONLY by <see cref="UnwrapForCallAsync"/> and MUST be used inside a
/// short-lived scope. Per the contract: never persist the unwrapped value, never put it in a
/// long-lived field, never log it.
/// </para>
/// </summary>
public interface IAiKeyVault
{
    /// <summary>Wrap and persist a freshly-entered API key with the agent <paramref name="agentId"/>.</summary>
    Task SetKeyAsync(string agentId, AiProviderConfig config, string apiKeyPlain);

    /// <summary>
    /// Read the persisted (non-secret) agent config — provider, model, endpoint and so on. The
    /// wrapped key is NOT returned here.
    /// </summary>
    Task<AiProviderConfig?> GetConfigAsync(string agentId);

    /// <summary>Every saved agent, oldest first.</summary>
    Task<IReadOnlyList<AiProviderConfig>> ListAsync();

    /// <summary>True iff a wrapped key is stored for this agent.</summary>
    Task<bool> HasKeyAsync(string agentId);

    /// <summary>
    /// Unwrap the API key for a single provider call. The returned <see cref="UnwrappedKey"/>
    /// MUST be disposed promptly -- it overwrites its internal byte buffer on Dispose
    /// (best-effort against memory dumps).
    /// </summary>
    Task<UnwrappedKey> UnwrapForCallAsync(string agentId);

    /// <summary>Remove the wrapped key + agent config. Best-effort zeroise before delete.</summary>
    Task RemoveAsync(string agentId);

    /// <summary>
    /// Spec 041 (R49) — Settings › Import, and the record of a connection test: write an agent's
    /// non-secret fields (keyed by <see cref="AiProviderConfig.Id"/>, else its provider id). An
    /// existing wrapped key is kept; a new agent is created without one (<c>HasKey = false</c>,
    /// whatever the import says) — an exported file never carries a key.
    /// </summary>
    Task UpsertConfigAsync(AiProviderConfig config);

    /// <summary>
    /// The agent editor's Save: <paramref name="config"/>'s <see cref="AiProviderConfig.Id"/> names
    /// the record. A non-empty <paramref name="apiKeyPlain"/> is wrapped and stored; an empty one
    /// keeps the stored key while the provider is unchanged — a key belongs to its provider, so a
    /// provider switch without a new key leaves the agent with none.
    /// </summary>
    Task SaveAgentAsync(AiProviderConfig config, string? apiKeyPlain);

    /// <summary>
    /// Duplicate: stores <paramref name="copy"/> (its own id) with the wrapped key of
    /// <paramref name="sourceAgentId"/>, re-wrapped for the copy's id.
    /// </summary>
    Task DuplicateAsync(string sourceAgentId, AiProviderConfig copy);
}

/// <summary>
/// One saved AI agent. Mirrors data-model.md E8 (AiProviderConfig), extended by the 2026-11
/// master–detail redesign with the agent's own <see cref="Id"/>, an on/off switch and the last
/// connection check. The wrapped key fields live alongside on the persistence side; the public
/// surface of <see cref="IAiKeyVault"/> never exposes them.
/// </summary>
public sealed class AiProviderConfig
{
    /// <summary>
    /// The agent's id — the record's key. A 32-hex id for agents added since 2026-11; the provider
    /// id ("openai") for agents saved before, which were one per provider.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    public string ProviderId { get; set; } = string.Empty;   // "openai", "anthropic", "gemini", "azure", "ollama", "lmstudio"

    /// <summary>The agent's name, unique among agents.</summary>
    public string DisplayName { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;
    public string? Endpoint { get; set; }    // required for Azure, Ollama, LM Studio
    public bool HasKey { get; set; }         // true iff a wrapped key is stored

    /// <summary>A turned-off agent stays in the list but cannot be the active one.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>ISO 8601 UTC creation time; orders the list. Empty for agents saved before 2026-11.</summary>
    public string CreatedUtc { get; set; } = string.Empty;

    /// <summary>The last Test connection, or null when never tested.</summary>
    public AiAgentCheck? LastCheck { get; set; }
}

/// <summary>The outcome of an agent's last Test connection. Never holds the key.</summary>
public sealed class AiAgentCheck
{
    public const string Ready = "ready";
    public const string Failed = "failed";

    /// <summary><see cref="Ready"/> or <see cref="Failed"/>.</summary>
    public string Status { get; set; } = Failed;

    /// <summary>ISO 8601 UTC time of the check.</summary>
    public string CheckedUtc { get; set; } = string.Empty;

    /// <summary>The round trip of a successful check; 0 after a failure.</summary>
    public int LatencyMs { get; set; }

    /// <summary>What the check said, at most 300 characters.</summary>
    public string Message { get; set; } = string.Empty;
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

    public async Task SetKeyAsync(string agentId, AiProviderConfig config, string apiKeyPlain)
    {
        if (string.IsNullOrEmpty(agentId)) throw new ArgumentException("agentId is required.", nameof(agentId));

        config.Id = agentId;
        // Callers from before agents had ids pass the provider id as the key and leave ProviderId empty.
        if (string.IsNullOrEmpty(config.ProviderId)) config.ProviderId = agentId;
        PersistedConfig record;
        if (string.IsNullOrEmpty(apiKeyPlain))
        {
            // Local providers (Ollama, LM Studio) don't use an API key. Persist the
            // config without wrapping anything; HasKey stays false.
            config.HasKey = false;
            record = new PersistedConfig { Config = config };
        }
        else
        {
            record = await WrapAsync(config, apiKeyPlain).ConfigureAwait(false);
        }

        await WriteAsync(agentId, record).ConfigureAwait(false);
    }

    public async Task SaveAgentAsync(AiProviderConfig config, string? apiKeyPlain)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrEmpty(config.Id)) throw new ArgumentException("The agent id is required.", nameof(config));

        if (!string.IsNullOrEmpty(apiKeyPlain))
        {
            await WriteAsync(config.Id, await WrapAsync(config, apiKeyPlain).ConfigureAwait(false)).ConfigureAwait(false);
            return;
        }

        var existing = await ReadAsync(config.Id).ConfigureAwait(false);
        var keepKey = existing != null && existing.Config.HasKey && !string.IsNullOrEmpty(existing.Ciphertext)
                      && string.Equals(existing.Config.ProviderId, config.ProviderId, StringComparison.Ordinal);
        config.HasKey = keepKey;
        await WriteAsync(config.Id, new PersistedConfig
        {
            Config = config,
            Ciphertext = keepKey ? existing!.Ciphertext : string.Empty,
            Iv = keepKey ? existing!.Iv : string.Empty,
            Aad = keepKey ? existing!.Aad : string.Empty,
        }).ConfigureAwait(false);
    }

    public async Task DuplicateAsync(string sourceAgentId, AiProviderConfig copy)
    {
        if (copy == null) throw new ArgumentNullException(nameof(copy));
        if (string.IsNullOrEmpty(copy.Id)) throw new ArgumentException("The copy's id is required.", nameof(copy));

        var source = await ReadAsync(sourceAgentId).ConfigureAwait(false);
        if (source == null || !source.Config.HasKey || string.IsNullOrEmpty(source.Ciphertext))
        {
            copy.HasKey = false;
            await WriteAsync(copy.Id, new PersistedConfig { Config = copy }).ConfigureAwait(false);
            return;
        }

        // Re-wrap for the copy's id: the AAD binds a key to its record (Invariant 5).
        using var plain = await UnwrapForCallAsync(sourceAgentId).ConfigureAwait(false);
        await WriteAsync(copy.Id, await WrapAsync(copy, plain.Value).ConfigureAwait(false)).ConfigureAwait(false);
    }

    public async Task<AiProviderConfig?> GetConfigAsync(string agentId)
    {
        if (string.IsNullOrEmpty(agentId)) return null;
        return (await ReadAsync(agentId).ConfigureAwait(false))?.Config;
    }

    public async Task<IReadOnlyList<AiProviderConfig>> ListAsync()
    {
        var entries = await _store.ListAsync(StoreNames.AiKeys).ConfigureAwait(false);
        var configs = new List<AiProviderConfig>();
        foreach (var entry in entries)
        {
            var record = SafeDeserialize(entry.Value);
            if (record?.Config == null || string.IsNullOrEmpty(record.Config.ProviderId)) continue;   // the "_active" pointer and junk
            if (string.IsNullOrEmpty(record.Config.Id)) record.Config.Id = entry.Key;
            configs.Add(record.Config);
        }
        // Agents saved before 2026-11 carry no creation time: they sort first, by name.
        return configs
            .OrderBy(c => c.CreatedUtc, StringComparer.Ordinal)
            .ThenBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<bool> HasKeyAsync(string agentId)
    {
        var config = await GetConfigAsync(agentId).ConfigureAwait(false);
        return config?.HasKey ?? false;
    }

    public async Task<UnwrappedKey> UnwrapForCallAsync(string agentId)
    {
        var raw = await _store.GetAsync(StoreNames.AiKeys, agentId).ConfigureAwait(false);
        if (string.IsNullOrEmpty(raw))
        {
            throw new InvalidOperationException($"No AI key stored for agent '{agentId}'.");
        }
        var record = SafeDeserialize(raw!)
            ?? throw new InvalidOperationException($"AI key record for '{agentId}' is corrupt.");

        // Defence-in-depth: local providers (Ollama / LM Studio) persist a config with
        // HasKey=false and empty ciphertext/iv/aad. Calling _crypto.UnwrapAsync on those would
        // throw a low-level Web Crypto OperationError that doesn't tell the caller what's wrong.
        if (record.Config == null || !record.Config.HasKey || string.IsNullOrEmpty(record.Ciphertext))
        {
            throw new InvalidOperationException(
                $"Agent '{agentId}' has no API key — check HasKey before calling Unwrap.");
        }

        var wrapped = new WrappedSecret
        {
            Ciphertext = Convert.FromBase64String(record.Ciphertext),
            Iv = Convert.FromBase64String(record.Iv),
            Aad = Convert.FromBase64String(record.Aad),
        };
        var plain = await _crypto.UnwrapAsync(wrapped).ConfigureAwait(false);
        return new UnwrappedKey(plain);
    }

    public async Task RemoveAsync(string agentId)
    {
        if (string.IsNullOrEmpty(agentId)) return;

        // Best-effort zeroisation: overwrite the ciphertext fields with empty values before
        // delete to tighten the residual exposure window (contracts/ai-key-wrapping.md "Remove key").
        var raw = await _store.GetAsync(StoreNames.AiKeys, agentId).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(raw))
        {
            var zeroed = new PersistedConfig { Config = new AiProviderConfig { Id = agentId } };
            await WriteAsync(agentId, zeroed).ConfigureAwait(false);
        }
        await _store.DeleteAsync(StoreNames.AiKeys, agentId).ConfigureAwait(false);
    }

    public async Task UpsertConfigAsync(AiProviderConfig config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrEmpty(config.ProviderId)) throw new ArgumentException("ProviderId is required.", nameof(config));

        var id = string.IsNullOrEmpty(config.Id) ? config.ProviderId : config.Id;
        var existing = await ReadAsync(id).ConfigureAwait(false);
        bool hasKey = existing != null && existing.Config.HasKey && !string.IsNullOrEmpty(existing.Ciphertext);
        var record = new PersistedConfig
        {
            Config = new AiProviderConfig
            {
                Id = id,
                ProviderId = config.ProviderId,
                DisplayName = config.DisplayName ?? string.Empty,
                Model = config.Model ?? string.Empty,
                Endpoint = config.Endpoint,
                HasKey = hasKey,
                Enabled = config.Enabled,
                CreatedUtc = string.IsNullOrEmpty(config.CreatedUtc) ? existing?.Config.CreatedUtc ?? string.Empty : config.CreatedUtc,
                LastCheck = config.LastCheck,
            },
            Ciphertext = hasKey ? existing!.Ciphertext : string.Empty,
            Iv = hasKey ? existing!.Iv : string.Empty,
            Aad = hasKey ? existing!.Aad : string.Empty,
        };
        await WriteAsync(id, record).ConfigureAwait(false);
    }

    internal static string AadFor(string agentId) => "akmlsql.aikey." + agentId;

    /// <summary>The provider id inside a stored agent record, or null when the text is not one.</summary>
    internal static string? ProviderIdOf(string? recordJson)
    {
        if (string.IsNullOrEmpty(recordJson)) return null;
        var provider = SafeDeserialize(recordJson)?.Config?.ProviderId;
        return string.IsNullOrEmpty(provider) ? null : provider;
    }

    private async Task<PersistedConfig> WrapAsync(AiProviderConfig config, string apiKeyPlain)
    {
        var wrapped = await _crypto.WrapAsync(apiKeyPlain, AadFor(config.Id)).ConfigureAwait(false);
        config.HasKey = true;
        return new PersistedConfig
        {
            Config = config,
            Ciphertext = Convert.ToBase64String(wrapped.Ciphertext),
            Iv = Convert.ToBase64String(wrapped.Iv),
            Aad = Convert.ToBase64String(wrapped.Aad),
        };
    }

    private async Task<PersistedConfig?> ReadAsync(string agentId)
    {
        var raw = await _store.GetAsync(StoreNames.AiKeys, agentId).ConfigureAwait(false);
        if (string.IsNullOrEmpty(raw)) return null;
        var record = SafeDeserialize(raw!);
        if (record?.Config != null && string.IsNullOrEmpty(record.Config.Id)) record.Config.Id = agentId;
        return record;
    }

    private Task WriteAsync(string agentId, PersistedConfig record) =>
        _store.SetAsync(StoreNames.AiKeys, agentId, JsonSerializer.Serialize(record));

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
