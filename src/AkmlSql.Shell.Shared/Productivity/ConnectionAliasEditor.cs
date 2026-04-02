#nullable enable
using System.Drawing;
using System.Windows.Forms;
using AkmlSql.Core.Config;
using Serilog;

namespace AkmlSql.Shell.Shared.Productivity
{
    /// <summary>
    /// T106: Provides alias editing UI integration for the SettingsDialog.
    /// Adds a DataGridView for editing connection alias mappings.
    /// </summary>
    internal static class ConnectionAliasEditor
    {
        /// <summary>
        /// Creates the connection alias editing controls and adds them to a tab page.
        /// Returns the DataGridView for later data binding in Load/Save.
        /// </summary>
        /// <param name="tab">The TabPage to add controls to.</param>
        /// <param name="y">Current vertical position; updated after adding controls.</param>
        /// <returns>The DataGridView for alias management.</returns>
        public static DataGridView CreateAliasGrid(TabPage tab, ref int y)
        {
            var sectionLabel = new Label
            {
                Text = "Connection Aliases",
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 84, 166),
                Location = new Point(15, y),
                AutoSize = true
            };
            tab.Controls.Add(sectionLabel);
            y += 26;

            var hintLabel = new Label
            {
                Text = "Map server names to friendly aliases displayed in the window title and status bar.",
                ForeColor = Color.FromArgb(120, 120, 130),
                Location = new Point(30, y),
                AutoSize = true,
                Font = new Font(SystemFonts.DefaultFont.FontFamily,
                    SystemFonts.DefaultFont.Size - 1f, FontStyle.Italic)
            };
            tab.Controls.Add(hintLabel);
            y += 22;

            var colServer = new DataGridViewTextBoxColumn
            {
                HeaderText = "Server Name / Pattern",
                Width = 220,
                Name = "ServerName"
            };
            var colAlias = new DataGridViewTextBoxColumn
            {
                HeaderText = "Alias",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                Name = "Alias"
            };

            var grid = new DataGridView
            {
                Location = new Point(15, y),
                Size = new Size(tab.ClientSize.Width > 0 ? tab.ClientSize.Width - 30 : 530, 140),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                RowHeadersVisible = true,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            grid.Columns.AddRange(colServer, colAlias);

            tab.Controls.Add(grid);
            y += 150;

            var patternHint = new Label
            {
                Text = "Use * wildcards in server name (e.g., *PROD*, *.database.windows.net)",
                ForeColor = Color.FromArgb(120, 120, 130),
                Location = new Point(30, y),
                AutoSize = true,
                Font = new Font(SystemFonts.DefaultFont.FontFamily,
                    SystemFonts.DefaultFont.Size - 1f, FontStyle.Italic)
            };
            tab.Controls.Add(patternHint);
            y += 22;

            return grid;
        }

        /// <summary>
        /// Loads alias data from settings into the grid.
        /// </summary>
        public static void LoadAliases(DataGridView grid, NavigationSettings navSettings)
        {
            grid.Rows.Clear();
            foreach (var alias in navSettings.ConnectionAliases)
            {
                grid.Rows.Add(alias.ServerName, alias.Alias);
            }
        }

        /// <summary>
        /// Saves alias data from the grid back to settings.
        /// </summary>
        public static void SaveAliases(DataGridView grid, NavigationSettings navSettings)
        {
            navSettings.ConnectionAliases.Clear();
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow) continue;
                var serverName = row.Cells["ServerName"].Value as string;
                var alias = row.Cells["Alias"].Value as string;

                if (string.IsNullOrWhiteSpace(serverName)) continue;

                navSettings.ConnectionAliases.Add(new ConnectionAliasEntry
                {
                    ServerName = serverName.Trim(),
                    Alias = alias?.Trim() ?? string.Empty
                });
            }

            Log.Debug("ConnectionAliasEditor: saved {Count} aliases", navSettings.ConnectionAliases.Count);
        }
    }
}
