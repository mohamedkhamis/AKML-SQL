using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using AkmlSql.Core.Ipc.Messages;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace AkmlSql.Shell.Shared.Analysis
{
    [Export(typeof(IViewTaggerProvider))]
    [ContentType("T-SQL")]
    [TagType(typeof(IErrorTag))]
    internal sealed class DiagnosticTaggerProvider : IViewTaggerProvider
    {
        public ITagger<T> CreateTagger<T>(ITextView textView, ITextBuffer buffer) where T : ITag
        {
            if (textView == null || buffer == null) return null;

            // Retrieve or create the AnalysisController stored in buffer properties by TextViewCreationListener
            var controller = buffer.Properties.GetOrCreateSingletonProperty(
                typeof(AnalysisController),
                () => new AnalysisController(buffer, Guid.NewGuid().ToString("N")));

            // Wire ErrorListReporter so Warning/Error findings appear in the VS/SSMS Error List (T010)
            buffer.Properties.GetOrCreateSingletonProperty(typeof(ErrorListReporter), () =>
            {
                string docPath = string.Empty;
                if (buffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument textDoc))
                    docPath = textDoc.FilePath ?? string.Empty;
                return new ErrorListReporter(controller, ServiceProvider.GlobalProvider, docPath);
            });

            return buffer.Properties.GetOrCreateSingletonProperty(
                typeof(DiagnosticTagger),
                () => new DiagnosticTagger(buffer, controller)) as ITagger<T>;
        }
    }

    internal sealed class DiagnosticTagger : ITagger<IErrorTag>, IDisposable
    {
        private readonly ITextBuffer _buffer;
        private readonly AnalysisController _controller;
        private CodeIssueInfo[] _issues = Array.Empty<CodeIssueInfo>();

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        internal DiagnosticTagger(ITextBuffer buffer, AnalysisController controller)
        {
            _buffer     = buffer;
            _controller = controller;
            _controller.DiagnosticsUpdated += OnDiagnosticsUpdated;
        }

        private void OnDiagnosticsUpdated(object sender, DiagnosticsUpdatedEventArgs e)
        {
            _issues = e.Issues;
            var span = new SnapshotSpan(e.Snapshot, 0, e.Snapshot.Length);
            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(span));
        }

        public IEnumerable<ITagSpan<IErrorTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            var snapshot = _buffer.CurrentSnapshot;
            var issues   = _issues;

            foreach (var issue in issues)
            {
                // Guard against stale offsets after edits
                var start = Math.Min(issue.StartOffset, snapshot.Length);
                var end   = Math.Min(issue.EndOffset,   snapshot.Length);
                if (start > end) continue;

                var issueSpan = new SnapshotSpan(snapshot, start, end - start);

                // Only emit tags for spans that overlap the requested region
                bool overlaps = false;
                foreach (var s in spans)
                    if (issueSpan.OverlapsWith(s)) { overlaps = true; break; }
                if (!overlaps) continue;

                var errorType = SeverityToErrorType(issue.Severity);
                yield return new TagSpan<IErrorTag>(issueSpan, new ErrorTag(errorType, issue.Message));
            }
        }

        private static string SeverityToErrorType(int severity)
        {
            return severity switch
            {
                3 => Microsoft.VisualStudio.Text.Adornments.PredefinedErrorTypeNames.SyntaxError, // Error
                2 => Microsoft.VisualStudio.Text.Adornments.PredefinedErrorTypeNames.Warning, // Warning
                1 => Microsoft.VisualStudio.Text.Adornments.PredefinedErrorTypeNames.OtherError, // Information
                _ => Microsoft.VisualStudio.Text.Adornments.PredefinedErrorTypeNames.HintedSuggestion // Hint
            };
        }

        public void Dispose()
        {
            _controller.DiagnosticsUpdated -= OnDiagnosticsUpdated;
        }
    }
}
