#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
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

        internal static string FormatAsCsv(string[] headers, List<string[]> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", headers.Select(EscapeCsvField)));
            foreach (var row in rows)
            {
                sb.AppendLine(string.Join(",", row.Select(EscapeCsvField)));
            }
            return sb.ToString();
        }

        internal static string FormatAsTsv(string[] headers, List<string[]> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join("\t", headers));
            foreach (var row in rows)
            {
                sb.AppendLine(string.Join("\t", row));
            }
            return sb.ToString();
        }

        internal static string FormatAsJson(string[] headers, List<string[]> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[");
            for (int r = 0; r < rows.Count; r++)
            {
                sb.Append("  {");
                var row = rows[r];
                for (int c = 0; c < headers.Length && c < row.Length; c++)
                {
                    if (c > 0) sb.Append(", ");
                    sb.Append('"');
                    sb.Append(EscapeJsonString(headers[c]));
                    sb.Append("\": ");
                    AppendJsonValue(sb, row[c]);
                }
                sb.Append('}');
                if (r < rows.Count - 1) sb.Append(',');
                sb.AppendLine();
            }
            sb.Append(']');
            return sb.ToString();
        }

        internal static string FormatAsXml(string[] headers, List<string[]> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.AppendLine("<rows>");
            foreach (var row in rows)
            {
                sb.Append("  <row>");
                for (int c = 0; c < headers.Length && c < row.Length; c++)
                {
                    var tag = SanitizeXmlTag(headers[c]);
                    sb.Append($"<{tag}>");
                    sb.Append(EscapeXml(row[c]));
                    sb.Append($"</{tag}>");
                }
                sb.AppendLine("</row>");
            }
            sb.Append("</rows>");
            return sb.ToString();
        }

        internal static string FormatAsHtml(string[] headers, List<string[]> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<table>");
            sb.Append("  <thead><tr>");
            foreach (var h in headers)
            {
                sb.Append($"<th>{EscapeHtml(h)}</th>");
            }
            sb.AppendLine("</tr></thead>");
            sb.AppendLine("  <tbody>");
            foreach (var row in rows)
            {
                sb.Append("    <tr>");
                for (int c = 0; c < headers.Length && c < row.Length; c++)
                {
                    sb.Append($"<td>{EscapeHtml(row[c])}</td>");
                }
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("  </tbody>");
            sb.Append("</table>");
            return sb.ToString();
        }

        internal static string FormatAsInsert(string[] headers, List<string[]> rows)
        {
            var sb = new StringBuilder();
            var columnList = string.Join(", ", headers.Select(h => $"[{h}]"));
            foreach (var row in rows)
            {
                sb.Append("INSERT INTO [TableName] (");
                sb.Append(columnList);
                sb.Append(") VALUES (");
                for (int c = 0; c < headers.Length && c < row.Length; c++)
                {
                    if (c > 0) sb.Append(", ");
                    sb.Append(QuoteSqlValue(row[c]));
                }
                sb.AppendLine(");");
            }
            return sb.ToString();
        }

        internal static string FormatAsMarkdown(string[] headers, List<string[]> rows)
        {
            var sb = new StringBuilder();
            sb.Append("| ");
            sb.Append(string.Join(" | ", headers));
            sb.AppendLine(" |");
            sb.Append("| ");
            sb.Append(string.Join(" | ", headers.Select(_ => "---")));
            sb.AppendLine(" |");
            foreach (var row in rows)
            {
                sb.Append("| ");
                var values = new string[headers.Length];
                for (int c = 0; c < headers.Length; c++)
                {
                    values[c] = c < row.Length ? row[c].Replace("|", "\\|") : "";
                }
                sb.Append(string.Join(" | ", values));
                sb.AppendLine(" |");
            }
            return sb.ToString();
        }

        #endregion

        #region Escape helpers

        private static string EscapeCsvField(string value)
        {
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
            return value;
        }

        private static string EscapeJsonString(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        private static void AppendJsonValue(StringBuilder sb, string value)
        {
            if (value == "NULL")
            {
                sb.Append("null");
                return;
            }

            // Try to detect numeric values
            if (long.TryParse(value, out var longVal))
            {
                sb.Append(longVal);
                return;
            }
            if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var dblVal))
            {
                sb.Append(dblVal.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return;
            }
            if (value is "True" or "true" or "False" or "false")
            {
                sb.Append(value.ToLowerInvariant());
                return;
            }

            sb.Append('"');
            sb.Append(EscapeJsonString(value));
            sb.Append('"');
        }

        private static string EscapeXml(string value)
        {
            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }

        private static string EscapeHtml(string value)
        {
            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }

        private static string SanitizeXmlTag(string name)
        {
            // XML tag names must start with a letter or underscore
            var sanitized = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                var ch = name[i];
                if (i == 0 && !char.IsLetter(ch) && ch != '_')
                {
                    sanitized.Append('_');
                }
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.')
                {
                    sanitized.Append(ch);
                }
                else
                {
                    sanitized.Append('_');
                }
            }
            return sanitized.Length > 0 ? sanitized.ToString() : "column";
        }

        internal static string QuoteSqlValue(string value)
        {
            if (value == "NULL") return "NULL";

            // Numeric check
            if (long.TryParse(value, out _)) return value;
            if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out _)) return value;

            // Boolean
            if (value is "True" or "true") return "1";
            if (value is "False" or "false") return "0";

            // String — use N'' for Unicode safety
            return "N'" + value.Replace("'", "''") + "'";
        }

        #endregion
    }
}
