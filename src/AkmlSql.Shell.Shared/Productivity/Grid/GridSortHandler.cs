#nullable enable
using System;
using System.ComponentModel;
using System.Data;
using System.Windows.Forms;
using Serilog;

namespace AkmlSql.Shell.Shared.Productivity.Grid
{
    /// <summary>
    /// Attaches to a DataGridView and enables column sorting on header click.
    /// Implements a 3-click cycle: Ascending → Descending → None.
    /// Uses the backing DataTable's DefaultView.Sort for actual sorting.
    /// </summary>
    internal sealed class GridSortHandler : IDisposable
    {
        private DataGridView? _grid;
        private int _sortedColumnIndex = -1;
        private SortOrder _currentSortOrder = SortOrder.None;

        public void Attach(DataGridView grid)
        {
            Detach();
            _grid = grid;
            _sortedColumnIndex = -1;
            _currentSortOrder = SortOrder.None;
            _grid.ColumnHeaderMouseClick += OnColumnHeaderMouseClick;
        }

        public void Detach()
        {
            if (_grid != null)
            {
                _grid.ColumnHeaderMouseClick -= OnColumnHeaderMouseClick;
                ClearSortGlyphs();
                _grid = null;
            }
            _sortedColumnIndex = -1;
            _currentSortOrder = SortOrder.None;
        }

        private void OnColumnHeaderMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
        {
            try
            {
                if (_grid == null || e.ColumnIndex < 0) return;

                var column = _grid.Columns[e.ColumnIndex];
                if (column == null) return;

                // Determine next sort order
                if (e.ColumnIndex == _sortedColumnIndex)
                {
                    // Same column — cycle: Asc → Desc → None
                    _currentSortOrder = _currentSortOrder switch
                    {
                        SortOrder.Ascending => SortOrder.Descending,
                        SortOrder.Descending => SortOrder.None,
                        _ => SortOrder.Ascending
                    };
                }
                else
                {
                    // Different column — start with Ascending
                    _currentSortOrder = SortOrder.Ascending;
                }

                _sortedColumnIndex = e.ColumnIndex;

                // Clear all column glyphs
                ClearSortGlyphs();

                if (_currentSortOrder == SortOrder.None)
                {
                    // Remove sort
                    RemoveSort();
                    return;
                }

                // Apply sort
                var columnName = column.DataPropertyName ?? column.Name;
                var direction = _currentSortOrder == SortOrder.Ascending ? "ASC" : "DESC";

                // Try DataView.Sort first (works when bound to DataTable)
                if (TrySortViaDataView(columnName, direction))
                {
                    column.HeaderCell.SortGlyphDirection = _currentSortOrder;
                    Log.Debug("GridSortHandler: sorted by [{Column}] {Dir} via DataView",
                        columnName, direction);
                    return;
                }

                // Fallback: use DataGridView built-in sort
                var listDirection = _currentSortOrder == SortOrder.Ascending
                    ? ListSortDirection.Ascending
                    : ListSortDirection.Descending;
                _grid.Sort(column, listDirection);
                column.HeaderCell.SortGlyphDirection = _currentSortOrder;
                Log.Debug("GridSortHandler: sorted by [{Column}] {Dir} via built-in sort",
                    columnName, direction);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "GridSortHandler: sort failed for column {Col}", e.ColumnIndex);
            }
        }

        private bool TrySortViaDataView(string columnName, string direction)
        {
            try
            {
                if (_grid?.DataSource is DataView dv)
                {
                    dv.Sort = $"[{columnName}] {direction}";
                    return true;
                }

                if (_grid?.DataSource is DataTable dt)
                {
                    dt.DefaultView.Sort = $"[{columnName}] {direction}";
                    return true;
                }

                if (_grid?.DataSource is BindingSource bs && bs.DataSource is DataView bsDv)
                {
                    bsDv.Sort = $"[{columnName}] {direction}";
                    return true;
                }
            }
            catch
            {
                // Sort expression may fail for computed or complex columns
            }
            return false;
        }

        private void RemoveSort()
        {
            try
            {
                if (_grid?.DataSource is DataView dv)
                    dv.Sort = string.Empty;
                else if (_grid?.DataSource is DataTable dt)
                    dt.DefaultView.Sort = string.Empty;
                else if (_grid?.DataSource is BindingSource bs && bs.DataSource is DataView bsDv)
                    bsDv.Sort = string.Empty;
            }
            catch
            {
                // Ignore — some data sources don't support clearing sort
            }
        }

        private void ClearSortGlyphs()
        {
            if (_grid == null || _grid.IsDisposed) return;
            try
            {
                foreach (DataGridViewColumn col in _grid.Columns)
                {
                    col.HeaderCell.SortGlyphDirection = SortOrder.None;
                }
            }
            catch (ObjectDisposedException)
            {
                // Grid was disposed by SSMS between our check and iteration — safe to ignore
            }
        }

        public void Dispose()
        {
            Detach();
        }
    }
}
