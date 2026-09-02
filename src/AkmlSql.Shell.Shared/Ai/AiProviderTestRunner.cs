#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using Serilog;

namespace AkmlSql.Shell.Shared.Ai
{
    /// <summary>
    /// Spec 036 (US2, FR-009) — the shell caller for the AiProviderTest (77/177) IPC pair, which
    /// existed engine-side since spec 021 but had no caller (research R3). Honours the caller
    /// obligations of <c>contracts/ai-provider-test.md</c>: the dialog's CURRENT field values are
    /// tested (not the saved settings), the provider is canonicalised before sending, the key is
    /// sent as the field holds it (never double-wrapped), the wait budget is
    /// <see cref="AiIpcTimeouts.ForAiRequestMs"/>, engine-not-connected is a distinct outcome, and
    /// the key is never logged.
    /// </summary>
    internal static class AiProviderTestRunner
    {
        /// <summary>
        /// Builds the request from raw dialog field values. The provider selection (a display name
        /// like "Kimi (Moonshot)" or a canonical id) is normalised through
        /// <see cref="AiProviderIds.Normalize"/> — sending the display string would reproduce the
        /// R8 bug. A blank endpoint is sent as null so the engine applies the provider default.
        /// </summary>
        public static AiProviderTestRequest BuildRequest(string? providerSelection, string? model,
            string? apiKey, string? endpoint)
        {
            return new AiProviderTestRequest
            {
                Provider = AiProviderIds.Normalize(providerSelection),
                Model = (model ?? string.Empty).Trim(),
                ApiKey = apiKey ?? string.Empty,
                Endpoint = string.IsNullOrWhiteSpace(endpoint) ? null : endpoint!.Trim(),
            };
        }

        /// <summary>
        /// Sends the test request and returns the outcome as user-facing text. Never throws —
        /// every failure path becomes a message (the engine maps provider failures to the FR-014
        /// taxonomy; IPC timeouts are described by <see cref="AiIpcTimeouts.DescribeFailure"/>).
        /// </summary>
        public static async Task<(bool Success, string Message)> RunAsync(
            IRpcClientAccessor rpc, AiProviderTestRequest request, AppSettings? settings,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(request.Provider))
                return (false, "Select an AI provider first.");

            // Engine-not-running is a distinct outcome from a provider failure (obligation 6).
            if (!rpc.IsConnected)
            {
                return (false, "The AI engine is not connected. Let AKML SQL start the engine " +
                               "(edit any SQL file), then retry.");
            }

            try
            {
                var response = await rpc.SendRequestAsync<AiProviderTestResponse, AiProviderTestRequest>(
                    MessageTypes.AiProviderTest, request,
                    AiIpcTimeouts.ForAiRequestMs(settings), ct);

                if (response.Success)
                {
                    return (true, $"Connection succeeded — '{request.Provider}' answered in " +
                                  $"{response.LatencyMs} ms using model " +
                                  $"'{response.ModelName ?? request.Model}'.");
                }

                return (false, response.ErrorMessage ??
                               "The provider test failed without an error message. See AKML SQL → View Logs.");
            }
            catch (Exception ex)
            {
                // Never log the key — provider/model/hasEndpoint only, per the handler's precedent.
                Log.Warning(ex, "AiProviderTestRunner: test failed (provider={Provider}, model={Model})",
                    request.Provider, request.Model);
                return (false, AiIpcTimeouts.DescribeFailure(ex, settings));
            }
        }
    }
}
