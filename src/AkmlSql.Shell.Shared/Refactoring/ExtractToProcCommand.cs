using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace AkmlSql.Shell.Shared.Refactoring
{
    /// <summary>
    /// Placeholder command for the Extract to Stored Procedure refactoring operation.
    /// Full implementation pending RefactoringPreviewDialog in a future build.
    /// </summary>
    internal static class ExtractToProcCommand
    {
        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
            commandService.AddCommand(new MenuCommand(Execute,
                new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdExtractToProc)));
        }

        private static void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsShellUtilities.ShowMessageBox(
                null,
                "Extract to Stored Procedure is available in the next update.",
                "AKML SQL",
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
