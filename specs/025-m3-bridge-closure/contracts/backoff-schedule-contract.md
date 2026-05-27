# Contract: Exponential-backoff reconnect schedule

**Spec**: 025-m3-bridge-closure
**Consumers**: US3 (FR-011 / FR-012 / FR-013 / FR-014 / FR-015 / FR-016)
**Related**: spec 021 T068 follow-up note; Research Decision 2; data-model.md E1

## State machine

`EngineBridge.State` transitions for the reconnect loop:

```
            ┌──────────────┐  socket close, not user-initiated   ┌─────────────────┐
            │              │ ────────────────────────────────► │                 │
            │     Open     │                                    │   Reconnecting  │
            │              │ ◄────────────────────────────────  │                 │
            └──────────────┘  retry succeeded, handshake ok    └─────────────────┘
                   ▲                                                     │
                   │                                                     │  NextDelay() elapses
                   │                                                     ▼
            ┌──────────────┐                                    ┌─────────────────┐
            │              │ ◄────────────────────────────────  │                 │
            │  Connecting  │  retry succeeded, handshake ok    │                 │
            │              │                                    │   (timer wait)  │
            └──────────────┘                                    └─────────────────┘
                   │
                   │  retry returned PinRequired
                   ▼
            ┌──────────────┐
            │    Failed    │   ← exit retry loop, surface re-pair UI (FR-014)
            └──────────────┘
```

User-initiated `DisconnectAsync` MUST bypass the reconnect path entirely:

```
            ┌──────────────┐  DisconnectAsync()                  ┌─────────────────┐
            │   Open       │ ────────────────────────────────►  │  Disconnected   │
            │              │                                     │   (no retries)  │
            └──────────────┘                                     └─────────────────┘
```

## Backoff sequence

Per Research Decision 2 + data-model E1:

```
attempt:  1     2     3     4     5     6     7     8+ (capped)
delay:    500ms 1.0s  2.0s  4.0s  8.0s  16.0s 30.0s 30.0s ...
                                                        (each ±100 ms jitter)
```

Formula: `delay_n = min(500ms × 2^(n-1), 30s) + Uniform(-100ms, +100ms)`.

`AttemptNumber` MUST reset to `0` on every transition to `Open` (whether reached after the first connection or after a successful reconnect).

## Bearer-token replay (FR-013)

Each retry MUST send the same `HandshakeRequest` shape as the original connect:

```
{
  "PairingPin": null,                          ← always null on reconnect
  "BearerToken": <token from IPairingTokenVault.RetrieveAsync>,
  "WebVersion": "1.0.0",
  "ProtocolVersionMin": 1,
  "ProtocolVersionMax": 1,
  "BrowserLabel": "Web edition"
}
```

The bearer comes from `IPairingTokenVault.RetrieveAsync(connection.Id)` — the same source used on the original connect. For a localhost-mode connection (where `BearerTokenWrappedRef` is null), `BearerToken` is also null on the retry, and the engine's localhost auto-accept path runs.

## Revocation-detection contract (FR-014)

When a retry's handshake response has `Status == HandshakeStatus.PinRequired`:

1. The bridge MUST transition to `Failed` (not back to `Reconnecting`).
2. The retry loop MUST exit.
3. The status bar MUST surface a "Re-pair required" indicator.
4. `IPairingTokenVault.RemoveAsync(connection.Id)` MUST run to drop the stale wrapped token from IndexedDB.
5. `IConnectionStore.UpdateAsync` MUST clear `BearerTokenWrappedRef` on the connection record.

The retry loop MUST NOT spin against a `PinRequired` response — `Failed` is a terminal state until the user re-pairs via the UI.

## In-browser work during Reconnecting (FR-015)

While `BridgeState.Reconnecting`:

- `FormatterService.FormatAsync` MUST continue to run in-browser using `AkmlSql.Formatting`'s WASM path (already shipped).
- `AnalyserService.AnalyseAsync` MUST continue to run in-browser using `AkmlSql.Analysis`'s WASM path (already shipped).
- `CompletionService.RequestAsync` MUST return an empty `CompletionResponse` (existing FR-016 behaviour from spec 021) — the in-browser cache fallback lands in M5/T109.

The reconnect timer MUST NOT run on the renderer thread — it MUST live on a `Task.Run` continuation with a `PeriodicTimer` or equivalent so the UI thread stays responsive.

## Status-bar surface (FR-016)

`StatusBar.razor` MUST extend the existing 5 pills (`Disconnected / Connecting / Open / Reconnecting / Failed`) with retry-info text when `State == Reconnecting`:

```
Reconnecting · next try in 4s
Reconnecting · trying now…
```

The countdown MUST update at 1 Hz; "trying now" appears in the brief window where the WebSocket `ConnectAsync` is in flight.

## Tests (`tests/AkmlSql.Web.Tests/Bridge/ReconnectLoopTests.cs`)

`ReconnectLoopTests` MUST cover:

| Test | Asserts |
|------|---------|
| `SocketCloseTransitionsToReconnecting` | Drive `FakeBridgeWebSocket` to a "remote close" frame; assert `State == Reconnecting` within 100 ms of the loop seeing the close. |
| `RetrySucceedsRestoresOpen` | After Reconnecting, `FakeBridgeWebSocket` accepts the next `ConnectAsync` and the handshake returns `Ok`; assert `State == Open` and `AttemptNumber` reset to 0. |
| `BackoffSequenceMatchesContract` | Injected jitter source returns `TimeSpan.Zero`; assert the observed sequence is `500ms, 1s, 2s, 4s, 8s, 16s, 30s, 30s` (no jitter). |
| `JitterStaysInRange` | Run 1000 iterations with a real random jitter source; assert every emitted delay is within `±100 ms` of the deterministic value. |
| `RevocationTerminatesLoop` | Configure `FakeBridgeWebSocket` to return `HandshakeStatus.PinRequired` on the next handshake; assert State transitions to `Failed`, the loop exits within 1 retry, and `IPairingTokenVault.RemoveAsync` was called. |
| `DisconnectAsyncBypassesRetry` | While `Reconnecting`, call `DisconnectAsync`; assert State transitions to `Disconnected` (not back through `Connecting`) and the retry timer is cancelled. |
| `InBrowserWorkSurvivesReconnect` | During `Reconnecting`, call `FormatterService.FormatAsync` against a fixture-installed in-browser formatter; assert it returns formatted SQL (not throws, not stalls). |

`FakeBridgeWebSocket` already exists per spec 021 T068 / T071; this contract reuses it without modification.
