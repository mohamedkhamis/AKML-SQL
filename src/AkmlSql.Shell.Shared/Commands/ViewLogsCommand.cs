using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.Shell;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Commands
{
    internal sealed class ViewLogsCommand
    {
        private readonly Package _package;

        private ViewLogsCommand(Package package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdViewLogs);
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
            var logsPath = Constants.LogsPath;
            if (!Directory.Exists(logsPath))
                Directory.CreateDirectory(logsPath);

            using (Process.Start("explorer.exe", logsPath)) { }
        }
    }
}
