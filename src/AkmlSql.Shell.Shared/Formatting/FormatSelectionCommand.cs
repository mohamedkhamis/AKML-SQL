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
    internal static class FormatSelectionCommand
    {
        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdFormatSelection);
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

                textView.GetSelection(out var startLine, out var startCol, out var endLine, out var endCol);

                textView.GetBuffer(out var buffer);
                if (buffer == null) return;

                buffer.GetPositionOfLineIndex(startLine, startCol, out var startOffset);
                buffer.GetPositionOfLineIndex(endLine, endCol, out var endOffset);

                if (startOffset == endOffset) return;

                buffer.GetLastLineIndex(out var lastLine, out var lastCol);
                buffer.GetLineText(0, 0, lastLine, lastCol, out var fullText);
                if (string.IsNullOrEmpty(fullText)) return;

                var manager = EngineLifecycle.Manager;
                var client = manager?.Client;

                if (client == null || !client.IsConnected)
                {
                    Log.Warning("Format selection: engine not available");
                    return;
                }

                var request = new FormatSelectionRequest
                {
                    SessionId = Guid.NewGuid().ToString("N"),
                    Text = fullText,
                    SelectionStart = startOffset,
                    SelectionEnd = endOffset
                };

                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        var response = await client.SendRequestAsync<FormatSelectionResponse, FormatSelectionRequest>(
                            MessageTypes.FormatSelection, request, timeoutMs: 10000);

                        if (response.Success && response.WasModified)
                        {
                            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                            FormatActionHelper.ApplyFormattedText(buffer, response.FormattedText);
                        }
                        else
                        {
                            // FR-005: original preserved. FormatSelectionResponse carries no
                            // Diagnostics, so the notice uses the generic preserve message.
                            await FormatFailureNotifier.NotifyIfPreservedAsync(
                                response.Success, response.ValidationPassed, diagnostics: null);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Format selection via engine failed");
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Format selection command failed");
            }
        }
    }
}
