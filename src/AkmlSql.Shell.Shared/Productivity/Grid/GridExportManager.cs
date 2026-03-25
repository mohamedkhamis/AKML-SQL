#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Models.Productivity;
using AkmlSql.Shell.Shared.Ipc;
using Microsoft.VisualStudio.Shell;
using Serilog;
using Task = System.Threading.Tasks.Task;

namespace AkmlSql.Shell.Shared.Productivity.Grid
{
    /// <summary>
    /// T028: "Export to File" functionality for the SSMS/VS results grid.
    /// For CSV/JSON/XML/MD/SQL: generates directly in the shell process.
    /// For XLSX: sends GridExportRequest to the engine (ClosedXML).
    /// Shows a SaveFileDialog with format filter, and a progress dialog for large datasets.
    /// </summary>
    internal static class GridExportManager
    {
        /// <summary>
        /// Shows the export file dialog and exports selected data from the active results grid.
        /// Must be called on the UI thread.
        /// </summary>
        public static void ExportToFile()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var grid = GridAccessHelper.GetActiveResultsGrid();
                if (grid == null)
                {
                    MessageBox.Show("No active results grid found.", "AKML SQL",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var (headers, rows) = GridCopyAsMenu.ExtractSelectedData(grid);
                if (headers.Length == 0 || rows.Count == 0)
                {
                    // If nothing selected, export all rows
                    var allHeaders = new string[grid.ColumnCount];
                    for (int c = 0; c < grid.ColumnCount; c++)
                        allHeaders[c] = grid.Columns[c].HeaderText ?? $"Column{c}";

                    var allRows = new List<string[]>();
                    for (int r = 0; r < grid.RowCount; r++)
                    {
                        if (grid.Rows[r].IsNewRow) continue;
                        var row = new string[grid.ColumnCount];
                        for (int c = 0; c < grid.ColumnCount; c++)
                        {
                            var val = grid.Rows[r].Cells[c].Value;
                            row[c] = val == null || val == DBNull.Value ? "NULL" : val.ToString() ?? "";
                        }
                        allRows.Add(row);
                    }
                    headers = allHeaders;
                    rows = allRows;
                }

                if (rows.Count == 0)
                {
                    MessageBox.Show("No data to export.", "AKML SQL",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Show SaveFileDialog
                using var dialog = new SaveFileDialog
                {
                    Title = "Export Grid Data",
                    Filter = "CSV files (*.csv)|*.csv|" +
                             "TSV files (*.tsv)|*.tsv|" +
                             "JSON files (*.json)|*.json|" +
                             "XML files (*.xml)|*.xml|" +
                             "Excel files (*.xlsx)|*.xlsx|" +
                             "HTML files (*.html)|*.html|" +
                             "SQL INSERT files (*.sql)|*.sql|" +
                             "Markdown files (*.md)|*.md",
                    FilterIndex = 1,
                    DefaultExt = "csv",
                    OverwritePrompt = true
                };

                if (dialog.ShowDialog() != DialogResult.OK)
                    return;

                var format = GetFormatFromFilterIndex(dialog.FilterIndex);
                var outputPath = dialog.FileName;

                // Get column types from the grid if available
                var columnTypes = GetColumnTypes(grid);

                if (format == GridExportFormat.Xlsx)
                {
                    // XLSX export goes to the engine
                    ExportViaEngine(outputPath, headers, rows, columnTypes);
                }
                else
                {
                    // All other formats are generated directly in the shell
                    ExportLocally(outputPath, headers, rows, format);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "GridExportManager: export failed");
                MessageBox.Show($"Export failed: {ex.Message}", "AKML SQL",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static GridExportFormat GetFormatFromFilterIndex(int filterIndex)
        {
            return filterIndex switch
            {
                1 => GridExportFormat.Csv,
                2 => GridExportFormat.Tsv,
                3 => GridExportFormat.Json,
                4 => GridExportFormat.Xml,
                5 => GridExportFormat.Xlsx,
                6 => GridExportFormat.Html,
                7 => GridExportFormat.SqlInsert,
                8 => GridExportFormat.Markdown,
                _ => GridExportFormat.Csv
            };
        }

        /// <summary>
        /// Generates the file locally for text-based formats.
        /// </summary>
        private static void ExportLocally(string outputPath, string[] headers,
            List<string[]> rows, GridExportFormat format)
        {
            try
            {
                string content = format switch
                {
                    GridExportFormat.Csv => GridCopyAsMenu.FormatAsCsv(headers, rows),
                    GridExportFormat.Tsv => GridCopyAsMenu.FormatAsTsv(headers, rows),
                    GridExportFormat.Json => GridCopyAsMenu.FormatAsJson(headers, rows),
                    GridExportFormat.Xml => GridCopyAsMenu.FormatAsXml(headers, rows),
                    GridExportFormat.Html => GridCopyAsMenu.FormatAsHtml(headers, rows),
                    GridExportFormat.SqlInsert => GridCopyAsMenu.FormatAsInsert(headers, rows),
                    GridExportFormat.Markdown => GridCopyAsMenu.FormatAsMarkdown(headers, rows),
                    _ => GridCopyAsMenu.FormatAsCsv(headers, rows)
                };

                File.WriteAllText(outputPath, content, Encoding.UTF8);

                Log.Information("GridExportManager: exported {RowCount} rows as {Format} to {Path}",
                    rows.Count, format, outputPath);

                MessageBox.Show($"Exported {rows.Count} rows to:\n{outputPath}",
                    "AKML SQL - Export Complete",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "GridExportManager: local export failed");
                MessageBox.Show($"Export failed: {ex.Message}", "AKML SQL",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Sends a GridExportRequest to the engine for XLSX generation.
        /// </summary>
        private static void ExportViaEngine(string outputPath, string[] headers,
            List<string[]> rows, string[] columnTypes)
        {
            var manager = EngineLifecycle.Manager;
            if (manager?.Client == null || !manager.Client.IsConnected)
            {
                MessageBox.Show("Engine is not running. Cannot export to XLSX.\nPlease try a different format.",
                    "AKML SQL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Show a progress message for large datasets
            var rowCount = rows.Count;
            if (rowCount > 10000)
            {
                Log.Information("GridExportManager: exporting {RowCount} rows to XLSX via engine", rowCount);
            }

            var request = new GridExportRequest
            {
                OutputPath = outputPath,
                ColumnHeaders = headers,
                Rows = rows.ToArray(),
                ColumnTypes = columnTypes,
                Format = (int)GridExportFormat.Xlsx
            };

            // Fire async request; handle result on completion
            _ = Task.Run(async () =>
            {
                try
                {
                    var response = await manager.Client.SendRequestAsync<GridExportResponse, GridExportRequest>(
                        MessageTypes.GridExport, request, timeoutMs: 60000);

                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    if (response.Success)
                    {
                        MessageBox.Show($"Exported {response.RowCount} rows to:\n{response.OutputPath}",
                            "AKML SQL - Export Complete",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        MessageBox.Show($"XLSX export failed: {response.Error}",
                            "AKML SQL", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "GridExportManager: XLSX export via engine failed");

                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    MessageBox.Show($"XLSX export failed: {ex.Message}",
                        "AKML SQL", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            });
        }

        /// <summary>
        /// Attempts to determine column types from the DataGridView's data source.
        /// Falls back to empty strings if type information is not available.
        /// </summary>
        private static string[] GetColumnTypes(DataGridView grid)
        {
            var types = new string[grid.ColumnCount];
            try
            {
                for (int c = 0; c < grid.ColumnCount; c++)
                {
                    var col = grid.Columns[c];
                    // Try to get the underlying data type from the column's ValueType
                    if (col.ValueType != null)
                    {
                        types[c] = MapClrTypeToSqlType(col.ValueType);
                    }
                    else
                    {
                        types[c] = "nvarchar";
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "GridExportManager: failed to detect column types");
                for (int c = 0; c < types.Length; c++)
                    types[c] = "nvarchar";
            }
            return types;
        }

        private static string MapClrTypeToSqlType(Type clrType)
        {
            if (clrType == typeof(int) || clrType == typeof(int?)) return "int";
            if (clrType == typeof(long) || clrType == typeof(long?)) return "bigint";
            if (clrType == typeof(short) || clrType == typeof(short?)) return "smallint";
            if (clrType == typeof(byte) || clrType == typeof(byte?)) return "tinyint";
            if (clrType == typeof(decimal) || clrType == typeof(decimal?)) return "decimal";
            if (clrType == typeof(double) || clrType == typeof(double?)) return "float";
            if (clrType == typeof(float) || clrType == typeof(float?)) return "real";
            if (clrType == typeof(bool) || clrType == typeof(bool?)) return "bit";
            if (clrType == typeof(DateTime) || clrType == typeof(DateTime?)) return "datetime";
            if (clrType == typeof(DateTimeOffset) || clrType == typeof(DateTimeOffset?)) return "datetimeoffset";
            if (clrType == typeof(TimeSpan) || clrType == typeof(TimeSpan?)) return "time";
            if (clrType == typeof(Guid) || clrType == typeof(Guid?)) return "uniqueidentifier";
            if (clrType == typeof(byte[])) return "varbinary";
            return "nvarchar";
        }
    }
}
