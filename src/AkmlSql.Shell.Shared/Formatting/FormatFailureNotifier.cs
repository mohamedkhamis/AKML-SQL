using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using AkmlSql.Core.Ipc.Messages;
using Serilog;

namespace AkmlSql.Shell.Shared.Formatting
{
    /// <summary>
    /// Spec 030 T018 / FR-005 — when the engine cannot parse or safely format SQL it preserves the
    /// original text; this surfaces that outcome to the user with a clear, actionable message
    /// (otherwise a failed Format SQL looks like a silent no-op). Uses
    /// <see cref="VsShellUtilities.ShowMessageBox"/>, the shell's established user-message
    /// mechanism (no InfoBar precedent in this codebase), which works in both SSMS 22 and VS 2026.
    /// </summary>
    internal static class FormatFailureNotifier
    {
        private const string Title = "AKML SQL — Format";

        /// <summary>
        /// Shows a message when a format result is a non-applied failure/preserve outcome.
        /// Returns true if a message was shown. No-op (returns false) for the success cases:
        /// applied (Success &amp;&amp; WasModified) and already-formatted (Success &amp;&amp;
        /// ValidationPassed &amp;&amp; !WasModified) — SQL Prompt is silent there too.
        /// <paramref name="diagnostics"/> is null for paths whose IPC response carries none
        /// (e.g. selection format); the message then degrades to the generic preserve text.
        /// </summary>
        public static async Task<bool> NotifyIfPreservedAsync(
            bool success, bool validationPassed, FormatDiagnosticInfo[]? diagnostics)
        {
            // OK: parsed + validated. Either it was applied or there was nothing to change.
            if (success && validationPassed)
                return false;

            var message = BuildMessage(success, diagnostics);
            await ShowNoticeAsync(message).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// Shows <paramref name="message"/> in the shell's standard warning message box. Extracted
        /// from <see cref="NotifyIfPreservedAsync"/> so other format outcomes can reuse the same
        /// plumbing — notably the profile-fallback notice, which fires on a SUCCESSFUL format (the
        /// preserve-notifier deliberately returns early there) and therefore needs its own entry
        /// point rather than a new special case inside the preserve path.
        /// Never throws: a failure to notify must not fail the format.
        /// </summary>
        public static async Task ShowNoticeAsync(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var sp = (IServiceProvider?)Package.GetGlobalService(typeof(SVsShell));
                if (sp == null)
                {
                    Log.Warning("Format notice: shell service unavailable, message not shown: {Message}", message);
                    return;
                }
                VsShellUtilities.ShowMessageBox(
                    sp, message, Title,
                    OLEMSGICON.OLEMSGICON_WARNING,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Format notice: failed to show message box");
            }
        }

        // ── Profile-fallback notice (spec 033 follow-up) ─────────────────────────────────
        //
        // Lives here, not on FormatRequestDispatcher: that dispatcher is currently unreferenced
        // production code (a deletion candidate), and the LIVE caller is FormatDocumentCommand —
        // removing the dispatcher must not silently take Format SQL's missing-style notice with it.
        // Warned-once bookkeeping is process-wide (static) so every format trigger shares it.

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

            Log.Warning("Format: {Warning}", warning);

            var notifier = ProfileFallbackNotifierOverride;
            if (notifier != null)
            {
                notifier(warning!);
                return;
            }

            // Fire-and-forget: formatting must not block on a message box.
            _ = ShowNoticeAsync(warning!);
        }


        private static string BuildMessage(bool success, FormatDiagnosticInfo[]? diagnostics)
        {
            var detail = FirstMeaningfulMessage(diagnostics);
            var lead = success
                // Parsed, but the formatted output failed semantic re-validation (Stage 6).
                ? "Formatting was skipped because the result would have changed the query's meaning. The original text was left unchanged."
                // Could not parse / format at all.
                : "The SQL could not be parsed, so it was left unchanged. Fix any syntax errors and try again.";
            return string.IsNullOrEmpty(detail) ? lead : lead + "\n\n" + detail;
        }

        private static string? FirstMeaningfulMessage(FormatDiagnosticInfo[]? diagnostics)
        {
            if (diagnostics == null || diagnostics.Length == 0)
                return null;
            // Prefer an Error (Severity 2), then a Warning (1), then anything with text.
            var pick = diagnostics.FirstOrDefault(d => d.Severity == 2 && !string.IsNullOrWhiteSpace(d.Message))
                       ?? diagnostics.FirstOrDefault(d => d.Severity == 1 && !string.IsNullOrWhiteSpace(d.Message))
                       ?? diagnostics.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d.Message));
            if (pick == null) return null;
            return pick.Line > 0 ? $"{pick.Message} (line {pick.Line})" : pick.Message;
        }
    }
}
