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

        public static void SetLoaded(IVsStatusbar statusBar)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            SetText(statusBar, $"AKML SQL v{Core.Constants.RuntimeVersion}");
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

            SetText(statusBar, $"AKML SQL v{Core.Constants.RuntimeVersion}");
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
