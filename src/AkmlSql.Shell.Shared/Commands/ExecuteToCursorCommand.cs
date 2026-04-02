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
    /// Executes SQL from the beginning of the document up to and including the statement
    /// containing the cursor (Ctrl+Shift+Enter).
    /// Sends a StatementBoundary request, selects from offset 0 to the end of the
    /// current statement, then invokes the host's built-in Execute command.
    /// </summary>
    internal sealed class ExecuteToCursorCommand
    {
        private ExecuteToCursorCommand(Package package, OleMenuCommandService commandService)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdExecuteToCursor);
            var menuItem = new OleMenuCommand(Execute, cmdId);
            menuItem.BeforeQueryStatus += OnBeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        public static ExecuteToCursorCommand? Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new ExecuteToCursorCommand(package, commandService);
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
                buffer.GetPositionOfLineIndex(caretLine, caretCol, out var cursorOffset);

                // Get full document text
                buffer.GetLastLineIndex(out var lastLine, out var lastCol);
                buffer.GetLineText(0, 0, lastLine, lastCol, out var documentText);

                if (string.IsNullOrEmpty(documentText)) return;

                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected)
                {
                    Log.Warning("ExecuteToCursor: engine not available");
                    return;
                }

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
                            Log.Debug("ExecuteToCursor: no statement found at offset {Offset}", cursorOffset);
                            return;
                        }

                        var endOffset = response.CurrentStatement.EndOffset;

                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                        // Select from document start (offset 0) to the end of the current statement
                        ExecuteCurrentStatementCommand.SelectRange(capturedBuffer, capturedView, 0, endOffset);

                        // Invoke Execute
                        var dte = (DTE)Package.GetGlobalService(typeof(DTE));
                        if (dte != null)
                        {
                            dte.ExecuteCommand("Query.Execute");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "ExecuteToCursor: IPC request failed");
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ExecuteToCursor command failed");
            }
        }
    }
}
