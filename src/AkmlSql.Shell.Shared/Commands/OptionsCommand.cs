using System;
using System.ComponentModel.Design;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Commands
{
    internal sealed class OptionsCommand
    {
        private readonly Package _package;

        private OptionsCommand(Package package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
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
            // TODO T102: Replace this placeholder with a WPF SettingsDialog that exposes
            //   IntelliSenseSettings and CacheSettings from AppSettings for editing.
            //   Dialog should bind to ConfigManager.Load() and Save() on OK.
            // TODO T103: After settings are saved, propagate changes to the running engine
            //   by sending a SettingsChanged notification via PipeRpcClient so the engine
            //   can update MaxSuggestions, RefreshInterval, FuzzyMatch, etc. without restart.
            MessageBox.Show(
                "Settings will be available in a future update.",
                Constants.ProductName + " Options",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }
}
