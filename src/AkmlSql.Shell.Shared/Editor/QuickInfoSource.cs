using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;
using Serilog;

#pragma warning disable CS0618 // IQuickInfoSource/IQuickInfoSourceProvider are obsolete but have no async replacement in SSMS 20 SDK

namespace AkmlSql.Shell.Shared.Editor
{
    /// <summary>
    /// T067: MEF-exported provider that creates QuickInfoSource instances for T-SQL buffers.
    /// </summary>
    [Export(typeof(IQuickInfoSourceProvider))]
    [ContentType("T-SQL")]
    [Name("AkmlSqlQuickInfoSource")]
    [Order(Before = "default")]
    internal class QuickInfoSourceProvider : IQuickInfoSourceProvider
    {
        public IQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer)
        {
            return new QuickInfoSource(textBuffer);
        }
    }

    /// <summary>
    /// T067: Bridges quick info requests to PipeRpcClient for QuickInfoRequest/QuickInfoResponse.
    /// Currently a skeleton with TODO for actual IPC calls.
    /// </summary>
    internal class QuickInfoSource : IQuickInfoSource
    {
        private readonly ITextBuffer _buffer;
        private bool _disposed;

        public QuickInfoSource(ITextBuffer buffer)
        {
            _buffer = buffer;
        }

        public void AugmentQuickInfoSession(IQuickInfoSession session, IList<object> quickInfoContent, out ITrackingSpan applicableToSpan)
        {
            applicableToSpan = null;

            if (_disposed) return;

            try
            {
                var point = session.GetTriggerPoint(_buffer.CurrentSnapshot);
                if (point == null) return;

                var position = point.Value.Position;
                var snapshot = _buffer.CurrentSnapshot;

                // Find the word under the cursor
                int start = position;
                while (start > 0 && IsIdentifierChar(snapshot[start - 1]))
                    start--;

                int end = position;
                while (end < snapshot.Length && IsIdentifierChar(snapshot[end]))
                    end++;

                if (start == end) return;

                applicableToSpan = snapshot.CreateTrackingSpan(
                    start, end - start, SpanTrackingMode.EdgeInclusive);

                // TODO: Send QuickInfoRequest via PipeRpcClient
                // var request = new QuickInfoRequest { SessionId = ..., CursorOffset = position };
                // var response = await _rpcClient.SendRequestAsync<QuickInfoResponse>(MessageTypes.QuickInfo, request);
                // Format response and add to quickInfoContent

                Log.Debug("QuickInfo requested at offset {Offset}", position);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error augmenting quick info session");
            }
        }

        private static bool IsIdentifierChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '#' || c == '@' || c == '.';
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}

#pragma warning restore CS0618
