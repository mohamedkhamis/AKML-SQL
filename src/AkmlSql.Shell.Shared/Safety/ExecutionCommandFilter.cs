#nullable enable
using System;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Serilog;

namespace AkmlSql.Shell.Shared.Safety
{
    /// <summary>
    /// Hooks into the DTE command pipeline to intercept "Query.Execute" (F5) and
    /// "Query.Parse" before SSMS/VS processes them. When a query execution is detected,
    /// extracts the SQL text from the active editor and the server name from the window
    /// caption, then delegates to <see cref="ExecutionInterceptor.OnBeforeExecute"/>.
    /// <para>
    /// Uses <see cref="DTE.Events.CommandEvents"/> which works across all SSMS 20/21/22
    /// and VS 2019/2022/2026 versions without version-specific COM interop.
    /// </para>
    /// </summary>
    internal static class ExecutionCommandFilter
    {
        private static bool _installed;
        private static CommandEvents? _executeEvents;
        private static DTE? _dte;

        // DTE command GUIDs and IDs for SQL execution
        // "Query.Execute" in SSMS uses the DataDude command set
        // We hook via CommandEvents which matches by command name (more portable)
        private const string QueryExecuteCommandName = "Query.Execute";

        // Cached (Guid, Id) of Query.Execute, resolved by name during Install() so we
        // can compare them in the hot handler path without a per-command COM lookup
        // (Commands.Item(guid, id) throws COMException in SSMS 22 for this command).
        //
        // GUID alone is NOT sufficient: SSMS's database-tools command set
        // {52692960-56BC-4989-B5D3-94C47A513E8D} contains many distinct commands
        // (Query.Parse, Query.CancelExecuting, Query.IntelliSense*, ConnectionConnect,
        // etc.) that all share that GUID and only differ by Id. Matching by GUID
        // alone fires the safety check on typing/IntelliSense/connection events,
        // not just F5. Verified in field logs: ENTER OnBeforeExecute fires every
        // few hundred ms while the user types, before any deliberate F5 press.
        private static string? _queryExecuteGuid;
        private static int _queryExecuteId = -1;

        /// <summary>
        /// Installs the DTE command event hooks. Called from
        /// <see cref="ExecutionInterceptor.Initialize"/> after verifying at least
        /// one safety setting is enabled.
        /// </summary>
        public static void Install(Package package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_installed) return;
            _installed = true;

            try
            {
                _dte = Package.GetGlobalService(typeof(DTE)) as DTE;
                if (_dte == null)
                {
                    Log.Warning("ExecutionCommandFilter: DTE service not available");
                    return;
                }

                // Cache Query.Execute (Guid, Id) by name — Commands.Item(string) is reliable
                // where Commands.Item(guid, id) can throw COMException in SSMS 22.
                try
                {
                    var cmd = _dte.Commands.Item(QueryExecuteCommandName);
                    if (cmd != null)
                    {
                        _queryExecuteGuid = cmd.Guid;
                        _queryExecuteId = cmd.ID;
                    }
                    Log.Debug("ExecutionCommandFilter: cached Query.Execute Guid={Guid} Id={Id}",
                        _queryExecuteGuid, _queryExecuteId);
                }
                catch
                {
                    // Caching failed — handler will fall back to per-command name resolution
                    Log.Debug("ExecutionCommandFilter: could not cache Query.Execute (Guid, Id); will resolve per-command");
                }

                // Hook all commands — filter to Query.Execute in the handler
                _executeEvents = _dte.Events.CommandEvents;
                _executeEvents.BeforeExecute += OnBeforeCommandExecute;

                Log.Information("ExecutionCommandFilter: installed DTE command hook for Query.Execute");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ExecutionCommandFilter: failed to install command hook");
            }
        }

        private static void OnBeforeCommandExecute(
            string guid, int id, object customIn, object customOut, ref bool cancelDefault)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                if (_dte == null) return;

                // Fast path: compare cached (Guid, Id) — both are required because
                // SSMS's database-tools command set GUID is shared across many
                // commands (Parse, IntelliSense, ConnectionConnect, etc.) that
                // differ only by Id.
                if (_queryExecuteGuid != null && _queryExecuteId >= 0)
                {
                    if (id != _queryExecuteId ||
                        !string.Equals(guid, _queryExecuteGuid, StringComparison.OrdinalIgnoreCase))
                        return;
                }
                else
                {
                    // Slow fallback: resolve command name from GUID + ID
                    string? commandName = null;
                    try
                    {
                        var command = _dte.Commands.Item(guid, id);
                        commandName = command?.Name;
                    }
                    catch
                    {
                        // Commands.Item(guid, id) can throw COMException in SSMS 22 —
                        // skip (don't return on unknown commands; just not Query.Execute)
                        return;
                    }

                    if (commandName == null) return;
                    if (!commandName.Equals(QueryExecuteCommandName, StringComparison.OrdinalIgnoreCase))
                        return;
                }

                Log.Debug("ExecutionCommandFilter: intercepted {Command}", QueryExecuteCommandName);

                // Extract SQL text from the active editor buffer
                var sqlText = GetActiveSqlText();
                if (string.IsNullOrWhiteSpace(sqlText))
                    return; // Empty editor — nothing to check

                // Extract server name from the window caption
                var serverName = GetActiveServerName();

                // Delegate to the execution interceptor
                if (!ExecutionInterceptor.OnBeforeExecute(sqlText, serverName))
                {
                    // User cancelled — suppress the execution command
                    cancelDefault = true;
                    Log.Debug("ExecutionCommandFilter: execution cancelled by user");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ExecutionCommandFilter: error in BeforeExecute hook (allowing execution)");
                // Fail-open: allow execution if hook errors
            }
        }

        /// <summary>
        /// Gets the full SQL text from the active document editor.
        /// </summary>
        private static string? GetActiveSqlText()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                if (_dte?.ActiveDocument == null) return null;

                var textDoc = _dte.ActiveDocument.Object("TextDocument") as TextDocument;
                if (textDoc == null) return null;

                // If there's a selection, check the selection; otherwise check the full document
                var selection = textDoc.Selection;
                if (selection != null && !selection.IsEmpty)
                {
                    return selection.Text;
                }

                // Full document text
                var startPoint = textDoc.StartPoint.CreateEditPoint();
                return startPoint.GetText(textDoc.EndPoint);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "ExecutionCommandFilter: failed to get SQL text");
                return null;
            }
        }

        /// <summary>
        /// Gets the server name from the active document's window caption.
        /// Uses the same caption-parsing approach as <see cref="Editor.SsmsConnectionDetector"/>.
        /// </summary>
        private static string? GetActiveServerName()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                if (_dte?.ActiveDocument?.ActiveWindow == null) return null;

                var sp = ServiceProvider.GlobalProvider as IServiceProvider;
                if (sp == null) return null;

                var connectionResult = Editor.SsmsConnectionDetector.TryDetectConnection(sp);
                return connectionResult?.Server;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "ExecutionCommandFilter: failed to detect server name");
                return null;
            }
        }
    }
}
