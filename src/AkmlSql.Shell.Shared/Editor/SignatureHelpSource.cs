using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

namespace AkmlSql.Shell.Shared.Editor
{
    /// <summary>
    /// T063: MEF-exported provider that creates SignatureHelpSource instances for T-SQL buffers.
    /// </summary>
    [Export(typeof(ISignatureHelpSourceProvider))]
    // SSMS 22 query editor reports content type "SQL"; register all three so signature help fires in SSMS.
    [ContentType("SQL Server Tools")]
    [ContentType("SQL")]
    [ContentType("T-SQL")]
    [Name("AkmlSqlSignatureHelpSource")]
    [Order(Before = "default")]
    internal class SignatureHelpSourceProvider : ISignatureHelpSourceProvider
    {
        public ISignatureHelpSource TryCreateSignatureHelpSource(ITextBuffer textBuffer)
        {
            return new SignatureHelpSource(textBuffer);
        }
    }

    /// <summary>
    /// Spec 030 T026 / FR-010 — parameter (signature) help. Sends
    /// <see cref="MessageTypes.RequestSignatureHelp"/> to the engine (whose SignatureHelpHandler
    /// finds the enclosing call against the live session document and computes the active overload
    /// + active parameter), and renders the overloads as MEF <see cref="ISignature"/> objects.
    /// <para>
    /// Triggered by <c>CompletionController</c> on '(' / ',' via <c>ISignatureHelpBroker</c>. The
    /// MEF source contract is synchronous, so the async IPC is bridged with the same
    /// cache-and-retrigger pattern as <see cref="QuickInfoSource"/>: the first pass fetches + returns
    /// nothing; the response is cached and <see cref="ISignatureHelpSession.Recompute"/> re-enters
    /// this method to render.
    /// </para>
    /// </summary>
    internal class SignatureHelpSource : ISignatureHelpSource
    {
        private readonly ITextBuffer _buffer;
        private readonly string? _sessionId;
        private readonly object _gate = new object();
        private readonly Dictionary<int, SignatureResponse> _cache = new Dictionary<int, SignatureResponse>();
        private readonly HashSet<int> _pending = new HashSet<int>();
        private bool _disposed;

        public SignatureHelpSource(ITextBuffer buffer)
        {
            _buffer = buffer;
            buffer.Properties.TryGetProperty("AkmlSqlSessionId", out _sessionId);
            buffer.Changed += OnBufferChanged;
        }

        private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            lock (_gate) { _cache.Clear(); _pending.Clear(); }
        }

        public void AugmentSignatureHelpSession(ISignatureHelpSession session, IList<ISignature> signatures)
        {
            if (_disposed || string.IsNullOrEmpty(_sessionId))
                return;

            try
            {
                var snapshot = _buffer.CurrentSnapshot;
                var point = session.GetTriggerPoint(snapshot);
                if (point == null)
                    return;
                int position = point.Value.Position;

                SignatureResponse? ready = null;
                bool fire = false;
                lock (_gate)
                {
                    if (_cache.TryGetValue(position, out var cached))
                    {
                        ready = cached;
                        _cache.Remove(position);
                    }
                    else if (!_pending.Contains(position))
                    {
                        _pending.Add(position);
                        fire = true;
                    }
                }

                if (ready != null)
                {
                    BuildSignatures(ready, session, snapshot, position, signatures);
                    return;
                }
                if (fire)
                    FetchAndRetrigger(session, position);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SignatureHelp: augment failed");
            }
        }

        public ISignature GetBestMatch(ISignatureHelpSession session)
        {
            // The engine picks the active overload; surface the one flagged on our signatures.
            foreach (var sig in session.Signatures)
            {
                if (sig is AkmlSignature aks && aks.IsActiveOverload)
                    return sig;
            }
            return session.Signatures.Count > 0 ? session.Signatures[0] : null;
        }

        private void FetchAndRetrigger(ISignatureHelpSession session, int position)
        {
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

                    var response = await client.SendRequestAsync<SignatureResponse, SignatureRequest>(
                        MessageTypes.RequestSignatureHelp,
                        new SignatureRequest { SessionId = _sessionId, CursorOffset = position },
                        timeoutMs: 1500).ConfigureAwait(false);

                    bool hasContent = response?.Overloads != null && response.Overloads.Length > 0;
                    lock (_gate)
                    {
                        _pending.Remove(position);
                        if (hasContent) _cache[position] = response!;
                    }
                    if (!hasContent)
                    {
                        // No function context — dismiss the (empty) session we opened.
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        if (!_disposed && !session.IsDismissed) session.Dismiss();
                        return;
                    }

                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    if (!_disposed && !session.IsDismissed)
                        session.Recalculate();   // re-enters AugmentSignatureHelpSession → renders from cache
                }
                catch (Exception ex)
                {
                    lock (_gate) { _pending.Remove(position); }
                    Log.Debug(ex, "SignatureHelp: fetch failed");
                }
            });
        }

        private static void BuildSignatures(
            SignatureResponse response, ISignatureHelpSession session, ITextSnapshot snapshot,
            int position, IList<ISignature> signatures)
        {
            // The signature(s) apply from the call's opening paren to the cursor, so the session
            // stays alive while the user types arguments.
            int spanStart = FindCallStart(snapshot, position);
            int len = Math.Max(0, position - spanStart);
            var applicable = snapshot.CreateTrackingSpan(spanStart, len, SpanTrackingMode.EdgeInclusive);

            int activeOverload = response.ActiveOverload;
            var overloads = response.Overloads ?? Array.Empty<SignatureOverload>();
            for (int i = 0; i < overloads.Length; i++)
            {
                var sig = new AkmlSignature(
                    overloads[i], response.FunctionName, applicable,
                    response.ActiveParameter, isActiveOverload: i == activeOverload);
                signatures.Add(sig);
            }
        }

        /// <summary>Backward scan to the nearest unmatched '(' before the cursor (the call start).</summary>
        private static int FindCallStart(ITextSnapshot snapshot, int position)
        {
            int depth = 0;
            for (int i = position - 1; i >= 0 && i < snapshot.Length; i--)
            {
                char c = snapshot[i];
                if (c == ')') depth++;
                else if (c == '(')
                {
                    if (depth == 0) return i + 1; // just after the open paren
                    depth--;
                }
                else if (c == ';' || c == '\n') break;
            }
            return position;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _buffer.Changed -= OnBufferChanged;
        }
    }

    /// <summary>MEF <see cref="ISignature"/> adapter over an engine <see cref="SignatureOverload"/>.</summary>
    internal sealed class AkmlSignature : ISignature
    {
        private IParameter? _currentParameter;

        public AkmlSignature(SignatureOverload overload, string functionName, ITrackingSpan applicableToSpan,
            int activeParameter, bool isActiveOverload)
        {
            ApplicableToSpan = applicableToSpan;
            Documentation = overload.Documentation ?? string.Empty;
            IsActiveOverload = isActiveOverload;

            // Build the displayed content + each parameter's locus within it.
            var parameters = overload.Parameters ?? Array.Empty<ParameterInfo>();
            var sb = new StringBuilder();
            sb.Append(string.IsNullOrEmpty(functionName) ? overload.Label : functionName).Append('(');
            var built = new List<IParameter>(parameters.Length);
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                var p = parameters[i];
                string display = string.IsNullOrEmpty(p.Type) ? p.Name : $"{p.Name} {p.Type}";
                int locusStart = sb.Length;
                sb.Append(display);
                built.Add(new AkmlParameter(p.Documentation ?? string.Empty,
                    new Span(locusStart, display.Length), p.Name ?? string.Empty, this));
            }
            sb.Append(')');
            Content = sb.ToString();
            Parameters = new ReadOnlyCollection<IParameter>(built);

            if (built.Count > 0)
            {
                int idx = activeParameter;
                if (idx < 0) idx = 0;
                if (idx >= built.Count) idx = built.Count - 1;
                _currentParameter = built[idx];
            }
        }

        public bool IsActiveOverload { get; }

        public ITrackingSpan ApplicableToSpan { get; }
        public string Content { get; }
        public string Documentation { get; }
        public ReadOnlyCollection<IParameter> Parameters { get; }
        public string PrettyPrintedContent => Content;

        public IParameter CurrentParameter
        {
            get => _currentParameter;
            set
            {
                if (_currentParameter == value) return;
                var prev = _currentParameter;
                _currentParameter = value;
                CurrentParameterChanged?.Invoke(this, new CurrentParameterChangedEventArgs(prev, value));
            }
        }

        public event EventHandler<CurrentParameterChangedEventArgs> CurrentParameterChanged;
    }

    /// <summary>MEF <see cref="IParameter"/> adapter over an engine <see cref="ParameterInfo"/>.</summary>
    internal sealed class AkmlParameter : IParameter
    {
        public AkmlParameter(string documentation, Span locus, string name, ISignature signature)
        {
            Documentation = documentation;
            Locus = locus;
            Name = name;
            Signature = signature;
            PrettyPrintedLocus = locus;
        }

        public string Documentation { get; }
        public Span Locus { get; }
        public string Name { get; }
        public ISignature Signature { get; }
        public Span PrettyPrintedLocus { get; }
    }
}
