using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Models.Analysis;
using AkmlSql.Shell.Shared.Formatting;
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
            // Always true — refactoring actions (Comment, Format) are always available
            // even when no analysis issues exist on the current line
            foreach (var issue in _issues)
            {
                if (SpanOverlapsIssue(range, issue)) return Task.FromResult(true);
            }

            // Check if contextual refactoring actions apply
            var text = range.GetText();
            if (!string.IsNullOrWhiteSpace(text))
                return Task.FromResult(true);

            return Task.FromResult(false);
        }

        public bool TryGetTelemetryId(out Guid telemetryId) { telemetryId = Guid.Empty; return false; }

        public IEnumerable<SuggestedActionSet> GetSuggestedActions(
            ISuggestedActionCategorySet requestedActionCategories,
            SnapshotSpan range,
            CancellationToken cancellationToken)
        {
            var snapshot = _buffer.CurrentSnapshot;

            // ── Analysis fix actions ────────────────────────────────────
            foreach (var issue in _issues)
            {
                if (!SpanOverlapsIssue(range, issue)) continue;

                var actions = new List<ISuggestedAction>();

                foreach (var fix in issue.FixActions)
                {
                    if (fix.FixType != (int)FixType.Suppress)
                        actions.Add(new FixAction(_buffer, fix, issue.RuleId));
                }

                if (!string.IsNullOrEmpty(issue.RuleId))
                {
                    actions.Add(new SuppressLineFixAction(_buffer, issue.Line, issue.RuleId));
                    actions.Add(new DisableRuleGloballyFixAction(issue.RuleId));
                }

                if (actions.Count > 0)
                    yield return new SuggestedActionSet(actions);
            }

            // ── Contextual refactoring actions ──────────────────────────
            var refactorActions = new List<ISuggestedAction>();
            var rangeText = range.GetText();

            // Detect SELECT * — offer Expand Wildcards
            if (ContainsSelectStar(snapshot, range))
            {
                refactorActions.Add(new RefactoringAction(
                    _buffer, "Expand Wildcards (SELECT *)", FormatActionType.ExpandWildcards));
            }

            // Detect unqualified table reference — offer Qualify Object Names
            // Only show when the line contains a FROM/JOIN keyword (likely table reference context)
            if (ContainsTableContext(snapshot, range))
            {
                refactorActions.Add(new RefactoringAction(
                    _buffer, "Qualify Object Names", FormatActionType.QualifyObjectNames));
            }

            // Detect sp_executesql — offer Convert to Static SQL
            if (ContainsSpExecutesql(snapshot, range))
            {
                refactorActions.Add(new RefactoringAction(
                    _buffer, "Convert sp_executesql to Static SQL", FormatActionType.ConvertSpExecutesql));
            }

            // If there is a selection, offer surround-with actions
            if (!string.IsNullOrWhiteSpace(rangeText) && rangeText.Length > 1)
            {
                refactorActions.Add(new RefactoringAction(
                    _buffer, "Surround with BEGIN/END", FormatActionType.EncapsulateBeginEnd));
            }

            // Always available actions
            refactorActions.Add(new RefactoringAction(
                _buffer, "Unformat (Compact SQL)", FormatActionType.Unformat));
            refactorActions.Add(new CommentToggleAction(_buffer));

            if (refactorActions.Count > 0)
                yield return new SuggestedActionSet(refactorActions);
        }

        /// <summary>
        /// Checks if the range or its containing line contains "SELECT *" or "SELECT  *".
        /// </summary>
        private static bool ContainsSelectStar(ITextSnapshot snapshot, SnapshotSpan range)
        {
            try
            {
                var lineNumber = snapshot.GetLineNumberFromPosition(range.Start.Position);
                var line = snapshot.GetLineFromLineNumber(lineNumber);
                var lineText = line.GetText();
                // Simple heuristic: look for "SELECT" followed by "*" on the same line
                var selectIdx = lineText.IndexOf("SELECT", StringComparison.OrdinalIgnoreCase);
                if (selectIdx < 0) return false;
                var afterSelect = lineText.Substring(selectIdx + 6).TrimStart();
                return afterSelect.StartsWith("*", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if the range's line contains FROM, JOIN, or EXEC keywords (table/proc reference context).
        /// </summary>
        private static bool ContainsTableContext(ITextSnapshot snapshot, SnapshotSpan range)
        {
            try
            {
                var lineNumber = snapshot.GetLineNumberFromPosition(range.Start.Position);
                var lineText = snapshot.GetLineFromLineNumber(lineNumber).GetText();
                return lineText.IndexOf("FROM", StringComparison.OrdinalIgnoreCase) >= 0
                    || lineText.IndexOf("JOIN", StringComparison.OrdinalIgnoreCase) >= 0
                    || lineText.IndexOf("EXEC", StringComparison.OrdinalIgnoreCase) >= 0
                    || lineText.IndexOf("INTO", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if the range's line contains "sp_executesql" (case-insensitive).
        /// </summary>
        private static bool ContainsSpExecutesql(ITextSnapshot snapshot, SnapshotSpan range)
        {
            try
            {
                var lineNumber = snapshot.GetLineNumberFromPosition(range.Start.Position);
                var lineText = snapshot.GetLineFromLineNumber(lineNumber).GetText();
                return lineText.IndexOf("sp_executesql", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
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
