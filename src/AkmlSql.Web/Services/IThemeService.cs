using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M2 task T034. Reads OS <c>prefers-color-scheme</c> and a user
/// override from IndexedDB (<c>ThemePreference</c> per data-model.md E10), then drives the
/// theme via an <see cref="IThemeApplier"/> abstraction. The production applier
/// (<see cref="JsThemeApplier"/>) calls into <c>akml-theme.js</c>, which swaps the
/// <c>&lt;link&gt;</c> tag's <c>href</c> and listens for OS-theme changes.
/// </summary>
public interface IThemeService
{
    /// <summary>The current mode (resolved -- "system" surfaces as the OS preference).</summary>
    ThemeMode Current { get; }

    /// <summary>Raised whenever <see cref="Current"/> changes.</summary>
    event Action? Changed;

    /// <summary>Initial mount: pull the stored override and apply.</summary>
    Task InitializeAsync();

    /// <summary>Update the user override; persists to IndexedDB and re-applies.</summary>
    Task SetAsync(ThemeMode mode);
}

public enum ThemeMode { System, Light, Dark, HighContrast }

/// <summary>
/// Strategy used by <see cref="ThemeService"/> to drive the browser's effective theme.
/// Implementations: <see cref="JsThemeApplier"/> (production, JS interop) and an in-memory
/// no-op used by tests.
/// </summary>
public interface IThemeApplier
{
    Task ApplyAsync(ThemeMode mode);
}

internal sealed class JsThemeApplier : IThemeApplier
{
    private readonly IJSRuntime _js;
    private readonly Lazy<Task<IJSObjectReference>> _moduleLoader;

    public JsThemeApplier(IJSRuntime js)
    {
        _js = js ?? throw new ArgumentNullException(nameof(js));
        _moduleLoader = new Lazy<Task<IJSObjectReference>>(() =>
            _js.InvokeAsync<IJSObjectReference>("import", "./js/akml-theme.js").AsTask());
    }

    public async Task ApplyAsync(ThemeMode mode)
    {
        var mod = await _moduleLoader.Value.ConfigureAwait(false);
        await mod.InvokeVoidAsync("apply", ToToken(mode)).ConfigureAwait(false);
    }

    internal static string ToToken(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => "light",
        ThemeMode.Dark => "dark",
        ThemeMode.HighContrast => "high-contrast",
        _ => "system",
    };
}

internal sealed class ThemeService : IThemeService
{
    private const string PreferenceKey = "current";
    private readonly IIndexedDbAdapter _store;
    private readonly IThemeApplier _applier;

    public ThemeService(IIndexedDbAdapter store, IThemeApplier applier)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _applier = applier ?? throw new ArgumentNullException(nameof(applier));
    }

    public ThemeMode Current { get; private set; } = ThemeMode.System;
    public event Action? Changed;

    public async Task InitializeAsync()
    {
        var stored = await _store.GetAsync(StoreNames.ThemePreference, PreferenceKey).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(stored))
        {
            try
            {
                var record = JsonSerializer.Deserialize<ThemePreferenceRecord>(stored);
                if (record != null) Current = record.Mode;
            }
            catch (JsonException)
            {
                // Corrupt record -- ignore and stay on System default.
            }
        }
        await _applier.ApplyAsync(Current).ConfigureAwait(false);
    }

    public async Task SetAsync(ThemeMode mode)
    {
        if (mode == Current) return;
        Current = mode;
        await _store.SetAsync(StoreNames.ThemePreference, PreferenceKey,
            JsonSerializer.Serialize(new ThemePreferenceRecord { Mode = mode })).ConfigureAwait(false);
        await _applier.ApplyAsync(mode).ConfigureAwait(false);
        Changed?.Invoke();
    }

    private sealed class ThemePreferenceRecord
    {
        public ThemeMode Mode { get; set; }
    }
}
