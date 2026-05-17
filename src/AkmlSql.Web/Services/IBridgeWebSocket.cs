using System;
using System.Threading;
using System.Threading.Tasks;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M3 task T068. Thin abstraction over a browser WebSocket so
/// the <see cref="EngineBridge"/> can be unit-tested with an in-memory transport.
/// </summary>
public interface IBridgeWebSocket : IAsyncDisposable
{
    /// <summary>Open the WebSocket connection. Throws on failure.</summary>
    Task ConnectAsync(string url, CancellationToken ct);

    /// <summary>Send one MessagePack-framed RpcMessage as a binary frame.</summary>
    Task SendAsync(byte[] frame, CancellationToken ct);

    /// <summary>
    /// Read one binary frame from the server. Returns null when the socket has been
    /// closed (graceful or abrupt).
    /// </summary>
    Task<byte[]?> ReceiveAsync(CancellationToken ct);

    /// <summary>Current connection state -- mirrors WebSocket.readyState semantics.</summary>
    BridgeWebSocketState State { get; }
}

public enum BridgeWebSocketState { Connecting, Open, Closing, Closed }
