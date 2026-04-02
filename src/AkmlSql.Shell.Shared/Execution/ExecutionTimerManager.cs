#nullable enable
using System;
using System.Diagnostics;
using System.Windows.Threading;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Serilog;

namespace AkmlSql.Shell.Shared.Execution
{
    /// <summary>
    /// T082: Manages a 1-second timer that updates the VS status bar with elapsed execution time
    /// while a query is running. Shows format "Executing... 0:00:05".
    /// </summary>
    internal sealed class ExecutionTimerManager : IDisposable
    {
        private static ExecutionTimerManager? _instance;

        private readonly IVsStatusbar? _statusBar;
        private readonly DispatcherTimer _timer;
        private readonly Stopwatch _stopwatch;
        private bool _isRunning;

        /// <summary>
        /// Gets the singleton instance. Returns null if not yet initialized.
        /// </summary>
        public static ExecutionTimerManager? Instance => _instance;

        private ExecutionTimerManager(IVsStatusbar? statusBar)
        {
            _statusBar = statusBar;
            _stopwatch = new Stopwatch();
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += OnTimerTick;
        }

        /// <summary>
        /// Initializes the singleton timer manager.
        /// Must be called on the UI thread during package initialization.
        /// </summary>
        public static void Initialize()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_instance != null) return;

            var statusBar = (IVsStatusbar?)Package.GetGlobalService(typeof(SVsStatusbar));
            _instance = new ExecutionTimerManager(statusBar);
            Log.Debug("ExecutionTimerManager: initialized");
        }

        /// <summary>
        /// Starts the execution timer. Call when a query begins executing.
        /// Thread-safe — switches to UI thread internally.
        /// </summary>
        public void Start()
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                StartInternal();
            });
        }

        /// <summary>
        /// Starts the execution timer on the UI thread.
        /// </summary>
        public void StartOnUIThread()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            StartInternal();
        }

        private void StartInternal()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            _stopwatch.Restart();
            _isRunning = true;
            _timer.Start();
            UpdateStatusBar();
            Log.Debug("ExecutionTimerManager: timer started");
        }

        /// <summary>
        /// Stops the execution timer and returns the total elapsed time.
        /// Thread-safe — switches to UI thread internally.
        /// </summary>
        /// <returns>Total elapsed time.</returns>
        public TimeSpan Stop()
        {
            _timer.Stop();
            _stopwatch.Stop();
            _isRunning = false;

            var elapsed = _stopwatch.Elapsed;

            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                RestoreStatusBar(elapsed);
            });

            Log.Debug("ExecutionTimerManager: timer stopped at {Elapsed}", elapsed);
            return elapsed;
        }

        /// <summary>
        /// Stops the execution timer on the UI thread and returns elapsed time.
        /// </summary>
        public TimeSpan StopOnUIThread()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            _timer.Stop();
            _stopwatch.Stop();
            _isRunning = false;

            var elapsed = _stopwatch.Elapsed;
            RestoreStatusBar(elapsed);

            Log.Debug("ExecutionTimerManager: timer stopped at {Elapsed}", elapsed);
            return elapsed;
        }

        /// <summary>
        /// Gets the current elapsed time without stopping the timer.
        /// </summary>
        public TimeSpan Elapsed => _stopwatch.Elapsed;

        /// <summary>
        /// Gets whether the timer is currently running.
        /// </summary>
        public bool IsRunning => _isRunning;

        private void OnTimerTick(object? sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            UpdateStatusBar();
        }

        private void UpdateStatusBar()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_statusBar == null) return;

            try
            {
                var elapsed = _stopwatch.Elapsed;
                var text = $"Executing... {elapsed:h\\:mm\\:ss}";
                StatusBar.StatusBarManager.SetTransactionIndicator(_statusBar, text);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "ExecutionTimerManager: failed to update status bar");
            }
        }

        private void RestoreStatusBar(TimeSpan elapsed)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_statusBar == null) return;

            try
            {
                var text = $"Execution completed in {elapsed.TotalSeconds:F1}s";
                StatusBar.StatusBarManager.SetTransactionIndicator(_statusBar, text);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "ExecutionTimerManager: failed to restore status bar");
            }
        }

        public void Dispose()
        {
            _timer.Stop();
            _stopwatch.Stop();
        }
    }
}
