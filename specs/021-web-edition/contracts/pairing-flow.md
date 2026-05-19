# Contract — Pairing flow (M3, LAN mode)

This document specifies the user-visible flow and the engine/browser obligations for first-time LAN-mode pairing and token revocation.

Cross-references: spec.md FR-013, FR-014, FR-013a; data-model.md E2/E3; contracts/rpc-handshake.md.

---

## Actors

- **Installer** — runs once at install time, on the engine host.
- **Engine** — runs as a Windows service or interactive process; serves `WebSocketTransport` on `bindAddress:port`.
- **Engine UI** — a small tray/desktop UI surface that re-displays the pairing PIN, the current bearer-token list, and offers a Revoke action.
- **Browser** — the user's browser tab loaded against the web-edition URL.

---

## Localhost mode (no pairing)

```text
[Browser]                                 [Engine]
   |                                          |
   |-- WSS not used; ws://127.0.0.1:PORT/ --->|   (no TLS for loopback)
   |                                          |
   |-- HandshakeRequest{}-------------------->|   (no PIN, no token)
   |<- HandshakeResponse{status="ok", ...} ---|
   |                                          |
   |== normal RPC frames ====================>|
```

Engine accepts any loopback connection. No PIN. No token. Browser still stores an `EngineConnection` record so the user sees "Local engine" in the connection picker.

---

## LAN mode — first time pairing

### Pre-conditions

- Installer was run with **LAN exposed** selected.
- Installer generated a self-signed TLS cert and bound it to the engine on `0.0.0.0:port`.
- Installer printed a one-time **6-digit pairing PIN** to the success page and to `INSTALL-SUMMARY.txt`.
- Installer added a Windows Firewall inbound rule for the chosen port.

### Sequence

```text
[User]            [Browser]                       [Engine]
  |                  |                               |
  | open https://host:port/akmlsql/                 |
  |---------------> |                               |
  |                  | (loads WASM bundle)          |
  |                  |                               |
  |                  | "Add connection" dialog      |
  |                  | host, port, PIN              |
  |<-prompt user---- |                               |
  | enter PIN----->  |                               |
  |                  |-- WSS handshake ------------>|
  |                  |-- HandshakeRequest{          |
  |                  |     PairingPin: "123456",    |
  |                  |     WebVersion: "1.0.0",     |
  |                  |     ProtocolMin/Max: 1..1,   |
  |                  |     BrowserLabel: "Chrome on M1 MBP"  |
  |                  |   } ------------------------>|
  |                  |                               | validate PIN; generate token (256-bit random)
  |                  |                               | record (BrowserLabel, fingerprint, ts) in BearerTokenStore
  |                  |<- HandshakeResponse{          |
  |                  |     Status: "ok",             |
  |                  |     EngineVersion: "1.0.0",   |
  |                  |     ChosenProtocolVersion: 1, |
  |                  |     EngineCapabilities: [...],|
  |                  |     NewBearerToken: "<hex>"   |
  |                  |   } -------------------------|
  |                  |                               |
  |                  | wrap token via Web Crypto    |
  |                  | store in PairingToken record |
  |                  | clear raw token from memory  |
  |                  |                               |
  |                  |== normal RPC frames =========>|
```

### Post-conditions

- PIN is discarded by the engine (single-use). Future handshakes with this PIN fail.
- Browser stores `EngineConnection { id, host, port, tlsFingerprint }` and `PairingToken { wrappedToken, iv, aad }`.
- Engine has a row in `BearerTokenStore` keyed by token-hash, with metadata `{ browserLabel, mintedAt, lastUsedAt, ttlExpiresAt }`.

---

## LAN mode — reconnect with bearer token

```text
[Browser]                                 [Engine]
   |                                          |
   |-- WSS handshake -----------------------> |
   |-- HandshakeRequest{                     |
   |     BearerToken: "<unwrapped hex>",     |
   |     WebVersion: "1.0.0",                |
   |     ProtocolMin/Max: 1..1               |
   |   } ----------------------------------->|
   |                                          | hash(token) → BearerTokenStore lookup
   |                                          | if found and not expired: ok; rotate ttl
   |                                          | else: status = "pin_required"
   |<- HandshakeResponse{...} ----------------|
```

Token is never sent back over the wire after the initial mint; it is rotated only on TTL expiry, by the engine returning `NewBearerToken` on a successful refresh handshake (engine MAY do this whenever `now > ttlExpiresAt - 14 days`).

---

## Token revocation (engine-initiated)

The Engine UI exposes:

- A table of currently-paired browsers: `{ browserLabel, mintedAt, lastUsedAt, ttlExpiresAt }`.
- A **Revoke** button per row.
- A **Revoke all** button.
- A **Regenerate PIN** button (forces fresh pairing for any new browser).

On revoke, the engine removes the row from `BearerTokenStore` and closes any open WebSocket using that token. The browser observes the close, attempts a reconnect, receives `Status = "pin_required"`, and falls back to PIN re-pairing.

---

## TLS fingerprint pinning

On the first successful WSS handshake, the browser stores `tlsFingerprint` (SHA-256 of the engine's leaf cert) on the `EngineConnection` record. On subsequent connections, the browser MUST compare the presented cert's fingerprint to the pinned value; mismatch surfaces a dialog:

> **The engine's certificate changed.**
>
> If you just reinstalled the engine, this is expected — click Re-pair.
> Otherwise this might be an impersonation attempt — click Cancel and verify with the engine host.

The user explicitly choosing Re-pair clears the pinned fingerprint and the bearer token; the next handshake will require a new PIN.

---

## Security obligations

- Engine MUST hash the bearer token at rest (e.g. SHA-256). The plain token MUST NOT be stored in `BearerTokenStore`.
- Engine MUST use constant-time comparison when validating PINs and tokens.
- Engine MUST limit PIN attempts to **5 per minute, per source IP**; further attempts return `Status = "pin_invalid"` without consulting the PIN store.
- Engine MUST log every successful and unsuccessful PIN/token attempt with `{ ts, sourceIp, browserLabel?, outcome }`.

---

## Test obligations

- Wrong PIN → `pin_invalid`; the engine's PIN must still be valid for one more attempt.
- Correct PIN → `NewBearerToken` minted; PIN is single-use afterwards.
- Bearer token replay after revocation → `pin_required`.
- TLS fingerprint mismatch → browser surfaces the dialog and refuses normal frames until re-pair.
- PIN attempt rate limit kicks in at the 6th attempt within 60 s from the same source IP.
