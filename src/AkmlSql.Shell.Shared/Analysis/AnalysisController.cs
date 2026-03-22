using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using Microsoft.VisualStudio.Text;
using Serilog;

namespace AkmlSql.Shell.Shared.Analysis
{
    /// <summary>
    /// Per-text-buffer controller: debounces document changes, fires a RequestAnalyze RPC to the
    /// out-of-process engine, and raises DiagnosticsUpdated when results arrive.
    /// </summary>
    internal sealed class AnalysisController : IDisposable
    {
        private readonly ITextBuffer _buffer;
        private readonly string _sessionId;
        private CancellationTokenSource _debounce;
        private volatile bool _disposed;
        private int _version;

        /// <summary>Raised on the thread pool when a new set of diagnostics is available.</summary>
        public event EventHandler<DiagnosticsUpdatedEventArgs> DiagnosticsUpdated;

        public AnalysisController(ITextBuffer buffer, string sessionId)
        {
            _buffer    = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _sessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
            _buffer.Changed += OnBufferChanged;
        }

        private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            if (_disposed) return;

            var version = Interlocked.Increment(ref _version);

            var prev = _debounce;
            _debounce = new CancellationTokenSource();
            try { prev?.Cancel(); } catch { /* already disposed */ }

            var ct = _debounce.Token;
            Task.Delay(300, ct).ContinueWith(
                _ => RunAnalysisAsync(version, ct),
                ct,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
        }

        private async Task RunAnalysisAsync(int documentVersion, CancellationToken ct)
        {
            if (_disposed) return;
            try
            {
                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected) return;

                var request = new CodeAnalysisRequest
                {
                    SessionId       = _sessionId,
                    RequestId       = documentVersion.ToString(),
                    DocumentText    = _buffer.CurrentSnapshot.GetText(),
                    DocumentVersion = documentVersion
                };

                var response = await client.SendRequestAsync<CodeAnalysisResponse, CodeAnalysisRequest>(
                    MessageTypes.RequestAnalyze, request, timeoutMs: 10_000, ct: ct);

                if (!ct.IsCancellationRequested)
                    DiagnosticsUpdated?.Invoke(this, new DiagnosticsUpdatedEventArgs(
                        _buffer.CurrentSnapshot, response?.Issues));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Warning(ex, "AnalysisController: analysis RPC failed for session {Session}", _sessionId);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _buffer.Changed -= OnBufferChanged;
            try { _debounce?.Cancel(); } catch { /* already disposed */ }
        }
    }

    internal sealed class DiagnosticsUpdatedEventArgs : EventArgs
    {
        public ITextSnapshot Snapshot { get; }
        public CodeIssueInfo[] Issues  { get; }

        public DiagnosticsUpdatedEventArgs(ITextSnapshot snapshot, CodeIssueInfo[] issues)
        {
            Snapshot = snapshot;
            Issues   = issues ?? Array.Empty<CodeIssueInfo>();
        }
    }
}
