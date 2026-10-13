using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 030 — Phase 5. Persists the user's execute resource caps (max rows / command timeout) to
/// IndexedDB, mirroring <see cref="IAnalysisSettingsStore"/>'s GetAsync/SetAsync POCO shape. These
/// caps are ADVISORY — the engine re-clamps them to its own ceilings on every execute (locked
/// constraint #6), so a tampered value can never exceed the engine limit.
///
/// <para>Spec 041 (research R52, FR-052): Settings › Queries owns the saved defaults. The toolbar's quick
/// control writes <see cref="SessionOverride"/> only — in memory, for this tab, never saved — and the editor
/// runs with <see cref="GetEffectiveAsync"/>. Saving a default clears the override.</para>
/// </summary>
public interface IExecutionSettingsStore
{
    /// <summary>The saved defaults.</summary>
    Task<ExecutionSettings> GetAsync();

    /// <summary>Save the defaults; clears <see cref="SessionOverride"/>.</summary>
    Task SetAsync(ExecutionSettings settings);

    /// <summary>The toolbar's row limit / timeout for this session; null when it follows the defaults.</summary>
    SessionOverride? SessionOverride { get; set; }

    /// <summary>The saved defaults with <see cref="SessionOverride"/> applied — what an execution uses.</summary>
    Task<ExecutionSettings> GetEffectiveAsync();

    /// <summary>Restore defaults: delete the saved record (only its own key) and drop the override.</summary>
    Task ResetAsync();

    /// <summary>Forget the cached record, so the next read sees what another tab saved.</summary>
    Task ReloadAsync();
}

/// <summary>Spec 041 (R52) — the toolbar's quick control: a value per cap, null where it follows the default.</summary>
public sealed class SessionOverride
{
    public int? MaxRows { get; init; }
    public int? CommandTimeoutSeconds { get; init; }
}

/// <summary>User-controlled execute caps (defaults match the engine defaults) and results-grid options.
/// New fields are additive JSON: an older record reads them as their defaults.</summary>
public sealed class ExecutionSettings
{
    public int MaxRows { get; set; } = 1000;
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>Spec 041 (FR-011) — show each column's declared type as a second header line.</summary>
    public bool ShowColumnTypes { get; set; }

    /// <summary>Spec 041 (FR-009, FR-030, FR-052) — keep line breaks inside values on copy and Save results;
    /// off replaces them with a space, as SSMS does by default.</summary>
    public bool RetainLineBreaksOnCopy { get; set; }
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
        SessionOverride = null;
        await _store.SetAsync(StoreNames.AnalysisSettings, Key,
            JsonSerializer.Serialize(settings)).ConfigureAwait(false);
    }

    public SessionOverride? SessionOverride { get; set; }

    public async Task<ExecutionSettings> GetEffectiveAsync()
    {
        var saved = await GetAsync().ConfigureAwait(false);
        var o = SessionOverride;
        if (o == null) return saved;
        return new ExecutionSettings
        {
            MaxRows = o.MaxRows ?? saved.MaxRows,
            CommandTimeoutSeconds = o.CommandTimeoutSeconds ?? saved.CommandTimeoutSeconds,
            ShowColumnTypes = saved.ShowColumnTypes,
            RetainLineBreaksOnCopy = saved.RetainLineBreaksOnCopy,
        };
    }

    public async Task ResetAsync()
    {
        // The analysisSettings store is shared (current, executionSettings, editorSettings): delete our key only.
        await _store.DeleteAsync(StoreNames.AnalysisSettings, Key).ConfigureAwait(false);
        _cached = new ExecutionSettings();
        SessionOverride = null;
    }

    public Task ReloadAsync()
    {
        _cached = null;
        return Task.CompletedTask;
    }
}
