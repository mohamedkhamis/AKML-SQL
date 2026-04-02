#nullable enable
using System;
using System.ComponentModel.Design;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Productivity.Grid
{
    /// <summary>
    /// T032: Wires all grid context menu items (Copy As, Export, Generate Script)
    /// and the CmdGridExport OleMenuCommand.
    /// </summary>
    internal static class GridContextMenuWiring
    {
        private static bool _contextMenuInitialized;

        /// <summary>
        /// Registers the CmdGridExport menu command with the command service.
        /// Must be called on the UI thread during package initialization.
        /// </summary>
        public static void RegisterCommands(OleMenuCommandService commandService)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // CmdGridExport toolbar/menu command
                var exportCmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdGridExport);
                var exportCmd = new OleMenuCommand(OnGridExportExecute, exportCmdId);
                exportCmd.BeforeQueryStatus += OnGridExportQueryStatus;
                commandService.AddCommand(exportCmd);

                Log.Debug("GridContextMenuWiring: registered grid commands");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "GridContextMenuWiring: failed to register commands");
            }
        }

        /// <summary>
        /// Initializes the grid context menu with Copy As, Export, and Generate Script items.
        /// Should be called after the results grid becomes available.
        /// Safe to call multiple times.
        /// </summary>
        public static void EnsureContextMenuInitialized()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_contextMenuInitialized) return;

            try
            {
                // Attach Copy As context menu
                GridCopyAsMenu.EnsureAttached();

                // Add script generation items to the existing context menu
                AttachScriptGenerationMenu();

                _contextMenuInitialized = true;
                Log.Debug("GridContextMenuWiring: context menu initialized");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "GridContextMenuWiring: failed to initialize context menu");
            }
        }

        /// <summary>
        /// Refreshes the context menu attachment (e.g., after a new results grid appears).
        /// </summary>
        public static void RefreshContextMenu()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                GridCopyAsMenu.AttachToGrids();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "GridContextMenuWiring: refresh failed");
            }
        }

        #region Command handlers

        private static void OnGridExportExecute(object? sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            GridExportManager.ExportToFile();
        }

        private static void OnGridExportQueryStatus(object? sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (sender is OleMenuCommand cmd)
            {
                // Enable only when a results grid with data is visible
                cmd.Enabled = GridAccessHelper.IsResultsGridFocused();
                cmd.Visible = true;
            }
        }

        #endregion

        #region Script generation context menu

        private static void AttachScriptGenerationMenu()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var grid = GridAccessHelper.GetActiveResultsGrid();
                if (grid?.ContextMenuStrip == null) return;

                var scriptMenu = new ToolStripMenuItem("Generate Script") { Name = "AkmlGenerateScript" };
                scriptMenu.DropDownItems.Add(CreateScriptMenuItem("INSERT Statements", GridScriptGenerator.ScriptMode.Insert));
                scriptMenu.DropDownItems.Add(CreateScriptMenuItem("UPDATE Statements", GridScriptGenerator.ScriptMode.Update));
                scriptMenu.DropDownItems.Add(CreateScriptMenuItem("DELETE Statements", GridScriptGenerator.ScriptMode.Delete));

                var exportItem = new ToolStripMenuItem("Export to File...") { Name = "AkmlExportToFile" };
                exportItem.Click += (_, _) => GridExportManager.ExportToFile();

                var menu = grid.ContextMenuStrip;
                if (!menu.Items.ContainsKey("AkmlGenerateScript"))
                {
                    menu.Items.Add(new ToolStripSeparator());
                    menu.Items.Add(scriptMenu);
                    menu.Items.Add(exportItem);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "GridContextMenuWiring: failed to attach script generation menu");
            }
        }

        private static ToolStripMenuItem CreateScriptMenuItem(string text, GridScriptGenerator.ScriptMode mode)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                GridScriptGenerator.GenerateScript(mode);
            };
            return item;
        }

        #endregion
    }
}
