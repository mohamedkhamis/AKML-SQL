using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Text;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;
using Serilog;

#pragma warning disable CS0618 // IQuickInfoSource/IQuickInfoSourceProvider are obsolete but SSMS 22's editor does not invoke the async replacement (IAsyncQuickInfoSource) — see the cache-and-retrigger note below.

namespace AkmlSql.Shell.Shared.Editor
{
    /// <summary>
    /// T067: MEF-exported provider that creates QuickInfoSource instances for T-SQL buffers.
    /// </summary>
    [Export(typeof(IQuickInfoSourceProvider))]
    // SSMS 22 query editor reports content type "SQL"; register all three so quick-info fires in SSMS.
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
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
    /// Spec 030 T025 / FR-009 — hover tooltips. Sends <see cref="MessageTypes.RequestQuickInfo"/>
    /// to the engine (whose <c>QuickInfoHandler</c> resolves the identifier against the live,
    /// continuously-synced session document) and renders the metadata.
    /// <para>
    /// The MEF <see cref="IQuickInfoSource"/> contract is synchronous (<see
    /// cref="AugmentQuickInfoSession"/> returns <c>out ITrackingSpan</c>) but the IPC is async, so
    /// this uses the <b>cache-and-retrigger</b> bridge: the first pass fires the request on a
    /// background thread and returns no content; when the response lands it is cached and
    /// <see cref="IQuickInfoSession.Recalculate"/> re-enters this method, where the cached response
    /// is rendered. This never blocks the UI thread (vs. <c>JoinableTaskFactory.Run</c>, which would
    /// freeze the editor on every hover). Migrating to <c>IAsyncQuickInfoSource</c> was rejected —
    /// the SSMS 22 editor does not invoke it.
    /// </para>
    /// </summary>
    internal class QuickInfoSource : IQuickInfoSource
    {
        private readonly ITextBuffer _buffer;
        private readonly string? _sessionId;
        private readonly object _gate = new object();
        // offset -> response that arrived from the engine and is awaiting render on the retrigger pass.
        private readonly Dictionary<int, QuickInfoResponse> _cache = new Dictionary<int, QuickInfoResponse>();
        // offsets with an IPC request currently in flight (so we don't fire twice).
        private readonly HashSet<int> _pending = new HashSet<int>();
        private int _cacheGeneration;   // bumped on edit so a slow fetch can't write a stale entry
        private bool _disposed;

        public QuickInfoSource(ITextBuffer buffer)
        {
            _buffer = buffer;
            buffer.Properties.TryGetProperty("AkmlSqlSessionId", out _sessionId);
            // Metadata can change as the document is edited — drop any cached/pending hovers.
            buffer.Changed += OnBufferChanged;
        }

        private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            lock (_gate) { _cache.Clear(); _pending.Clear(); _cacheGeneration++; }
        }

        public void AugmentQuickInfoSession(IQuickInfoSession session, IList<object> quickInfoContent, out ITrackingSpan applicableToSpan)
        {
            applicableToSpan = null;
            if (_disposed || string.IsNullOrEmpty(_sessionId))
                return;

            try
            {
                var snapshot = _buffer.CurrentSnapshot;
                var point = session.GetTriggerPoint(snapshot);
                if (point == null)
                    return;

                int position = point.Value.Position;

                // Word span under the cursor (also what the tooltip applies to).
                int start = position;
                while (start > 0 && IsIdentifierChar(snapshot[start - 1])) start--;
                int end = position;
                while (end < snapshot.Length && IsIdentifierChar(snapshot[end])) end++;
                if (start == end)
                    return;

                applicableToSpan = snapshot.CreateTrackingSpan(start, end - start, SpanTrackingMode.EdgeInclusive);

                QuickInfoResponse? ready = null;
                bool fire = false;
                lock (_gate)
                {
                    if (_cache.TryGetValue(position, out var cached))
                    {
                        ready = cached;            // retrigger pass — render below
                        _cache.Remove(position);
                    }
                    else if (!_pending.Contains(position))
                    {
                        _pending.Add(position);    // first pass — fetch
                        fire = true;
                    }
                    // else: in flight, render nothing this pass
                }

                if (ready != null)
                {
                    Render(ready, quickInfoContent);
                    return;
                }
                if (fire)
                    FetchAndRetrigger(session, position);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "QuickInfo: augment failed");
            }
        }

        private void FetchAndRetrigger(IQuickInfoSession session, int position)
        {
            int generation;
            lock (_gate) { generation = _cacheGeneration; }

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var client = EngineLifecycle.Manager?.Client;
                    if (client == null || !client.IsConnected)
                    {
                        lock (_gate) { _pending.Remove(position); }
                        return;
                    }

                    var response = await client.SendRequestAsync<QuickInfoResponse, QuickInfoRequest>(
                        MessageTypes.RequestQuickInfo,
                        new QuickInfoRequest { SessionId = _sessionId, CursorOffset = position },
                        timeoutMs: 1500).ConfigureAwait(false);

                    bool hasContent = response != null
                        && (!string.IsNullOrEmpty(response.Header)
                            || (response.Details != null && response.Details.Length > 0)
                            || !string.IsNullOrEmpty(response.Description));

                    bool store;
                    lock (_gate)
                    {
                        _pending.Remove(position);
                        // Drop a response whose generation has moved on (edit landed mid-fetch) so a
                        // stale hover can't linger in the cache for the same offset.
                        store = hasContent && generation == _cacheGeneration;
                        if (store) _cache[position] = response!;
                    }
                    if (!store) return;   // nothing to show / superseded — don't re-trigger

                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    if (!_disposed && !session.IsDismissed)
                        session.Recalculate();   // re-enters AugmentQuickInfoSession, where the cache renders
                }
                catch (Exception ex)
                {
                    lock (_gate) { _pending.Remove(position); }
                    Log.Debug(ex, "QuickInfo: fetch failed");
                }
            });
        }

        private static void Render(QuickInfoResponse response, IList<object> quickInfoContent)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(response.Header))
            {
                if (!string.IsNullOrEmpty(response.ObjectType))
                    sb.Append('[').Append(response.ObjectType).Append("] ");
                sb.AppendLine(response.Header);
            }
            if (response.Details != null)
            {
                foreach (var d in response.Details)
                {
                    if (string.IsNullOrEmpty(d?.Label) && string.IsNullOrEmpty(d?.Value)) continue;
                    sb.Append("  ").Append(d!.Label);
                    if (!string.IsNullOrEmpty(d.Value)) sb.Append(": ").Append(d.Value);
                    sb.AppendLine();
                }
            }
            if (!string.IsNullOrEmpty(response.Description))
                sb.AppendLine().Append(response.Description);

            var text = sb.ToString().TrimEnd();
            if (text.Length > 0)
                quickInfoContent.Add(text);
        }

        private static bool IsIdentifierChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '#' || c == '@' || c == '.';
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _buffer.Changed -= OnBufferChanged;
        }
    }
}

#pragma warning restore CS0618
