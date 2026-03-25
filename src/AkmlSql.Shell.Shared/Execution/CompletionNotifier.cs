#nullable enable
using System;
using System.Runtime.InteropServices;
using AkmlSql.Core.Config;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Execution
{
    /// <summary>
    /// T083: Sends a Windows toast notification when a query execution exceeds the configured
    /// notification threshold (default 30 seconds). Uses the Windows notification API via COM interop.
    /// Falls back to a system tray balloon if toast is not available.
    /// </summary>
    internal static class CompletionNotifier
    {
        private static bool _initialized;

        /// <summary>
        /// Initializes the notifier. Currently a no-op; placeholder for future
        /// notification channel registration.
        /// </summary>
        public static void Initialize()
        {
            _initialized = true;
            Log.Debug("CompletionNotifier: initialized");
        }

        /// <summary>
        /// Sends a completion notification if the elapsed time exceeds the configured threshold.
        /// Safe to call from any thread.
        /// </summary>
        /// <param name="databaseName">The database context for the query.</param>
        /// <param name="elapsed">Total elapsed time of the query.</param>
        /// <param name="rowCount">Number of rows returned or affected.</param>
        /// <param name="success">Whether the query completed successfully.</param>
        public static void NotifyIfThresholdExceeded(
            string databaseName, TimeSpan elapsed, int rowCount, bool success)
        {
            if (!_initialized) return;

            try
            {
                var settings = ConfigManager.Load();
                var thresholdSeconds = settings.ExecutionProductivity.NotificationThreshold;

                if (thresholdSeconds <= 0 || elapsed.TotalSeconds < thresholdSeconds)
                    return;

                // Check if the VS window is currently in the foreground — no notification needed
                if (IsVsWindowActive())
                {
                    Log.Debug("CompletionNotifier: VS window is active, skipping notification");
                    return;
                }

                var title = success ? "Query Completed" : "Query Failed";
                var body = success
                    ? $"Database: {databaseName}\nRows: {rowCount:N0}\nElapsed: {elapsed:h\\:mm\\:ss\\.f}"
                    : $"Database: {databaseName}\nElapsed: {elapsed:h\\:mm\\:ss\\.f}";

                ShowToastNotification(title, body);

                Log.Information(
                    "CompletionNotifier: sent notification for {Database} (elapsed={Elapsed}s, success={Success})",
                    databaseName, elapsed.TotalSeconds, success);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "CompletionNotifier: failed to send notification");
            }
        }

        /// <summary>
        /// Shows a Windows toast/balloon notification.
        /// Uses NotifyIcon as a cross-version fallback since Windows toast APIs
        /// require UWP registration that may not be available in all environments.
        /// </summary>
        private static void ShowToastNotification(string title, string body)
        {
            try
            {
                // Use System.Windows.Forms.NotifyIcon for maximum compatibility across
                // Windows versions and VS/SSMS host environments.
                var notifyIcon = new System.Windows.Forms.NotifyIcon
                {
                    Visible = true,
                    Icon = System.Drawing.SystemIcons.Information,
                    BalloonTipTitle = $"AKML SQL - {title}",
                    BalloonTipText = body,
                    BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Info
                };

                notifyIcon.BalloonTipClosed += (s, e) =>
                {
                    notifyIcon.Visible = false;
                    notifyIcon.Dispose();
                };

                // Auto-cleanup after 10 seconds if user doesn't interact
                var cleanupTimer = new System.Windows.Forms.Timer { Interval = 10000 };

                notifyIcon.BalloonTipClicked += (s, e) =>
                {
                    // Stop the cleanup timer since the user interacted with the balloon
                    cleanupTimer.Stop();
                    cleanupTimer.Dispose();

                    // Bring VS/SSMS to the foreground when the user clicks the notification
                    try
                    {
                        ThreadHelper.JoinableTaskFactory.Run(async () =>
                        {
                            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                            var dte = (EnvDTE.DTE?)Package.GetGlobalService(typeof(EnvDTE.DTE));
                            dte?.MainWindow?.Activate();
                        });
                    }
                    catch { /* best effort */ }

                    notifyIcon.Visible = false;
                    notifyIcon.Dispose();
                };

                notifyIcon.ShowBalloonTip(5000);
                cleanupTimer.Tick += (s, e) =>
                {
                    cleanupTimer.Stop();
                    cleanupTimer.Dispose();
                    if (notifyIcon.Visible)
                    {
                        notifyIcon.Visible = false;
                        notifyIcon.Dispose();
                    }
                };
                cleanupTimer.Start();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "CompletionNotifier: toast notification failed");
            }
        }

        /// <summary>
        /// Checks whether the VS/SSMS main window is currently the foreground window.
        /// </summary>
        private static bool IsVsWindowActive()
        {
            try
            {
                var foregroundWindow = NativeMethods.GetForegroundWindow();
                if (foregroundWindow == IntPtr.Zero) return false;

                NativeMethods.GetWindowThreadProcessId(foregroundWindow, out uint processId);
                return processId == (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            }
            catch
            {
                return false;
            }
        }

        private static class NativeMethods
        {
            [DllImport("user32.dll")]
            public static extern IntPtr GetForegroundWindow();

            [DllImport("user32.dll")]
            public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        }
    }
}
