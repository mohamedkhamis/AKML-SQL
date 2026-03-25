#nullable enable
using System;
using System.ComponentModel.Design;
using AkmlSql.Shell.Shared.Productivity.Grid;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Commands
{
    /// <summary>
    /// T024: OleMenuCommand for CmdGridFind (0x060A) that toggles <see cref="GridFindBar"/>
    /// visibility. BeforeQueryStatus enables the command only when a results grid has focus.
    /// </summary>
    internal sealed class GridCommands
    {
        private readonly GridFindBar _findBar;

        private GridCommands(OleMenuCommandService commandService)
        {
            _findBar = new GridFindBar();

            // Register CmdGridFind
            var findCmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdGridFind);
            var findMenuItem = new OleMenuCommand(OnGridFindExecute, findCmdId);
            findMenuItem.BeforeQueryStatus += OnGridFindBeforeQueryStatus;
            commandService.AddCommand(findMenuItem);

            Log.Debug("GridCommands: registered CmdGridFind (0x060A)");
        }

        /// <summary>Singleton instance.</summary>
        public static GridCommands? Instance { get; private set; }

        /// <summary>
        /// Provides access to the <see cref="GridFindBar"/> for external callers (e.g. keyboard shortcut
        /// handlers that need to toggle the find bar).
        /// </summary>
        public GridFindBar FindBar => _findBar;

        /// <summary>
        /// Creates the singleton instance and registers grid-related commands.
        /// Must be called on the UI thread during package initialization.
        /// </summary>
        public static void Initialize(OleMenuCommandService commandService)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Instance = new GridCommands(commandService);
        }

        /// <summary>
        /// Executes the Grid Find command — toggles the find bar overlay on the active results grid.
        /// </summary>
        private void OnGridFindExecute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var grid = GridAccessHelper.GetActiveResultsGrid();
                if (grid != null)
                {
                    _findBar.Toggle(grid);
                    Log.Debug("GridCommands: toggled find bar");
                }
                else
                {
                    Log.Debug("GridCommands: no active results grid for find bar");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "GridCommands: CmdGridFind execution failed");
            }
        }

        /// <summary>
        /// Enables the Grid Find command only when a results grid is currently focused or visible.
        /// </summary>
        private void OnGridFindBeforeQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (sender is OleMenuCommand cmd)
            {
                try
                {
                    // Enable if a results grid is focused or if the find bar is already visible
                    cmd.Enabled = GridAccessHelper.IsResultsGridFocused() || _findBar.Visible;
                    cmd.Visible = true;
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "GridCommands: BeforeQueryStatus check failed");
                    cmd.Enabled = false;
                }
            }
        }
    }
}
