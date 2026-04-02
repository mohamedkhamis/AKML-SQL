#nullable enable
using System;
using System.Drawing;
using System.Windows.Forms;
using AkmlSql.Core.Config;
using Serilog;

namespace AkmlSql.Shell.Shared.Productivity.Grid
{
    /// <summary>
    /// T074: Hooks DataGridView.CellFormatting to display DBNull/null values as italic "NULL"
    /// with a gray background. Guarded by <see cref="GridSettings.NullHighlight"/>.
    /// </summary>
    internal sealed class NullHighlighter : IDisposable
    {
        private DataGridView? _attachedGrid;
        private bool _disposed;

        private static readonly Color NullBackgroundColor = Color.FromArgb(245, 245, 245);
        private static readonly Color NullForegroundColor = Color.Gray;
        private static Font? _nullFont;

        /// <summary>
        /// Checks if the null highlighting feature is enabled in settings.
        /// </summary>
        public static bool IsEnabled()
        {
            try
            {
                var settings = ConfigManager.Load();
                return settings.Grid.NullHighlight;
            }
            catch
            {
                return true; // Default enabled
            }
        }

        /// <summary>
        /// Attaches the null highlighter to a DataGridView. Detaches from any previously
        /// attached grid first.
        /// </summary>
        public void Attach(DataGridView grid)
        {
            Detach();

            _attachedGrid = grid;
            _attachedGrid.CellFormatting += OnCellFormatting;

            // Force a repaint so existing cells get styled
            _attachedGrid.Invalidate();

            Log.Debug("NullHighlighter: attached to grid ({Rows}x{Cols})",
                grid.RowCount, grid.ColumnCount);
        }

        /// <summary>
        /// Detaches from the currently attached grid.
        /// </summary>
        public void Detach()
        {
            if (_attachedGrid != null)
            {
                _attachedGrid.CellFormatting -= OnCellFormatting;
                _attachedGrid.Invalidate(); // Repaint to clear styles
                _attachedGrid = null;
            }
        }

        private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (_disposed || _attachedGrid == null) return;

            try
            {
                if (e.RowIndex < 0 || e.RowIndex >= _attachedGrid.RowCount) return;
                if (e.ColumnIndex < 0 || e.ColumnIndex >= _attachedGrid.ColumnCount) return;

                var cellValue = _attachedGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value;

                if (cellValue == null || cellValue is DBNull)
                {
                    // Display "NULL" in italic gray
                    e.Value = "NULL";
                    e.CellStyle ??= new DataGridViewCellStyle();
                    e.CellStyle.BackColor = NullBackgroundColor;
                    e.CellStyle.ForeColor = NullForegroundColor;
                    e.CellStyle.Font = GetNullFont(e.CellStyle.Font);
                    e.FormattingApplied = true;
                }
            }
            catch (Exception ex)
            {
                // CellFormatting fires very frequently; log at Debug only
                Log.Debug(ex, "NullHighlighter: CellFormatting handler failed");
            }
        }

        /// <summary>
        /// Creates or returns a cached italic version of the base font for null display.
        /// </summary>
        private static Font GetNullFont(Font? baseFont)
        {
            if (_nullFont != null) return _nullFont;

            try
            {
                var family = baseFont?.FontFamily ?? FontFamily.GenericMonospace;
                var size = baseFont?.Size ?? 9f;
                _nullFont = new Font(family, size, FontStyle.Italic);
            }
            catch
            {
                _nullFont = new Font("Consolas", 9f, FontStyle.Italic);
            }

            return _nullFont;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Detach();
        }
    }
}
