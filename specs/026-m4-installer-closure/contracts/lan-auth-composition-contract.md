# Contract: Engine-side LAN Auth Composition (US2)

Defines how the engine wires the handshake to enforce the pairing PIN in LAN mode. Covers FR-013a..FR-013e + SC-010. This is the spec-025-left-undone auth wiring surfaced during the M4 plan-stage audit.

## Current state (the gap)

`EngineHandlerRegistry.cs:258` registers `router.Register(new Handlers.Handshake.HandshakeHandler());` — the parameterless constructor. Its callbacks (`HandshakeHandler.cs:37-45`) are all-permissive:

```csharp
public HandshakeHandler() : this(
    pairingRequired: () => false,
    pinValidator:    _ => true,
    bearerValidator: _ => true,
    bearerMinter:    _ => null,
    serverCanonicalIdentityProvider: () => null) { }
```

So `_pairingRequiredProvider()` returns false, the entire PIN/bearer block in `HandleAsync` (lines 87–148) is skipped, and **every LAN connection is auto-accepted**. `PairingService` / `BearerTokenStore` are never instantiated in production.

## C1 — LAN composition

When `EngineHost` builds a `WebSocketTransport` and `BridgeOptions.IsLoopback == false`:

```csharp
var pairing = new PairingService();                       // mints initial PIN in ctor
var tokens  = new BearerTokenStore(bridge.TokenStorePath, // %CommonAppData%\AKML SQL Web\tokens.json
                  TimeSpan.FromDays(bridge.TokenTtlDays));
var handshake = new HandshakeHandler(
    pairingRequired: () => true,
    pinValidator:    pin => pairing.ValidatePin(/*sourceId*/ "ws", pin) == PinAttemptResult.Valid,
    bearerValidator: tok => tokens.Validate(tok),
    bearerMinter:    label => tokens.Mint(label),
    serverCanonicalIdentityProvider: /* existing resolver */);
```

`EngineHandlerRegistry` registers the **supplied** `handshake` instead of the hardcoded `new HandshakeHandler()`. The registry method signature gains an optional `HandshakeHandler?` parameter (default null ⇒ parameterless registration, preserving every existing caller).

**Required on `sourceId`** (the rate-limit bucket key): it MUST be the transport-observed remote endpoint — `HttpListenerContext.Request.RemoteEndPoint` (address only, port stripped) — carried per-connection to the `pinValidator`. It MUST NOT be a constant (a constant degrades `PairingService`'s per-source 5/min limit into a global 5/min limit, which both lets a single attacker pace a brute force AND locks the legitimate operator out of pairing for the rest of the minute — contradicting FR-013c). It MUST NOT be a client-supplied field on `HandshakeRequest` (spoofable).

**Mechanism (resolved during the plan audit)**: `RpcContext` is a **per-process shared singleton** (`RpcContext.cs`: *"Per-process shared state passed to every IRpcRequestHandler invocation"*), so it CANNOT carry per-connection state — do **not** put the endpoint there (it would race across concurrent connections). Instead, `WebSocketTransport` sets an `AsyncLocal<System.Net.IPAddress?>` at the top of each connection's frame-handling flow, before dispatching the handshake frame; the singleton `HandshakeHandler`'s `pinValidator` closure reads that ambient. `AsyncLocal` isolates per-connection because each accepted socket runs in its own logical async flow. This wiring is part of FR-013a, not deferred.

## C2 — Loopback / no-bridge composition

When `IsLoopback == true` or no bridge section exists, registration is the parameterless `new HandshakeHandler()` exactly as today. No `PairingService`/`BearerTokenStore` is constructed. Localhost browsers and the named-pipe IDE plugin are unaffected.

## C3 — Handshake outcomes (already implemented in `HandleAsync`)

The closure does **not** rewrite `HandleAsync` — it only supplies live delegates. The existing body then yields:

| Input | Outcome |
|-------|---------|
| LAN, wrong/expired/rate-limited PIN | `PinInvalid`, no bearer (FR-013c) |
| LAN, correct PIN | `Ok` + `NewBearerToken` minted + persisted; PIN consumed single-use (FR-013d) |
| LAN, valid stored bearer | `Ok`, no PIN consumed |
| LAN, revoked/unknown bearer | `PinRequired` (FR-013d) |
| Loopback, no PIN | `Ok` (auto-accept, FR-013b) |
| protocol mismatch | `ProtocolMismatch` (unchanged) |

## C4 — `EngineHostTests` composition matrix (FR-013e)

New/extended tests assert, without a live socket (construct the handler via the same composition helper the host uses):

1. LAN `BridgeOptions` → handshake with wrong PIN returns `PinInvalid`, `NewBearerToken == null`.
2. LAN `BridgeOptions` → handshake with the right PIN returns `Ok`, `NewBearerToken != null`; a second handshake with the same PIN returns `PinInvalid` (single-use).
3. Loopback `BridgeOptions` → no-PIN handshake returns `Ok`.
4. Both LAN and loopback compositions register all non-handshake handlers against the **same** `RpcRouter` instance (no regression to spec 025's dual-transport composition).

**Verification**: `dotnet test tests/AkmlSql.Engine.Tests --filter FullyQualifiedName~EngineHostTests` green; SC-010 holds (no configuration bypasses LAN PIN/bearer validation).
