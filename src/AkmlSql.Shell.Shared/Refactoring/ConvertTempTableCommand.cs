using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace AkmlSql.Shell.Shared.Refactoring
{
    /// <summary>
    /// Placeholder commands for Convert Temp Table ↔ Table Variable refactoring operations.
    /// Full implementation pending RefactoringPreviewDialog in a future build.
    /// </summary>
    internal static class ConvertTempTableCommand
    {
        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
            commandService.AddCommand(new MenuCommand(ExecuteTempToVar,
                new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdConvertTempToTableVar)));
            commandService.AddCommand(new MenuCommand(ExecuteVarToTemp,
                new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdConvertTableVarToTemp)));
        }

        private static void ExecuteTempToVar(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsShellUtilities.ShowMessageBox(
                null,
                "Convert Temp Table to Table Variable is available in the next update.",
                "AKML SQL",
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }

        private static void ExecuteVarToTemp(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsShellUtilities.ShowMessageBox(
                null,
                "Convert Table Variable to Temp Table is available in the next update.",
                "AKML SQL",
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
