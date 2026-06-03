using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Engine.Ai.Prompts;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M6 task T129; extended by spec 028 (M6) task T009.
/// Builds AI prompts using the extracted <c>AkmlSql.AI</c> prompt builders, then sends them
/// via <see cref="IAiClientFactory"/>.
///
/// <para>
/// The schema context is resolved internally per feature via
/// <see cref="IAiSchemaContextProvider"/>, honouring that feature's active privacy
/// disclosure mode (US1). Callers no longer pass schema text — centralising resolution here
/// keeps the "no schema" guarantee (FR-007) on every code path.
/// </para>
/// </summary>
public interface IAiPromptService
{
    /// <summary>Build + send "Explain this SQL".</summary>
    Task<string> ExplainAsync(string selectedSql, CancellationToken ct);

    /// <summary>Build + send "Fix this SQL" given the engine's error message + error number.</summary>
    Task<string> FixAsync(string failingSql, string errorMessage, int errorNumber, CancellationToken ct);

    /// <summary>Build + send "Optimize this SQL".</summary>
    Task<string> OptimizeAsync(string selectedSql, CancellationToken ct);

    /// <summary>Build + send "Convert NL to SQL".</summary>
    Task<string> TextToSqlAsync(string naturalLanguage, CancellationToken ct);
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

    public async Task<string> ExplainAsync(string selectedSql, CancellationToken ct)
    {
        var schemaText = await _schema.GetSchemaTextAsync("explain", selectedSql, ct).ConfigureAwait(false);
        var (system, user) = ExplainPrompt.Build(schemaText, selectedSql);
        return await CallAsync("explain", system, user, ct).ConfigureAwait(false);
    }

    public async Task<string> FixAsync(string failingSql, string errorMessage, int errorNumber, CancellationToken ct)
    {
        var schemaText = await _schema.GetSchemaTextAsync("fix", failingSql, ct).ConfigureAwait(false);
        var (system, user) = FixPrompt.Build(schemaText, failingSql, errorMessage, errorNumber);
        return await CallAsync("fix", system, user, ct).ConfigureAwait(false);
    }

    public async Task<string> OptimizeAsync(string selectedSql, CancellationToken ct)
    {
        var schemaText = await _schema.GetSchemaTextAsync("optimize", selectedSql, ct).ConfigureAwait(false);
        var (system, user) = OptimizePrompt.Build(schemaText, selectedSql);
        return await CallAsync("optimize", system, user, ct).ConfigureAwait(false);
    }

    public async Task<string> TextToSqlAsync(string naturalLanguage, CancellationToken ct)
    {
        var schemaText = await _schema.GetSchemaTextAsync("texttosql", naturalLanguage, ct).ConfigureAwait(false);
        var (system, user) = TextToSqlPrompt.Build(schemaText, naturalLanguage);
        return await CallAsync("texttosql", system, user, ct).ConfigureAwait(false);
    }

    private async Task<string> CallAsync(string featureId, string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var providerId = await _preference.GetActiveAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(providerId))
        {
            throw new InvalidOperationException(
                "No active AI provider. Open Settings -> AI and add a provider with an API key.");
        }

        // FR-004 / FR-012 fully-local guard, enforced at the send path (not only the picker)
        // so a per-feature FullyLocal override, or a global-mode flip while a cloud provider is
        // active, can never leak schema to a cloud provider. Mode was already applied to the
        // schema text; this gate stops the request itself.
        var mode = await _settings.ResolveModeAsync(featureId).ConfigureAwait(false);
        if (mode == AiPrivacyMode.FullyLocal && !AiProviders.IsLocal(providerId))
        {
            throw new InvalidOperationException(
                $"'{featureId}' is set to Fully local, but the active provider '{providerId}' is not local. " +
                "Switch to Ollama or LM Studio, or change the privacy mode in Settings -> AI.");
        }

        return await _client.SendAsync(providerId, new AiChatRequest
        {
            SystemPrompt = systemPrompt,
            UserPrompt = userPrompt,
        }, ct).ConfigureAwait(false);
    }
}
