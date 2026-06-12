using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Serilog;

namespace AkmlSql.Shell.Shared.StatusBar
{
    internal static class StatusBarManager
    {
        /// <summary>Tracks whether we are currently displaying a transaction indicator.</summary>
        private static bool _transactionIndicatorActive;

        /// <summary>
        /// The current idle text, restored after a transient indicator clears. Spec 030 T021 /
        /// FR-006: when "show active style in status bar" is on this carries the active formatting
        /// style, so the user can always see which style Format SQL will apply.
        /// </summary>
        private static string _idleText = $"AKML SQL v{Core.Constants.RuntimeVersion}";

        public static void SetLoaded(IVsStatusbar statusBar) => SetLoaded(statusBar, null);

        /// <summary>
        /// Sets the idle status text, optionally annotated with the active formatting style
        /// (spec 030 T021). Pass <paramref name="activeProfile"/> = null for the plain version.
        /// </summary>
        public static void SetLoaded(IVsStatusbar statusBar, string? activeProfile)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _idleText = BuildIdleText(activeProfile);
            SetText(statusBar, _idleText);
        }

        /// <summary>
        /// Updates the active-style portion of the idle text when the user switches styles
        /// (spec 030 T021 / FR-006). Repaints immediately unless a transient indicator is showing,
        /// in which case the new idle text is restored when that indicator clears.
        /// </summary>
        public static void SetActiveProfile(IVsStatusbar statusBar, string? activeProfile)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _idleText = BuildIdleText(activeProfile);
            if (!_transactionIndicatorActive)
                SetText(statusBar, _idleText);
        }

        private static string BuildIdleText(string? activeProfile)
        {
            var version = $"AKML SQL v{Core.Constants.RuntimeVersion}";
            return string.IsNullOrWhiteSpace(activeProfile)
                ? version
                : $"{version} · Format: {activeProfile}";
        }

        public static void SetFailed(IVsStatusbar statusBar)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            SetText(statusBar, "AKML SQL [FAILED]");
        }

        /// <summary>
        /// Displays a transaction warning indicator in the status bar.
        /// Called by <see cref="Safety.TransactionMonitor"/> to show elapsed time.
        /// </summary>
        /// <param name="statusBar">The VS status bar service.</param>
        /// <param name="text">
        /// The text to display (e.g. <c>"OPEN TRANSACTION (2m 15s)"</c>).
        /// </param>
        public static void SetTransactionIndicator(IVsStatusbar statusBar, string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            SetText(statusBar, text);
            _transactionIndicatorActive = true;
        }

        /// <summary>
        /// Clears the transaction indicator from the status bar and restores the default text.
        /// </summary>
        /// <param name="statusBar">The VS status bar service.</param>
        public static void ClearTransactionIndicator(IVsStatusbar statusBar)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!_transactionIndicatorActive) return;

            SetText(statusBar, _idleText);
            _transactionIndicatorActive = false;
        }

        private static void SetText(IVsStatusbar statusBar, string text)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                statusBar.IsFrozen(out int frozen);
                if (frozen != 0)
                {
                    statusBar.FreezeOutput(0);
                }

                statusBar.SetText(text);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to update status bar");
            }
        }
    }
}
