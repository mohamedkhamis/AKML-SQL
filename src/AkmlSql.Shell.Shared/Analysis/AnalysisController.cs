using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
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

        /// <summary>
        /// Spec 030 T055 — the most recent set of issues, cached so consumers (e.g. the Ctrl-hover
        /// issue-details popup) can read the current findings directly without subscribing — robust to
        /// being created after the last <see cref="DiagnosticsUpdated"/> fired. Array-ref read/write is
        /// atomic; the value may be one analysis cycle stale, which is fine for a hover.
        /// </summary>
        public CodeIssueInfo[] CurrentIssues => _currentIssues;
        private volatile CodeIssueInfo[] _currentIssues = Array.Empty<CodeIssueInfo>();

        public AnalysisController(ITextBuffer buffer, string sessionId)
        {
            _buffer    = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _sessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
            _buffer.Changed += OnBufferChanged;

            // Initial analysis so a freshly-opened/pasted document shows findings without first
            // requiring an edit. (No-ops if the engine isn't connected yet; the first edit re-triggers.)
            ScheduleAnalysis();
        }

        private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            if (_disposed) return;
            if (!ShouldAnalyzeOnEdit(CurrentSettings())) return;
            ScheduleAnalysis();
        }

        /// <summary>
        /// Spec 040 (OPT-01) — Code Analysis "Analyze while typing": edits analyse only while code
        /// analysis is on and that option is on. Opening a document and the explicit analysis
        /// commands still analyse.
        /// </summary>
        internal static bool ShouldAnalyzeOnEdit(CodeAnalysisSettings s) => s.Enabled && s.RunOnType;

        private static CodeAnalysisSettings _cachedSettings;
        private static DateTime _cachedSettingsUtc;

        /// <summary>Spec 047 installed-product run (F10): Options OK drops the 2 s cache, so what it saved is read at once.</summary>
        internal static void InvalidateSettings() => _cachedSettings = null;

        /// <summary>Settings read at most every 2 s — this runs on every keystroke (and for the tagger and lightbulb).</summary>
        internal static CodeAnalysisSettings CurrentSettings()
        {
            var cached = _cachedSettings;
            if (cached == null || (DateTime.UtcNow - _cachedSettingsUtc).TotalSeconds > 2)
            {
                try { cached = ConfigManager.Load().CodeAnalysis; }
                catch (Exception ex)
                {
                    Log.Warning(ex, "AnalysisController: could not read settings; analysing");
                    cached = new CodeAnalysisSettings();
                }
                _cachedSettings = cached;
                _cachedSettingsUtc = DateTime.UtcNow;
            }
            return cached;
        }

        /// <summary>
        /// Spec 030 T056 — forces a fresh analysis pass without an edit. Used by the
        /// "Toggle Code Analysis" command so disabling clears the squiggles immediately (the engine
        /// returns no issues when disabled) and enabling re-populates them.
        /// </summary>
        public void TriggerReanalysis() => ScheduleAnalysis();

        /// <summary>Debounced (300ms) trigger of a single analysis pass; cancels any in-flight one.</summary>
        private void ScheduleAnalysis()
        {
            if (_disposed) return;

            var version = Interlocked.Increment(ref _version);

            var prev = _debounce;
            _debounce = new CancellationTokenSource();
            try { prev?.Cancel(); prev?.Dispose(); } catch (ObjectDisposedException) { }

            var ct = _debounce.Token;
            Task.Delay(300, ct).ContinueWith(
                _ => RunAnalysisAsync(version, ct),
                ct,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
        }

        /// <summary>Spec 047 (SSMS-002): the engine client, resolved at call time. A test replaces it.</summary>
        internal static Func<IRpcClientAccessor> RpcAccessor { get; set; } = () => EngineRpcClientAccessor.Instance;

        /// <summary>
        /// Spec 047 (SSMS-002): runs a callback once the engine is connected (true), or gives up after the 10 s the
        /// other "when ready" waits use (false). A test replaces it.
        /// </summary>
        internal static Func<Func<Task>, Task<bool>> WhenEngineReady { get; set; } =
            send => Editor.ConnectionWiringHelper.WhenEngineReadyAsync(Editor.ConnectionWiringHelper.EngineReady, send);

        // 1 while a pass waits for the engine: however many passes run meanwhile, the controller waits once.
        private int _waitingForEngine;

        private async Task RunAnalysisAsync(int documentVersion, CancellationToken ct)
        {
            if (_disposed) return;
            var sw = Stopwatch.StartNew();
            Log.Debug("Analysis triggered for session {Session}", _sessionId);
            try
            {
                var client = RpcAccessor();
                if (client == null || !client.IsConnected)
                {
                    // Spec 047 (SSMS-002): a document opened before the engine connected was never analysed until the
                    // user typed. Analyse it as soon as the engine is up.
                    Log.Debug("Analysis deferred for session {Session}: engine not connected", _sessionId);
                    WaitForEngine();
                    return;
                }

                var request = new CodeAnalysisRequest
                {
                    SessionId       = _sessionId,
                    RequestId       = documentVersion.ToString(),
                    DocumentText    = _buffer.CurrentSnapshot.GetText(),
                    DocumentVersion = documentVersion,
                    // Spec 030 FR-024 / T051: thread the document path so the engine locates the
                    // nearest .casettings and honors per-project rule config + inline suppressions
                    // in the LIVE editor (was hardcoded null → editor always saw global defaults).
                    FilePath        = ResolveDocumentPath()
                };

                var response = await client.SendRequestAsync<CodeAnalysisResponse, CodeAnalysisRequest>(
                    MessageTypes.RequestAnalyze, request, timeoutMs: 10_000, ct: ct);

                if (!ct.IsCancellationRequested)
                {
                    sw.Stop();
                    var issues = response?.Issues ?? Array.Empty<CodeIssueInfo>();
                    _currentIssues = issues; // cache for direct readers (T055 Ctrl-hover popup)
                    Log.Debug("Analysis complete: {Count} findings in {Ms}ms for session {Session}",
                        issues.Length, sw.ElapsedMilliseconds, _sessionId);
                    DiagnosticsUpdated?.Invoke(this, new DiagnosticsUpdatedEventArgs(
                        _buffer.CurrentSnapshot, issues));
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Warning(ex, "AnalysisController: analysis RPC failed for session {Session}", _sessionId);
            }
        }

        /// <summary>Spec 047 (SSMS-002): one wait for the engine, after which the pass runs again (debounced).</summary>
        private void WaitForEngine()
        {
            if (Interlocked.CompareExchange(ref _waitingForEngine, 1, 0) != 0) return;
            _ = WaitForEngineAsync();
        }

        private async Task WaitForEngineAsync()
        {
            try
            {
                var connected = await WhenEngineReady(() =>
                {
                    Interlocked.Exchange(ref _waitingForEngine, 0);
                    if (!_disposed) ScheduleAnalysis();
                    return Task.CompletedTask;
                }).ConfigureAwait(false);

                if (!connected)
                {
                    Interlocked.Exchange(ref _waitingForEngine, 0);
                    Log.Warning("Analysis for session {Session}: the engine did not connect within 10s; the next edit or connection analyses",
                        _sessionId);
                }
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _waitingForEngine, 0);
                Log.Warning(ex, "AnalysisController: waiting for the engine failed for session {Session}", _sessionId);
            }
        }

        /// <summary>
        /// Spec 030 FR-024 / T051: resolve the active document's file path from the buffer's
        /// property bag (same source <see cref="DiagnosticTagger"/> / ErrorListReporter use) so the
        /// engine can find the nearest <c>.casettings</c> and apply per-project rule config + inline
        /// suppressions in the LIVE editor (matching the CLI analyzer). Resolved per-request so a
        /// save/rename after the controller was created is picked up. Returns <c>null</c> for an
        /// unsaved buffer (no directory to search) → the engine falls back to global defaults.
        /// </summary>
        private string? ResolveDocumentPath()
        {
            try
            {
                if (_buffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument textDoc))
                {
                    var path = textDoc?.FilePath;
                    if (!string.IsNullOrEmpty(path)) return path;
                }
            }
            catch { /* property-bag race / disposed buffer — fall back to global defaults */ }
            return null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _buffer.Changed -= OnBufferChanged;
            try { _debounce?.Cancel(); _debounce?.Dispose(); } catch (ObjectDisposedException) { }
        }
    }

    internal sealed class DiagnosticsUpdatedEventArgs(ITextSnapshot snapshot, CodeIssueInfo[] issues) : EventArgs
    {
        public ITextSnapshot Snapshot { get; } = snapshot;
        public CodeIssueInfo[] Issues  { get; } = issues ?? Array.Empty<CodeIssueInfo>();
    }
}
