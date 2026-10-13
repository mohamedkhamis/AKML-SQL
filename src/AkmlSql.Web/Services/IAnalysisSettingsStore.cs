using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M2 task T044. Persists user-controlled analyser settings to
/// IndexedDB (data-model.md E5). The web edition does not honour the IDE plugin's
/// project-scoped <c>.casettings</c> file -- per-document overrides via inline
/// <c>-- akml-disable RuleId</c> comments are unchanged because the analyser engine reads
/// them directly from the document text.
/// </summary>
public interface IAnalysisSettingsStore
{
    /// <summary>The current (possibly mutated) settings. Cached after first read.</summary>
    Task<WebAnalysisSettings> GetAsync();

    /// <summary>Persist a new settings record.</summary>
    Task SetAsync(WebAnalysisSettings settings);

    /// <summary>Spec 041 (R47): Restore defaults — delete the record (key <c>current</c> only).</summary>
    Task ResetAsync();

    /// <summary>Spec 041: forget the cached record, so the next read sees what another tab saved.</summary>
    Task ReloadAsync();
}

/// <summary>
/// The web-edition view of the analyser settings. Mirrors the engine's
/// <c>CodeAnalysisSettings</c> but only exposes the fields the UI surfaces. Per-rule
/// severity overrides are kept as a flat map (RuleId -> "off" | "info" | "warning" | "error").
/// </summary>
public sealed class WebAnalysisSettings
{
    public bool Enabled { get; set; } = true;
    public bool AutoAnalyseOnFormat { get; set; } = true;
    public Dictionary<string, string> RuleOverrides { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Spec 041 (R51, FR-055) — the severities the Problems tab shows when it opens. Additive: an older
    /// record reads it as "everything".</summary>
    public ProblemsFilter ProblemsFilter { get; set; } = new();
}

/// <summary>Spec 041 (R51) — the Problems tab's default severity filters.</summary>
public sealed class ProblemsFilter
{
    public bool ShowInfo { get; set; } = true;
    public bool ShowWarning { get; set; } = true;
    public bool ShowError { get; set; } = true;
}

internal sealed class AnalysisSettingsStore : IAnalysisSettingsStore
{
    private const string Key = "current";
    private readonly IIndexedDbAdapter _store;
    private WebAnalysisSettings? _cached;

    public AnalysisSettingsStore(IIndexedDbAdapter store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<WebAnalysisSettings> GetAsync()
    {
        if (_cached != null) return _cached;

        var raw = await _store.GetAsync(StoreNames.AnalysisSettings, Key).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(raw))
        {
            try
            {
                _cached = JsonSerializer.Deserialize<WebAnalysisSettings>(raw) ?? new WebAnalysisSettings();
                _cached.ProblemsFilter ??= new ProblemsFilter();   // an explicit null in a hand-edited record
                _cached.RuleOverrides ??= new Dictionary<string, string>(StringComparer.Ordinal);
                return _cached;
            }
            catch (JsonException)
            {
                // fall through to defaults.
            }
        }
        _cached = new WebAnalysisSettings();
        return _cached;
    }

    public async Task SetAsync(WebAnalysisSettings settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        _cached = settings;
        await _store.SetAsync(StoreNames.AnalysisSettings, Key,
            JsonSerializer.Serialize(settings)).ConfigureAwait(false);
    }

    public async Task ResetAsync()
    {
        // The analysisSettings store is shared (current, executionSettings, editorSettings): delete our key only.
        await _store.DeleteAsync(StoreNames.AnalysisSettings, Key).ConfigureAwait(false);
        _cached = new WebAnalysisSettings();
    }

    public Task ReloadAsync()
    {
        _cached = null;
        return Task.CompletedTask;
    }
}
