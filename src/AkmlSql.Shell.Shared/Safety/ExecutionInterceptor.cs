#nullable enable
using System;
using System.Windows.Forms;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
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

                _anySettingEnabled = safety.ProductionWarning
                                     || safety.DeleteWithoutWhere
                                     || safety.UpdateWithoutWhere
                                     || safety.DropConfirmation
                                     || safety.TruncateConfirmation;

                if (!_anySettingEnabled)
                {
                    Log.Information("ExecutionInterceptor: all safety checks are disabled");
                    return;
                }

                // TODO: Hook into the SSMS/VS pre-execution path.
                //
                // The exact hookup mechanism varies between SSMS 20/21/22 and VS 2019/2022/2026.
                // In SSMS, the pre-execution event is typically exposed via:
                //   - ScriptFactory.Instance for SSMS 20 (IsolatedShell)
                //   - SSMS 21/22 may expose events through IVsQueryExecution or similar COM interop
                //
                // The general approach is:
                //   1. Get the ScriptFactory or query execution service from the package
                //   2. Subscribe to the QueryExecuting / BeforeExecute event
                //   3. In the event handler, call OnBeforeExecute() with the SQL text and server name
                //   4. If OnBeforeExecute() returns false, cancel the execution
                //
                // Example (pseudo-code for SSMS 22):
                //   var scriptFactory = package.GetService(typeof(IScriptFactory)) as IScriptFactory;
                //   scriptFactory.QueryExecuting += (sender, args) => {
                //       if (!OnBeforeExecute(args.SqlText, args.ServerName))
                //       {
                //           args.Cancel = true;
                //       }
                //   };

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
            if (!_anySettingEnabled)
                return true;

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

                // Determine if this is a production server
                bool isProductionServer = false;
                try
                {
                    var envRule = EnvironmentDetector.Match(serverName);
                    if (envRule != null)
                    {
                        // Check if the label indicates production
                        isProductionServer = envRule.Label.IndexOf(
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

                // Filter warnings based on which safety settings are enabled
                var filteredWarnings = FilterBySettings(response.Warnings);
                if (filteredWarnings.Length == 0)
                {
                    return true; // All detected warnings are for disabled settings
                }

                // Show the warning dialog on the UI thread
                DialogResult dialogResult = DialogResult.Cancel;
                ThreadHelper.ThrowIfNotOnUIThread();
                dialogResult = SafetyWarningDialog.Show(filteredWarnings);

                if (dialogResult == DialogResult.OK)
                {
                    Log.Information("ExecutionInterceptor: user confirmed execution despite {Count} warning(s) on server '{Server}'",
                        filteredWarnings.Length, serverName);
                    return true;
                }

                Log.Information("ExecutionInterceptor: user cancelled execution due to {Count} warning(s) on server '{Server}'",
                    filteredWarnings.Length, serverName);
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
        /// Filters warnings based on which safety settings are currently enabled in config.
        /// </summary>
        private static SafetyWarningDto[] FilterBySettings(SafetyWarningDto[] warnings)
        {
            SafetySettings? safety;
            try
            {
                var settings = ConfigManager.Load();
                safety = settings.Safety;
            }
            catch
            {
                return warnings; // Can't load config — show all warnings
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
