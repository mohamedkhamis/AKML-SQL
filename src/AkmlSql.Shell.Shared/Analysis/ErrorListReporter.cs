using System;
using AkmlSql.Core.Ipc.Messages;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Analysis
{
    /// <summary>
    /// Pushes Warning/Error severity diagnostics from an AnalysisController into the VS/SSMS Error List.
    /// Uses the traditional SVsErrorList / IVsErrorList task-provider API which works in all target hosts.
    /// </summary>
    internal sealed class ErrorListReporter : IDisposable
    {
        private readonly AnalysisController _controller;
        private readonly IServiceProvider   _serviceProvider;
        private readonly string             _documentPath;
        private TaskProvider                _taskProvider;
        private bool                        _disposed;

        public ErrorListReporter(AnalysisController controller, IServiceProvider serviceProvider, string documentPath)
        {
            _controller      = controller      ?? throw new ArgumentNullException(nameof(controller));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _documentPath    = documentPath    ?? string.Empty;

            _taskProvider = new TaskProvider(serviceProvider);
            _controller.DiagnosticsUpdated += OnDiagnosticsUpdated;
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
            _controller.DiagnosticsUpdated -= OnDiagnosticsUpdated;
            _taskProvider?.Dispose();
            _taskProvider = null;
        }
    }
}
