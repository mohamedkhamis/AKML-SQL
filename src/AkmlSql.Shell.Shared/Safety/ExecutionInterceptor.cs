#nullable enable
using System;
using System.Linq;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
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

                // Resolve environment info once — used for production detection, dialog mode, and audit
                EnvironmentRule? matchedEnvRule = null;
                bool isProductionServer = false;
                try
                {
                    matchedEnvRule = EnvironmentDetector.Match(serverName);
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

                // Per-environment severity override: "Disabled" skips the
                // guard entirely for this environment (e.g. the user's DEV boxes).
                if (IsEnvironmentDisabled(envLabel, cachedSafety))
                {
                    Log.Warning("[ExecutionGuard] Safety check suppressed for environment '{EnvLabel}': marked as Disabled in Safety.EnvironmentSeverity config",
                        envLabel);
                    LogAuditEvent(serverName, envLabel, envColor, filteredWarnings, "SkippedByEnvironmentConfig");
                    return true;
                }

                ThreadHelper.ThrowIfNotOnUIThread();
                var dialog = SafetyWarningDialog.CreateForWarnings(filteredWarnings, serverName, envLabel, envColor);
                var beforeDialogTs = DateTime.UtcNow;
                Log.Information(
                    "[ExecutionGuard] BEFORE SafetyWarningDialog.ShowDialog: {Count} warnings, env='{Env}' — if SSMS appears to freeze NOW, check Alt-Tab / other monitors for a hidden modal dialog",
                    filteredWarnings.Length, envLabel);

                bool? wpfResult;
                try
                {
                    wpfResult = dialog.ShowDialog();
                }
                finally
                {
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
                    return true;
                }

                LogAuditEvent(serverName, envLabel, envColor, filteredWarnings, "Blocked");
                Log.Information("[ExecutionGuard] EXIT: user Cancelled ({Ms} ms total)",
                    (DateTime.UtcNow - enterTs).TotalMilliseconds);
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
