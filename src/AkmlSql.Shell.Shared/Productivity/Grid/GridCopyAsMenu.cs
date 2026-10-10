#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using AkmlSql.Core.Text;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Productivity.Grid
{
    /// <summary>
    /// T027: Registers a "Copy As" context menu on the SSMS/VS DataGridView results grid.
    /// Provides formatters for CSV, TSV, JSON, XML, HTML, and INSERT statements.
    /// All operations copy formatted data to the clipboard.
    /// </summary>
    internal static class GridCopyAsMenu
    {
        private static bool _initialized;
        private static ContextMenuStrip? _contextMenu;

        /// <summary>
        /// Attaches the "Copy As" context menu to the active results grid.
        /// Safe to call multiple times; only the first invocation creates the menu.
        /// Must be called on the UI thread.
        /// </summary>
        public static void EnsureAttached()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_initialized) return;
            _initialized = true;

            try
            {
                _contextMenu = CreateContextMenu();
                AttachToGrids();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "GridCopyAsMenu: failed to attach context menu");
            }
        }

        /// <summary>
        /// Attempts to attach the context menu to the currently active results grid.
        /// Called when a new results grid appears.
        /// </summary>
        public static void AttachToGrids()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_contextMenu == null) return;

            try
            {
                var grid = GridAccessHelper.GetActiveResultsGrid();
                if (grid != null && grid.ContextMenuStrip != _contextMenu)
                {
                    // Preserve any existing context menu items by merging
                    if (grid.ContextMenuStrip != null)
                    {
                        foreach (ToolStripItem item in _contextMenu.Items)
                        {
                            if (!grid.ContextMenuStrip.Items.ContainsKey(item.Name))
                            {
                                grid.ContextMenuStrip.Items.Add(item);
                            }
                        }
                    }
                    else
                    {
                        grid.ContextMenuStrip = _contextMenu;
                    }

                    Log.Debug("GridCopyAsMenu: attached to results grid");
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "GridCopyAsMenu: failed to attach to grid");
            }
        }

        #region Context menu creation

        private static ContextMenuStrip CreateContextMenu()
        {
            var menu = new ContextMenuStrip();

            var copyAsMenu = new ToolStripMenuItem("Copy As") { Name = "AkmlCopyAs" };
            copyAsMenu.DropDownItems.Add(CreateMenuItem("CSV", CopyAsCsv));
            copyAsMenu.DropDownItems.Add(CreateMenuItem("TSV", CopyAsTsv));
            copyAsMenu.DropDownItems.Add(new ToolStripSeparator());
            copyAsMenu.DropDownItems.Add(CreateMenuItem("JSON", CopyAsJson));
            copyAsMenu.DropDownItems.Add(CreateMenuItem("XML", CopyAsXml));
            copyAsMenu.DropDownItems.Add(CreateMenuItem("HTML Table", CopyAsHtml));
            copyAsMenu.DropDownItems.Add(new ToolStripSeparator());
            copyAsMenu.DropDownItems.Add(CreateMenuItem("INSERT Statements", CopyAsInsert));
            copyAsMenu.DropDownItems.Add(CreateMenuItem("IN Clause", CopyAsInClause));
            copyAsMenu.DropDownItems.Add(CreateMenuItem("Markdown Table", CopyAsMarkdown));

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(copyAsMenu);

            return menu;
        }

        private static ToolStripMenuItem CreateMenuItem(string text, EventHandler handler)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += handler;
            return item;
        }

        #endregion

        #region Format handlers

        private static void CopyAsCsv(object? sender, EventArgs e)
        {
            CopyFormattedData(FormatAsCsv);
        }

        private static void CopyAsTsv(object? sender, EventArgs e)
        {
            CopyFormattedData(FormatAsTsv);
        }

        private static void CopyAsJson(object? sender, EventArgs e)
        {
            CopyFormattedData(FormatAsJson);
        }

        private static void CopyAsXml(object? sender, EventArgs e)
        {
            CopyFormattedData(FormatAsXml);
        }

        private static void CopyAsHtml(object? sender, EventArgs e)
        {
            CopyFormattedData(FormatAsHtml);
        }

        private static void CopyAsInsert(object? sender, EventArgs e)
        {
            CopyFormattedData(FormatAsInsert);
        }

        private static void CopyAsInClause(object? sender, EventArgs e)
        {
            CopyFormattedData(FormatAsInClause);
        }

        private static void CopyAsMarkdown(object? sender, EventArgs e)
        {
            CopyFormattedData(FormatAsMarkdown);
        }

        #endregion

        #region Core copy logic

        private static void CopyFormattedData(Func<string[], List<string[]>, string> formatter)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var grid = GridAccessHelper.GetActiveResultsGrid();
                if (grid == null)
                {
                    Log.Debug("GridCopyAsMenu: no active grid found");
                    return;
                }

                var (headers, rows) = ExtractSelectedData(grid);
                if (headers.Length == 0 || rows.Count == 0)
                {
                    Log.Debug("GridCopyAsMenu: no data selected");
                    return;
                }

                var formatted = formatter(headers, rows);
                if (!string.IsNullOrEmpty(formatted))
                {
                    Clipboard.SetText(formatted);
                    Log.Debug("GridCopyAsMenu: copied {RowCount} rows to clipboard", rows.Count);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "GridCopyAsMenu: failed to copy formatted data");
            }
        }

        /// <summary>
        /// Extracts headers and row data from selected cells in the grid.
        /// If full rows are selected, uses all columns. Otherwise uses only selected columns.
        /// </summary>
        internal static (string[] Headers, List<string[]> Rows) ExtractSelectedData(DataGridView grid)
        {
            if (grid.SelectedCells.Count == 0 && grid.SelectedRows.Count == 0)
                return (Array.Empty<string>(), new List<string[]>());

            // Determine which rows and columns are selected
            var selectedRowIndices = new SortedSet<int>();
            var selectedColIndices = new SortedSet<int>();

            if (grid.SelectedRows.Count > 0)
            {
                // Full row selection — use all columns
                foreach (DataGridViewRow row in grid.SelectedRows)
                {
                    if (!row.IsNewRow)
                        selectedRowIndices.Add(row.Index);
                }
                for (int c = 0; c < grid.ColumnCount; c++)
                    selectedColIndices.Add(c);
            }
            else
            {
                // Individual cell selection
                foreach (DataGridViewCell cell in grid.SelectedCells)
                {
                    selectedRowIndices.Add(cell.RowIndex);
                    selectedColIndices.Add(cell.ColumnIndex);
                }
            }

            var colIndices = selectedColIndices.ToArray();
            var headers = colIndices.Select(c => grid.Columns[c].HeaderText ?? $"Column{c}").ToArray();

            var rows = new List<string[]>();
            foreach (var rowIdx in selectedRowIndices)
            {
                if (rowIdx < 0 || rowIdx >= grid.RowCount) continue;
                var row = new string[colIndices.Length];
                for (int i = 0; i < colIndices.Length; i++)
                {
                    var val = grid.Rows[rowIdx].Cells[colIndices[i]].Value;
                    row[i] = val == null || val == DBNull.Value ? "NULL" : val.ToString() ?? "";
                }
                rows.Add(row);
            }

            return (headers, rows);
        }

        #endregion

        #region Formatters

        // Spec 041 (research R5): the formatter bodies live in AkmlSql.Core.Text.GridTextFormats, shared
        // with the web edition's results grid. These delegations keep every caller (the Copy As menu,
        // GridExportManager, GridScriptGenerator) unchanged; GridTextFormatsTests pins the output to the
        // goldens captured from the code that used to be here.

        internal static string FormatAsCsv(string[] headers, List<string[]> rows) => GridTextFormats.Csv(headers, rows);

        internal static string FormatAsTsv(string[] headers, List<string[]> rows) => GridTextFormats.Tsv(headers, rows);

        internal static string FormatAsJson(string[] headers, List<string[]> rows) => GridTextFormats.Json(headers, rows);

        internal static string FormatAsXml(string[] headers, List<string[]> rows) => GridTextFormats.Xml(headers, rows);

        internal static string FormatAsHtml(string[] headers, List<string[]> rows) => GridTextFormats.Html(headers, rows);

        internal static string FormatAsInsert(string[] headers, List<string[]> rows) => GridTextFormats.Insert(headers, rows);

        internal static string FormatAsInClause(string[] headers, List<string[]> rows) => GridTextFormats.InClause(headers, rows);

        internal static string FormatAsMarkdown(string[] headers, List<string[]> rows) => GridTextFormats.Markdown(headers, rows);

        internal static string QuoteSqlValue(string value) => GridTextFormats.QuoteSqlValue(value);

        #endregion
    }
}
