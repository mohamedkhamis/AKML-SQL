#nullable enable
using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using AkmlSql.Shell.Shared.Productivity.DocumentOutline;
using Serilog;

namespace AkmlSql.Shell.Shared.Commands
{
    /// <summary>
    /// Opens the Document Outline tool window.
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
            }
            catch (Exception ex)
            {
                Log.Error(ex, "DocumentOutlineCommand: failed to show tool window");
            }
        }
    }
}
