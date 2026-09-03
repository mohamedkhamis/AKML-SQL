using System.ClientModel;
using System.Diagnostics;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Ai.Providers;
using MessagePack;
using Microsoft.Extensions.AI;
using Serilog;

namespace AkmlSql.Engine.Ai;

/// <summary>
/// Handles <see cref="MessageTypes.AiProviderTest"/> (77) requests from the shell.
/// <para>
/// Creates a temporary <see cref="IChatClient"/> from the request parameters, sends a short
/// test prompt, and returns success/failure with latency information. This allows the settings
/// dialog to validate provider connectivity before the user commits their configuration.
/// </para>
/// <para>
/// Failures are mapped to the five-cause FR-014 taxonomy (contracts/ai-provider-test.md) via
/// <see cref="MapFailureMessage"/>; full exception detail goes to the log and never to the user.
/// </para>
/// </summary>
public class AiProviderTestHandler
{
    /// <summary>
    /// The simple test prompt sent to the provider to verify connectivity and authentication.
    /// Kept intentionally short to minimise token usage and latency.
    /// </summary>
    private const string TestPrompt = "Say hello in one sentence.";

    private readonly Func<int> _aiTimeoutSeconds;

    /// <param name="aiTimeoutSeconds">
    /// Reads the configured AI timeout so the timeout row of the taxonomy can name the real
    /// value (FR-014). Defaults to the <see cref="AiSettings"/> default when not supplied.
    /// </param>
    public AiProviderTestHandler(Func<int>? aiTimeoutSeconds = null)
    {
        _aiTimeoutSeconds = aiTimeoutSeconds ?? (() => new AiSettings().Timeout);
    }

    /// <summary>
    /// Processes a provider test request and returns the result.
    /// </summary>
    /// <param name="message">The incoming RPC message with <see cref="AiProviderTestRequest"/> payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An <see cref="RpcMessage"/> containing an <see cref="AiProviderTestResponse"/> with
    /// <c>Success = true</c> and latency on success, or <c>Success = false</c> with an error
    /// message on failure.
    /// </returns>
    public async Task<RpcMessage?> HandleAsync(RpcMessage message, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        AiProviderTestRequest? request = null;
        try
        {
            if (message.Payload == null)
            {
                return CreateResponse(new AiProviderTestResponse
                {
                    Success = false,
                    ErrorMessage = "Request payload is empty."
                }, message.RequestId);
            }

            request = MessagePackSerializer.Deserialize<AiProviderTestRequest>(message.Payload);

            Log.Debug("AiProviderTest: provider={Provider}, model={Model}, hasEndpoint={HasEndpoint}",
                request.Provider, request.Model, !string.IsNullOrEmpty(request.Endpoint));

            if (string.IsNullOrWhiteSpace(request.Provider))
            {
                return CreateResponse(new AiProviderTestResponse
                {
                    Success = false,
                    ErrorMessage = "Provider name is required."
                }, message.RequestId);
            }

            if (string.IsNullOrWhiteSpace(request.Model))
            {
                return CreateResponse(new AiProviderTestResponse
                {
                    Success = false,
                    ErrorMessage = "Model name is required."
                }, message.RequestId);
            }

            // Build temporary AiSettings from the test request
            var testSettings = new AiSettings
            {
                Provider = request.Provider,
                ApiKey = request.ApiKey,
                Endpoint = request.Endpoint ?? string.Empty,
                Model = request.Model
            };

            // Create client and send test prompt
            using var client = AiProviderFactory.Create(testSettings);

            var response = await client.GetResponseAsync(TestPrompt, cancellationToken: ct);

            sw.Stop();

            Log.Information(
                "AI provider test succeeded: provider={Provider}, model={Model}, latency={LatencyMs}ms",
                request.Provider, request.Model, sw.ElapsedMilliseconds);

            var result = new AiProviderTestResponse
            {
                Success = true,
                ModelName = request.Model,
                ProviderVersion = request.Provider,
                LatencyMs = (int)sw.ElapsedMilliseconds
            };

            return CreateResponse(result, message.RequestId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The CALLER cancelled (or gave up waiting) — distinct from a provider-side deadline,
            // which arrives with ct still live and is mapped to the timeout row below.
            sw.Stop();
            Log.Debug("AI provider test cancelled after {LatencyMs}ms", sw.ElapsedMilliseconds);

            return CreateResponse(new AiProviderTestResponse
            {
                Success = false,
                ErrorMessage = "Test was cancelled.",
                LatencyMs = (int)sw.ElapsedMilliseconds
            }, message.RequestId);
        }
        catch (Exception ex)
        {
            sw.Stop();
            // Full detail (status, body, stack) stays in the log; the user gets the mapped cause.
            Log.Error(ex, "AI provider test failed after {LatencyMs}ms", sw.ElapsedMilliseconds);

            var errorMessage = MapFailureMessage(
                ex,
                request?.Provider ?? string.Empty,
                request?.Model ?? string.Empty,
                request?.Endpoint,
                _aiTimeoutSeconds());

            return CreateResponse(new AiProviderTestResponse
            {
                Success = false,
                ErrorMessage = errorMessage,
                LatencyMs = (int)sw.ElapsedMilliseconds
            }, message.RequestId);
        }
    }

    /// <summary>
    /// Maps a provider-test failure to one actionable message per the FR-014 taxonomy
    /// (contracts/ai-provider-test.md): missing/invalid key, unknown model, unreachable endpoint,
    /// quota/rate-limit, timeout. A raw provider payload (JSON body, stack trace) must never
    /// reach the user — unknown shapes degrade to a type name plus a pointer to the log.
    /// </summary>
    internal static string MapFailureMessage(Exception ex, string provider, string model,
        string? endpoint, int timeoutSeconds)
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
            // Provider-side deadline elapsed (the caller's token was still live — the caller-cancel
            // path is filtered out by HandleAsync before this mapping runs).
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
            return $"The provider test failed (HTTP {status}). " +
                   "Full detail is in the log — AKML SQL → View Logs.";
        }

        return $"The provider test failed with an unexpected error ({ex.GetType().Name}). " +
               "Full detail is in the log — AKML SQL → View Logs.";
    }

    /// <summary>The endpoint to name in messages: the request's, else the provider's default.</summary>
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

    /// <summary>
    /// Wraps an <see cref="AiProviderTestResponse"/> in an <see cref="RpcMessage"/>.
    /// </summary>
    private static RpcMessage CreateResponse(AiProviderTestResponse payload, int requestId)
    {
        return new RpcMessage
        {
            MessageType = MessageTypes.AiProviderTestResult,
            RequestId = requestId,
            Payload = MessagePackSerializer.Serialize(payload)
        };
    }
}
