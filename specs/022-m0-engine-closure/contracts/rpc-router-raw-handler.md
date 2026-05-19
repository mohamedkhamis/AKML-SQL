# Contract — `RpcRouter.RegisterRaw`

**Status**: New surface added in P2 of spec 022 (M0 closure). Additive — existing `Register<TReq, TResp>(IRpcRequestHandler<TReq, TResp>)` is unchanged.

## Why this overload exists

The spec 021 M0 work migrated all ~50 message types out of the named-pipe transport's switch statement, but ~14 of them are *delegating handlers* — they take the raw `RpcMessage` (sometimes with a session-lookup callback) and return a fully-formed response envelope. They were registered via a `DelegatingMessageHandler` wrapper into a `_pluggableHandlers` dictionary on the transport itself.

When `RpcRouter` becomes the single dispatch surface (P2 of the closure), those delegating handlers need a way to live in the router's adapter map without being forced through the typed `IRpcRequestHandler<TReq, TResp>` contract. The typed contract's `TypedHandlerAdapter` would MessagePack-deserialise the request only to hand a raw envelope back, then re-serialise an already-shaped response — pure double-work.

`RegisterRaw` is the escape hatch: register a function that maps `(RpcMessage, CancellationToken) → Task<RpcMessage?>` directly, and the router invokes it without any serialisation pass.

## Public surface

```csharp
namespace AkmlSql.Engine;

public sealed class RpcRouter
{
    // ----- Existing surface (unchanged) -----
    public void Register<TReq, TResp>(IRpcRequestHandler<TReq, TResp> handler);
    public bool IsRegistered(int requestMessageType);
    public IReadOnlyCollection<int> RegisteredMessageTypes { get; }
    public int RegisterAllInAssembly(Assembly? assembly = null);
    public Task<RpcMessage?> RouteAsync(RpcMessage msg, RpcContext ctx, CancellationToken ct);

    // ----- New (P2) -----
    public void RegisterRaw(
        int messageType,
        Func<RpcMessage, CancellationToken, Task<RpcMessage?>> handler);
}
```

## Behavioural contract

### `RegisterRaw(messageType, handler)`

- MUST throw `InvalidOperationException` if a handler (typed or raw) is already registered for the same `messageType`. Same semantics as the typed `Register<,>`.
- MUST throw `ArgumentNullException` if `handler` is null.
- MUST add the handler to the same internal `_adapters` map that typed handlers use. After registration, `IsRegistered(messageType)` returns `true` and the code appears in `RegisteredMessageTypes`.
- MUST be safe to call from any thread; the underlying map is `ConcurrentDictionary<int, IHandlerAdapter>`.

### Dispatch path

When a frame with `MessageType == X` arrives and `X` was registered via `RegisterRaw`:

1. `RouteAsync(msg, ctx, ct)` looks up `X` in the adapter map.
2. The matched adapter is a `RawHandlerAdapter` (private class inside `RpcRouter`).
3. The adapter invokes the registered function as `handler(msg, ct)` — no MessagePack deserialise pass, no `RpcContext` passed (the function is expected to close over any state it needs at registration time).
4. The function's return value is returned to the transport verbatim. `null` means "notification, no reply" — same as today's `null` from typed handlers.

### Exception propagation

The same outer-catch as the typed path applies: an uncaught exception in the function is caught by `NamedPipeTransport.DispatchAsync` (or whichever transport is in use) and converted to an error envelope. `OperationCanceledException` propagates if the transport's outer scope cancels.

## When to use `RegisterRaw` vs `Register<,>`

| Use `Register<TReq, TResp>` when... | Use `RegisterRaw` when... |
|---|---|
| The handler takes a strongly-typed request | The handler needs the raw `RpcMessage` (e.g. to inspect `RequestId`, or to handle a polymorphic response shape) |
| The response shape is fixed | The handler returns multiple polymorphic response types based on input |
| The handler is brand new and follows the modern pattern | The handler is a legacy `IMessageHandler` adapter (session, history, productivity, navigation, AI bridge, AiStreamCancel) |
| You can deserialise/serialise via MessagePack with zero loss | The serialisation already happened inside the handler and re-serialising would duplicate work |

## Caller migration

Every line in `EngineHandlerRegistry.RegisterAllHandlers` that today reads:

```csharp
_pluggableHandlers[MessageTypes.SessionSave] =
    new DelegatingMessageHandler((msg, ct) => _sessionRequestHandler.HandleAsync(msg, MessageTypes.SessionSave));
```

becomes:

```csharp
router.RegisterRaw(MessageTypes.SessionSave,
    (msg, ct) => _sessionRequestHandler.HandleAsync(msg, MessageTypes.SessionSave));
```

Concretely: ~14 lines in the registry switch from dictionary writes to `RegisterRaw` calls. The lambda bodies are byte-identical to today's `DelegatingMessageHandler` lambdas.

The `DelegatingMessageHandler` class itself stays in the source tree initially — moving the AI bridge off it (Phase 3 in the closure plan) deletes it later, but it is not in this contract's scope.

## Invariants

1. `RegisterRaw` and `Register<,>` MUST be mutually exclusive per `messageType` — second call throws (whichever order).
2. The post-closure `RegisteredMessageTypes` set MUST equal the pre-closure `PipeRpcServer.RegisteredMessageTypeCodes` set. The matrix test `AllMessageTypesInProcessTests.AllShellToEngineMessageTypes_Are_Registered` verifies this.
3. Bytes returned for every delegating-handler message MUST be identical pre- and post-closure for the same input. The closure-plan Task 5 step 7 runs `PipeRoundTripTests` to confirm.
4. `RegisterRaw` MUST NOT affect the reflection-based `RegisterAllInAssembly` path — that path only matches typed handlers via the `IRpcRequestHandler<,>` interface scan.
