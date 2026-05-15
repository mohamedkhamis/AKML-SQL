# M3 — WebSocket Transport & Local-Agent Bridge

**Status**: Draft
**Phase**: M3 (live schema; first time the browser talks to the engine)
**Estimated effort**: 2–3 weeks
**Branch prefix**: `m3-websocket-transport`
**Depends on**: M0 merged, M2 shipped

---

## 1. Executive summary

M2 shipped a self-contained web edition that can format and analyse SQL but has no awareness of any real database. M3 connects the browser to a running `AkmlSql.Engine` process via WebSocket, unlocking live schema, IntelliSense from actual `sys.objects`, and the same Phase A / Phase B schema cache lifecycle the SSMS plugin uses.

Two transports are added to the engine — both `WebSocketTransport` variants, distinguished by binding:

| Mode | Binding | Auth | Reachable from |
|------|---------|------|----------------|
| **Localhost** | `127.0.0.1:<port>` | None (loopback only) | Same machine only |
| **LAN** | `0.0.0.0:<port>` | Pairing token | Any host on the same network |

Mode is chosen at engine startup based on `config.json`; the installer (M4) writes this setting. The browser app gets an "Add a connection" UI: it shows the current connection, lets the user paste a pairing token (LAN mode only), and stores the token in IndexedDB after pairing.

---

## 2. Why now

M2 proved the browser can do real work. The natural next step is letting the browser talk to a real database — and that means an engine bridge. M3 is the first time we cross the browser-to-engine boundary, and doing it before M4 (IIS installer) means we know exactly what the installer has to configure.

The LAN mode is in scope because of the spec call ("both — install-time choice between localhost and LAN-exposed"). LAN mode forces us to confront authentication now rather than deferring it.

---

## 3. Current state

End of M2:

- Browser app runs entirely in WASM, no network calls except to load static files
- Engine runs on the same machine but the browser has no way to reach it
- Engine has `NamedPipeTransport` and `InProcessTransport` from M0; no network transport
- LAN exposure was never on the table; firewall rules don't exist

---

## 4. Proposed architecture

### 4.1 Two-mode binding

`WebSocketTransport` is one class with a binding mode parameter:

```csharp
public sealed class WebSocketTransport : IRpcTransport
{
    public WebSocketTransport(WebSocketTransportOptions opts) { ... }
}

public sealed class WebSocketTransportOptions
{
    public string BindAddress { get; init; } = "127.0.0.1"; // or "0.0.0.0" for LAN
    public int Port { get; init; } = 47291;                // configurable
    public bool RequirePairingToken { get; init; }         // forced true if BindAddress != loopback
    public string TokenStorePath { get; init; }            // %AppData%/AKML SQL Web/tokens.json
    public TimeSpan TokenTtl { get; init; } = TimeSpan.FromDays(90);
}
```

### 4.2 Pairing flow (LAN mode)

```
1. Engine starts; generates a one-time pairing PIN (6 digits) and prints it to its log + tray icon balloon
2. User opens the browser app, clicks "Add connection"
3. User pastes the host + port + PIN
4. Browser sends WebSocket handshake with the PIN in a query parameter
5. Engine validates PIN; on success, mints a long-lived bearer token (256-bit, random)
6. Engine returns token in the first frame; browser stores in IndexedDB
7. Subsequent connections use the token; PIN is single-use and discarded
```

Localhost mode skips steps 1–6: any connection from `127.0.0.1` is trusted.

### 4.3 Frame shape

Same MessagePack `RpcMessage` as the named pipe transport — the whole point of M0 was that the wire format is transport-agnostic. WebSocket frames carry `RpcMessage` payloads directly; one WebSocket message = one `RpcMessage`.

The only addition is the handshake. The first message after `connect` is a `HandshakeRequest` with `{ pairingPin?, bearerToken? }`. The engine replies with `HandshakeResponse` containing `{ status, newBearerToken?, errorMessage? }`. After successful handshake the connection is a normal MessagePack bidirectional stream.

### 4.4 Connection state in the browser

```
AkmlSql.Web
└── Services/
    ├── ConnectionStore.cs       ← list of known connections + active selection
    ├── EngineConnection.cs      ← WebSocket client; framing; reconnect
    └── ConnectionPicker.razor   ← UI for adding/switching/removing connections
```

Connection record:

```
{
  id: "uuid",
  name: "Local engine",
  host: "127.0.0.1",
  port: 47291,
  bearerToken: "...",         // omitted in localhost mode
  isLocalhost: true,
  lastConnectedAt: "2026-..."
}
```

### 4.5 Firewall handling

LAN mode requires the engine to bind `0.0.0.0`. On first start in LAN mode, Windows Firewall will prompt. The installer (M4) creates an inbound rule preemptively for the chosen port if LAN mode is selected at install time. The engine itself never modifies firewall rules at runtime — that's strictly an install-time decision.

---

## 5. Feature scope

| Feature | In M3 |
|---------|-------|
| `WebSocketTransport` in engine (localhost) | Yes |
| `WebSocketTransport` in engine (LAN) | Yes |
| Pairing PIN generation + display | Yes |
| Bearer token mint + persistence | Yes |
| Browser-side connection store | Yes |
| Connection picker UI | Yes |
| Auto-reconnect on transient drops | Yes |
| Live schema fetch (Phase A) | Yes |
| Phase B background loading | Yes |
| IntelliSense from live schema | Yes |
| QuickInfo from live schema | Yes |
| Signature help from live schema | Yes |
| Tab colouring based on connection | **No** — defer; tab colouring is per-DB and the web has no tabs yet |
| Snippets | **No** — M5 |
| Refactoring | **No** — M5 |
| AI | **No** — M6 |

---

## 6. Milestones

### M3.1 — WebSocketTransport on localhost (week 1, days 1–3)

Add `WebSocketTransport`. Engine startup options include `--transport=pipe,websocket-localhost`. Browser connects, no auth, runs `Ping` → `Pong`. Pure plumbing.

### M3.2 — Browser connection store + picker UI (week 1, days 4–5)

Connection store in IndexedDB. Connection picker shows current connection + status + lets user add a new one (host + port + optional PIN field). Reconnect loop with exponential back-off.

### M3.3 — Live schema fetch (week 2)

Wire `SchemaRefresh` / `SchemaQuery` handlers (from M0) to the browser. Phase A query runs; results render in a tree view (DB → schema → tables). Phase B background updates the tree.

### M3.4 — IntelliSense from live schema (week 2, end)

Editor calls `CompletionRequest` for completions. Results render in editor completion dropdown. Dot-trigger, schema-aware ranking, fuzzy matching — all already in the engine, just need wiring.

### M3.5 — LAN mode + pairing (week 3, days 1–3)

`0.0.0.0` binding. PIN generation. Token mint. Token persistence on engine side (`%AppData%/AKML SQL Web/tokens.json` with ACL restricting to engine's user). Browser pairing UI.

### M3.6 — Firewall + documentation (week 3, days 4–5)

Document the firewall prompt user-experience. Document the port choice (`47291` proposed; configurable). Write a "how to pair from another machine" guide. Run a security review on the pairing flow.

---

## 7. Risks and mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| WebSocket framing bugs introduce off-by-one issues with MessagePack | Medium | High | Reuse the same `[length][CRC][MessagePack]` discipline; integration test against the same fixtures M0 used |
| Pairing flow has a subtle auth bypass | Medium | Very high | Security review at end of M3.5; write down threat model; do not skip |
| LAN mode + Windows Firewall = user thinks it's broken | High | Medium | Installer creates the firewall rule when LAN mode is chosen; error message in browser explicitly mentions firewall |
| Engine binding to `0.0.0.0` survives a switch back to localhost | Medium | Medium | Setting must be checked on every start; never persist binding decision implicitly |
| Token theft (someone reads `tokens.json`) | Low | High | ACL the file to the engine's user; document that LAN mode is not a secure deployment for hostile networks |
| Reconnect storm during engine restart | Medium | Low | Exponential back-off; max retry interval 30s |

---

## 8. Threat model (LAN mode)

Documented explicitly so it can be audited:

| Threat | Mitigation |
|--------|------------|
| Anyone on the LAN connects to the WebSocket | Pairing-token bearer auth required |
| Eavesdropper captures the token over plaintext WebSocket | TLS optional in M3 (deferred to M4 with installer providing the cert option); document that plaintext LAN is "trusted-LAN-only" |
| Replay attack with captured token | Tokens have 90-day TTL; user can rotate any time via "Remove and re-pair" |
| Brute force the 6-digit PIN | PIN is single-use; expires after 5 min; after 3 wrong attempts, engine refuses pairing for 15 min |
| Token file stolen from disk | File ACL restricts to engine user; documented as "physical access = total access" |
| Man-in-the-middle on first pair | Out of scope for plaintext mode; M4 may add `https://` via cert option |

This is "good enough for a trusted LAN" — explicitly not "good enough for a hostile network." Documented as such.

---

## 9. Success metrics

- Browser running on the same machine connects to localhost engine in < 200 ms
- Browser running on a second LAN machine pairs in < 30 seconds (including PIN entry)
- Live IntelliSense parity with the WPF plugin on a reference test database
- Phase A schema completes < 500 ms (same as engine target)
- Token persists across browser restart; user does not re-pair daily
- Reconnect after engine restart succeeds within 10 seconds

---

## 10. Out of scope

- TLS on the WebSocket — M4 (installer can provide a self-signed cert option)
- Multi-engine connections from a single browser (one connection at a time in M3)
- Per-connection profiles (each connection inherits the global profile setting)
- Connection sharing between two browsers — they'd each pair separately
- Mobile browsers pairing to a Windows host — not tested; not blocked

---

## 11. Open questions

1. **Default port** — `47291` is arbitrary; check that it isn't a registered IANA port for something common. Make it configurable.
2. **One token per browser or one token per machine?** — Per browser is safer (revocable per device); per machine is simpler. Lean per-browser.
3. **PIN length** — 6 digits gives 1 million combos; with rate-limiting that's fine. Could go 8 digits for paranoia. Keep 6.
4. **Should the engine bind to both pipe + WebSocket simultaneously?** — Yes, so the same engine instance serves both SSMS plugin and browser. Confirmed in M0's transport plurality.

---

## 12. Definition of done

- [ ] `WebSocketTransport` works in localhost mode
- [ ] `WebSocketTransport` works in LAN mode with pairing
- [ ] Browser connects, fetches Phase A schema, renders tree
- [ ] Live IntelliSense works in the editor
- [ ] Pairing flow works end-to-end with a second machine on the LAN
- [ ] Threat model documented in `docs/m3-security.md`
- [ ] Firewall guidance documented
- [ ] Branch `m3-websocket-transport` merged to master via PR
