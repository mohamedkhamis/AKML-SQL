using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 028 (M6) task T006. The browser's AI privacy <b>disclosure</b> modes plus
/// ghost-text settings, persisted to the <see cref="StoreNames.AiFeatureSettings"/> store.
///
/// <para>
/// These four modes are a different axis than the engine's <c>PrivacyTransformer</c>
/// redaction modes (<c>full</c>/<c>schemaOnly</c>/<c>anonymous</c>): they control <i>how
/// much schema is disclosed</i> to the provider, not literal redaction or identifier
/// hashing (research Decision 1 / Reconciliation 2). The engine's <c>anonymous</c> mode is
/// out of scope for the browser.
/// </para>
/// </summary>
public enum AiPrivacyMode
{
    /// <summary>Full schema: tables + columns + FKs + descriptions.</summary>
    FullSchema = 0,

    /// <summary>Table + column names only; no data types, no foreign keys.</summary>
    SchemaNamesOnly = 1,

    /// <summary>No schema at all; only the user's SQL / prompt leaves the browser.</summary>
    NoSchema = 2,

    /// <summary>Full schema, but the provider is forced to a local one (Ollama / LM Studio).</summary>
    FullyLocal = 3,
}

/// <summary>Persisted AI feature settings: global + per-feature privacy mode, ghost-text knobs.</summary>
public sealed class AiFeatureSettings
{
    /// <summary>The default disclosure mode for any feature without an explicit override.</summary>
    public AiPrivacyMode GlobalDefaultMode { get; set; } = AiPrivacyMode.FullSchema;

    /// <summary>
    /// Per-feature overrides keyed by feature id
    /// (<c>explain</c>/<c>fix</c>/<c>optimize</c>/<c>texttosql</c>/<c>indexanalysis</c>/<c>chat</c>/<c>ghosttext</c>).
    /// Absent ⇒ use <see cref="GlobalDefaultMode"/>.
    /// </summary>
    public Dictionary<string, AiPrivacyMode> FeatureModeOverrides { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Ghost Text master switch. Opt-in — off by default (parity with the WPF surface).</summary>
    public bool GhostTextEnabled { get; set; }

    /// <summary>Debounce after the last keystroke before requesting a ghost completion.</summary>
    public int GhostTextDelayMs { get; set; } = 350;

    /// <summary>Rate limit for ghost completions (requests per 3 seconds of active typing).</summary>
    public int GhostTextMaxRequestsPer3s { get; set; } = 1;

    /// <summary>Resolve the effective mode for a feature: per-feature override, else the global default.</summary>
    public AiPrivacyMode Resolve(string featureId) =>
        !string.IsNullOrEmpty(featureId) && FeatureModeOverrides.TryGetValue(featureId, out var mode)
            ? mode
            : GlobalDefaultMode;
}

/// <summary>
/// Singleton accessor for <see cref="AiFeatureSettings"/>, mirroring <see cref="IAnalysisSettingsStore"/>:
/// cached after first read, persisted to IndexedDB on write.
/// </summary>
public interface IAiFeatureSettings
{
    /// <summary>The current settings. Cached after first read.</summary>
    Task<AiFeatureSettings> GetAsync();

    /// <summary>Persist a new settings record (invalidates the cache).</summary>
    Task SetAsync(AiFeatureSettings settings);

    /// <summary>Convenience: resolve the effective disclosure mode for a feature id.</summary>
    Task<AiPrivacyMode> ResolveModeAsync(string featureId);
}

internal sealed class AiFeatureSettingsStore : IAiFeatureSettings
{
    private const string Key = "current";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IIndexedDbAdapter _store;
    private AiFeatureSettings? _cached;

    public AiFeatureSettingsStore(IIndexedDbAdapter store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<AiFeatureSettings> GetAsync()
    {
        if (_cached != null) return _cached;

        var raw = await _store.GetAsync(StoreNames.AiFeatureSettings, Key).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(raw))
        {
            try
            {
                _cached = JsonSerializer.Deserialize<AiFeatureSettings>(raw, JsonOptions) ?? new AiFeatureSettings();
                return _cached;
            }
            catch (JsonException)
            {
                // fall through to defaults.
            }
        }
        _cached = new AiFeatureSettings();
        return _cached;
    }

    public async Task SetAsync(AiFeatureSettings settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        _cached = settings;
        await _store.SetAsync(StoreNames.AiFeatureSettings, Key,
            JsonSerializer.Serialize(settings, JsonOptions)).ConfigureAwait(false);
    }

    public async Task<AiPrivacyMode> ResolveModeAsync(string featureId)
    {
        var settings = await GetAsync().ConfigureAwait(false);
        return settings.Resolve(featureId);
    }
}
