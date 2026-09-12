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
    /// (contracts/ai-provider-test.md) — the shared mapping lives in <see cref="AiFailureTaxonomy"/>
    /// so the live request path (the fallback chain) sanitizes with the same rules (FR-058).
    /// </summary>
    internal static string MapFailureMessage(Exception ex, string provider, string model,
        string? endpoint, int timeoutSeconds)
        => AiFailureTaxonomy.Map(ex, provider, model, endpoint, timeoutSeconds, "The provider test");

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
