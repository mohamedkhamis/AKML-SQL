#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using Serilog;

namespace AkmlSql.Shell.Shared.Formatting
{
    /// <summary>
    /// Phase 10 (spec 019) / US14 FR-074 — single shared helper that dispatches a
    /// <see cref="FormatRequest"/> via the engine IPC. Consumed by the three
    /// Format-on-* triggers (<c>FormatOnSaveHandler</c>, <c>FormatOnPasteHandler</c>,
    /// <c>FormatOnDelimiterHandler</c>) which previously held three near-identical
    /// TODO stubs per the 2026-05-05 codebase audit (BUG-A4..A6).
    /// <para>
    /// Stateless. Each handler instance constructs one dispatcher and forwards
    /// trigger-specific document text into <see cref="DispatchAsync"/>; the
    /// returned formatted text (or <c>null</c> on failure) is applied by the
    /// handler via its own <c>ITextEdit</c>.
    /// </para>
    /// </summary>
    internal sealed class FormatRequestDispatcher
    {
        private readonly Func<PipeRpcClient?> _clientAccessor;
        private readonly Func<AppSettings> _settingsAccessor;
        private readonly int _timeoutMs;

        /// <summary>
        /// Construct a dispatcher.
        /// </summary>
        /// <param name="clientAccessor">
        /// Returns the active <see cref="PipeRpcClient"/> or <c>null</c> if the
        /// engine is not connected. Lazily resolved so the dispatcher does not
        /// hold a stale connection across engine restarts.
        /// </param>
        /// <param name="settingsAccessor">
        /// Returns the current <see cref="AppSettings"/> snapshot. Used to read
        /// the active profile name.
        /// </param>
        /// <param name="timeoutMs">
        /// Maximum time to wait for a format response. Defaults to 2000 ms —
        /// long enough for a typical document but short enough to keep
        /// Format-on-Save responsive.
        /// </param>
        public FormatRequestDispatcher(
            Func<PipeRpcClient?> clientAccessor,
            Func<AppSettings> settingsAccessor,
            int timeoutMs = 2000)
        {
            _clientAccessor = clientAccessor ?? throw new ArgumentNullException(nameof(clientAccessor));
            _settingsAccessor = settingsAccessor ?? throw new ArgumentNullException(nameof(settingsAccessor));
            _timeoutMs = timeoutMs;
        }

        /// <summary>
        /// Dispatch a format request and return the resulting formatted SQL.
        /// Returns <c>null</c> when the engine is unavailable, the request times
        /// out, or the engine reports an error — caller MUST treat null as
        /// "format silently skipped" and leave the buffer unchanged.
        /// </summary>
        /// <param name="sessionId">Editor session id for the active document.</param>
        /// <param name="originalSql">Current text of the document or selection.</param>
        /// <param name="trigger">
        /// Logical trigger source — Save, Paste, Delimiter, ManualCommand —
        /// used only for diagnostic logging.
        /// </param>
        /// <param name="cancellationToken">Caller cancellation.</param>
        /// <returns>Formatted SQL on success, or <c>null</c> on any failure.</returns>
        public async Task<string?> DispatchAsync(
            string sessionId,
            string originalSql,
            FormatTrigger trigger,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(originalSql))
            {
                return null;
            }

            var client = _clientAccessor();
            if (client is null || client.IsConnected == false)
            {
                Log.Debug("FormatRequestDispatcher: engine not connected, skipping format ({Trigger})", trigger);
                return null;
            }

            var settings = _settingsAccessor();
            var request = new FormatRequest
            {
                SessionId = sessionId,
                Text = originalSql,
                ProfileName = settings?.Formatter?.ActiveProfile ?? "Khamis Style",
            };

            try
            {
                var response = await client
                    .SendRequestAsync<FormatResponse, FormatRequest>(
                        MessageTypes.FormatDocument,
                        request,
                        _timeoutMs,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (response is null || response.Success == false)
                {
                    Log.Debug(
                        "FormatRequestDispatcher: engine returned {Outcome} ({Trigger})",
                        response is null ? "null" : "Success=false",
                        trigger);
                    return null;
                }

                // The engine formatted successfully but could NOT load the requested style, so the
                // output silently used built-in defaults. Surface it — a wrong-style format is
                // indistinguishable from a correct one to the eye, and FormatFailureNotifier stays
                // quiet on success by design. Warned once per style per session: a missing style is
                // a persistent configuration problem, not a per-keystroke event, and this path also
                // runs on save/paste/delimiter auto-format.
                NotifyProfileFallbackOnce(response.ProfileFallbackWarning);

                return response.FormattedText;
            }
            catch (OperationCanceledException)
            {
                Log.Debug("FormatRequestDispatcher: cancelled or timed out after {Timeout} ms ({Trigger})",
                    _timeoutMs, trigger);
                return null;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "FormatRequestDispatcher: format dispatch failed ({Trigger})", trigger);
                return null;
            }
        }

        // ── Profile-fallback notice (spec 033 follow-up) ─────────────────────────────────
        //
        // Warned-once bookkeeping is process-wide (static): the three Format-on-* handlers each
        // construct their OWN dispatcher, so per-instance state would re-warn per trigger kind.

        private static readonly object FallbackGate = new object();
        private static readonly System.Collections.Generic.HashSet<string> WarnedFallbacks =
            new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Test seam: replaced by unit tests to capture the notice without a live VS shell.
        /// Null in production, where the message box is shown instead.
        /// </summary>
        internal static Action<string>? ProfileFallbackNotifierOverride { get; set; }

        /// <summary>Clears the warned-once set. Test-only — keeps cases independent.</summary>
        internal static void ResetProfileFallbackWarnings()
        {
            lock (FallbackGate) WarnedFallbacks.Clear();
        }

        /// <summary>Internal (not private) so the dedupe contract is unit-testable without a
        /// live engine connection — <c>PipeRpcClient</c> is concrete and cannot be faked.</summary>
        internal static void NotifyProfileFallbackOnce(string? warning)
        {
            if (string.IsNullOrWhiteSpace(warning)) return;

            lock (FallbackGate)
            {
                if (!WarnedFallbacks.Add(warning!)) return;   // already told them this session
            }

            Log.Warning("FormatRequestDispatcher: {Warning}", warning);

            var notifier = ProfileFallbackNotifierOverride;
            if (notifier != null)
            {
                notifier(warning!);
                return;
            }

            // Fire-and-forget: formatting must not block on a message box.
            _ = FormatFailureNotifier.ShowNoticeAsync(warning!);
        }

        /// <summary>
        /// Logical trigger source. Used only for diagnostic logging — does not
        /// change the request payload.
        /// </summary>
        public enum FormatTrigger
        {
            Save,
            Paste,
            Delimiter,
            ManualCommand,
        }
    }
}
