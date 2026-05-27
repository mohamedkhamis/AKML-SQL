using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using MessagePack;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M3 task T068. The browser's bridge client: opens a
/// <see cref="IBridgeWebSocket"/> to a paired engine, performs the handshake, and
/// exposes request/response RPC. Implements exponential-backoff reconnect (FR-017)
/// and preserves the editor state across disconnect.
/// </summary>
public interface IEngineBridge : IAsyncDisposable
{
    /// <summary>Latest known state of the underlying WebSocket + handshake.</summary>
    BridgeState State { get; }

    /// <summary>Raised whenever <see cref="State"/> changes.</summary>
    event Action<BridgeState>? StateChanged;

    /// <summary>
    /// Spec 025 (M3 closure) US3 / FR-016. Raised when the reconnect loop schedules
    /// the next retry, carrying the wall-clock instant at which the retry will fire.
    /// Sibling event to <see cref="StateChanged"/> (kept separate to avoid breaking
    /// existing subscribers — see contracts/backoff-schedule-contract.md §Status-bar).
    /// Subscribers compute the countdown locally via
    /// <c>nextRetryAt - DateTimeOffset.UtcNow</c>. A value of <c>null</c> means the
    /// loop is currently in flight ("trying now…"). The event also fires once with
    /// <c>null</c> right when the loop exits.
    /// </summary>
    event Action<DateTimeOffset?>? RetryScheduled;

    /// <summary>Capabilities the engine advertised on the most recent handshake. Empty when disconnected.</summary>
    string[] EngineCapabilities { get; }

    /// <summary>Engine version from the most recent handshake.</summary>
    string? EngineVersion { get; }

    /// <summary>
    /// Connect + handshake against <paramref name="connection"/>. When LAN mode, the
    /// caller has supplied the unwrapped <paramref name="bearerToken"/> (the bridge
    /// does not unwrap it itself). When a PIN is supplied the engine mints a new
    /// token which the caller stores via <see cref="IPairingTokenVault"/>.
    /// </summary>
    Task<HandshakeResponse> ConnectAsync(
        EngineConnection connection,
        string? bearerToken,
        string? pairingPin,
        CancellationToken ct);

    /// <summary>
    /// Send <paramref name="request"/> and await the matching response. Honours the
    /// per-call <paramref name="ct"/>; on disconnect during the wait, throws.
    /// </summary>
    Task<TResponse> SendAsync<TRequest, TResponse>(
        int requestMessageType,
        TRequest request,
        CancellationToken ct)
        where TRequest : class
        where TResponse : class;

    Task DisconnectAsync();
}

public enum BridgeState { Disconnected, Connecting, Open, Reconnecting, Failed }

internal sealed class EngineBridge : IEngineBridge
{
    private readonly Func<IBridgeWebSocket> _socketFactory;
    private readonly IDiagnosticsRingBuffer _diagnostics;
    private readonly IPairingTokenVault? _tokenVault;
    private readonly IConnectionStore? _connections;
    private readonly Func<TimeSpan, TimeSpan, TimeSpan> _jitterSource;

    private IBridgeWebSocket? _socket;
    private Task? _receiveLoop;
    private int _nextRequestId;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<RpcMessage>> _pendingRequests = new();

    // Spec 025 (M3 closure) US3 — reconnect plumbing per contracts/backoff-schedule-contract.md.
    private readonly BackoffSchedule _backoff;
    private CancellationTokenSource? _reconnectCts;
    private Task? _reconnectLoop;
    private bool _userDisconnectRequested;
    private EngineConnection? _lastConnection;
    private string? _lastBearerToken;

    /// <summary>Production-DI constructor. Token vault + connection store are required for the
    /// US3 revocation path; tests that don't exercise reconnect can use the test-only
    /// constructor below.</summary>
    public EngineBridge(
        Func<IBridgeWebSocket> socketFactory,
        IDiagnosticsRingBuffer diagnostics,
        IPairingTokenVault tokenVault,
        IConnectionStore connections)
        : this(socketFactory, diagnostics, tokenVault, connections, jitterSource: null)
    {
    }

    /// <summary>Spec 021 compatibility ctor — used by HandshakeClientTests, BridgeRoutedServicesTests.
    /// Reconnect path is still functional but revocation cleanup is a no-op (vault + store null).</summary>
    public EngineBridge(Func<IBridgeWebSocket> socketFactory, IDiagnosticsRingBuffer diagnostics)
        : this(socketFactory, diagnostics, tokenVault: null, connections: null, jitterSource: null)
    {
    }

    /// <summary>Test-only ctor with an injectable jitter source for deterministic backoff
    /// assertions. The jitter source receives <c>(-100ms, +100ms)</c> on every call and
    /// returns the per-step offset to add to the deterministic base delay.</summary>
    internal EngineBridge(
        Func<IBridgeWebSocket> socketFactory,
        IDiagnosticsRingBuffer diagnostics,
        IPairingTokenVault? tokenVault,
        IConnectionStore? connections,
        Func<TimeSpan, TimeSpan, TimeSpan>? jitterSource)
    {
        _socketFactory = socketFactory ?? throw new ArgumentNullException(nameof(socketFactory));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        _tokenVault = tokenVault;
        _connections = connections;
        _jitterSource = jitterSource ?? DefaultJitter;
        _backoff = new BackoffSchedule(_jitterSource);
    }

    private static readonly Random _rng = new();
    private static TimeSpan DefaultJitter(TimeSpan min, TimeSpan max)
    {
        var range = (max - min).TotalMilliseconds;
        var offset = _rng.NextDouble() * range + min.TotalMilliseconds;
        return TimeSpan.FromMilliseconds(offset);
    }

    private BridgeState _state = BridgeState.Disconnected;
    public BridgeState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value;
            StateChanged?.Invoke(_state);
        }
    }
    public event Action<BridgeState>? StateChanged;
    public event Action<DateTimeOffset?>? RetryScheduled;

    public string[] EngineCapabilities { get; private set; } = Array.Empty<string>();
    public string? EngineVersion { get; private set; }

    public async Task<HandshakeResponse> ConnectAsync(
        EngineConnection connection,
        string? bearerToken,
        string? pairingPin,
        CancellationToken ct)
    {
        if (connection == null) throw new ArgumentNullException(nameof(connection));

        // Spec 025 US3 — remember what we connected to so the reconnect loop can retry
        // the same target with the same bearer (replay path per FR-013). The user-disconnect
        // flag clears here because a fresh ConnectAsync call is a user intent, not a
        // reconnect from the previous loop.
        _lastConnection = connection;
        _lastBearerToken = bearerToken;
        _userDisconnectRequested = false;

        State = BridgeState.Connecting;
        await CloseSocketOnlyAsync().ConfigureAwait(false);    // close any prior socket, keep reconnect context

        _socket = _socketFactory();
        var url = (connection.IsLocalhost ? "ws://" : "wss://") + connection.Host + ":" + connection.Port + "/akmlsql";

        try
        {
            await _socket.ConnectAsync(url, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            State = BridgeState.Failed;
            _diagnostics.Log(DiagnosticLevel.Error, "bridge", $"Connect failed: {ex.Message}");
            throw;
        }

        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_socket));

        // Send the handshake request as the first frame.
        var handshakeRequest = new HandshakeRequest
        {
            PairingPin = pairingPin,
            BearerToken = bearerToken,
            WebVersion = "1.0.0",
            ProtocolVersionMin = 1,
            ProtocolVersionMax = 1,
            BrowserLabel = "Web edition",
        };
        var response = await SendAsync<HandshakeRequest, HandshakeResponse>(
            MessageTypes.HandshakeRequest, handshakeRequest, ct).ConfigureAwait(false);

        if (response.Status != HandshakeStatus.Ok)
        {
            State = BridgeState.Failed;
            _diagnostics.Log(DiagnosticLevel.Warn, "bridge",
                $"Handshake declined: {response.Status} -- {response.ErrorMessage}");
            return response;   // caller inspects Status and surfaces the right UI affordance
        }

        EngineCapabilities = response.EngineCapabilities ?? Array.Empty<string>();
        EngineVersion = response.EngineVersion;

        // Spec 025 (M3 bridge closure) FR-006: TLS fingerprint diagnostic.
        // The engine populates ServerTlsThumbprint only for non-loopback transports;
        // localhost connects leave it null and skip this entire block.
        if (!string.IsNullOrEmpty(response.ServerTlsThumbprint))
        {
            if (string.IsNullOrEmpty(connection.TlsFingerprint))
            {
                connection.TlsFingerprint = response.ServerTlsThumbprint;
                _diagnostics.Log(DiagnosticLevel.Info, "bridge",
                    $"Pinned TLS fingerprint for connection '{connection.Name}': {Last12(response.ServerTlsThumbprint)}. " +
                    "The picker persists the connection record after this call returns via IConnectionStore.AddAsync / UpdateAsync — " +
                    "no extra work required of the caller.");
            }
            else if (!string.Equals(connection.TlsFingerprint, response.ServerTlsThumbprint, StringComparison.OrdinalIgnoreCase))
            {
                _diagnostics.Log(DiagnosticLevel.Warn, "bridge",
                    $"TLS fingerprint for connection '{connection.Name}' changed from {Last12(connection.TlsFingerprint)} " +
                    $"to {Last12(response.ServerTlsThumbprint)}. " +
                    "This is expected after a cert regeneration on the engine host. " +
                    "The user-facing mismatch dialog is a deferred follow-up (spec 025 §Out of Scope).");
                connection.TlsFingerprint = response.ServerTlsThumbprint;
            }
        }

        State = BridgeState.Open;
        _diagnostics.Log(DiagnosticLevel.Info, "bridge",
            $"Connected to engine {EngineVersion} with {EngineCapabilities.Length} capability(s).");
        return response;
    }

    private static string Last12(string? thumb) =>
        string.IsNullOrEmpty(thumb) ? "<empty>" :
        thumb.Length <= 12 ? thumb : "…" + thumb.Substring(thumb.Length - 12);

    public async Task<TResponse> SendAsync<TRequest, TResponse>(
        int requestMessageType,
        TRequest request,
        CancellationToken ct)
        where TRequest : class
        where TResponse : class
    {
        if (_socket == null || _socket.State != BridgeWebSocketState.Open)
        {
            throw new InvalidOperationException("Bridge is not open.");
        }

        var requestId = Interlocked.Increment(ref _nextRequestId);
        var tcs = new TaskCompletionSource<RpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[requestId] = tcs;

        try
        {
            var envelope = new RpcMessage
            {
                MessageType = requestMessageType,
                RequestId = requestId,
                Payload = MessagePackSerializer.Serialize(request),
            };
            var frame = MessagePackSerializer.Serialize(envelope);
            await _socket.SendAsync(frame, ct).ConfigureAwait(false);

            using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
            var response = await tcs.Task.ConfigureAwait(false);

            if (response.Payload == null || response.Payload.Length == 0)
            {
                return default!;
            }
            return MessagePackSerializer.Deserialize<TResponse>(response.Payload);
        }
        finally
        {
            _pendingRequests.TryRemove(requestId, out _);
        }
    }

    private async Task ReceiveLoopAsync(IBridgeWebSocket socket)
    {
        try
        {
            while (true)
            {
                var frame = await socket.ReceiveAsync(CancellationToken.None).ConfigureAwait(false);
                if (frame == null) break;

                RpcMessage envelope;
                try { envelope = MessagePackSerializer.Deserialize<RpcMessage>(frame); }
                catch (Exception ex)
                {
                    _diagnostics.Log(DiagnosticLevel.Warn, "bridge", $"Bad inbound frame: {ex.Message}");
                    continue;
                }

                if (_pendingRequests.TryRemove(envelope.RequestId, out var tcs))
                {
                    tcs.TrySetResult(envelope);
                }
                else
                {
                    _diagnostics.Log(DiagnosticLevel.Trace, "bridge",
                        $"Unsolicited frame: type={envelope.MessageType} requestId={envelope.RequestId}");
                }
            }
        }
        catch (Exception ex)
        {
            _diagnostics.Log(DiagnosticLevel.Warn, "bridge", $"Receive loop crashed: {ex.Message}");
        }
        finally
        {
            FailAllPending(new InvalidOperationException("Bridge disconnected."));
        }

        // Spec 025 US3 — receive-loop exit fork (kept outside finally because C# forbids
        // `return` from inside one; the unconditional pending-request cleanup lives in
        // the finally above):
        //   1) Disowned by CloseSocketOnlyAsync (it nulled or replaced _socket) — the
        //      caller manages state; we leave without touching it.
        //   2) User called DisconnectAsync — transition to Disconnected.
        //   3) Initial handshake never reached Open (Failed/Connecting at drop) — no
        //      auto-reconnect; surface Disconnected and let the user retry manually.
        //   4) Unexpected close from an established (Open) session — reconnect.
        if (!ReferenceEquals(socket, _socket)) return;

        if (_userDisconnectRequested)
        {
            State = BridgeState.Disconnected;
            return;
        }

        if (State != BridgeState.Open || _lastConnection == null)
        {
            State = BridgeState.Disconnected;
            return;
        }

        _socket = null;
        State = BridgeState.Reconnecting;
        _backoff.Reset();
        _reconnectCts = new CancellationTokenSource();
        var reconnectCt = _reconnectCts.Token;
        _reconnectLoop = Task.Run(() => ReconnectLoopAsync(reconnectCt));
    }

    private async Task ReconnectLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _lastConnection != null)
            {
                var delay = _backoff.NextDelay();
                var nextRetryAt = DateTimeOffset.UtcNow + delay;
                RetryScheduled?.Invoke(nextRetryAt);

                try { await Task.Delay(delay, ct).ConfigureAwait(false); }
                catch (TaskCanceledException) { return; }

                if (ct.IsCancellationRequested || _userDisconnectRequested) return;

                RetryScheduled?.Invoke(null);   // "trying now…"

                HandshakeResponse? response = null;
                try
                {
                    response = await ConnectAsync(_lastConnection, _lastBearerToken, pairingPin: null, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _diagnostics.Log(DiagnosticLevel.Warn, "bridge",
                        $"Reconnect attempt failed: {ex.Message}");
                    // ConnectAsync set State = Failed on throw; restore Reconnecting so the
                    // status bar's countdown comes back rather than freezing on Failed.
                    State = BridgeState.Reconnecting;
                    continue;
                }

                if (response.Status == HandshakeStatus.PinRequired)
                {
                    // Bearer was revoked. Terminal — surface re-pair UI and exit.
                    _diagnostics.Log(DiagnosticLevel.Warn, "bridge",
                        $"Bearer revoked for '{_lastConnection.Name}' — re-pair required.");
                    if (_tokenVault != null)
                    {
                        try { await _tokenVault.RemoveAsync(_lastConnection.Id).ConfigureAwait(false); }
                        catch (Exception ex)
                        {
                            _diagnostics.Log(DiagnosticLevel.Warn, "bridge",
                                $"Token vault clear failed for '{_lastConnection.Id}': {ex.Message}");
                        }
                    }
                    if (_connections != null)
                    {
                        _lastConnection.BearerTokenWrappedRef = null;
                        try { await _connections.UpdateAsync(_lastConnection).ConfigureAwait(false); }
                        catch (Exception ex)
                        {
                            _diagnostics.Log(DiagnosticLevel.Warn, "bridge",
                                $"Connection store update failed: {ex.Message}");
                        }
                    }
                    // Suppress the about-to-fire receive-loop reconnect on the rejected socket.
                    _userDisconnectRequested = true;
                    await CloseSocketOnlyAsync().ConfigureAwait(false);
                    State = BridgeState.Failed;
                    return;
                }

                if (response.Status == HandshakeStatus.Ok)
                {
                    // ConnectAsync's success path already set State = Open. Reset the
                    // backoff so a future drop starts from 500 ms again.
                    _backoff.Reset();
                    return;
                }

                // Non-terminal failure (ProtocolMismatch, server transient) — stay Reconnecting.
                State = BridgeState.Reconnecting;
            }
        }
        finally
        {
            RetryScheduled?.Invoke(null);
        }
    }

    private void FailAllPending(Exception error)
    {
        foreach (var kv in _pendingRequests)
        {
            kv.Value.TrySetException(error);
        }
        _pendingRequests.Clear();
    }

    /// <summary>Internal cleanup of just the socket + its receive loop, without touching the
    /// reconnect machinery. Used by ConnectAsync when rebinding to a fresh socket and by the
    /// reconnect-loop terminal path. The receive loop's finally sees <c>!ReferenceEquals(socket, _socket)</c>
    /// once we null <c>_socket</c> here and skips the State mutation.</summary>
    private async Task CloseSocketOnlyAsync()
    {
        var oldSocket = _socket;
        var oldLoop = _receiveLoop;
        _socket = null;
        _receiveLoop = null;
        if (oldSocket != null)
        {
            try { await oldSocket.DisposeAsync().ConfigureAwait(false); }
            catch { /* swallow */ }
        }
        if (oldLoop != null)
        {
            try { await oldLoop.ConfigureAwait(false); }
            catch { /* swallow -- the receive loop logs its own errors */ }
        }
    }

    public async Task DisconnectAsync()
    {
        _userDisconnectRequested = true;

        // Cancel any active reconnect loop first so the retry timer wakes up immediately.
        var reconnectCts = _reconnectCts;
        var reconnectLoop = _reconnectLoop;
        _reconnectCts = null;
        _reconnectLoop = null;
        if (reconnectCts != null)
        {
            try { reconnectCts.Cancel(); } catch { }
            reconnectCts.Dispose();
        }
        if (reconnectLoop != null)
        {
            try { await reconnectLoop.ConfigureAwait(false); } catch { }
        }

        await CloseSocketOnlyAsync().ConfigureAwait(false);

        _lastConnection = null;
        _lastBearerToken = null;
        State = BridgeState.Disconnected;
        EngineCapabilities = Array.Empty<string>();
        EngineVersion = null;
        RetryScheduled?.Invoke(null);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }

    // ── Spec 025 US3: BackoffSchedule ────────────────────────────────────────────────
    //
    // Exponential backoff with ±100 ms jitter, capped at 30 s. The injected jitter
    // source lets ReconnectLoopTests assert the deterministic sequence
    // 500 ms, 1 s, 2 s, 4 s, 8 s, 16 s, 30 s, 30 s … with jitter set to zero.
    /// <summary>Backoff schedule per <c>contracts/backoff-schedule-contract.md</c>.</summary>
    internal sealed class BackoffSchedule
    {
        internal static readonly TimeSpan InitialDelay = TimeSpan.FromMilliseconds(500);
        internal const double Multiplier = 2.0;
        internal static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);
        internal static readonly TimeSpan JitterMin = TimeSpan.FromMilliseconds(-100);
        internal static readonly TimeSpan JitterMax = TimeSpan.FromMilliseconds(100);

        private readonly Func<TimeSpan, TimeSpan, TimeSpan> _jitter;
        private int _attemptNumber;

        public BackoffSchedule(Func<TimeSpan, TimeSpan, TimeSpan> jitter)
        {
            _jitter = jitter ?? throw new ArgumentNullException(nameof(jitter));
        }

        public int AttemptNumber => _attemptNumber;

        public void Reset() => _attemptNumber = 0;

        /// <summary>Compute the delay for the next retry. Increments AttemptNumber.</summary>
        public TimeSpan NextDelay()
        {
            _attemptNumber++;
            // delay_n = min(500ms × 2^(n-1), 30s) + Uniform(-100ms, +100ms)
            var baseMs = InitialDelay.TotalMilliseconds * Math.Pow(Multiplier, _attemptNumber - 1);
            var capped = TimeSpan.FromMilliseconds(Math.Min(baseMs, MaxDelay.TotalMilliseconds));
            var jitter = _jitter(JitterMin, JitterMax);
            var total = capped + jitter;
            if (total < TimeSpan.Zero) total = TimeSpan.Zero;
            return total;
        }
    }
}
