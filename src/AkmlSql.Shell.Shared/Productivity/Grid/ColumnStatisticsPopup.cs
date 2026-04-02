#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Serilog;

namespace AkmlSql.Shell.Shared.Productivity.Grid
{
    /// <summary>
    /// T072: WinForms popup shown on column header right-click. Computes and displays
    /// column statistics: min, max, avg, distinct count, null count. Uses
    /// <see cref="GridAccessHelper"/> to read column data from the DataGridView.
    /// </summary>
    internal sealed class ColumnStatisticsPopup : Form
    {
        private ColumnStatisticsPopup()
        {
            // Programmatic layout — no Designer
        }

        /// <summary>
        /// Shows a column statistics popup for the specified column of the given DataGridView.
        /// The popup is positioned near the column header.
        /// </summary>
        /// <param name="grid">The DataGridView containing the data.</param>
        /// <param name="columnIndex">The zero-based index of the column to analyze.</param>
        /// <param name="screenLocation">Screen position (typically from mouse click) for popup placement.</param>
        public static void Show(DataGridView grid, int columnIndex, Point screenLocation)
        {
            if (grid == null || columnIndex < 0 || columnIndex >= grid.ColumnCount)
                return;

            try
            {
                var stats = ComputeStatistics(grid, columnIndex);
                var popup = new ColumnStatisticsPopup();
                popup.BuildLayout(grid.Columns[columnIndex].HeaderText ?? $"Column {columnIndex}", stats);
                popup.StartPosition = FormStartPosition.Manual;
                popup.Location = screenLocation;
                popup.Show();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ColumnStatisticsPopup: failed to show statistics for column {Col}", columnIndex);
            }
        }

        /// <summary>
        /// Hooks into a DataGridView's column header right-click to show statistics.
        /// Call once during grid attachment.
        /// </summary>
        public static void AttachToGrid(DataGridView grid)
        {
            grid.ColumnHeaderMouseClick += (sender, e) =>
            {
                if (e.Button != MouseButtons.Right) return;

                var screenPoint = grid.PointToScreen(new Point(e.X, e.Y));
                Show(grid, e.ColumnIndex, screenPoint);
            };

            Log.Debug("ColumnStatisticsPopup: attached to grid column header right-click");
        }

        private void BuildLayout(string columnName, ColumnStats stats)
        {
            Text = $"Column Statistics: {columnName}";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            ShowInTaskbar = false;
            Size = new Size(320, 280);
            TopMost = true;

            // Deactivate event closes the popup
            Deactivate += (_, __) => Close();

            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Padding = new Padding(12),
                AutoSize = false
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));

            int row = 0;

            // Header
            var headerLabel = new Label
            {
                Text = columnName,
                Font = new Font(Font.FontFamily, 10, FontStyle.Bold),
                AutoSize = true,
                Padding = new Padding(0, 0, 0, 8)
            };
            panel.SetColumnSpan(headerLabel, 2);
            panel.Controls.Add(headerLabel, 0, row++);

            // Statistics rows
            AddStatRow(panel, row++, "Total Rows:", stats.TotalCount.ToString("N0"));
            AddStatRow(panel, row++, "Distinct Values:", stats.DistinctCount.ToString("N0"));
            AddStatRow(panel, row++, "NULL Count:", stats.NullCount.ToString("N0"));

            if (stats.IsNumeric)
            {
                AddStatRow(panel, row++, "Min:", FormatNumber(stats.Min));
                AddStatRow(panel, row++, "Max:", FormatNumber(stats.Max));
                AddStatRow(panel, row++, "Average:", FormatNumber(stats.Average));
                AddStatRow(panel, row++, "Sum:", FormatNumber(stats.Sum));
            }
            else
            {
                AddStatRow(panel, row++, "Min Length:", stats.MinLength.ToString("N0"));
                AddStatRow(panel, row++, "Max Length:", stats.MaxLength.ToString("N0"));
                if (!string.IsNullOrEmpty(stats.MinString))
                    AddStatRow(panel, row++, "Min Value:", Truncate(stats.MinString, 30));
                if (!string.IsNullOrEmpty(stats.MaxString))
                    AddStatRow(panel, row++, "Max Value:", Truncate(stats.MaxString, 30));
            }

            // Adjust height based on rows
            Size = new Size(320, Math.Max(200, 60 + row * 28));

            Controls.Add(panel);
        }

        private static void AddStatRow(TableLayoutPanel panel, int row, string label, string value)
        {
            panel.RowCount = row + 1;
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var labelCtrl = new Label
            {
                Text = label,
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Padding = new Padding(0, 2, 0, 2)
            };

            var valueCtrl = new Label
            {
                Text = value,
                AutoSize = true,
                Font = new Font("Consolas", 9f),
                Padding = new Padding(0, 2, 0, 2)
            };

            panel.Controls.Add(labelCtrl, 0, row);
            panel.Controls.Add(valueCtrl, 1, row);
        }

        private static ColumnStats ComputeStatistics(DataGridView grid, int columnIndex)
        {
            var stats = new ColumnStats();
            var distinctValues = new HashSet<string>(StringComparer.Ordinal);
            var numericValues = new List<double>();
            bool hasNonNumeric = false;

            for (int row = 0; row < grid.RowCount; row++)
            {
                var cellValue = GridAccessHelper.GetCellValue(grid, row, columnIndex);
                stats.TotalCount++;

                if (cellValue == null || cellValue is DBNull)
                {
                    stats.NullCount++;
                    continue;
                }

                var strValue = cellValue.ToString() ?? string.Empty;
                distinctValues.Add(strValue);

                // Track string lengths
                if (strValue.Length < stats.MinLength || stats.MinLength == 0)
                    stats.MinLength = strValue.Length;
                if (strValue.Length > stats.MaxLength)
                    stats.MaxLength = strValue.Length;

                // Track string min/max (lexicographic)
                if (stats.MinString == null || string.Compare(strValue, stats.MinString, StringComparison.Ordinal) < 0)
                    stats.MinString = strValue;
                if (stats.MaxString == null || string.Compare(strValue, stats.MaxString, StringComparison.Ordinal) > 0)
                    stats.MaxString = strValue;

                // Try numeric
                if (TryConvertToDouble(cellValue, out double numVal))
                {
                    numericValues.Add(numVal);
                }
                else
                {
                    hasNonNumeric = true;
                }
            }

            stats.DistinctCount = distinctValues.Count;

            // Consider the column numeric only if ALL non-null values are numeric
            if (numericValues.Count > 0 && !hasNonNumeric)
            {
                stats.IsNumeric = true;
                double sum = 0;
                double min = double.MaxValue;
                double max = double.MinValue;

                foreach (var v in numericValues)
                {
                    sum += v;
                    if (v < min) min = v;
                    if (v > max) max = v;
                }

                stats.Sum = sum;
                stats.Min = min;
                stats.Max = max;
                stats.Average = numericValues.Count > 0 ? sum / numericValues.Count : 0;
            }

            return stats;
        }

        private static bool TryConvertToDouble(object value, out double result)
        {
            result = 0;
            if (value is double d) { result = d; return true; }
            if (value is float f) { result = f; return true; }
            if (value is decimal dec) { result = (double)dec; return true; }
            if (value is int i) { result = i; return true; }
            if (value is long l) { result = l; return true; }
            if (value is short s) { result = s; return true; }
            if (value is byte b) { result = b; return true; }

            var str = value.ToString();
            return str != null && double.TryParse(str, NumberStyles.Any, CultureInfo.CurrentCulture, out result);
        }

        private static string FormatNumber(double value)
        {
            if (Math.Abs(value - Math.Truncate(value)) < 0.0001)
                return value.ToString("N0", CultureInfo.CurrentCulture);
            return value.ToString("N4", CultureInfo.CurrentCulture);
        }

        private static string Truncate(string value, int maxLength)
        {
            if (value.Length <= maxLength) return value;
            return value.Substring(0, maxLength - 3) + "...";
        }

        private sealed class ColumnStats
        {
            public int TotalCount;
            public int DistinctCount;
            public int NullCount;
            public bool IsNumeric;
            public double Sum;
            public double Min;
            public double Max;
            public double Average;
            public int MinLength;
            public int MaxLength;
            public string? MinString;
            public string? MaxString;
        }
    }
}
