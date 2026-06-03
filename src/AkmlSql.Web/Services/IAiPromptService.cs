using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Engine.Ai.Prompts;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M6 task T129; extended by spec 028 (M6) tasks T009/T015/T026.
/// Builds AI prompts using the extracted <c>AkmlSql.AI</c> prompt builders, then sends them
/// via <see cref="IAiClientFactory"/>.
///
/// <para>
/// Schema context is resolved internally per feature via <see cref="IAiSchemaContextProvider"/>,
/// honouring that feature's active privacy disclosure mode (US1). Each feature has a buffered
/// method and a streaming overload; the streaming path yields text deltas (US2), and the
/// buffered method is the fallback when a provider/mode does not stream. The fully-local guard
/// (FR-004/FR-012) is enforced on both paths.
/// </para>
/// </summary>
public interface IAiPromptService
{
    Task<string> ExplainAsync(string selectedSql, CancellationToken ct);
    Task<string> FixAsync(string failingSql, string errorMessage, int errorNumber, CancellationToken ct);
    Task<string> OptimizeAsync(string selectedSql, CancellationToken ct);
    Task<string> TextToSqlAsync(string naturalLanguage, CancellationToken ct);

    /// <summary>Index Analysis (spec 028 T026): suggests CREATE INDEX statements for the query.</summary>
    Task<string> IndexAnalysisAsync(string selectedSql, CancellationToken ct);

    IAsyncEnumerable<string> ExplainStreamAsync(string selectedSql, CancellationToken ct);
    IAsyncEnumerable<string> FixStreamAsync(string failingSql, string errorMessage, int errorNumber, CancellationToken ct);
    IAsyncEnumerable<string> OptimizeStreamAsync(string selectedSql, CancellationToken ct);
    IAsyncEnumerable<string> TextToSqlStreamAsync(string naturalLanguage, CancellationToken ct);
    IAsyncEnumerable<string> IndexAnalysisStreamAsync(string selectedSql, CancellationToken ct);
}

internal sealed class AiPromptService : IAiPromptService
{
    private readonly IAiClientFactory _client;
    private readonly IAiPreference _preference;
    private readonly IAiSchemaContextProvider _schema;
    private readonly IAiFeatureSettings _settings;

    public AiPromptService(
        IAiClientFactory client,
        IAiPreference preference,
        IAiSchemaContextProvider schema,
        IAiFeatureSettings settings)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _preference = preference ?? throw new ArgumentNullException(nameof(preference));
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    // ── Buffered ─────────────────────────────────────────────────────────────────────────

    public async Task<string> ExplainAsync(string selectedSql, CancellationToken ct)
    {
        var (system, user) = await BuildExplainAsync(selectedSql, ct).ConfigureAwait(false);
        return await CallAsync("explain", system, user, ct).ConfigureAwait(false);
    }

    public async Task<string> FixAsync(string failingSql, string errorMessage, int errorNumber, CancellationToken ct)
    {
        var (system, user) = await BuildFixAsync(failingSql, errorMessage, errorNumber, ct).ConfigureAwait(false);
        return await CallAsync("fix", system, user, ct).ConfigureAwait(false);
    }

    public async Task<string> OptimizeAsync(string selectedSql, CancellationToken ct)
    {
        var (system, user) = await BuildOptimizeAsync(selectedSql, ct).ConfigureAwait(false);
        return await CallAsync("optimize", system, user, ct).ConfigureAwait(false);
    }

    public async Task<string> TextToSqlAsync(string naturalLanguage, CancellationToken ct)
    {
        var (system, user) = await BuildTextToSqlAsync(naturalLanguage, ct).ConfigureAwait(false);
        return await CallAsync("texttosql", system, user, ct).ConfigureAwait(false);
    }

    public async Task<string> IndexAnalysisAsync(string selectedSql, CancellationToken ct)
    {
        var (system, user) = await BuildIndexAnalysisAsync(selectedSql, ct).ConfigureAwait(false);
        return await CallAsync("indexanalysis", system, user, ct).ConfigureAwait(false);
    }

    // ── Streaming ────────────────────────────────────────────────────────────────────────

    public async IAsyncEnumerable<string> ExplainStreamAsync(string selectedSql, [EnumeratorCancellation] CancellationToken ct)
    {
        var (system, user) = await BuildExplainAsync(selectedSql, ct).ConfigureAwait(false);
        await foreach (var tok in CallStreamAsync("explain", system, user, ct).ConfigureAwait(false)) yield return tok;
    }

    public async IAsyncEnumerable<string> FixStreamAsync(string failingSql, string errorMessage, int errorNumber, [EnumeratorCancellation] CancellationToken ct)
    {
        var (system, user) = await BuildFixAsync(failingSql, errorMessage, errorNumber, ct).ConfigureAwait(false);
        await foreach (var tok in CallStreamAsync("fix", system, user, ct).ConfigureAwait(false)) yield return tok;
    }

    public async IAsyncEnumerable<string> OptimizeStreamAsync(string selectedSql, [EnumeratorCancellation] CancellationToken ct)
    {
        var (system, user) = await BuildOptimizeAsync(selectedSql, ct).ConfigureAwait(false);
        await foreach (var tok in CallStreamAsync("optimize", system, user, ct).ConfigureAwait(false)) yield return tok;
    }

    public async IAsyncEnumerable<string> TextToSqlStreamAsync(string naturalLanguage, [EnumeratorCancellation] CancellationToken ct)
    {
        var (system, user) = await BuildTextToSqlAsync(naturalLanguage, ct).ConfigureAwait(false);
        await foreach (var tok in CallStreamAsync("texttosql", system, user, ct).ConfigureAwait(false)) yield return tok;
    }

    public async IAsyncEnumerable<string> IndexAnalysisStreamAsync(string selectedSql, [EnumeratorCancellation] CancellationToken ct)
    {
        var (system, user) = await BuildIndexAnalysisAsync(selectedSql, ct).ConfigureAwait(false);
        await foreach (var tok in CallStreamAsync("indexanalysis", system, user, ct).ConfigureAwait(false)) yield return tok;
    }

    // ── Prompt builders (schema resolved per feature mode) ─────────────────────────────────

    private async Task<(string System, string User)> BuildExplainAsync(string selectedSql, CancellationToken ct)
    {
        var schemaText = await _schema.GetSchemaTextAsync("explain", selectedSql, ct).ConfigureAwait(false);
        return ExplainPrompt.Build(schemaText, selectedSql);
    }

    private async Task<(string System, string User)> BuildFixAsync(string failingSql, string errorMessage, int errorNumber, CancellationToken ct)
    {
        var schemaText = await _schema.GetSchemaTextAsync("fix", failingSql, ct).ConfigureAwait(false);
        return FixPrompt.Build(schemaText, failingSql, errorMessage, errorNumber);
    }

    private async Task<(string System, string User)> BuildOptimizeAsync(string selectedSql, CancellationToken ct)
    {
        var schemaText = await _schema.GetSchemaTextAsync("optimize", selectedSql, ct).ConfigureAwait(false);
        return OptimizePrompt.Build(schemaText, selectedSql);
    }

    private async Task<(string System, string User)> BuildTextToSqlAsync(string naturalLanguage, CancellationToken ct)
    {
        var schemaText = await _schema.GetSchemaTextAsync("texttosql", naturalLanguage, ct).ConfigureAwait(false);
        return TextToSqlPrompt.Build(schemaText, naturalLanguage);
    }

    private async Task<(string System, string User)> BuildIndexAnalysisAsync(string selectedSql, CancellationToken ct)
    {
        var schemaText = await _schema.GetSchemaTextAsync("indexanalysis", selectedSql, ct).ConfigureAwait(false);
        // The browser has no execution plan offline -> pass null (prompt degrades to schema + SQL).
        return IndexAnalysisPrompt.Build(schemaText, selectedSql, executionPlanXml: null);
    }

    // ── Provider resolution + fully-local guard + transport ────────────────────────────────

    private async Task<string> ResolveProviderAsync(string featureId)
    {
        var providerId = await _preference.GetActiveAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(providerId))
        {
            throw new InvalidOperationException(
                "No active AI provider. Open Settings -> AI and add a provider with an API key.");
        }

        // FR-004 / FR-012 fully-local guard, enforced at the send path on both buffered and
        // streaming calls so a per-feature override (or a global-mode flip while a cloud
        // provider is active) can never leak to a cloud provider.
        var mode = await _settings.ResolveModeAsync(featureId).ConfigureAwait(false);
        if (mode == AiPrivacyMode.FullyLocal && !AiProviders.IsLocal(providerId))
        {
            throw new InvalidOperationException(
                $"'{featureId}' is set to Fully local, but the active provider '{providerId}' is not local. " +
                "Switch to Ollama or LM Studio, or change the privacy mode in Settings -> AI.");
        }

        return providerId;
    }

    private async Task<string> CallAsync(string featureId, string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var providerId = await ResolveProviderAsync(featureId).ConfigureAwait(false);
        return await _client.SendAsync(providerId, new AiChatRequest
        {
            SystemPrompt = systemPrompt,
            UserPrompt = userPrompt,
        }, ct).ConfigureAwait(false);
    }

    private async IAsyncEnumerable<string> CallStreamAsync(
        string featureId, string systemPrompt, string userPrompt, [EnumeratorCancellation] CancellationToken ct)
    {
        var providerId = await ResolveProviderAsync(featureId).ConfigureAwait(false);
        var stream = _client.StreamAsync(providerId, new AiChatRequest
        {
            SystemPrompt = systemPrompt,
            UserPrompt = userPrompt,
        }, ct);
        await foreach (var token in stream.ConfigureAwait(false)) yield return token;
    }
}
