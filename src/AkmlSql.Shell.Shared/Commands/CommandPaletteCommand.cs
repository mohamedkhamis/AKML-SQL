#nullable enable
using System;
using System.ComponentModel.Design;
using AkmlSql.Shell.Shared.Productivity.CommandPalette;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Commands
{
    /// <summary>
    /// T037: OleMenuCommand for CmdCommandPalette (0x0600).
    /// Shows the Command Palette window when invoked.
    /// Keyboard shortcut: Ctrl+Shift+P.
    /// </summary>
    internal sealed class CommandPaletteCommand
    {
        private readonly CommandPaletteWindow _paletteWindow = new();

        private CommandPaletteCommand(Package package, OleMenuCommandService commandService)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdCommandPalette);
            var menuItem = new MenuCommand(Execute, cmdId);
            commandService.AddCommand(menuItem);
        }

        /// <summary>Singleton instance.</summary>
        public static CommandPaletteCommand? Instance { get; private set; }

        /// <summary>
        /// Creates the singleton command instance and registers it with the command service.
        /// Must be called on the UI thread during package initialization.
        /// Overload for AsyncPackage (SSMS 21/22, VS 2019/2022/2026).
        /// </summary>
        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
            Instance = new CommandPaletteCommand(package, commandService);
        }

        /// <summary>
        /// Creates the singleton command instance and registers it with the command service.
        /// Must be called on the UI thread during package initialization.
        /// Overload for synchronous Package (SSMS 20).
        /// </summary>
        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new CommandPaletteCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                _paletteWindow.Show();
                Log.Debug("CommandPaletteCommand: palette shown");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "CommandPaletteCommand: failed to show palette");
            }
        }
    }
}
