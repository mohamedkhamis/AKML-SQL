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
    /// Removes all non-essential whitespace and formatting from the active SQL document
    /// (or selection), compressing it into a minimal single-line form.
    /// Sends a <see cref="FormatActionRequest"/> with <see cref="FormatActionType.Unformat"/>
    /// to the engine via named-pipe RPC.
    /// </summary>
    internal sealed class UnformatCommand
    {
        private UnformatCommand(Package package, OleMenuCommandService commandService)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdUnformat);
            var menuItem = new OleMenuCommand(Execute, cmdId);
            menuItem.BeforeQueryStatus += OnBeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        public static UnformatCommand Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new UnformatCommand(package, commandService);
        }

        private void OnBeforeQueryStatus(object sender, EventArgs e)
        {
            var cmd = (OleMenuCommand)sender;
            cmd.Enabled = true;
            cmd.Visible = true;
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Log.Information("Unformat: command invoked");

            try
            {
                var textManager = (IVsTextManager)Package.GetGlobalService(typeof(SVsTextManager));
                if (textManager == null)
                {
                    Log.Warning("Unformat: IVsTextManager not available");
                    return;
                }

                textManager.GetActiveView(1, null, out var textView);
                if (textView == null)
                {
                    Log.Warning("Unformat: no active text view");
                    return;
                }

                textView.GetBuffer(out var buffer);
                if (buffer == null) return;

                // Check if there's a selection
                textView.GetSelection(out var startLine, out var startCol, out var endLine, out var endCol);
                buffer.GetPositionOfLineIndex(startLine, startCol, out var startOffset);
                buffer.GetPositionOfLineIndex(endLine, endCol, out var endOffset);

                bool hasSelection = startOffset != endOffset;

                buffer.GetLastLineIndex(out var lastLine, out var lastCol);
                buffer.GetLineText(0, 0, lastLine, lastCol, out var documentText);

                if (string.IsNullOrEmpty(documentText)) return;

                var manager = EngineLifecycle.Manager;
                var client = manager?.Client;

                if (client == null || !client.IsConnected)
                {
                    Log.Warning("Unformat: engine not available");
                    return;
                }

                var request = new FormatActionRequest
                {
                    SessionId = Guid.NewGuid().ToString("N"),
                    Text = documentText,
                    ActionType = (int)FormatActionType.Unformat,
                    SelectionStart = hasSelection ? startOffset : 0,
                    SelectionLength = hasSelection ? (endOffset - startOffset) : 0
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
                        Log.Error(ex, "Unformat via engine failed");
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unformat command failed");
            }
        }
    }
}
