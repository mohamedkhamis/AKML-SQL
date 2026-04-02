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

                // Hook "Query.Execute" (F5) via CommandEvents
                // The empty GUID and 0 ID means "all commands" — we filter by name in the handler.
                // Using specific command lookup avoids capturing unrelated commands.
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

                // Resolve the command name from GUID + ID
                if (_dte == null) return;

                string? commandName = null;
                try
                {
                    var command = _dte.Commands.Item(guid, id);
                    commandName = command?.Name;
                }
                catch
                {
                    // Command lookup can fail for unknown GUIDs — ignore
                    return;
                }

                if (commandName == null) return;

                // Only intercept query execution commands
                if (!commandName.Equals(QueryExecuteCommandName, StringComparison.OrdinalIgnoreCase))
                    return;

                Log.Debug("ExecutionCommandFilter: intercepted {Command}", commandName);

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
