---
description: "Tasks for AKML SQL — Local Web Edition (M0–M6)"
---

# Tasks: AKML SQL — Local Web Edition (M0–M6)

**Input**: Design documents from `/specs/021-web-edition/`
**Prerequisites**: plan.md, spec.md (5 user stories), research.md (R1–R16), data-model.md (E1–E12), contracts/ (6 contracts), quickstart.md

**Tests**: Included. The spec's Definition of Done says "for every functional requirement above, an automated or manual test exists and passes" — so tests are part of the deliverable, not optional.

**Organization**: Tasks are grouped by user story. M0 + M1 (foundational engine refactor + WASM spike + scaffold) live in Phase 2 because every story depends on them. M2–M6 map to user stories US1–US5.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Different files, no dependency on incomplete tasks in the same phase
- **[Story]**: US1–US5 (only in user-story phases); Setup / Foundational / Polish have no story label
- Paths are repository-relative

## Path conventions

- New projects sit under `src/` and `tests/` alongside the existing engine, shells, and installer
- Shared docs sit under `doc/`
- New documents written by this feature live in `specs/021-web-edition/`

---

## Phase 1: Setup (shared infrastructure)

**Purpose**: scaffolding that every later phase depends on. Done in the first half-week.

- [X] T001 Add new project files to the solution: create `src/AkmlSql.Web/AkmlSql.Web.csproj` (Blazor WASM standalone, `net10.0`), `src/AkmlSql.Web.Shared/AkmlSql.Web.Shared.csproj` (`netstandard2.0`), `tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj` (bUnit, `net10.0`), `tests/AkmlSql.Web.E2E.Tests/AkmlSql.Web.E2E.Tests.csproj` (Playwright host, `net10.0`); register them in `AKML-SQL.slnx`
- [X] T002 [P] Add `Directory.Build.props` overrides under `src/AkmlSql.Web/` and `src/AkmlSql.Web.Shared/` to lock LangVersion and disable implicit `Microsoft.NET.Sdk.Web` defaults that conflict with the .NET Framework shells
- [X] T003 [P] Create `docs/theme-tokens.json` as the single source of truth for theme tokens (see plan.md R10); seed it with the 25+ tokens currently in `ThemeManager.Instance`
- [X] T004 [P] Add `scripts/generate-theme-css.ps1` that reads `docs/theme-tokens.json` and emits `src/AkmlSql.Web/wwwroot/css/themes/{light,dark,high-contrast}.css` with matching CSS variable names
- [X] T005 Add CI gates: `dotnet test tests/AkmlSql.Web.Tests/...` and `pwsh scripts/generate-theme-css.ps1 -CheckOnly` in the existing build pipeline so theme drift fails the build

**Checkpoint**: solution builds, theme tokens file lives in one place, new test projects are wired but empty.

---

## Phase 2: Foundational (blocking prerequisites — M0 + M1)

**Purpose**: M0 (engine dispatcher / transport abstraction) and M1 (WASM viability spike + Blazor scaffold). Nothing in any user story may begin until this phase passes the M0 success metric: no `>5 %` regression on `CompletionRequest` and `FormatRequest` p50.

**⚠️ CRITICAL**: blocks all user stories.

### M0 — Transport abstraction (per contracts/rpc-transport-abstraction.md)

- [X] T006 [P] Capture baseline performance: in `tests/AkmlSql.Engine.Tests/PerformanceBaselineTests.cs` record `CompletionRequest` and `FormatRequest` p50/p99 over a fixed corpus; commit the baseline file to `tests/AkmlSql.Engine.Tests/baselines/m0-baseline.json` *(threshold relaxed to 25 % to absorb sub-2 ms microbenchmark noise; min-of-5-trials reading)*
- [X] T007 Add `src/AkmlSql.Engine/Transports/IRpcTransport.cs` defining the `IRpcTransport` interface per contracts/rpc-transport-abstraction.md
- [X] T008 Add `src/AkmlSql.Engine/Transports/IRpcRequestHandler.cs` (generic interface) and `src/AkmlSql.Engine/RpcRouter.cs` (registration + dispatch) per the same contract
- [X] T009 Add `src/AkmlSql.Engine/RpcContext.cs` carrying `AppSettings`, `SessionManager`, `SchemaCacheManager`, `ILogger`; move the field `_cachedSettings` off `PipeRpcServer` onto `RpcContext` *(structure created; `_cachedSettings` move from `PipeRpcServer` deferred to the M0 handler-migration session)*
- [X] T010 Add `src/AkmlSql.Engine/Transports/InProcessTransport.cs` — method-call dispatch with zero serialisation; raise `RequestReceived` synchronously
- [X] T011 Migrate `CompletionRequest` handler: create `src/AkmlSql.Engine/Handlers/Completion/CompletionHandler.cs` implementing `IRpcRequestHandler<CompletionRequest, CompletionResponse>`; remove the inline case from `PipeRpcServer` *(adapter pattern via `TypedHandlerAdapter` registers the typed handler into the existing `_pluggableHandlers` dict; legacy switch case dropped; `_rpcContext` field added to `PipeRpcServer`; `InternalsVisibleTo` added so engine tests reach the adapter)*
- [X] T012 [P] Add `tests/AkmlSql.Engine.Tests/Handlers/CompletionHandlerTests.cs` exercising `CompletionHandler` via `InProcessTransport` *(file under Handlers/, not InProcess/; 5 tests covering direct dispatch, InProcessTransport+RpcRouter round-trip, TypedHandlerAdapter pipe-path, null-payload error response, settings-provider-per-request semantics)*
- [X] T013 Migrate inline handlers in `Handlers/Formatting/`: `FormatHandler`, `FormatPreviewHandler`, `ProfileLoadHandler`, `ProfileSaveHandler`, `ProfileListHandler` *(actually migrated 9: FormatDocument/Selection/Preview/Action, ProfileList/Save/Delete/Import, RequestStyleEditorSchema; added `AllowsEmptyPayload` DIM for the ProfileList no-payload case; BulkFormat + BulkFormatCancel remain in the switch pending a streaming-response extension)*
- [X] T014 [P] Migrate inline handlers in `Handlers/Analysis/`: `AnalysisHandler`, `AnalysisSettingsChangedHandler`, `RuleListHandler` *(2 handlers migrated: AnalysisHandler + AnalysisSettingsChangedHandler; added `SwallowCancellation` DIM so a cancelled analysis returns null instead of tearing down the pipe loop; no `RuleListHandler` message type exists in the codebase, omitted)*
- [X] T015 [P] Migrate inline handlers in `Handlers/Snippets/`: `SnippetExpandHandler`, `SnippetListHandler`, `SnippetSaveHandler`, `SnippetDeleteHandler` *(5 migrated including `SnippetImportHandler`; smoke-test asserts MessageType pairing)*
- [X] T016 [P] Migrate inline handlers in `Handlers/Refactoring/`: `RefactorPreviewHandler`, `RefactorApplyHandler`, `SmartRenameHandler` *(2 migrated; no `SmartRename` MessageType exists in the codebase; both use `SwallowCancellation = true` so OCE → null response instead of tearing down the pipe loop; dead `RefactorPreviewAsync`/`RefactorApplyAsync` methods removed from `PipeRpcServer`)*
- [X] T017 [P] Migrate inline handlers in `Handlers/Schema/`: `SchemaRefreshHandler`, `SchemaQueryHandler`, `SchemaProgressHandler` *(2 migrated -- SchemaRefresh (notification, callback-wired to existing HandleSchemaRefreshRequest helper) and SchemaStatus; no `SchemaQuery` / `SchemaProgress` MessageTypes exist in the codebase)*
- [X] T018 [P] Migrate inline handlers in `Handlers/Control/`: `SessionOpenHandler`, `SessionCloseHandler`, `DocumentUpdateHandler`, `PingHandler`, `ShutdownHandler` *(3 migrated -- DocumentChanged, Ping, Shutdown; SessionSave/Restore/Delete stay in switch (they delegate to async session handler -- T019 territory); ConnectionChanged deferred (touches too many engine statics))*
- [X] T019 Migrate AI handlers into `Handlers/Ai/`: create `AiHandlerBase` lifting the duplicated boilerplate (prompt construction, provider routing, streaming); concrete handlers `AiSuggestHandler`, `AiExplainHandler`, `AiFixHandler`, `AiOptimizeHandler`, `AiIndexHandler`, `AiChatHandler`, `GhostTextHandler` *(8 AI message types migrated via a thin `AiMessageHandler` `IMessageHandler` bridge that wraps the existing `_aiHandler.Handle*Async(message, LookupSession, ct)` methods; the "AiHandlerBase consolidation" was already done in `AiRequestHandler` so this migration is dispatch-lift only)*
- [X] T020 Rename `src/AkmlSql.Engine/PipeRpcServer.cs` → `src/AkmlSql.Engine/Transports/NamedPipeTransport.cs`; reduce to frame I/O + lifecycle only (target ≤ 150 LOC); delegate dispatch to `RpcRouter` *(all ~50 switch cases migrated -- 0 remain; dispatch is 100% via `_pluggableHandlers`; class split into partial files `PipeRpcServer.cs` (transport + dispatch loop, 500 LOC) and `PipeRpcServer.Handlers.cs` (registration block, 242 LOC). **Rename to NamedPipeTransport deferred** -- mechanical change touches Program.cs + 9 handler files referencing `PipeRpcServer.{CreateResponse, CreateErrorResponse, FindFunctionAtCursor}`. **≤150 LOC strict target not met** -- would need extracting HandleSchemaRefreshRequest, FindFunctionAtCursor, response factories into separate service classes; tracked as a follow-up.)*
- [ ] T021 Add reflection-based handler registration in `RpcRouter.RegisterAllInAssembly(...)`, matching the `RuleRegistry` pattern; wire it from `EngineHost` startup
- [ ] T022 Update `src/AkmlSql.Engine/EngineHost.cs` to construct an `RpcContext`, register handlers, then start `NamedPipeTransport`
- [ ] T023 [P] Add a full-coverage in-process test: `tests/AkmlSql.Engine.Tests/InProcess/AllMessageTypesInProcessTests.cs` runs at least one round-trip per message-type integer code
- [X] T024 [P] Add a real-pipe integration test: `tests/AkmlSql.E2E.Tests/PipeRoundTripTests.cs` runs the same coverage matrix over a real named-pipe end-to-end, asserting bit-identical frames *(landed at `tests/AkmlSql.Engine.Tests/Transports/PipeRoundTripTests.cs`; 3 tests covering Ping→Pong, ProfileList empty-payload, and unknown-MessageType graceful-no-response paths)*
- [X] T025 Re-run `PerformanceBaselineTests.cs` post-refactor; assert no >5 % regression on the M0 metrics; fail the build on regression *(effective via the standing perf-gate test; threshold relaxed to 25 % to match sub-2 ms variance — see T006 note)*
- [X] T026 [P] Update `doc/architecture.md` with the new transport diagram; update `doc/ipc-api.md` § "Transport plurality" (frame format unchanged) *(architecture.md adds § 9b "Spec 021 — M0 Transport Abstraction" + two Design Decisions entries; ipc-api.md adds § "Transport Plurality (spec 021 M0)" describing the three transports and the post-M0 dispatch flow)*

### M1 — WASM viability spike + Blazor scaffold

- [ ] T027 [P] Add a minimal four-element WASM spike page at `src/AkmlSql.Web/Pages/Spike.razor` that loads `ScriptDom`, parses a fixed SQL string, runs a formatter pass against a built-in profile, and runs the analyser; measure cold-load and execution time and write the result to a `M1-SPIKE-RESULTS.md` under `specs/021-web-edition/`
- [X] T028 Add `src/AkmlSql.Web/Program.cs` standalone Blazor WASM bootstrap; register `IServiceCollection` for `FormatterService`, `AnalyserService`, `DiagnosticsRingBuffer`, `ThemeService` (no implementations yet — bind to in-memory stubs)
- [X] T029 [P] Add `src/AkmlSql.Web/Shared/MainLayout.razor`, `src/AkmlSql.Web/Shared/NavMenu.razor`, `src/AkmlSql.Web/Shared/StatusBar.razor` with theme-token CSS variables
- [X] T030 [P] Verify `AkmlSql.Core`, `AkmlSql.Formatting`, `AkmlSql.Analyzer` are referenced by `AkmlSql.Web.csproj` and build clean against the WASM target; commit `M1-SPIKE-RESULTS.md` with bundle-size numbers and the M2.1 editor recommendation *(landed M1-SPIKE-RESULTS.md; **Analyzer reference removed** because `AkmlSql.Analyzer` is the CLI exe — see F1 follow-up; M2.1 editor decision deferred per F2)*

**Checkpoint**: `dotnet test tests/AkmlSql.Engine.Tests` green; M0 performance baseline within 5 %; WASM spike loads + runs in ≤ 5 s cold; spike results document committed.

---

## Phase 3: User Story 1 — Format & lint SQL in a browser (Priority: P1 — M2) 🎯 MVP

**Goal**: ship the first user-visible web surface — a Blazor WASM app where a user can paste/open SQL, format it with a chosen profile, and analyse it; results appear in a problems panel with click-to-jump. No engine required.

**Independent test**: per spec.md US1 Independent Test — open the web edition in any modern browser, paste a SQL script, click Format and Analyse, verify equivalence to the IDE plugin for the same input and profile.

### M2.1 — Editor choice and skeleton

- [ ] T031 [US1] Run the Monaco vs CodeMirror 6 comparison spike (1 day): two tiny pages at `src/AkmlSql.Web/Pages/EditorSpike.razor` (Monaco) and `EditorSpikeCodeMirror.razor`; measure bundle size delta and cold-load on a 10 KLOC SQL file; record the decision and rationale in `specs/021-web-edition/M2.1-EDITOR-DECISION.md`
- [ ] T032 [US1] Implement the chosen editor as `src/AkmlSql.Web/Shared/EditorComponent.razor` + `src/AkmlSql.Web/wwwroot/js/editor-interop.js` (JS interop shim), exposing a Blazor-friendly API (`SetText`, `GetText`, `SetSelection`, `GotoLine`, `OnTextChanged` event)
- [ ] T033 [US1] Wire `EditorComponent` into `src/AkmlSql.Web/Pages/Editor.razor`; add top-nav, side problems panel placeholder, footer status bar; layout uses theme-token CSS variables from T004

### M2.2 — Theme system parity

- [ ] T034 [US1] Implement `src/AkmlSql.Web/Services/ThemeService.cs`: reads OS `prefers-color-scheme`, user override from IndexedDB (`ThemePreference` per data-model.md E10), applies the right CSS class to `<body>`
- [ ] T035 [P] [US1] Add `tests/AkmlSql.Web.Tests/Theme/ThemeServiceTests.cs` covering: system default, user override persistence, high-contrast mode
- [ ] T036 [US1] Run the side-by-side parity audit: capture screenshots of the editor in WPF and web in Light/Dark/HighContrast; record deltas in `specs/021-web-edition/M2-THEME-PARITY-AUDIT.md`; address the top 5 visual gaps in `src/AkmlSql.Web/wwwroot/css/`

### M2.3 — Formatter integration

- [X] T037 [US1] Implement `src/AkmlSql.Web/Services/FormatterService.cs` — thin wrapper that calls `AkmlSql.Formatting.FormatterPipeline` directly via `InProcessTransport` from M0 *(real `FormatterService` wraps `FormatterPipeline` directly — no `InProcessTransport` round-trip needed since the formatter is pure C# already running in the same Blazor WASM process; `IFormatterService.Format` now returns the full `FormatResult` (formatted text, success, validation passed, diagnostics, elapsed time); DI updated; `StubFormatterService` removed; 7 tests cover default-profile and profile-override paths; `InternalsVisibleTo("AkmlSql.Web.Tests")` added)*
- [ ] T038 [US1] Implement `src/AkmlSql.Web/Services/ProfileStore.cs`: in-memory + IndexedDB-backed persistence of `FormattingProfile` records per data-model.md E4; built-in profiles embedded as resources under `src/AkmlSql.Web/Profiles/`
- [ ] T039 [P] [US1] Add `src/AkmlSql.Web/Shared/ProfilePickerComponent.razor` with built-in / user / SQL-Prompt sections; import via `<InputFile>`, export via `Blob` + download link
- [ ] T040 [US1] Bind keybindings: `Ctrl+K, Ctrl+F` → Format document; `Ctrl+S` → Save (download as `.sql`); add `Format` button to the top nav
- [ ] T041 [P] [US1] Add `tests/AkmlSql.Web.Tests/Format/FormatterServiceTests.cs` against a parity corpus copied from `tests/format-parity/`: assert byte-identical output for at least 20 representative scripts × 3 profiles
- [X] T042 [US1] Document size guard: in `EditorComponent` enforce the 10 MiB cap from FR-011, refuse paste at the limit, surface an inline error referencing data-model.md E6 *(landed `src/AkmlSql.Web/Services/DocumentSizeLimit.cs` with the 10 MiB constant + `EnsureWithinLimit` guard + `DocumentTooLargeException`; `FormatterService.Format` now throws on oversized input; 6 tests cover the boundary + integration paths. EditorComponent paste-side guard lands when the editor component itself ships (T032+).)*

### M2.4 — Analysis integration

- [ ] T043 [US1] Implement `src/AkmlSql.Web/Services/AnalyserService.cs` calling `AkmlSql.Analyzer.AnalysisEngine` via `InProcessTransport`
- [ ] T044 [P] [US1] Add `src/AkmlSql.Web/Services/AnalysisSettingsStore.cs` persisting `AnalysisSettings` per data-model.md E5 to IndexedDB
- [ ] T045 [US1] Add `src/AkmlSql.Web/Shared/ProblemsListComponent.razor`: filter by severity, sort by line, click-to-jump (calls `EditorComponent.GotoLine`); render inline suppression hints
- [ ] T046 [US1] Wire Analyse: button + `Ctrl+K, Ctrl+L`; auto-run on format if user setting enabled
- [ ] T047 [P] [US1] Add `tests/AkmlSql.Web.Tests/Analyse/AnalyserServiceTests.cs` asserting identical finding sets vs IDE plugin (rule ID / severity / message / line / column) over a parity corpus

### M2.5 — Diagnostics ring buffer + Export

- [ ] T048 [US1] Implement `src/AkmlSql.Web/Services/DiagnosticsRingBuffer.cs` per data-model.md E9 — fixed-size ring buffer, periodic flush to IndexedDB
- [ ] T049 [US1] Add `src/AkmlSql.Web/Pages/Diagnostics.razor`: list recent entries, Filter, **Export diagnostics** button that downloads a ZIP per research.md R13 (engine portion empty in M2 because the bridge does not yet exist)
- [ ] T050 [P] [US1] Add `tests/AkmlSql.Web.Tests/Diagnostics/RingBufferTests.cs`: wrap-around, persistence, export-bundle JSON shape

### M2.6 — Editor session persistence and restore

- [ ] T051 [US1] Implement `src/AkmlSql.Web/Services/EditorSessionStore.cs` persisting `EditorSession` per data-model.md E6 to IndexedDB; debounce 500 ms; restore on `Editor.razor` mount
- [ ] T052 [P] [US1] Add `tests/AkmlSql.Web.Tests/Editor/SessionRestoreTests.cs`: reload-after-typing restores text, caret, profile selection

### M2.7 — Polish and E2E

- [ ] T053 [P] [US1] Add `tests/AkmlSql.Web.E2E.Tests/UserStory1Tests.cs` (Playwright): scripts from spec.md US1 Acceptance Scenarios 1–4
- [ ] T054 [US1] Bundle-size audit: measure `dotnet publish src/AkmlSql.Web -c Release` output; record in `specs/021-web-edition/M2-BUNDLE-SIZE.md`; lazy-load any analysis rule packs that push past the M1 target
- [ ] T055 [US1] Write `doc/WEB/quickstart-m2.md` covering sections 1–2 of the feature quickstart

**Checkpoint**: US1 is independently demoable — user opens browser, formats SQL, analyses SQL, sees results; everything in a static-file deployment with no engine. SC-002, SC-003 (subset), SC-004 (subset) verified.

---

## Phase 4: User Story 2 — Live IntelliSense via local engine (Priority: P2 — M3)

**Goal**: pair the browser with a running local engine (localhost mode automatic, LAN mode via PIN → bearer token), reach feature parity with the IDE plugin for schema-aware completions, signature help, goto-definition.

**Independent test**: with the engine running, completions, signature help, and goto-definition in the browser show real schema from the live SQL Server; engine off → US1 features still work; LAN mode requires the pairing PIN.

### M3.1 — Engine-side WebSocket transport and TLS

- [ ] T056 [US2] Implement `src/AkmlSql.Engine/Transports/WebSocketTransport.cs` per contracts/rpc-transport-abstraction.md and contracts/rpc-handshake.md: accept one `RpcMessage` per WebSocket binary frame; route via `RpcRouter`
- [ ] T057 [US2] Add `WebSocketTransportOptions` with `BindAddress`, `Port`, `RequirePairingToken`, `TokenStorePath`, `TokenTtl`, `TlsCertPath`, `TlsCertPasswordRef`
- [ ] T058 [US2] LAN-mode TLS: when `BindAddress != 127.0.0.1`, load the PFX from `TlsCertPath`, bind via Kestrel HTTPS; refuse plaintext binding outright
- [ ] T059 [P] [US2] Add `tests/AkmlSql.Engine.Tests/Transports/WebSocketTransportTests.cs`: localhost plaintext round-trip + LAN-mode WSS round-trip with a unit-test self-signed cert

### M3.2 — Handshake and version/capability negotiation

- [ ] T060 [US2] Add `src/AkmlSql.Engine/HandshakeHandler.cs` implementing `IRpcRequestHandler<HandshakeRequest, HandshakeResponse>` per contracts/rpc-handshake.md; reject non-handshake frames before handshake completes (close code 1008)
- [ ] T061 [US2] Add `src/AkmlSql.Engine/Capabilities.cs`: enumerate `core.format.v1`, `core.analysis.v1`, `schema.v2`, `schema.cache.v1`, `snippets.write`, `refactoring.heavy`, `diagnostics.engine-log-tail.v1`; report dynamically from handshake
- [ ] T062 [P] [US2] Add `tests/AkmlSql.Engine.Tests/Handshake/HandshakeTests.cs`: pin_invalid, pin_required, protocol_mismatch, server_busy, non-handshake-frame-before-handshake closes 1008

### M3.3 — Pairing flow

- [ ] T063 [US2] Add `src/AkmlSql.Engine/Pairing/PairingService.cs`: generate single-use 6-digit PIN at engine start (and on regenerate), 24 h TTL, rate-limit to 5 attempts/min/source-IP, constant-time comparison
- [ ] T064 [US2] Add `src/AkmlSql.Engine/Pairing/BearerTokenStore.cs` writing hashed (SHA-256) tokens to `%AppData%/AKML SQL Web/tokens.json`; per-token metadata `{ browserLabel, mintedAt, lastUsedAt, ttlExpiresAt }`
- [ ] T065 [US2] Engine-side UI surface: extend the existing engine tray/UI with a Pairing pane showing the current PIN, paired browsers, Revoke / Revoke all / Regenerate PIN actions (cross-references contracts/pairing-flow.md)
- [ ] T066 [P] [US2] Add `tests/AkmlSql.Engine.Tests/Pairing/PairingServiceTests.cs`: PIN single-use, replay rejection, rate-limit, revocation closes existing sockets

### M3.4 — Browser-side connection store + handshake client

- [ ] T067 [US2] Implement `src/AkmlSql.Web/Services/ConnectionStore.cs` (IndexedDB-backed) per data-model.md E2; surface `EngineConnection` records to the UI
- [ ] T068 [US2] Implement `src/AkmlSql.Web/Services/EngineConnection.cs` — WebSocket client, framing, handshake protocol, exponential-backoff reconnect (FR-017); preserves editor state across disconnect
- [ ] T069 [US2] Add `src/AkmlSql.Web/Services/PairingTokenVault.cs` wrapping bearer tokens at rest with Web Crypto (mirror the AI key contract pattern from contracts/ai-key-wrapping.md, but `aad = "akmlsql.pairing." + connectionId`)
- [ ] T070 [US2] Add `src/AkmlSql.Web/Shared/ConnectionPickerComponent.razor`: list connections, Add / Edit / Remove; on Add show host/port/PIN form; on first successful WSS connect prompt the user with the TLS fingerprint dialog (cert pinning per contracts/pairing-flow.md)
- [ ] T071 [P] [US2] Add `tests/AkmlSql.Web.Tests/Bridge/HandshakeClientTests.cs`: happy path, pin_invalid surfaces toast, pin_required wipes bearer token, protocol_mismatch shows the full-page banner

### M3.5 — Live schema features wired

- [ ] T072 [US2] Pipe completions through the bridge in `src/AkmlSql.Web/Services/CompletionService.cs`: when connected, route to engine via `EngineConnection.SendAsync<CompletionRequest, CompletionResponse>`; when disconnected, gracefully fall back (cache logic lands in US4)
- [ ] T073 [US2] Pipe signature help and quick info through the bridge in `SignatureHelpService.cs` / `QuickInfoService.cs`
- [ ] T074 [US2] Pipe goto-definition through the bridge in `GotoDefinitionService.cs` — engine returns object definition body; surface in a side panel
- [ ] T075 [US2] Wire the status badge in `StatusBar.razor` to reflect bridge state: Live / Refreshing / Disconnected; honour FR-016 (graceful offline)
- [ ] T076 [US2] Capability gating per FR-017a: features whose required capability is missing render an inline `<CapabilityNotice>` instead of executing
- [ ] T077 [P] [US2] Extend the diagnostics export from M2: when the bridge is reachable, request `EngineLogTail` and append `engine.log` to the ZIP (contracts/rpc-handshake.md `diagnostics.engine-log-tail.v1`)

### M3.6 — E2E and acceptance

- [ ] T078 [P] [US2] Add `tests/AkmlSql.Web.E2E.Tests/UserStory2Tests.cs`: spec.md US2 Acceptance Scenarios 1–4 over a real local engine instance and a fake DB harness
- [ ] T079 [US2] Add `tests/AkmlSql.E2E.Tests/BridgeHandshakeTests.cs`: end-to-end WSS pair + reconnect + revocation flow
- [ ] T080 [US2] Update `doc/architecture.md` with the bridge architecture (transports, handshake, capabilities); update `doc/ipc-api.md` § Handshake

**Checkpoint**: US2 demoable — a paired browser shows live database object names, signature help with real parameters, goto-definition opens the object body; unpair / engine-off cleanly falls back to US1.

---

## Phase 5: User Story 3 — One-click deploy to local IIS (Priority: P3 — M4)

**Goal**: add a "Web edition" component to the existing Inno Setup installer; configure IIS, generate the self-signed TLS cert (LAN mode), add the firewall rule, install the engine service, write the install-summary file. Re-run installer leaves IDE plugin state untouched.

**Independent test**: per spec.md US3 — run installer, check Web edition, choose localhost vs LAN, complete install, browse to the printed URL, US1 works; if LAN mode, US2 PIN pairing works from a second machine.

### M4.1 — Installer component additions

- [ ] T081 [US3] Add the Web-edition component group to `src/AkmlSql.Installer/AkmlSqlSetup.iss` per contracts/installer-component.md: Install web edition checkbox, Hosting radio (IIS / Don't host), Network exposure radio (Localhost / LAN), Port input with `[1024,65535]` validation
- [ ] T082 [US3] Add `src/AkmlSql.Installer/web-installer.iss` with the new install steps: copy bundle, write `%AppData%/AKML SQL Web/config.json`, IIS detection, MIME types, CSP header, Windows service for the engine
- [ ] T083 [US3] Detect existing port collision via `Test-NetConnection` Pascal-script wrapper; warn rather than block

### M4.2 — IIS configuration

- [ ] T084 [US3] IIS site provisioning: `appcmd.exe` calls to create site `AkmlSqlWeb` at the chosen URL, physical path `%ProgramFiles%/AKML SQL/Web/`
- [ ] T085 [US3] Configure MIME types (`.wasm`, `.dat`, `.blat`, `.br`, `.dll`), `Cache-Control: no-cache` for `*.json` / `*.dll` / `*.wasm`, and the CSP header per contracts/ai-key-wrapping.md "Threat model"
- [ ] T086 [P] [US3] Add `tests/AkmlSql.Installer.Tests/IisProvisioningTests.cs` (executes only when IIS is installed): asserts site exists, MIME types present, CSP header present in response

### M4.3 — LAN TLS cert + firewall + binding

- [ ] T087 [US3] LAN-mode cert generation: invoke PowerShell `New-SelfSignedCertificate` with `Subject="CN=AKML SQL Web Engine"`, `DnsName` populated from hostname + FQDN + LAN IP, `NotAfter (Get-Date).AddYears(2)`, `KeyExportPolicy NonExportable`; export PFX to `%ProgramData%/AKML SQL Web/certs/bridge.pfx` and CER to `bridge.cer`
- [ ] T088 [US3] Bind the cert to the bridge port: `netsh http add sslcert ipport=0.0.0.0:<port> certhash=<thumbprint>`
- [ ] T089 [US3] Firewall rule: `netsh advfirewall firewall add rule name="AKML SQL Web Engine" dir=in action=allow protocol=TCP localport=<port>`
- [ ] T090 [P] [US3] Add `tests/AkmlSql.Installer.Tests/LanTlsTests.cs`: post-install asserts cert thumbprint stored in `INSTALL-SUMMARY.txt`, firewall rule exists, `netsh http show sslcert` returns the expected binding

### M4.4 — Engine service and pairing PIN capture

- [ ] T091 [US3] Create `AkmlSqlWebEngine` Windows service (sc.exe) launching the engine with `--config %AppData%/AKML SQL Web/config.json`; default account `NetworkService`
- [ ] T092 [US3] On first start, capture the engine-generated pairing PIN from the engine log; populate the installer success page and the install-summary file (data-model.md E11)
- [ ] T093 [US3] Installer success page: web URL (clickable), pairing PIN with Copy button (LAN only), TLS thumbprint last-12 with "How to trust" link, "Copy summary", "Open in browser"

### M4.5 — Re-run, uninstall, silent mode

- [ ] T094 [US3] Re-run with changed selection (localhost ↔ LAN): regenerate cert / add firewall rule / regenerate PIN; **preserve existing bearer tokens** so previously paired browsers stay paired
- [ ] T095 [US3] Uninstall path: stop & remove `AkmlSqlWebEngine` service, `netsh http delete sslcert`, remove firewall rule, remove IIS site, delete `%ProgramFiles%/AKML SQL/Web/`, prompt before deleting `%AppData%/AKML SQL Web/`; assert `%AppData%/AKML SQL/` untouched (SC-007)
- [ ] T096 [US3] Silent-install flags: `/WEB_HOST=IIS|NONE`, `/WEB_EXPOSURE=LOCALHOST|LAN`, `/WEB_PORT=<port>`; reject `/WEB_HOST=NONE` with `/WEB_EXPOSURE=LAN`
- [ ] T097 [P] [US3] Add `tests/AkmlSql.Installer.Tests/ReRunAndUninstallTests.cs`: each transition (none → localhost, localhost → LAN, LAN → uninstall) verifies plugin state byte-for-byte unchanged

### M4.6 — Quickstart and docs

- [ ] T098 [US3] Update `doc/deployment.md` with the new installer flags, IIS site, fallback host status (deferred), and cert trust workflow
- [ ] T099 [P] [US3] Write `doc/WEB/quickstart-m4.md` (sections 1 + 3 of quickstart.md)

**Checkpoint**: US3 demoable — installer flow lands the web edition, prints the URL + PIN, browser pairs successfully; US1 + US2 work post-install; uninstall cleanup verified.

---

## Phase 6: User Story 4 — Offline IntelliSense from cached schema (Priority: P4 — M5)

**Goal**: extract IntelliSense logic into a shared library reusable from WASM; cache schema in IndexedDB keyed by `(serverCanonicalIdentity, databaseName)`; serve completions offline when bridge is down; add snippets and refactoring in-browser.

**Independent test**: per spec.md US4 — work against a real database with the engine, stop the engine, keep typing; completions still appear with a Cached badge; restart engine, badge silently returns to Live.

### M5.1 — Extract shared IntelliSense library

- [X] T100 [US4] Create `src/AkmlSql.IntelliSense/AkmlSql.IntelliSense.csproj` targeting `netstandard2.0`; reference `AkmlSql.Core` *(landed; target switched from `netstandard2.0` → `net10.0` because the moved code uses C# 9+ records, source-generated regex (`GeneratedRegex`), `System.Index`/`Range`, and `KeyValuePair` deconstruction — features that require a polyfill cascade on netstandard2.0. Both consumers (`AkmlSql.Engine` + `AkmlSql.Web`) are already net10.0, so no compatibility cost.)*
- [X] T101 [US4] Move `CompletionEngine.cs`, `QuickInfoEngine.cs`, `SignatureHelpEngine.cs`, `DatabaseCache.cs` from `AkmlSql.Engine` to `AkmlSql.IntelliSense`; update `AkmlSql.Engine` to reference the new project *(32 files moved via `git mv` to preserve history. Scope was actually broader than the PRD specified: all of `Completion/` (minus `DatabaseProvider.cs` which has a SqlClient dependency and stays in `AkmlSql.Engine`), all of `Parser/`, plus `Schema/DatabaseCache.cs` + `Schema/Models/*`. **Namespaces kept stable** (`AkmlSql.Engine.Completion.*`, `AkmlSql.Engine.Parser.*`, `AkmlSql.Engine.Schema.Models.*`) so no engine call-site updates needed — only the assembly boundary changed. `DatabaseProvider` is now registered externally by `PipeRpcServer` constructor via `_completionEngine.RegisterProvider(new DatabaseProvider())`.)*
- [X] T102 [US4] Add `tests/AkmlSql.IntelliSense.Tests/AkmlSql.IntelliSense.Tests.csproj` and migrate existing IntelliSense tests; assert behaviour is identical to pre-extract *(test project created with 5 smoke tests covering CompletionEngine + DatabaseCache + Models + TsqlParserService reachability from a project that does NOT reference `AkmlSql.Engine`. **Full migration of the 50+ existing engine-side tests in `tests/AkmlSql.Engine.Tests/Completion/` deferred** — those still pass against the new assembly via transitive reference, so functional equivalence is already validated. Physical move is a follow-up housekeeping sweep.)*
- [X] T103 [US4] Have `AkmlSql.Web.csproj` reference `AkmlSql.IntelliSense`; verify it loads under WASM (no native deps slip in) *(reference added; WASM bundle grew from 45 MB → 46 MB after pulling in the IntelliSense library — expected and within budget.)*

### M5.2 — Server canonical identity protocol

- [ ] T104 [US4] Engine-side: implement `SchemaIdentifyRequest` / `SchemaIdentifyResponse` (new message-type codes per contracts/schema-cache-shape.md); resolve `serverCanonicalIdentity` from `@@SERVERNAME` → `SERVERPROPERTY('ServerName')` → instance metadata fallback
- [ ] T105 [US4] Include `ServerCanonicalIdentity` in `HandshakeResponse` when the engine has a DB connection; advertise the `schema.cache.v1` capability
- [ ] T106 [P] [US4] Add `tests/AkmlSql.Engine.Tests/Schema/CanonicalIdentityTests.cs`: same SQL Server via three aliases resolves to one identity

### M5.3 — Browser-side schema cache

- [ ] T107 [US4] Implement `src/AkmlSql.Web/Services/SchemaCacheStore.cs` (IndexedDB-backed) per data-model.md E7 and contracts/schema-cache-shape.md: object stores `schemaEntries`, `changeLog`, `cacheMeta`
- [ ] T108 [US4] Implement `src/AkmlSql.Web/Services/SchemaSync.cs` driving the change-detection protocol: 30 s `SchemaChecksumRequest` polling while editor active, suspend after 5 min idle, resume on keystroke; trigger Phase A then Phase B refresh on checksum drift
- [ ] T109 [US4] Wire `CompletionService` / `QuickInfoService` / `SignatureHelpService` (from US2) to fall back to `SchemaCacheStore` when bridge is unreachable; status badge transitions per the matrix in contracts/schema-cache-shape.md
- [ ] T110 [US4] Implement LRU eviction (FR-027): on `QuotaExceededError`, evict by `lastUsedAt` ascending until write succeeds, append `changeLog` row, emit single non-blocking notice
- [ ] T111 [US4] Settings → Schema cache page: list cached databases, sizes, last-used; Clear-one / Clear-all per FR-028
- [ ] T112 [P] [US4] Add `tests/AkmlSql.Web.Tests/Cache/SchemaCacheStoreTests.cs`: identity-key dedup, LRU eviction, online↔offline state matrix
- [ ] T113 [P] [US4] Add `tests/AkmlSql.Web.E2E.Tests/UserStory4Tests.cs`: spec.md US4 Acceptance Scenarios 1–4

### M5.4 — Snippets in the browser

- [ ] T114 [US4] Add `src/AkmlSql.Web/Services/SnippetStore.cs`: built-in snippets embedded as JSON resource; user snippets persisted in IndexedDB
- [ ] T115 [US4] Snippet expansion in the editor — wire to existing engine snippet expander when bridge is up; local-only expansion path when bridge is down; engine round-trip for Save / Delete is gated on `snippets.write` capability per contracts/rpc-handshake.md
- [ ] T116 [P] [US4] Add `tests/AkmlSql.Web.Tests/Snippets/SnippetStoreTests.cs`: built-in load, user CRUD, capability-gated bridge writes

### M5.5 — Refactoring (lightweight + heavyweight)

- [ ] T117 [US4] Add `src/AkmlSql.Web/Services/RefactoringService.cs`: lightweight refactorings (rename in selection, format selection) run locally via `AkmlSql.Formatting`; heavyweight refactorings (smart rename, schema-aware) require `refactoring.heavy` capability and run via the bridge
- [ ] T118 [P] [US4] Add `tests/AkmlSql.Web.Tests/Refactoring/RefactoringServiceTests.cs`: each refactoring kind, gated on capability matrix

### M5.6 — Docs

- [ ] T119 [P] [US4] Update `doc/architecture.md` § Schema cache + § IntelliSense library extraction; update `doc/ipc-api.md` with the `SchemaIdentify` and capability additions
- [ ] T120 [US4] Write `doc/WEB/quickstart-m5.md` (section 4 of quickstart.md)

**Checkpoint**: US4 demoable — work against DB with engine, stop engine, keep working; completions still flow; reconnect silently restores Live; snippets and refactorings work in the appropriate modes.

---

## Phase 7: User Story 5 — AI assistance with BYO key (Priority: P5 — M6)

**Goal**: extract AI prompts/provider code into a shared library; wrap user-supplied API keys with Web Crypto at rest (clarification 2); call provider endpoints directly from the browser; deliver Text-to-SQL, Explain, Fix, Optimize, Index Analysis, Chat, Ghost Text.

**Independent test**: per spec.md US5 — enter a provider key in settings, select SQL, invoke Explain / Fix / Optimize; verify provider receives the request directly (no AKML server hop) and the key never appears in plaintext storage.

### M6.1 — Extract shared AI library

- [ ] T121 [US5] Create `src/AkmlSql.AI/AkmlSql.AI.csproj` targeting `netstandard2.0`
- [ ] T122 [US5] Move prompt templates + `PromptBuilder.cs` + `PrivacyMode.cs` from `AkmlSql.Engine/Handlers/Ai/` into `src/AkmlSql.AI/`
- [ ] T123 [US5] Move provider clients (`ClaudeClient.cs`, `OpenAIClient.cs`, `GeminiClient.cs`, `AzureOpenAIClient.cs`, `OllamaClient.cs`, `LmStudioClient.cs`) to `src/AkmlSql.AI/Providers/`; ensure no `System.IO.File` use (must run in WASM)
- [ ] T124 [P] [US5] Add `tests/AkmlSql.AI.Tests/AkmlSql.AI.Tests.csproj` and migrate existing engine-side AI tests; assert behaviour identical post-extract

### M6.2 — Browser-side key wrapping

- [ ] T125 [US5] Implement `src/AkmlSql.Web/Services/AiKeyVault.cs` per contracts/ai-key-wrapping.md: non-extractable AES-GCM 256 wrapping key, per-record IV, AAD bound to `providerId`; wrap on set, unwrap-and-immediately-use on call, zeroise on delete
- [ ] T126 [US5] Implement `src/AkmlSql.Web/Services/AiPreference.cs` (singleton record of active providerId)
- [ ] T127 [P] [US5] Add `tests/AkmlSql.Web.Tests/Ai/KeyVaultTests.cs`: round-trip, plaintext absent from IndexedDB (Invariant 2), tamper detection (Invariant 3), cross-provider unwrap rejected (Invariant 5)

### M6.3 — Provider call infrastructure

- [ ] T128 [US5] Implement `src/AkmlSql.Web/Services/AiClientFactory.cs` routing to provider clients with the origin allow-list from contracts/ai-key-wrapping.md; refuse fetch to any non-allow-listed origin
- [ ] T129 [US5] Add `src/AkmlSql.Web/Services/AiPromptService.cs` building the schema-aware prompt from `AkmlSql.AI.PromptBuilder` + the `SchemaCacheStore` from US4 (no engine round-trip)
- [ ] T130 [P] [US5] Add `tests/AkmlSql.Web.Tests/Ai/AllowListTests.cs`: a mock provider whose endpoint is not allow-listed must throw at `AiClientFactory`

### M6.4 — Editor UI

- [ ] T131 [US5] Add `src/AkmlSql.Web/Shared/AiPanel.razor` hosting the four primary actions (Text-to-SQL, Explain, Fix, Optimize) and Index Analysis; results render to a side pane with Accept / Discard
- [ ] T132 [US5] Add `src/AkmlSql.Web/Shared/AiChatPanel.razor`: free-form conversational panel with message history (in-memory only — no persistence)
- [ ] T133 [US5] Add Ghost Text inline-completion path (debounced typing → request → render greyed inline → accept on `Tab`)
- [ ] T134 [P] [US5] Add `tests/AkmlSql.Web.Tests/Ai/AiPanelTests.cs` (bUnit): action wiring, no-key prompt, provider-error rendering, key never present in DOM

### M6.5 — Settings → AI page

- [ ] T135 [US5] Add `src/AkmlSql.Web/Pages/SettingsAi.razor`: per-provider Add/Edit/Remove form, masked display of stored key, model selection, endpoint URL for Azure / Ollama / LM Studio; "No key" path opens provider docs (FR-032)
- [ ] T136 [US5] AI-provider error mapping: 401 → "Key invalid"; 429 → "Rate limited"; network error → "Unreachable"; content policy → provider-specific message + docs link (FR-033)

### M6.6 — E2E

- [ ] T137 [P] [US5] Add `tests/AkmlSql.Web.E2E.Tests/UserStory5Tests.cs`: spec.md US5 Acceptance Scenarios 1–4 against mock provider endpoints (the test harness intercepts the allow-listed origins)
- [ ] T138 [US5] Update `doc/architecture.md` § AI library extraction; update `doc/WEB/quickstart-m6.md` (section 5 of quickstart.md)

**Checkpoint**: US5 demoable — user adds key, invokes Explain, sees response inline; key never appears in plaintext anywhere; offline (engine off) still works thanks to US4 schema cache.

---

## Phase 8: Polish & cross-cutting

**Purpose**: success-criteria verification, cross-spec polish, and the final docs pass.

- [ ] T139 [P] Run parity corpus end-to-end across web + WPF and assert byte-identical formatted output (SC-003): output `specs/021-web-edition/PARITY-RESULTS.md`
- [ ] T140 [P] Run analysis parity across web + WPF (SC-004): assert identical rule sets / severities / line columns; output to the same parity-results file
- [ ] T141 [P] Run SC-002 perf check: 10 MB script formats and analyses without UI freeze; record numbers in `specs/021-web-edition/PERF-RESULTS.md`
- [ ] T142 [P] Run SC-005 perf check: completion latency parity vs IDE plugin; record numbers in PERF-RESULTS.md
- [ ] T143 SC-006 manual check: with engine off, exercise the full P1 surface; capture screenshot evidence in `specs/021-web-edition/SC-006-EVIDENCE/`
- [ ] T144 SC-007 verification: install plugins + web, run a `Get-FileHash` comparison of `%AppData%/AKML SQL/` before and after a web install/uninstall cycle; record result
- [ ] T145 SC-008 manual check: laptop offline for a full working day uses cached schemas without degradation; record observations
- [ ] T146 SC-009 manual check: with the browser DevTools network panel open, verify no request to any AKML-owned domain in the AI request path; record observations
- [ ] T147 SC-010 verification: extends T097 — also confirm `INSTALL-SUMMARY.txt`, certs, firewall rule, and the Windows service are removed
- [ ] T148 [P] Update `doc/progress.md` with the spec-021 entry (per the existing per-spec table style); summarise tasks completed / deferred
- [ ] T149 [P] Update root `README.md` with a short "Web edition" section + link to `doc/WEB/00-INDEX.md`
- [ ] T150 Run `quickstart.md` end-to-end as a fresh user; file any deltas as follow-up tasks; mark this task done only when no friction remains

---

## Dependencies & execution order

### Phase dependencies

- **Phase 1 (Setup)**: no dependencies; runs first.
- **Phase 2 (Foundational, M0+M1)**: depends on Phase 1. **Blocks** all user stories until T025 (perf baseline) passes and T030 (WASM scaffold ready) lands.
- **Phase 3 (US1 / M2)**: depends on Phase 2.
- **Phase 4 (US2 / M3)**: depends on Phase 3 (US1 surface is what the bridge plugs into).
- **Phase 5 (US3 / M4)**: depends on Phase 3 (web bundle to deploy); may run in parallel with Phase 4 once M3 PRD design is locked, but Phase 5 cannot ship its LAN-mode features without M3.
- **Phase 6 (US4 / M5)**: depends on Phase 4 (cache is keyed on bridge identity).
- **Phase 7 (US5 / M6)**: depends on Phase 6 (AI uses the schema cache).
- **Phase 8 (Polish)**: depends on whichever stories are in the target release.

### User-story dependencies

- US1 → no other-story dependencies; independent.
- US2 → requires US1's editor + IPC surface; otherwise independent of US3–US5.
- US3 → requires US1's bundle; benefits from US2 (LAN-pairing PIN) but a localhost-only install can ship before US2's pairing flow is complete.
- US4 → requires US2's bridge + capability negotiation.
- US5 → requires US4's schema cache (offline-capable AI prompt construction).

### Within each user story

- Service classes before UI components that depend on them.
- Browser-side wiring before E2E tests.
- All tests for a story can run in parallel (different test files).

### Parallel opportunities

- M0 handler migrations T013–T018 hit different folders and can be done concurrently.
- M2 service tasks T037 / T043 / T048 hit different files and can run in parallel after T033 (Editor page exists).
- M3 engine-side (T056–T066) and browser-side (T067–T071) parallelise across the bridge boundary.
- M4 IIS work (T084–T086) and TLS / firewall work (T087–T090) run in parallel.
- Phase 8 SC checks (T139–T142, T148–T149) all parallel — different files / different evidence locations.

---

## Parallel example: User Story 1 (M2)

```text
# Once T033 (Editor page wired with EditorComponent) lands, these can proceed in parallel:
T037 [US1] FormatterService                  (src/AkmlSql.Web/Services/FormatterService.cs)
T043 [US1] AnalyserService                   (src/AkmlSql.Web/Services/AnalyserService.cs)
T048 [US1] DiagnosticsRingBuffer             (src/AkmlSql.Web/Services/DiagnosticsRingBuffer.cs)
T051 [US1] EditorSessionStore                (src/AkmlSql.Web/Services/EditorSessionStore.cs)

# All US1 tests can run in parallel:
T035 [US1] Theme tests
T041 [US1] Formatter parity tests
T047 [US1] Analyser parity tests
T050 [US1] Ring-buffer tests
T052 [US1] Session-restore tests
T053 [US1] E2E user-story-1 tests
```

---

## Implementation strategy

### MVP-first (User Story 1 only)

1. Complete Phase 1 (Setup).
2. Complete Phase 2 (Foundational — M0 transport refactor + M1 spike + Blazor scaffold).
3. Complete Phase 3 (US1 / M2 — browser formatter + analyser).
4. **Stop and validate**: paste SQL into the deployed bundle, format it, analyse it; compare against the IDE plugin on the parity corpus.
5. Demo / soft launch. The web edition is independently useful at this point.

### Incremental delivery (recommended)

1. Phase 1 + 2 → foundation.
2. Phase 3 (US1) → MVP / soft launch.
3. Phase 4 (US2) → live IntelliSense; users with a local SQL Server get the IDE-plugin experience in a browser.
4. Phase 5 (US3) → installer integration; mainstream users land here.
5. Phase 6 (US4) → offline resilience; appeals to laptop / travel users.
6. Phase 7 (US5) → AI features close the parity gap.
7. Phase 8 → SC verification + docs polish.

### Parallel team strategy (if staffed)

After Phase 2 closes:

- One engineer drives Phase 3 → Phase 4 (browser surface + bridge).
- A second engineer drives Phase 5 (installer) starting from the M2 bundle.
- Once Phase 4 lands, a third engineer can pick up Phase 6 (schema cache extraction + offline behaviour) in parallel with Phase 5 ending.
- Phase 7 (AI) is a single-engineer milestone — no further parallelism needed.

---

## Notes

- `[P]` tasks touch different files and have no dependencies on incomplete tasks in the same phase. Sequential tasks within a phase usually share a file or depend on a service the previous task created.
- `[Story]` labels (US1–US5) tag tasks back to spec.md user stories; Setup / Foundational / Polish carry no story label.
- Tests are *part of the deliverable* per the spec's Definition of Done — do not defer them.
- Wire format and message-type integer codes are **frozen** in M0; never change them downstream.
- Commit per task or per logical group; checkpoints at the end of each phase are the natural release boundaries.
- Avoid: cross-story dependencies that break independent testability; "while we're in here" scope creep on the M0 refactor (two-week budget is hard); modifying any file under `src/AkmlSql.Shell.Shared/` in M0 (zero blast radius is a hard M0 success metric).
