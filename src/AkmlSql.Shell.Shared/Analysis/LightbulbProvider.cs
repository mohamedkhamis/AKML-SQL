using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Models.Analysis;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace AkmlSql.Shell.Shared.Analysis
{
    [Export(typeof(ISuggestedActionsSourceProvider))]
    [Name("AkmlSqlLightbulbProvider")]
    [ContentType("T-SQL")]
    internal sealed class LightbulbProvider : ISuggestedActionsSourceProvider
    {
        public ISuggestedActionsSource CreateSuggestedActionsSource(ITextView textView, ITextBuffer textBuffer)
        {
            if (textView == null || textBuffer == null) return null;

            // Retrieve the AnalysisController stored by DiagnosticTaggerProvider / TextViewCreationListener
            if (!textBuffer.Properties.TryGetProperty(typeof(AnalysisController), out AnalysisController controller))
                return null;

            return new LightbulbSource(textBuffer, controller);
        }
    }

    internal sealed class LightbulbSource : ISuggestedActionsSource
    {
        private readonly ITextBuffer       _buffer;
        private readonly AnalysisController _controller;
        private CodeIssueInfo[]            _issues = Array.Empty<CodeIssueInfo>();

        public event EventHandler<EventArgs> SuggestedActionsChanged;

        internal LightbulbSource(ITextBuffer buffer, AnalysisController controller)
        {
            _buffer     = buffer;
            _controller = controller;
            _controller.DiagnosticsUpdated += OnDiagnosticsUpdated;
        }

        private void OnDiagnosticsUpdated(object sender, DiagnosticsUpdatedEventArgs e)
        {
            _issues = e.Issues;
            SuggestedActionsChanged?.Invoke(this, EventArgs.Empty);
        }

        public Task<bool> HasSuggestedActionsAsync(ISuggestedActionCategorySet requestedActionCategories,
            SnapshotSpan range, CancellationToken cancellationToken)
        {
            foreach (var issue in _issues)
            {
                if (SpanOverlapsIssue(range, issue)) return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public bool TryGetTelemetryId(out Guid telemetryId) { telemetryId = Guid.Empty; return false; }

        public IEnumerable<SuggestedActionSet> GetSuggestedActions(
            ISuggestedActionCategorySet requestedActionCategories,
            SnapshotSpan range,
            CancellationToken cancellationToken)
        {
            var snapshot = _buffer.CurrentSnapshot;

            foreach (var issue in _issues)
            {
                if (!SpanOverlapsIssue(range, issue)) continue;

                var actions = new List<ISuggestedAction>();

                // One fix action per repair suggestion from the engine
                // Skip FixType.Suppress here — it is always added unconditionally below
                foreach (var fix in issue.FixActions)
                {
                    if (fix.FixType != (int)FixType.Suppress)
                        actions.Add(new FixAction(_buffer, fix, issue.RuleId));
                }

                // Always offer suppress-line and disable-globally actions
                if (!string.IsNullOrEmpty(issue.RuleId))
                {
                    actions.Add(new SuppressLineFixAction(_buffer, issue.Line, issue.RuleId));
                    actions.Add(new DisableRuleGloballyFixAction(issue.RuleId));
                }

                if (actions.Count > 0)
                    yield return new SuggestedActionSet(actions);
            }
        }

        private static bool SpanOverlapsIssue(SnapshotSpan range, CodeIssueInfo issue)
        {
            var snapshot = range.Snapshot;
            var start    = Math.Min(issue.StartOffset, snapshot.Length);
            var end      = Math.Min(issue.EndOffset,   snapshot.Length);
            if (start == end && start == snapshot.Length) return false;
            var issueSpan = new SnapshotSpan(snapshot, start, Math.Max(0, end - start));
            return range.OverlapsWith(issueSpan) || range.IntersectsWith(issueSpan);
        }

        public void Dispose()
        {
            _controller.DiagnosticsUpdated -= OnDiagnosticsUpdated;
        }
    }
}
