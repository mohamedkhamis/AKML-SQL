# Contract — Bridge handshake (M3)

The first MessagePack frame on a freshly opened WebSocket connection between browser and engine MUST be a `HandshakeRequest`. The engine MUST reply with a `HandshakeResponse` before any other frame is processed. Sending any other request before completing the handshake MUST result in the engine closing the connection with WebSocket close code `1008` (policy violation).

Message-type integer codes (added in M3, reserved at planning time):

| Code | Type | Direction |
|------|------|-----------|
| `200` | `HandshakeRequest` | browser → engine |
| `201` | `HandshakeResponse` | engine → browser |

These reuse the existing `RpcMessage` envelope (the same `[length][CRC][MessagePack]` framing, but framing is provided by WebSocket itself for this transport per the WebSocketTransport profile).

---

## HandshakeRequest

```csharp
[MessagePackObject]
public sealed class HandshakeRequest
{
    /// <summary>One-time PIN supplied for first-time LAN pairing. Mutually exclusive with bearerToken.</summary>
    [Key(0)] public string? PairingPin { get; init; }

    /// <summary>Long-lived bearer token from a prior successful pairing.</summary>
    [Key(1)] public string? BearerToken { get; init; }

    /// <summary>Web-edition version string (semver, e.g. "1.0.0").</summary>
    [Key(2)] public string WebVersion { get; init; } = default!;

    /// <summary>Highest protocol version the web client supports. Current: 1.</summary>
    [Key(3)] public int ProtocolVersionMax { get; init; }

    /// <summary>Lowest protocol version the web client supports. Current: 1.</summary>
    [Key(4)] public int ProtocolVersionMin { get; init; }

    /// <summary>Optional human-readable identifier of the browser (for the engine pairing UI).</summary>
    [Key(5)] public string? BrowserLabel { get; init; }
}
```

Constraints:

- Exactly one of `PairingPin` or `BearerToken` MUST be non-null. Localhost mode may set neither (engine accepts loopback connections unauthenticated).
- `WebVersion`, `ProtocolVersionMin`, `ProtocolVersionMax` MUST be present.
- Maximum frame size for the handshake message is 4 KB.

---

## HandshakeResponse

```csharp
[MessagePackObject]
public sealed class HandshakeResponse
{
    /// <summary>"ok" or one of the error statuses below.</summary>
    [Key(0)] public string Status { get; init; } = default!;

    /// <summary>Engine version (semver).</summary>
    [Key(1)] public string EngineVersion { get; init; } = default!;

    /// <summary>Protocol version chosen for this connection. Always within the intersection of client min/max and engine min/max.</summary>
    [Key(2)] public int ChosenProtocolVersion { get; init; }

    /// <summary>Flat list of capability identifiers the engine supports.</summary>
    [Key(3)] public string[] EngineCapabilities { get; init; } = Array.Empty<string>();

    /// <summary>Set ONLY in response to a successful PairingPin handshake. The browser stores it (wrapped) and uses it on future connections.</summary>
    [Key(4)] public string? NewBearerToken { get; init; }

    /// <summary>Server's canonical identity for any SQL Server currently selected by this engine, used as cache key. Null if engine has no DB connection.</summary>
    [Key(5)] public string? ServerCanonicalIdentity { get; init; }

    /// <summary>Human-readable detail on error; null on success.</summary>
    [Key(6)] public string? ErrorMessage { get; init; }
}
```

`Status` values:

| Value | Meaning |
|-------|---------|
| `"ok"` | Handshake succeeded; the connection is open for further frames. |
| `"pin_invalid"` | `PairingPin` was wrong or expired. Engine closes the connection after the response. |
| `"pin_required"` | `BearerToken` was rejected (revoked / unknown). The browser must fall back to PIN re-pairing. |
| `"protocol_mismatch"` | Engine and browser have disjoint protocol-version ranges. Engine closes the connection. |
| `"server_busy"` | Engine is shutting down or already serves the maximum allowed concurrent browsers. Browser may retry with backoff. |

---

## Capability identifiers (initial set)

Capability identifiers are stable strings. The set may grow; old strings are never reused for new meanings.

| Capability | Required for feature |
|------------|----------------------|
| `core.format.v1` | Formatter (always present from M0+) |
| `core.analysis.v1` | Analyser (always present from M0+) |
| `schema.v2` | Live schema and IntelliSense (M3) |
| `schema.cache.v1` | Schema cache identity protocol (M5) — engine reports `serverCanonicalIdentity` |
| `snippets.write` | Snippet save/delete via the bridge (M5) |
| `refactoring.heavy` | Heavyweight schema-aware refactorings (M5) |
| `ai.text-to-sql.v1` | Direct AI invocation contracts unchanged — but engine may host helper endpoints (none required in M6 plan) |
| `diagnostics.engine-log-tail.v1` | The `EngineLogTail` request used by the diagnostics bundle (M2 ring buffer; engine extension lands at M2 alongside the diagnostics export) |

Browser behaviour when a capability is missing:

- The feature gated on that capability is hidden or disabled with an inline notice.
- The notice text must name the capability in user-friendly terms (e.g. "Schema-aware completions require engine version ≥ 1.3").
- The bridge connection remains open for all features whose capabilities ARE present.

---

## Handshake error handling

| Outcome | Browser behaviour |
|---------|-------------------|
| `HandshakeResponse.status = "ok"` | Connection enters normal operation; `EngineConnection` updated with `lastKnownEngineVersion` and `lastKnownCapabilities`. |
| `"pin_invalid"` | User-facing toast: "Pairing PIN was wrong or expired — try again from the engine UI." |
| `"pin_required"` | Wrap-zeroise the stored bearer token; ask user for a fresh PIN. |
| `"protocol_mismatch"` | Full-page banner: "Your engine and web edition versions are too far apart — update one of them via the installer." This is the *only* case where the entire bridge is unusable; per clarification 5 it is a hard incompatibility, not a per-feature one. |
| `"server_busy"` | Show a transient toast and retry with exponential backoff. |
| WebSocket close without response | Treat as bridge offline; trigger reconnect per FR-017. |

---

## Test obligations

- A `HandshakeRequest` with `PairingPin` matching the engine's current PIN succeeds and produces a `NewBearerToken`.
- A `HandshakeRequest` with a `BearerToken` revoked by the engine returns `"pin_required"`.
- A `HandshakeRequest` whose `ProtocolVersionMin` exceeds the engine's `ProtocolVersionMax` returns `"protocol_mismatch"` and closes the connection.
- A non-handshake frame sent before completing the handshake closes the connection with WebSocket code `1008`.
- A successful handshake against an engine with `EngineCapabilities` missing `schema.v2` must NOT block subsequent format/analyse calls.
