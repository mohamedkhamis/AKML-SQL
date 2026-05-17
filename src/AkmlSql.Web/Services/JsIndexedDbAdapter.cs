using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M2 foundation. Production binding of
/// <see cref="IIndexedDbAdapter"/> backed by the browser's IndexedDB API via the
/// <c>akml-indexeddb.js</c> shim. The shim is loaded once on first use and exposes
/// <c>get / set / delete / list / clear</c> against a single IndexedDB database
/// (<c>AkmlSqlWeb</c>) with one object-store per <see cref="StoreNames"/> entry.
///
/// All operations are thin wrappers over the matching JS export; failures bubble
/// as <see cref="JSException"/>.
/// </summary>
internal sealed class JsIndexedDbAdapter : IIndexedDbAdapter
{
    private readonly IJSRuntime _js;
    private readonly Lazy<Task<IJSObjectReference>> _moduleLoader;

    public JsIndexedDbAdapter(IJSRuntime js)
    {
        _js = js ?? throw new ArgumentNullException(nameof(js));
        _moduleLoader = new Lazy<Task<IJSObjectReference>>(LoadModule);
    }

    private Task<IJSObjectReference> LoadModule() => _js
        .InvokeAsync<IJSObjectReference>("import", "./js/akml-indexeddb.js")
        .AsTask();

    public async Task<string?> GetAsync(string store, string key)
    {
        var mod = await _moduleLoader.Value.ConfigureAwait(false);
        return await mod.InvokeAsync<string?>("get", store, key).ConfigureAwait(false);
    }

    public async Task SetAsync(string store, string key, string valueJson)
    {
        var mod = await _moduleLoader.Value.ConfigureAwait(false);
        await mod.InvokeVoidAsync("set", store, key, valueJson).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string store, string key)
    {
        var mod = await _moduleLoader.Value.ConfigureAwait(false);
        await mod.InvokeVoidAsync("del", store, key).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<KeyValuePair<string, string>>> ListAsync(string store)
    {
        var mod = await _moduleLoader.Value.ConfigureAwait(false);
        // The JS shim returns an array of { key, value } objects; we deserialise to a
        // KeyValuePair list rather than allocating a wrapper DTO.
        var raw = await mod.InvokeAsync<JsRecord[]>("list", store).ConfigureAwait(false);
        var output = new KeyValuePair<string, string>[raw.Length];
        for (int i = 0; i < raw.Length; i++)
        {
            output[i] = new KeyValuePair<string, string>(raw[i].Key, raw[i].Value);
        }
        return output;
    }

    public async Task ClearAsync(string store)
    {
        var mod = await _moduleLoader.Value.ConfigureAwait(false);
        await mod.InvokeVoidAsync("clear", store).ConfigureAwait(false);
    }

    private sealed class JsRecord
    {
        [System.Text.Json.Serialization.JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("value")] public string Value { get; set; } = string.Empty;
    }
}

/// <summary>
/// Canonical store-name constants. Centralised here so a typo at the call site cannot
/// silently spawn a phantom store.
/// </summary>
public static class StoreNames
{
    public const string Profiles = "profiles";
    public const string AnalysisSettings = "analysisSettings";
    public const string EditorSession = "editorSession";
    public const string Diagnostics = "diagnostics";
    public const string ThemePreference = "themePreference";

    // Reserved for M5 / M6 (not used in M2 but listed here so the JS shim knows about them).
    public const string SchemaEntries = "schemaEntries";
    public const string AiKeys = "aiKeys";

    // Spec 021 M3 -- bridge state.
    public const string Connections = "connections";
    public const string PairingTokens = "pairingTokens";

    // Spec 021 M3/M6 -- non-extractable wrap-key handle.
    public const string KeyMaterial = "keyMaterial";
}
