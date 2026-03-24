#nullable enable
using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using AkmlSql.Core.Config;
using AkmlSql.Core.Models.Safety;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Serilog;
using Constants = AkmlSql.Core.Constants;
using Timer = System.Threading.Timer;

namespace AkmlSql.Shell.Shared.Safety
{
    /// <summary>
    /// Monitors SQL execution for open transactions and reminds the user to commit or rollback.
    /// <para>
    /// Tracks <c>BEGIN TRAN</c>, <c>COMMIT</c>, and <c>ROLLBACK</c> keywords in executed SQL text.
    /// When a transaction remains open for longer than the configured reminder interval,
    /// a warning dialog is shown.
    /// </para>
    /// <para>
    /// Also intercepts tab close events when an uncommitted transaction exists,
    /// prompting the user before allowing the close.
    /// </para>
    /// </summary>
    internal static class TransactionMonitor
    {
        // ----- Regex patterns for transaction detection -----

        /// <summary>
        /// Matches BEGIN TRAN or BEGIN TRANSACTION (case-insensitive, word boundaries).
        /// Accounts for optional transaction name.
        /// </summary>
        private static readonly Regex BeginTranRegex = new Regex(
            @"\bBEGIN\s+TRAN(?:SACTION)?\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Matches COMMIT TRAN, COMMIT TRANSACTION, or bare COMMIT (case-insensitive).
        /// </summary>
        private static readonly Regex CommitRegex = new Regex(
            @"\bCOMMIT\b(?:\s+TRAN(?:SACTION)?\b)?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Matches ROLLBACK TRAN, ROLLBACK TRANSACTION, or bare ROLLBACK (case-insensitive).
        /// </summary>
        private static readonly Regex RollbackRegex = new Regex(
            @"\bROLLBACK\b(?:\s+TRAN(?:SACTION)?\b)?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // ----- State -----

        /// <summary>
        /// Active transaction states keyed by document moniker (full path or tab identifier).
        /// </summary>
        private static readonly ConcurrentDictionary<string, TransactionState> _activeTransactions
            = new ConcurrentDictionary<string, TransactionState>(StringComparer.OrdinalIgnoreCase);

        private static Package? _package;
        private static Timer? _reminderTimer;
        private static Timer? _statusBarTimer;
        private static int _reminderIntervalSeconds = 300;
        private static bool _reminderEnabled = true;
        private static bool _initialized;
        private static DTE? _dte;

        // ----- Initialization -----

        /// <summary>
        /// Initializes the transaction monitor. Must be called on the UI thread.
        /// </summary>
        /// <param name="package">The hosting <see cref="AsyncPackage"/>.</param>
        public static void Initialize(Package package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_initialized) return;

            _package = package;

            try
            {
                // Load settings
                var settings = ConfigManager.Load();
                _reminderEnabled = settings.Safety?.TransactionReminder ?? true;
                _reminderIntervalSeconds = settings.Safety?.TransactionReminderInterval ?? 300;
                if (_reminderIntervalSeconds < 30) _reminderIntervalSeconds = 30;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "TransactionMonitor: failed to load safety settings; using defaults");
            }

            try
            {
                _dte = (DTE)Package.GetGlobalService(typeof(DTE));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "TransactionMonitor: DTE service unavailable");
            }

            // Start the periodic reminder timer (checks every 30 seconds)
            _reminderTimer = new Timer(OnReminderTimerTick, null,
                TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

            // Start a status bar update timer (every 1 second for elapsed time display)
            _statusBarTimer = new Timer(OnStatusBarTimerTick, null,
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

            _initialized = true;
            Log.Information("TransactionMonitor: initialized (reminder={Enabled}, interval={Interval}s)",
                _reminderEnabled, _reminderIntervalSeconds);
        }

        /// <summary>
        /// Shuts down the transaction monitor and releases timers.
        /// </summary>
        public static void Shutdown()
        {
            _reminderTimer?.Dispose();
            _reminderTimer = null;

            _statusBarTimer?.Dispose();
            _statusBarTimer = null;

            _activeTransactions.Clear();
            _initialized = false;
            _dte = null;
            _package = null;
        }

        // ----- Public API for execution hooks -----

        /// <summary>
        /// Analyzes the executed SQL text for transaction control statements and updates
        /// the transaction state for the specified tab.
        /// <para>
        /// Call this after each SQL execution completes (from the execution event handler).
        /// </para>
        /// </summary>
        /// <param name="tabId">The document moniker or unique tab identifier.</param>
        /// <param name="executedSql">The SQL text that was executed.</param>
        public static void OnSqlExecuted(string tabId, string executedSql)
        {
            if (string.IsNullOrEmpty(tabId) || string.IsNullOrEmpty(executedSql))
                return;

            try
            {
                // Count occurrences of each transaction keyword
                var beginCount = BeginTranRegex.Matches(executedSql).Count;
                var commitCount = CommitRegex.Matches(executedSql).Count;
                var rollbackCount = RollbackRegex.Matches(executedSql).Count;

                // Handle ROLLBACK first — it clears the entire transaction regardless of nesting
                if (rollbackCount > 0)
                {
                    if (_activeTransactions.TryRemove(tabId, out _))
                    {
                        Log.Information("TransactionMonitor: ROLLBACK detected on '{TabId}', transaction cleared", tabId);
                        UpdateStatusBarForActiveTab();
                    }
                    return;
                }

                // Handle COMMIT — decrements the nesting count
                if (commitCount > 0)
                {
                    if (_activeTransactions.TryGetValue(tabId, out var existing))
                    {
                        existing.TranCount -= commitCount;
                        if (existing.TranCount <= 0)
                        {
                            _activeTransactions.TryRemove(tabId, out _);
                            Log.Information("TransactionMonitor: all transactions committed on '{TabId}'", tabId);
                            UpdateStatusBarForActiveTab();
                        }
                        else
                        {
                            Log.Debug("TransactionMonitor: COMMIT on '{TabId}', remaining TranCount={Count}",
                                tabId, existing.TranCount);
                        }
                    }
                }

                // Handle BEGIN TRAN — creates or increments
                if (beginCount > 0)
                {
                    _activeTransactions.AddOrUpdate(
                        tabId,
                        _ =>
                        {
                            Log.Information("TransactionMonitor: BEGIN TRAN detected on '{TabId}'", tabId);
                            return new TransactionState(tabId, DateTime.UtcNow, beginCount);
                        },
                        (_, existing) =>
                        {
                            existing.TranCount += beginCount;
                            Log.Debug("TransactionMonitor: nested BEGIN TRAN on '{TabId}', TranCount={Count}",
                                tabId, existing.TranCount);
                            return existing;
                        });
                    UpdateStatusBarForActiveTab();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "TransactionMonitor: error processing SQL execution for '{TabId}'", tabId);
            }
        }

        /// <summary>
        /// Checks whether the specified tab has an uncommitted transaction. If so,
        /// shows a modal dialog warning the user and returns the chosen action.
        /// <para>
        /// Call this before allowing a tab/document to close.
        /// </para>
        /// </summary>
        /// <param name="tabId">The document moniker.</param>
        /// <returns>
        /// <c>true</c> if the close should proceed (no transaction, or user chose Commit/Rollback).
        /// <c>false</c> if the user chose Cancel.
        /// </returns>
        public static bool OnTabClosing(string tabId)
        {
            if (string.IsNullOrEmpty(tabId))
                return true;

            if (!_activeTransactions.TryGetValue(tabId, out var state))
                return true;

            if (state.TranCount <= 0)
            {
                _activeTransactions.TryRemove(tabId, out _);
                return true;
            }

            // Show modal warning on the UI thread
            var elapsed = DateTime.UtcNow - state.StartedAt;
            var message = $"This tab has an uncommitted transaction (open for {FormatElapsed(elapsed)}).\n\n" +
                          "Choose an action:\n" +
                          "  Yes = Commit the transaction and close\n" +
                          "  No = Rollback the transaction and close\n" +
                          "  Cancel = Keep the tab open";

            var result = MessageBox.Show(
                message,
                Constants.ProductName + " - Open Transaction Warning",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button3); // Default to Cancel (safe choice)

            switch (result)
            {
                case DialogResult.Yes:
                    Log.Information("TransactionMonitor: user chose COMMIT for '{TabId}'", tabId);
                    _activeTransactions.TryRemove(tabId, out _);
                    return true; // Let the tab close (commit should be handled by the caller)

                case DialogResult.No:
                    Log.Information("TransactionMonitor: user chose ROLLBACK for '{TabId}'", tabId);
                    _activeTransactions.TryRemove(tabId, out _);
                    return true; // Let the tab close

                case DialogResult.Cancel:
                default:
                    Log.Information("TransactionMonitor: user cancelled close for '{TabId}'", tabId);
                    return false; // Prevent close
            }
        }

        /// <summary>
        /// Returns the <see cref="TransactionState"/> for the specified tab, or <c>null</c>
        /// if no transaction is active.
        /// </summary>
        public static TransactionState? GetState(string tabId)
        {
            if (string.IsNullOrEmpty(tabId)) return null;
            return _activeTransactions.TryGetValue(tabId, out var state) ? state : null;
        }

        /// <summary>
        /// Returns <c>true</c> if there are any active transactions across all tabs.
        /// </summary>
        public static bool HasActiveTransactions => !_activeTransactions.IsEmpty;

        /// <summary>
        /// Clears all tracked transaction state. Used during shutdown.
        /// </summary>
        public static void ClearAll()
        {
            _activeTransactions.Clear();
        }

        // ----- Timer callbacks -----

        /// <summary>
        /// Periodic reminder check. For each tab with an open transaction, if the time since
        /// the last reminder exceeds the configured interval, show a reminder message.
        /// </summary>
        private static void OnReminderTimerTick(object? state)
        {
            if (!_reminderEnabled || _activeTransactions.IsEmpty)
                return;

            var now = DateTime.UtcNow;

            foreach (var kvp in _activeTransactions)
            {
                var txState = kvp.Value;
                if (txState.TranCount <= 0) continue;

                // Determine the last reminder time (or use StartedAt if never reminded)
                var lastCheck = txState.LastReminderAt ?? txState.StartedAt;
                var elapsed = now - lastCheck;

                if (elapsed.TotalSeconds < _reminderIntervalSeconds)
                    continue;

                // Show reminder on UI thread
                try
                {
                    ThreadHelper.JoinableTaskFactory.Run(async () =>
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                        var totalElapsed = DateTime.UtcNow - txState.StartedAt;
                        var message = $"Tab '{kvp.Key}' has had an uncommitted transaction for {FormatElapsed(totalElapsed)}.\n\n" +
                                      "Remember to COMMIT or ROLLBACK when done.";

                        MessageBox.Show(
                            message,
                            Constants.ProductName + " - Transaction Reminder",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    });

                    txState.LastReminderAt = DateTime.UtcNow;
                    Log.Information("TransactionMonitor: reminder shown for '{TabId}' (open {Elapsed})",
                        kvp.Key, FormatElapsed(now - txState.StartedAt));
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "TransactionMonitor: failed to show reminder for '{TabId}'", kvp.Key);
                }
            }
        }

        /// <summary>
        /// Updates the status bar every second with the elapsed time of the active transaction
        /// for the currently focused document.
        /// </summary>
        private static void OnStatusBarTimerTick(object? state)
        {
            if (_activeTransactions.IsEmpty)
                return;

            try
            {
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    UpdateStatusBarForActiveTab();
                });
            }
            catch
            {
                // Swallow — status bar update is non-critical
            }
        }

        /// <summary>
        /// Updates the status bar text for the currently active document's transaction state.
        /// Must be called on the UI thread.
        /// </summary>
        private static void UpdateStatusBarForActiveTab()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var statusBar = (Microsoft.VisualStudio.Shell.Interop.IVsStatusbar?)
                    Package.GetGlobalService(typeof(Microsoft.VisualStudio.Shell.Interop.SVsStatusbar));
                if (statusBar == null) return;

                // Get the active document moniker
                string? activeTabId = null;
                try
                {
                    if (_dte?.ActiveDocument != null)
                    {
                        activeTabId = _dte.ActiveDocument.FullName;
                    }
                }
                catch
                {
                    // No active document
                }

                if (!string.IsNullOrEmpty(activeTabId) &&
                    _activeTransactions.TryGetValue(activeTabId!, out var txState) &&
                    txState.TranCount > 0)
                {
                    var elapsed = DateTime.UtcNow - txState.StartedAt;
                    StatusBar.StatusBarManager.SetTransactionIndicator(
                        statusBar, $"OPEN TRANSACTION ({FormatElapsed(elapsed)})");
                }
                else
                {
                    StatusBar.StatusBarManager.ClearTransactionIndicator(statusBar);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "TransactionMonitor: status bar update failed");
            }
        }

        // ----- Helpers -----

        /// <summary>
        /// Formats a <see cref="TimeSpan"/> as a human-readable elapsed time string.
        /// </summary>
        private static string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed.TotalHours >= 1)
                return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m {elapsed.Seconds}s";
            if (elapsed.TotalMinutes >= 1)
                return $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";
            return $"{(int)elapsed.TotalSeconds}s";
        }
    }
}
