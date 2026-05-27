# Implementation Plan: M3 — WebSocket Transport & Local-Agent Bridge Closure

**Branch**: `025-m3-bridge-closure` | **Date**: 2026-05-27 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/025-m3-bridge-closure/spec.md`

## Summary

Close the five genuinely-unmet items from the M3 PRD (`doc/WEB/M3-websocket-transport.md`) so the M3 Definition of Done can be retired against shipped evidence: (1) make the LAN-mode WebSocket binding actually serve `wss://` on the wire using the installer-produced cert binding **and wire the engine host to actually start a `WebSocketTransport` alongside the existing `NamedPipeTransport`** (spec 021 T058 deferred + engine-host composition gap surfaced during the plan-stage code audit; FR-027), (2) wire `BridgeState.Reconnecting` into an exponential-backoff loop with bearer-token replay (T068 follow-up), (3) render the cached Phase A / Phase B snapshots as a Database → Schema → Object-Kind tree in the editor sidebar (DoD §12 "renders tree"), (4) land `doc/m3-security.md` (threat model) + `doc/WEB/quickstart-m3.md` (firewall + pairing walkthrough) so the DoD docs row closes, and (5) add Playwright + xUnit E2E coverage on the wire (T078 + T079 deferred). Twenty-five of thirty Phase-4 tasks (T056–T080) are already merged; this closure is a focused, mostly-verification slice.

The new application surfaces are: a sibling `https://` prefix path on the existing `WebSocketTransport` (no new transport class), a config-driven composition addition in `EngineHost.RunAsync` so the bridge actually runs (`WebSocketTransport` exists but is currently never instantiated by the engine — see Research Decision 6), a reconnect loop inside the existing `EngineBridge`, and one new Razor component (`SchemaTreeComponent.razor`) that reads the already-shipped `ISchemaCacheStore`. Everything else is documentation or test code.

## Technical Context

**Language/Version**: C# 12 on .NET 10 (`net10.0`) for the engine, engine tests, and bridge E2E tests; `netstandard2.0 + net10.0` dual-target for the shared Core/IPC types; Blazor WebAssembly (`net10.0`) for `AkmlSql.Web` (already integrated by spec 021).
**Primary Dependencies**: `System.Net.HttpListener` + `System.Net.WebSockets` (BCL — already in use by the existing localhost-mode transport); `MessagePack` (already integrated for `RpcMessage` framing); xUnit + bUnit (already integrated by `tests/AkmlSql.Web.Tests/`); Playwright .NET (already integrated by `tests/AkmlSql.Web.E2E.Tests/` per spec 024). **No new package references** for the LAN-TLS path — see Research Decision 1.
**Storage**: No new persistence. The threat-model and quickstart artefacts are checked-in markdown under `doc/`. The schema tree renders entries already persisted in IndexedDB by `ISchemaCacheStore` (spec 021 T108); no new IndexedDB store name.
**Testing**: `dotnet test` (xUnit) for `tests/AkmlSql.Engine.Tests/Transports/WebSocketTransportTests.cs` (LAN HTTPS round-trip extension) and `tests/AkmlSql.Web.Tests/Bridge/` (reconnect-state machine, bUnit tree-component); `dotnet test --filter Category=BridgeE2E` for the two new E2E suites at `tests/AkmlSql.Web.E2E.Tests/UserStory2Tests.cs` and `tests/AkmlSql.E2E.Tests/BridgeHandshakeTests.cs`. Reuses existing `FakeBridgeWebSocket` loopback for unit-level coverage. No new test framework introduced.
**Target Platform**: Windows 11 + .NET 10 SDK for the engine and engine tests; Chromium (Playwright .NET default) for the browser-side E2E; Windows-only for `netsh http add sslcert` binding (engine is already `RuntimeIdentifier=win-x64`).
**Project Type**: Verification + plumbing slice over an already-merged Blazor WASM + engine bridge stack. No new csproj files; no new public IPC message types; the existing `MessageTypes.HandshakeRequest=200/201` envelope is unchanged.
**Performance Goals**: Pair-from-second-machine to `BridgeState.Open` ≤ 30 s end-to-end (PRD §9, SC-001); reconnect after engine restart ≤ 10 s (PRD §9, SC-002); schema tree first render ≤ 500 ms on a 2,000-table snapshot (FR-022, SC-005).
**Constraints**:

- Existing localhost-mode loopback transport MUST continue to work unchanged (FR-003) — no regression in the already-shipped Phase-4 happy path.
- Plaintext non-loopback binding MUST refuse at startup (FR-002, SC-007). The current construction-time refusal in `WebSocketTransport.cs:45-51` stays; the new HTTPS path adds a sibling check that the configured PFX exists on disk and its thumbprint matches the active `netsh http show sslcert` binding.
- The reconnect loop MUST NOT block the UI thread (FR-015) — runs via `Task.Run`/timer, never on the renderer thread.
- The schema tree MUST stay readable when offline (FR-020) — reads `ISchemaCacheStore` directly so a `Disconnected` bridge still renders the last snapshot.
- E2E tests MUST build engine + web from current source before launching (FR-025) — same stale-build discipline spec 024 set.

**Scale/Scope**: Five user stories; 26 functional requirements; the LAN-TLS change is a sibling listener prefix swap (~50 LOC delta on `WebSocketTransport`); the reconnect logic is one nested `Task.Run` loop inside `EngineBridge` (~80 LOC delta); the schema tree is one new Razor component (~250 LOC); two new markdown documents (~300 lines combined); two new test classes (~400 LOC combined including the fixture).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

No `.specify/memory/constitution.md` exists for this repository, so no constitution gates apply. The closure spec already constrains itself in three ways that serve as effective gates:

- **No new IPC message types.** The existing `HandshakeRequest`/`HandshakeResponse` (`MessageTypes` 200/201) plus the already-shipped completion / quick-info / signature-help / goto-definition / schema messages cover every wire interaction; no new envelope is introduced.
- **No new test framework.** Existing xUnit + bUnit + Playwright .NET stacks are extended; no new harness is introduced.
- **Existing localhost path untouched.** The HttpListener loopback code path stays byte-for-byte the same; the LAN HTTPS extension is purely additive (FR-003).

These three self-imposed gates are checked again in the Post-Design re-evaluation below.

## Project Structure

### Documentation (this feature)

```text
specs/025-m3-bridge-closure/
├── plan.md                                          # This file (/speckit.plan command output)
├── spec.md                                          # Already written by /speckit.specify
├── research.md                                      # Phase 0 output — five decisions, one per US
├── data-model.md                                    # Phase 1 output — five new conceptual entities
├── quickstart.md                                    # Phase 1 output — how to run all five user stories
├── contracts/                                       # Phase 1 output — four artefact contracts
│   ├── lan-https-binding-contract.md
│   ├── backoff-schedule-contract.md
│   ├── schema-tree-contract.md
│   └── bridge-e2e-harness-contract.md
├── checklists/
│   └── requirements.md                              # Created by /speckit.specify; all green
└── tasks.md                                         # Phase 2 output (/speckit.tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
├── AkmlSql.Engine/
│   ├── EngineHost.cs                                # ← extended to start WebSocketTransport when config requests it (US1 / FR-027)
│   └── Transports/
│       └── WebSocketTransport.cs                    # ← extended for `https://` prefix on non-loopback (US1 / FR-001..FR-003)
│
└── AkmlSql.Web/
    ├── Services/
    │   └── IEngineBridge.cs                         # ← reconnect loop wired into existing EngineBridge (US3 / FR-011..FR-016)
    └── Shared/
        └── SchemaTreeComponent.razor                # ← NEW; renders cached Phase A/B snapshots (US4 / FR-017..FR-022)

tests/
├── AkmlSql.Engine.Tests/
│   ├── EngineHostTests.cs                           # ← NEW; assert dual-transport composition shares the router (US1 / FR-027)
│   └── Transports/
│       └── WebSocketTransportTests.cs               # ← extend with LAN HTTPS round-trip + thumbprint mismatch refusal (US1 / FR-006)
├── AkmlSql.Web.Tests/
│   └── Bridge/
│       ├── ReconnectLoopTests.cs                    # ← NEW; FakeBridgeWebSocket-driven state machine tests (US3)
│       └── SchemaTreeComponentTests.cs              # ← NEW; bUnit assertions on tree shape + stale badge + click-to-insert (US4)
├── AkmlSql.Web.E2E.Tests/
│   ├── UserStory2Tests.cs                           # ← NEW; spec 021 US2 acceptance scenarios end-to-end (US5 / FR-023, FR-026)
│   └── Harness/
│       └── EngineLaunchFixture.cs                   # ← NEW; xUnit IAsyncLifetime: build + launch + ready-wait + teardown
└── AkmlSql.E2E.Tests/
    └── BridgeHandshakeTests.cs                      # ← NEW; pure-xUnit WSS pair → reconnect → revocation flow (US5 / FR-024)

doc/
├── m3-security.md                                   # ← NEW; threat model per PRD §8 + two added rows (US2 / FR-007, FR-009)
└── WEB/
    └── quickstart-m3.md                             # ← NEW; pair-from-second-machine walkthrough (US2 / FR-008, FR-010)
```

**Structure Decision**: Plumbing + verification slice over the already-merged Phase-4 stack. Three source-code touches (1 transport extension, 1 service-internal reconnect loop, 1 new Razor component), two checked-in markdown docs under `doc/`, four new test classes plus one xUnit fixture. All other artefacts (the engine handlers, the pairing service, the bearer-token store, the connection store, the pairing-token vault, the connection picker, every bridge-routed IntelliSense / quick-info / signature-help service, the schema sync, the diagnostics export, the installer cert generation + binding + firewall rule) are already shipped and are not retouched.

## Phase 0: Research

Five technical decisions drive the plan, one per user story. Captured in `research.md`. Summary:

1. **LAN-mode TLS termination** — keep `HttpListener` and switch to `https://` prefix on non-loopback bindings, consuming the cert already bound to the port by spec 021 T088 (`netsh http add sslcert`). The `WebSocketTransportOptions.TlsCertPath` value becomes a sanity-check input: the transport verifies the PFX exists and its thumbprint matches the active netsh binding, throwing on mismatch. Avoids adding the ~30 MB `Microsoft.AspNetCore.App` FrameworkReference to the engine.
2. **Reconnect schedule** — initial 500 ms, multiplier 2.0, cap 30 s, ±100 ms jitter. Honours PRD §7 cap; the 500 ms floor recovers near-instantly from sub-second blips; jitter prevents synchronised storms when several browsers reconnect against the same engine.
3. **Schema tree architecture** — `SchemaTreeComponent.razor` reads `ISchemaCacheStore` directly (not the bridge), watches `ISchemaSync.ChecksumDrifted` for refresh, virtualises children past a threshold (~200) via Blazor's built-in `<Virtualize>` component. Reading the cache (not the bridge) means a `Disconnected` bridge still renders the last snapshot, which FR-020 requires.
4. **E2E engine-launch fixture** — `EngineLaunchFixture : IAsyncLifetime` runs `dotnet build` then `dotnet run` against `src/AkmlSql.Engine/`, picks a free port, waits for the bridge to accept connections, and tears down the process on disposal. Same shape as spec 024's `DotnetRunFixture`. Reused by both `tests/AkmlSql.Web.E2E.Tests/UserStory2Tests.cs` (Playwright + engine) and `tests/AkmlSql.E2E.Tests/BridgeHandshakeTests.cs` (pure RPC + engine).
5. **Plaintext-LAN refusal** — keep the construction-time refusal in `WebSocketTransport.cs:45-51`; add a sibling check on the HTTPS path that asserts the PFX file exists on disk and its thumbprint matches `netsh http show sslcert ipport=...`. Mismatch throws with both thumbprints in the message so an operator can diagnose without re-running the installer.

6. **Engine-host composition (added during plan-stage audit)** — `EngineHost.RunAsync` currently only instantiates `NamedPipeTransport`. The `WebSocketTransport` class exists with handlers and tests, but no caller. The host MUST be extended to read a `Bridge` section from `config.json` and conditionally compose a `WebSocketTransport` alongside the named pipe; both share the same `RpcRouter` per PRD §11 open question 4. When the `Bridge` section is absent or disabled, behaviour is byte-for-byte identical to today's IDE-plugin-only deployment. The composition runs both transports' `StartAsync` and awaits both their `RequestReceived` events (already shipped as `IRpcTransport`).

`research.md` records each decision with rationale + alternatives.

## Phase 1: Design & Contracts

### Data model (`data-model.md`)

Five new conceptual entities — none persist anywhere new; the IndexedDB store names from spec 021 are reused unchanged. Recorded for cross-reference in tasks.md.

1. **BackoffSchedule** — deterministic interval generator for the reconnect loop.
2. **SchemaTreeNode** — one node in the rendered schema tree (Database / Schema / Object-Kind / Object / Column).
3. **ThreatModelEntry** — one row in `doc/m3-security.md`'s threat table.
4. **QuickstartStep** — one numbered step in `doc/WEB/quickstart-m3.md`.
5. **BridgeE2EFixtureState** — the build-and-launch state machine the E2E fixture exposes.

### Contracts (`contracts/`)

Four artefact contracts, one per user story that produces a non-trivial format or harness:

1. **`lan-https-binding-contract.md`** — engine startup sequence on a non-loopback binding: prefix selection, PFX existence check, thumbprint match against netsh binding, refusal messages, where errors surface in the activity log. Defines the FR-002 / FR-007 error-message contract.
2. **`backoff-schedule-contract.md`** — the retry interval sequence, jitter range, bearer-replay behaviour, status-bar surface format for "time until next retry". Defines the FR-011..FR-016 timing contract.
3. **`schema-tree-contract.md`** — node hierarchy (Database → Schema → Object-Kind → Object → Column), virtualisation threshold, expansion-state preservation across snapshot refresh, click-to-insert payload format (`[schema].[name]`), stale-indicator badge format. Defines FR-017..FR-022 rendering contract.
4. **`bridge-e2e-harness-contract.md`** — `EngineLaunchFixture` lifecycle (build → free-port pick → launch → readiness probe → teardown), the `[Trait("Category","BridgeE2E")]` opt-in convention, what each test asserts. Defines FR-023..FR-026 harness contract.

### Quickstart (`quickstart.md`)

A walkthrough developers run to land each user story:

- **US1**: extend `WebSocketTransport.cs` → add `WebSocketTransportTests` LAN round-trip → publish engine → smoke-test against a localhost LAN binding.
- **US2**: write `doc/m3-security.md` → write `doc/WEB/quickstart-m3.md` → cross-link from `doc/WEB/00-INDEX.md`.
- **US3**: add `Reconnecting` transition to `EngineBridge` receive loop → add backoff timer → add tests → manual smoke test (kill engine while bridge open, observe recovery).
- **US4**: write `SchemaTreeComponent.razor` → add bUnit tests → mount on `Editor.razor` sidebar → manual smoke test against a test DB.
- **US5**: write `EngineLaunchFixture` → write `BridgeHandshakeTests` → write `UserStory2Tests` → run `dotnet test --filter Category=BridgeE2E` → confirm green.

### Agent context

Run `.specify/scripts/powershell/update-agent-context.ps1 -AgentType claude` to refresh the agent context file with the new technology surfaces this closure adds (HTTPS-prefix `HttpListener` on the engine; reconnect loop in `EngineBridge`; `SchemaTreeComponent.razor`; `EngineLaunchFixture`).

## Phase 2 planning note

Tasks are generated by `/speckit.tasks`, not here. The tasks file will turn each user story into a sequence of concrete tasks: in US1 order, extend the transport's prefix logic → add PFX existence + thumbprint match check → wire the failing-case error message → add LAN HTTPS round-trip test; in US2, write the threat-model markdown → write the quickstart-m3 markdown → update the WEB index; in US3, add `Reconnecting` state transition → add backoff timer + jitter → add bearer replay on retry → assert revocation halts retries → bUnit tests on state-machine; in US4, write `SchemaTreeComponent` → write bUnit tests → place in Editor.razor sidebar; in US5, write `EngineLaunchFixture` → write `BridgeHandshakeTests` → write `UserStory2Tests` → wire the `BridgeE2E` trait + filter.

## Complexity Tracking

No constitution gate violations to justify (no constitution). The three self-imposed gates from the Constitution Check section all hold post-design:

- **No new IPC message types** — the LAN HTTPS work, the reconnect loop, the schema tree, and both E2E suites all use `MessageTypes.HandshakeRequest / Response` plus the already-shipped completion / signature-help / quick-info / goto-definition / schema messages.
- **No new test framework** — every new test file uses the existing xUnit / bUnit / Playwright .NET stack already configured in `tests/AkmlSql.Engine.Tests/`, `tests/AkmlSql.Web.Tests/`, `tests/AkmlSql.Web.E2E.Tests/`, and `tests/AkmlSql.E2E.Tests/`.
- **Existing localhost path untouched** — `WebSocketTransport.StartAsync` still binds `http://127.0.0.1:<port>/` for `IsLoopback`; only the non-loopback branch is new.

Every artefact listed in the Project Structure block is either a test file, a checked-in markdown document, a one-component addition, or a targeted extension of an existing class. No new persistence layer, no new IPC message type, no new public service interface. Closure spec discipline holds.
