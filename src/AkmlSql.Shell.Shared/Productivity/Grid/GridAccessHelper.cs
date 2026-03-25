#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Productivity.Grid
{
    /// <summary>
    /// T021: Utility to locate the SSMS/VS DataGridView results grid from the active document
    /// window by walking the WPF visual tree and WinForms control hierarchy.
    /// All methods must be called on the UI thread.
    /// </summary>
    internal static class GridAccessHelper
    {
        // TODO: SSMS-version-specific grid discovery — SSMS 20 uses a different results pane class
        // than SSMS 21/22. Add version-specific fallback paths here when those differences are known.

        /// <summary>
        /// Attempts to locate the active results grid (DataGridView) from the SSMS results pane.
        /// Returns null if no results grid is currently visible.
        /// </summary>
        public static DataGridView? GetActiveResultsGrid()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // Strategy 1: Walk open forms looking for a DataGridView inside the SSMS results pane.
                // The SSMS results grid is hosted inside a WinForms control hierarchy within the
                // document window. We search from Application.OpenForms downward.
                foreach (Form form in Application.OpenForms)
                {
                    var grid = FindDataGridViewInControl(form);
                    if (grid != null && grid.Visible && grid.RowCount > 0)
                    {
                        return grid;
                    }
                }

                // Strategy 2: Use DTE to get the active document window and search its content.
                var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                if (dte?.ActiveDocument != null)
                {
                    var activeWindow = dte.ActiveWindow;
                    if (activeWindow != null)
                    {
                        var grid = FindGridFromDteWindow(activeWindow);
                        if (grid != null)
                        {
                            return grid;
                        }
                    }
                }

                // Strategy 3: Fallback — search all controls in the focused form
                var focusedControl = GetFocusedControl();
                if (focusedControl != null)
                {
                    // Walk up to find a parent that contains a DataGridView
                    var parent = focusedControl;
                    while (parent != null)
                    {
                        if (parent is DataGridView dgv && dgv.RowCount > 0)
                        {
                            return dgv;
                        }

                        var grid = FindDataGridViewInControl(parent);
                        if (grid != null && grid.Visible && grid.RowCount > 0)
                        {
                            return grid;
                        }

                        parent = parent.Parent;
                    }
                }

                Log.Debug("GridAccessHelper: no active results grid found");
                return null;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "GridAccessHelper.GetActiveResultsGrid failed");
                return null;
            }
        }

        /// <summary>
        /// Returns the currently selected cells from the active results grid.
        /// Returns an empty list if no grid is active or no cells are selected.
        /// </summary>
        public static List<DataGridViewCell> GetSelectedCells()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var result = new List<DataGridViewCell>();
            var grid = GetActiveResultsGrid();
            if (grid == null) return result;

            try
            {
                foreach (DataGridViewCell cell in grid.SelectedCells)
                {
                    result.Add(cell);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "GridAccessHelper.GetSelectedCells failed");
            }

            return result;
        }

        /// <summary>
        /// Returns all column headers from the active results grid.
        /// Returns an empty list if no grid is active.
        /// </summary>
        public static List<string> GetColumnHeaders()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var headers = new List<string>();
            var grid = GetActiveResultsGrid();
            if (grid == null) return headers;

            try
            {
                foreach (DataGridViewColumn col in grid.Columns)
                {
                    headers.Add(col.HeaderText ?? string.Empty);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "GridAccessHelper.GetColumnHeaders failed");
            }

            return headers;
        }

        /// <summary>
        /// Returns the value at the specified row and column in the active results grid.
        /// Returns null if the grid is not active or the coordinates are out of range.
        /// </summary>
        public static object? GetCellValue(int rowIndex, int columnIndex)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var grid = GetActiveResultsGrid();
            if (grid == null) return null;

            try
            {
                if (rowIndex < 0 || rowIndex >= grid.RowCount) return null;
                if (columnIndex < 0 || columnIndex >= grid.ColumnCount) return null;

                return grid.Rows[rowIndex].Cells[columnIndex].Value;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "GridAccessHelper.GetCellValue({Row}, {Col}) failed", rowIndex, columnIndex);
                return null;
            }
        }

        /// <summary>
        /// Returns the value at the specified row and column from a specific DataGridView.
        /// </summary>
        public static object? GetCellValue(DataGridView grid, int rowIndex, int columnIndex)
        {
            try
            {
                if (rowIndex < 0 || rowIndex >= grid.RowCount) return null;
                if (columnIndex < 0 || columnIndex >= grid.ColumnCount) return null;

                return grid.Rows[rowIndex].Cells[columnIndex].Value;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "GridAccessHelper.GetCellValue(grid, {Row}, {Col}) failed", rowIndex, columnIndex);
                return null;
            }
        }

        /// <summary>
        /// Checks whether a DataGridView (or one of its children) currently has focus.
        /// </summary>
        public static bool IsResultsGridFocused()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var focused = GetFocusedControl();
                while (focused != null)
                {
                    if (focused is DataGridView) return true;
                    focused = focused.Parent;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "GridAccessHelper.IsResultsGridFocused check failed");
            }

            return false;
        }

        #region Private helpers

        /// <summary>
        /// Recursively searches a control hierarchy for the first visible DataGridView.
        /// </summary>
        private static DataGridView? FindDataGridViewInControl(Control parent)
        {
            if (parent is DataGridView dgv && dgv.Visible)
            {
                return dgv;
            }

            foreach (Control child in parent.Controls)
            {
                var found = FindDataGridViewInControl(child);
                if (found != null) return found;
            }

            return null;
        }

        /// <summary>
        /// Attempts to find a DataGridView from a DTE window by using reflection to access
        /// the underlying WinForms control host.
        /// </summary>
        private static DataGridView? FindGridFromDteWindow(EnvDTE.Window window)
        {
            try
            {
                // Try to get the underlying WinForms control via reflection.
                // SSMS hosts results in a control accessible via internal properties.
                var windowObj = window.Object;
                if (windowObj == null) return null;

                // Walk known property names used by SSMS results panes
                var type = windowObj.GetType();
                var controlProp = type.GetProperty("GridControl",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (controlProp?.GetValue(windowObj) is DataGridView grid)
                {
                    return grid;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "GridAccessHelper: reflection-based grid search failed");
            }

            return null;
        }

        /// <summary>
        /// Gets the currently focused WinForms control using Win32 interop.
        /// </summary>
        private static Control? GetFocusedControl()
        {
            try
            {
                var hwnd = NativeMethods.GetFocus();
                if (hwnd == IntPtr.Zero) return null;
                return Control.FromHandle(hwnd);
            }
            catch
            {
                return null;
            }
        }

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern IntPtr GetFocus();
        }

        #endregion
    }
}
