#nullable enable
using System;
using System.ComponentModel.Design;
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
    /// AI Explain command: sends the selected SQL (or current statement) to the AI engine
    /// for explanation, and shows the structured result in an <see cref="ExplanationPanel"/>.
    /// Command ID: <see cref="CommandIds.CmdAiExplain"/> (0x0701).
    /// </summary>
    internal sealed class AiExplainCommand
    {
        private readonly AsyncPackage? _asyncPackage;
        private readonly Package? _syncPackage;

        private AiExplainCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _asyncPackage = package ?? throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdAiExplain);
            var menuItem = new OleMenuCommand(Execute, cmdId);
            menuItem.BeforeQueryStatus += AiCommandVisibility.OnBeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        private AiExplainCommand(Package package, OleMenuCommandService commandService)
        {
            _syncPackage = package ?? throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdAiExplain);
            var menuItem = new OleMenuCommand(Execute, cmdId);
            menuItem.BeforeQueryStatus += AiCommandVisibility.OnBeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        public static AiExplainCommand? Instance { get; private set; }

        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
            Instance = new AiExplainCommand(package, commandService);
        }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new AiExplainCommand(package, commandService);
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
                    Log.Error(ex, "AiExplainCommand: failed");
                }
            });
        }

        private async Task ExecuteAsync()
        {
            var manager = EngineLifecycle.Manager;
            if (manager?.Client == null || !manager.Client.IsConnected)
            {
                Log.Warning("AiExplainCommand: engine not connected");
                await ShowStatusBarMessageAsync("AI Explain: Engine not connected");
                return;
            }

            // Get selected text or current statement from the active editor
            string selectedSql = string.Empty;
            string sessionId = string.Empty;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var (sql, session) = GetSelectedOrCurrentStatement();
                selectedSql = sql;
                sessionId = session;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AiExplainCommand: failed to get SQL text");
                return;
            }

            if (string.IsNullOrWhiteSpace(selectedSql))
            {
                await ShowStatusBarMessageAsync("AI Explain: No SQL text selected");
                return;
            }

            await ShowStatusBarMessageAsync("AI Explain: Analyzing...");

            try
            {
                var request = new AiExplainRequest
                {
                    SessionId = sessionId,
                    SelectedSql = selectedSql
                };

                var response = await manager.Client.SendRequestAsync<AiExplainResponse, AiExplainRequest>(
                    MessageTypes.AiExplain, request, timeoutMs: 60000);

                if (!response.Success)
                {
                    Log.Information("AiExplainCommand: AI returned error - {Error}", response.ErrorMessage);
                    await ShowStatusBarMessageAsync($"AI Explain: {response.ErrorMessage}");
                    return;
                }

                // Show the explanation panel on the UI thread
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                var panel = new ExplanationPanel(
                    response.Purpose,
                    response.StepByStep,
                    response.KeyDetails,
                    response.Suggestions,
                    response.LatencyMs,
                    response.TokensUsed);

                panel.ShowDialog();

                await ShowStatusBarMessageAsync(
                    $"AI Explain: Done ({response.LatencyMs}ms, {response.TokensUsed} tokens)");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AiExplainCommand: IPC request failed");
                await ShowStatusBarMessageAsync($"AI Explain: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the selected text from the active editor. If no selection, gets the current line
        /// or paragraph (contiguous non-empty lines around the cursor).
        /// Returns (sqlText, sessionId).
        /// </summary>
        internal static (string SqlText, string SessionId) GetSelectedOrCurrentStatement()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var dte = (DTE2)Package.GetGlobalService(typeof(DTE));
            if (dte?.ActiveDocument == null)
                return (string.Empty, string.Empty);

            var textDocument = dte.ActiveDocument.Object("TextDocument") as TextDocument;
            if (textDocument == null)
                return (string.Empty, string.Empty);

            var sessionId = Guid.NewGuid().ToString("N");

            // Check if there is a selection
            var selection = textDocument.Selection;
            if (!string.IsNullOrWhiteSpace(selection.Text))
            {
                return (selection.Text, sessionId);
            }

            // No selection: get the current paragraph (contiguous non-empty lines)
            var editPoint = selection.ActivePoint.CreateEditPoint();
            var currentLine = editPoint.Line;
            var startLine = currentLine;
            var endLine = currentLine;

            // Find start of paragraph (walk up to first empty line)
            while (startLine > 1)
            {
                var lineText = editPoint.GetLines(startLine - 1, startLine);
                if (string.IsNullOrWhiteSpace(lineText))
                    break;
                startLine--;
            }

            // Find end of paragraph (walk down to first empty line)
            var totalLines = textDocument.EndPoint.Line;
            while (endLine < totalLines)
            {
                var lineText = editPoint.GetLines(endLine, endLine + 1);
                if (string.IsNullOrWhiteSpace(lineText))
                    break;
                endLine++;
            }

            var paragraphText = editPoint.GetLines(startLine, endLine + 1);
            return (paragraphText.Trim(), sessionId);
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
