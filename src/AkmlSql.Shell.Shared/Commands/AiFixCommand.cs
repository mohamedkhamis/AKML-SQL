#nullable enable
using System;
using System.ComponentModel.Design;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ai;
using AkmlSql.Shell.Shared.Ipc;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Serilog;
using Task = System.Threading.Tasks.Task;

namespace AkmlSql.Shell.Shared.Commands
{
    /// <summary>
    /// AI Fix command: sends the current document SQL and a captured execution error to the AI engine,
    /// and shows the suggested fix in a <see cref="FixPreviewPanel"/> with Apply/Close options.
    /// Command ID: <see cref="CommandIds.CmdAiFix"/> (0x0702).
    /// </summary>
    internal sealed class AiFixCommand
    {
        private readonly AsyncPackage? _asyncPackage;
        private readonly Package? _syncPackage;

        /// <summary>
        /// Immutable snapshot of the last captured execution error.
        /// Updated atomically via <see cref="Interlocked.CompareExchange{T}"/>.
        /// </summary>
        private sealed record ErrorSnapshot(string FailingSql, string ErrorMessage, int ErrorNumber);
        private static ErrorSnapshot? _lastError;

        private AiFixCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _asyncPackage = package ?? throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdAiFix);
            var menuItem = new OleMenuCommand(Execute, cmdId);
            menuItem.BeforeQueryStatus += AiCommandVisibility.OnBeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        private AiFixCommand(Package package, OleMenuCommandService commandService)
        {
            _syncPackage = package ?? throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdAiFix);
            var menuItem = new OleMenuCommand(Execute, cmdId);
            menuItem.BeforeQueryStatus += AiCommandVisibility.OnBeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        public static AiFixCommand? Instance { get; private set; }

        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
            Instance = new AiFixCommand(package, commandService);
        }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new AiFixCommand(package, commandService);
        }

        /// <summary>
        /// Called from execution hooks when a SQL error occurs.
        /// Stores the error context so the AI Fix command can use it.
        /// When <see cref="AiSettings.AutoFixOnError"/> is true, also offers a fix notification.
        /// </summary>
        /// <param name="sql">The SQL that failed.</param>
        /// <param name="errorMessage">The SQL Server error message.</param>
        /// <param name="errorNumber">The SQL Server error number.</param>
        public static void OfferFixForError(string sql, string errorMessage, int errorNumber)
        {
            Interlocked.Exchange(ref _lastError, new ErrorSnapshot(sql, errorMessage, errorNumber));

            Log.Debug("AiFixCommand: captured error #{ErrorNumber}: {ErrorMessage}",
                errorNumber, errorMessage);

            // If auto-fix is enabled, show a non-modal notification via the status bar
            try
            {
                var settings = ConfigManager.Load();
                if (settings.Ai.Enabled && settings.Ai.AutoFixOnError)
                {
                    // Use status bar to offer the fix (non-modal approach)
                    ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        try
                        {
                            var statusBar = (IVsStatusbar?)Package.GetGlobalService(typeof(SVsStatusbar));
                            statusBar?.SetText($"SQL Error #{errorNumber} - Use AI Fix (Ctrl+Shift+F) to get a suggested fix");
                        }
                        catch
                        {
                            // Best-effort
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "AiFixCommand: failed to check AutoFixOnError setting");
            }
        }

        /// <summary>
        /// Clears the cached error context.
        /// </summary>
        public static void ClearErrorContext()
        {
            Interlocked.Exchange(ref _lastError, null);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await ExecuteAsync();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "AiFixCommand: failed");
                }
            });
        }

        private async Task ExecuteAsync()
        {
            var manager = EngineLifecycle.Manager;
            if (manager?.Client == null || !manager.Client.IsConnected)
            {
                Log.Warning("AiFixCommand: engine not connected");
                await ShowStatusBarMessageAsync("AI Fix: Engine not connected");
                return;
            }

            // Determine the SQL and error context
            string failingSql;
            string errorMessage;
            int errorNumber;

            var snapshot = Interlocked.CompareExchange(ref _lastError, null, null);
            if (snapshot != null)
            {
                // Use the captured error context
                failingSql = snapshot.FailingSql;
                errorMessage = snapshot.ErrorMessage;
                errorNumber = snapshot.ErrorNumber;
            }
            else
            {
                // No error context available - try to get SQL from editor and prompt for error
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var (sql, _) = AiExplainCommand.GetSelectedOrCurrentStatement();

                if (string.IsNullOrWhiteSpace(sql))
                {
                    await ShowStatusBarMessageAsync(
                        "AI Fix: No SQL error to fix. Run a query first, or select the failing SQL.");
                    return;
                }

                // Without error context, we can still try to fix but need at least a generic message
                failingSql = sql;
                errorMessage = "(No specific error captured - please run the query to capture the error)";
                errorNumber = 0;

                await ShowStatusBarMessageAsync(
                    "AI Fix: No error captured. Run the query first for better results.");
                return;
            }

            var sessionId = Guid.NewGuid().ToString("N");

            await ShowStatusBarMessageAsync("AI Fix: Analyzing error...");

            try
            {
                var request = new AiFixRequest
                {
                    SessionId = sessionId,
                    FailingSql = failingSql,
                    ErrorMessage = errorMessage,
                    ErrorNumber = errorNumber
                };

                var response = await manager.Client.SendRequestAsync<AiFixResponse, AiFixRequest>(
                    MessageTypes.AiFix, request, timeoutMs: 60000);

                if (!response.Success)
                {
                    Log.Information("AiFixCommand: AI returned error - {Error}", response.ErrorMessage);
                    await ShowStatusBarMessageAsync($"AI Fix: {response.ErrorMessage}");
                    return;
                }

                // Show the fix preview on the UI thread
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                var panel = new FixPreviewPanel(
                    failingSql,
                    response.FixedSql ?? string.Empty,
                    response.Explanation,
                    response.Annotations,
                    response.LatencyMs,
                    response.TokensUsed);

                panel.ShowDialog();

                if (panel.ApplyFix && !string.IsNullOrEmpty(panel.FixedSql))
                {
                    // Apply the fix by replacing the text in the active editor
                    ApplyFixToEditor(failingSql, panel.FixedSql);
                    await ShowStatusBarMessageAsync(
                        $"AI Fix: Applied ({response.LatencyMs}ms, {response.TokensUsed} tokens)");

                    // Clear the error context after applying
                    ClearErrorContext();
                }
                else
                {
                    await ShowStatusBarMessageAsync("AI Fix: Cancelled");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AiFixCommand: IPC request failed");
                await ShowStatusBarMessageAsync($"AI Fix: {ex.Message}");
            }
        }

        /// <summary>
        /// Applies the fixed SQL to the active editor, replacing the failing SQL.
        /// </summary>
        private static void ApplyFixToEditor(string originalSql, string fixedSql)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var dte = (DTE2)Package.GetGlobalService(typeof(DTE));
                if (dte?.ActiveDocument == null)
                    return;

                var textDocument = dte.ActiveDocument.Object("TextDocument") as TextDocument;
                if (textDocument == null)
                    return;

                var selection = textDocument.Selection;

                // If there's a selection that matches the original SQL, replace it
                if (!string.IsNullOrEmpty(selection.Text))
                {
                    selection.Insert(fixedSql, (int)vsInsertFlags.vsInsertFlagsContainNewText);
                    return;
                }

                // Otherwise, try to find and replace the original SQL in the document
                var editPoint = textDocument.StartPoint.CreateEditPoint();
                var fullText = editPoint.GetText(textDocument.EndPoint);

                var idx = fullText.IndexOf(originalSql, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    // Select the matching text and replace it
                    editPoint.MoveToAbsoluteOffset(idx + 1); // 1-based
                    var endPoint = editPoint.CreateEditPoint();
                    endPoint.MoveToAbsoluteOffset(idx + 1 + originalSql.Length);
                    editPoint.ReplaceText(endPoint, fixedSql, (int)vsEPReplaceTextOptions.vsEPReplaceTextAutoformat);
                }
                else
                {
                    // Fallback: replace entire document content
                    Log.Debug("AiFixCommand: original SQL not found in document, replacing at cursor");
                    selection.SelectAll();
                    selection.Insert(fixedSql, (int)vsInsertFlags.vsInsertFlagsContainNewText);
                }

                Log.Information("AiFixCommand: fix applied to editor");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AiFixCommand: failed to apply fix to editor");
            }
        }

        private static async Task ShowStatusBarMessageAsync(string message)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            try
            {
                var statusBar = (IVsStatusbar?)Package.GetGlobalService(typeof(SVsStatusbar));
                statusBar?.SetText(message);
            }
            catch
            {
                // Best-effort status bar update
            }
        }
    }
}
