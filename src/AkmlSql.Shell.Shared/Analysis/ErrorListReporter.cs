using System;
using System.Collections.Generic;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Analysis
{
    /// <summary>
    /// Pushes Warning/Error severity diagnostics from an AnalysisController into the VS/SSMS Error List.
    /// Uses the traditional SVsErrorList / IVsErrorList task-provider API which works in all target hosts.
    /// </summary>
    internal sealed class ErrorListReporter : IDisposable, ErrorListReporter.IReapplicable
    {
        /// <summary>Something that can re-publish its findings after an Options change.</summary>
        internal interface IReapplicable
        {
            void Reapply();
        }

        // ─── Spec 040 (OPT-01): "Show in Error List" ───────────────────────────
        // Live reporters, held weakly so a closed document's reporter can still be collected.
        private static readonly object RegistryGate = new object();
        private static readonly List<WeakReference<IReapplicable>> Registry = new List<WeakReference<IReapplicable>>();

        private static CodeAnalysisSettings _cachedSettings;
        private static DateTime _cachedSettingsUtc;

        /// <summary>True when findings should appear in the Error List.</summary>
        internal static bool ShouldPublish(CodeAnalysisSettings s) => s.ShowInErrorList;

        internal static void Register(IReapplicable reporter)
        {
            lock (RegistryGate) Registry.Add(new WeakReference<IReapplicable>(reporter));
        }

        internal static void Unregister(IReapplicable reporter)
        {
            lock (RegistryGate)
                Registry.RemoveAll(w => !w.TryGetTarget(out var r) || ReferenceEquals(r, reporter));
        }

        /// <summary>
        /// Re-applies the current settings to every live reporter: called after Options OK, so
        /// turning "Show in Error List" off clears the list at once and turning it on refills it
        /// from each document's current findings.
        /// </summary>
        internal static void ReapplyAll()
        {
            _cachedSettings = null;
            var live = new List<IReapplicable>();
            lock (RegistryGate)
            {
                Registry.RemoveAll(w => !w.TryGetTarget(out _));
                foreach (var w in Registry)
                    if (w.TryGetTarget(out var r)) live.Add(r);
            }
            foreach (var reporter in live)
            {
                try { reporter.Reapply(); }
                catch (Exception ex) { Log.Warning(ex, "ErrorListReporter: re-apply failed"); }
            }
        }

        /// <summary>Settings read at most every 2 s, so a burst of analyses doesn't hit the disk.</summary>
        private static CodeAnalysisSettings CurrentSettings()
        {
            var cached = _cachedSettings;
            if (cached == null || (DateTime.UtcNow - _cachedSettingsUtc).TotalSeconds > 2)
            {
                try { cached = ConfigManager.Load().CodeAnalysis; }
                catch (Exception ex)
                {
                    Log.Warning(ex, "ErrorListReporter: could not read settings; publishing");
                    cached = new CodeAnalysisSettings();
                }
                _cachedSettings = cached;
                _cachedSettingsUtc = DateTime.UtcNow;
            }
            return cached;
        }

        private readonly AnalysisController _controller;
        private readonly IServiceProvider   _serviceProvider;
        private readonly string             _documentPath;
        // Spec 040 (OPT-01): an ErrorListProvider, not a plain TaskProvider — a TaskProvider's items go
        // to the Task List window, so "Show in Error List" never reached the Error List.
        private ErrorListProvider           _taskProvider;
        private bool                        _disposed;

        public ErrorListReporter(AnalysisController controller, IServiceProvider serviceProvider, string documentPath)
        {
            _controller      = controller      ?? throw new ArgumentNullException(nameof(controller));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _documentPath    = documentPath    ?? string.Empty;

            _taskProvider = new ErrorListProvider(serviceProvider) { ProviderName = "AKML SQL" };
            _controller.DiagnosticsUpdated += OnDiagnosticsUpdated;
            Register(this);
        }

        void IReapplicable.Reapply()
        {
            if (_disposed) return;
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    RefreshTaskList(_controller.CurrentIssues);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "ErrorListReporter: failed to re-apply settings");
                }
            });
        }

        private void OnDiagnosticsUpdated(object sender, DiagnosticsUpdatedEventArgs e)
        {
            if (_disposed) return;

            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    RefreshTaskList(e.Issues);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "ErrorListReporter: failed to refresh task list");
                }
            });
        }

        private void RefreshTaskList(CodeIssueInfo[] issues)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_taskProvider == null || _disposed) return;

            _taskProvider.Tasks.Clear();

            if (!ShouldPublish(CurrentSettings()))
            {
                _taskProvider.Refresh();
                return;
            }

            foreach (var issue in issues)
            {
                // Only surface Warning and above in the Error List
                if (issue.Severity < 2) continue;

                var category = issue.Severity >= 3
                    ? TaskErrorCategory.Error
                    : TaskErrorCategory.Warning;

                var task = new ErrorTask
                {
                    Text          = $"[{issue.RuleId}] {issue.Message}",
                    ErrorCategory = category,
                    Document      = _documentPath,
                    Line          = issue.Line - 1,   // VS Error List is 0-based
                    Column        = issue.Column - 1,
                    Priority      = category == TaskErrorCategory.Error
                                    ? TaskPriority.High
                                    : TaskPriority.Normal
                };

                _taskProvider.Tasks.Add(task);
            }

            _taskProvider.Refresh();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Unregister(this);
            _controller.DiagnosticsUpdated -= OnDiagnosticsUpdated;
            _taskProvider?.Dispose();
            _taskProvider = null;
        }
    }
}
