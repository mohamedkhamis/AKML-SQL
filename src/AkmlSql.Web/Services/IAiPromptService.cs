using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Engine.Ai.Prompts;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M6 task T129. Builds AI prompts using the extracted
/// <c>AkmlSql.AI</c> prompt builders, then sends them via <see cref="IAiClientFactory"/>.
/// Schema context (the prompt's "context window" describing the active database)
/// comes from <see cref="ISchemaCacheStore"/> in M5; in M6 the schema text is
/// passed in directly by the AiPanel caller until the schema-cache fallback wires
/// in.
/// </summary>
public interface IAiPromptService
{
    /// <summary>Build + send "Explain this SQL".</summary>
    Task<string> ExplainAsync(string schemaText, string selectedSql, CancellationToken ct);

    /// <summary>Build + send "Fix this SQL" given the engine's error message + error number.</summary>
    Task<string> FixAsync(string schemaText, string failingSql, string errorMessage, int errorNumber, CancellationToken ct);

    /// <summary>Build + send "Optimize this SQL".</summary>
    Task<string> OptimizeAsync(string schemaText, string selectedSql, CancellationToken ct);

    /// <summary>Build + send "Convert NL to SQL".</summary>
    Task<string> TextToSqlAsync(string schemaText, string naturalLanguage, CancellationToken ct);
}

internal sealed class AiPromptService : IAiPromptService
{
    private readonly IAiClientFactory _client;
    private readonly IAiPreference _preference;

    public AiPromptService(IAiClientFactory client, IAiPreference preference)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _preference = preference ?? throw new ArgumentNullException(nameof(preference));
    }

    public async Task<string> ExplainAsync(string schemaText, string selectedSql, CancellationToken ct)
    {
        var (system, user) = ExplainPrompt.Build(schemaText, selectedSql);
        return await CallAsync(system, user, ct).ConfigureAwait(false);
    }

    public async Task<string> FixAsync(string schemaText, string failingSql, string errorMessage, int errorNumber, CancellationToken ct)
    {
        var (system, user) = FixPrompt.Build(schemaText, failingSql, errorMessage, errorNumber);
        return await CallAsync(system, user, ct).ConfigureAwait(false);
    }

    public async Task<string> OptimizeAsync(string schemaText, string selectedSql, CancellationToken ct)
    {
        var (system, user) = OptimizePrompt.Build(schemaText, selectedSql);
        return await CallAsync(system, user, ct).ConfigureAwait(false);
    }

    public async Task<string> TextToSqlAsync(string schemaText, string naturalLanguage, CancellationToken ct)
    {
        var (system, user) = TextToSqlPrompt.Build(schemaText, naturalLanguage);
        return await CallAsync(system, user, ct).ConfigureAwait(false);
    }

    private async Task<string> CallAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var providerId = await _preference.GetActiveAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(providerId))
        {
            throw new InvalidOperationException(
                "No active AI provider. Open Settings -> AI and add a provider with an API key.");
        }
        return await _client.SendAsync(providerId, new AiChatRequest
        {
            SystemPrompt = systemPrompt,
            UserPrompt = userPrompt,
        }, ct).ConfigureAwait(false);
    }
}
