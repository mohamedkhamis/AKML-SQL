using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;

namespace AkmlSql.Shell.Shared.Commands
{
    internal sealed class AboutCommand
    {
        private AboutCommand(Package package, OleMenuCommandService commandService)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdAbout);
            var menuItem = new MenuCommand(Execute, cmdId);
            commandService.AddCommand(menuItem);
        }

        public static AboutCommand Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new AboutCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            using (var dialog = new Dialogs.AboutDialog())
            {
                dialog.ShowDialog();
            }
        }
    }
}
