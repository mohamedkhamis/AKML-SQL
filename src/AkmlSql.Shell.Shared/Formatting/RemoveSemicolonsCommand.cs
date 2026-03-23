using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using Serilog;

namespace AkmlSql.Shell.Shared.Formatting
{
    /// <summary>
    /// Removes semicolons from each statement in the active SQL document.
    /// </summary>
    internal static class RemoveSemicolonsCommand
    {
        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdRemoveSemicolons);
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

                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected) { Log.Warning("Engine not available"); return; }

                var request = new FormatActionRequest
                {
                    SessionId       = Guid.NewGuid().ToString("N"),
                    Text            = documentText,
                    ActionType      = (int)FormatActionType.RemoveSemicolons
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
                    catch (Exception ex) { Log.Error(ex, "RemoveSemicolons failed"); }
                });
            }
            catch (Exception ex) { Log.Error(ex, "Remove semicolons command failed"); }
        }
    }
}
