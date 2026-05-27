# Research: M3 — WebSocket Transport & Local-Agent Bridge Closure

**Branch**: `025-m3-bridge-closure` | **Date**: 2026-05-27 | **Spec**: [spec.md](./spec.md)

Five technical decisions, one per user story. No open `NEEDS CLARIFICATION` items — the closure scope is well-defined and every choice below has a single defensible default given the spec 021 Phase 4 + Phase 5 work already on disk.

---

## Decision 1 — LAN-mode TLS termination strategy

**Decision**: Keep `HttpListener` and switch the URI prefix from `http://` to `https://` for non-loopback bindings. The TLS cert is already bound to the port by the installer's `netsh http add sslcert ipport=0.0.0.0:<port> certhash=<thumbprint>` step (spec 021 T088). The transport reads `WebSocketTransportOptions.TlsCertPath`, loads the PFX, computes its SHA-1 thumbprint, and verifies it matches the active netsh binding — throwing on mismatch.

**Rationale**:

1. **Reuses an already-shipped binding.** Spec 021 T087 (cert generation) + T088 (netsh binding) already produce the exact artefact `HttpListener https://` expects. The Kestrel path would ignore the netsh binding and consume the PFX directly — duplicating cert-management logic the installer already covers.
2. **No new package reference.** The engine project's csproj already pulls in `System.Net.HttpListener` + `System.Net.WebSockets` via the BCL. Kestrel would require `<FrameworkReference Include="Microsoft.AspNetCore.App" />` which adds ~30 MB to the self-contained `win-x64` engine deployment.
3. **Matches the existing localhost code path.** `WebSocketTransport.ServeAsync` already drives `WebSocket` framing on top of `WebSocketContext` from `HttpListener.AcceptWebSocketAsync`. Switching the prefix changes one line; everything downstream of the upgrade is unchanged.
4. **The thumbprint sanity check is cheap and catches the realistic failure mode.** If an operator re-runs the installer but the engine still points at an old `TlsCertPath`, the mismatch fires at startup with both thumbprints in the error message — they don't have to grep through `netsh http show sslcert` output to diagnose.

**Alternatives considered**:

- **Kestrel HTTPS** (advisor's initial framing): Rejected. Adds ~30 MB to the engine deployment for no functional gain — every cert-management capability Kestrel offers (PFX load, password-via-env-var, hot reload) is either already covered by the installer or out of scope here. Also adds a Kestrel host-builder boot path the engine doesn't otherwise need.
- **`SslStream` over a raw `TcpListener`**: Rejected. We'd reimplement HTTP upgrade + WebSocket framing under TLS by hand. The whole point of `HttpListener` for the localhost path is that the WinHTTP layer handles upgrade + TLS; throwing that away for symmetry-with-Kestrel reasoning is the long way round.
- **Drop the `TlsCertPath` field entirely on the HTTPS path** and rely solely on the netsh binding: Rejected. The PFX path is what `config.json` already records; using it as a sanity-check input is cheap defensive coding and makes operator diagnostics far better than "the netsh binding is wrong, somewhere."

**Consumer**: US1 / FR-001 / FR-002 / FR-003 / FR-006.

---

## Decision 2 — Reconnect schedule constants

**Decision**: Exponential back-off starting at 500 ms with multiplier 2.0, capped at 30 s, with ±100 ms uniform jitter applied to each interval. Bearer token is replayed on every retry; if a retry returns `HandshakeStatus.PinRequired` the loop transitions to `Failed` and stops.

Concrete sequence (mean values, before jitter):

```
attempt:  1     2     3     4     5     6     7+
delay:    500ms 1.0s  2.0s  4.0s  8.0s  16.0s 30.0s (capped)
```

**Rationale**:

1. **500 ms initial recovers near-instantly from sub-second blips.** A momentary network hiccup or a single dropped TCP packet shouldn't cost the user a multi-second pause. PRD §9 says "Reconnect after engine restart succeeds within 10 seconds" — the 500 ms floor + the engine's ~3 s cold start fits inside that budget on the first retry.
2. **2.0x multiplier + 30 s cap matches PRD §7.** The "Reconnect storm" mitigation specifies "max retry interval 30s"; doubling from 500 ms reaches the cap in 7 attempts (~62 s of total back-off across the sequence), which is fast enough for a transient outage and slow enough not to hammer a permanently-down engine.
3. **±100 ms jitter prevents synchronised storms.** When several browsers on the same LAN reconnect against the same engine at the same wall-clock moment (e.g., after a Windows update reboot), uniform deltas would queue every handshake against the same engine startup instant. Jitter spreads them and matches the standard `exponential-backoff-with-jitter` pattern (AWS Architecture Blog, "Exponential Backoff And Jitter").
4. **Bearer-replay on every retry is correct.** The bearer token was minted by the handshake; the engine's `HandshakeHandler` already validates bearers against `BearerTokenStore` and returns `PinRequired` for revoked tokens. The retry loop just resubmits the same `HandshakeRequest`.

**Alternatives considered**:

- **1 s initial delay**: Rejected. Too slow for sub-second blips; the user would see a full second of `Disconnected` for a momentary blip when 500 ms recovers without them noticing.
- **No jitter**: Rejected. Multi-browser sync storms are realistic in office deployments where everyone wakes from screen lock at 9 AM.
- **Knob-driven values** (read from `config.json` per connection): Rejected. The constants serve every realistic case; making them tunable is over-engineering for a closure spec. If telemetry later shows they're wrong, that's a follow-up.
- **Decorrelated jitter** (Amazon's "Full Jitter" variant): Considered. The uniform `±100 ms` is simpler and the storm-spreading benefit is already captured at this scale (one engine, a handful of browsers); decorrelated jitter pays off at higher fan-outs.

**Consumer**: US3 / FR-011 / FR-012 / FR-013 / FR-014 / FR-016.

---

## Decision 3 — Schema tree component architecture

**Decision**: `SchemaTreeComponent.razor` reads the active `ISchemaCacheStore` snapshot directly (not via the bridge). It subscribes to `ISchemaSync.ChecksumDrifted` for refresh signals and re-renders the tree on a new snapshot while preserving the user's `HashSet<string>` of currently-expanded node paths. Children past a threshold of 200 are virtualised with Blazor's built-in `<Virtualize>` component. Click-to-insert raises an event the editor page subscribes to; the payload is the bracketed qualifier (e.g., `[dbo].[Customer]`).

**Rationale**:

1. **Reading the cache, not the bridge, makes offline rendering free.** FR-020 requires the tree to render from a cached snapshot in `Disconnected`/`Reconnecting` states; if the component talked to the bridge, that requirement would mean a fallback code path. Reading the cache directly means the same code path handles online and offline equally — the "stale" badge is just `bridge.State != Open`.
2. **`<Virtualize>` is the built-in Blazor answer.** It renders only the rows in view; a 2,000-table snapshot fits comfortably under FR-022's "no jank" bar. The 200-children threshold is the inflection point where virtualisation overhead becomes worth the win — under 200, plain `@foreach` is faster.
3. **Expansion-state preservation via path-set survives snapshot version changes.** When a refresh fires, the new snapshot is replaced into the cache, but the tree keeps the same path keys (`Database/Schema/Tables/Customer`). A `HashSet<string>` of expanded paths is the simplest correctness-preserving primitive — no need to diff old vs new snapshots.
4. **The click-to-insert event keeps the tree decoupled from the editor.** The component raises an `EventCallback<string>` with the bracketed qualifier; `Editor.razor` subscribes and inserts at the caret via its existing CodeMirror JS interop. The tree component itself stays editor-agnostic and is reusable in any future place we want a schema picker.

**Alternatives considered**:

- **Bridge-routed tree (live RPC per expand)**: Rejected. The cache already has Phase A *and* Phase B; round-tripping to the engine on every expand wastes a few ms and breaks offline rendering.
- **External tree library (MudBlazor's `MudTreeView`)**: Rejected. We'd add a new Razor-component-library dependency for a single 250-LOC component. MudBlazor's theming would also fight `theme-tokens.json` (spec 016/021 token system).
- **Tree-renders-only-Phase-A and clicks Phase-B**: Rejected. Phase B persists into the same `SchemaSnapshot` record; reading it costs zero extra round-trips. The Phase B fetch is already wired by `ISchemaSync.FetchPhaseBAsync` after a checksum drift, so there's no reason to gate column display behind a click.
- **Persist expansion-state across sessions** (write the `HashSet<string>` to IndexedDB): Considered. Adds a new IndexedDB record. Defer to a follow-up if users ask for it; the closure spec keeps the in-memory scope.

**Consumer**: US4 / FR-017 / FR-018 / FR-019 / FR-020 / FR-021 / FR-022.

---

## Decision 4 — E2E engine-launch fixture

**Decision**: An `EngineLaunchFixture : IAsyncLifetime` under `tests/AkmlSql.Web.E2E.Tests/Harness/` that runs `dotnet build src/AkmlSql.Engine -c Release` first; aborts the test run if the build fails; picks a free TCP port via `TcpListener(IPAddress.Loopback, 0)`; launches the engine as a child process with `--bridge-port=<picked>` and `--bridge-mode=localhost`; polls the bridge port until it accepts a `ws://` connection or 30 s elapse; tears down the engine process on disposal. Both `UserStory2Tests` (Playwright + engine) and `BridgeHandshakeTests` (pure RPC + engine) consume the same fixture via xUnit's `IClassFixture<>` mechanism.

**Rationale**:

1. **Same shape as spec 024's `DotnetRunFixture`.** That fixture established the pattern (build then launch then ready-probe then teardown); reusing it here keeps the test conventions consistent across the project. New contributors learn one harness pattern, not two.
2. **Free-port picking via `TcpListener(0)` is the standard trick.** Hard-coding the port would race against the developer's running engine or another concurrent test run. The picked port lives in the fixture; both `UserStory2Tests` and `BridgeHandshakeTests` read it.
3. **Build-before-launch eliminates the stale-build false-positive.** A passing test against a stale `dotnet run` is worthless; FR-025 requires building from source.
4. **`IAsyncLifetime` (not `IDisposable`) supports the async readiness probe.** xUnit's standard async fixture protocol cleanly handles the "wait for the engine to accept connections" step.

**Alternatives considered**:

- **Reuse a pre-installed engine** (run against `%ProgramFiles%/AKML SQL/Engine/AkmlSql.Engine.exe`): Rejected. Couples the test run to whatever happened to be installed, defeats FR-025 stale-build discipline, and doesn't survive a `git pull`.
- **In-process `EngineComposition` (no child process)**: Rejected. The whole point of these E2E tests is the wire path. In-process testing already lives at `tests/AkmlSql.Web.Tests/Bridge/HandshakeClientTests.cs` (the `FakeBridgeWebSocket` loopback) and covers the protocol logic — adding more in-process tests adds no coverage; the wire is what's untested today.
- **Playwright's built-in `webServer` config**: Rejected. Playwright .NET doesn't have the Node-style `playwright.config.ts` `webServer` field; we'd reimplement it. Sticking with the xUnit fixture is cleaner.
- **Share one engine across both test classes via xUnit `[Collection]`**: Considered. Adds complexity (a `CollectionDefinition` plus a `[Collection(...)]` attribute on each test class) and the cold-launch cost is small (~3 s). Leave each class with its own fixture; each test class is a few-scenario file, not a hundred-test suite.

**Consumer**: US5 / FR-023 / FR-024 / FR-025 / FR-026.

---

## Decision 5 — Plaintext-LAN refusal location

**Decision**: Keep the existing construction-time refusal in `WebSocketTransport.cs:45-51` (loops on the existing `TlsCertPath` requirement for non-loopback). On the new HTTPS code path, add a sibling check at `StartAsync` time that asserts the PFX file exists on disk and its SHA-1 thumbprint matches the active `netsh http show sslcert ipport=0.0.0.0:<port>` binding. Mismatch throws an `InvalidOperationException` whose message names both thumbprints (PFX-derived vs netsh-bound) so the operator can pick the right fix without running `netsh` themselves.

**Rationale**:

1. **Two refusal sites match the two failure modes.** Construction-time catches misconfigured `config.json` (binding non-loopback with empty `TlsCertPath`); startup-time catches cert-binding drift (the PFX is fine but `netsh` was never run, or a re-install regenerated the cert but the netsh binding wasn't refreshed). Both messages reference `TlsCertPath` and FR-013a so operators land at the same diagnostic doc.
2. **Startup-time is where the listener actually opens.** Construction-time can't probe `netsh` because the port isn't open yet; doing it at `StartAsync` is the natural place.
3. **Naming both thumbprints saves an operator step.** Without the thumbprint dump, the operator has to run `netsh http show sslcert ipport=0.0.0.0:<port>` and compare to `Get-PfxCertificate`. With it, the error message is the diagnostic.

**Alternatives considered**:

- **Refuse only at construction time** (move the netsh check into the `WebSocketTransportOptions` constructor): Rejected. Construction time happens before the port is open; we can't legitimately call `netsh http show sslcert` against a port no one is listening on yet.
- **Refuse only at startup time** (drop the existing construction-time refusal): Rejected. Construction-time fast-fail catches misconfigured `config.json` before the engine wastes any setup; keeping it is cheap.
- **Silently log a warning on thumbprint mismatch instead of throwing**: Rejected. Silent insecure path: if the netsh binding points at a stale cert, the operator might not notice, the engine still serves, and pairing succeeds against a cert the user thinks they're trusting. Throw, with a clear message.
- **Let the runtime emit the WinHTTP TLS-cert-mismatch error on first connect**: Rejected. The error surfaces in the browser's network stack, not the engine log; debuggability is bad.

**Consumer**: US1 / FR-002 / FR-005 (paired with the cert-fingerprint diagnostic log entry).

---

## Decision 6 — Engine-host composition for dual transports

**Decision**: `EngineHost.RunAsync` reads a new `"Bridge"` section from `%AppData%/AKML SQL Web/config.json`; when present and `Enabled=true`, it constructs a `WebSocketTransport` from the section's `WebSocketTransportOptions` and runs it alongside the existing `NamedPipeTransport`. Both transports' `RequestReceived` events forward to the same `RpcRouter` and `RpcContext` instance from `EngineComposition.Build()`. When the section is absent or `Enabled=false`, only the named pipe runs — the IDE-plugin-only deployment is unchanged.

**Rationale**:

1. **Resolves the plan-stage audit finding.** `WebSocketTransport` exists as a class with handlers and 5 tests (spec 021 T056 / T057 / T059), but `EngineHost.RunAsync:97` only constructs `NamedPipeTransport`. Without engine-side composition, every browser-side handshake fails with a connection-refused. US1 cannot be demonstrated without this gap closed.
2. **Single-router design matches PRD §11 open question 4.** The PRD answers "yes, both pipe + WebSocket simultaneously" — the same engine instance serves the SSMS plugin and the browser. Sharing one `RpcRouter` + one `RpcContext` is the simplest way to honour that without divergent handler chains, and it matches spec 022's M0 closure design (`EngineComposition.Build()` returns one router; the transport is the only swappable layer).
3. **Config-driven over CLI-driven matches PRD §4.1.** The PRD says "Mode is chosen at engine startup based on `config.json`; the installer (M4) writes this setting." CLI flags would force the installer to know the engine's full arg vocabulary; a config section is what the M4 installer (T091) is already writing.
4. **The empty/disabled case is the no-op default.** Existing IDE-plugin-only installs have no `Bridge` section in their `config.json` (it didn't exist before this spec). They get the named-pipe-only behaviour they already have today. Zero regression risk for the most common deployment.

**Config schema** (added to `%AppData%/AKML SQL Web/config.json`):

```json
{
  "Bridge": {
    "Enabled": true,
    "BindAddress": "127.0.0.1",
    "Port": 47291,
    "TlsCertPath": "C:\\ProgramData\\AKML SQL Web\\certs\\bridge.pfx",
    "TlsCertPasswordRef": "AKMLSQL_BRIDGE_PFX_PASSWORD",
    "TokenStorePath": "C:\\ProgramData\\AKML SQL Web\\tokens.json",
    "TokenTtlDays": 90
  }
}
```

The fields map 1:1 onto `WebSocketTransportOptions` (already shipped by spec 021 T057). The composition code reads the section with `System.Text.Json` (already a transitive dep), constructs the options, hands them to the transport.

**Alternatives considered**:

- **CLI args `--bridge-port` and `--bridge-mode`**: Considered. Rejected because the PRD already specifies config-driven, and CLI args would force the M4 installer (T091) to write specific args into the `sc.exe create` binPath. Config-driven means the installer writes one JSON file and the engine reads it on every start.
- **A separate `WebEngineHost.RunAsync`**: Rejected. Two host entry points means two engine processes — but spec 021's design is one engine serving both surfaces simultaneously.
- **Composition root reads transport list at `EngineComposition.Build()`**: Rejected. `EngineComposition` is the service composition (router + context + handlers); transport composition is an orthogonal concern that belongs in `EngineHost`, which already owns transport lifecycle. Keeping these separate is the spec-022 design.

**Consumer**: US1 / FR-027 / SC-009.

---

## Open follow-ups (out of scope for this spec)

Same items the spec itself lists as deferred — recorded here so research-stage decisions don't quietly include them:

- **TLS fingerprint mismatch dialog** (browser-side modal that lets a user re-trust a new cert). FR-005 records the fingerprint change to diagnostics; the UI is a follow-up.
- **Engine-side tray pairing pane** (spec 021 T065). Out of scope.
- **In-flight WebSocket revocation** (spec 021 T066-partial). Out of scope; the *next* handshake already rejects revoked bearers, this closure does not close the in-flight grace window.

---

## Verified against current source

Each decision was checked against the actual current state of the codebase, not the M3 PRD's "current state" paragraph (which is stale-greenfield):

| Decision | Checked file / fact | Result |
|----------|---------------------|--------|
| 1 — HTTPS prefix path | `src/AkmlSql.Engine/Transports/WebSocketTransport.cs:32-91` uses `HttpListener` with `http://` prefix; netsh binding from `web-tls-setup.ps1` already exists | ✓ |
| 2 — Reconnect schedule | `src/AkmlSql.Web/Services/IEngineBridge.cs:57` defines `BridgeState.Reconnecting` (enum value present but never set today) | ✓ |
| 3 — Schema tree from cache | `src/AkmlSql.Web/Services/ISchemaSync.cs:1-291` correctly persists Phase A + Phase B into `ISchemaCacheStore`; no rendering component exists | ✓ |
| 4 — Engine launch fixture | `tests/AkmlSql.Web.E2E.Tests/` already exists (spec 024 fixture); no engine-side fixture yet | ✓ |
| 5 — Refusal sites | `WebSocketTransport.cs:45-51` enforces the existing FR-013a refusal | ✓ |
| 6 — Engine-host composition gap | `src/AkmlSql.Engine/EngineHost.cs:97` only constructs `NamedPipeTransport`; `WebSocketTransport` has no caller anywhere in `src/AkmlSql.Engine/`. Verified via `grep WebSocketTransport src/AkmlSql.Engine` returning only the class file itself and `IRpcTransport.cs:12` comment — no instantiation site | ✓ (gap surfaced) |
