using System;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using MessagePack;
using Serilog;

namespace AkmlSql.Engine.Transports
{
    /// <summary>
    /// Spec 021 (web edition) -- M3 task T056. WebSocket transport for the engine bridge:
    /// the browser-side <c>EngineConnection</c> (M3 T068) connects to this. One WebSocket
    /// binary message = one MessagePack <see cref="RpcMessage"/> payload. The WebSocket
    /// protocol handles framing; there is no additional <c>[length][CRC]</c> envelope (the
    /// named-pipe envelope was a hand-rolled framing layer that WebSocket replaces).
    ///
    /// Localhost vs LAN
    ///   localhost (default): plaintext ws://, no pairing token required, accepts any
    ///                        loopback connection.
    ///   LAN (T058+):         wss:// only, with installer-generated self-signed cert,
    ///                        pairing-PIN handshake required (T060 -> T063).
    ///
    /// Implementation note: uses <see cref="HttpListener"/> for the HTTP-upgrade dance and
    /// <see cref="WebSocket"/> for the post-upgrade framed stream. This avoids the
    /// Kestrel/AspNetCore dependency on the engine's lightweight console-host model. TLS
    /// support (T058) will add a parallel Kestrel-hosted variant for the LAN-exposed mode;
    /// the localhost path stays on HttpListener since it doesn't need TLS.
    /// </summary>
    public sealed class WebSocketTransport : IRpcTransport
    {
        private readonly WebSocketTransportOptions _options;
        private HttpListener? _listener;
        private CancellationTokenSource? _acceptCts;
        private Task? _acceptLoop;
        private volatile bool _disposed;

        public WebSocketTransport(WebSocketTransportOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));

            // Spec 021 T056: LAN-mode TLS check. Implementation lands in T058.
            if (!_options.IsLoopback && string.IsNullOrEmpty(_options.TlsCertPath))
            {
                throw new InvalidOperationException(
                    "WebSocketTransport: LAN-mode binding (BindAddress != loopback) requires TlsCertPath. " +
                    "Spec 021 FR-013a forbids plaintext WebSocket over LAN. Set TlsCertPath in config.json " +
                    "or bind to 127.0.0.1 for localhost-only mode.");
            }
        }

        /// <inheritdoc />
        public event Func<RpcMessage, CancellationToken, Task<RpcMessage?>>? RequestReceived;

        /// <inheritdoc />
        public Task StartAsync(CancellationToken ct)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WebSocketTransport));
            if (_listener != null) throw new InvalidOperationException("WebSocketTransport already started.");

            // HttpListener uses URI prefix notation; "+" binds all interfaces, but we
            // honour the configured BindAddress for clarity. Loopback uses 127.0.0.1.
            var host = _options.IsLoopback ? "127.0.0.1" : _options.BindAddress;
            var prefix = $"http://{host}:{_options.Port}/";

            _listener = new HttpListener();
            _listener.Prefixes.Add(prefix);

            try
            {
                _listener.Start();
            }
            catch (HttpListenerException ex)
            {
                _listener = null;
                throw new InvalidOperationException(
                    $"WebSocketTransport: failed to bind {prefix}. " +
                    "On Windows, non-localhost prefixes may need 'netsh http add urlacl' or admin rights. " +
                    "See the installer's LAN-mode setup (T088).",
                    ex);
            }

            _acceptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _acceptLoop = Task.Run(() => AcceptLoopAsync(_acceptCts.Token));

            Log.Information("WebSocketTransport listening on {Prefix} ({Mode})",
                prefix, _options.IsLoopback ? "localhost" : "LAN");
            return Task.CompletedTask;
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener?.IsListening == true)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (HttpListenerException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                if (!context.Request.IsWebSocketRequest)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    context.Response.Close();
                    continue;
                }

                // Optional: reject non-loopback connections in localhost mode.
                if (_options.IsLoopback && !IsLoopback(context.Request.RemoteEndPoint?.Address))
                {
                    Log.Warning("WebSocketTransport: rejected non-loopback connection from {Addr}",
                        context.Request.RemoteEndPoint);
                    context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                    context.Response.Close();
                    continue;
                }

                _ = Task.Run(() => HandleConnectionAsync(context, ct));
            }
        }

        private static bool IsLoopback(IPAddress? addr) =>
            addr != null && IPAddress.IsLoopback(addr);

        private async Task HandleConnectionAsync(HttpListenerContext context, CancellationToken ct)
        {
            WebSocketContext? wsContext = null;
            try
            {
                wsContext = await context.AcceptWebSocketAsync(subProtocol: null).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "WebSocketTransport: AcceptWebSocketAsync failed");
                try { context.Response.Close(); } catch { /* ignore */ }
                return;
            }

            using var ws = wsContext.WebSocket;
            await ServeAsync(ws, ct).ConfigureAwait(false);
        }

        private async Task ServeAsync(WebSocket socket, CancellationToken ct)
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                RpcMessage? request;
                try
                {
                    request = await ReadMessageAsync(socket, ct).ConfigureAwait(false);
                }
                catch (WebSocketException) { break; }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log.Warning(ex, "WebSocketTransport: read failed");
                    break;
                }

                if (request == null) break;   // peer closed

                RpcMessage? response = null;
                var handler = RequestReceived;
                if (handler != null)
                {
                    try
                    {
                        response = await handler(request, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "WebSocketTransport: handler failed for MessageType={Type}", request.MessageType);
                        response = RpcResponseFactory.CreateErrorResponse(ex.Message, request.RequestId);
                    }
                }

                if (response != null)
                {
                    try
                    {
                        await WriteMessageAsync(socket, response, ct).ConfigureAwait(false);
                    }
                    catch (WebSocketException) { break; }
                }
            }

            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "transport shutting down", CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch { /* best effort */ }
            }
        }

        // ----- Frame I/O ------------------------------------------------------
        // One WebSocket binary message = one MessagePack(RpcMessage) payload.
        // Per contracts/rpc-transport-abstraction.md, no [length][CRC] envelope
        // here -- WebSocket provides framing already.

        private const int InitialReceiveBufferSize = 4 * 1024;
        private const int MaxMessageSize = 16 * 1024 * 1024;   // 16 MB to match named pipe

        private static async Task<RpcMessage?> ReadMessageAsync(WebSocket socket, CancellationToken ct)
        {
            using var ms = new System.IO.MemoryStream();
            var buffer = new byte[InitialReceiveBufferSize];
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) return null;
                if (result.MessageType != WebSocketMessageType.Binary)
                {
                    throw new InvalidDataException(
                        $"WebSocketTransport: expected binary message, got {result.MessageType}.");
                }
                ms.Write(buffer, 0, result.Count);
                if (ms.Length > MaxMessageSize)
                {
                    throw new InvalidDataException(
                        $"WebSocketTransport: message exceeds {MaxMessageSize} bytes.");
                }
            }
            while (!result.EndOfMessage);

            return MessagePackSerializer.Deserialize<RpcMessage>(ms.ToArray(), cancellationToken: ct);
        }

        private static async Task WriteMessageAsync(WebSocket socket, RpcMessage message, CancellationToken ct)
        {
            var payload = MessagePackSerializer.Serialize(message, cancellationToken: ct);
            await socket.SendAsync(
                new ArraySegment<byte>(payload),
                WebSocketMessageType.Binary,
                endOfMessage: true,
                cancellationToken: ct).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            try { _acceptCts?.Cancel(); } catch { /* ignore */ }
            try { _listener?.Stop(); } catch { /* ignore */ }
            try { _listener?.Close(); } catch { /* ignore */ }

            if (_acceptLoop != null)
            {
                try { await _acceptLoop.ConfigureAwait(false); }
                catch { /* swallow accept-loop shutdown errors */ }
            }

            _acceptCts?.Dispose();
        }
    }
}
