using System.ClientModel;
using System.Net.Http;
using AkmlSql.Core.Config;
using AkmlSql.Engine.Ai.Providers;

namespace AkmlSql.Engine.Ai;

/// <summary>
/// The FR-014 failure taxonomy (contracts/ai-provider-test.md), shared by the provider-test
/// path (<see cref="AiProviderTestHandler"/>) and the live request path (the fallback chain in
/// <see cref="AiPipelineServices"/>): missing/invalid key, unknown model, unreachable endpoint,
/// quota/rate-limit, timeout. A raw provider payload (JSON body, stack trace, echoed
/// credentials) must never reach the user (FR-058) — unknown shapes degrade to a type name plus
/// a pointer to the log. Full exception detail goes to the log at the call sites.
/// </summary>
internal static class AiFailureTaxonomy
{
    /// <summary>
    /// Maps a provider failure to one actionable message. <paramref name="failureNoun"/> is the
    /// sentence subject for the two generic rows ("The provider test" on the test path, "The AI
    /// request" on the live path); the specific rows read the same on both.
    /// </summary>
    internal static string Map(Exception ex, string provider, string model,
        string? endpoint, int timeoutSeconds, string failureNoun)
    {
        var providerName = string.IsNullOrWhiteSpace(provider) ? "the configured provider" : $"'{provider}'";
        var status = TryGetHttpStatus(ex);

        // Factory-side validation (missing key/model, family mismatch, unknown provider) already
        // says the right thing — keep it verbatim.
        if (ex is InvalidOperationException)
            return ex.Message;

        if (status == 429 || IsRateLimitType(ex))
        {
            // FR-014 edge case: quota must never read as "AI is disabled".
            return $"The {providerName} account is rate-limited or out of quota (HTTP 429). " +
                   "Check the plan/billing with the provider, or wait and retry.";
        }

        if (status == 401 || status == 403)
        {
            // Kimi note (contracts/kimi-provider.md): a .cn key against the .ai endpoint is the
            // likely first-run mistake, so the endpoint is named alongside the key.
            return $"The API key was rejected by {providerName} (HTTP {status}). Check the key — and " +
                   $"the endpoint ('{EndpointForMessage(provider, endpoint)}'): a key issued for a " +
                   "different service or region will be rejected.";
        }

        if (status == 404)
        {
            var example = AiModelFamily.DefaultModelFor(provider);
            var exampleClause = example != null ? $", e.g. \"{example}\"" : string.Empty;
            return $"The model '{model}' was not found at {providerName} (HTTP 404). " +
                   $"Use a valid model{exampleClause}, or update the Model field in Options → AI Assistance.";
        }

        if (ex is OperationCanceledException)
        {
            // Provider-side deadline elapsed (the caller's token was still live).
            return $"The AI provider did not respond within the timeout ({timeoutSeconds}s) — it timed out. " +
                   "Increase 'Timeout (seconds)' under Options → AI Assistance, or retry when the provider is less loaded.";
        }

        if (ex is HttpRequestException || HasInnerHttpError(ex))
        {
            var where = EndpointForMessage(provider, endpoint);
            return where != null
                ? $"Could not reach the AI provider endpoint '{where}'. Check the URL and the network connection."
                : "Could not reach the AI provider. Check the endpoint URL and the network connection.";
        }

        if (status != null)
        {
            return $"{failureNoun} failed (HTTP {status}). " +
                   "Full detail is in the log — AKML SQL → View Logs.";
        }

        return $"{failureNoun} failed with an unexpected error ({ex.GetType().Name}). " +
               "Full detail is in the log — AKML SQL → View Logs.";
    }

    /// <summary>The endpoint to name in messages: the attempt's, else the provider's default.</summary>
    private static string? EndpointForMessage(string provider, string? endpoint)
    {
        if (!string.IsNullOrWhiteSpace(endpoint)) return endpoint;
        return AiProviderIds.Normalize(provider) switch
        {
            AiProviderIds.Kimi => AiProviderFactory.DefaultKimiEndpoint,
            AiProviderIds.Ollama => "http://localhost:11434",
            _ => null,
        };
    }

    /// <summary>Walks the exception chain for an HTTP status carried by a known SDK shape.</summary>
    private static int? TryGetHttpStatus(Exception ex)
    {
        for (Exception? cur = ex; cur != null; cur = cur.InnerException)
        {
            switch (cur)
            {
                case ClientResultException cre when cre.Status != 0:
                    return cre.Status;
                case HttpRequestException hre when hre.StatusCode.HasValue:
                    return (int)hre.StatusCode.Value;
            }
        }
        return null;
    }

    /// <summary>Anthropic.SDK's RateLimitsExceeded carries no status — the type name is the signal.</summary>
    private static bool IsRateLimitType(Exception ex)
    {
        for (Exception? cur = ex; cur != null; cur = cur.InnerException)
        {
            if (cur.GetType().Name.Contains("RateLimit", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool HasInnerHttpError(Exception ex)
    {
        for (Exception? cur = ex.InnerException; cur != null; cur = cur.InnerException)
        {
            if (cur is HttpRequestException)
                return true;
        }
        return false;
    }
}
