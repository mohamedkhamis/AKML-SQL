using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 041 (research R53, FR-053) — the editor options of Settings › Editor: font size, word wrap and tab size.
/// Kept as key <c>editorSettings</c> in the <c>analysisSettings</c> object store (beside <c>current</c> and
/// <c>executionSettings</c>), so no IndexedDB version bump is needed. The editor applies them on create and on
/// <see cref="Changed"/>.
/// </summary>
public interface IEditorSettingsStore
{
    /// <summary>The current settings, already within their limits. Cached after the first read.</summary>
    Task<EditorSettings> GetAsync();

    /// <summary>Save (clamped to the limits) and raise <see cref="Changed"/>.</summary>
    Task SetAsync(EditorSettings settings);

    /// <summary>Restore defaults: delete the record (only its own key) and raise <see cref="Changed"/>.</summary>
    Task ResetAsync();

    /// <summary>Forget the cached record, so the next read sees what another tab saved.</summary>
    Task ReloadAsync();

    event Action? Changed;
}

/// <summary>The editor options. Font size 9–32 px, tab size 1–8.</summary>
public sealed class EditorSettings
{
    public const int MinFontSize = 9;
    public const int MaxFontSize = 32;
    public const int MinTabSize = 1;
    public const int MaxTabSize = 8;

    public int FontSize { get; set; } = 13;
    public bool WordWrap { get; set; }
    public int TabSize { get; set; } = 4;

    /// <summary>A copy within the limits.</summary>
    public EditorSettings Clamped() => new()
    {
        FontSize = Math.Clamp(FontSize, MinFontSize, MaxFontSize),
        WordWrap = WordWrap,
        TabSize = Math.Clamp(TabSize, MinTabSize, MaxTabSize),
    };
}

internal sealed class EditorSettingsStore : IEditorSettingsStore
{
    private const string Key = "editorSettings";
    private readonly IIndexedDbAdapter _store;
    private EditorSettings? _cached;

    public EditorSettingsStore(IIndexedDbAdapter store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public event Action? Changed;

    public async Task<EditorSettings> GetAsync()
    {
        if (_cached != null) return _cached;

        var raw = await _store.GetAsync(StoreNames.AnalysisSettings, Key).ConfigureAwait(false);
        EditorSettings? read = null;
        if (!string.IsNullOrEmpty(raw))
        {
            try { read = JsonSerializer.Deserialize<EditorSettings>(raw); }
            catch (JsonException) { /* defaults */ }
        }
        _cached = (read ?? new EditorSettings()).Clamped();
        return _cached;
    }

    public async Task SetAsync(EditorSettings settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        _cached = settings.Clamped();
        await _store.SetAsync(StoreNames.AnalysisSettings, Key, JsonSerializer.Serialize(_cached)).ConfigureAwait(false);
        Changed?.Invoke();
    }

    public async Task ResetAsync()
    {
        await _store.DeleteAsync(StoreNames.AnalysisSettings, Key).ConfigureAwait(false);
        _cached = new EditorSettings();
        Changed?.Invoke();
    }

    public Task ReloadAsync()
    {
        _cached = null;
        return Task.CompletedTask;
    }
}
