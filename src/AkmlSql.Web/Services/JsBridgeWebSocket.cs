using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M3 task T068. Production <see cref="IBridgeWebSocket"/>
/// backed by the browser's WebSocket via <c>wwwroot/js/akml-bridge.js</c>. Each
/// instance manages one underlying socket; <see cref="EngineBridge"/> creates a fresh
/// one per <see cref="EngineBridge.ConnectAsync"/> call.
/// </summary>
internal sealed class JsBridgeWebSocket : IBridgeWebSocket
{
    private readonly IJSRuntime _js;
    private readonly Lazy<Task<IJSObjectReference>> _moduleLoader;
    private string? _socketId;

    public JsBridgeWebSocket(IJSRuntime js)
    {
        _js = js ?? throw new ArgumentNullException(nameof(js));
        _moduleLoader = new Lazy<Task<IJSObjectReference>>(() =>
            _js.InvokeAsync<IJSObjectReference>("import", "./js/akml-bridge.js").AsTask());
    }

    /// <summary>
    /// Tracked optimistically on connect/dispose. We don't poll the JS side per-call
    /// because the receive loop already surfaces a null frame on close, which
    /// EngineBridge uses to drive its state machine.
    /// </summary>
    public BridgeWebSocketState State { get; private set; } = BridgeWebSocketState.Closed;

    public async Task ConnectAsync(string url, CancellationToken ct)
    {
        State = BridgeWebSocketState.Connecting;
        var mod = await _moduleLoader.Value.ConfigureAwait(false);
        _socketId = await mod.InvokeAsync<string>("connect", ct, url).ConfigureAwait(false);
        State = BridgeWebSocketState.Open;
    }

    public async Task SendAsync(byte[] frame, CancellationToken ct)
    {
        if (_socketId == null) throw new InvalidOperationException("Socket is not connected.");
        var mod = await _moduleLoader.Value.ConfigureAwait(false);
        await mod.InvokeVoidAsync("send", ct, _socketId, frame).ConfigureAwait(false);
    }

    public async Task<byte[]?> ReceiveAsync(CancellationToken ct)
    {
        if (_socketId == null) return null;
        var mod = await _moduleLoader.Value.ConfigureAwait(false);
        return await mod.InvokeAsync<byte[]?>("receive", ct, _socketId).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        State = BridgeWebSocketState.Closing;
        if (_socketId == null) { State = BridgeWebSocketState.Closed; return; }
        try
        {
            var mod = await _moduleLoader.Value.ConfigureAwait(false);
            await mod.InvokeVoidAsync("close", _socketId).ConfigureAwait(false);
        }
        catch (JSDisconnectedException) { /* tab closing */ }
        catch { /* swallow on disposal */ }
        _socketId = null;
        State = BridgeWebSocketState.Closed;
    }
}

/// <summary>
/// Test-only loopback WebSocket. Hands frames back to a server-side callback the
/// test provides; the server's responses are queued for the bridge to read. Lets
/// HandshakeClientTests round-trip the handshake without spinning up a real engine.
/// </summary>
public sealed class FakeBridgeWebSocket : IBridgeWebSocket
{
    private readonly Func<byte[], byte[]?> _serverHandler;
    private readonly System.Collections.Concurrent.ConcurrentQueue<byte[]> _inbox = new();
    private readonly SemaphoreSlim _signal = new(0);
    public BridgeWebSocketState State { get; private set; } = BridgeWebSocketState.Closed;

    public FakeBridgeWebSocket(Func<byte[], byte[]?> serverHandler)
    {
        _serverHandler = serverHandler ?? throw new ArgumentNullException(nameof(serverHandler));
    }

    public Task ConnectAsync(string url, CancellationToken ct)
    {
        State = BridgeWebSocketState.Open;
        return Task.CompletedTask;
    }

    public Task SendAsync(byte[] frame, CancellationToken ct)
    {
        if (State != BridgeWebSocketState.Open) throw new InvalidOperationException("Not open.");
        // Invoke the test's server handler synchronously and queue the response.
        var response = _serverHandler(frame);
        if (response != null)
        {
            _inbox.Enqueue(response);
            _signal.Release();
        }
        return Task.CompletedTask;
    }

    public async Task<byte[]?> ReceiveAsync(CancellationToken ct)
    {
        try
        {
            await _signal.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return null; }
        return _inbox.TryDequeue(out var frame) ? frame : null;
    }

    public ValueTask DisposeAsync()
    {
        State = BridgeWebSocketState.Closed;
        _signal.Release();    // unblock any pending Receive
        return default;
    }
}
