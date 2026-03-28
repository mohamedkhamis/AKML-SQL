#nullable enable
using System;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Models.History;
using AkmlSql.Shell.Shared.Ipc;
using Microsoft.VisualStudio.Shell;
using Serilog;
using Task = System.Threading.Tasks.Task;

namespace AkmlSql.Shell.Shared.History
{
    /// <summary>
    /// Captures SQL execution events from SSMS/VS and sends history recording requests
    /// to the out-of-process engine via fire-and-forget IPC notifications.
    /// </summary>
    internal static class ExecutionCapture
    {
        private static bool _initialized;
        private static bool _enabled;

        /// <summary>
        /// Initializes execution capture. Reads configuration to determine if history
        /// recording is enabled, and hooks into SSMS query execution completion events.
        /// </summary>
        /// <param name="package">The VS/SSMS package for service resolution.</param>
        public static void Initialize(Package package)
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                var settings = ConfigManager.Load();
                _enabled = settings.History.Enabled;

                if (!_enabled)
                {
                    Log.Information("ExecutionCapture: history recording is disabled by configuration");
                    return;
                }

                // TODO: Hook into SSMS query execution completion events.
                //
                // The exact hookup varies between SSMS 20/21/22 and VS 2019/2022/2026.
                // In SSMS, the execution event is typically exposed via:
                //   - ScriptFactory.Instance for SSMS 20 (IsolatedShell)
                //   - SSMS 21/22 may expose events through IVsQueryExecution or similar COM interop
                //
                // The general approach is:
                //   1. Get the ScriptFactory or query execution service from the package
                //   2. Subscribe to the QueryExecutionCompleted event
                //   3. In the event handler, call OnExecutionCompleted() with the captured data
                //
                // For now, the OnExecutionCompleted method below provides the capture logic
                // that any execution event handler should call.
                //
                // Example (pseudo-code for SSMS 22):
                //   var scriptFactory = package.GetService(typeof(IScriptFactory)) as IScriptFactory;
                //   scriptFactory.QueryExecutionCompleted += (sender, args) => {
                //       OnExecutionCompleted(
                //           sqlText: args.SqlText,
                //           server: args.ServerName,
                //           database: args.DatabaseName,
                //           username: args.UserName,
                //           durationMs: (long)args.Duration.TotalMilliseconds,
                //           rowCount: args.RowCount,
                //           status: args.Succeeded ? ExecutionStatus.Success : ExecutionStatus.Error,
                //           errorMessage: args.ErrorMessage,
                //           source: args.FilePath,
                //           tabTitle: args.TabTitle);
                //   };

                Log.Information("ExecutionCapture: initialized and ready for SSMS execution event hookup");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ExecutionCapture: failed to initialize");
            }
        }

        /// <summary>
        /// Called when a SQL execution completes. Sends a fire-and-forget HistoryRecord
        /// notification to the engine process. This method is safe to call from any thread
        /// and will not block the query execution flow.
        /// </summary>
        /// <param name="sqlText">The SQL text that was executed.</param>
        /// <param name="server">Server name.</param>
        /// <param name="database">Database name.</param>
        /// <param name="username">Login/username.</param>
        /// <param name="durationMs">Execution duration in milliseconds.</param>
        /// <param name="rowCount">Number of rows affected/returned.</param>
        /// <param name="status">Outcome of the execution.</param>
        /// <param name="errorMessage">Error message if execution failed.</param>
        /// <param name="source">Source file path or identifier.</param>
        /// <param name="tabTitle">Title of the editor tab/window.</param>
        public static void OnExecutionCompleted(
            string sqlText,
            string? server,
            string? database,
            string? username,
            long durationMs,
            long rowCount,
            ExecutionStatus status,
            string? errorMessage,
            string? source,
            string? tabTitle)
        {
            if (!_enabled) return;

            // Skip recording failed executions if configured
            if (status != ExecutionStatus.Success)
            {
                try
                {
                    var settings = ConfigManager.Load();
                    if (!settings.History.RecordFailures)
                    {
                        Log.Debug("ExecutionCapture: skipping failed execution (recordFailures=false)");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "ExecutionCapture: failed to check recordFailures setting");
                }
            }

            // Skip empty queries
            if (string.IsNullOrWhiteSpace(sqlText)) return;

            // Fire-and-forget: send to engine via IPC notification (RequestId=0)
            _ = Task.Run(async () =>
            {
                try
                {
                    var client = EngineLifecycle.Manager?.Client;
                    if (client == null || !client.IsConnected)
                    {
                        Log.Debug("ExecutionCapture: engine not connected, skipping history record");
                        return;
                    }

                    // Truncate at shell side to avoid sending massive payloads over IPC
                    const int maxIpcChars = 1_048_576; // 1 MB
                    var truncated = false;
                    if (sqlText.Length > maxIpcChars)
                    {
                        sqlText = sqlText.Substring(0, maxIpcChars);
                        truncated = true;
                    }

                    var request = new HistoryRecordRequest
                    {
                        SqlText = sqlText,
                        Truncated = truncated,
                        Server = server,
                        Database = database,
                        Username = username,
                        DurationMs = durationMs,
                        RowCount = rowCount,
                        Status = (int)status,
                        ErrorMessage = errorMessage,
                        Source = source,
                        TabTitle = tabTitle
                    };

                    // Send as notification (RequestId=0) to avoid blocking query execution
                    await client.SendNotificationAsync(MessageTypes.HistoryRecord, request);

                    Log.Debug("ExecutionCapture: history record sent to engine (server={Server}, db={Database})",
                        server, database);
                }
                catch (Exception ex)
                {
                    // Never let history recording failures bubble up to the user
                    Log.Warning(ex, "ExecutionCapture: failed to send history record to engine");
                }
            });
        }
    }
}
