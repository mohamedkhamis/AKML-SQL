#nullable enable
using System;
using System.ComponentModel.Design;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using Serilog;

namespace AkmlSql.Shell.Shared.Commands
{
    /// <summary>
    /// Executes only the SQL statement at the current cursor position (Alt+Enter).
    /// Sends a StatementBoundary request to the engine, selects the detected range
    /// in the editor, then invokes the host's built-in Execute command.
    /// </summary>
    internal sealed class ExecuteCurrentStatementCommand
    {
        private ExecuteCurrentStatementCommand(Package package, OleMenuCommandService commandService)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdExecuteCurrentStatement);
            var menuItem = new OleMenuCommand(Execute, cmdId);
            menuItem.BeforeQueryStatus += OnBeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        public static ExecuteCurrentStatementCommand? Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new ExecuteCurrentStatementCommand(package, commandService);
        }

        private void OnBeforeQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (sender is OleMenuCommand cmd)
            {
                var textManager = (IVsTextManager)Package.GetGlobalService(typeof(SVsTextManager));
                if (textManager == null) { cmd.Enabled = false; return; }
                textManager.GetActiveView(1, null, out var textView);
                cmd.Enabled = textView != null;
            }
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var textManager = (IVsTextManager)Package.GetGlobalService(typeof(SVsTextManager));
                if (textManager == null) return;

                textManager.GetActiveView(1, null, out var textView);
                if (textView == null) return;

                textView.GetBuffer(out var buffer);
                if (buffer == null) return;

                // Get cursor position
                textView.GetCaretPos(out var caretLine, out var caretCol);

                // Convert line/col to offset
                buffer.GetPositionOfLineIndex(caretLine, caretCol, out var cursorOffset);

                // Get full document text
                buffer.GetLastLineIndex(out var lastLine, out var lastCol);
                buffer.GetLineText(0, 0, lastLine, lastCol, out var documentText);

                if (string.IsNullOrEmpty(documentText)) return;

                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected)
                {
                    Log.Warning("ExecuteCurrentStatement: engine not available");
                    return;
                }

                // Capture references we need on the background thread callback
                var capturedBuffer = buffer;
                var capturedView = textView;

                var request = new StatementBoundaryRequest
                {
                    SessionId = Guid.NewGuid().ToString("N"),
                    SqlText = documentText,
                    CursorOffset = cursorOffset,
                    AllStatements = false
                };

                _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    try
                    {
                        var response = await client.SendRequestAsync<StatementBoundaryResponse, StatementBoundaryRequest>(
                            MessageTypes.StatementBoundary, request, timeoutMs: 5000);

                        if (!response.Success || response.CurrentStatement == null)
                        {
                            Log.Debug("ExecuteCurrentStatement: no statement found at offset {Offset}", cursorOffset);
                            return;
                        }

                        var stmt = response.CurrentStatement;

                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                        // Select the statement range in the editor
                        SelectRange(capturedBuffer, capturedView, stmt.StartOffset, stmt.EndOffset);

                        // Invoke the host's Execute command (works for both SSMS and VS)
                        var dte = (DTE)Package.GetGlobalService(typeof(DTE));
                        if (dte != null)
                        {
                            dte.ExecuteCommand("Query.Execute");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "ExecuteCurrentStatement: IPC request failed");
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ExecuteCurrentStatement command failed");
            }
        }

        /// <summary>
        /// Selects the text range between two character offsets in the editor buffer.
        /// </summary>
        internal static void SelectRange(IVsTextLines buffer, IVsTextView textView, int startOffset, int endOffset)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            buffer.GetLineIndexOfPosition(startOffset, out var startLine, out var startCol);
            buffer.GetLineIndexOfPosition(endOffset, out var endLine, out var endCol);

            textView.SetSelection(startLine, startCol, endLine, endCol);
        }
    }
}
