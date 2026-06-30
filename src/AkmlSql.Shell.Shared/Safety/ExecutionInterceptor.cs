#nullable enable
using System;
using System.Linq;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Editor;
using AkmlSql.Shell.Shared.Ipc;
using AkmlSql.Core.Models.Tabs;
using AkmlSql.Shell.Shared.Tabs;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Safety
{
    /// <summary>
    /// Intercepts SQL query execution to perform safety checks before the query is sent
    /// to SQL Server. Sends the SQL text to the engine for AST analysis, and if dangerous
    /// patterns are detected, shows a modal warning dialog to the user.
    /// <para>
    /// The interceptor blocks execution until the user confirms or cancels. If the user
    /// cancels, the query execution is prevented.
    /// </para>
    /// </summary>
    internal static class ExecutionInterceptor
    {
        private static bool _initialized;
        private static bool _anySettingEnabled;
        private static Package? _package;

        /// <summary>
        /// Per-session set of warning type ints the user has opted out of via the
        /// "Don't ask again for this session" checkbox.  Cleared when the IDE session
        /// ends (static lifetime = package lifetime).
        /// </summary>
        private static readonly System.Collections.Generic.HashSet<int> _suppressedWarningTypes = new();

        // ── Re-entry dedup (asymmetric: different windows for cancel vs. confirm) ─
        // After the WPF dialog closes, SSMS re-dispatches Query.Execute differently
        // depending on the user's choice:
        //
        //   • CANCEL: SSMS auto-retries the cancelled command at ~+4 s (and
        //     occasionally a second time at ~+8 s), then gives up. Field logs
        //     show a 22-second gap of silence between cancel and the user's
        //     deliberate F5, confirming SSMS does not retry indefinitely.
        //     A 10-second window catches the auto-retries while letting a
        //     deliberate user retry through after a wait.
        //
        //   • CONFIRM: the SQL has executed; SSMS does NOT auto-retry. We only
        //     need a short window (4000 ms) to absorb the immediate modal-pump
        //     re-fire. A deliberate F5 after this window must re-prompt — caching
        //     a confirm longer would silently bypass the safety dialog on
        //     destructive re-runs (a safety regression).
        //
        // Cache is keyed on sqlHash; process-static, so persists across tabs (same
        // SQL in another tab = same intent — acceptable).
        private const int CancelDedupWindowMs = 10000;
        private const int ConfirmDedupWindowMs = 4000;
        private static int _lastDecisionSqlHash;
        private static DateTime _lastDecisionTimeUtc;
        private static bool _lastDecisionResult;

        // ── Concurrent-dialog guard ──────────────────────────────────────────────
        // Re-entry dedup only catches re-fires AFTER ShowDialog returns (cache is
        // populated in RememberDecision at end-of-flow). SSMS also re-fires
        // Query.Execute *during* the dialog's modal pump — observed at 18:43:38.354
        // BEFORE ShowDialog → 18:43:38.358 ENTER (4 ms later, dialog still open),
        // both reaching the engine and stacking concurrent dialogs. While
        // _dialogShowing is true, suppress the duplicate dispatch.
        private static volatile bool _dialogShowing;

        /// <summary>
        /// Initializes the execution interceptor. Reads safety settings from config to
        /// determine if any safety checks are enabled.
        /// </summary>
        /// <param name="package">The VS/SSMS package for service resolution.</param>
        public static void Initialize(Package package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_initialized) return;
            _initialized = true;
            _package = package;

            try
            {
                var settings = ConfigManager.Load();
                var safety = settings.Safety;

                // Note: TransactionReminder is handled by TransactionMonitor, not ExecutionInterceptor.
                // Including it here would cause unnecessary engine IPC on every execution when
                // only TransactionReminder is enabled (engine has no corresponding warning type).
                _anySettingEnabled = safety.ProductionWarning
                                     || safety.DeleteWithoutWhere
                                     || safety.UpdateWithoutWhere
                                     || safety.DropConfirmation
                                     || safety.TruncateConfirmation
                                     || safety.MergeNoFilter
                                     || safety.InsideJoin
                                     || safety.InsideProcOrTrigger;

                // Always install the DTE hook — settings may be enabled later without restart.
                // OnBeforeExecute re-checks settings dynamically on each invocation.
                ExecutionCommandFilter.Install(package);

                // Register F1 help context for the safety dialog.
                Help.F1HelpListener.Default.Register("akmlsql.dialog.safety",
                    "https://github.com/mohamedkhamis/AKML-SQL/blob/master/doc/execution-safety.md");

                Log.Information("ExecutionInterceptor: initialized (safety checks enabled)");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ExecutionInterceptor: failed to initialize");
            }
        }

        /// <summary>
        /// Called before a SQL query is executed. Sends the SQL text to the engine for
        /// safety analysis. If warnings are found, displays a modal dialog on the UI thread.
        /// <para>
        /// This method blocks synchronously — it must complete before execution proceeds.
        /// </para>
        /// </summary>
        /// <param name="sqlText">The SQL text about to be executed.</param>
        /// <param name="serverName">The target SQL Server name (may be null).</param>
        /// <returns>
        /// <c>true</c> if execution should proceed (no warnings, or user confirmed).
        /// <c>false</c> if execution should be cancelled (user chose Cancel).
        /// </returns>
        public static bool OnBeforeExecute(string sqlText, string? serverName)
        {
            // ── Trace entry + exit for each step so the next SSMS hang repro is
            // localizable from logs alone (previous repro went 3 minutes silent
            // after this method was called — no way to tell where it blocked).
            // Use Information so the trace survives Debug-filtered configs.
            var enterTs = DateTime.UtcNow;
            Log.Information("[ExecutionGuard] ENTER OnBeforeExecute: sql.Length={SqlLen} server={Server}",
                sqlText?.Length ?? 0, serverName ?? "(null)");

            // Concurrent-dialog guard — see field declarations above. If a dialog
            // is currently showing, this is SSMS's modal-pump re-fire of the same
            // Query.Execute (the cache from RememberDecision hasn't been populated
            // yet because ShowDialog hasn't returned). Suppress the duplicate so
            // we don't stack two dialogs for one F5 press.
            if (_dialogShowing)
            {
                Log.Warning("[ExecutionGuard] Re-entry while dialog open: suppressing duplicate dispatch (SSMS modal-pump re-fire)");
                return false;
            }

            // Re-entry dedup — see field declarations above. Asymmetric by outcome:
            //   • cancel-window: same SQL within CancelDedupWindowMs of last cancel
            //     → return false (suppresses SSMS auto-retries which fire at ~+4 s)
            //   • confirm-window: same SQL within ConfirmDedupWindowMs of last confirm
            //     → return true (just enough to absorb the immediate modal-pump re-fire)
            // Past the window, a deliberate F5 falls through to a fresh check.
            int sqlHash = sqlText?.GetHashCode() ?? 0;
            var sinceLastDecision = (enterTs - _lastDecisionTimeUtc).TotalMilliseconds;
            if (sqlHash != 0 && sqlHash == _lastDecisionSqlHash)
            {
                if (!_lastDecisionResult && sinceLastDecision < CancelDedupWindowMs)
                {
                    Log.Warning(
                        "[ExecutionGuard] Re-entry dedup (cancel-window): same SQL submitted {Ms} ms after last cancel — returning cached decision=false (wait {ExpiryS}s past cancel or edit SQL to retry)",
                        sinceLastDecision, CancelDedupWindowMs / 1000);
                    return false;
                }
                if (_lastDecisionResult && sinceLastDecision < ConfirmDedupWindowMs)
                {
                    Log.Warning(
                        "[ExecutionGuard] Re-entry dedup (confirm-window): same SQL submitted {Ms} ms after last confirm — returning cached decision=true",
                        sinceLastDecision);
                    return true;
                }
            }

            // Re-check settings dynamically on each invocation so that enabling
            // safety settings via Options takes effect without an IDE restart.
            // Cache the loaded settings to avoid a second disk read in FilterBySettings.
            SafetySettings? cachedSafety = null;
            try
            {
                cachedSafety = ConfigManager.Load().Safety;
                _anySettingEnabled = cachedSafety.ProductionWarning
                                     || cachedSafety.DeleteWithoutWhere
                                     || cachedSafety.UpdateWithoutWhere
                                     || cachedSafety.DropConfirmation
                                     || cachedSafety.TruncateConfirmation
                                     || cachedSafety.MergeNoFilter
                                     || cachedSafety.InsideJoin
                                     || cachedSafety.InsideProcOrTrigger;
            }
            catch
            {
                // Config load failure — use last known value
            }

            // ── Emergency kill-switch. If the safety check is suspected of hanging
            // SSMS, the user can set `Safety.TemporarilyDisabled=true` in
            // %AppData%\AKML SQL\config.json and F5 will go straight through without
            // touching the engine or the modal dialog — no reinstall required.
            if (cachedSafety?.TemporarilyDisabled == true)
            {
                Log.Warning("[ExecutionGuard] EXIT: Safety.TemporarilyDisabled=true in config — skipping all checks ({Ms} ms)",
                    (DateTime.UtcNow - enterTs).TotalMilliseconds);
                return true;
            }

            if (!_anySettingEnabled)
            {
                Log.Warning("[ExecutionGuard] EXIT: all safety checks disabled in config ({Ms} ms)",
                    (DateTime.UtcNow - enterTs).TotalMilliseconds);
                return true;
            }

            if (string.IsNullOrWhiteSpace(sqlText))
            {
                Log.Debug("[ExecutionGuard] EXIT: empty SQL text ({Ms} ms)",
                    (DateTime.UtcNow - enterTs).TotalMilliseconds);
                return true;
            }

            try
            {
                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected)
                {
                    Log.Information("[ExecutionGuard] EXIT: engine not connected, skipping safety check ({Ms} ms)",
                        (DateTime.UtcNow - enterTs).TotalMilliseconds);
                    return true; // Fail-open: allow execution if engine is unavailable
                }

                // Resolve environment info once — used for production detection, dialog mode, and audit.
                // Pass both server AND database so database-target EnvironmentRules (MatchTarget=Database)
                // fire correctly; using only serverName (1-arg overload) permanently passed databaseName=null
                // and silently skipped all database-scoped safety rules.
                EnvironmentRule? matchedEnvRule = null;
                bool isProductionServer = false;
                try
                {
                    string? databaseName = null;
                    try
                    {
                        var sp = ServiceProvider.GlobalProvider as System.IServiceProvider;
                        if (sp != null)
                        {
                            var connResult = SsmsConnectionDetector.TryDetectConnection(sp);
                            databaseName = connResult?.Database;
                        }
                    }
                    catch (Exception dbEx)
                    {
                        Log.Debug(dbEx, "ExecutionInterceptor: failed to resolve active database name for environment detection — proceeding with server-only match");
                    }

                    matchedEnvRule = EnvironmentDetector.Match(serverName, databaseName);
                    if (matchedEnvRule != null)
                    {
                        isProductionServer = matchedEnvRule.Label.IndexOf(
                            "PROD", StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "ExecutionInterceptor: environment detection failed");
                }

                var request = new SafetyCheckRequest
                {
                    SqlText = sqlText,
                    Server = serverName,
                    IsProductionServer = isProductionServer
                };

                // Run the IPC on the thread pool and wall-clock-wait with a hard ceiling.
                // JoinableTaskFactory.Run on the UI thread deadlocks if any continuation
                // requires the UI thread back; Task.Run + Wait(timeout) avoids that
                // because the awaiter is a thread-pool thread, not the UI thread.
                const int SafetyCheckTimeoutMs = 500;
                // Inner IPC timeout is slightly under the wall-clock ceiling so the inner
                // cancellation normally wins and we see a clean TaskCanceledException
                // rather than the outer Wait() timing out.
                const int InnerTimeoutSlackMs = 50;
                SafetyCheckResponse? response = null;
                var beforeIpcTs = DateTime.UtcNow;
                Log.Information("[ExecutionGuard] BEFORE engine SafetyCheck ({TimeoutMs}ms timeout)", SafetyCheckTimeoutMs);

                var ipcTask = System.Threading.Tasks.Task.Run(() =>
                    client.SendRequestAsync<SafetyCheckResponse, SafetyCheckRequest>(
                        MessageTypes.SafetyCheck,
                        request,
                        timeoutMs: SafetyCheckTimeoutMs - InnerTimeoutSlackMs));
                Log.Information("[ExecutionGuard] TaskRun scheduled, status={Status}, about to Wait({TimeoutMs}ms)",
                    ipcTask.Status, SafetyCheckTimeoutMs);

                try
                {
                    if (!ipcTask.Wait(SafetyCheckTimeoutMs))
                    {
                        // Wall-clock ceiling hit — fail-open without touching the task further.
                        // The task continues in the background; its result is ignored.
                        Log.Warning(
                            "[ExecutionGuard] AFTER engine SafetyCheck: WALL-CLOCK TIMEOUT after {Ms} ms (fail-open)",
                            (DateTime.UtcNow - beforeIpcTs).TotalMilliseconds);
                        // Hook a continuation so we eventually see whether the orphaned task
                        // completed (engine slow but alive) vs. hung indefinitely (engine dead).
                        var orphanStartTs = beforeIpcTs;
                        _ = ipcTask.ContinueWith(t => Log.Warning(
                                "[ExecutionGuard] orphan ipcTask completed AFTER timeout: status={Status} faulted={Faulted} totalMs={Ms}",
                                t.Status, t.IsFaulted, (DateTime.UtcNow - orphanStartTs).TotalMilliseconds),
                            System.Threading.Tasks.TaskScheduler.Default);
                        return true;
                    }
                    response = ipcTask.GetAwaiter().GetResult();
                    Log.Information(
                        "[ExecutionGuard] AFTER engine SafetyCheck: response={ResponseState} ({Ms} ms, taskStatus={Status})",
                        response == null ? "null" :
                            (response.RequiresConfirmation ? $"RequiresConfirmation w/ {response.Warnings?.Length ?? 0} warnings" : "no-warnings"),
                        (DateTime.UtcNow - beforeIpcTs).TotalMilliseconds,
                        ipcTask.Status);
                }
                catch (Exception ipcEx)
                {
                    var inner = (ipcEx as AggregateException)?.InnerException ?? ipcEx;
                    Log.Information(inner,
                        "[ExecutionGuard] AFTER engine SafetyCheck: FAILED/TIMEOUT after {Ms} ms (fail-open)",
                        (DateTime.UtcNow - beforeIpcTs).TotalMilliseconds);
                    return true;
                }

                if (response == null || !response.RequiresConfirmation || response.Warnings.Length == 0)
                {
                    return true; // No warnings — proceed
                }

                // Filter warnings based on which safety settings are enabled (reuse cached settings)
                var filteredWarnings = FilterBySettings(response.Warnings, cachedSafety);
                if (filteredWarnings.Length == 0)
                {
                    return true; // All detected warnings are for disabled settings
                }

                // Strip warnings the user suppressed for this session
                filteredWarnings = filteredWarnings
                    .Where(w => !_suppressedWarningTypes.Contains(w.WarningType))
                    .ToArray();
                if (filteredWarnings.Length == 0)
                {
                    return true;
                }

                var envLabel = matchedEnvRule?.Label ?? "Unknown";
                var envColor = matchedEnvRule?.Color ?? "";
                var envSeverity = cachedSafety != null &&
                                  cachedSafety.EnvironmentSeverity.TryGetValue(envLabel, out var sev)
                                  ? sev : "(default)";
                Log.Information("[ExecutionGuard] env: server='{Server}' label='{EnvLabel}' severity={Severity} → {Count} warning(s) about to be shown",
                    serverName ?? "(null)", envLabel, envSeverity, filteredWarnings.Length);

                // Per-environment severity override: "Disabled" silences the
                // guard for the environment — but NEVER for destructive (Severity≥2)
                // warnings (DELETE/UPDATE without WHERE, DROP, TRUNCATE). Even on
                // a DEV box those should still be confirmed so users don't acci-
                // dentally wipe a table just because the env happens to be local.
                if (IsEnvironmentDisabled(envLabel, cachedSafety))
                {
                    var destructive = filteredWarnings.Where(w => w.Severity >= 2).ToArray();
                    if (destructive.Length == 0)
                    {
                        Log.Warning("[ExecutionGuard] Safety check suppressed for environment '{EnvLabel}' (severity=Disabled, no destructive warnings)",
                            envLabel);
                        LogAuditEvent(serverName, envLabel, envColor, filteredWarnings, "SkippedByEnvironmentConfig");
                        return true;
                    }
                    Log.Warning("[ExecutionGuard] Environment '{EnvLabel}' is Disabled but {Count} destructive warning(s) cannot be silently bypassed — showing dialog anyway",
                        envLabel, destructive.Length);
                    filteredWarnings = destructive;
                }

                ThreadHelper.ThrowIfNotOnUIThread();
                var dialog = SafetyWarningDialog.CreateForWarnings(filteredWarnings, serverName, envLabel, envColor);
                var beforeDialogTs = DateTime.UtcNow;
                Log.Information(
                    "[ExecutionGuard] BEFORE SafetyWarningDialog.ShowDialog: {Count} warnings, env='{Env}' — if SSMS appears to freeze NOW, check Alt-Tab / other monitors for a hidden modal dialog",
                    filteredWarnings.Length, envLabel);

                bool? wpfResult;
                _dialogShowing = true;
                try
                {
                    wpfResult = dialog.ShowDialog();
                }
                finally
                {
                    _dialogShowing = false;
                    Log.Information("[ExecutionGuard] AFTER SafetyWarningDialog.ShowDialog: user sat on the dialog for {Ms} ms",
                        (DateTime.UtcNow - beforeDialogTs).TotalMilliseconds);
                }

                if (wpfResult == true)
                {
                    // Record opt-out if the user ticked "Don't ask again"
                    if (dialog.SuppressForSession)
                    {
                        foreach (var w in filteredWarnings)
                            _suppressedWarningTypes.Add(w.WarningType);
                    }
                    LogAuditEvent(serverName, envLabel, envColor, filteredWarnings, "Confirmed");
                    Log.Information("[ExecutionGuard] EXIT: user Confirmed ({Ms} ms total)",
                        (DateTime.UtcNow - enterTs).TotalMilliseconds);
                    RememberDecision(sqlHash, true);
                    return true;
                }

                LogAuditEvent(serverName, envLabel, envColor, filteredWarnings, "Blocked");
                Log.Information("[ExecutionGuard] EXIT: user Cancelled ({Ms} ms total)",
                    (DateTime.UtcNow - enterTs).TotalMilliseconds);
                RememberDecision(sqlHash, false);
                return false;
            }
            catch (Exception ex)
            {
                // IPC timeout/faults are caught and logged inline near JoinableTaskFactory.Run;
                // this outer handler is for anything that escapes (dialog construction, env
                // detection, etc.). Keep it fail-open so a guard bug never blocks execution.
                Log.Error(ex, "[ExecutionGuard] EXIT: failed with exception ({Ms} ms total, fail-open)",
                    (DateTime.UtcNow - enterTs).TotalMilliseconds);
                return true; // Fail-open on error
            }
        }

        /// <summary>
        /// Writes a structured audit log entry for an execution guard event.
        /// Logged at Warning level so audit entries are always visible in the log file.
        /// </summary>
        private static void LogAuditEvent(
            string? serverName,
            string environment,
            string environmentColor,
            SafetyWarningDto[] warnings,
            string outcome)
        {
            foreach (var w in warnings)
            {
                var statementType = ((Core.Models.Safety.SafetyWarningType)w.WarningType).ToString();
                var sqlPreview = w.Message?.Length > 500 ? w.Message.Substring(0, 500) : w.Message;

                Log.Warning(
                    "[ExecutionGuard] {Outcome} | Server={Server} | Environment={Environment} | Color={EnvironmentColor} | StatementType={StatementType} | Object={ObjectName} | SQL={SqlPreview}",
                    outcome,
                    serverName ?? "(unknown)",
                    environment,
                    environmentColor,
                    statementType,
                    w.ObjectName ?? "",
                    sqlPreview ?? "");
            }
        }

        /// <summary>
        /// Returns <c>true</c> if the user has configured the given environment
        /// as "Disabled" in <c>Safety.EnvironmentSeverity</c>, meaning the execution guard
        /// should be a no-op for that environment.
        /// </summary>
        /// <summary>
        /// Records the user's dialog decision so the very next OnBeforeExecute
        /// for the same SQL within <see cref="ReentryDedupWindowMs"/> ms can
        /// short-circuit and return the same answer instead of re-prompting.
        /// </summary>
        private static void RememberDecision(int sqlHash, bool result)
        {
            _lastDecisionSqlHash = sqlHash;
            _lastDecisionTimeUtc = DateTime.UtcNow;
            _lastDecisionResult = result;
        }

        private static bool IsEnvironmentDisabled(string envLabel, SafetySettings? safety)
        {
            if (safety == null ||
                string.IsNullOrEmpty(envLabel) ||
                string.Equals(envLabel, "Unknown", StringComparison.OrdinalIgnoreCase))
                return false;

            return safety.EnvironmentSeverity.TryGetValue(envLabel, out var level) &&
                   string.Equals(level, "Disabled", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Filters warnings based on which safety settings are currently enabled.
        /// </summary>
        private static SafetyWarningDto[] FilterBySettings(SafetyWarningDto[] warnings, SafetySettings? safety = null)
        {
            if (safety == null)
            {
                try
                {
                    safety = ConfigManager.Load().Safety;
                }
                catch
                {
                    return warnings; // Can't load config — show all warnings
                }
            }

            var filtered = new System.Collections.Generic.List<SafetyWarningDto>(warnings.Length);
            foreach (var w in warnings)
            {
                var type = (Core.Models.Safety.SafetyWarningType)w.WarningType;
                switch (type)
                {
                    case Core.Models.Safety.SafetyWarningType.ProductionDml:
                    case Core.Models.Safety.SafetyWarningType.ProductionDdl:
                        if (safety.ProductionWarning) filtered.Add(w);
                        break;
                    case Core.Models.Safety.SafetyWarningType.DeleteWithoutWhere:
                        if (safety.DeleteWithoutWhere) filtered.Add(w);
                        break;
                    case Core.Models.Safety.SafetyWarningType.UpdateWithoutWhere:
                        if (safety.UpdateWithoutWhere) filtered.Add(w);
                        break;
                    case Core.Models.Safety.SafetyWarningType.DropTable:
                    case Core.Models.Safety.SafetyWarningType.DropDatabase:
                        if (safety.DropConfirmation) filtered.Add(w);
                        break;
                    case Core.Models.Safety.SafetyWarningType.TruncateTable:
                        if (safety.TruncateConfirmation) filtered.Add(w);
                        break;
                    // Extended detection patterns (MERGE, JOIN, proc/trigger)
                    case Core.Models.Safety.SafetyWarningType.MergeWithoutFilter:
                        if (safety.MergeNoFilter) filtered.Add(w);
                        break;
                    case Core.Models.Safety.SafetyWarningType.DmlInsideJoinWithoutWhere:
                        if (safety.InsideJoin) filtered.Add(w);
                        break;
                    case Core.Models.Safety.SafetyWarningType.UnsafeDmlInProcOrTrigger:
                        if (safety.InsideProcOrTrigger) filtered.Add(w);
                        break;
                    default:
                        filtered.Add(w); // Unknown type — show it
                        break;
                }
            }

            return filtered.ToArray();
        }
    }
}
