#nullable enable
using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using Serilog;

namespace AkmlSql.Shell.Shared.Commands
{
    /// <summary>
    /// Navigate to the next or previous SQL statement boundary.
    /// CmdNavigateNextStatement (Ctrl+PageDown): move caret to the start of the next statement.
    /// CmdNavigatePrevStatement (Ctrl+PageUp): move caret to the start of the previous statement.
    /// </summary>
    internal sealed class NavigateStatementCommand
    {
        private readonly bool _forward;

        private NavigateStatementCommand(Package package, OleMenuCommandService commandService, int commandId, bool forward)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            _forward = forward;

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, commandId);
            var menuItem = new OleMenuCommand(Execute, cmdId);
            menuItem.BeforeQueryStatus += OnBeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        public static NavigateStatementCommand? NextInstance { get; private set; }
        public static NavigateStatementCommand? PrevInstance { get; private set; }

        /// <summary>
        /// Registers both the Next and Previous statement navigation commands.
        /// </summary>
        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            NextInstance = new NavigateStatementCommand(
                package, commandService, CommandIds.CmdNavigateNextStatement, forward: true);
            PrevInstance = new NavigateStatementCommand(
                package, commandService, CommandIds.CmdNavigatePrevStatement, forward: false);
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

                textView.GetCaretPos(out var caretLine, out var caretCol);
                buffer.GetPositionOfLineIndex(caretLine, caretCol, out var cursorOffset);

                buffer.GetLastLineIndex(out var lastLine, out var lastCol);
                buffer.GetLineText(0, 0, lastLine, lastCol, out var documentText);

                if (string.IsNullOrEmpty(documentText)) return;

                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected)
                {
                    Log.Warning("NavigateStatement: engine not available");
                    return;
                }

                var capturedView = textView;
                var capturedBuffer = buffer;
                var forward = _forward;

                var request = new StatementBoundaryRequest
                {
                    SessionId = Guid.NewGuid().ToString("N"),
                    SqlText = documentText,
                    CursorOffset = cursorOffset,
                    AllStatements = true
                };

                _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    try
                    {
                        var response = await client.SendRequestAsync<StatementBoundaryResponse, StatementBoundaryRequest>(
                            MessageTypes.StatementBoundary, request, timeoutMs: 5000);

                        if (!response.Success || response.AllStatements == null || response.AllStatements.Length == 0)
                        {
                            Log.Debug("NavigateStatement: no statements found");
                            return;
                        }

                        var statements = response.AllStatements;
                        int targetOffset = -1;

                        if (forward)
                        {
                            // Find the first statement that starts after the current cursor
                            foreach (var stmt in statements)
                            {
                                if (stmt.StartOffset > cursorOffset)
                                {
                                    targetOffset = stmt.StartOffset;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            // Find the last statement that starts before the current cursor
                            for (int i = statements.Length - 1; i >= 0; i--)
                            {
                                if (statements[i].StartOffset < cursorOffset)
                                {
                                    targetOffset = statements[i].StartOffset;
                                    break;
                                }
                            }
                        }

                        if (targetOffset < 0) return;

                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                        capturedBuffer.GetLineIndexOfPosition(targetOffset, out var line, out var col);
                        capturedView.SetCaretPos(line, col);
                        capturedView.CenterLines(line, 1);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "NavigateStatement: IPC request failed");
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "NavigateStatement command failed");
            }
        }
    }
}
