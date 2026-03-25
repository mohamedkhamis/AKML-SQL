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
    /// T031: Generates SQL scripts (INSERT, UPDATE, DELETE) from selected grid rows.
    /// Opens the generated SQL in a new editor tab via DTE.
    /// </summary>
    internal static class GridScriptGenerator
    {
        /// <summary>Script generation mode.</summary>
        public enum ScriptMode
        {
            Insert,
            Update,
            Delete
        }

        /// <summary>
        /// Generates a SQL script from selected grid rows and opens it in a new editor tab.
        /// Must be called on the UI thread.
        /// </summary>
        public static void GenerateScript(ScriptMode mode)
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
                    MessageBox.Show("Please select rows to generate a script.", "AKML SQL",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Try to detect primary key columns for UPDATE/DELETE WHERE clauses
                var pkColumns = DetectPrimaryKeyColumns(grid);

                string sql = mode switch
                {
                    ScriptMode.Insert => GenerateInsertScript(headers, rows),
                    ScriptMode.Update => GenerateUpdateScript(headers, rows, pkColumns),
                    ScriptMode.Delete => GenerateDeleteScript(headers, rows, pkColumns),
                    _ => throw new ArgumentOutOfRangeException(nameof(mode))
                };

                if (string.IsNullOrWhiteSpace(sql))
                {
                    MessageBox.Show("No script was generated.", "AKML SQL",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                OpenInNewEditorTab(sql);

                Log.Information("GridScriptGenerator: generated {Mode} script for {RowCount} rows",
                    mode, rows.Count);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "GridScriptGenerator: script generation failed for mode {Mode}", mode);
                MessageBox.Show($"Script generation failed: {ex.Message}", "AKML SQL",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #region Script generators

        private static string GenerateInsertScript(string[] headers, List<string[]> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("-- Generated INSERT statements");
            sb.AppendLine("-- TODO: Replace [TableName] with the actual table name");
            sb.AppendLine();

            var columnList = string.Join(", ", headers.Select(h => $"[{h}]"));

            foreach (var row in rows)
            {
                sb.Append("INSERT INTO [TableName] (");
                sb.Append(columnList);
                sb.AppendLine(")");
                sb.Append("VALUES (");
                for (int c = 0; c < headers.Length && c < row.Length; c++)
                {
                    if (c > 0) sb.Append(", ");
                    sb.Append(GridCopyAsMenu.QuoteSqlValue(row[c]));
                }
                sb.AppendLine(");");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string GenerateUpdateScript(string[] headers, List<string[]> rows,
            List<int> pkColumns)
        {
            var sb = new StringBuilder();
            sb.AppendLine("-- Generated UPDATE statements");
            sb.AppendLine("-- TODO: Replace [TableName] with the actual table name");

            if (pkColumns.Count == 0)
            {
                sb.AppendLine("-- WARNING: No primary key detected. Using first column for WHERE clause.");
                sb.AppendLine("--          Please verify the WHERE clause is correct before executing.");
                pkColumns = new List<int> { 0 };
            }

            sb.AppendLine();

            // Non-PK columns are the SET columns
            var setColumns = Enumerable.Range(0, headers.Length)
                .Where(i => !pkColumns.Contains(i))
                .ToList();

            if (setColumns.Count == 0)
            {
                sb.AppendLine("-- ERROR: All columns appear to be primary key columns. Nothing to UPDATE.");
                return sb.ToString();
            }

            foreach (var row in rows)
            {
                sb.Append("UPDATE [TableName]");
                sb.AppendLine();
                sb.Append("SET ");

                bool firstSet = true;
                foreach (var colIdx in setColumns)
                {
                    if (colIdx >= row.Length) continue;
                    if (!firstSet)
                    {
                        sb.AppendLine(",");
                        sb.Append("    ");
                    }
                    sb.Append($"[{headers[colIdx]}] = {GridCopyAsMenu.QuoteSqlValue(row[colIdx])}");
                    firstSet = false;
                }

                sb.AppendLine();
                sb.Append("WHERE ");

                bool firstWhere = true;
                foreach (var pkIdx in pkColumns)
                {
                    if (pkIdx >= row.Length || pkIdx >= headers.Length) continue;
                    if (!firstWhere) sb.Append(" AND ");
                    sb.Append($"[{headers[pkIdx]}] = {GridCopyAsMenu.QuoteSqlValue(row[pkIdx])}");
                    firstWhere = false;
                }

                sb.AppendLine(";");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string GenerateDeleteScript(string[] headers, List<string[]> rows,
            List<int> pkColumns)
        {
            var sb = new StringBuilder();
            sb.AppendLine("-- Generated DELETE statements");
            sb.AppendLine("-- TODO: Replace [TableName] with the actual table name");

            if (pkColumns.Count == 0)
            {
                sb.AppendLine("-- WARNING: No primary key detected. Using first column for WHERE clause.");
                sb.AppendLine("--          Please verify the WHERE clause is correct before executing.");
                pkColumns = new List<int> { 0 };
            }

            sb.AppendLine();

            foreach (var row in rows)
            {
                sb.Append("DELETE FROM [TableName]");
                sb.AppendLine();
                sb.Append("WHERE ");

                bool firstWhere = true;
                foreach (var pkIdx in pkColumns)
                {
                    if (pkIdx >= row.Length || pkIdx >= headers.Length) continue;
                    if (!firstWhere) sb.Append(" AND ");
                    sb.Append($"[{headers[pkIdx]}] = {GridCopyAsMenu.QuoteSqlValue(row[pkIdx])}");
                    firstWhere = false;
                }

                sb.AppendLine(";");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        #endregion

        #region PK detection

        /// <summary>
        /// Attempts to detect primary key columns from the DataGridView.
        /// Uses heuristics: columns named "Id", "ID", ending with "ID" or "Id",
        /// or marked as read-only (common for identity columns).
        /// Returns column indices that are likely PKs.
        /// </summary>
        private static List<int> DetectPrimaryKeyColumns(DataGridView grid)
        {
            var pkCandidates = new List<int>();

            try
            {
                for (int c = 0; c < grid.ColumnCount; c++)
                {
                    var name = grid.Columns[c].HeaderText ?? "";
                    var nameLower = name.ToLowerInvariant();

                    // Common PK naming patterns
                    if (nameLower == "id" ||
                        nameLower.EndsWith("id") ||
                        nameLower.EndsWith("_id") ||
                        nameLower.EndsWith("key") ||
                        nameLower.EndsWith("_key"))
                    {
                        pkCandidates.Add(c);
                    }
                }

                // If we found exactly one "id" column, use it
                if (pkCandidates.Count == 1)
                    return pkCandidates;

                // If we found none or too many, try read-only columns (identity columns)
                if (pkCandidates.Count == 0)
                {
                    for (int c = 0; c < grid.ColumnCount; c++)
                    {
                        if (grid.Columns[c].ReadOnly)
                        {
                            pkCandidates.Add(c);
                            break; // Use only the first read-only column
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "GridScriptGenerator: PK detection failed");
            }

            return pkCandidates;
        }

        #endregion

        #region DTE helper

        /// <summary>
        /// Opens the generated SQL text in a new SSMS/VS editor tab.
        /// </summary>
        private static void OpenInNewEditorTab(string sql)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                if (dte == null)
                {
                    Log.Warning("GridScriptGenerator: DTE not available, copying to clipboard instead");
                    Clipboard.SetText(sql);
                    MessageBox.Show("Script copied to clipboard (editor not available).", "AKML SQL",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                // Create a new SQL file
                dte.ItemOperations.NewFile("General\\Text File", "GeneratedScript.sql");

                var doc = dte.ActiveDocument;
                if (doc != null)
                {
                    var textDoc = doc.Object("TextDocument") as EnvDTE.TextDocument;
                    if (textDoc != null)
                    {
                        var editPoint = textDoc.StartPoint.CreateEditPoint();
                        editPoint.Insert(sql);
                        Log.Debug("GridScriptGenerator: opened generated SQL in new editor tab");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "GridScriptGenerator: failed to open in editor, copying to clipboard");
                Clipboard.SetText(sql);
                MessageBox.Show("Script copied to clipboard (failed to open editor tab).", "AKML SQL",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        #endregion
    }
}
