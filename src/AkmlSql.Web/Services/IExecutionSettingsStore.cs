using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 030 — Phase 5. Persists the user's execute resource caps (max rows / command timeout) to
/// IndexedDB, mirroring <see cref="IAnalysisSettingsStore"/>'s GetAsync/SetAsync POCO shape. These
/// caps are ADVISORY — the engine re-clamps them to its own ceilings on every execute (locked
/// constraint #6), so a tampered value can never exceed the engine limit. The toolbar seeds its
/// numeric inputs from here and writes them back on change.
/// </summary>
public interface IExecutionSettingsStore
{
    Task<ExecutionSettings> GetAsync();
    Task SetAsync(ExecutionSettings settings);
}

/// <summary>User-controlled execute caps. Defaults match the engine defaults.</summary>
public sealed class ExecutionSettings
{
    public int MaxRows { get; set; } = 1000;
    public int CommandTimeoutSeconds { get; set; } = 30;
}

internal sealed class ExecutionSettingsStore : IExecutionSettingsStore
{
    // Reuse the existing analysisSettings object-store with a distinct key so no IndexedDB version
    // bump (and its multi-tab "blocked" risk) is needed. The store is a flat key→JSON map.
    private const string Key = "executionSettings";
    private readonly IIndexedDbAdapter _store;
    private ExecutionSettings? _cached;

    public ExecutionSettingsStore(IIndexedDbAdapter store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<ExecutionSettings> GetAsync()
    {
        if (_cached != null) return _cached;

        var raw = await _store.GetAsync(StoreNames.AnalysisSettings, Key).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(raw))
        {
            try
            {
                _cached = JsonSerializer.Deserialize<ExecutionSettings>(raw) ?? new ExecutionSettings();
                return _cached;
            }
            catch (JsonException)
            {
                // fall through to defaults.
            }
        }
        _cached = new ExecutionSettings();
        return _cached;
    }

    public async Task SetAsync(ExecutionSettings settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        _cached = settings;
        await _store.SetAsync(StoreNames.AnalysisSettings, Key,
            JsonSerializer.Serialize(settings)).ConfigureAwait(false);
    }
}
