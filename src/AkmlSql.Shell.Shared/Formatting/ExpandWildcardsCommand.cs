using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using AkmlSql.Shell.Shared.Refactoring;
using Serilog;

namespace AkmlSql.Shell.Shared.Formatting
{
    /// <summary>
    /// Expands SELECT * wildcards into explicit column lists.
    /// </summary>
    internal static class ExpandWildcardsCommand
    {
        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdExpandWildcards);
            var menuItem = new MenuCommand(Execute, cmdId);
            commandService.AddCommand(menuItem);
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

                var manager = EngineLifecycle.Manager;
                var client = manager?.Client;

                if (client == null || !client.IsConnected)
                {
                    Log.Warning("ExpandWildcards: engine not available");
                    return;
                }

                var editorCtx = RefactorCommandHelper.TryGetActiveEditor();
                var sessionId = editorCtx?.SessionId ?? Guid.NewGuid().ToString("N");

                var request = new FormatActionRequest
                {
                    SessionId       = sessionId,
                    Text            = documentText,
                    ActionType      = (int)FormatActionType.ExpandWildcards,
                    SelectionStart  = editorCtx != null && editorCtx.SelectionLength > 0 ? editorCtx.SelectionStart  : 0,
                    SelectionLength = editorCtx != null && editorCtx.SelectionLength > 0 ? editorCtx.SelectionLength : 0
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
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "ExpandWildcards via engine failed");
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Expand wildcards command failed");
            }
        }
    }
}
