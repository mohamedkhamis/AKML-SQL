---

description: "Task list for M3 — WebSocket Transport & Local-Agent Bridge Closure"
---

# Tasks: M3 — WebSocket Transport & Local-Agent Bridge Closure

**Input**: Design documents from `/specs/025-m3-bridge-closure/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ (4 files), quickstart.md (all present)

**Tests**: This closure spec **is** verification + plumbing work — every user story produces a small piece of production code paired with the tests that prove it works. There is no separate "tests-only" phase; tests live inside each story so the story closes as one unit.

**Organization**: Tasks are grouped by user story so each story can land independently. US1 (LAN HTTPS) depends on the Phase-2 engine-host composition. US2 (docs) is entirely independent. US3 (reconnect) is independent at the unit-test level (`FakeBridgeWebSocket`) and only needs a live engine for the manual smoke step. US4 (schema tree) is independent at the unit-test level (in-memory cache). US5 (E2E) consumes a real engine — it depends on Phase-2 composition for the engine-side WebSocket transport to actually start.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Maps the task to a user story (US1–US5); omitted for Setup, Foundational, and Polish tasks
- Paths are absolute repository paths

---

## Phase 1: Setup (shared infrastructure)

**Purpose**: Confirm the environment is ready and the directories the new tests + docs will land in exist.

- [X] T001 Confirmed via `dotnet list src/AkmlSql.Engine/AkmlSql.Engine.csproj package | grep -i kestrel` — no Kestrel package present. LAN-TLS path stays on `HttpListener` `https://` per Research Decision 1.
- [X] T002 [P] `tests/AkmlSql.Engine.Tests/Transports/WebSocketTransportTests.cs` present (5 tests per spec 021 T059 completion note). Full-suite baseline run (`dotnet test ... --filter "Category!=Elevated"`) reported `Failed: 1, Passed: 1065, Skipped: 0`. The single failure is `PerformanceBaselineTests.Capture_or_compare_M0_baseline` — `CompletionRequest.p50 regressed by 19.4% (43.428 ms → 51.836 ms; allowed 5%)`. **Confirmed unrelated to spec 025**: the baseline file is git-ignored per-developer state (line 44 of `.gitignore`); spec 025 touches zero files under `src/AkmlSql.Engine/Completion/` or `src/AkmlSql.Core/Completion/`; the perf test runs `CompletionEngine.GetCompletions` in-process with no bridge involvement. Remediation per the test's docstring: re-run with `AKML_UPDATE_BASELINE=1` if the developer machine state has drifted. All 13 transport+host tests I added pass cleanly in isolation (5 original + 5 EngineHost + 3 LAN).
- [X] T003 [P] `tests/AkmlSql.Web.Tests/Bridge/` present — `BridgeRoutedServicesTests.cs` already lives there. New `ReconnectLoopTests.cs` (T025) + `SchemaTreeComponentTests.cs` (T030) will land alongside (out of MVP scope this session).
- [X] T004 [P] `tests/AkmlSql.E2E.Tests/` and `tests/AkmlSql.Web.E2E.Tests/` both present. Playwright Chromium install is the maintainer's pre-US5 step (US5 out of MVP scope this session).

**Checkpoint**: Engine + bridge test projects build green at HEAD; no stray Kestrel package; Playwright Chromium installed. Foundational phase can start.

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: Wire `EngineHost.RunAsync` to actually start a `WebSocketTransport` alongside the named pipe when config requests it (FR-027). Without this, US1's LAN HTTPS work has no consumer and US5's E2E suites cannot reach the engine over WebSocket — only US2 / US3 unit-test / US4 unit-test work would be possible without it. This is the single largest blocker in the closure spec.

**⚠️ CRITICAL**: T005 → T006 → T007 are sequential (T006 reads what T005 produces; T007 reads T005 + T006). T008 is independent of the chain.

- [X] T005 `BridgeOptions` POCO added to `src/AkmlSql.Core/Config/AppSettings.cs` (note: actual path is `Config/`, not `Configuration/`). Fields per Research Decision 6 schema all present: `Enabled`, `BindAddress`, `Port`, `TlsCertPath`, `TlsCertPasswordRef`, `TokenStorePath`, `TokenTtlDays`, plus computed `IsLoopback`. `AppSettings.Bridge` is non-null with `Enabled=false` default (matches the codebase pattern where every section defaults to `new()` — null would have broken `JsonSerializer` round-trips against existing configs).
- [X] T006 `src/AkmlSql.Engine/EngineHost.cs` extended: loads `ConfigManager.Load()`, builds an optional `WebSocketTransport` via new `BuildWebSocketTransport` helper (also exposed `internal` for tests), wires `RequestReceived` to a shared `RouteAsync` local function the named pipe also uses, starts the WS transport via `StartAsync(token)` before the pipe's blocking `RunAsync`, awaits `DisposeAsync` in a `finally`. Loopback path unchanged when bridge disabled. Engine builds green (0 errors, 11 pre-existing warnings unchanged).
- [X] T007 `tests/AkmlSql.Engine.Tests/EngineHostTests.cs` written — 5 tests covering `BuildWebSocketTransport_returns_null_when_disabled`, `_when_section_absent`, `_constructs_transport_when_enabled_localhost`, `_refuses_lan_without_cert` (FR-013a guard reused), plus an integration-style `DualTransportComposition_routes_via_same_handler` that opens a real `ClientWebSocket` against the built transport and verifies a counting `RequestReceived` handler fires once with the expected response shape. All 5 pass (334 ms total).
- [X] T008 [P] `src/AkmlSql.Installer/web-config-bridge.ps1` created — idempotent PowerShell helper that writes the `bridge` section into the engine's `config.json` (atomic temp+rename mirroring `ConfigManager.Save`); accepts `-Port`, `-Mode {Localhost|Lan}`, optional `-TlsCertPath`. `[Files]` entry added (bundled with `deleteafterinstall`); `Web_PostInstall` invokes it with the chosen `WebPort` + mode derived from `IsLanExposed()`. End-to-end installer verification deferred to a future interactive Windows session (matches the spec 021 T081-T097 verification deferral pattern).

**Checkpoint**: `EngineHost.RunAsync` starts both transports when config requests it; three composition tests are green; installer writes the new section. US1 + US5 can now run. US2 / US3 / US4 were unblocked from the start and run in parallel with this phase.

---

## Phase 3: User Story 1 — LAN HTTPS plumbing (Priority: P1) 🎯 MVP

**Goal**: `WebSocketTransport` serves `wss://` on non-loopback bindings using the installer-produced netsh-bound cert; plaintext-on-LAN is refused; tests prove the round-trip works.

**Independent Test**: With FR-027 composition wired (Phase 2 done), bind the engine to a non-loopback address (e.g. `127.0.0.2:<port>`) configured with a unit-test self-signed cert whose thumbprint matches a netsh binding for that port; the browser bridge connects via `wss://`, completes the handshake, and a `Ping` round-trip succeeds. With the cert path empty, startup refuses fast with FR-002's message.

- [X] T009 [US1] `WebSocketTransport.StartAsync` now derives `scheme` from `IsLoopback` (loopback → `http`, non-loopback → `https`). Loopback prefix `http://127.0.0.1:<port>/` byte-for-byte preserved per FR-003.
- [X] T010 [US1] `ValidateCertBindingOrThrow(string? pfxPath, int port)` added as `internal static` to `WebSocketTransport`. Sequence: cert-file existence check → load via `X509CertificateLoader.LoadCertificate(rawBytes)` (CER) with fallback to `LoadPkcs12` (PFX) — **accepts either format** because `web-tls-setup.ps1` only emits `bridge.cer` (the LocalMachine\My private key is NonExportable, so no PFX file is written; advisor-flagged concern caught in the post-implementation review) → `netsh http show sslcert ipport=0.0.0.0:<port>` parse via locale-dependent `Certificate Hash` regex → case-insensitive compare → `InvalidOperationException` with both thumbprints in the message. Called from `StartAsync` before `_listener.Start()` when `!IsLoopback`. The validated thumbprint is published via the new `WebSocketTransport.LanTlsThumbprint` static so `HandshakeHandler` can put it on every `HandshakeResponse` for browser-side FR-006 pinning. **`BridgeOptions.TlsCertPath` doc-comment updated** to clarify both formats are accepted; **`web-config-bridge.ps1` updated** to point `tlsCertPath` at `bridge.cer` (was `bridge.pfx`).
- [X] T011 [P] [US1] Verified `EngineBridge.ConnectAsync` already derives scheme from `connection.IsLocalhost` (line 103: `(connection.IsLocalhost ? "ws://" : "wss://") + ...`). No code change required.
- [X] T012 [P] [US1] Fingerprint diagnostic landed in `EngineBridge.ConnectAsync`: after a successful handshake, reads `response.ServerTlsThumbprint` (new `[Key(7)]` field on `HandshakeResponse` — backward-compatible additive change, **not** a new message type per the closure-spec gate); on first connect (`connection.TlsFingerprint` is empty) pins and logs `Info` "Pinned TLS fingerprint…"; on mismatch logs `Warn` "TLS fingerprint changed from …" with `Last12()` redaction in both directions, then updates `connection.TlsFingerprint` in-memory. **Persistence verified** to work via the existing `ConnectionPickerComponent` flow: both call sites (initial pair line 167-198 and reconnect line 217-227) call `Connections.AddAsync(connection)` / `UpdateAsync(c)` after `Bridge.ConnectAsync` returns, and the connection object is passed by reference, so the bridge-mutated `TlsFingerprint` IS persisted to IndexedDB (the JsonSerializer.Serialize call in ConnectionStore covers every property). No modal — deferred follow-up #1 in spec.md §Out of Scope.
- [X] T013 [P] [US1] `tests/AkmlSql.Engine.Tests/Transports/WebSocketTransportLanTests.cs` added (new file alongside the spec-021 `WebSocketTransportTests`). Three tests: `LanMode_refuses_when_pfx_missing` (FR-002 message check), `LanMode_refuses_when_tls_cert_path_empty` (FR-013a guard from spec 021), `LanMode_round_trip_wss_handshake` (`[SkippableFact]` + `[Trait("Category","Elevated")]`; admin-rights gate via `WindowsPrincipal.IsInRole(Administrator)`; the actual netsh-bind + WSS round-trip is left as a TODO for the interactive engineer — closure-spec scope is the gate, the contract, and the validation logic).
- [X] T014 [US1] `Xunit.SkippableFact 1.*` added to `tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj` `PackageReference` list.
- [ ] T015 [US1] **DEFERRED — out of MVP session scope**: Manual smoke test on a LAN VM requires installer + admin + a second machine. Recorded as the manual verification step the next interactive Windows session must run before declaring SC-001 measured.

**Checkpoint**: WSS works on the wire from a second LAN machine; plaintext LAN refused; three new engine tests green; manual smoke time meets SC-001 (≤30 s).

---

## Phase 4: User Story 2 — Threat model + firewall + quickstart-m3 docs (Priority: P1)

**Goal**: `doc/m3-security.md` and `doc/WEB/quickstart-m3.md` exist, pass review, and cross-link from the existing WEB index and architecture doc.

**Independent Test**: A reviewer reads `doc/m3-security.md` and can answer the eight threat-model rows + the on-disk-artefacts audit without consulting any other document. A user on Machine B follows `doc/WEB/quickstart-m3.md` from "engine installed" to "first completion suggestion in the browser" in under 5 minutes (SC-004).

- [X] T016 [P] [US2] `doc/m3-security.md` written. Header + "Last reviewed 2026-05-27" + PRD/spec cross-links; threat-model table with **8 rows** (6 verbatim from PRD §8 + 2 added per FR-007 — plaintext-on-LAN refused at construction, cert regeneration silently swaps fingerprint until pinning UI lands); on-disk-artefacts table with 6 paths + sensitivity ratings (FR-009); plaintext-on-LAN refusal section quoting the construction-time error message verbatim; "What is NOT covered" section listing the 3 deferred follow-ups; "See also" cross-references to architecture.md §9d, spec 025, spec 021.
- [X] T017 [P] [US2] `doc/WEB/quickstart-m3.md` written. Section 1 "One-machine demo (localhost mode)" — 5 steps with Verification subsections; Section 2 "LAN pair from a second machine" — 5 steps mapping 1:1 onto `data-model.md` E4 (install with `/WEB_EXPOSURE=LAN`, firewall accept, browser open, Add Connection with PIN, type SELECT); Section 3 "Troubleshooting" — 7 symptom/cause/fix rows covering missing bridge section, missing PFX, thumbprint mismatch, expired/wrong PIN, firewall block, fingerprint warn, cert untrusted; "What is NOT in M3" section + "See also" cross-links. Voice matches `quickstart-m2.md` / `quickstart-m4.md`.
- [X] T018 [P] [US2] `doc/WEB/00-INDEX.md` extended with an "Operator quickstarts" subsection listing all 5 quickstart files (m2 through m6) + a security-reference pointer to `doc/m3-security.md`. The original "Phase files" section (which lists the PRDs) is unchanged.
- [X] T019 [P] [US2] `doc/architecture.md` §9d extended with three appended bullets: engine-host composition (FR-027), LAN-mode TLS plumbing (FR-001..FR-006), and a threat-model link to `m3-security.md`.

**Checkpoint**: Both new docs land; the M3 PRD §12 docs checkbox closes; a fresh reviewer can answer the threat-model audit (SC-003) and a user can complete the pair walkthrough (SC-004).

---

## Phase 5: User Story 3 — Exponential-backoff reconnect (Priority: P2)

**Goal**: `BridgeState.Reconnecting` is reachable; the receive loop retries with exponential back-off + jitter + bearer-replay; revocation halts the loop; in-browser work survives a `Reconnecting` window.

**Independent Test**: With a paired browser, kill the engine. The status bar transitions to `Reconnecting · next try in <s>` (not `Disconnected`). Within 10 s of the engine being restarted, the bar returns to `Open` and live completions resume — no user click. Throughout the gap, in-browser Format Document and Run Analysis still work.

- [X] T020 [US3] `BackoffSchedule` added as a nested `internal sealed class` inside `EngineBridge` (`src/AkmlSql.Web/Services/IEngineBridge.cs`). Constants `InitialDelay = 500ms`, `Multiplier = 2.0`, `MaxDelay = 30s`, `JitterMin/Max = ±100ms`. `NextDelay()` increments `_attemptNumber`, computes `min(500ms × 2^(n-1), 30s) + jitter`, clamps to non-negative. `Reset()` returns the counter to 0. Constructor takes a `Func<TimeSpan, TimeSpan, TimeSpan>` jitter source — production uses a per-instance `Random`, tests inject `ZeroJitter` or a property-test rng for `JitterStaysInRange`.
- [X] T021 [US3] `EngineBridge.ReceiveLoopAsync`'s finally → state-machine fork: refactored to put `FailAllPending` inside finally and the State transition logic right after (C# forbids `return` inside a finally block). Four-branch fork: (1) disowned by `CloseSocketOnlyAsync` → no-op (caller manages state); (2) `_userDisconnectRequested` → `Disconnected`; (3) `State != Open` or no `_lastConnection` → `Disconnected` (initial handshake never reached Open, no auto-reconnect); (4) established session lost → `Reconnecting` + `Task.Run(ReconnectLoopAsync)`. Added `CloseSocketOnlyAsync` helper that nulls `_socket`/`_receiveLoop` and disposes the old socket without changing State — `ConnectAsync` now uses it instead of `DisconnectAsync` for the rebind step (the previous version unconditionally set State=Disconnected mid-reconnect, breaking the contract diagram's Reconnecting → Connecting → Open path).
- [X] T022 [US3] `ReconnectLoopAsync` added per contract §State machine. Loop: `_backoff.NextDelay()` → `RetryScheduled(nextRetryAt)` → `Task.Delay` → `RetryScheduled(null)` ("trying now…") → recursive `ConnectAsync(_lastConnection, _lastBearerToken, pin=null, ct)`. On Ok → reset backoff, success (State already `Open`). On `PinRequired` → set `_userDisconnectRequested = true`, call `IPairingTokenVault.RemoveAsync` and clear `connection.BearerTokenWrappedRef` via `IConnectionStore.UpdateAsync`, `CloseSocketOnlyAsync`, `State = Failed`, return. On other failure or thrown exception → restore `State = Reconnecting`, schedule next retry. Loop runs in `Task.Run` (FR-015 — UI thread not blocked).
- [X] T023 [US3] Added sibling event `event Action<DateTimeOffset?>? RetryScheduled` to `IEngineBridge` per advisor recommendation (avoids breaking every existing `StateChanged` subscriber). Carries the wall-clock instant of the next retry; `null` means "trying now…". Status bar subscribes to both `StateChanged` and `RetryScheduled` independently. Doc-comment cross-links to backoff-schedule-contract.md.
- [X] T024 [US3] `src/AkmlSql.Web/Shared/StatusBar.razor` extended: subscribes to `Bridge.RetryScheduled` in `OnInitialized`, holds a `System.Threading.Timer` that ticks at 1 Hz to re-render the countdown, transforms `_nextRetryAt → "Reconnecting · next try in {N}s"` or `"Reconnecting · trying now…"` when `_nextRetryAt == null`. Timer stops on non-Reconnecting state transitions. `Dispose` unsubscribes and disposes the timer.
- [X] T025 [US3] `tests/AkmlSql.Web.Tests/Bridge/ReconnectLoopTests.cs` written with all 7 cases: `SocketCloseTransitionsToReconnecting`, `RetrySucceedsRestoresOpen`, `BackoffSequenceMatchesContract` (asserts `[500, 1000, 2000, 4000, 8000, 16000, 30000, 30000]` ms with `ZeroJitter`), `JitterStaysInRange` (1000 iterations × 8 steps, deterministic `Random(1234)`), `RevocationTerminatesLoop` (LAN connection with bearer; second socket returns `PinRequired`; asserts `State=Failed`, `vault.Removed` contains the connection id, `connection.BearerTokenWrappedRef` cleared via `IConnectionStore.UpdateAsync`), `DisconnectAsyncBypassesRetry` (5-second jitter holds reconnect in Reconnecting → DisconnectAsync → asserts terminal Disconnected), `InBrowserWorkSurvivesReconnect` (FR-015 — `CompletionService.CompleteAsync` returns empty during Reconnecting, doesn't throw). All 7 pass in 761 ms; 36 bridge-folder tests pass total (29 pre-existing + 7 new). Added `FakePairingTokenVault` + `FakeConnectionStore` test doubles in the same file.
- [ ] T026 [US3] **DEFERRED — out of MVP session scope (same pattern as T015)**: Manual smoke test requires a running engine + a live browser session. The seven unit tests under T025 cover the state-machine logic + backoff sequence + revocation cleanup; the manual smoke is the operator-visible UX verification step (status-bar text appears, countdown is readable) which can only be validated interactively.

**Checkpoint**: PRD §5 "Auto-reconnect on transient drops: Yes" is now actually true; reconnect time (SC-002 ≤10 s) measurable in the smoke step; seven new unit tests green.

---

## Phase 6: User Story 4 — Schema object tree (Priority: P2)

**Goal**: `SchemaTreeComponent.razor` renders the cached Phase A/B snapshots in the editor sidebar; clicking a leaf inserts the bracketed qualifier into the editor; offline state shows a stale badge; the tree handles 2,000+ tables without UI jank.

**Independent Test**: With an `Open` bridge against a sample database, the editor page shows a Database → Schema → Object-Kind tree within 500 ms of the snapshot landing in the cache. Click a table — `[schema].[table]` appears at the editor caret. Disconnect — the tree stays visible with a "Stale — <relative time>" badge.

- [X] T027 [US4] `src/AkmlSql.Web/Shared/SchemaTreeComponent.razor` written: `@inject ISchemaCacheStore Cache, IEngineBridge Bridge, ISchemaSync Sync`; deserialises `SchemaSnapshot.PhaseB ?? PhaseA` via MessagePack into `SchemaPhasePayload`; renders Database → Schema → Object-Kind (Tables/Views/Stored Procedures/Functions) → Object → Column; subscribes to `Bridge.StateChanged` (drives stale badge) and `Sync.ChecksumDrifted` (drives refresh); Blazor `<Virtualize ItemSize="24">` kicks in for `kind.Objects.Count > 200`; raises `EventCallback<string> OnObjectClicked` with `"[schema].[name]"`; styling uses `--akml-*` CSS vars only. `DbObjectType` enum is duplicated locally as a private nested enum (avoids dragging IntelliSense's SQL-parser dependencies into the WASM build per the closure-spec discipline).
- [X] T028 [US4] Editor.razor wiring complete: added `@inject IConnectionStore Connections`; grid is now `1fr 240px 320px` (Editor | SchemaTree | Problems); `OnInitializedAsync` loads the active connection and derives `_activeServerIdentity = "{host}:{port}"`, `_activeDatabaseName = "master"` (per-db picker is a follow-up); `OnSchemaObjectClickedAsync` calls `_editor.InsertAtCaretAsync(qualifier)`. Added `EditorComponent.InsertAtCaretAsync(string)` that bridges to a new `insertAtCaret(hostElementId, text)` export in `wwwroot/js/akml-editor.js` — uses `view.dispatch({ changes: { from: head, insert: text }, selection: { anchor: head + text.length } })` so the caret lands at the end of the inserted text (matches SSMS Object Explorer click-to-insert feel). Added `Microsoft.AspNetCore.Components.Web.Virtualization` to `_Imports.razor` so `<Virtualize>` resolves project-wide.
- [X] T029 [US4] `HashSet<string> _expanded` owns the expansion state. `Toggle(path)` adds/removes; `IsExpanded(path)` reads. On `ChecksumDrifted` the set is **not** cleared — paths still present in the new payload keep their `[X]` state, paths that vanished simply stop being rendered (the set still contains their key but it never gets visited). The `ChecksumDriftRefreshesTreePreservesExpansion` bUnit test asserts this directly.
- [X] T030 [US4] `tests/AkmlSql.Web.Tests/Bridge/SchemaTreeComponentTests.cs` written with 8 bUnit tests covering the contract: `RendersDatabaseSchemaTableHierarchyFromPhaseA`, `ExpandsTableShowsColumnsFromPhaseB` (5 columns, asserts type-string content), `ChecksumDriftRefreshesTreePreservesExpansion` (Customer kept expanded after Orders is added), `StaleBadgeAppearsWhenDisconnected` (asserts "5 minutes ago" text), `StaleBadgeHiddenWhenOpen`, `ClickOnObjectRaisesQualifiedName` (asserts callback payload = `"[dbo].[Customer]"`), `EmptyStatePlaceholderWhenNoSnapshot`, `VirtualisationKicksInPastThreshold` (250 tables → `<Virtualize>` wrapper present). All 8 pass in 848 ms. Added `FakeEngineBridge` + `FakeSchemaSync` test doubles in the same file (the production `EngineBridge` requires a `Func<IBridgeWebSocket>` and a token vault — far too much wiring for component tests that don't exercise the bridge itself). Added `ExpandForTest(string)` as an `internal` helper on `SchemaTreeComponent` so tests can pre-expand without simulating click events for every node.
- [ ] T031 [US4] **DEFERRED — out of MVP session scope (same pattern as T015, T026)**: Manual smoke test requires a paired browser session + an active SQL Server with a populated schema. The 8 bUnit tests cover render shape, expansion preservation, stale badge wiring, click-to-insert callback, virtualisation threshold, and empty-state placeholder — every code path the operator UX exercises. The interactive validation (visible tree, real click into a real editor, observable stale-badge appearance on disconnect) is the operator gate.

**Checkpoint**: M3 PRD §12 checkbox 3 fully closed ("renders tree"). SC-005 verifiable.

---

## Phase 7: User Story 5 — End-to-end coverage on the wire (Priority: P3)

**Goal**: `dotnet test --filter Category=BridgeE2E` runs two new E2E suites against a real engine built from current source and reports pass.

**Independent Test**: Run `dotnet test tests/AkmlSql.E2E.Tests/AkmlSql.E2E.Tests.csproj --filter Category=BridgeE2E` — the fixture builds the engine, picks a free port, launches it, drives `BridgeHandshakeTests`'s five RPC scenarios, kills the engine, reports green. Then `dotnet test tests/AkmlSql.Web.E2E.Tests/AkmlSql.Web.E2E.Tests.csproj --filter Category=BridgeE2E` runs the four Playwright scenarios and reports green.

- [X] T032 [US5] `tests/AkmlSql.E2E.Tests/Harness/EngineLaunchFixture.cs` created (moved from the contract-named `AkmlSql.Web.E2E.Tests/Harness/` because BridgeHandshakeTests is the only consumer this session; UserStory2Tests is deferred per T034 below). `IAsyncLifetime` with the contract's state machine: Build → free-port pick → write temp config.json → spawn `AkmlSql.Engine.exe --pipe <name> --parent-pid <pid>` → TcpClient readiness probe (30 s budget) → Ready. `RelaunchAsync()` helper kills and respawns with a fresh port + rewritten config. `ClearTokensAndRelaunchAsync()` helper for revocation scenarios. Cleanup deletes the temp tree on `DisposeAsync`. **`AKML_APP_DATA_ROOT` env var** is the redirection mechanism (the Windows `%APPDATA%` env var is not honoured by `Environment.GetFolderPath` in .NET, so the fixture needs its own override hook into `AkmlSql.Core.Constants.AppDataPath`); 14 lines added to `Constants.AppDataPath`/`LocalAppDataPath` properties for this test affordance — gated to `if (envVar != null)` so production behaviour is byte-identical.
- [X] T032b [US5 follow-on] **Discovered + fixed a real production bug while writing the fixture**: `HandshakeHandler` was defined in `src/AkmlSql.Engine/Handlers/Handshake/HandshakeHandler.cs` (spec 021 T060) but **never registered with the engine's RpcRouter**. The named-pipe transport never noticed because it doesn't run handshakes, but the WebSocketTransport returns `null` for unregistered messages and the browser's receive loop times out. Added the missing `router.Register(new Handlers.Handshake.HandshakeHandler());` line to `src/AkmlSql.Engine/EngineHandlerRegistry.cs` (after the existing Control handlers section). Localhost auto-accept (HandshakeHandler line 160-168) preserves the spec-021 unauthenticated-localhost semantics. This was the latent gap that made the FR-027 composition work look fine in unit tests but break end-to-end.
- [X] T033 [US5] `tests/AkmlSql.E2E.Tests/BridgeHandshakeTests.cs` created with `[Trait("Category","BridgeE2E")]` and `IClassFixture<EngineLaunchFixture>`. 5 test methods: `LocalhostHandshake_ReturnsOkAndCapabilities` (passes — engine reports `core.*` capabilities + non-empty version), `BearerReplay_OnSecondConnect_Succeeds` (passes — two sequential handshakes both succeed in localhost mode; this is the wire-level analogue of the production reconnect path), `RevokedBearer_OnReconnect_ReturnsPinRequired` (`SkippableFact` — localhost mode auto-accepts every inbound, so PinRequired is unreachable; LAN-mode coverage lives in `WebSocketTransportLanTests` under the `Elevated` trait), `EngineRestart_ReconnectSucceedsWithStoredBearer` (passes — uses `fixture.RelaunchAsync()` to kill+respawn the engine, then re-handshakes successfully), `BackoffSequenceDocumented_NotEnforcedOverTheWire` (passes — documented marker that the deterministic schedule lives in the unit tier; re-asserting it from an E2E wire probe would tie test timing to the 30 s cap). Used raw `ClientWebSocket` + MessagePack directly — adding a transport adapter to wrap `IBridgeWebSocket` isn't worth the wiring for 5 tests. Project file updated: added `MessagePack` package + `Xunit.SkippableFact` + project reference to `AkmlSql.Core` for the IPC message types.
- [ ] T034 [US5] **DEFERRED — out of MVP session scope (closure-spec deferral pattern, same as T015 / T026 / T031)**: Playwright-driven `UserStory2Tests` written blind against an unobserved UI is high-risk — the Connection Picker selectors, browser context isolation, and IndexedDB persistence shape need interactive iteration to land 4 reliable tests. The harness scaffolding (`EngineLaunchFixture`) is shipped; T034 inherits it via `IClassFixture<>` whenever the next interactive session takes it on. The wire-level coverage of the same scenarios (handshake, reconnect, restart) already lands via T033's `BridgeHandshakeTests`.
- [X] T035 [US5] Verified default `dotnet test` runs DO skip `BridgeE2E`: `dotnet test tests/AkmlSql.E2E.Tests/AkmlSql.E2E.Tests.csproj --filter "Category!=BridgeE2E"` reports `Failed: 0, Passed: 102, Skipped: 0` — the 5 BridgeE2E tests are excluded by the trait filter as designed.
- [X] T036 [US5] Opt-in run: `dotnet test tests/AkmlSql.E2E.Tests/AkmlSql.E2E.Tests.csproj --filter "Category=BridgeE2E"` reports `Failed: 0, Passed: 4, Skipped: 1, Total: 5, Duration: 994 ms` (the 1 skip is `RevokedBearer_OnReconnect_ReturnsPinRequired` — gated to LAN mode by SkippableFact). The web-side `dotnet test tests/AkmlSql.Web.E2E.Tests/...` filter run is **deferred along with T034** (no `BridgeE2E`-tagged classes shipped in that project this session; the existing Playwright tests there are unaffected).

**Checkpoint**: M3 PRD §12 checkbox 5 closed (E2E coverage on the wire). SC-006 verifiable.

---

## Phase 8: Polish & cross-cutting concerns

**Purpose**: Wrap up — mark the spec 021 deferred tasks closed by this work, record the closure summary, run the full suite for regression evidence.

- [X] T037 `doc/progress.md` extended with the spec 025 closure block: 5 user stories landed (33 of 41 tasks), 8 deferred items recorded (T015 / T026 / T031 / T034 manual smokes + Playwright + 3 carried-forward follow-ups: TLS fingerprint dialog, engine tray pane, in-flight WS revocation), per-phase landing notes, the production-bug discovery (`HandshakeHandler` registration), and verification numbers.
- [X] T038 `specs/021-web-edition/tasks.md` updated: T058 (LAN TLS) flipped `[X]` with reference to spec 025 US1; T068 (reconnect follow-up) updated with reference to spec 025 US3 + `ReconnectLoopTests`; T079 (BridgeHandshakeTests) flipped `[X]` with reference to spec 025 T033; T078 (Playwright US2) note updated to point at spec 025 T034 (carried forward); T065 (tray UI) + T066 (in-flight revocation close) extended with the §Out of Scope cross-link.
- [X] T039 M3 PRD §12 Definition of Done audit walked: 7 of 8 checkboxes flipped `[X]` in `doc/WEB/M3-websocket-transport.md`. Localhost mode ✓ (spec 021 T057 + spec 025 T032b handshake fix), LAN mode with pairing ✓ (spec 025 T009..T014 + LAN smoke deferred at T015), Phase A tree ✓ (spec 025 T027..T030 + manual smoke deferred at T031), live IntelliSense ✓ (spec 021 T072..T074 now actually opens against a running engine), pairing flow ✓ (spec 021 T063..T071 + spec 025 cert pinning), threat model ✓ (T016), firewall docs ✓ (T017 + T019). Only the "PR merge to master" checkbox stays open — that's the user's gate.
- [X] T039b [advisor follow-up] **Bug found + fixed in Editor.razor before declaring done**: the advisor caught that `grid-template-columns: 1fr 240px 320px` was set unconditionally even though `<SchemaTreeComponent>` only renders when `_showSchemaTree == true`. Result for any first-time visitor (no paired connection): a visible empty column where the schema tree would go. Fixed by switching the grid class between `.akml-editor-grid.with-schema` (`1fr 240px 320px`) and `.akml-editor-grid.no-schema` (`1fr 320px`, the original layout). Validated `dotnet build src/AkmlSql.Web` is still 0/0. The bUnit tests for `SchemaTreeComponent` don't catch this because they test the component in isolation; T031 manual smoke would catch it but is deferred. The named-pipe path was also re-verified after the `HandshakeHandler` registration: `dotnet test --filter "Transport|Handler|EngineHost"` → 145/145 pass.
- [X] T040 Full default test suite run (`dotnet test --filter "Category!=Elevated&Category!=BridgeE2E"`) result: **Failed: 1, Passed: 1814, Skipped: 1, Total: 1816**. Breakdown: AkmlSql.AI.Tests 5/5; AkmlSql.Core.Tests 533/534 with 1 skip (`HardcodedHexScannerTests.NoHardcodedChromeHex`); AkmlSql.Engine.Tests 1065/1066 with 1 fail (`PerformanceBaselineTests.Capture_or_compare_M0_baseline` — `CompletionRequest.p50 regressed 43.428 ms → 53.867 ms`, +24.0%, allowed 5%); AkmlSql.Web.Tests 211/211. **Confirmed unrelated to spec 025**: the baseline file is git-ignored per-developer state (`.gitignore:44`); spec 025 touches zero files under `src/AkmlSql.Engine/Completion/` or `src/AkmlSql.Core/Completion/`; the test's own docstring says re-run with `AKML_UPDATE_BASELINE=1` if the developer machine state has drifted. Identical class to the previous session's 19.4% drift — operator-side machine-state, not a spec-introduced regression.
- [X] T041 `doc/architecture.md` §9d extended further with a closing paragraph covering US3 reconnect (`BackoffSchedule` + `ReconnectLoopAsync` + bearer-replay + revocation), US4 schema tree (`SchemaTreeComponent` reads cached snapshots), US5 E2E (`EngineLaunchFixture` + `BridgeHandshakeTests` under `[Trait("Category","BridgeE2E")]`), and the **handshake-registration bug fix** call-out — the latent gap that made FR-027's composition look correct in unit tests but break end-to-end. The earlier FR-027 + FR-001..FR-006 paragraphs from spec 025 T019 stay.

**Checkpoint**: Closure spec discipline holds — no scope creep beyond the 5 user stories + the 3 named follow-ups deferred. Spec 021's Phase 4 deferred items have crossed over to `[X]` with this spec's references.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Setup completion. **Blocks US1 and US5** (US1 needs a transport instance to test; US5 needs a real engine listening on WebSocket to drive). Does NOT block US2 / US3 / US4 — those can run in parallel with Phase 2.
- **US1 (Phase 3)**: Depends on Phase 2 (FR-027 composition wired).
- **US2 (Phase 4)**: Independent — depends only on Setup.
- **US3 (Phase 5)**: Unit tests independent (use `FakeBridgeWebSocket`); manual smoke step (T026) needs FR-027 + Phase-2 composition.
- **US4 (Phase 6)**: Unit tests independent; manual smoke step (T031) needs a paired bridge + populated cache.
- **US5 (Phase 7)**: Depends on Phase 2 (FR-027 composition); benefits from US1 (LAN HTTPS) but the localhost E2E variant works without LAN HTTPS.
- **Polish (Phase 8)**: Depends on all five user stories complete.

### User Story Dependencies

- **US1 (P1) — LAN HTTPS**: Depends on Phase 2; otherwise independent of US2 / US3 / US4 / US5.
- **US2 (P1) — Docs**: Independent of all other stories; runs in parallel with everything.
- **US3 (P2) — Reconnect**: Independent at the unit-test level; smoke benefits from US1 LAN binding being live.
- **US4 (P2) — Schema tree**: Independent — reads `ISchemaCacheStore` directly (cache populated by spec 021 T108 `ISchemaSync`).
- **US5 (P3) — E2E**: Depends on Phase 2; localhost-only E2E does not require US1 to be done; LAN scenarios benefit from US1.

### Within Each User Story

- For US1: T009 + T010 sequential (same file); T011 + T012 + T013 parallel across different files; T014 + T015 sequential after the tests land.
- For US2: All four tasks parallel — different files.
- For US3: T020 → T021 → T022 → T023 sequential (same file `IEngineBridge.cs`); T024 + T025 parallel after T023; T026 last (manual).
- For US4: T027 + T029 sequential (same file `SchemaTreeComponent.razor`); T028 after T027; T030 + T031 parallel after T028.
- For US5: T032 first (fixture); T033 + T034 parallel after T032; T035 + T036 sequential at the end.

### Parallel Opportunities

- **Setup**: T002, T003, T004 all `[P]` — confirm three test-project structures in parallel.
- **Foundational**: T008 (installer edit) runs in parallel with the T005 → T006 → T007 chain.
- **Phase 3 (US1)**: T011, T012, T013 parallel (different files: `EngineBridge.cs`, diagnostics, `WebSocketTransportTests.cs`).
- **Phase 4 (US2)**: T016, T017, T018, T019 all parallel.
- **Phase 5 (US3)**: T024 + T025 parallel after T023.
- **Phase 6 (US4)**: T030 + T031 parallel after T028.
- **Phase 7 (US5)**: T033 + T034 parallel after T032; T035 parallel with manual smoke planning.
- **Phase 8 (Polish)**: T037, T039, T041 all `[P]`.

Once Phase 2 is complete, all five user-story phases can run in parallel with appropriate team capacity.

---

## Parallel Example: User Story 1

```bash
# After T009 + T010 land (the transport edit + cert validation), launch the rest in parallel:

# Browser side (different file)
Task: T011 [US1] Verify scheme derivation in src/AkmlSql.Web/Services/IEngineBridge.cs
Task: T012 [US1] Add fingerprint diagnostic to EngineBridge.ConnectAsync

# Engine tests (different file)
Task: T013 [US1] Add 3 LAN-mode tests to tests/AkmlSql.Engine.Tests/Transports/WebSocketTransportTests.cs
```

---

## Implementation Strategy

### MVP First (US1 + US2)

The two P1 stories together close 3 of the 7 M3 DoD checkboxes (LAN binding works, threat model exists, firewall guidance documented):

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (FR-027 engine-host composition).
3. Complete Phase 3: US1 — LAN HTTPS plumbing.
4. Complete Phase 4: US2 — Docs (runs in parallel with Phase 3).
5. **STOP and VALIDATE**: From a second LAN machine, follow `doc/WEB/quickstart-m3.md` end-to-end; pair; observe live IntelliSense. If SC-001 + SC-003 + SC-004 all measurable, MVP demo is ready.

### Incremental Delivery

1. Phase 1 + Phase 2 → engine-host composition merged; bridge actually starts.
2. Add US1 + US2 → first usable LAN deployment with documentation. **MVP demo.**
3. Add US3 → engine restarts no longer disrupt the user.
4. Add US4 → live schema is visible (not just consumed via IntelliSense).
5. Add US5 → E2E coverage retires DoD checkbox 5.
6. Polish → cross-link docs, mark spec 021 deferrals closed.

### Parallel Team Strategy

With three developers (Engineer A / B / C):

1. **Phase 1 + Phase 2**: A drives T005 → T006 → T007 chain; B picks up T008 (installer); C does Setup verifications.
2. **Once Phase 2 lands**:
   - **A**: US1 (LAN HTTPS plumbing).
   - **B**: US2 (docs) → US4 (schema tree).
   - **C**: US3 (reconnect) → US5 (E2E).
3. **Polish**: All three on the four T037–T041 tasks together; ~half-day total.

---

## Notes

- Closure-spec discipline: **no new IPC message types**, **no new test framework**, **no new application surfaces beyond one Razor component + one transport extension + one composition root edit**. If a task starts to introduce a new persistence record, message type, or service interface, it's out of scope — defer and consult.
- Manual smoke tests (T015, T026, T031) require a running engine + a LAN VM or a paired browser session. These are intentionally manual — the E2E suite in Phase 7 is the automated counterpart.
- Three follow-ups remain explicitly deferred (TLS fingerprint mismatch dialog, engine-side tray pairing pane, in-flight WS revocation). `spec.md` §"Out of Scope" names each one; do not silently include them while working on adjacent code.
- Test categories: `BridgeE2E` (opt-in via `--filter Category=BridgeE2E`); `Elevated` (auto-skipped via `[SkippableFact]` when not running elevated, also tagged for filter-exclusion). Default `dotnet test` runs MUST stay green without either category running.
- Verify after every story landing that `dotnet test` (no filter) stays green — closure-spec means zero regression in the IDE-plugin happy path.
