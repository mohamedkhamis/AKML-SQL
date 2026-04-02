#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.StatusBar;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Serilog;

namespace AkmlSql.Shell.Shared.Productivity.Grid
{
    /// <summary>
    /// T023: Subscribes to DataGridView.SelectionChanged and computes aggregate values
    /// (SUM, AVG, COUNT, MIN, MAX) for the selected cells. Displays results in the VS status bar
    /// using <see cref="StatusBarManager"/>. Shows COUNT only for non-numeric selections.
    /// </summary>
    internal sealed class GridAggregatesProvider : IDisposable
    {
        private DataGridView? _attachedGrid;
        private readonly IVsStatusbar? _statusBar;
        private bool _disposed;

        /// <summary>
        /// Creates a new aggregates provider. The status bar service is resolved from the
        /// global service provider.
        /// </summary>
        public GridAggregatesProvider()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _statusBar = Package.GetGlobalService(typeof(SVsStatusbar)) as IVsStatusbar;
        }

        /// <summary>
        /// Attaches the aggregates provider to a DataGridView. Detaches from any previously
        /// attached grid first.
        /// </summary>
        public void Attach(DataGridView grid)
        {
            Detach();

            _attachedGrid = grid;
            _attachedGrid.SelectionChanged += OnSelectionChanged;

            Log.Debug("GridAggregatesProvider: attached to grid ({Rows}x{Cols})",
                grid.RowCount, grid.ColumnCount);
        }

        /// <summary>
        /// Detaches from the currently attached grid and clears the status bar.
        /// </summary>
        public void Detach()
        {
            if (_attachedGrid != null)
            {
                _attachedGrid.SelectionChanged -= OnSelectionChanged;
                _attachedGrid = null;
            }
        }

        /// <summary>
        /// Checks if the aggregates feature is enabled in settings.
        /// </summary>
        public static bool IsEnabled()
        {
            try
            {
                var settings = ConfigManager.Load();
                return settings.Grid.Aggregates;
            }
            catch
            {
                return true; // Default enabled
            }
        }

        private void OnSelectionChanged(object? sender, EventArgs e)
        {
            if (_disposed || _attachedGrid == null || _statusBar == null) return;

            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                ComputeAndDisplayAggregates();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "GridAggregatesProvider: selection changed handler failed");
            }
        }

        private void ComputeAndDisplayAggregates()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_attachedGrid == null || _statusBar == null) return;

            var selectedCells = _attachedGrid.SelectedCells;
            if (selectedCells.Count == 0)
            {
                return;
            }

            // If only one cell selected, don't show aggregates
            if (selectedCells.Count == 1)
            {
                return;
            }

            var numericValues = new List<double>();
            int totalCount = 0;
            int nullCount = 0;

            foreach (DataGridViewCell cell in selectedCells)
            {
                var value = cell.Value;
                if (value == null || value is DBNull)
                {
                    nullCount++;
                    totalCount++;
                    continue;
                }

                totalCount++;
                if (TryConvertToDouble(value, out double numericValue))
                {
                    numericValues.Add(numericValue);
                }
            }

            string statusText;
            if (numericValues.Count > 0)
            {
                statusText = FormatNumericAggregates(numericValues, totalCount, nullCount);
            }
            else
            {
                statusText = FormatCountOnly(totalCount, nullCount);
            }

            try
            {
                _statusBar.IsFrozen(out int frozen);
                if (frozen != 0)
                {
                    _statusBar.FreezeOutput(0);
                }

                _statusBar.SetText(statusText);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "GridAggregatesProvider: failed to update status bar");
            }
        }

        private static string FormatNumericAggregates(List<double> values, int totalCount, int nullCount)
        {
            double sum = 0;
            double min = double.MaxValue;
            double max = double.MinValue;

            foreach (var v in values)
            {
                sum += v;
                if (v < min) min = v;
                if (v > max) max = v;
            }

            double avg = sum / values.Count;

            var parts = new List<string>
            {
                $"Count: {totalCount}",
                $"Sum: {FormatNumber(sum)}",
                $"Avg: {FormatNumber(avg)}",
                $"Min: {FormatNumber(min)}",
                $"Max: {FormatNumber(max)}"
            };

            if (nullCount > 0)
            {
                parts.Add($"Nulls: {nullCount}");
            }

            return string.Join("  |  ", parts);
        }

        private static string FormatCountOnly(int totalCount, int nullCount)
        {
            if (nullCount > 0)
            {
                return $"Count: {totalCount}  |  Nulls: {nullCount}";
            }

            return $"Count: {totalCount}";
        }

        private static string FormatNumber(double value)
        {
            // Use a reasonable number of decimal places
            if (Math.Abs(value - Math.Truncate(value)) < 0.0001)
            {
                return value.ToString("N0", CultureInfo.CurrentCulture);
            }

            return value.ToString("N2", CultureInfo.CurrentCulture);
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
            if (str != null && double.TryParse(str, NumberStyles.Any, CultureInfo.CurrentCulture, out result))
            {
                return true;
            }

            result = 0;
            return false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Detach();
        }
    }
}
