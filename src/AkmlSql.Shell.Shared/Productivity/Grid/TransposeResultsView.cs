#nullable enable
using System;
using System.Drawing;
using System.Windows.Forms;
using Serilog;

namespace AkmlSql.Shell.Shared.Productivity.Grid
{
    /// <summary>
    /// T073: WinForms dialog showing a single row from the results grid as column-to-value pairs
    /// in a 2-column DataGridView (Column Name | Value). Useful for inspecting wide result sets
    /// where horizontal scrolling makes individual row review difficult.
    /// </summary>
    internal sealed class TransposeResultsView : Form
    {
        private readonly DataGridView _transposeGrid;
        private readonly Label _infoLabel;
        private readonly Button _prevRowButton;
        private readonly Button _nextRowButton;
        private readonly Button _closeButton;

        private readonly DataGridView _sourceGrid;
        private int _currentRowIndex;

        /// <summary>
        /// Creates a new transpose view for a specific row.
        /// </summary>
        /// <param name="sourceGrid">The source DataGridView.</param>
        /// <param name="rowIndex">The zero-based index of the row to display.</param>
        private TransposeResultsView(DataGridView sourceGrid, int rowIndex)
        {
            _sourceGrid = sourceGrid;
            _currentRowIndex = rowIndex;

            Text = $"Row Details - Row {rowIndex + 1}";
            Size = new Size(520, 480);
            MinimumSize = new Size(400, 300);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            ShowInTaskbar = false;
            ShowIcon = false;
            KeyPreview = true;

            // Info label
            _infoLabel = new Label
            {
                Text = $"Row {rowIndex + 1} of {sourceGrid.RowCount}",
                Dock = DockStyle.Top,
                Height = 28,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0),
                BackColor = SystemColors.Control
            };

            // Transpose grid
            _transposeGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
            };

            _transposeGrid.Columns.Add("ColumnName", "Column");
            _transposeGrid.Columns.Add("Value", "Value");

            _transposeGrid.Columns[0].FillWeight = 35;
            _transposeGrid.Columns[1].FillWeight = 65;
            _transposeGrid.Columns[0].DefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            _transposeGrid.Columns[1].DefaultCellStyle.Font = new Font("Consolas", 9f);

            // Button panel
            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(4)
            };

            _closeButton = new Button
            {
                Text = "Close",
                Width = 80,
                Height = 28,
                DialogResult = DialogResult.Cancel
            };

            _nextRowButton = new Button
            {
                Text = "Next Row \u25B6",
                Width = 90,
                Height = 28
            };
            _nextRowButton.Click += (_, __) => NavigateRow(1);

            _prevRowButton = new Button
            {
                Text = "\u25C0 Prev Row",
                Width = 90,
                Height = 28
            };
            _prevRowButton.Click += (_, __) => NavigateRow(-1);

            buttonPanel.Controls.Add(_closeButton);
            buttonPanel.Controls.Add(_nextRowButton);
            buttonPanel.Controls.Add(_prevRowButton);

            CancelButton = _closeButton;

            Controls.Add(_transposeGrid);
            Controls.Add(_infoLabel);
            Controls.Add(buttonPanel);

            PopulateGrid();
            UpdateNavigationButtons();

            KeyDown += OnKeyDown;
        }

        /// <summary>
        /// Shows the transpose view for the currently selected row in the grid, or the
        /// specified row. Returns immediately (modeless).
        /// </summary>
        public static void ShowForRow(DataGridView grid, int rowIndex)
        {
            if (grid == null || rowIndex < 0 || rowIndex >= grid.RowCount)
            {
                Log.Debug("TransposeResultsView: invalid grid or row index {Row}", rowIndex);
                return;
            }

            try
            {
                var view = new TransposeResultsView(grid, rowIndex);
                view.Show();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "TransposeResultsView: failed to show for row {Row}", rowIndex);
            }
        }

        /// <summary>
        /// Shows the transpose view for the first selected row in the grid.
        /// </summary>
        public static void ShowForSelectedRow(DataGridView grid)
        {
            if (grid == null) return;

            int rowIndex = 0;
            if (grid.SelectedCells.Count > 0)
            {
                rowIndex = grid.SelectedCells[0].RowIndex;
            }
            else if (grid.CurrentCell != null)
            {
                rowIndex = grid.CurrentCell.RowIndex;
            }

            ShowForRow(grid, rowIndex);
        }

        /// <summary>
        /// Hooks into the DataGridView's row header double-click to show the transpose view.
        /// </summary>
        public static void AttachToGrid(DataGridView grid)
        {
            grid.RowHeaderMouseDoubleClick += (sender, e) =>
            {
                ShowForRow(grid, e.RowIndex);
            };

            Log.Debug("TransposeResultsView: attached to grid row header double-click");
        }

        private void PopulateGrid()
        {
            _transposeGrid.Rows.Clear();

            if (_currentRowIndex < 0 || _currentRowIndex >= _sourceGrid.RowCount)
                return;

            var row = _sourceGrid.Rows[_currentRowIndex];
            for (int col = 0; col < _sourceGrid.ColumnCount; col++)
            {
                var columnName = _sourceGrid.Columns[col].HeaderText ?? $"Column {col}";
                var cellValue = row.Cells[col].Value;

                string displayValue;
                if (cellValue == null || cellValue is DBNull)
                {
                    displayValue = "NULL";
                }
                else
                {
                    displayValue = cellValue.ToString() ?? string.Empty;
                }

                int rowIdx = _transposeGrid.Rows.Add(columnName, displayValue);

                // Style NULL values
                if (cellValue == null || cellValue is DBNull)
                {
                    _transposeGrid.Rows[rowIdx].Cells[1].Style.ForeColor = Color.Gray;
                    _transposeGrid.Rows[rowIdx].Cells[1].Style.Font =
                        new Font("Consolas", 9f, FontStyle.Italic);
                }
            }

            _infoLabel.Text = $"Row {_currentRowIndex + 1} of {_sourceGrid.RowCount}";
            Text = $"Row Details - Row {_currentRowIndex + 1}";
        }

        private void NavigateRow(int delta)
        {
            int newIndex = _currentRowIndex + delta;
            if (newIndex < 0 || newIndex >= _sourceGrid.RowCount) return;

            _currentRowIndex = newIndex;
            PopulateGrid();
            UpdateNavigationButtons();
        }

        private void UpdateNavigationButtons()
        {
            _prevRowButton.Enabled = _currentRowIndex > 0;
            _nextRowButton.Enabled = _currentRowIndex < _sourceGrid.RowCount - 1;
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Left:
                case Keys.Up when e.Alt:
                    NavigateRow(-1);
                    e.Handled = true;
                    break;

                case Keys.Right:
                case Keys.Down when e.Alt:
                    NavigateRow(1);
                    e.Handled = true;
                    break;
            }
        }
    }
}
