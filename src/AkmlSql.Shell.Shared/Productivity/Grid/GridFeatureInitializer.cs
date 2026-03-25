#nullable enable
using System;
using System.Windows.Forms;
using AkmlSql.Core.Config;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Productivity.Grid
{
    /// <summary>
    /// T025/T077: Wires all grid productivity features together. Called from
    /// <c>AkmlSqlPackage.InitializeAsync()</c> (or <c>Initialize()</c> for SSMS 20).
    /// <para>
    /// Integration points in <c>AkmlSqlPackage</c>:
    /// <code>
    /// // In InitializeAsync(), after command registration:
    /// GridCommands.Initialize(commandService);
    /// GridFeatureInitializer.Initialize();
    /// </code>
    /// </para>
    /// <para>
    /// Grid features monitor for new results grids and attach/detach automatically.
    /// A polling timer periodically checks for new DataGridView instances, since SSMS
    /// creates them dynamically when queries execute.
    /// </para>
    /// </summary>
    internal static class GridFeatureInitializer
    {
        private static GridAggregatesProvider? _aggregatesProvider;
        private static NullHighlighter? _nullHighlighter;
        private static RowNumberProvider? _rowNumberProvider;
        private static Timer? _gridMonitorTimer;
        private static DataGridView? _lastKnownGrid;
        private static bool _initialized;

        /// <summary>
        /// Initializes all grid productivity features. Safe to call multiple times;
        /// only the first call has effect.
        /// </summary>
        public static void Initialize()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_initialized) return;
            _initialized = true;

            try
            {
                var settings = ConfigManager.Load();

                // Create feature providers (they only attach when a grid is found)
                if (settings.Grid.Aggregates)
                {
                    _aggregatesProvider = new GridAggregatesProvider();
                    Log.Debug("GridFeatureInitializer: aggregates provider created");
                }

                if (settings.Grid.NullHighlight)
                {
                    _nullHighlighter = new NullHighlighter();
                    Log.Debug("GridFeatureInitializer: null highlighter created");
                }

                if (settings.Grid.RowNumbers)
                {
                    _rowNumberProvider = new RowNumberProvider();
                    Log.Debug("GridFeatureInitializer: row number provider created");
                }

                // Start polling timer to detect new results grids
                // Check every 2 seconds — lightweight since it only calls GetActiveResultsGrid()
                _gridMonitorTimer = new Timer { Interval = 2000 };
                _gridMonitorTimer.Tick += OnGridMonitorTick;
                _gridMonitorTimer.Start();

                Log.Information("GridFeatureInitializer: initialized (aggregates={Agg}, nullHighlight={Null}, rowNumbers={Row})",
                    settings.Grid.Aggregates, settings.Grid.NullHighlight, settings.Grid.RowNumbers);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "GridFeatureInitializer: initialization failed");
            }
        }

        /// <summary>
        /// Shuts down all grid features and disposes resources. Called from package Dispose.
        /// </summary>
        public static void Shutdown()
        {
            _gridMonitorTimer?.Stop();
            _gridMonitorTimer?.Dispose();
            _gridMonitorTimer = null;

            _aggregatesProvider?.Dispose();
            _aggregatesProvider = null;

            _nullHighlighter?.Dispose();
            _nullHighlighter = null;

            _rowNumberProvider?.Dispose();
            _rowNumberProvider = null;

            _lastKnownGrid = null;
            _initialized = false;

            Log.Debug("GridFeatureInitializer: shut down");
        }

        /// <summary>
        /// Timer callback that checks for new results grids and attaches features.
        /// </summary>
        private static void OnGridMonitorTick(object? sender, EventArgs e)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                var grid = GridAccessHelper.GetActiveResultsGrid();
                if (grid == null || grid == _lastKnownGrid) return;

                // New grid detected — attach features
                _lastKnownGrid = grid;
                AttachToGrid(grid);
            }
            catch (Exception ex)
            {
                // Don't log every tick failure — just Debug level
                Log.Debug(ex, "GridFeatureInitializer: grid monitor tick failed");
            }
        }

        /// <summary>
        /// Attaches all enabled features to a newly discovered DataGridView.
        /// </summary>
        private static void AttachToGrid(DataGridView grid)
        {
            Log.Debug("GridFeatureInitializer: attaching features to new grid ({Rows}x{Cols})",
                grid.RowCount, grid.ColumnCount);

            // T023: Aggregates
            _aggregatesProvider?.Attach(grid);

            // T074: Null highlighting
            _nullHighlighter?.Attach(grid);

            // T075: Row numbers
            _rowNumberProvider?.Attach(grid);

            // T072: Column statistics on header right-click
            ColumnStatisticsPopup.AttachToGrid(grid);

            // T073: Transpose view on row header double-click
            TransposeResultsView.AttachToGrid(grid);

            // T076: Cell edit on double-click (with Ctrl held)
            AttachCellEditHandler(grid);
        }

        /// <summary>
        /// Attaches a cell double-click handler that shows the edit dialog when Ctrl is held.
        /// </summary>
        private static void AttachCellEditHandler(DataGridView grid)
        {
            grid.CellDoubleClick += (sender, e) =>
            {
                try
                {
                    if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

                    // Only show edit dialog on Ctrl+DoubleClick
                    if ((Control.ModifierKeys & Keys.Control) != Keys.Control) return;

                    CellEditDialog.ShowForCell(grid, e.RowIndex, e.ColumnIndex);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "GridFeatureInitializer: cell edit handler failed");
                }
            };
        }
    }
}
