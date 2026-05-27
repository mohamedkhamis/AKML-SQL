# Feature Specification: M3 — WebSocket Transport & Local-Agent Bridge Closure

**Feature Branch**: `025-m3-bridge-closure`
**Created**: 2026-05-27
**Status**: Draft
**Input**: User description: PRD `doc/WEB/M3-websocket-transport.md` (Status: Draft; Estimated effort 2–3 weeks)

## Overview

The M3 PRD ("Web edition talks to the local engine over WebSocket") looks like greenfield work but is substantially **already merged inside spec 021 Phase 4** — the engine has a working `WebSocketTransport`, a working `HandshakeHandler`, a working `PairingService` + `BearerTokenStore`; the browser has a working `EngineBridge`, `ConnectionStore`, `PairingTokenVault`, `ConnectionPickerComponent`, and every bridge-routed IntelliSense / signature-help / quick-info / goto-definition service is wired. Twenty-five of the thirty Phase-4 tasks (T056–T080) are marked `[X]` with detailed completion notes.

What is **not** merged maps to four named gaps and one verification gap, each tied to a Definition-of-Done checkbox the M3 PRD §12 cannot retire today:

1. **The WebSocket transport is never started by the engine host, and the LAN binding has no `https://` code path** — `WebSocketTransport` exists as a class and has 5 round-trip tests, but `EngineHost.RunAsync` only instantiates `NamedPipeTransport`; the engine never actually listens for WebSocket connections in production. The class also rejects non-loopback construction outright (spec 021 T057 + FR-013a). The TLS certificate, the firewall rule, the cert binding, and the `TlsCertPath` option are all already produced by the M4 installer work (T087/T088 merged); the engine just doesn't consume any of them. Spec 021 T058 is deferred against this; the engine-host composition is an additional gap surfaced during the closure-spec plan review.
2. **Auto-reconnect is documented but unwired** — `EngineBridge`'s `BridgeState` enum already declares `Reconnecting`, the M3 PRD §5 says "Auto-reconnect on transient drops: Yes", but the receive loop ends in `State = BridgeState.Disconnected` and never re-handshakes. Spec 021 T068's completion note calls this out as a follow-up.
3. **The DoD's "renders tree" UI does not exist** — `ISchemaSync` correctly fetches Phase A / Phase B snapshots into IndexedDB on checksum drift, but there is no `SchemaTreeComponent.razor` rendering `Database → Schema → Tables` for the user. The DoD requires "Browser connects, fetches Phase A schema, **renders tree**."
4. **Two security-and-rollout documents promised by DoD §12 do not exist** — `doc/m3-security.md` (threat model) and `doc/WEB/quickstart-m3.md` (firewall guidance + how-to-pair-from-another-machine walkthrough). The other quickstart docs exist (`quickstart-m2.md`, `quickstart-m4.md`, `quickstart-m5.md`, `quickstart-m6.md`); M3's is the only gap.
5. **No end-to-end coverage on the wire** — spec 021 T078 (Playwright UserStory2Tests over a real local engine) and T079 (`BridgeHandshakeTests.cs` for WSS pair + reconnect + revocation) are deferred awaiting Playwright + a fake DB harness + a real engine. Without them, the DoD's "Pairing flow works end-to-end with a second machine on the LAN" checkbox cannot be retired against evidence.

This is a verification + plumbing closure, not a redesign. The five user stories below map 1:1 to these gaps in priority order; everything else the M3 PRD describes is already shipped and is explicitly **not** rewritten by this spec.

**Open follow-ups acknowledged but deferred** (consistent with how spec 021 left T065 and T066-partial open):

- **TLS fingerprint pinning UI** (`EngineConnection.TlsFingerprint` exists in the data model and is recorded on first connect; the cert-mismatch dialog is unbuilt). Out of scope here — beyond DoD §12, completes a `contracts/pairing-flow.md` flow.
- **Engine-side tray UI for the pairing pane** (T065 — Windows WPF/tray work).
- **In-flight WebSocket revocation when a bearer is revoked** (T066-partial — drops paired sockets the instant `BearerTokenStore.RevokeByHash` runs; the *next* connect already refuses revoked bearers).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Pair a browser on a second LAN machine over WSS (Priority: P1)

A team-mate on the same LAN can open the web edition in their browser, paste the host + port + PIN from the engine's install summary, and have a working live-schema session against the engine running on the host workstation. The WebSocket connection is encrypted end-to-end (`wss://`), and the engine refuses any plaintext binding to a non-loopback address.

**Why this priority**: This is the single biggest unmet checkbox in the M3 DoD. Until WSS works on the wire, the LAN feature the PRD's first paragraph promises ("the browser app gets an Add a connection UI… stores the token in IndexedDB after pairing") is a no-op for any binding the installer can actually produce.

**Independent Test**: Stand up the engine on Machine A bound to `0.0.0.0:47291` with the installer-generated PFX at `%ProgramData%/AKML SQL Web/certs/bridge.pfx`. From Machine B's browser, open the web edition, click Add Connection, enter Machine A's IP + port + the PIN printed in the install summary. The handshake succeeds, the bearer token is minted and stored, the status bar transitions to Open, and a subsequent reload reconnects without re-prompting for the PIN.

**Acceptance Scenarios**:

1. **Given** the engine bound to `0.0.0.0:47291` with a valid PFX path, **When** the browser issues `wss://<machine-a>:47291/akmlsql` with a valid PIN, **Then** the handshake returns `HandshakeStatus.Ok` with a newly-minted bearer token and the bridge state becomes `Open`.
2. **Given** the engine bound to `0.0.0.0:47291` with **no** PFX configured, **When** the engine starts, **Then** startup fails fast with a clear error referencing `TlsCertPath` and FR-013a — plaintext LAN binding is never silently accepted.
3. **Given** a paired browser on Machine B, **When** the browser is closed and re-opened the next day, **Then** the stored bearer token authenticates the reconnect without showing the PIN prompt and the bridge reaches `Open` within 10 seconds.
4. **Given** the engine bound to `127.0.0.1:47291` only, **When** Machine B tries to connect to Machine A's LAN IP, **Then** the connection is refused at the network layer (the engine is not listening on that interface) and Machine B's browser surfaces a connection-failed status without leaking the loopback URL.

---

### User Story 2 - Read the M3 threat model and firewall guidance before deploying (Priority: P1)

A reviewer auditing the LAN-mode deployment — or a user trying to pair from a second machine for the first time and confronted by the Windows Firewall prompt — can read a written threat model and a step-by-step pairing walkthrough that match what the code actually does.

**Why this priority**: DoD §12 explicitly requires `doc/m3-security.md` and "firewall guidance documented." Neither file exists today. The PRD §8 already contains the threat-model table; what this spec produces is the version-controlled markdown that lives in the repo so security review has a stable artefact and so the next on-call has somewhere to point a confused user.

**Independent Test**: After landing, a fresh reviewer who has never seen the codebase can read `doc/m3-security.md` end-to-end and answer: "What does plaintext-on-LAN mean for me?", "Why is the PIN single-use?", "How long does a token last?", "What does the engine do when I revoke?". A user on Machine B can follow `doc/WEB/quickstart-m3.md` from a blank Chrome session to a working live-schema editor in under 5 minutes, including the firewall click-through on first start.

**Acceptance Scenarios**:

1. **Given** the threat-model document, **When** a reviewer reads it, **Then** every PRD §8 row appears with the same wording and the document explicitly states that plaintext-on-LAN is forbidden by the code (refusal at `WebSocketTransport` construction).
2. **Given** the quickstart document, **When** a user follows it from "engine installed" to "first completion suggestion in the browser", **Then** every command and click is present (firewall prompt, PIN paste, status-bar verification) with verbatim text or screenshots.
3. **Given** DoD §12 checkbox 6 ("Threat model documented in `doc/m3-security.md`"), **When** the document lands at that path, **Then** the checkbox can be marked closed.

---

### User Story 3 - Survive an engine restart without losing the editor session (Priority: P2)

When the engine process restarts (Windows update reboot, manual service restart, transient network blip on the LAN), the browser does not stay stuck on Disconnected. The receive loop transitions to `Reconnecting`, retries with exponential back-off capped at 30 s, re-runs the handshake against the stored bearer token, and the editor is functional throughout — formatting and analysis keep working in-browser per FR-016 while the bridge is down.

**Why this priority**: PRD §5 Feature scope lists "Auto-reconnect on transient drops: Yes" in M3. PRD §7 risks-and-mitigations specifies "Reconnect storm during engine restart — Exponential back-off; max retry interval 30s." PRD §9 success metric: "Reconnect after engine restart succeeds within 10 seconds." `BridgeState.Reconnecting` already exists in the enum (`src/AkmlSql.Web/Services/IEngineBridge.cs:57`). The state value never gets set today because the receive loop fails into `Disconnected` on socket close.

**Independent Test**: Start the engine, pair the browser, type SQL in the editor (completions arrive from the engine). Restart the engine process. The status bar shows `Reconnecting` (not `Disconnected`); within 10 seconds of the engine accepting connections again, the bar returns to `Open` and live completions resume — without the user clicking anything. Throughout the gap, in-browser formatting / analysis still respond to Ctrl+S / Ctrl+K Ctrl+F.

**Acceptance Scenarios**:

1. **Given** an `Open` bridge, **When** the engine process is killed, **Then** the bridge transitions to `Reconnecting` (not `Disconnected`) and a retry is scheduled.
2. **Given** a `Reconnecting` bridge, **When** the engine comes back up, **Then** the next retry succeeds, the handshake replays the stored bearer token, and the bridge transitions to `Open` — the user never re-enters the PIN.
3. **Given** an engine that stays down, **When** retries fail repeatedly, **Then** the back-off doubles with each attempt and is capped at 30 seconds; the status bar shows the time-until-next-retry so the user knows what's happening.
4. **Given** a `Reconnecting` bridge, **When** the user runs Format Document or Run Analysis, **Then** both commands work entirely in-browser (FR-016 graceful offline) without erroring or showing a connectivity dialog.

---

### User Story 4 - Browse the database object tree in a side panel (Priority: P2)

A user with an `Open` bridge sees a collapsible tree of the live database: top-level node per database the engine has loaded, then schemas, then tables / views / stored procedures. Clicking a node either inserts the qualified name into the editor or expands to reveal its columns (Phase B). The tree updates progressively — Phase A nodes appear within 500 ms of the bridge opening; Phase B detail fills in as the background fetch completes.

**Why this priority**: DoD §12 checkbox 3 is "Browser connects, fetches Phase A schema, **renders tree**." Phase A fetching is wired (`ISchemaSync.FetchPhaseAAsync`), Phase B fetching is wired (`FetchPhaseBAsync`), the snapshots persist into IndexedDB (`SchemaSnapshot` records), but no UI surface renders them. The user has no way to see what schema the engine knows about — and without the tree, "live schema" is invisible.

**Independent Test**: With an `Open` bridge against a sample database, open the web edition. Within 500 ms of `Open`, a `Schema` panel on the right (or wherever the layout places it) shows the database name + its schemas + every user table. Within ~5 seconds (background Phase B), each table is expandable to show its columns and types. Click a table — its `[schema].[table]` qualifier is inserted at the editor caret.

**Acceptance Scenarios**:

1. **Given** an `Open` bridge and a populated Phase A snapshot, **When** the user opens the Editor page, **Then** the schema panel renders Database → Schema → Object-Kind groupings within 500 ms of the snapshot being available.
2. **Given** a populated Phase B snapshot, **When** the user expands a table node, **Then** its columns and types are shown without a fresh round-trip to the engine (read from the cached snapshot).
3. **Given** the bridge is in `Disconnected` state, **When** a previously-cached snapshot exists in IndexedDB, **Then** the schema panel renders the cached tree with a stale-indicator badge instead of going blank.
4. **Given** a checksum drift detected by `ISchemaSync`, **When** the new Phase A snapshot lands, **Then** the tree refreshes in place without losing the user's currently-expanded nodes.

---

### User Story 5 - End-to-end coverage proves the wire works (Priority: P3)

A Playwright suite under `tests/AkmlSql.Web.E2E.Tests/` exercises User Story 2 acceptance scenarios against a real engine instance; a parallel xUnit suite under `tests/AkmlSql.E2E.Tests/BridgeHandshakeTests.cs` covers the WSS pair → reconnect → revoke flow end-to-end. Both runs build the engine and the web bundle from the current source before launching so a passing run cannot be a stale-build artefact.

**Why this priority**: DoD §12 checkbox 5 is "Pairing flow works end-to-end with a second machine on the LAN." Today the protocol logic is covered by `FakeBridgeWebSocket` loopback tests (`HandshakeClientTests`); the WSS-on-the-wire variant has no green run. Without this suite, the DoD's "second machine on the LAN" assertion is unbacked by evidence.

**Independent Test**: `dotnet test tests/AkmlSql.Web.E2E.Tests/UserStory2Tests.cs` builds the web app + engine, launches both, drives a Chromium pair → completion → reconnect → revocation in a single fixture, and ends green. `dotnet test tests/AkmlSql.E2E.Tests/BridgeHandshakeTests.cs` runs the same flow without a browser (pure RPC).

**Acceptance Scenarios**:

1. **Given** a fresh checkout, **When** the developer runs the E2E suite, **Then** the harness builds engine + web from current source, launches the engine on a free port, drives the browser through pairing + a completion request + an engine kill + a reconnect, and reports pass.
2. **Given** a paired engine, **When** the suite revokes the active bearer mid-session, **Then** the next bridge request fails with `PinRequired` and the test asserts the browser surfaces the re-pair UI rather than retrying indefinitely.
3. **Given** the suite is excluded by default (matching how spec 023's `[Trait("Category","SpikeGenerator")]` is excluded), **When** the normal `dotnet test` command runs, **Then** the bridge E2E does not run automatically — a developer must opt in with the `--filter Category=BridgeE2E` flag.

---

### Edge Cases

- **Plaintext LAN binding requested** — the engine refuses to start; the error message points at `TlsCertPath` and FR-013a. The installer never writes a config that would produce this, but a user editing `config.json` by hand will see a clear refusal rather than a silent insecure binding.
- **Self-signed cert mismatch after a reinstall** — the installer regenerates the cert on re-run; previously-paired browsers will see a new TLS fingerprint. Until the fingerprint-pinning UI lands (deferred follow-up), the existing connection will silently use the new cert because the `TlsFingerprint` field is recorded but not compared. Document this in the threat model.
- **Engine restart during a long-running analysis** — the analysis runs in-browser (`AnalysisEngine` over `netstandard2.0`), so the engine death has no effect on the running pass. The reconnect handler must not cancel the in-browser work.
- **Reconnect storm** — the back-off doubles each attempt; ensure the first retry happens fast (~500 ms) so a one-second engine blip recovers instantly, but the cap of 30 s prevents a permanently-down engine from being polled at burst rate.
- **Schema tree on a database with thousands of tables** — Phase A bytes can be large. The tree must virtualise rows or lazy-render schema children, otherwise the editor page chokes on the first render.
- **Stale tokens that survive a re-install** — `BearerTokenStore.json` is preserved across installer re-runs (spec 021 T094); paired browsers stay paired. The threat model must note this so a security reviewer understands why an uninstall does not invalidate pairing unless the AppData prompt is accepted.
- **Two browsers on the same machine pair against the same engine** — each pairs with its own PIN-mint cycle, gets its own bearer token, and revokes independently. The threat model must call this out so per-browser revocation is visible to operators.
- **Quickstart vs install summary drift** — the quickstart references the install summary file at `%CommonAppData%/AKML SQL Web/INSTALL-SUMMARY.txt`. If spec 021 T093 changes that path, both docs must be updated together.

## Requirements *(mandatory)*

### Functional Requirements

#### LAN-mode TLS plumbing (US1)

- **FR-001**: The engine's WebSocket transport MUST serve `wss://` on the configured `BindAddress:Port` whenever `BindAddress` is non-loopback, using the PFX path supplied in `WebSocketTransportOptions.TlsCertPath` and the password reference in `TlsCertPasswordRef`.
- **FR-002**: The engine MUST refuse to start when `BindAddress` is non-loopback and `TlsCertPath` is empty or missing on disk. The refusal message MUST reference both `TlsCertPath` and the FR-013a constraint from spec 021.
- **FR-003**: The loopback HttpListener path MUST continue to work unchanged — localhost-mode bindings stay on the existing path and do not require a certificate.
- **FR-004**: The browser bridge MUST construct `ws://` for localhost connections and `wss://` for non-localhost connections; the protocol scheme is derived from the connection record's `IsLocalhost` flag (not from configuration on the browser side).
- **FR-005**: The first connect to a non-localhost connection MUST record the engine's TLS certificate fingerprint into `EngineConnection.TlsFingerprint`. Subsequent reconnects to the same connection MUST observe the recorded fingerprint and log a warning to the diagnostics ring buffer when it changes. (The user-facing cert-mismatch dialog itself is a deferred follow-up.)
- **FR-006**: Engine unit tests under `tests/AkmlSql.Engine.Tests/Transports/` MUST cover a round-trip WSS handshake on a non-loopback binding using a unit-test self-signed certificate and assert that the LAN-without-TLS construction refusal still works.

#### Threat model + firewall + quickstart docs (US2)

- **FR-007**: `doc/m3-security.md` MUST exist and MUST contain every row from PRD §8 Threat Model with the same wording, plus the additional rows: "plaintext-on-LAN refused at construction" and "cert regeneration on installer re-run silently swaps fingerprint until the pinning UI lands."
- **FR-008**: `doc/WEB/quickstart-m3.md` MUST exist and MUST walk a user from a fresh `AKMLSQLSetup.exe /WEB_EXPOSURE=LAN` install through: the Windows Firewall click-through, copying the PIN out of the install summary, pasting it into the browser's Add Connection dialog, verifying the status bar reaches `Open`, and seeing a live completion suggestion.
- **FR-009**: `doc/m3-security.md` MUST cite the file paths the running engine actually uses: `%CommonAppData%/AKML SQL Web/tokens.json` (hashed bearer tokens), `%CommonAppData%/AKML SQL Web/pairing-pin.txt` (current PIN), `%ProgramData%/AKML SQL Web/certs/bridge.pfx` (TLS cert), so a reviewer can audit ACLs and on-disk state.
- **FR-010**: `doc/WEB/quickstart-m3.md` MUST follow the format of the existing `quickstart-m2.md` and `quickstart-m4.md` documents so the four-quickstart set reads as one product story.

#### Exponential-backoff reconnect (US3)

- **FR-011**: When the browser's `EngineBridge` receive loop terminates due to a socket close that is not initiated by the user (i.e., `DisconnectAsync` was not called), the state MUST transition to `Reconnecting` and a retry MUST be scheduled.
- **FR-012**: Retries MUST use exponential back-off starting at approximately 500 ms with a multiplier of 2.0 and a maximum interval of 30 seconds (matches PRD §7 risk row "Reconnect storm").
- **FR-013**: When a retry succeeds against a connection that has a stored bearer token, the bridge MUST replay the bearer in the handshake — the user MUST NOT be prompted for the PIN.
- **FR-014**: When a retry's handshake returns `PinRequired` (bearer revoked), the bridge MUST transition to `Failed`, surface the re-pair UI, and stop retrying. The retry loop MUST NOT spin against a revoked bearer.
- **FR-015**: During `Reconnecting`, in-browser formatting (`FormatterService`) and analysis (`AnalyserService`) MUST continue to function — the reconnect attempt MUST NOT block the UI thread and MUST NOT cancel in-flight in-browser work.
- **FR-016**: The status bar MUST display the current retry interval and a "Reconnecting now" indicator during the brief windows when the WebSocket open call is in flight.

#### Schema object tree (US4)

- **FR-017**: A `SchemaTreeComponent.razor` (or equivalent shared surface) MUST render the Phase A snapshot for the active connection within 500 ms of `Open`, organised as Database → Schema → Object-Kind (Tables, Views, Stored Procedures, Functions).
- **FR-018**: When the Phase B snapshot is available, expanding a table or view node MUST reveal its columns with types, read from the cached snapshot — no extra round-trip to the engine.
- **FR-019**: Clicking a leaf node MUST insert the qualified `[schema].[name]` into the editor at the current caret position.
- **FR-020**: When the bridge is in `Disconnected` or `Reconnecting` state and a cached snapshot exists in IndexedDB, the tree MUST render the cached data with a "stale — last fetched <relative-time>" badge.
- **FR-021**: When `ISchemaSync.ChecksumDrifted` fires and a new Phase A snapshot lands, the tree MUST refresh in place without losing the user's currently-expanded nodes (preserve expansion state across snapshot version changes).
- **FR-022**: Rendering MUST handle a Phase A snapshot containing at least 2,000 tables without freezing the editor page (lazy / virtualised children acceptable; the bar is "no jank" not a specific frame budget).

#### End-to-end coverage (US5)

- **FR-023**: `tests/AkmlSql.Web.E2E.Tests/UserStory2Tests.cs` MUST exist and MUST drive the four US2 acceptance scenarios from spec 021 against a real engine instance launched by the test fixture.
- **FR-024**: `tests/AkmlSql.E2E.Tests/BridgeHandshakeTests.cs` MUST exist and MUST cover the WSS pair → completion → engine-kill → reconnect → revocation flow without a browser (pure xUnit + the production `WebSocketTransport`).
- **FR-025**: Both suites MUST build the engine and (for UserStory2Tests) the web bundle from the current source before launching, so a green run is not a stale-build artefact.
- **FR-026**: Both suites MUST be excluded from default `dotnet test` runs via `[Trait("Category","BridgeE2E")]`, runnable with `--filter Category=BridgeE2E`. This mirrors the established `SpikeGenerator` and `ParityBaseline` opt-in patterns.

#### Engine-host composition (US1, cross-cutting)

- **FR-027**: `EngineHost.RunAsync` MUST compose a `WebSocketTransport` **alongside** the existing `NamedPipeTransport` whenever the engine's config requests a bridge — both transports share the same `RpcRouter` so the SSMS plugin and the web edition serve identical handler chains (PRD §11 open question 4: "yes, both pipe + WebSocket simultaneously"). Today the host only instantiates `NamedPipeTransport`; the `WebSocketTransport` class exists but is never started. The composition MUST read `WebSocketTransportOptions` from the same `config.json` the engine already reads (`%AppData%/AKML SQL Web/config.json` per spec 021); when the bridge section is absent or `Disabled`, only the named pipe runs (preserves IDE-plugin-only deployments). An `EngineHostTests` assertion MUST cover the dual-transport composition: when config requests both, both transports are started and both can reach the same `RpcRouter` instance.

### Key Entities

- **WSS Engine Binding**: The Kestrel-hosted variant of `WebSocketTransport` that serves `wss://0.0.0.0:<port>/akmlsql` using a PFX from disk. Reuses the existing `HandshakeHandler` and `RpcRouter` — only the listener tier changes.
- **Reconnect schedule**: The exponential-backoff sequence (≈ `500 ms, 1 s, 2 s, 4 s, 8 s, 16 s, 30 s, 30 s, …`) used between retry attempts; surfaced to the status bar so users see the time-until-next-retry.
- **Schema tree node**: A Database / Schema / Object-Kind / Object / Column entry rendered from the cached `SchemaSnapshot`; carries kind, parent linkage, lazy-children flag, and qualified-name string for click-to-insert.
- **M3 threat-model document**: The version-controlled markdown at `doc/m3-security.md` carrying PRD §8 plus the two additional rows; the canonical security reference for LAN-mode deployments.
- **Quickstart-m3 document**: The version-controlled markdown at `doc/WEB/quickstart-m3.md` walking a fresh user through LAN-mode pairing.
- **Bridge E2E test category**: The `[Trait("Category","BridgeE2E")]` opt-in label that gates `UserStory2Tests` and `BridgeHandshakeTests` from the default test run.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user on Machine B can pair with the engine on Machine A and reach `BridgeState.Open` within 30 seconds of starting Add Connection (matches PRD §9 success metric).
- **SC-002**: After an engine kill while the bridge is `Open`, the bridge returns to `Open` within 10 seconds of the engine accepting new connections — without a manual click (matches PRD §9 success metric).
- **SC-003**: A reviewer reading `doc/m3-security.md` end-to-end can answer the threat-model audit checklist (PRD §8 rows + the two added rows) without consulting any other document.
- **SC-004**: A user following `doc/WEB/quickstart-m3.md` from blank Chrome on Machine B to first live completion suggestion completes the walkthrough in under 5 minutes.
- **SC-005**: The schema tree renders within 500 ms of an `Open` bridge against a sample database with up to 2,000 tables — no UI freeze.
- **SC-006**: `dotnet test ... --filter Category=BridgeE2E` runs both bridge E2E suites and reports pass on the closure-spec landing commit.
- **SC-007**: Plaintext LAN binding (non-loopback `BindAddress` + empty `TlsCertPath`) refuses at engine startup 100% of the time — no silent insecure path.
- **SC-008**: Every M3 PRD §12 Definition-of-Done checkbox can be marked closed against either a shipped feature (already merged) or one of FR-001 … FR-027.
- **SC-009**: With the bridge section enabled in `config.json`, a single `AkmlSql.Engine.exe` process serves both the SSMS plugin (via named pipe) and the web edition (via WebSocket) simultaneously — verified by `EngineHostTests.DualTransportCompositionRoutesViaSameRouter`.

## Dependencies and Assumptions

### Dependencies

- Spec 021 Phase 4 tasks T056–T080 (already merged) provide the engine handshake, pairing service, bearer-token store, browser-side bridge, connection store, pairing-token vault, connection picker, and bridge-routed services. This spec does not re-touch any of them.
- Spec 021 Phase 5 tasks T087–T088 (already merged) generate the self-signed cert and bind it to the bridge port. FR-001 consumes `%ProgramData%/AKML SQL Web/certs/bridge.pfx` produced by this work.
- Spec 021 Phase 6 task T108 (already merged) provides `ISchemaSync` — the Phase A/B fetch and IndexedDB persistence US4 renders.
- The Playwright .NET stack and `Microsoft.Playwright` package are already wired into `tests/AkmlSql.Web.E2E.Tests/` per spec 024.

### Assumptions

- The installer-generated cert under `%ProgramData%/AKML SQL Web/certs/bridge.pfx` is the only cert the engine needs to consume in M3. Custom-cert support is out of scope here.
- Reconnect timing values (500 ms initial, 2x multiplier, 30 s cap) are not knobs the user adjusts — they are constants in `EngineBridge`. If telemetry later shows they're wrong, that's a separate spec.
- The schema tree's "lazy-render past N children" threshold is an implementation detail picked during the build (the FR sets a behavioural bar, not a specific N).
- The fingerprint-pinning UI dialog and the engine-side pairing tray UI remain deferred follow-ups; this spec records the fingerprint change to the diagnostics buffer but does not put up a modal.
- The E2E suite runs developer-side (not CI) — same constraint as the parity tests in spec 024.

## Out of Scope (deferred follow-ups)

The PRD's open questions §11 and the "deferred follow-up" notes in spec 021 Phase 4 leave the following items un-addressed by this closure spec. They are listed so the next M3-touching session can find them:

1. **TLS fingerprint mismatch dialog** — FR-005 records the fingerprint change to diagnostics; the user-facing modal that lets a user re-trust the new cert is unbuilt.
2. **Engine-side tray pairing pane** (spec 021 T065) — Revoke / Revoke all / Regenerate PIN actions in a Windows WPF tray context. Defers to a session with an interactive Windows desktop.
3. **In-flight WebSocket revocation** (spec 021 T066-partial) — drop open sockets the moment `BearerTokenStore.RevokeByHash` runs; the next handshake already rejects revoked bearers, so the gap is only the in-flight grace window.
4. **Multi-engine connections from a single browser** — PRD §10 explicitly defers this. The browser still holds one active connection at a time.
5. **Per-connection profiles** — every connection inherits the global formatting profile (PRD §10).
6. **Mobile browser pairing** — PRD §10 is silent on mobile; the layout assumptions in US4's tree component are desktop-first.

Each of these can land as a one-task addition to a future closure or as a standalone follow-up spec if telemetry or user demand surfaces them.
