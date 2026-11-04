using System;
using System.Threading.Tasks;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M6 task T126. Singleton record of the active AI agent (the one
/// the editor's AI actions, the chat and ghost text use). Persisted to the
/// <see cref="StoreNames.AiKeys"/> store under a sentinel key.
///
/// <para>2026-11 master–detail redesign: the record holds an AGENT id. Agents saved before
/// that are keyed by their provider id, so an existing choice ("openai") keeps working.</para>
/// </summary>
public interface IAiPreference
{
    /// <summary>The active agent's id (e.g. "openai", or a 32-hex id). Empty when none is selected.</summary>
    Task<string> GetActiveAsync();

    Task SetActiveAsync(string agentId);

    /// <summary>
    /// The provider behind the active agent ("anthropic", "ollama"…) — what the privacy guards
    /// and the browser-direct check test. Empty when none is selected. The default suits a store
    /// where every agent is keyed by its provider id.
    /// </summary>
    async Task<string> GetActiveProviderAsync() => await GetActiveAsync().ConfigureAwait(false);
}

internal sealed class AiPreference : IAiPreference
{
    private const string ActiveKey = "_active";
    private readonly IIndexedDbAdapter _store;

    public AiPreference(IIndexedDbAdapter store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<string> GetActiveAsync()
    {
        var v = await _store.GetAsync(StoreNames.AiKeys, ActiveKey).ConfigureAwait(false);
        return v ?? string.Empty;
    }

    public Task SetActiveAsync(string agentId) =>
        _store.SetAsync(StoreNames.AiKeys, ActiveKey, agentId ?? string.Empty);

    public async Task<string> GetActiveProviderAsync()
    {
        var agentId = await GetActiveAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(agentId)) return string.Empty;
        // The agent record lives in the same store, under the agent's id.
        var record = await _store.GetAsync(StoreNames.AiKeys, agentId).ConfigureAwait(false);
        return AiKeyVault.ProviderIdOf(record) ?? agentId;
    }
}
