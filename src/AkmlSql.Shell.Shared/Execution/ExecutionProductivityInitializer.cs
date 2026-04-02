#nullable enable
using System;
using AkmlSql.Core.Config;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Execution
{
    /// <summary>
    /// T084: Wires up execution timer and completion notifier during package initialization.
    /// Both components are initialized unconditionally; feature flags are checked at invocation time.
    /// </summary>
    internal static class ExecutionProductivityInitializer
    {
        private static bool _initialized;

        /// <summary>
        /// Initializes the execution timer and completion notifier.
        /// Must be called on the UI thread during package initialization.
        /// </summary>
        public static void Initialize(AsyncPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_initialized) return;

            try
            {
                var settings = ConfigManager.Load();

                if (settings.ExecutionProductivity.ShowExecutionTimer)
                {
                    ExecutionTimerManager.Initialize();
                }

                CompletionNotifier.Initialize();

                _initialized = true;
                Log.Information("ExecutionProductivityInitializer: initialized " +
                    "(timer={Timer}, notifications threshold={Threshold}s)",
                    settings.ExecutionProductivity.ShowExecutionTimer,
                    settings.ExecutionProductivity.NotificationThreshold);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ExecutionProductivityInitializer: failed to initialize");
            }
        }

        /// <summary>
        /// Initializes for SSMS 20 (synchronous Package).
        /// </summary>
        public static void Initialize(Package package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_initialized) return;

            try
            {
                var settings = ConfigManager.Load();

                if (settings.ExecutionProductivity.ShowExecutionTimer)
                {
                    ExecutionTimerManager.Initialize();
                }

                CompletionNotifier.Initialize();

                _initialized = true;
                Log.Information("ExecutionProductivityInitializer: initialized " +
                    "(timer={Timer}, notifications threshold={Threshold}s)",
                    settings.ExecutionProductivity.ShowExecutionTimer,
                    settings.ExecutionProductivity.NotificationThreshold);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ExecutionProductivityInitializer: failed to initialize");
            }
        }
    }
}
