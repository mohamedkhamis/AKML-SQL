#nullable enable
using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.TextManager.Interop;
using AkmlSql.Shell.Shared.Productivity.DocumentOutline;
using Serilog;

namespace AkmlSql.Shell.Shared.Commands
{
    /// <summary>
    /// Opens the Document Outline tool window and attaches it to the active editor buffer.
    /// Sends a DocumentOutlineRequest to the engine via IPC and populates the ViewModel
    /// with the response nodes.
    /// Bound to <see cref="CommandIds.CmdDocumentOutline"/> (0x060D).
    /// </summary>
    internal sealed class DocumentOutlineCommand
    {
        private readonly Package _package;

        private DocumentOutlineCommand(Package package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdDocumentOutline);
            var menuItem = new OleMenuCommand(Execute, cmdId);
            commandService.AddCommand(menuItem);
        }

        public static DocumentOutlineCommand? Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new DocumentOutlineCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // Find or create the Document Outline tool window
                var window = _package.FindToolWindow(typeof(DocumentOutlineToolWindow), 0, true);
                if (window?.Frame == null)
                {
                    Log.Warning("DocumentOutlineCommand: could not create tool window");
                    return;
                }

                var windowFrame = (IVsWindowFrame)window.Frame;
                Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());

                // Attach the ViewModel to the active editor buffer so it can
                // send DocumentOutlineRequest to the engine and populate nodes.
                var toolWindow = window as DocumentOutlineToolWindow;
                if (toolWindow == null) return;

                AttachToActiveEditor(toolWindow.ViewModel);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "DocumentOutlineCommand: failed to show tool window");
            }
        }

        /// <summary>
        /// Gets the active text editor buffer and attaches the ViewModel to it.
        /// The ViewModel handles IPC communication with the engine to build
        /// the outline tree and listens for buffer changes to keep it updated.
        /// </summary>
        private static void AttachToActiveEditor(DocumentOutlineViewModel viewModel)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var textManager = (IVsTextManager)Package.GetGlobalService(typeof(SVsTextManager));
                if (textManager == null)
                {
                    Log.Debug("DocumentOutlineCommand: IVsTextManager not available");
                    return;
                }

                textManager.GetActiveView(1, null, out var vsTextView);
                if (vsTextView == null)
                {
                    Log.Debug("DocumentOutlineCommand: no active text view");
                    return;
                }

                vsTextView.GetBuffer(out var vsBuffer);
                if (vsBuffer == null)
                {
                    Log.Debug("DocumentOutlineCommand: no text buffer for active view");
                    return;
                }

                // Get the managed ITextBuffer — try direct cast first, then adapter factory
                // (direct cast works in VS; adapter factory handles SSMS)
                ITextBuffer? managedBuffer = vsBuffer as ITextBuffer;
                if (managedBuffer == null)
                {
                    var componentModel = Package.GetGlobalService(typeof(SComponentModel)) as IComponentModel;
                    var adapters = componentModel?.GetService<IVsEditorAdaptersFactoryService>();
                    managedBuffer = adapters?.GetDocumentBuffer(vsBuffer);
                }

                if (managedBuffer != null)
                {
                    var sessionId = managedBuffer.Properties.GetOrCreateSingletonProperty(
                        "AkmlSqlSessionId", () => Guid.NewGuid().ToString("N"));

                    viewModel.AttachToBuffer(managedBuffer, sessionId);
                    Log.Debug("DocumentOutlineCommand: attached to buffer, session {SessionId}", sessionId);
                }
                else
                {
                    Log.Debug("DocumentOutlineCommand: buffer is not a managed ITextBuffer");
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "DocumentOutlineCommand: failed to attach to active editor");
            }
        }
    }
}
