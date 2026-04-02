#nullable enable
using System;
using System.Windows.Forms;
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
                                     || safety.TruncateConfirmation;

                // Always install the DTE hook — settings may be enabled later without restart.
                // OnBeforeExecute re-checks settings dynamically on each invocation.
                ExecutionCommandFilter.Install(package);

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
                                     || cachedSafety.TruncateConfirmation;
            }
            catch
            {
                // Config load failure — use last known value
            }

            if (!_anySettingEnabled)
            {
                Log.Debug("[ExecutionGuard] Bypassed — all safety checks disabled");
                return true;
            }

            if (string.IsNullOrWhiteSpace(sqlText))
                return true;

            try
            {
                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected)
                {
                    Log.Debug("ExecutionInterceptor: engine not connected, skipping safety check");
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

                // Synchronous wait — must block before execution proceeds
                // Use JoinableTaskFactory to avoid deadlock on the UI thread
                SafetyCheckResponse? response = null;
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    response = await client.SendRequestAsync<SafetyCheckResponse, SafetyCheckRequest>(
                        MessageTypes.SafetyCheck,
                        request,
                        timeoutMs: 10_000);
                });

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

                var envLabel = matchedEnvRule?.Label ?? "Unknown";
                var envColor = matchedEnvRule?.Color ?? "";

                // Show the warning dialog on the UI thread, passing environment info
                // so the dialog can use EnvironmentSeverity config to determine mode
                DialogResult dialogResult = DialogResult.Cancel;
                ThreadHelper.ThrowIfNotOnUIThread();
                dialogResult = SafetyWarningDialog.Show(filteredWarnings, serverName, envLabel, envColor);

                if (dialogResult == DialogResult.OK)
                {
                    LogAuditEvent(serverName, envLabel, envColor, filteredWarnings, "Confirmed");
                    return true;
                }

                LogAuditEvent(serverName, envLabel, envColor, filteredWarnings, "Blocked");
                return false;
            }
            catch (OperationCanceledException)
            {
                Log.Debug("ExecutionInterceptor: safety check timed out, allowing execution (fail-open)");
                return true; // Fail-open on timeout
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ExecutionInterceptor: safety check failed, allowing execution (fail-open)");
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
                    default:
                        filtered.Add(w); // Unknown type — show it
                        break;
                }
            }

            return filtered.ToArray();
        }
    }
}
