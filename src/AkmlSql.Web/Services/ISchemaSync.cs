using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M5 task T108. Drives the change-detection protocol per
/// contracts/schema-cache-shape.md:
/// <list type="bullet">
///   <item>While the editor is active and the bridge is reachable, poll
///   <c>SchemaChecksumRequest</c> every 30 s.</item>
///   <item>Pause polling after 5 min of editor idle (no edit + no keyboard focus).</item>
///   <item>Resume polling within 1 s of the next keystroke / focus event.</item>
///   <item>On checksum drift: trigger a Phase A refresh, then Phase B in the background.</item>
/// </list>
///
/// The implementation is driven by an external <see cref="ReportEditorActive"/> hook
/// the Editor.razor page calls on every keystroke; the sync service tracks the
/// timestamp and decides whether to poll on its 30 s timer tick.
/// </summary>
public interface ISchemaSync : IAsyncDisposable
{
    /// <summary>Start the polling timer. Idempotent.</summary>
    Task StartAsync(string sessionId, string serverCanonicalIdentity, string databaseName);

    /// <summary>Stop polling for the current (server, db). The cache entry is retained.</summary>
    Task StopAsync();

    /// <summary>The editor page calls this on every keystroke / focus event.</summary>
    void ReportEditorActive();

    /// <summary>Raised whenever the engine reports a checksum that differs from the cached value.</summary>
    event Action<ChecksumDriftNotice>? ChecksumDrifted;
}

public sealed record ChecksumDriftNotice(string ServerCanonicalIdentity, string DatabaseName, string NewChecksum);

internal sealed class SchemaSync : ISchemaSync
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan IdleSuspendThreshold = TimeSpan.FromMinutes(5);

    private readonly ISchemaCacheStore _cache;
    private readonly IEngineBridge _bridge;
    private readonly IDiagnosticsRingBuffer _diagnostics;

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private DateTimeOffset _lastEditorActivity = DateTimeOffset.UtcNow;
    private string? _sessionId;
    private string? _server;
    private string? _database;

    public SchemaSync(ISchemaCacheStore cache, IEngineBridge bridge, IDiagnosticsRingBuffer diagnostics)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public event Action<ChecksumDriftNotice>? ChecksumDrifted;

    public Task StartAsync(string sessionId, string serverCanonicalIdentity, string databaseName)
    {
        if (_cts != null) return Task.CompletedTask;   // already running

        _sessionId = sessionId;
        _server = serverCanonicalIdentity;
        _database = databaseName;
        _cts = new CancellationTokenSource();
        _lastEditorActivity = DateTimeOffset.UtcNow;
        _loop = Task.Run(() => RunLoopAsync(_cts.Token));
        _diagnostics.Log(DiagnosticLevel.Info, "schema-sync",
            $"Started polling for {_server}/{_database}.");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cts == null) return;
        _cts.Cancel();
        try { if (_loop != null) await _loop.ConfigureAwait(false); }
        catch { /* swallow */ }
        _cts.Dispose();
        _cts = null;
        _loop = null;
        _sessionId = null;
        _server = null;
        _database = null;
    }

    public void ReportEditorActive() => _lastEditorActivity = DateTimeOffset.UtcNow;

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, ct).ConfigureAwait(false);
            }
            catch (TaskCanceledException) { return; }

            if (IsIdle())
            {
                continue;   // suspend polling while idle; ReportEditorActive resumes naturally.
            }

            if (_sessionId == null || _server == null || _database == null) continue;

            try
            {
                await PollOnceAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _diagnostics.Log(DiagnosticLevel.Warn, "schema-sync", $"Poll failed: {ex.Message}");
            }
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        // When the bridge is closed we can still refresh LastUsedAt to keep LRU warm.
        if (_bridge.State != BridgeState.Open)
        {
            await _cache.TouchAsync(_server!, _database!).ConfigureAwait(false);
            return;
        }

        var response = await _bridge.SendAsync<SchemaChecksumRequest, SchemaChecksumResponse>(
            MessageTypes.SchemaChecksumRequest,
            new SchemaChecksumRequest
            {
                SessionId = _sessionId!,
                ServerCanonicalIdentity = _server!,
                DatabaseName = _database!,
            },
            ct).ConfigureAwait(false);

        if (!response.HasConnection)
        {
            // Engine has no live connection -- treat as idle for this poll.
            await _cache.TouchAsync(_server!, _database!).ConfigureAwait(false);
            return;
        }

        var existing = await _cache.GetAsync(_server!, _database!).ConfigureAwait(false);
        var cachedChecksum = existing?.Checksum ?? string.Empty;

        if (!string.Equals(cachedChecksum, response.Checksum, StringComparison.Ordinal))
        {
            _diagnostics.Log(DiagnosticLevel.Info, "schema-sync",
                $"Checksum drift detected for {_server}/{_database}: '{cachedChecksum}' -> '{response.Checksum}'");
            if (existing != null)
            {
                existing.Checksum = response.Checksum;
                await _cache.SetAsync(existing).ConfigureAwait(false);
            }
            ChecksumDrifted?.Invoke(new ChecksumDriftNotice(_server!, _database!, response.Checksum));
        }
        else
        {
            await _cache.TouchAsync(_server!, _database!).ConfigureAwait(false);
        }
    }

    private bool IsIdle() =>
        DateTimeOffset.UtcNow - _lastEditorActivity > IdleSuspendThreshold;

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    // ── Test surface ─────────────────────────────────────────────────────────
    internal bool IsRunning => _cts != null && !_cts.IsCancellationRequested;
    internal DateTimeOffset LastEditorActivity => _lastEditorActivity;

    /// <summary>Test helper: force a single poll without waiting for the timer tick.</summary>
    internal Task PollNowAsync(string sessionId, string server, string database, CancellationToken ct = default)
    {
        _sessionId = sessionId;
        _server = server;
        _database = database;
        _lastEditorActivity = DateTimeOffset.UtcNow;
        return PollOnceAsync(ct);
    }
}
