---

description: "Task list for M0 Engine Transport Closure"
---

# Tasks: M0 Engine Transport Closure

**Input**: Design documents from `/specs/022-m0-engine-closure/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md
**Tests**: Targeted unit + smoke tests are part of the spec's acceptance scenarios (US1 calls out unit-test confirmation; US3 acceptance scenarios drive the per-handler smoke tests). Full TDD discipline is followed where the spec demands evidence — implementation tasks for those areas are paired with explicit "write failing test" / "run to verify pass" steps.

**Organization**: Tasks are grouped by user story (US1 → US4) so each story can be implemented, reviewed, and committed independently. The closure plan at `docs/superpowers/plans/2026-05-19-m0-engine-transport-closure.md` carries the detailed code snippets; this list is the executable surface.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Different file, no dependency on an incomplete task → safe to parallelise.
- **[Story]**: `US1`, `US2`, `US3`, `US4` (Setup and Polish phases carry no story label).
- Each task names an exact file path or a runnable command.

## Path Conventions

- **Engine source**: `src/AkmlSql.Engine/`
- **Engine tests**: `tests/AkmlSql.Engine.Tests/`
- **Shell projects (not modified by this closure)**: `src/AkmlSql.{Ssms20,Ssms21,Ssms22,VS2019,VS2022,VS2026}/`, `src/AkmlSql.Shell.Shared/`
- **Build prefix**: `MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the current branch tip is green before any closure work lands. No closure code is touched in this phase.

- [X] T001 Build the engine in Release mode: `dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release`. Expect zero errors. **Done 2026-05-19**: 0 errors, 13 pre-existing warnings.
- [X] T002 Run the engine test suite: `dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj -c Release`. Record any pre-existing skipped tests so they are not later blamed on the closure. **Done 2026-05-19**: 1002/1002 non-perf tests pass. Pre-existing flake on `PerformanceBaselineTests.Capture_or_compare_M0_baseline` (committed baseline is from a different machine; sub-2-ms latencies vary > 25 % across runs). US4 fixes this. US1 verification gate uses `--filter "FullyQualifiedName!~PerformanceBaselineTests"`.
- [X] T003 [P] Capture a fresh per-machine perf baseline by deleting `tests/AkmlSql.Engine.Tests/baselines/m0-baseline.json` and running `dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "Capture_or_compare_M0_baseline" -c Release`. Do not commit the regenerated baseline file — it is per-machine. **Done 2026-05-19**: workflow verified end-to-end; restored to committed state for US1.
- [X] T004 [P] Smoke-build SSMS 22 to establish the "shell-side unchanged" baseline: run `"$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Restore -p:Configuration=Release -v:quiet` then the same command with `-t:Build -v:minimal`. Expect "Build succeeded." with zero errors. **Done 2026-05-19**: builds clean with VS 18 Enterprise MSBuild at `C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe` (this machine has VS 18 preview, not the VS 2022 path the CLAUDE.md hint cites — use the VS 18 path for all subsequent MSBUILD invocations).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: None required. The architectural foundation (`IRpcTransport`, `IRpcRequestHandler<,>`, `RpcRouter`, `RpcContext`, three transports, `Handlers/*` folder structure, reflection-discovery, in-process matrix tests) already shipped via spec 021 M0 (PR #236, merged 2026-05-15). Closure work begins directly at Phase 3.

**Checkpoint**: Foundation is already in place; user stories may start in any order.

---

## Phase 3: User Story 1 — Single source of truth for engine settings (Priority: P1) 🎯 MVP

**Goal**: Move `_cachedSettings` off the named-pipe transport and onto `RpcContext` so every transport sees one cache; add `EnsureSettings()` / `InvalidateSettings()` as the only access pattern.

**Independent Test**: `grep -rn "_cachedSettings" src/AkmlSql.Engine/` returns exactly one source file (`RpcContext.cs`); `RpcContextTests` passes; the engine test suite remains green.

**Contract**: `specs/022-m0-engine-closure/contracts/rpc-context-settings.md`

### Tests for User Story 1 ⚠️

> Tests are written FIRST and must FAIL before T007's implementation lands.

- [X] T005 [P] [US1] Write failing test file `tests/AkmlSql.Engine.Tests/RpcContextTests.cs` with two `[Fact]`s: `EnsureSettings_loads_once_and_caches` (asserts loader called exactly once across two reads + `Assert.Same`) and `InvalidateSettings_forces_reload_on_next_call` (asserts loader called twice after one invalidation). Construct `RpcContext` with `required init` fields populated and a `SettingsLoader` lambda that increments a counter. **Done 2026-05-19**: file created.
- [X] T006 [US1] Run `dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "RpcContextTests" -c Release` and confirm both tests FAIL with compile errors referencing missing `SettingsLoader` / `EnsureSettings` / `InvalidateSettings` members. **Done 2026-05-19**: confirmed compile failures on the missing members.

### Implementation for User Story 1

- [X] T007 [US1] Implement the new surface in `src/AkmlSql.Engine/RpcContext.cs`: replace the body with the post-closure shape from `contracts/rpc-context-settings.md` — private `_cachedSettings` field, private `_settingsLock`, `required init Func<AppSettings> SettingsLoader`, `EnsureSettings()` (lock-protected lazy load), `InvalidateSettings()` (lock-protected drop). Remove the old `Settings { get; set; }` property. **Done 2026-05-19**: RpcContext.cs rewritten per contract.
- [X] T008 [US1] Re-run `dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "RpcContextTests" -c Release`. Both tests must PASS. **Done 2026-05-19**: both pass.
- [X] T009 [US1] Compile the engine: `dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release`. Expected: BUILD FAILS in `src/AkmlSql.Engine/Server/PipeRpcServer.Handlers.cs` referencing the removed `Settings` property. This is the trigger for T010–T013. **Done 2026-05-19**: confirmed build break at PipeRpcServer.Handlers.cs:92,102.
- [X] T010 [US1] Delete the `_cachedSettings` field declaration from `src/AkmlSql.Engine/Server/PipeRpcServer.cs:35`. **Done 2026-05-19**.
- [X] T011 [US1] Rewrite the `RpcContext` initialiser in `src/AkmlSql.Engine/Server/PipeRpcServer.Handlers.cs:33-41` to inject `SettingsLoader = Core.Config.ConfigManager.Load` instead of `Settings = _cachedSettings`. Remove the `_cachedSettings` reference. **Done 2026-05-19**.
- [X] T012 [US1] Rewrite the `CompletionHandler` registration closure in `src/AkmlSql.Engine/Server/PipeRpcServer.Handlers.cs:43-52` so the callback returns `_rpcContext.EnsureSettings()` instead of mutating `_cachedSettings` / `_rpcContext.Settings`. **Done 2026-05-19**: closure now `() => _rpcContext.EnsureSettings()`.
- [X] T013 [US1] Rewrite the `AnalysisHandler` registration closure in `src/AkmlSql.Engine/Server/PipeRpcServer.Handlers.cs:87-94` the same way: callback returns `_rpcContext.EnsureSettings()`. **Done 2026-05-19**.
- [X] T014 [US1] Rewrite the `AnalysisSettingsChanged` callback in `src/AkmlSql.Engine/Server/PipeRpcServer.Handlers.cs:98-104` to replace `_cachedSettings = null; _rpcContext.Settings = null;` with `_rpcContext.InvalidateSettings();`. Keep the `_aiHandler.RefreshSettings()` call for now (it is deleted in T044 once US3 lands). **Done 2026-05-19**.
- [X] T015 [US1] Build + run the full test suite: `dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release && dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj -c Release`. Both must succeed. **Done 2026-05-19**: 1016/1016 non-perf tests pass (perf-baseline test excluded — pre-existing flake, US4 fixes). Test suite required updating 7 existing test fixtures to satisfy the new `required init SettingsLoader` member (CompletionHandlerTests, AnalysisHandlersTests, ControlAndSchemaHandlersTests, HandshakeHandlerTests, FormattingHandlersTests, InProcessRoundTripTests, AllMessageTypesInProcessTests).
- [X] T016 [US1] Verify the sole-owner invariant: `grep -rn "_cachedSettings" src/AkmlSql.Engine/` must return exactly one path (`RpcContext.cs`). **Done 2026-05-19**: grep returns exactly one file.

**Checkpoint**: User Story 1 complete. Run the FR-019 invariant check: `git diff --name-only master -- src/AkmlSql.Shell.Shared/ src/AkmlSql.Ssms20/ src/AkmlSql.Ssms21/ src/AkmlSql.Ssms22/ src/AkmlSql.VS2019/ src/AkmlSql.VS2022/ src/AkmlSql.VS2026/` and confirm the output is empty. Then stop and inform the user: "Gap 1 (`_cachedSettings` move) complete. Ready to commit?" — do not stage or commit without explicit approval.

---

## Phase 4: User Story 2 — Clean named-pipe transport file as a reference shape (Priority: P2)

**Goal**: Extract service construction + handler registration out of the named-pipe transport into `EngineComposition` + `EngineHandlerRegistry`; add `RpcRouter.RegisterRaw` for delegating handlers; rename `Server/PipeRpcServer.cs` → `Transports/NamedPipeTransport.cs`; trim the file to ≤ 150 LOC.

**Independent Test**: `wc -l src/AkmlSql.Engine/Transports/NamedPipeTransport.cs` returns ≤ 150; `PipeRoundTripTests` + `AllMessageTypesInProcessTests` both pass; bytes returned by every message type are unchanged.

**Contracts**: `specs/022-m0-engine-closure/contracts/rpc-router-raw-handler.md`, plan §"Project Structure".

### Implementation for User Story 2

- [ ] T017 [P] [US2] Create `src/AkmlSql.Engine/EngineComposition.cs` with a public sealed class and `static EngineComposition Build()` factory. Build `SessionManager`, `TsqlParserService`, `SchemaCacheManager`, `SchemaMetadataService`; construct `RpcContext { SettingsLoader = ConfigManager.Load, ... }`; construct `RpcRouter`; instantiate `EngineHandlerRegistry(ctx)` and call `RegisterAllHandlers(router)`; build the `HistoryRetentionService`. Return `{ Context, Router, HistoryRetention }`.
- [ ] T018 [US2] Create `src/AkmlSql.Engine/EngineHandlerRegistry.cs` as `internal sealed class`. Body of `RegisterAllHandlers(RpcRouter router)` is a verbatim port of the registration block currently at `src/AkmlSql.Engine/Server/PipeRpcServer.Handlers.cs:19–352` with three mechanical changes: (a) replace `_field` references with locals constructed inside the method, (b) replace `_pluggableHandlers[X] = new TypedHandlerAdapter<...>(...)` with `router.Register(handler)`, (c) leave the `DelegatingMessageHandler` lines untouched for now — they migrate to `RegisterRaw` in T020 after T019 lands.
- [ ] T019 [US2] Add the `RegisterRaw` overload to `src/AkmlSql.Engine/RpcRouter.cs` per `contracts/rpc-router-raw-handler.md`: public method `void RegisterRaw(int messageType, Func<RpcMessage, CancellationToken, Task<RpcMessage?>> handler)`; private `RawHandlerAdapter` implementing `IHandlerAdapter`; duplicate-key throw matches `Register<,>`.
- [ ] T020 [US2] In `src/AkmlSql.Engine/EngineHandlerRegistry.cs`, replace every `new DelegatingMessageHandler((msg, ct) => ...)` registration with `router.RegisterRaw(MessageTypes.X, (msg, ct) => ...)`. Cover: `SessionSave`, `SessionRestore`, `SessionDelete`, `SafetyCheck`, `HistoryRecord`, `HistorySearch`, `HistoryAction`, `StatementBoundary`, `DocumentOutline`, `GetObjectDefinition`, `FindReferences`, `ObjectSearch`, `CrudGeneration`, `ScriptAs`, `GridExport`, `AiStreamCancel`, the 8 AI bridge entries.
- [ ] T021 [US2] Run `dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "AllMessageTypesInProcess" -c Release` and confirm the matrix test still sees every shell-to-engine message type as registered. The router replaces `PipeRpcServer.RegisteredMessageTypeCodes` as the source of truth.
- [ ] T022 [US2] Rewrite `DispatchAsync` in `src/AkmlSql.Engine/Server/PipeRpcServer.cs` (lines 219–305) as: `var response = await _router.RouteAsync(message, _ctx, ct); if (response == null && !_router.IsRegistered(message.MessageType)) Log.Warning(...); return response;` wrapped in the existing try/catch that produces an error envelope. Drop the `_pluggableHandlers` field, `DispatchPluggableAsync` helper, `RegisterPluggableHandlers` partial method, and `RegisteredMessageTypeCodes` accessor.
- [ ] T023 [US2] Update the `PipeRpcServer` constructor signature in `src/AkmlSql.Engine/Server/PipeRpcServer.cs:84-161` to `public PipeRpcServer(string pipeName, RpcContext ctx, RpcRouter router)`. Store the three fields; delete every service-init line (the constructor body shrinks from ~80 lines to ~5).
- [ ] T024 [US2] Update `src/AkmlSql.Engine/EngineHost.cs:87-94` to wire the new flow: `var composition = EngineComposition.Build(); var server = new PipeRpcServer(pipeName, composition.Context, composition.Router); await server.RunAsync(token);`.
- [ ] T025 [US2] Delete `src/AkmlSql.Engine/Server/PipeRpcServer.Handlers.cs` — its content lives in `EngineHandlerRegistry.cs` after T018+T020.
- [ ] T026 [US2] Build + test: `dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release && dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj -c Release`. Both pass. Update any test reference to `PipeRpcServer.RegisteredMessageTypeCodes` to use `RpcRouter.RegisteredMessageTypes` instead.
- [ ] T027 [US2] Rename `src/AkmlSql.Engine/Server/PipeRpcServer.cs` → `src/AkmlSql.Engine/Transports/NamedPipeTransport.cs`. In a single edit: move the file, change `namespace AkmlSql.Engine.Server` → `namespace AkmlSql.Engine.Transports`, rename class `PipeRpcServer` → `NamedPipeTransport`. Make the class implement `IRpcTransport` (add `event Func<RpcMessage, CancellationToken, Task<RpcMessage?>>? RequestReceived`; expose `Task StartAsync(CancellationToken ct)` that drives the existing accept loop; preserve `RunAsync` for `EngineHost` compatibility).
- [ ] T028 [US2] Find and update every reference to `PipeRpcServer` outside the renamed file: `grep -rln "PipeRpcServer" src/ tests/`. Expected hits: `src/AkmlSql.Engine/EngineHost.cs`, `src/AkmlSql.Engine/EngineComposition.cs`, `tests/AkmlSql.Engine.Tests/Transports/PipeRoundTripTests.cs`, `tests/AkmlSql.Engine.Tests/InProcess/AllMessageTypesInProcessTests.cs`. Doc-comment references inside other handler files may be updated opportunistically but are not load-bearing.
- [ ] T029 [US2] Build + test + measure LOC: `dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release && dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj -c Release && wc -l src/AkmlSql.Engine/Transports/NamedPipeTransport.cs`. Engine builds, tests pass, LOC reports ≤ 150.

**Checkpoint**: User Story 2 complete. Run the FR-019 invariant check: `git diff --name-only master -- src/AkmlSql.Shell.Shared/ src/AkmlSql.Ssms20/ src/AkmlSql.Ssms21/ src/AkmlSql.Ssms22/ src/AkmlSql.VS2019/ src/AkmlSql.VS2022/ src/AkmlSql.VS2026/` and confirm the output is empty. Then stop and inform the user: "Gap 2 (rename + LOC ≤150) complete. Ready to commit?"

---

## Phase 5: User Story 3 — Small focused handler class per AI message type (Priority: P3)

**Goal**: Extract `AiPipelineServices` and `AiHandlerBase`; migrate seven AI message handlers into per-message subclasses each ≤ 80 LOC; delete the 1896-LOC monolith and the bridge class.

**Independent Test**: `wc -l src/AkmlSql.Engine/Handlers/Ai/Ai*Handler.cs` reports ≤ 80 lines for every concrete handler; per-handler smoke tests pass; the matrix test still sees every AI message type registered; bytes returned for every AI message are unchanged.

**Contract**: `specs/022-m0-engine-closure/contracts/ai-handler-base.md`

### Tests for User Story 3 ⚠️

> The `AiHandlerBase` consent-gate test must fail before T031's implementation. Per-subclass smoke tests are written alongside each migration in T032–T038.

- [ ] T030 [P] [US3] Write failing test file `tests/AkmlSql.Engine.Tests/Handlers/Ai/AiHandlerBaseTests.cs` with two `[Fact]`s exercising a test-only `EchoHandler : AiHandlerBase<TestRequest, TestResponse>`: (a) `Local_provider_skips_consent_check_and_returns_response` against `Provider = "ollama"`, (b) `Cloud_provider_with_consent_required_throws_PrivacyConsentRequiredException` against `Provider = "anthropic"`. Construct `AiPipelineServices.Build(...)` with a stub schema-cache + parser.
- [ ] T031 [US3] Run the new tests; confirm they FAIL with compile errors (the base class and `AiPipelineServices` are not yet defined).

### Shared collaborator extraction

- [ ] T032 [US3] Create `src/AkmlSql.Engine/Ai/AiPipelineServices.cs` per `contracts/ai-handler-base.md` and `data-model.md` Entity 7: `public sealed class` with `required init` `SchemaContextBuilder SchemaContext`, `PrivacyTransformer Privacy`, `TsqlParserService Parser`, `Func<AiSettings> SettingsProvider`; static factory `Build(SchemaCacheManager, TsqlParserService, Func<AiSettings>)`; static helper `ExecuteWithBackoffAsync<T>(...)` lifted verbatim from the existing `AiRequestHandler` retry-with-backoff method (`AiRequestHandler.cs:~100`).

### Base class

- [ ] T033 [US3] Create `src/AkmlSql.Engine/Handlers/Ai/AiHandlerBase.cs` per `contracts/ai-handler-base.md`: abstract class `AiHandlerBase<TRequest, TResponse> : IRpcRequestHandler<TRequest, TResponse> where TResponse : new()`. Protected `Services` + `Log`. Abstract `RequestMessageType` / `ResponseMessageType`. Virtual `SwallowCancellation = true`. Abstract `protected Task<TResponse> InvokeAsync(TRequest, RpcContext, CancellationToken)`. Sealed `HandleAsync` runs `CheckPrivacyConsent(Services.SettingsProvider())` then `await InvokeAsync(...)` with the OCE-swallow guard. Private static `CheckPrivacyConsent` + `LocalProviders` set lifted from `AiRequestHandler.cs:78-91`.
- [ ] T034 [US3] Run `dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "AiHandlerBaseTests" -c Release`; both tests pass.

### Per-message handler migrations

> Each migration follows the same recipe: create the new subclass file, write a smoke test, delete the corresponding `HandleXxxAsync` method from `AiRequestHandler.cs`, replace the `Handlers.Ai.AiMessageHandler` registration line in `EngineHandlerRegistry.cs` with `router.Register(new AiXxxHandler(aiServices))`, verify `wc -l` reports ≤ 80 lines for the new file, build + run tests.

- [ ] T035 [US3] Migrate `AiTextToSql`: create `src/AkmlSql.Engine/Handlers/Ai/AiTextToSqlHandler.cs` (≤ 80 LOC, derived from `AiHandlerBase<AiTextToSqlRequest, AiTextToSqlResponse>`); write smoke test at `tests/AkmlSql.Engine.Tests/Handlers/Ai/AiTextToSqlHandlerTests.cs` covering local-provider happy path; delete `HandleTextToSqlAsync` from `src/AkmlSql.Engine/Ai/AiRequestHandler.cs` (line ~276); replace `_pluggableHandlers[MessageTypes.AiTextToSql] = new Handlers.Ai.AiMessageHandler(...)` in `EngineHandlerRegistry.cs` with `router.Register(new Handlers.Ai.AiTextToSqlHandler(aiServices))`; build + test.
- [ ] T036 [US3] Migrate `AiExplain`: same recipe → `AiExplainHandler.cs` + `AiExplainHandlerTests.cs`; delete `HandleExplainAsync` (`AiRequestHandler.cs:~480`); update registration; build + test.
- [ ] T037 [US3] Migrate `AiFix`: same recipe → `AiFixHandler.cs` + `AiFixHandlerTests.cs`; delete `HandleFixAsync` (`AiRequestHandler.cs:~701`); update registration; build + test.
- [ ] T038 [US3] Migrate `AiOptimize`: same recipe → `AiOptimizeHandler.cs` + `AiOptimizeHandlerTests.cs`; delete `HandleOptimizeAsync` (`AiRequestHandler.cs:~916`); update registration; build + test.
- [ ] T039 [US3] Migrate `AiIndexAnalysis`: same recipe → `AiIndexAnalysisHandler.cs` + `AiIndexAnalysisHandlerTests.cs`; delete `HandleIndexAnalysisAsync` (`AiRequestHandler.cs:~1084`); update registration; build + test.
- [ ] T040 [US3] Migrate `AiChat`: same recipe → `AiChatHandler.cs` + `AiChatHandlerTests.cs`; delete `HandleChatAsync` (`AiRequestHandler.cs:~1259`); update registration; build + test. Streaming-response contract is preserved by reusing the existing chat provider invocation pattern.
- [ ] T041 [US3] Migrate `AiGhostText`: same recipe → `AiGhostTextHandler.cs` + `AiGhostTextHandlerTests.cs`; delete `HandleGhostTextAsync` (`AiRequestHandler.cs:~1476`); update registration. Override `SwallowCancellation = true` is the default but the smoke test must explicitly cover a cancelled-request path returning an empty response.

### Monolith deletion + verification

- [ ] T042 [US3] Delete `src/AkmlSql.Engine/Ai/AiRequestHandler.cs` once all seven migrations land + every smoke test passes. The remaining `RefreshSettings()` and `LoadAiSettings()` helpers go with it — settings reach AI handlers through `Services.SettingsProvider()` now.
- [ ] T043 [US3] Delete `src/AkmlSql.Engine/Handlers/Ai/AiMessageHandlers.cs`. The legacy `AiMessageHandler` bridge class is no longer referenced by `EngineHandlerRegistry`.
- [ ] T044 [US3] Remove the `_aiHandler.RefreshSettings()` call from the `AnalysisSettingsChanged` callback in `EngineHandlerRegistry.cs` (left in place by T014; now harmless to drop since `AiRequestHandler` is gone). Also remove the `_aiHandler` field if `EngineHandlerRegistry` still holds one.
- [ ] T045 [US3] Verify the LOC budget: `wc -l src/AkmlSql.Engine/Handlers/Ai/Ai*Handler.cs`. Every concrete handler ≤ 80; `AiHandlerBase.cs` ≤ 100. If any file overshoots, extract per-prompt helpers into `AiPipelineServices` and re-measure.
- [ ] T046 [US3] Verify the structural invariants from `contracts/ai-handler-base.md`: `grep -L "AiHandlerBase" src/AkmlSql.Engine/Handlers/Ai/*.cs` returns at most the base file itself; `grep -rn "CheckPrivacyConsent" src/AkmlSql.Engine/` returns exactly two lines (definition + single call site in `AiHandlerBase.cs`). Run the full engine test suite.

**Checkpoint**: User Story 3 complete. Run the FR-019 invariant check: `git diff --name-only master -- src/AkmlSql.Shell.Shared/ src/AkmlSql.Ssms20/ src/AkmlSql.Ssms21/ src/AkmlSql.Ssms22/ src/AkmlSql.VS2019/ src/AkmlSql.VS2022/ src/AkmlSql.VS2026/` and confirm the output is empty. Then stop and inform the user: "Gap 3 (AiHandlerBase + 7 subclasses) complete. Ready to commit?"

---

## Phase 6: User Story 4 — Meaningful performance-regression gate (Priority: P4)

**Goal**: Scale the perf-baseline corpus, add a third workload (`BulkFormatRequest`), and set `MaxRegressionFraction = 0.05` so the gate detects real regressions instead of microbenchmark noise.

**Independent Test**: Three consecutive runs of the perf gate on unchanged code all pass at the 5 % threshold; a synthetic 10 % slowdown injected into the completion dispatch path fails the gate on the next run.

**Contract**: `specs/022-m0-engine-closure/contracts/performance-gate.md`

### Implementation for User Story 4

- [ ] T047 [P] [US4] Replace `CorpusSql` in `tests/AkmlSql.Engine.Tests/PerformanceBaselineTests.cs` with `BuildCorpus(repeats: 10)` where `BuildCorpus(int repeats)` emits the four representative statement blocks per `i`, each identifier suffixed `_b{i}` to defeat the parser's identifier cache. Per `contracts/performance-gate.md`: corpus must produce ≥ 300 statements and ≥ 30 KB of text.
- [ ] T048 [P] [US4] Add a third measurement method `MeasureBulkFormat()` in `PerformanceBaselineTests.cs` following the same shape as `MeasureFormat()` but invoking `BulkFormatHandler.HandleAsync` against the scaled corpus split into statements. Add a `BulkFormatRequest` field of type `BaselineSample` to the `BaselineDocument` record. Update `Capture_or_compare_M0_baseline` to measure + assert the new workload.
- [ ] T049 [US4] Set `MaxRegressionFraction = 0.05` (line 48 of `PerformanceBaselineTests.cs`). Update the surrounding comment to reflect the heavier-workload rationale; remove the old "sub-2 ms microbenchmark noise" justification.
- [ ] T050 [US4] Recapture the baseline: `rm -f tests/AkmlSql.Engine.Tests/baselines/m0-baseline.json` then `AKML_UPDATE_BASELINE=1 dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "Capture_or_compare_M0_baseline" -c Release`. Confirm every workload's captured p50 is ≥ 20 ms (≥ 30 ms for BulkFormat) per FR-015.
- [ ] T051 [US4] Verify the gate's stability with three back-to-back runs against unchanged code: `for i in 1 2 3; do dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "Capture_or_compare_M0_baseline" -c Release; done`. All three pass. If any flake, raise `MeasureIterations` from 50 → 200 in `PerformanceBaselineTests.cs` and re-verify — do NOT relax the threshold.

**Checkpoint**: User Story 4 complete. Run the FR-019 invariant check: `git diff --name-only master -- src/AkmlSql.Shell.Shared/ src/AkmlSql.Ssms20/ src/AkmlSql.Ssms21/ src/AkmlSql.Ssms22/ src/AkmlSql.VS2019/ src/AkmlSql.VS2022/ src/AkmlSql.VS2026/` and confirm the output is empty. Then stop and inform the user: "Gap 4 (perf gate tightened to 5%) complete. Ready to commit?"

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Documentation updates, six-host smoke verification, and the end-to-end quickstart walkthrough that confirms the entire closure.

- [ ] T052 [P] Update `doc/architecture.md` § 9b "Spec 021 — M0 Transport Abstraction" to reflect the post-closure shape: `EngineComposition.Build()` as composition root; `EngineHandlerRegistry` as registration surface; `NamedPipeTransport` as the renamed transport file. Add a closure footnote referencing `docs/superpowers/plans/2026-05-19-m0-engine-transport-closure.md`.
- [ ] T053 [P] Update `doc/ipc-api.md` § "Transport Plurality (spec 021 M0)": replace every `PipeRpcServer` mention with `NamedPipeTransport`. Wire format and message-type integer codes are unchanged — keep those sections intact.
- [ ] T054 [P] Append a "M0 Closure (2026-05-19)" section to `specs/021-web-edition/tasks.md` listing the four closed PRD gaps with `[X]` marks plus a link to this plan.
- [ ] T055 Build all six shell hosts to confirm the shells remain green: `for proj in Ssms20 Ssms21 Ssms22 VS2019 VS2022 VS2026; do "$MSBUILD" "src/AkmlSql.${proj}/AkmlSql.${proj}.csproj" -t:Restore -p:Configuration=Release -v:quiet; "$MSBUILD" "src/AkmlSql.${proj}/AkmlSql.${proj}.csproj" -t:Build -p:Configuration=Release -v:minimal || echo "FAIL: ${proj}"; done`. Expected: all six succeed. Any failure indicates accidental shell-side coupling — diagnose before merging.
- [ ] T056 Publish the engine to confirm it still produces a self-contained single-file output: `dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64`. Expected: `bin/Release/net10.0/win-x64/publish/AkmlSql.Engine.exe` exists.
- [ ] T057 Walk through `specs/022-m0-engine-closure/quickstart.md` end-to-end on a clean clone. Confirm every section's expected output matches. This is the final acceptance gate.
- [ ] T058 SC-005 synthetic-regression check: inject a temporary `Thread.SpinWait(50000);` into `CompletionEngine.GetCompletions` (roughly 10 % slowdown), re-run `dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "Capture_or_compare_M0_baseline" -c Release`, confirm the test FAILS with a message naming `CompletionRequest.p50` as the regressed metric, then revert the injected change. Validates that the tightened gate has teeth (`contracts/performance-gate.md` § "Synthetic-regression contract"). Do not commit the injected slowdown.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies. T003 and T004 are `[P]` and may run alongside T001/T002.
- **Foundational (Phase 2)**: None required.
- **User Story 1 (Phase 3)**: Begins after Setup completes. T005 may run in parallel with T003 / T004 if Setup tasks are still in flight, since RpcContext changes are independent of the perf baseline and shell builds.
- **User Story 2 (Phase 4)**: Easiest after US1 lands (uses `ctx.EnsureSettings()` via `EngineComposition`). Technically independent: if US2 lands first, the registration closures in T012/T013 use the old `_cachedSettings` field; US1's later landing then removes the field and updates the closures. Recommended order: US1 → US2.
- **User Story 3 (Phase 5)**: Independent of US1 and US2 in scope. T032 (`AiPipelineServices`) and T033 (`AiHandlerBase`) can land before or after US2; T035–T041's `EngineHandlerRegistry` edits assume US2's `EngineHandlerRegistry` exists. If US3 ships before US2, the same edits apply to `PipeRpcServer.Handlers.cs`. Recommended order: US1 → US2 → US3.
- **User Story 4 (Phase 6)**: Fully independent — touches only `tests/AkmlSql.Engine.Tests/PerformanceBaselineTests.cs`. T047 / T048 / T049 are marked `[P]` and may run in parallel with US1/US2/US3 by a different developer.
- **Polish (Phase 7)**: Depends on US1, US2, US3, and US4 being done.

### Within Each User Story

- **US1**: Tests (T005-T006) before implementation (T007); implementation (T007) before consumer migration (T010–T014); consumer migration before verification (T015–T016).
- **US2**: T017 + T018 (composition root + registry) before T019–T021 (router + registry edits); T022–T025 (transport simplification + handler-partial deletion) before T026 (test gate); T027–T029 (rename + reference updates + LOC check) last.
- **US3**: T030–T031 (failing tests) before T032 (services) + T033 (base) + T034 (passing tests); per-handler migrations T035–T041 happen sequentially (each commit boundary green); deletion T042–T044 only after all seven smoke tests pass; T045–T046 verification last.
- **US4**: T047 / T048 are file-disjoint enough to parallelise; T049 closes the threshold; T050 captures fresh baseline; T051 verifies stability.

### Cross-Story Coordination

- The `EngineHandlerRegistry.cs` file is touched by every user story (US1 modifies two registration closures, US2 creates the file, US3 edits 7+ registration lines). Implementation order avoids merge conflicts: if running US1 + US3 in parallel, US1 owns the lines around the Completion/Analysis closures and US3 owns the AI lines; they do not collide. US2 creates the file in the first place — concurrent US1/US3 work against the older `PipeRpcServer.Handlers.cs` then needs reconciliation when US2 lands.
- The closure plan recommends sequential `US1 → US2 → US3 → US4` execution to avoid that reconciliation cost.

### Parallel Opportunities

- **Within Setup**: T003 + T004 in parallel.
- **Within US1**: T005 is `[P]` and may run while T003 / T004 finish.
- **Within US2**: T017 + T018 are `[P]` — independent file creations.
- **Within US3**: T030 (failing test) `[P]` against T032 (services) — disjoint files.
- **Within US4**: T047 + T048 are `[P]` — the corpus generator and the BulkFormat method are independent edits within the same file but in different sections; if one developer prefers them as one commit, drop the `[P]` for one of the two.
- **Across stories**: US4 can be done by a second developer in parallel with US1+US2+US3.
- **Within Polish**: T052 + T053 + T054 are all `[P]` (three independent doc files).

---

## Parallel Example: User Story 4 (runnable alongside US1–US3 by a second developer)

```bash
# T047 (corpus) and T048 (BulkFormat measurement) — same file, different regions
git diff tests/AkmlSql.Engine.Tests/PerformanceBaselineTests.cs   # confirm minimal cross-edit risk
# Or do them sequentially in one commit for simplicity
```

## Parallel Example: Polish phase

```bash
# Three doc updates run in parallel
Task: "Update doc/architecture.md § 9b"
Task: "Update doc/ipc-api.md § Transport Plurality"
Task: "Append M0 Closure section to specs/021-web-edition/tasks.md"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Complete Phase 1 (Setup): T001–T004
2. Skip Phase 2 (Foundational: nothing needed)
3. Complete Phase 3 (US1): T005–T016
4. **STOP and VALIDATE**: `grep -rn "_cachedSettings" src/AkmlSql.Engine/` returns one path; `dotnet test ...` green; build six shells (T055 from Polish) to confirm no shell-side coupling crept in.
5. Inform the user, ask permission to commit.

### Incremental Delivery

1. Setup + US1 → commit → "Gap 1 closed"
2. US2 (T017–T029) → commit → "Gap 2 closed: NamedPipeTransport ≤ 150 LOC"
3. US3 (T030–T046) → commit per-handler (T035–T041 each its own commit) → "Gap 3 closed"
4. US4 (T047–T051) → commit → "Gap 4 closed: perf gate at 5 %"
5. Polish (T052–T057) → commit → ready for PR

### Parallel Team Strategy

- Developer A: US1 → US2 → US3 (sequential per closure plan)
- Developer B: US4 (independent, can run any time after Setup)
- Both join on Polish

---

## Notes

- `[P]` markers indicate disjoint files only; tasks that touch `EngineHandlerRegistry.cs` are NOT parallelisable across stories without merge coordination.
- Every checkpoint ends at "ready to commit" — no task in this list stages or commits. `CLAUDE.md` git policy is binding.
- Per-handler migrations in US3 (T035–T041) are intentionally separate commit units. The closure plan calls these "atomic units of mechanical work" — keeping them as separate commits gives reviewers per-handler diffs.
- Performance-gate flake at the 5 % threshold is resolved by raising iteration count, not by relaxing the threshold (FR-016 + `contracts/performance-gate.md` invariant 4).
- The detailed code snippets each task needs live in `docs/superpowers/plans/2026-05-19-m0-engine-transport-closure.md`. Cross-reference that file when an LLM executes a task without enough local context.
