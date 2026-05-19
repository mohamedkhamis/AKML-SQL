# Phase 0 — Research: AKML SQL Web Edition

This document consolidates the technology decisions that resolve all open `NEEDS CLARIFICATION` markers from `plan.md`'s Technical Context. Each subsection follows the standard *Decision / Rationale / Alternatives considered* shape. Where the M0–M6 PRDs in `doc/WEB/` already made the call, the PRD is cited.

There are **no unresolved NEEDS CLARIFICATION items** remaining after this phase.

---

## R1. Browser runtime: Blazor WebAssembly (standalone)

**Decision**: Blazor WASM (.NET 10) running as a **standalone** static-file app, hosted via IIS (preferred) or any static host.

**Rationale**:

- The formatter and analyser are already `netstandard2.0` and reference `Microsoft.SqlServer.TransactSql.ScriptDom`, which the M1 spike confirms runs unchanged in WASM. Choosing Blazor WASM lets the entire pipeline (`AkmlSql.Core` + `AkmlSql.Formatting` + `AkmlSql.Analyzer`) run in-browser with no port effort.
- Standalone (not Blazor Server) means zero per-user server compute. The web app is shipped as a static bundle the user can serve from IIS, from the lightweight fallback (deferred), or even `file://`. This aligns with FR-001 (modern evergreen browser) and SC-006 (P1 works with no engine).
- A `.NET 10` runtime keeps the web tier on the same major version as the existing engine, simplifying shared-library targets.

**Alternatives considered**:

- *Blazor Server*: rejected — would require a Kestrel app per user, breaks the "static-files-on-IIS" deployment story, adds SignalR concerns, and centralises CPU on the host.
- *MAUI / native desktop wrapper*: rejected — defeats the "any modern browser, any OS" purpose; ties the user to a Windows-only client.
- *React / Vue + a JSON-RPC backend*: rejected — would require a parallel reimplementation of formatter and analyser logic in JavaScript, doubling maintenance.

Source: `doc/WEB/00-INDEX.md` § "Key decisions already baked in"; `doc/WEB/M1-wasm-spike-skeleton.md`; `doc/WEB/M2-formatter-analyser-mvp.md` § 4.1.

---

## R2. Editor component: Monaco vs CodeMirror 6 (decision deferred to M2.1 spike)

**Decision**: Defer to a one-day comparison spike at the start of M2 (M2.1). The architecture treats the editor as a swappable component behind `EditorComponent.razor`; either choice fits the plan unchanged.

**Rationale**:

- Both editors meet the functional requirements (syntax highlighting, line numbers, click-to-jump, keybinding registration, theme integration).
- Monaco: ~2 MB bundle, same engine as VS Code (familiarity for the SSMS/VS audience), rich TypeScript API.
- CodeMirror 6: ~500 KB bundle, modern modular API, better SQL grammar via `@codemirror/lang-sql`.
- The bundle-size delta meaningfully affects the SC-001 budget (5 minutes install→edit, of which cold load is a slice). M2.1 is the right moment to measure on real hardware.

**Alternatives considered**:

- *Custom contenteditable / textarea*: rejected — too primitive for SQL editing (no syntax highlighting, no click-to-jump-from-problems).
- *Ace*: rejected as legacy compared to CodeMirror 6 and Monaco.

Source: `doc/WEB/M2-formatter-analyser-mvp.md` § 4.2.

**Open at this phase**: choice itself; resolved at M2.1.

---

## R3. Transport abstraction (M0): `IRpcTransport` + `IRpcRequestHandler<,>` + `RpcRouter`

**Decision**: Refactor the existing monolithic `PipeRpcServer` into:

- `IRpcTransport` — frame I/O and lifecycle (one implementation per transport: `NamedPipeTransport`, `InProcessTransport`, `WebSocketTransport`)
- `IRpcRequestHandler<TRequest, TResponse>` — one handler class per message type, grouped by folder (Completion, Formatting, Analysis, Snippets, Refactoring, Schema, AI, Control)
- `RpcRouter` — resolves `MessageType` integer, deserialises payload, dispatches to handler
- `RpcContext` — shared per-request state (settings, sessions, schema cache, logger)

Frame format `[length][CRC][MessagePack(RpcMessage)]` and all ~50 message type integer codes are **unchanged**.

**Rationale**:

- Three transports must serve the same handler set: named pipes (existing IDE plugins), in-process (Blazor WASM running engine logic in the browser tab, and engine unit tests with zero serialization), and WebSocket (browser ↔ local engine, M3+).
- Keeping the wire format unchanged means existing shell extensions need zero updates after the refactor — a hard requirement for SC-007.
- Handler registration via reflection scan matches the established `RuleRegistry` pattern in the analyser.

**Alternatives considered**:

- *Subclass `PipeRpcServer` per transport*: rejected — duplicates frame logic and the dispatch switch grows wider, not narrower.
- *Source-generated handler registration*: rejected for M0 scope — manual reflection works and keeps the change footprint small. Can revisit if registration ever shows up in a profile.
- *Different wire formats per transport* (e.g. JSON over WebSocket): rejected — doubles serialization code; MessagePack works fine over WebSocket binary frames.

Source: `doc/WEB/M0-dispatcher-transport.md` §§ 4.1–4.3.

---

## R4. Bridge transport: WebSocket over a single port; one MessagePack `RpcMessage` per WebSocket message

**Decision**: `WebSocketTransport` exposes the engine on a single configurable TCP port (default `47291`). The binding address determines mode:

- `127.0.0.1` → localhost mode, plaintext, no token required
- `0.0.0.0` → LAN mode, WSS required, pairing-token required

One WebSocket binary message carries exactly one `RpcMessage` MessagePack payload — no in-message framing.

**Rationale**:

- WebSocket gives full-duplex, low-latency, browser-native transport with reliable framing handled by the protocol — no need to re-implement the named-pipe `[length][CRC]` envelope inside individual messages.
- A single port keeps the firewall surface area minimal (one inbound rule for LAN mode) and matches the IDE plugin's single-pipe model.
- Localhost mode skipping the token is consistent with browser loopback being a trust boundary already; LAN mode forcing both WSS (clarification 1) and token (FR-013) closes the realistic threat models.

**Alternatives considered**:

- *gRPC-Web*: rejected — heavier dependency, adds protobuf to a stack already standardised on MessagePack.
- *Server-Sent Events / long polling*: rejected — half-duplex, awkward for the request/response shape.
- *Multiple ports (one per mode)*: rejected — doubles firewall config and confuses users.

Source: `doc/WEB/M3-websocket-transport.md` §§ 4.1, 4.3; clarification 1 (LAN-mode TLS).

---

## R5. LAN-mode TLS (clarification 1)

**Decision**: LAN-bound `WebSocketTransport` is **wss://** only, with an installer-generated self-signed RSA-2048 certificate emitted at install time, installed as the engine bridge's server certificate, and presented to the user with copyable trust instructions for each browser host. Localhost-bound transport may stay `ws://`.

**Rationale**:

- Modern browsers' mixed-content rules can refuse `ws://` from an HTTPS-loaded page, so plaintext is operationally fragile even before considering security.
- Pairing tokens carried in cleartext over a LAN can be captured by anyone with packet-capture access on the segment. Token confidentiality is required for FR-013/FR-014 to mean anything.
- A self-signed cert is the practical floor for "install in one click" — users on a private LAN can trust it manually; users with a CA can replace it.

**Alternatives considered** (presented at clarification time):

- *Plaintext + token only* — rejected at clarification time (Option C).
- *User-supplied cert path with self-signed fallback* (Option B) — fine but adds installer complexity for negligible benefit; users who can supply a cert can swap it post-install.
- *mTLS with per-browser client certs* (Option D) — rejected; needlessly heavy for a small-LAN single-user product and adds install friction.

Source: clarification 1, recorded in `spec.md` § Clarifications and codified in FR-013a.

---

## R6. Pairing flow: one-time 6-digit PIN → long-lived 256-bit bearer token

**Decision**: LAN-mode pairing flow:

1. Engine generates a one-time 6-digit PIN at startup and surfaces it (engine log + tray balloon + installer success page).
2. Browser sends a `HandshakeRequest` over WSS containing `{ pairingPin }`.
3. Engine validates the PIN; on success, mints a 256-bit random bearer token and discards the PIN.
4. `HandshakeResponse` returns `{ status: "ok", newBearerToken: "<hex>" }`.
5. Browser stores the token in IndexedDB on the connection record.
6. Subsequent connections supply the token in `HandshakeRequest.bearerToken`.

Token TTL: 90 days, refreshed on use.

Localhost mode skips steps 1–4: any loopback connection is trusted.

**Rationale**:

- PIN is short enough to read off a screen and dictate; one-time use mitigates shoulder-surfing.
- Bearer token is long-lived to avoid pestering the user, but regenerable from the engine UI (FR-014) to revoke compromised browsers.
- TTL of 90 days is conservative; user can reduce or override.

**Alternatives considered**:

- *No PIN, just a shared secret printed by the installer*: rejected — relies on the user to copy it correctly the first time, with no second-channel verification.
- *OAuth-style refresh tokens*: rejected — overkill for a single-user, single-trust-boundary product.

Source: `doc/WEB/M3-websocket-transport.md` § 4.2; FR-013/FR-014/FR-013a.

---

## R7. Version + capability handshake (clarification 5)

**Decision**: On every successful pairing (and on subsequent reconnections), the handshake exchanges:

- `engineVersion` (semver), `webVersion` (semver), `protocolVersion` (integer; M0 starts at 1)
- `engineCapabilities` (a flat list of capability identifiers, e.g. `["schema.v2", "ai.streaming.v1", "snippets.write"]`)

The browser tracks `engineCapabilities` and:

- enables only those features whose required capability identifier is present;
- shows an inline, dismissable notice on features whose required capability is missing, telling the user "this feature requires engine version ≥ X — open the installer to update";
- does **not** block the bridge for the unaffected features.

**Rationale**: matches clarification 5 (graceful degradation, no full-page blocker). Capability identifiers are more durable than version comparisons — a backported fix can advertise a capability the version number alone would not reveal.

**Alternatives considered**:

- *Strict version match (build hash)*: rejected (Option C in clarification) — breaks FR-023 install-one-component independence.
- *No handshake check (D)*: rejected — produces hard-to-diagnose support cases.
- *Full-page blocker on any mismatch (A)*: rejected — too disruptive for a partial-mismatch case.

Source: clarification 5, FR-017a.

---

## R8. Schema cache identity key (clarification 3)

**Decision**: The IndexedDB schema cache keys an entry as the tuple `(serverCanonicalIdentity, databaseName)`, where `serverCanonicalIdentity` is the value reported by the engine (preferably `@@SERVERNAME` if set, else `SERVERPROPERTY('ServerName')`, else a stable fallback derived from instance metadata — engine returns the resolved canonical identifier in a `SchemaIdentify` response on first connect).

**Rationale**: matches clarification 3 — same physical SQL Server seen via DNS alias, IP-vs-FQDN, or via a different engine pairing all resolve to one entry. Per-user separation comes for free from browser profile isolation, so the key does not encode user identity.

**Alternatives considered**:

- *Connection string + database name*: rejected (Option B in clarification) — duplicate caches for alias variations.
- *Engine pairing id + server identity + database name*: rejected (Option C) — duplicates when two paired engines point at the same SQL Server.
- *User-named workspace + database*: rejected (Option D) — extra setup friction.

Source: clarification 3; FR-024–FR-028; data-model.md `SchemaCacheEntry`.

---

## R9. Browser-side AI key storage (clarification 2)

**Decision**: AI provider keys are stored in IndexedDB **wrapped at rest** using a non-extractable Web Crypto `CryptoKey` of algorithm `AES-GCM` (256-bit), with per-record IV and additional authenticated data containing the provider identifier. The wrapping key is generated once per browser profile and stored as a non-extractable `CryptoKey` reference in IndexedDB (the underlying material is held by the browser key store and is never exposed to JS). No passphrase prompt to the user.

**Rationale**:

- Non-extractable wrapping keys mean a malicious browser extension or co-resident origin reading IndexedDB cannot exfiltrate the plain key — the wrap can only be undone inside the same browser security context.
- AES-GCM gives authenticated encryption; AAD bound to provider identifier prevents cross-record substitution attacks.
- Zero ergonomic cost (no passphrase prompt) — matches clarification 2's "transparent to the user" requirement.

**Alternatives considered**:

- *Plain in IndexedDB* (Option A): rejected — gives the attacker the key with no further work.
- *Per-session passphrase wrapping* (Option D): rejected — users would store the passphrase next to the key, or disable the feature.
- *Holding the key in the engine via DPAPI* (Option C): would have broken FR-029's "stored only in browser storage" promise and added an engine-up dependency for AI features that should work offline.

Source: clarification 2; FR-029; contracts/ai-key-wrapping.md.

---

## R10. Theme parity: single JSON source generates WPF tokens and web CSS variables

**Decision**: Add `docs/theme-tokens.json` as the single source of truth. The WPF `ThemeRegistry` (spec 016) and a new CSS generator script both consume this JSON. The script emits `themes/light.css`, `themes/dark.css`, `themes/high-contrast.css` with CSS custom properties (`--akml-surface-base`, `--akml-accent`, etc.) named identically to the WPF brush tokens.

**Rationale**:

- Two surfaces (WPF, web) that diverge visually undermine the "same product" identity (FR-004).
- A single source keeps the two in sync mechanically; M2.5 includes a side-by-side screenshot pass to validate.

**Alternatives considered**:

- *Manual hand-port of theme tokens to CSS*: rejected — high drift risk over the M2–M6 timeline.
- *Run the WPF theme system inside the browser*: rejected — impossible (WPF is Windows-only) and unnecessary.

Source: `doc/WEB/M2-formatter-analyser-mvp.md` § 4.3; spec 016 (existing WPF theme tokens).

---

## R11. Profile storage in the browser

**Decision**: Three tiers, all rooted in IndexedDB plus embedded resources:

- **Built-in profiles** (e.g. ANSI defaults): embedded in the WASM bundle as JSON resources.
- **User-imported profiles**: imported via `<InputFile>` (`.akmlstyle` or `.sqlpromptstylev2`), parsed by the same C# code the WPF surface uses (round-trips through `AkmlSql.Formatting`), persisted to IndexedDB. Exported back as a download.
- **SQL Prompt round-trip**: unchanged C# code from spec 020 runs in WASM identically.

**Rationale**: keeps the import/export experience identical to the IDE plugin (FR-009) and reuses the validated spec-020 round-trip code without divergence.

Source: `doc/WEB/M2-formatter-analyser-mvp.md` § 4.4.

---

## R12. Installer integration (M4)

**Decision**: Add a new component group to the existing Inno Setup 7 installer (`AkmlSqlSetup.iss`) with two sub-options:

1. **Host on local IIS (recommended)** — installer detects IIS (registry `HKLM\SOFTWARE\Microsoft\InetStp` + `inetsrv\appcmd.exe`), creates an IIS site (or application under Default Web Site) pointing at the WASM bundle directory, configures MIME types (`.wasm`, `.dat`, `.dll`, `.blat`, `.br`), and writes the engine's transport binding into `%AppData%/AKML SQL Web/config.json`.
2. **Don't host — I'll serve the files myself** — lays down the WASM bundle to `%ProgramFiles%/AKML SQL/Web/` but skips IIS configuration.

Network exposure radio: **Localhost only** (default) or **LAN exposed**. LAN exposed:

- Creates a Windows Firewall inbound rule for the chosen port (default `47291`).
- Generates a self-signed RSA-2048 cert via `New-SelfSignedCertificate` (PowerShell) bound to the host's reachable name and IP.
- Prints the pairing PIN and the LAN URL on the installer success page; writes a copyable summary file to `%ProgramFiles%/AKML SQL/Web/INSTALL-SUMMARY.txt`.

The lightweight fallback host (Kestrel-as-Windows-service) is mentioned in the spec (FR-021) but deferred to a follow-up — M4 PRD scopes IIS only. See Open Questions.

**Rationale**: matches the M4 PRD; reuses the existing installer rather than a second installer; the deferred fallback host is non-blocking for the rest of the milestones.

**Alternatives considered**:

- *Bundle Kestrel-as-Windows-service in M4*: deferred — adds installer testing surface without enabling new user-visible functionality the spec mandates as in-scope for M4.
- *Separate installer for web edition*: rejected — duplicates engine deployment logic.

Source: `doc/WEB/M4-iis-installer.md`; FR-018–FR-023.

---

## R13. Diagnostic ring buffer + Export bundle (clarification 4)

**Decision**: New service `DiagnosticsRingBuffer` in `AkmlSql.Web.Services`:

- Fixed size ring buffer in memory (default 2 048 entries, configurable per session), persisted to IndexedDB on flush.
- Entry shape: `{ ts: ISO8601, level: "trace"|"info"|"warn"|"error", source: "formatter"|"analyser"|"bridge"|"ai"|"cache"|"ui", message: string, data?: object }`
- `Settings → Export diagnostics` triggers an `ExportBundle` request:
  - Gather the ring buffer (JSON array)
  - If bridge is reachable, request `EngineLogTail` (last N MB of the engine's most recent log file) over the bridge — engine returns the bytes, browser appends to the bundle as `engine.log`
  - Bundle the artefacts into a single downloadable `akmlsql-web-diagnostics-<timestamp>.zip`
- No diagnostic content is transmitted off the user's machine without the user clicking Export.

**Rationale**: matches clarification 4 and FR-005a. The bundle is the one artefact users send to support; engine logs are still on disk for advanced diagnostics.

**Alternatives considered**:

- *Browser-only logs* (Option B): too narrow — most reportable issues need both sides.
- *DevTools-only* (Option C): unscalable for non-technical users.
- *Engine-only* (Option D): ignores the new browser code path entirely.

Source: clarification 4; FR-005a.

---

## R14. Two engines on one host (FR-003, edge case "Two engines coexist")

**Decision**: The web edition's engine instance runs out of `%AppData%/AKML SQL Web/`, distinct from the plugin instance at `%AppData%/AKML SQL/`. The two never share:

- config (`config.json`)
- logs
- cache directories
- named pipe names (web edition's engine uses pipe name suffix derived from the web-edition install path hash)
- WebSocket ports (the web edition's engine uses the configured port; the plugin engine does not listen on a WebSocket at all)

The web-edition engine is launched by the web-edition Windows service or the IIS app pool init script, not by `EngineProcessManager` in the shell.

**Rationale**: spec mandates independence (FR-003, SC-007). Co-located config has bitten the project before per CLAUDE.md hints; separating directories is the cheapest, most durable approach.

**Alternatives considered**:

- *Shared engine with namespaced config*: rejected — couples plugin and web release cycles.
- *Same `%AppData%` directory with sub-key partitioning*: rejected — file lock conflicts.

Source: `doc/WEB/00-INDEX.md` § Key decisions; FR-003.

---

## R15. ScriptDom + System.Text.Json + Serilog in WASM (M1 confirmation)

**Decision**: The M1 spike confirms these dependencies run in WASM:

- `Microsoft.SqlServer.TransactSql.ScriptDom` — yes; the package is pure C# and runs unchanged. Bundle size impact ~3 MB pre-trim, trimmed to ~1.6 MB.
- `System.Text.Json` — yes; .NET 10 WASM includes the runtime.
- `Serilog` — yes; the file sink does not work in WASM but an in-memory sink (the ring buffer) is wired up instead.

**Rationale**: validates that the formatter/analyser/profile-parsing code can be reused without porting.

Source: `doc/WEB/M1-wasm-spike-skeleton.md`.

---

## R16. Open / deferred items (informational; not blocking)

These are not `NEEDS CLARIFICATION` items — they are scoped follow-ups that do not block planning or implementation of M0–M6 as written.

| Item | Why deferred | When to revisit |
|------|--------------|-----------------|
| Lightweight Kestrel-as-service fallback host (FR-021) | M4 PRD limits itself to IIS; fallback host is one week's work and can land between M4 and M5 | After M4 ships; reopen as a small spec if a non-IIS user reports the need |
| Accessibility / a11y conformance level (WCAG target) | Spec does not commit to a target; existing IDE plugin has no published a11y baseline | At M2.1 with the editor choice, the a11y story for Monaco / CodeMirror is locked |
| Localisation (i18n) | Spec does not require localisation; IDE plugin is English-only | Reopen if the project adopts a localisation initiative |
| Bridge rate-limiting | Single-user model removes the typical motivation; the engine's existing input validation suffices | Reopen if a multi-tab or multi-browser pattern shows pathological load |
| Streaming AI responses over the bridge | Out of M6 scope per `doc/WEB/M6-ai-browser.md`; in-scope work uses request/response | Separate spec post-M6 |
| Mobile / tablet layout | Spec marks as out-of-scope | Separate spec if/when a mobile use case is funded |

---

## Summary: NEEDS CLARIFICATION → resolved

| Marker location | Status |
|-----------------|--------|
| Technical Context — Language/Version | Resolved (R1, R3) |
| Technical Context — Primary Dependencies | Resolved (R1, R2, R4) |
| Technical Context — Storage | Resolved (R8, R9, R11, R13, R14) |
| Technical Context — Testing | Resolved (project structure in plan.md) |
| Technical Context — Target Platform | Resolved (R1) |
| Technical Context — Project Type | Resolved (plan.md Structure Decision) |
| Technical Context — Performance Goals | Resolved (R1, R2, M0 success metric) |
| Technical Context — Constraints | Resolved (parity corpus, R3) |
| Technical Context — Scale/Scope | Resolved (R1, R14) |

Phase 0 complete — proceed to Phase 1 (data model, contracts, quickstart).
