#nullable enable
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Ai
{
    /// <summary>
    /// IPC wait budget for engine AI requests. The engine gives the provider
    /// <see cref="AiSettings.Timeout"/> seconds, so the shell must wait LONGER than that —
    /// a shorter shell-side wait cancels the pipe request mid-generation and the user sees
    /// "Error: A task was canceled" while the provider was still answering (the old
    /// AiChatPanel hard-coded 30 s against the 90 s default provider timeout).
    /// </summary>
    internal static class AiIpcTimeouts
    {
        private const int DefaultProviderTimeoutSec = 90;   // mirrors AiSettings.Timeout default
        private const int MarginSec = 30;                   // engine-side overhead + pipe latency

        public static int ForAiRequestMs(AppSettings? settings)
        {
            var providerSec = settings?.Ai?.Timeout ?? 0;
            if (providerSec <= 0) providerSec = DefaultProviderTimeoutSec;
            return (providerSec + MarginSec) * 1000;
        }

        /// <summary>
        /// User-facing text for a failed AI request. A timed-out IPC wait surfaces as the bare
        /// "A task was canceled" — useless to the user; say it timed out, for how long, and
        /// where to look. Provider errors (quota, key, model) keep their original message.
        /// </summary>
        public static string DescribeFailure(System.Exception ex, AppSettings? settings)
        {
            if (ex is System.OperationCanceledException)
            {
                var waitedSec = ForAiRequestMs(settings) / 1000;
                return $"The AI request timed out after {waitedSec}s — the provider may be slow or rate-limited. " +
                       "See AKML SQL → View Logs for the provider's last error.";
            }
            return ex.Message;
        }

        /// <summary>
        /// Spec 037 (US5, FR-057): whether a failed LIVE request reads as caused by the agent's
        /// own configuration — a missing or rejected key, a missing or unreachable endpoint, an
        /// unknown or cross-family model, an unrecognised provider. Momentary states the agent's
        /// settings cannot fix are NOT configuration-caused: the privacy-consent gate, a slow
        /// provider (timeout/cancellation), quota or rate-limiting, the engine being down, AI
        /// being disabled. Only configuration-caused failures name the agent and route to its
        /// settings — naming one on a quota error would send the user to a dialog that cannot
        /// fix it.
        /// </summary>
        public static bool IsConfigurationCaused(string? errorMessage)
        {
            if (string.IsNullOrWhiteSpace(errorMessage)) return false;
            var msg = errorMessage!;

            // The momentary states first — several of their texts also mention keys or
            // endpoints, and they must win the classification.
            if (msg.StartsWith("CONSENT_REQUIRED:", System.StringComparison.Ordinal)) return false;
            if (Has(msg, "timed out") || Has(msg, "timeout") ||
                Has(msg, "cancelled") || Has(msg, "canceled")) return false;
            if (Has(msg, "quota") || Has(msg, "rate-limited") || Has(msg, "rate limit")) return false;
            if (Has(msg, "engine is not connected") || Has(msg, "engine is running")) return false;
            if (Has(msg, "assistance is disabled")) return false;

            return
                Has(msg, "api key") ||
                Has(msg, "apikey") ||
                Has(msg, "x-api-key") ||
                Has(msg, "authentication") ||
                Has(msg, "http 401") ||
                Has(msg, "http 403") ||
                Has(msg, "endpoint") ||
                Has(msg, "could not reach") ||
                Has(msg, "no such host") ||
                Has(msg, "http 404") ||
                (Has(msg, "model") && (Has(msg, "not found") || Has(msg, "does not exist"))) ||
                Has(msg, " model, not a ") ||
                Has(msg, " model, but the ai provider") ||
                Has(msg, "unknown ai provider") ||
                Has(msg, "unknown model");
        }

        /// <summary>net472 has no <c>string.Contains(string, StringComparison)</c>.</summary>
        private static bool Has(string haystack, string needle)
            => haystack.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Spec 037 (US5, FR-057): the user-facing text for a failed LIVE request. A
        /// configuration-caused failure names the agent whose settings produced it and states
        /// the route that fixes it (the chat panel renders the deep-link button beside this
        /// text); anything else keeps its bare message.
        /// </summary>
        public static string DescribeLiveFailure(string errorMessage, string? agentName)
        {
            if (string.IsNullOrWhiteSpace(agentName) || !IsConfigurationCaused(errorMessage))
                return errorMessage;
            return $"\"{agentName}\" failed — {errorMessage} Fix it under Options → AI Assistance.";
        }
    }
}
