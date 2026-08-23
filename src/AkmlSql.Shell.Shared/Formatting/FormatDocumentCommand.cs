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
    /// Formats the entire active SQL document by sending a FormatRequest to the engine
    /// via named-pipe RPC and replacing the editor buffer contents with the result.
    /// </summary>
    internal sealed class FormatDocumentCommand
    {
        private FormatDocumentCommand(Package package, OleMenuCommandService commandService)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdFormatDocument);
            var menuItem = new OleMenuCommand(Execute, cmdId);
            menuItem.BeforeQueryStatus += OnBeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        public static FormatDocumentCommand Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new FormatDocumentCommand(package, commandService);
        }

        private void OnBeforeQueryStatus(object sender, EventArgs e)
        {
            // Always enable — IVsTextManager.GetActiveView returns null in SSMS 22
            var cmd = (OleMenuCommand)sender;
            cmd.Enabled = true;
            cmd.Visible = true;
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Log.Information("Format Document: command invoked");

            try
            {
                var textManager = (IVsTextManager)Package.GetGlobalService(typeof(SVsTextManager));
                if (textManager == null)
                {
                    Log.Warning("Format Document: IVsTextManager not available");
                    return;
                }

                textManager.GetActiveView(1, null, out var textView);
                if (textView == null)
                {
                    Log.Warning("Format Document: no active text view");
                    return;
                }

                textView.GetBuffer(out var buffer);
                if (buffer == null) return;

                buffer.GetLastLineIndex(out var lastLine, out var lastCol);
                buffer.GetLineText(0, 0, lastLine, lastCol, out var documentText);

                if (string.IsNullOrEmpty(documentText)) return;

                var manager = EngineLifecycle.Manager;
                var client = manager?.Client;

                if (client == null || !client.IsConnected)
                {
                    Log.Warning("Format document: engine not available");
                    return;
                }

                var request = new FormatRequest
                {
                    SessionId = Guid.NewGuid().ToString("N"),
                    Text = documentText,
                    // Without this the engine gets null, falls back to new FormattingProfile(), and
                    // formats with POCO defaults — so Format SQL ignored the active style entirely.
                    // Resolved per invocation so activating a style takes effect immediately.
                    ProfileName = FormatActionHelper.ResolveActiveProfileName(),
                };

                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        var response = await client.SendRequestAsync<FormatResponse, FormatRequest>(
                            MessageTypes.FormatDocument, request, timeoutMs: 10000);

                        // A style that cannot be loaded still "succeeds" (with defaults), so this is
                        // reported outside the preserve branch below — which stays silent on success.
                        FormatFailureNotifier.NotifyProfileFallbackOnce(response.ProfileFallbackWarning);

                        if (response.Success && response.WasModified)
                        {
                            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                            FormatActionHelper.ApplyFormattedText(buffer, response.FormattedText);
                        }
                        else
                        {
                            // FR-005: the engine preserved the original — tell the user why.
                            await FormatFailureNotifier.NotifyIfPreservedAsync(
                                response.Success, response.ValidationPassed, response.Diagnostics);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Format document via engine failed");
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Format document command failed");
            }
        }
    }
}
