using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Pairing;
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
            //
            // Spec 025 (M3 bridge closure) FR-001..FR-004: non-loopback bindings use
            // `https://` so WinHTTP terminates TLS via the cert the installer already
            // bound with `netsh http add sslcert ipport=<addr>:<port> certhash=<thumb>`
            // (spec 021 T088 -- web-tls-setup.ps1). Loopback path stays `http://` --
            // it does not need TLS and the existing engine tests rely on that.
            var host = _options.IsLoopback ? "127.0.0.1" : _options.BindAddress;
            var scheme = _options.IsLoopback ? "http" : "https";
            var prefix = $"{scheme}://{host}:{_options.Port}/";

            if (!_options.IsLoopback)
            {
                ValidateCertBindingOrThrow(_options.TlsCertPath, _options.Port);
            }

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

            // Spec 026 (M4 closure) FR-013a: publish this connection's remote IP as a
            // per-connection ambient so the handshake's pinValidator uses the real source
            // as PairingService's rate-limit bucket key. The scope restores the previous
            // value on connection end so values never leak across connections.
            using (BridgeSourceIp.Set(context.Request.RemoteEndPoint?.Address))
            {
                await ServeAsync(ws, ct).ConfigureAwait(false);
            }
        }

        private async Task ServeAsync(WebSocket socket, CancellationToken ct)
        {
            // Spec 026 (M4 closure) C1 / FR-013c / SC-010: per-connection authentication gate.
            // When the transport requires pairing (LAN mode -- RequirePairingToken is forced true
            // for any non-loopback binding, see EngineHost.BuildWebSocketTransport), a connection
            // may send ONLY a HandshakeRequest until it completes a successful (Status == Ok)
            // handshake. Every other message before that is rejected WITHOUT being dispatched to the
            // router, so the PIN/bearer handshake is a hard precondition for every data-plane RPC --
            // not an advisory message an attacker can skip. Loopback / named-pipe deployments leave
            // RequirePairingToken false, so the gate opens immediately and their behaviour is unchanged.
            var authenticated = !_options.RequirePairingToken;

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

                // Auth gate: refuse any non-handshake frame until this connection is authenticated.
                // The frame is never handed to the router, so unauthenticated callers cannot reach
                // formatting / schema / AI / session handlers.
                if (!authenticated && request.MessageType != MessageTypes.HandshakeRequest)
                {
                    Log.Warning(
                        "WebSocketTransport: rejected pre-handshake MessageType={Type} from unauthenticated connection",
                        request.MessageType);
                    try
                    {
                        await WriteMessageAsync(
                            socket,
                            RpcResponseFactory.CreateErrorResponse(
                                "Authentication required: complete the pairing handshake before sending requests.",
                                request.RequestId),
                            ct).ConfigureAwait(false);
                    }
                    catch (WebSocketException) { break; }
                    continue;
                }

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

                // Open the gate ONLY on a genuine, successful handshake response. The MessageType
                // check is load-bearing: HandshakeResponse.Status defaults to "ok", so an error
                // envelope (or any other response) blindly deserialised as a HandshakeResponse would
                // otherwise read as authenticated and re-open the bypass this fix closes.
                if (!authenticated &&
                    request.MessageType == MessageTypes.HandshakeRequest &&
                    response != null &&
                    response.MessageType == MessageTypes.HandshakeResponse)
                {
                    authenticated = TryReadHandshakeOk(response);
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

        /// <summary>
        /// Spec 026 (M4 closure) C1. Returns true only when <paramref name="response"/>'s payload
        /// deserialises to a <see cref="HandshakeResponse"/> whose <c>Status</c> is
        /// <see cref="HandshakeStatus.Ok"/>. Any deserialise failure or non-Ok status returns false
        /// (fail-closed). Callers MUST already have verified the envelope MessageType is
        /// <see cref="MessageTypes.HandshakeResponse"/> before trusting the result.
        /// </summary>
        private static bool TryReadHandshakeOk(RpcMessage response)
        {
            try
            {
                var payload = response.Payload;
                if (payload == null || payload.Length == 0) return false;
                var hs = MessagePackSerializer.Deserialize<HandshakeResponse>(payload);
                return hs != null && hs.Status == HandshakeStatus.Ok;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "WebSocketTransport: could not read handshake response status; treating as unauthenticated");
                return false;
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

        // ----- LAN-mode cert binding validation -----------------------------
        // Spec 025 (M3 bridge closure) FR-002 + Research Decision 5: before opening
        // the HTTPS listener, verify the configured PFX exists on disk and its
        // thumbprint matches the active `netsh http show sslcert ipport=<port>`
        // binding. Mismatch throws with both thumbprints in the message so the
        // operator can diagnose without re-running the installer.

        /// <summary>
        /// Spec 025 (M3 bridge closure) FR-006. SHA-1 hex thumbprint of the LAN-mode
        /// TLS certificate the transport is currently serving. Set by
        /// <see cref="ValidateCertBindingOrThrow"/> when a non-loopback transport starts;
        /// null on localhost-only deployments. <see cref="HandshakeHandler"/> reads it and
        /// publishes it on every <c>HandshakeResponse</c> so the browser can pin / detect drift.
        /// </summary>
        public static string? LanTlsThumbprint { get; private set; }

        internal static void ValidateCertBindingOrThrow(string? pfxPath, int port)
        {
            if (string.IsNullOrWhiteSpace(pfxPath))
            {
                throw new InvalidOperationException(
                    $"WebSocketTransport: TlsCertPath is empty for a non-loopback binding on port {port}. " +
                    "Spec 021 FR-013a forbids plaintext WebSocket over LAN. Set TlsCertPath in config.json " +
                    "or bind to 127.0.0.1 for localhost-only mode.");
            }

            if (!File.Exists(pfxPath))
            {
                throw new InvalidOperationException(
                    $"WebSocketTransport: TlsCertPath does not exist on disk: '{pfxPath}'. " +
                    "Re-run AKMLSQLSetup.exe or check `%ProgramData%/AKML SQL Web/certs/bridge.cer`.");
            }

            string pfxThumb;
            try
            {
                // The installer's web-tls-setup.ps1 emits `bridge.cer` (public part
                // only) because the LocalMachine\My private key is NonExportable --
                // no PFX file is written in production. We accept either:
                //   * CER (the installer's default) via LoadCertificateFromFile
                //   * PFX (user-supplied path) via LoadPkcs12FromFile
                // We only need the thumbprint to compare against the netsh binding,
                // so the private key is not required.
                var raw = File.ReadAllBytes(pfxPath);
                X509Certificate2 cert;
                try
                {
                    cert = X509CertificateLoader.LoadCertificate(raw);
                }
                catch
                {
                    cert = X509CertificateLoader.LoadPkcs12(
                        raw, password: null, X509KeyStorageFlags.EphemeralKeySet);
                }
                using (cert)
                {
                    pfxThumb = cert.Thumbprint;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"WebSocketTransport: failed to load certificate at '{pfxPath}': {ex.Message}. " +
                    "TlsCertPath accepts either a `.cer` (the installer default) or a `.pfx`. " +
                    "Check the file is a valid certificate and the engine user has read access.", ex);
            }

            string? netshThumb = ReadNetshThumbprint(port);
            if (string.IsNullOrEmpty(netshThumb))
            {
                throw new InvalidOperationException(
                    $"WebSocketTransport: no netsh http sslcert binding found for port {port}. " +
                    "Run `web-tls-setup.ps1` or re-run AKMLSQLSetup.exe to bind the cert.");
            }

            if (!string.Equals(pfxThumb, netshThumb, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "WebSocketTransport: certificate thumbprint mismatch with netsh binding. " +
                    $"TlsCertPath ('{pfxPath}') reports {pfxThumb}; netsh binding for 0.0.0.0:{port} " +
                    $"reports {netshThumb}. Re-run `web-tls-setup.ps1` or update TlsCertPath.");
            }

            // Publish the validated thumbprint so HandshakeHandler can include it in
            // the response per FR-006. The static is set once at LAN startup and never
            // mutated thereafter -- thread-safe by virtue of being write-once.
            LanTlsThumbprint = pfxThumb;
        }

        /// <summary>
        /// Reads the bound cert thumbprint from `netsh http show sslcert ipport=0.0.0.0:&lt;port&gt;`.
        /// Returns null when no binding exists. Locale-dependent on the English label
        /// "Certificate Hash" -- see `contracts/lan-https-binding-contract.md` §"Locale-dependency note".
        /// </summary>
        private static string? ReadNetshThumbprint(int port)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = $"http show sslcert ipport=0.0.0.0:{port}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p == null) return null;
                var stdout = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                if (p.ExitCode != 0) return null;

                // `Certificate Hash    : <40-hex>` (English Windows). The regex below
                // tolerates extra whitespace and case variation in the label.
                var match = Regex.Match(stdout, @"Certificate\s+Hash\s*:\s*([0-9A-Fa-f]{40})",
                    RegexOptions.IgnoreCase);
                return match.Success ? match.Groups[1].Value : null;
            }
            catch
            {
                return null;
            }
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
