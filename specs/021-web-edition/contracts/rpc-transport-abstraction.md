# Contract — RPC transport abstraction (M0)

**Surface**: internal C# API in `AkmlSql.Engine`, consumed by transport implementations and by `AkmlSql.Web` (for `InProcessTransport`).

**Status**: target API delivered by M0; consumed unchanged through M3 and beyond.

---

## C#-level contract

```csharp
namespace AkmlSql.Engine.Transports;

/// <summary>
/// One transport implementation per medium (named pipe, in-process, WebSocket).
/// Handles frame I/O and lifecycle only. Routing and handler dispatch belong to RpcRouter.
/// </summary>
public interface IRpcTransport : IAsyncDisposable
{
    /// <summary>
    /// Begin accepting connections / frames. Returns once the transport is listening.
    /// </summary>
    Task StartAsync(CancellationToken ct);

    /// <summary>
    /// Raised when a complete RpcMessage frame arrives.
    /// Implementer awaits the returned Task and writes the response (if any) back over the same transport.
    /// </summary>
    event Func<RpcMessage, CancellationToken, Task<RpcMessage?>> RequestReceived;
}

/// <summary>
/// One handler implementation per message-type integer code.
/// Handlers do NOT depend on the transport that delivered the request.
/// </summary>
public interface IRpcRequestHandler<TRequest, TResponse>
{
    int MessageType { get; }
    Task<TResponse> HandleAsync(TRequest request, RpcContext ctx, CancellationToken ct);
}

/// <summary>
/// Per-process router. Resolves MessageType to a handler, deserialises payload, dispatches.
/// </summary>
public sealed class RpcRouter
{
    public void Register<TReq, TResp>(IRpcRequestHandler<TReq, TResp> handler);
    public Task<RpcMessage?> RouteAsync(RpcMessage msg, RpcContext ctx, CancellationToken ct);
}

/// <summary>
/// Per-request shared state. The settings reference is the same object across requests
/// served by a transport, but is replaced atomically when AnalysisSettingsChanged fires.
/// </summary>
public sealed class RpcContext
{
    public AppSettings Settings { get; init; }
    public SessionManager Sessions { get; init; }
    public SchemaCacheManager SchemaCache { get; init; }
    public ILogger Logger { get; init; }
}
```

---

## Wire format (unchanged from current named-pipe transport)

Per existing engine implementation, no change in M0:

```text
+------------------+------------------+-----------------------------------+
| 4 bytes (LE)     | 4 bytes (LE)     | N bytes                           |
| payload length N | XOR CRC of below | MessagePack(RpcMessage)           |
+------------------+------------------+-----------------------------------+
```

`RpcMessage`:

```csharp
[MessagePackObject]
public sealed class RpcMessage
{
    [Key(0)] public int MessageType { get; init; }    // integer code per docs/ipc-api.md
    [Key(1)] public int RequestId { get; init; }      // 0 for notifications
    [Key(2)] public byte[] Payload { get; init; }     // MessagePack-encoded request/response struct
}
```

Maximum frame size: 16 MB (existing limit).

---

## Transport profiles

| Transport | Class | Wire | Notes |
|-----------|-------|------|-------|
| Named pipe (IDE plugins) | `NamedPipeTransport` | `[length][CRC][MessagePack]` over `\\.\pipe\akmlsql-engine-{SID}-{PID}` | ACL: owner SID allow, Network deny |
| In-process (WASM, tests) | `InProcessTransport` | Method calls; `RpcMessage` carried by reference; no serialization | Used by Blazor WASM running engine logic in-browser; used by engine unit tests |
| WebSocket (browser ↔ engine, M3) | `WebSocketTransport` | One WebSocket binary message = one MessagePack(RpcMessage). No additional framing. | Localhost: `ws://`; LAN: `wss://` (mandatory) |

---

## Compatibility constraints

- **Frame format is frozen.** Existing IDE plugin builds (SSMS 20/21/22, VS 2019/22/26) must talk to the M0-refactored engine without recompilation.
- **Message-type integer codes are frozen.** Adding a new message type allocates the next integer; never reuse retired codes.
- **`RpcMessage.RequestId` semantics**: 0 = notification (no response expected); non-zero = correlated request. `InProcessTransport` ignores the field but must not zero it on the response.
- **Cancellation token contract**: a transport that supports per-request cancellation (named pipe via disconnection; WebSocket via close frame) signals the handler's `ct`. `InProcessTransport` cancellation is callsite-controlled.

---

## Test obligations (M0)

- A unit test that exercises each of the ~50 message types via `InProcessTransport` with zero serialisation, asserting handler dispatch is correct.
- An integration test that exercises a representative subset of message types (Completion, Format, Analysis, Snippet, AI) over a real named pipe end-to-end, with and without the M0 refactor, asserting bit-identical frames on the wire.
- A performance baseline test that captures `CompletionRequest` and `FormatRequest` p50/p99 before M0.2 and asserts no >5 % regression at the end of M0.
