# M3 — WebSocket bridge security & threat model

**Spec**: [025-m3-bridge-closure](../specs/025-m3-bridge-closure/spec.md) (FR-007 / FR-009)
**PRD**: [M3-websocket-transport.md](WEB/M3-websocket-transport.md)
**Last reviewed**: 2026-05-27

This document is the canonical security reference for the M3 WebSocket bridge between the browser-side AKML SQL web edition and the local `AkmlSql.Engine` process. It covers the threat model, the on-disk artefacts a security review needs to audit, the plaintext-on-LAN refusal contract, and the items NOT covered by this closure spec.

## Threat model (LAN mode)

Documented explicitly so it can be audited against the running code. The first six rows are verbatim from PRD §8; the last two (plaintext-on-LAN refused at construction; cert regeneration silently swaps fingerprint until the pinning UI lands) were added by spec 025 FR-007.

| Threat | Mitigation | Residual risk |
|--------|------------|---------------|
| Anyone on the LAN connects to the WebSocket | Pairing PIN / bearer auth required. When `RequirePairingToken` is set (any non-loopback binding), `WebSocketTransport.ServeAsync` gates the connection: every non-handshake frame is rejected until that connection completes a successful (`Status==Ok`) handshake validated by `HandshakeHandler` — so the PIN/bearer is a hard precondition for every RPC, not advisory. | None for a single-engine deployment; multi-engine sharing of `tokens.json` is out of scope. |
| Eavesdropper captures the token over plaintext WebSocket | **Closed by spec 025 FR-001**: non-loopback bindings serve `wss://` only via `HttpListener` + the installer-bound cert (`netsh http add sslcert ipport=…:<port> certhash=<thumb>`); the construction-time guard refuses any non-loopback `WebSocketTransport` without a `TlsCertPath`. | None on a non-loopback binding; localhost-only bindings stay plaintext (loopback is in-OS — never reaches the wire). |
| Replay attack with captured token | 90-day TTL on bearer tokens; user can rotate any time via "Remove and re-pair" in `ConnectionPickerComponent`. | A captured-and-still-valid token within the TTL window remains useful to an attacker until rotation. |
| Brute force the 6-digit PIN | PIN is single-use and CSPRNG-generated; expires after 24 h; a per-source rate limit of 5 attempts/minute plus a global circuit-breaker that freezes pairing after 100 failed attempts in any 15-minute window until the operator regenerates the PIN (`PairingService.ValidatePin`). | The global cap bounds an attacker — even one spread across many LAN source IPs — to ~100 guesses per 15 min over the 1,000,000-PIN space (≈0.96 %/day); combined with single-use consumption, success probability is negligible. |
| Token file stolen from disk | File ACL restricts to engine user; only hashed SHA-256 tokens hit disk. | Physical access = total access — documented limit; not addressable at the token layer. |
| Man-in-the-middle on first pair | **Closed by spec 025 FR-001 + FR-006**: first pair runs over `wss://` (TLS-encrypted). The browser observes `HandshakeResponse.ServerTlsThumbprint` and pins it into `EngineConnection.TlsFingerprint`; subsequent reconnects compare and log a `Warn` diagnostic on mismatch. | The user-facing **fingerprint-mismatch modal** is a deferred follow-up (spec 025 §Out of Scope #1) — a silent fingerprint change still produces only a log entry, not a dialog. |
| **Spec 025 FR-007 addition** — Plaintext-on-LAN attempted via hand-edited `config.json` | Refused at `WebSocketTransport` construction with the verbatim message: `"WebSocketTransport: LAN-mode binding (BindAddress != loopback) requires TlsCertPath. Spec 021 FR-013a forbids plaintext WebSocket over LAN. Set TlsCertPath in config.json or bind to 127.0.0.1 for localhost-only mode."` The installer never produces this configuration; a user editing the file by hand will see the refusal at engine startup. | None — refusal is hard and at the earliest possible time. |
| **Spec 025 FR-007 addition** — Cert regeneration on installer re-run silently swaps fingerprint until the pinning UI lands | `EngineBridge.ConnectAsync` logs a `Warn` to `IDiagnosticsRingBuffer` when `response.ServerTlsThumbprint` differs from the stored `connection.TlsFingerprint`. The connection record is updated in-memory so future compares track the new value. | User does NOT see a modal asking to re-trust the new cert — they only see a `Warn` row in the diagnostics export. Mitigation: a security-conscious user can inspect `EngineConnection.TlsFingerprint` after a re-install and verify it matches the new `INSTALL-SUMMARY.txt` thumbprint. |

This is "good enough for a trusted LAN" — explicitly **not** "good enough for a hostile network." LAN-mode bridges should be deployed on networks the operator already trusts (an office LAN, a developer VLAN). Hostile-network deployments need additional controls (VPN, network segmentation) that are out of scope for the web edition.

## On-disk artefacts (audit checklist)

A reviewer auditing a LAN-mode deployment should inspect these paths. All ACLs are owner-restricted to the engine user (LocalSystem on a Windows-service install; the user account on a manual run).

| Path | Contents | Sensitivity |
|------|----------|-------------|
| `%CommonAppData%\AKML SQL Web\tokens.json` | SHA-256 hashes of bearer tokens + per-token metadata (browser label, mint time, last-used time, TTL). | High — token rotation requires editing or deleting this file. Plain tokens never appear. |
| `%CommonAppData%\AKML SQL Web\pairing-pin.txt` | Current pairing PIN. Rotated on engine start and after each successful pair. | High — anyone with read access can pair a new browser within the PIN's 5-minute window. |
| `%ProgramData%\AKML SQL Web\certs\bridge.pfx` | LAN-mode self-signed TLS certificate (NonExportable private key, SHA-256 hash, 2-year expiry). | High — the private key is NonExportable but a file-level steal of the PFX is enough for cert impersonation if the operator then imports it elsewhere. |
| `%ProgramData%\AKML SQL Web\certs\thumbprint.txt` | Thumbprint of the LAN-mode cert, written by `web-tls-setup.ps1`. | Low — public information; surfaces in the install summary and the netsh binding. |
| `%AppData%\AKML SQL\config.json` | Engine config. The `bridge` section (added by spec 025 FR-027 / T008) carries `Enabled`, `BindAddress`, `Port`, `TlsCertPath`, `TlsCertPasswordRef`, `TokenStorePath`, `TokenTtlDays`. | Medium — exposes the bridge port and the bound cert path. Plaintext bridge enable on a hand-edited file is the threat already covered above. |
| `%CommonAppData%\AKML SQL Web\install.log` | PowerShell-helper install log (firewall + IIS + TLS + bridge config writes). | Low — diagnostic info; no secrets. |

## Plaintext-on-LAN refusal

The engine refuses any non-loopback binding without a TLS cert at two checkpoints:

1. **`WebSocketTransport` constructor** (spec 021 T057 / FR-013a). When `BindAddress != "127.0.0.1"` / `"::1"` / `"localhost"` and `TlsCertPath` is empty, throws `InvalidOperationException`:
   > WebSocketTransport: LAN-mode binding (BindAddress != loopback) requires TlsCertPath. Spec 021 FR-013a forbids plaintext WebSocket over LAN. Set TlsCertPath in config.json or bind to 127.0.0.1 for localhost-only mode.

2. **`WebSocketTransport.StartAsync` PFX-existence + netsh-thumbprint match** (spec 025 FR-002 / T010). When `TlsCertPath` is set but the file doesn't exist, or its thumbprint doesn't match the active `netsh http show sslcert` binding, throws with both thumbprints in the message so the operator can diagnose without re-running the installer.

Both refusals happen before the listener opens — a misconfigured engine never binds, never accepts connections, never logs handshake traffic. The installer (`AKMLSQLSetup.exe /WEB_EXPOSURE=LAN`) never writes a configuration that would produce either refusal; the only path to hitting them is a hand-edited `%AppData%\AKML SQL\config.json`.

## What is NOT covered (deferred follow-ups)

Three items are intentionally out of scope for this closure spec. They're recorded here so the next M3-touching session can find them.

1. **TLS fingerprint mismatch dialog** (spec 025 §Out of Scope #1). When `EngineBridge.ConnectAsync` observes a `ServerTlsThumbprint` change, it logs a `Warn` to the diagnostics buffer. There is no modal asking the user to re-trust the new cert. A future spec adds the UI surface.
2. **Engine-side tray pairing pane** (spec 021 T065). The PRD §6 M3.5 mentions a tray-icon UI showing the current PIN, paired browsers, Revoke / Revoke all / Regenerate PIN actions. Today, the engine writes the PIN to `pairing-pin.txt` and the installer reads it for the install summary; in-process management is unbuilt.
3. **In-flight WebSocket revocation** (spec 021 T066 partial). When `BearerTokenStore.RevokeByHash` runs, currently-open WebSocket connections bound to that bearer stay open — only the *next* handshake is rejected. A future spec drops the socket the moment the token is revoked.

For any item above, file under "future M3 hardening" and revisit when telemetry or a security audit shows a concrete need.

## See also

- Spec 025 spec / plan / contracts: [`specs/025-m3-bridge-closure/`](../specs/025-m3-bridge-closure/)
- Spec 021 web edition umbrella spec: [`specs/021-web-edition/`](../specs/021-web-edition/)
- Engine bridge architecture: [`doc/architecture.md`](architecture.md) §9d
- Pair-from-second-machine walkthrough: [`doc/WEB/quickstart-m3.md`](WEB/quickstart-m3.md)
