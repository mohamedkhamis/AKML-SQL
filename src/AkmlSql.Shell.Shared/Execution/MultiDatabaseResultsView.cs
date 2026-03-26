#nullable enable
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AkmlSql.Shell.Shared.Execution
{
    /// <summary>
    /// T080: A tabbed panel that displays execution results from multiple databases.
    /// Each database result gets its own TabPage with a DataGridView.
    /// A summary tab shows success/failure status and elapsed time for all databases.
    /// </summary>
    internal sealed class MultiDatabaseResultsView : Form
    {
        private readonly TabControl _tabControl;
        private readonly List<DatabaseExecutionResult> _results;

        public MultiDatabaseResultsView(List<DatabaseExecutionResult> results)
        {
            _results = results;

            Text = "Multi-Database Execution Results";
            Size = new Size(900, 600);
            MinimumSize = new Size(600, 400);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;

            _tabControl = new TabControl { Dock = DockStyle.Fill };

            // Summary tab first
            _tabControl.TabPages.Add(CreateSummaryTab());

            // Individual database tabs
            foreach (var result in results)
            {
                _tabControl.TabPages.Add(CreateDatabaseTab(result));
            }

            Controls.Add(_tabControl);

            var closeButton = new Button
            {
                Text = "Close",
                Size = new Size(90, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                DialogResult = DialogResult.OK,
                FlatStyle = FlatStyle.System
            };

            var bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 50
            };
            bottomPanel.Layout += (_, __) =>
            {
                closeButton.Location = new Point(bottomPanel.Width - 102, 10);
            };
            bottomPanel.Controls.Add(closeButton);

            Controls.Add(bottomPanel);
            AcceptButton = closeButton;
        }

        private TabPage CreateSummaryTab()
        {
            var tab = new TabPage("Summary");

            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false
            };

            grid.Columns.Add("Database", "Database");
            grid.Columns.Add("Status", "Status");
            grid.Columns.Add("RowsAffected", "Rows Affected");
            grid.Columns.Add("Elapsed", "Elapsed (ms)");
            grid.Columns.Add("Error", "Error");

            int successCount = 0;
            int failCount = 0;
            long totalElapsed = 0;

            foreach (var result in _results)
            {
                var rowIdx = grid.Rows.Add(
                    result.DatabaseName,
                    result.Success ? "OK" : "FAILED",
                    result.TotalRowsAffected.ToString(),
                    result.ElapsedMs.ToString(),
                    result.ErrorMessage ?? string.Empty
                );

                if (!result.Success)
                {
                    grid.Rows[rowIdx].DefaultCellStyle.BackColor = Color.FromArgb(255, 230, 230);
                    failCount++;
                }
                else
                {
                    successCount++;
                }

                totalElapsed += result.ElapsedMs;
            }

            var summaryLabel = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0),
                Text = $"Total: {_results.Count} databases | Success: {successCount} | " +
                       $"Failed: {failCount} | Total elapsed: {totalElapsed}ms"
            };

            tab.Controls.Add(grid);
            tab.Controls.Add(summaryLabel);

            return tab;
        }

        private TabPage CreateDatabaseTab(DatabaseExecutionResult result)
        {
            var tabTitle = result.Success ? result.DatabaseName : $"{result.DatabaseName} (FAILED)";
            var tab = new TabPage(tabTitle);

            if (!result.Success)
            {
                var errorLabel = new Label
                {
                    Dock = DockStyle.Top,
                    Height = 50,
                    Text = $"Error: {result.ErrorMessage}",
                    ForeColor = Color.Red,
                    Padding = new Padding(10),
                    AutoEllipsis = true
                };
                tab.Controls.Add(errorLabel);
                return tab;
            }

            if (result.ResultSets.Count == 0)
            {
                var noResultsLabel = new Label
                {
                    Dock = DockStyle.Fill,
                    Text = $"Command completed successfully.\nRows affected: {result.TotalRowsAffected}\nElapsed: {result.ElapsedMs}ms",
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = SystemColors.GrayText
                };
                tab.Controls.Add(noResultsLabel);
                return tab;
            }

            // If multiple result sets, use a sub-tab control
            if (result.ResultSets.Count > 1)
            {
                var subTabs = new TabControl { Dock = DockStyle.Fill };
                for (int i = 0; i < result.ResultSets.Count; i++)
                {
                    var subTab = new TabPage($"Result Set {i + 1}");
                    var grid = CreateResultGrid(result.ResultSets[i]);
                    subTab.Controls.Add(grid);
                    subTabs.TabPages.Add(subTab);
                }
                tab.Controls.Add(subTabs);
            }
            else
            {
                var grid = CreateResultGrid(result.ResultSets[0]);
                tab.Controls.Add(grid);
            }

            return tab;
        }

        private static DataGridView CreateResultGrid(System.Data.DataTable dataTable)
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells,
                DataSource = dataTable,
                RowHeadersVisible = true
            };

            return grid;
        }
    }
}
