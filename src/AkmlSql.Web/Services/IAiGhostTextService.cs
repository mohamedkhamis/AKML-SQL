using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Engine.Ai.Prompts;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 028 (M6) task T031 (US5). Direct-to-provider inline ghost-text completion. Reuses the
/// shared <see cref="GhostTextPrompt"/> (the engine path is unavailable — keys are
/// browser-side). Caches by prompt+prefix (FR-025), rate-limits requests (FR-027), counts
/// session requests (FR-028), and honours the active privacy mode incl. the fully-local guard
/// (FR-029/FR-004). Ghost text fails <b>silently</b> (returns null) — it must never throw into
/// the editor's typing path.
/// </summary>
public interface IAiGhostTextService
{
    /// <summary>
    /// Return an inline suggestion for the text before the cursor, or null when no suggestion
    /// should be shown (disabled, no/incapable provider, rate-limited, fully-local-violated, or
    /// the provider failed). Cache hits do not consume the rate limit.
    /// </summary>
    Task<string?> CompleteAsync(string precedingText, CancellationToken ct);

    /// <summary>Per-session count of provider requests issued (FR-028 usage counter).</summary>
    int SessionRequestCount { get; }
}

internal sealed class AiGhostTextService : IAiGhostTextService
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(3);

    private readonly IAiClientFactory _client;
    private readonly IAiPreference _preference;
    private readonly IAiSchemaContextProvider _schema;
    private readonly IAiFeatureSettings _settings;
    private readonly Func<DateTimeOffset> _clock;

    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);
    private readonly Queue<DateTimeOffset> _recent = new();

    public AiGhostTextService(
        IAiClientFactory client, IAiPreference preference,
        IAiSchemaContextProvider schema, IAiFeatureSettings settings)
        : this(client, preference, schema, settings, () => DateTimeOffset.UtcNow)
    {
    }

    internal AiGhostTextService(
        IAiClientFactory client, IAiPreference preference,
        IAiSchemaContextProvider schema, IAiFeatureSettings settings, Func<DateTimeOffset> clock)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _preference = preference ?? throw new ArgumentNullException(nameof(preference));
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public int SessionRequestCount { get; private set; }

    public async Task<string?> CompleteAsync(string precedingText, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(precedingText)) return null;

        var settings = await _settings.GetAsync().ConfigureAwait(false);
        if (!settings.GhostTextEnabled) return null; // opt-in (FR-027)

        // Schema is read from the local cache (no provider call), so the cache key is cheap.
        var schemaText = await _schema.GetSchemaTextAsync("ghosttext", precedingText, ct).ConfigureAwait(false);
        var key = schemaText.GetHashCode().ToString("x") + "" + precedingText;

        // Cache hit: serve immediately, no provider call, no rate-limit consumption (FR-025).
        if (_cache.TryGetValue(key, out var cached)) return cached;

        // Rate limit (FR-027): at most N requests per rolling 3s window.
        var now = _clock();
        while (_recent.Count > 0 && now - _recent.Peek() > Window) _recent.Dequeue();
        var max = Math.Max(1, settings.GhostTextMaxRequestsPer3s);
        if (_recent.Count >= max) return null;

        var providerId = await _preference.GetActiveAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(providerId)) return null;
        if (!_client.IsBrowserDirectCapable(providerId)) return null; // OpenAI/Azure: no ghost text

        var mode = await _settings.ResolveModeAsync("ghosttext").ConfigureAwait(false);
        if (mode == AiPrivacyMode.FullyLocal && !AiProviders.IsLocal(providerId)) return null; // silent FR-004

        _recent.Enqueue(now);
        SessionRequestCount++;

        var (system, user) = GhostTextPrompt.Build(schemaText, precedingText);
        string raw;
        try
        {
            raw = await _client.SendAsync(providerId, new AiChatRequest
            {
                SystemPrompt = system,
                UserPrompt = user,
                MaxTokens = 150,
                Temperature = 0.2,
            }, ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null; // ghost text never surfaces an error into the editor
        }

        var suggestion = StripFences(raw).TrimEnd();
        if (string.IsNullOrWhiteSpace(suggestion)) return null; // don't cache an empty suggestion
        _cache[key] = suggestion;
        return suggestion;
    }

    private static string StripFences(string s)
    {
        if (string.IsNullOrEmpty(s) || !s.Contains("```", StringComparison.Ordinal)) return s;
        var kept = s.Replace("\r\n", "\n").Split('\n').Where(l => !l.TrimStart().StartsWith("```", StringComparison.Ordinal));
        return string.Join("\n", kept);
    }
}
