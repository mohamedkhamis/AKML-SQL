using System;
using System.ComponentModel.Design;
using System.Windows.Forms;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Dialogs;
using Microsoft.VisualStudio.Shell;
using Serilog;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Commands
{
    internal sealed class OptionsCommand
    {
        private OptionsCommand(Package package, OleMenuCommandService commandService)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdOptions);
            var menuItem = new MenuCommand(Execute, cmdId);
            commandService.AddCommand(menuItem);
        }

        public static OptionsCommand Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new OptionsCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            try
            {
                var settings = ConfigManager.Load();
                using (var dialog = new SettingsDialog(settings))
                {
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        var updated = dialog.GetSettings();
                        ConfigManager.Save(updated);
                        Log.Information("Settings saved successfully.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open settings dialog");
                MessageBox.Show(
                    "Failed to load settings: " + ex.Message,
                    Constants.ProductName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}
