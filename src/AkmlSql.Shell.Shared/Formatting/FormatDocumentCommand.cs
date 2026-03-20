using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio;
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
            ThreadHelper.ThrowIfNotOnUIThread();

            var cmd = (OleMenuCommand)sender;
            // Enable only when there is an active text view
            var textManager = (IVsTextManager)Package.GetGlobalService(typeof(SVsTextManager));
            if (textManager == null)
            {
                cmd.Enabled = false;
                return;
            }

            textManager.GetActiveView(1, null, out var textView);
            cmd.Enabled = textView != null;
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // Get active text view
                var textManager = (IVsTextManager)Package.GetGlobalService(typeof(SVsTextManager));
                if (textManager == null) return;

                textManager.GetActiveView(1, null, out var textView);
                if (textView == null) return;

                textView.GetBuffer(out var buffer);
                if (buffer == null) return;

                // Get full document text
                buffer.GetLastLineIndex(out var lastLine, out var lastCol);
                buffer.GetLineText(0, 0, lastLine, lastCol, out var documentText);

                if (string.IsNullOrEmpty(documentText)) return;

                // Try engine IPC first; fall back to direct pipeline if engine unavailable
                var manager = EngineLifecycle.Manager;
                var client = manager?.Client;

                if (client != null && client.IsConnected)
                {
                    // Async IPC path — fire on thread pool, then marshal result back to UI thread
                    var request = new FormatRequest
                    {
                        SessionId = Guid.NewGuid().ToString("N"),
                        Text = documentText
                    };

                    System.Threading.Tasks.Task.Run(async () =>
                    {
                        try
                        {
                            var response = await client.SendRequestAsync<FormatResponse, FormatRequest>(
                                MessageTypes.FormatDocument, request, timeoutMs: 10000);

                            if (response.Success && response.WasModified)
                            {
                                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                                ApplyFormattedText(buffer, response.FormattedText);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "Format via engine IPC failed, falling back to direct pipeline");
                            FormatDirect(buffer, documentText);
                        }
                    });
                }
                else
                {
                    // Direct in-process formatting (engine not available)
                    FormatDirect(buffer, documentText);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Format document command failed");
            }
        }

        private static void FormatDirect(IVsTextLines buffer, string documentText)
        {
            try
            {
                var pipeline = new AkmlSql.Formatting.Pipeline.FormatterPipeline();
                var profile = new AkmlSql.Formatting.Profiles.FormattingProfile();
                var result = pipeline.Format(documentText, profile);

                if (result.Success && result.WasModified)
                {
                    ThreadHelper.JoinableTaskFactory.Run(async () =>
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        ApplyFormattedText(buffer, result.FormattedText);
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Direct format pipeline failed");
            }
        }

        private static void ApplyFormattedText(IVsTextLines buffer, string formattedText)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                buffer.GetLastLineIndex(out var lastLine, out var lastCol);

                // Replace entire document content
                var hr = buffer.ReplaceLines(0, 0, lastLine, lastCol,
                    System.Runtime.InteropServices.Marshal.StringToHGlobalUni(formattedText),
                    formattedText.Length, null);

                if (hr != VSConstants.S_OK)
                {
                    Log.Warning("Failed to apply formatted text, HRESULT=0x{Hr:X8}", hr);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to apply formatted text to buffer");
            }
        }
    }
}
