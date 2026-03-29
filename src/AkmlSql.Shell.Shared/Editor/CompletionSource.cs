using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Windows.Media;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor
{
    [Export(typeof(ICompletionSourceProvider))]
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [ContentType("T-SQL")]
    [Name("AkmlSqlCompletionSource")]
    [Order(Before = "default")]
    internal class CompletionSourceProvider : ICompletionSourceProvider
    {
        public ICompletionSource TryCreateCompletionSource(ITextBuffer textBuffer)
        {
            return new CompletionSource(textBuffer);
        }
    }

    internal class CompletionSource : ICompletionSource
    {
        private readonly ITextBuffer _buffer;
        private bool _disposed;

        public CompletionSource(ITextBuffer buffer)
        {
            _buffer = buffer;
        }

        public void AugmentCompletionSession(ICompletionSession session, IList<CompletionSet> completionSets)
        {
            if (_disposed)
                return;

            try
            {
                // Get completions — MUST be non-blocking (this runs on UI thread)
                var items = CompletionRpcHelper.GetCachedCompletions(_buffer, session);
                var completionSet = new CompletionSet(
                    "AKML SQL",
                    "AKML SQL",
                    FindTokenSpanAtPosition(session),
                    items,
                    null);
                completionSets.Add(completionSet);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error augmenting completion session");
            }
        }

        private ITrackingSpan FindTokenSpanAtPosition(ICompletionSession session)
        {
            var point = session.GetTriggerPoint(_buffer.CurrentSnapshot);
            if (point == null)
                return _buffer.CurrentSnapshot.CreateTrackingSpan(0, 0, SpanTrackingMode.EdgeInclusive);

            var position = point.Value.Position;
            var snapshot = _buffer.CurrentSnapshot;

            int start = position;
            while (start > 0 && IsIdentifierChar(snapshot[start - 1]))
                start--;

            return snapshot.CreateTrackingSpan(start, position - start, SpanTrackingMode.EdgeInclusive);
        }

        private static bool IsIdentifierChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '#' || c == '@';
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
