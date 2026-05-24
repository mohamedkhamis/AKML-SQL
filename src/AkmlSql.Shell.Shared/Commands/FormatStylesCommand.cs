using System;
using System.ComponentModel.Design;
using System.Windows;
using AkmlSql.Shell.Shared.Formatting;
using Microsoft.VisualStudio.Shell;
using Serilog;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Commands
{
    /// <summary>
    /// Spec 020 US3 T059 — opens the <see cref="FormatStylesEditorWindow"/> three-column
    /// editor. Mirrors <see cref="OptionsCommand"/> structurally; the editor itself owns
    /// theme attach, DTE owner, IPC plumbing, and live preview.
    /// </summary>
    internal sealed class FormatStylesCommand
    {
        private FormatStylesCommand(Package package, OleMenuCommandService commandService)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdFormatStyles);
            var menuItem = new MenuCommand(Execute, cmdId);
            commandService.AddCommand(menuItem);
        }

        public static FormatStylesCommand Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new FormatStylesCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            try
            {
                FormatStylesEditorWindow.Launch();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open Format Styles editor");
                MessageBox.Show(
                    "Failed to open Format Styles editor: " + ex.Message,
                    Constants.ProductName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
