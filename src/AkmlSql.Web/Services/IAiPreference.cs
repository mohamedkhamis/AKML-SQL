using System;
using System.Threading.Tasks;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M6 task T126. Singleton record of the active AI provider
/// (the one Editor.razor uses for AI actions). Persisted to the
/// <see cref="StoreNames.AiKeys"/> store under a sentinel key.
/// </summary>
public interface IAiPreference
{
    /// <summary>The currently active provider id (e.g. "openai"). Empty when none selected.</summary>
    Task<string> GetActiveAsync();

    Task SetActiveAsync(string providerId);
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

    public Task SetActiveAsync(string providerId) =>
        _store.SetAsync(StoreNames.AiKeys, ActiveKey, providerId ?? string.Empty);
}
