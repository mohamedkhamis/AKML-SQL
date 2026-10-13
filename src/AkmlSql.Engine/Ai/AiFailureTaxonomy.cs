using System.ClientModel;
using System.Net.Http;
using AkmlSql.Core.Config;
using AkmlSql.Engine.Ai.Providers;
using GeminiApiException = Mscc.GenerativeAI.Types.GeminiApiException;

namespace AkmlSql.Engine.Ai;

/// <summary>
/// The FR-014 failure taxonomy (contracts/ai-provider-test.md), shared by the provider-test
/// path (<see cref="AiProviderTestHandler"/>) and the live request path (the fallback chain in
/// <see cref="AiPipelineServices"/>): missing/invalid key, unknown model, unreachable endpoint,
/// quota/rate-limit, timeout — plus a provider-side outage (5xx), which is neither the user's
/// settings nor their network. A raw provider payload (JSON body, stack trace, echoed
/// credentials) must never reach the user (FR-058) — unknown shapes degrade to a type name plus
/// a pointer to the log. Full exception detail goes to the log at the call sites.
/// </summary>
internal static class AiFailureTaxonomy
{
    /// <summary>
    /// Server-side failures: 500/502/503/504, and Anthropic's 529 "overloaded". Gemini answers
    /// 503 UNAVAILABLE "This model is currently experiencing high demand" — before this row
    /// that read as "unexpected error" and sent the user to the log for nothing.
    /// </summary>
    private static readonly int[] UnavailableStatuses = { 500, 502, 503, 504, 529 };

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

        if (status == 401 || status == 403 || IsGoogleRejectedKey(status, ex) || IsAnthropicRejectedKey(ex))
        {
            // Kimi note (contracts/kimi-provider.md): a .cn key against the .ai endpoint is the
            // likely first-run mistake, so the endpoint is named alongside the key — when there
            // is one to name (the clouds with a fixed endpoint have none).
            var keyEndpoint = EndpointForMessage(provider, endpoint);
            var checkClause = keyEndpoint != null
                ? $"Check the key — and the endpoint ('{keyEndpoint}'): a key issued for a " +
                  "different service or region will be rejected."
                : "Check the key: a key issued for a different service will be rejected.";
            return $"The API key was rejected by {providerName} (HTTP {status ?? 401}). {checkClause}";
        }

        if (status == 404)
        {
            var example = AiModelFamily.DefaultModelFor(provider);
            var exampleClause = example != null ? $", e.g. \"{example}\"" : string.Empty;
            return $"The model '{model}' was not found at {providerName} (HTTP 404). " +
                   $"Use a valid model{exampleClause}, or update the Model field in Options → AI Assistance.";
        }

        if (status != null && Array.IndexOf(UnavailableStatuses, status.Value) >= 0)
        {
            var subject = string.IsNullOrWhiteSpace(provider) ? "The AI provider" : $"The AI provider '{provider}'";
            return $"{subject} is temporarily unavailable or overloaded (HTTP {status}). " +
                   "Retry in a few minutes, or choose a different model in Options → AI Assistance.";
        }

        if (IsTimeout(ex))
        {
            // Provider-side deadline elapsed (the caller's token was still live).
            return $"The AI provider did not respond within the timeout ({timeoutSeconds}s) — it timed out. " +
                   "Increase 'Timeout' under Options → AI Assistance, or retry when the provider is less loaded.";
        }

        // A status code means the server answered, so the endpoint was reachable — only a
        // status-less HTTP failure (DNS, refused connection, TLS) is "could not reach".
        if (status == null && (ex is HttpRequestException || HasInnerHttpError(ex)))
        {
            var where = EndpointForMessage(provider, endpoint);
            return where != null
                ? $"Could not reach the AI provider endpoint '{where}'. Check the URL and the network connection."
                : "Could not reach the AI provider. Check the endpoint URL and the network connection.";
        }

        if (status != null)
        {
            return $"{failureNoun} failed (HTTP {status}). " +
                   "Full detail is in the log — AKML SQL → Help → View Logs.";
        }

        return $"{failureNoun} failed with an unexpected error ({ex.GetType().Name}). " +
               "Full detail is in the log — AKML SQL → Help → View Logs.";
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
                // Mscc.GenerativeAI (Gemini) throws a plain-Exception subclass that keeps the
                // failed response; without this case every Gemini 404/429/503 read as
                // "unexpected error (GeminiApiException)".
                case GeminiApiException gae when gae.Response != null:
                    return (int)gae.Response.StatusCode;
            }
        }
        return null;
    }

    /// <summary>
    /// Google answers a bad API key with HTTP 400 INVALID_ARGUMENT, reason
    /// <c>API_KEY_INVALID</c> ("API key not valid. Please pass a valid API key.") — never 401/403,
    /// so the status alone would file it under a generic bad request. Gemini's exception message
    /// carries the response body; it is read here to classify only and is never echoed (FR-058).
    /// </summary>
    private static bool IsGoogleRejectedKey(int? status, Exception ex)
    {
        if (status != 400) return false;
        for (Exception? cur = ex; cur != null; cur = cur.InnerException)
        {
            var text = cur.Message;
            if (text != null && (text.Contains("API_KEY_INVALID", StringComparison.Ordinal) ||
                                 text.Contains("API key not valid", StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Anthropic.SDK answers HTTP 401 with a top-level
    /// <see cref="System.Security.Authentication.AuthenticationException"/> ("Anthropic rejected
    /// your authorization … Full API response follows: …") that carries no status. Top level only:
    /// a TLS handshake failure arrives as an <see cref="HttpRequestException"/> with this type
    /// INSIDE it, and that is an unreachable endpoint, not a bad key.
    /// </summary>
    private static bool IsAnthropicRejectedKey(Exception ex)
        => ex is System.Security.Authentication.AuthenticationException;

    /// <summary>
    /// A provider-side deadline: an <see cref="OperationCanceledException"/> (HttpClient's own
    /// timeout) or a <see cref="TimeoutException"/>, anywhere in the chain — Mscc.GenerativeAI
    /// turns its per-request deadline into a TimeoutException and wraps it in a
    /// <see cref="GeminiApiException"/> with no response, which used to read as "unexpected error".
    /// The caller's own cancellation never reaches here: the handlers catch it first.
    /// </summary>
    private static bool IsTimeout(Exception ex)
    {
        for (Exception? cur = ex; cur != null; cur = cur.InnerException)
        {
            if (cur is OperationCanceledException || cur is TimeoutException)
                return true;
        }
        return false;
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
