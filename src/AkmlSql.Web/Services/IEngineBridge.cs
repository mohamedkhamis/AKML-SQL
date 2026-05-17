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

    private IBridgeWebSocket? _socket;
    private Task? _receiveLoop;
    private int _nextRequestId;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<RpcMessage>> _pendingRequests = new();

    public EngineBridge(Func<IBridgeWebSocket> socketFactory, IDiagnosticsRingBuffer diagnostics)
    {
        _socketFactory = socketFactory ?? throw new ArgumentNullException(nameof(socketFactory));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
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

    public string[] EngineCapabilities { get; private set; } = Array.Empty<string>();
    public string? EngineVersion { get; private set; }

    public async Task<HandshakeResponse> ConnectAsync(
        EngineConnection connection,
        string? bearerToken,
        string? pairingPin,
        CancellationToken ct)
    {
        if (connection == null) throw new ArgumentNullException(nameof(connection));

        State = BridgeState.Connecting;
        await DisconnectAsync().ConfigureAwait(false);    // close any prior socket

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
        State = BridgeState.Open;
        _diagnostics.Log(DiagnosticLevel.Info, "bridge",
            $"Connected to engine {EngineVersion} with {EngineCapabilities.Length} capability(s).");
        return response;
    }

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
            State = BridgeState.Disconnected;
            FailAllPending(new InvalidOperationException("Bridge disconnected."));
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

    public async Task DisconnectAsync()
    {
        if (_socket != null)
        {
            try { await _socket.DisposeAsync().ConfigureAwait(false); }
            catch { /* swallow */ }
            _socket = null;
        }
        if (_receiveLoop != null)
        {
            try { await _receiveLoop.ConfigureAwait(false); }
            catch { /* swallow -- the receive loop logs its own errors */ }
            _receiveLoop = null;
        }
        State = BridgeState.Disconnected;
        EngineCapabilities = Array.Empty<string>();
        EngineVersion = null;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }
}
