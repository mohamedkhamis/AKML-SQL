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
    }
}
