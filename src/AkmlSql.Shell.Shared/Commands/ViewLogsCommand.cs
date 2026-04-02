using System;
using System.ComponentModel.Design;
using System.IO;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using AkmlSql.Shell.Shared.Dialogs;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Commands
{
    internal sealed class ViewLogsCommand
    {
        private ViewLogsCommand(Package package, OleMenuCommandService commandService)
        {
            if (package == null)
                throw new ArgumentNullException(nameof(package));

            var cmdId    = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdViewLogs);
            var menuItem = new MenuCommand(Execute, cmdId);
            commandService.AddCommand(menuItem);
        }

        public static ViewLogsCommand Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new ViewLogsCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            try
            {
                // Ensure the logs directory exists so the viewer can open immediately
                Directory.CreateDirectory(Constants.LogsPath);

                using var dialog = new LogViewerDialog();
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "ViewLogsCommand: failed to open log viewer");
                MessageBox.Show(
                    "Could not open the log viewer: " + ex.Message,
                    Constants.ProductName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}
