#nullable enable
using System;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using Serilog;

namespace AkmlSql.Shell.Shared.Productivity.Grid
{
    /// <summary>
    /// Provides column-level text filtering on DataGridView results grids.
    /// Shows a small popup on column header right-click with a filter textbox.
    /// Uses DataView.RowFilter for filtering.
    /// </summary>
    internal sealed class GridFilterPopup : IDisposable
    {
        private DataGridView? _grid;
        private Form? _popup;
        private int _filteredColumnIndex = -1;
        private string _activeFilter = string.Empty;

        public void Attach(DataGridView grid)
        {
            Detach();
            _grid = grid;
            _filteredColumnIndex = -1;
            _activeFilter = string.Empty;
            _grid.ColumnHeaderMouseClick += OnColumnHeaderClick;
        }

        public void Detach()
        {
            ClosePopup();
            if (_grid != null)
            {
                _grid.ColumnHeaderMouseClick -= OnColumnHeaderClick;
                // Don't call ClearFilter() here — the old grid's RowFilter belongs to that result
                // and should persist. Only reset our tracking state.
                _grid = null;
            }
            _filteredColumnIndex = -1;
            _activeFilter = string.Empty;
        }

        private void OnColumnHeaderClick(object? sender, DataGridViewCellMouseEventArgs e)
        {
            // Only respond to right-click
            if (e.Button != MouseButtons.Right || e.ColumnIndex < 0 || _grid == null)
                return;

            try
            {
                var column = _grid.Columns[e.ColumnIndex];
                var columnName = column.DataPropertyName ?? column.Name;

                // Get click screen position
                var headerRect = _grid.GetColumnDisplayRectangle(e.ColumnIndex, true);
                var screenPos = _grid.PointToScreen(new Point(headerRect.Left, headerRect.Bottom));

                ShowFilterPopup(screenPos, columnName, e.ColumnIndex);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "GridFilterPopup: failed to show filter popup");
            }
        }

        private void ShowFilterPopup(Point screenLocation, string columnName, int columnIndex)
        {
            ClosePopup();

            _popup = new Form
            {
                Text = $"Filter: {columnName}",
                FormBorderStyle = FormBorderStyle.FixedToolWindow,
                StartPosition = FormStartPosition.Manual,
                Location = screenLocation,
                Size = new Size(280, 110),
                ShowInTaskbar = false,
                TopMost = true
            };

            var filterBox = new TextBox
            {
                Text = _filteredColumnIndex == columnIndex ? _activeFilter : string.Empty,
                Location = new Point(10, 12),
                Size = new Size(250, 24),
                Font = new Font("Segoe UI", 9)
            };

            var btnApply = new Button
            {
                Text = "Filter",
                Location = new Point(90, 46),
                Size = new Size(75, 26)
            };
            btnApply.Click += (s, ev) =>
            {
                ApplyFilter(columnName, columnIndex, filterBox.Text.Trim());
                ClosePopup();
            };

            var btnClear = new Button
            {
                Text = "Clear",
                Location = new Point(175, 46),
                Size = new Size(75, 26)
            };
            btnClear.Click += (s, ev) =>
            {
                ClearFilter();
                ClosePopup();
            };

            _popup.Controls.AddRange(new Control[] { filterBox, btnApply, btnClear });
            _popup.AcceptButton = btnApply;
            _popup.ActiveControl = filterBox;

            // Close on deactivate
            _popup.Deactivate += (s, ev) => ClosePopup();

            _popup.Show();
        }

        private void ApplyFilter(string columnName, int columnIndex, string filterText)
        {
            if (_grid == null || string.IsNullOrEmpty(filterText)) return;

            try
            {
                // Escape single quotes for DataView filter expression
                var escaped = filterText.Replace("'", "''");
                var expression = $"CONVERT([{columnName}], 'System.String') LIKE '%{escaped}%'";

                if (_grid.DataSource is DataView dv)
                {
                    dv.RowFilter = expression;
                }
                else if (_grid.DataSource is DataTable dt)
                {
                    dt.DefaultView.RowFilter = expression;
                }
                else if (_grid.DataSource is BindingSource bs && bs.DataSource is DataView bsDv)
                {
                    bsDv.RowFilter = expression;
                }

                _filteredColumnIndex = columnIndex;
                _activeFilter = filterText;

                Log.Debug("GridFilterPopup: applied filter [{Column}] LIKE '%{Filter}%'",
                    columnName, filterText);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "GridFilterPopup: filter failed for column {Col}", columnName);
                // Some columns may not support LIKE — show a message
                MessageBox.Show($"Cannot filter column '{columnName}'.\n{ex.Message}",
                    "Filter Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ClearFilter()
        {
            try
            {
                if (_grid?.DataSource is DataView dv)
                    dv.RowFilter = string.Empty;
                else if (_grid?.DataSource is DataTable dt)
                    dt.DefaultView.RowFilter = string.Empty;
                else if (_grid?.DataSource is BindingSource bs && bs.DataSource is DataView bsDv)
                    bsDv.RowFilter = string.Empty;

                _filteredColumnIndex = -1;
                _activeFilter = string.Empty;
            }
            catch
            {
                // Ignore — some data sources don't support clearing filter
            }
        }

        private void ClosePopup()
        {
            if (_popup != null && !_popup.IsDisposed)
            {
                _popup.Close();
                _popup.Dispose();
            }
            _popup = null;
        }

        public void Dispose()
        {
            Detach();
        }
    }
}
