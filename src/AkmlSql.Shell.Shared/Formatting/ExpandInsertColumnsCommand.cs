using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using Serilog;

namespace AkmlSql.Shell.Shared.Formatting
{
    /// <summary>
    /// Expands INSERT statements without a column list to include explicit column names.
    /// </summary>
    internal static class ExpandInsertColumnsCommand
    {
        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdExpandInsertColumns);
            commandService.AddCommand(new MenuCommand(Execute, cmdId));
        }

        private static void Execute(object sender, EventArgs e)
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

                buffer.GetLastLineIndex(out var lastLine, out var lastCol);
                buffer.GetLineText(0, 0, lastLine, lastCol, out var documentText);
                if (string.IsNullOrEmpty(documentText)) return;

                // Get selection bounds
                textView.GetSelection(out var startLine, out var startCol, out var endLine, out var endCol);
                buffer.GetPositionOfLineIndex(startLine, startCol, out var selStart);
                buffer.GetPositionOfLineIndex(endLine, endCol, out var selEnd);
                var selLength = Math.Max(0, selEnd - selStart);

                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected) { Log.Warning("Engine not available"); return; }

                var request = new FormatActionRequest
                {
                    SessionId       = Guid.NewGuid().ToString("N"),
                    Text            = documentText,
                    ActionType      = (int)FormatActionType.ExpandInsertColumns,
                    SelectionStart  = selLength > 0 ? selStart : 0,
                    SelectionLength = selLength
                };

                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        var response = await client.SendRequestAsync<FormatActionResponse, FormatActionRequest>(
                            MessageTypes.FormatAction, request, timeoutMs: 10000);

                        if (response.Success && response.WasModified)
                        {
                            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                            FormatActionHelper.ApplyFormattedText(buffer, response.FormattedText);
                        }

                        if (response.Warnings != null && response.Warnings.Length > 0)
                        {
                            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                            VsShellUtilities.ShowMessageBox(
                                null,
                                string.Join("\n", response.Warnings),
                                "AKML SQL \u2013 Expand INSERT Columns",
                                OLEMSGICON.OLEMSGICON_INFO,
                                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                        }
                    }
                    catch (Exception ex) { Log.Error(ex, "ExpandInsertColumns failed"); }
                });
            }
            catch (Exception ex) { Log.Error(ex, "ExpandInsertColumns command failed"); }
        }
    }
}
