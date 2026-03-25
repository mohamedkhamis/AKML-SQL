#nullable enable
using System;
using System.Drawing;
using System.Windows.Forms;
using AkmlSql.Core.Config;
using Serilog;

namespace AkmlSql.Shell.Shared.Productivity.Grid
{
    /// <summary>
    /// T075: Adds a virtual "Row #" column to the DataGridView results grid showing 1-based
    /// row numbers. Guarded by <see cref="GridSettings.RowNumbers"/>. The column is read-only,
    /// frozen, and positioned as the first column.
    /// </summary>
    internal sealed class RowNumberProvider : IDisposable
    {
        private const string RowNumberColumnName = "__AkmlSql_RowNum";
        private const string RowNumberHeaderText = "Row #";

        private DataGridView? _attachedGrid;
        private bool _disposed;

        /// <summary>
        /// Checks if the row number feature is enabled in settings.
        /// </summary>
        public static bool IsEnabled()
        {
            try
            {
                var settings = ConfigManager.Load();
                return settings.Grid.RowNumbers;
            }
            catch
            {
                return false; // Default disabled
            }
        }

        /// <summary>
        /// Attaches the row number provider to a DataGridView. Adds a "Row #" column
        /// and populates it with 1-based row numbers. Detaches from any previously
        /// attached grid first.
        /// </summary>
        public void Attach(DataGridView grid)
        {
            Detach();

            _attachedGrid = grid;

            try
            {
                AddRowNumberColumn(grid);
                PopulateRowNumbers(grid);

                // Subscribe to row changes to update numbers
                grid.RowsAdded += OnRowsChanged;
                grid.RowsRemoved += OnRowsChanged;
                grid.DataBindingComplete += OnDataBindingComplete;

                Log.Debug("RowNumberProvider: attached to grid, added Row # column for {Rows} rows",
                    grid.RowCount);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "RowNumberProvider: failed to attach to grid");
            }
        }

        /// <summary>
        /// Detaches from the currently attached grid and removes the Row # column.
        /// </summary>
        public void Detach()
        {
            if (_attachedGrid == null) return;

            try
            {
                _attachedGrid.RowsAdded -= OnRowsChanged;
                _attachedGrid.RowsRemoved -= OnRowsChanged;
                _attachedGrid.DataBindingComplete -= OnDataBindingComplete;

                RemoveRowNumberColumn(_attachedGrid);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "RowNumberProvider: error during detach");
            }

            _attachedGrid = null;
        }

        private static void AddRowNumberColumn(DataGridView grid)
        {
            // Check if column already exists
            if (grid.Columns.Contains(RowNumberColumnName)) return;

            var col = new DataGridViewTextBoxColumn
            {
                Name = RowNumberColumnName,
                HeaderText = RowNumberHeaderText,
                ReadOnly = true,
                Frozen = true,
                Width = 60,
                MinimumWidth = 40,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                Resizable = DataGridViewTriState.True,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = DataGridViewContentAlignment.MiddleRight,
                    BackColor = Color.FromArgb(245, 245, 245),
                    ForeColor = Color.Gray,
                    Font = new Font("Consolas", 8.5f)
                }
            };

            // Insert as first column
            grid.Columns.Insert(0, col);
        }

        private static void RemoveRowNumberColumn(DataGridView grid)
        {
            if (grid.Columns.Contains(RowNumberColumnName))
            {
                grid.Columns.Remove(RowNumberColumnName);
            }
        }

        private static void PopulateRowNumbers(DataGridView grid)
        {
            var colIndex = grid.Columns[RowNumberColumnName]?.Index;
            if (colIndex == null) return;

            for (int i = 0; i < grid.RowCount; i++)
            {
                var row = grid.Rows[i];
                if (!row.IsNewRow)
                {
                    row.Cells[colIndex.Value].Value = (i + 1).ToString();
                }
            }
        }

        private void OnRowsChanged(object? sender, DataGridViewRowsAddedEventArgs e)
        {
            if (_attachedGrid != null)
            {
                PopulateRowNumbers(_attachedGrid);
            }
        }

        private void OnRowsChanged(object? sender, DataGridViewRowsRemovedEventArgs e)
        {
            if (_attachedGrid != null)
            {
                PopulateRowNumbers(_attachedGrid);
            }
        }

        private void OnDataBindingComplete(object? sender, DataGridViewBindingCompleteEventArgs e)
        {
            if (_attachedGrid != null)
            {
                // Re-add column if it was removed during data binding
                if (!_attachedGrid.Columns.Contains(RowNumberColumnName))
                {
                    AddRowNumberColumn(_attachedGrid);
                }

                PopulateRowNumbers(_attachedGrid);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Detach();
        }
    }
}
