#nullable enable
using System;
using System.ComponentModel.Design;
using AkmlSql.Shell.Shared.History;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Serilog;

namespace AkmlSql.Shell.Shared.Commands
{
    /// <summary>
    /// Opens (or brings to front) the SQL History tool window.
    /// Bound to <see cref="CommandIds.CmdHistoryPanel"/> (0x0500), keyboard shortcut Ctrl+Alt+H.
    /// </summary>
    internal sealed class HistoryPanelCommand
    {
        private readonly Package _package;

        private HistoryPanelCommand(Package package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdHistoryPanel);
            var menuItem = new MenuCommand(Execute, cmdId);
            commandService.AddCommand(menuItem);
        }

        /// <summary>Singleton instance.</summary>
        public static HistoryPanelCommand? Instance { get; private set; }

        /// <summary>
        /// Creates the singleton command instance and registers it with the command service.
        /// Must be called on the UI thread during package initialization.
        /// Overload for AsyncPackage (SSMS 21/22, VS 2019/2022/2026).
        /// </summary>
        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
            Instance = new HistoryPanelCommand(package, commandService);
        }

        /// <summary>
        /// Creates the singleton command instance and registers it with the command service.
        /// Must be called on the UI thread during package initialization.
        /// Overload for synchronous Package (SSMS 20).
        /// </summary>
        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new HistoryPanelCommand(package, commandService);
        }

        /// <summary>
        /// Finds or creates the <see cref="HistoryToolWindow"/> and shows it.
        /// </summary>
        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var window = _package.FindToolWindow(typeof(HistoryToolWindow), 0, create: true);
                if (window?.Frame == null)
                {
                    Log.Warning("HistoryPanelCommand: failed to create HistoryToolWindow");
                    return;
                }

                var windowFrame = (IVsWindowFrame)window.Frame;
                Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());

                Log.Debug("HistoryPanelCommand: SQL History tool window shown");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HistoryPanelCommand: failed to show SQL History tool window");
            }
        }
    }
}
