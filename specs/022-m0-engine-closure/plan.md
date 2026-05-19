# Implementation Plan: M0 Engine Transport Closure

**Branch**: `022-m0-engine-closure` | **Date**: 2026-05-19 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/022-m0-engine-closure/spec.md`

## Summary

Close the four PRD success metrics from spec 021 (web edition) M0 that were explicitly deferred when PR #236 merged to master on 2026-05-15. Every architectural piece M0 introduced — `IRpcTransport`, `IRpcRequestHandler<,>`, `RpcRouter`, `RpcContext`, three transports (named-pipe, in-process, WebSocket), the `Handlers/*` folder structure, reflection-based handler discovery, and the in-process matrix tests — already exists in production code. This closure finishes the polish: collapse the dual-owned `_cachedSettings` field down to a single owner on the shared request context (P1); rename the named-pipe transport file and extract its composition root so the file is ≤ 150 lines (P2); split the 1896-LOC AI dispatcher into an abstract base plus seven concrete per-message subclasses, each ≤ 80 lines (P3); replace the 25 % regression-detection threshold with a 5 % gate backed by heavier-workload corpora (P4).

**Technical approach** (consolidated from `docs/superpowers/plans/2026-05-19-m0-engine-transport-closure.md` and the user's gap-scope confirmation on 2026-05-19):

1. **P1 — Settings cache** Add `EnsureSettings()` + `InvalidateSettings()` methods to `RpcContext`; carry the on-disk loader as an injected `Func<AppSettings>`. Remove the `_cachedSettings` field from the named-pipe transport. Migrate the two registration sites in `PipeRpcServer.Handlers.cs` (Completion + Analysis) and the `AnalysisSettingsChanged` invalidation callback to go through the context.
2. **P2 — Transport file shape** Extract the constructor body and partial-handler-registration block from `PipeRpcServer` into a new `EngineComposition` (root) + `EngineHandlerRegistry` (registration block). Add `RpcRouter.RegisterRaw(...)` so delegating handlers (session, history, productivity, navigation, the AI bridge) register without forcing a typed contract. Rewrite the dispatch loop to delegate to `RpcRouter.RouteAsync`. Rename the file `Server/PipeRpcServer.cs` → `Transports/NamedPipeTransport.cs` and update every reference. Confirm `wc -l` reports ≤ 150 LOC for the renamed file.
3. **P3 — AI handler split** Extract the cross-handler boilerplate (privacy-consent check, retry-with-backoff, settings retrieval, error-envelope construction) into a new abstract `AiHandlerBase<TRequest, TResponse>` under `Handlers/Ai/`. Migrate each of the seven `HandleXxxAsync` methods inside `AiRequestHandler.cs` into its own concrete subclass (`AiTextToSqlHandler`, `AiExplainHandler`, `AiFixHandler`, `AiOptimizeHandler`, `AiIndexAnalysisHandler`, `AiChatHandler`, `AiGhostTextHandler`); register them directly with the router; verify each file is ≤ 80 LOC. Delete the monolithic `AiRequestHandler.cs` and the `Handlers/Ai/AiMessageHandlers.cs` bridge once the migration completes.
4. **P4 — Performance gate** Scale the `PerformanceBaselineTests.cs` corpus by ~10× so the measured operations cross the 20 ms threshold; add `BulkFormatRequest` as a third measured workload; set `MaxRegressionFraction = 0.05`. Confirm three consecutive runs at the new threshold all pass on a clean baseline.

**Cross-cutting**: the on-wire frame format and integer message-type codes are unchanged. No file under `src/AkmlSql.Shell.Shared/` or `src/AkmlSql.{Ssms,VS}*/` is touched. The reflection-based handler discovery path (`RpcRouter.RegisterAllInAssembly`) remains additive and silent-skip — no new requirement on handler constructors.

## Technical Context

**Language/Version**: C# / .NET 10 for the engine (`net10.0`, win-x64, self-contained, trimmed). Test assemblies on `net10.0` with xUnit 2.x. No shell-side code (`net472`) is modified.

**Primary Dependencies**: Existing only — `MessagePack-CSharp` (wire serialisation), `Serilog 4.x` (logging), `System.IO.Pipes` (named-pipe transport), `System.Net.WebSockets` + `System.Net.HttpListener` (WebSocket transport, already shipped in M3 work). No new external packages.

**Storage**: N/A. The closure does not introduce or modify any persistent data. Settings continue to load from `%AppData%/AKML SQL/config.json` via the existing `ConfigManager.Load()`. Performance baselines live per-machine at `tests/AkmlSql.Engine.Tests/baselines/m0-baseline.json` (gitignored convention from T006).

**Testing**: `xUnit` for unit tests under `tests/AkmlSql.Engine.Tests/`; existing perf gate at `PerformanceBaselineTests.cs`; existing in-process matrix test at `AllMessageTypesInProcessTests.cs`; existing named-pipe round-trip test at `Transports/PipeRoundTripTests.cs`. New tests added: `RpcContextTests.cs` (P1), `Handlers/Ai/AiHandlerBaseTests.cs` (P3), one smoke test per AI subclass (P3). All other test files remain untouched.

**Target Platform**: Windows x64 (.NET 10 engine runtime). The closure does not change platform support.

**Project Type**: Internal refactor of the existing engine library. No new top-level project.

**Performance Goals**:
- Completion-dispatch p50 within 5 % of the post-closure baseline (FR-014)
- Format-dispatch p50 within 5 % of the post-closure baseline (FR-014)
- Bulk-format pipeline-run p50 within 5 % of the post-closure baseline (FR-014, new measurement)
- Every measured workload's p50 ≥ 20 ms so the 5 % threshold has signal (FR-015)
- Three consecutive runs on the same machine with unchanged code all pass (FR-016)

**Constraints**:
- Wire format unchanged (FR-017): no change to `[length][CRC][MessagePack(RpcMessage)]` envelope or any integer message-type code.
- Shell-side untouched (FR-019): zero modifications under `src/AkmlSql.Shell.Shared/` and the six shell project folders.
- Test suite green at every commit boundary (FR-022): no skipped or quarantined tests added.
- LOC budgets: named-pipe transport ≤ 150 (FR-006); concrete AI handlers ≤ 80 (FR-012).
- Closure must not break the reflection-discovery silent-skip property (Edge Case 4) — handlers with constructor dependencies remain explicitly registered.

**Scale/Scope**:
- ~50 existing message-type integer codes — must continue to dispatch identically through every transport.
- 4 user stories, 22 functional requirements, 10 success criteria, 7 edge cases.
- Estimated effort: half a day for P1, one day for P2, two days for P3, one day for P4 — four developer-days total when run sequentially.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

`.specify/memory/constitution.md` does not exist in this repository. The gate is therefore advisory only, following the same pattern as spec 021's plan.

Applying common engineering gates by inspection:

| Gate | Result | Notes |
|------|--------|-------|
| Refactor scope justified by concrete cost | **PASS** | Each gap maps to a specific PRD success metric with a measurable acceptance bar (LOC, percentage, identity-of-bytes). |
| No new technology introduced | **PASS** | All work uses dependencies already shipped in spec 021. No new NuGet, no new external lib, no new runtime target. |
| Wire-compatible with existing IDE plugin engine | **PASS** | FR-017, FR-018, FR-020 explicitly forbid wire change. Verified by the existing matrix test + the named-pipe round-trip test. |
| Independence from IDE plugins | **PASS** | FR-019 forbids touching shell sources. Verified by Task 14 in the closure implementation plan (all six shell hosts must build unchanged). |
| Test coverage maintained or expanded | **PASS** | FR-022 forbids test-suite regression; P1/P3 add targeted unit tests; P4 strengthens the existing perf gate. |
| Public-API stability preserved | **PASS** | `IRpcTransport`, `IRpcRequestHandler<,>`, `RpcRouter`, `RpcContext` remain public with the same signatures. P1 adds two new methods on `RpcContext`; P2 adds one new method on `RpcRouter` (`RegisterRaw`); both are additive. |
| No premature abstraction | **PASS** | `EngineComposition` and `EngineHandlerRegistry` extract existing code into named places; they do not introduce new abstractions. `AiHandlerBase` factors out boilerplate that already exists inlined seven times. |

No violations to track in **Complexity Tracking**.

## Project Structure

### Documentation (this feature)

```text
specs/022-m0-engine-closure/
├── plan.md                           # this file
├── spec.md                           # produced by /speckit.specify
├── checklists/
│   └── requirements.md               # produced by /speckit.specify
├── research.md                       # Phase 0 output (this command)
├── data-model.md                     # Phase 1 output (this command)
├── quickstart.md                     # Phase 1 output (this command)
├── contracts/
│   ├── rpc-context-settings.md       # EnsureSettings / InvalidateSettings + SettingsLoader contract
│   ├── rpc-router-raw-handler.md     # RpcRouter.RegisterRaw additive overload
│   ├── ai-handler-base.md            # AiHandlerBase<TReq,TResp> template-method contract
│   └── performance-gate.md           # workload sizing + threshold + sample-count requirements
└── tasks.md                          # produced by /speckit.tasks (next command)
```

### Source Code (repository root)

The closure works inside the existing engine project. No new top-level structure. The diff is narrow:

```text
src/
├── AkmlSql.Engine/
│   ├── EngineComposition.cs                    # NEW (P2) — composition root
│   ├── EngineHandlerRegistry.cs                # NEW (P2) — registration block lifted out of PipeRpcServer.Handlers.cs
│   ├── EngineHost.cs                           # MODIFIED (P2) — wires composition root to NamedPipeTransport
│   ├── RpcContext.cs                           # MODIFIED (P1) — sole owner of _cachedSettings; EnsureSettings / InvalidateSettings
│   ├── RpcRouter.cs                            # MODIFIED (P2) — adds RegisterRaw overload
│   ├── Server/
│   │   ├── PipeRpcServer.cs                    # DELETED (P2) — replaced by Transports/NamedPipeTransport.cs
│   │   ├── PipeRpcServer.Handlers.cs           # DELETED (P2) — content moves to EngineHandlerRegistry.cs
│   │   ├── DelegatingHandlerAdapter.cs         # NEW (P2) — bridges IMessageHandler into IRpcRequestHandler<,> via RegisterRaw
│   │   ├── DelegatingMessageHandler.cs         # MODIFIED (P2) — kept for now; future tasks may delete after RegisterRaw migration is verified
│   │   ├── IMessageHandler.cs                  # UNCHANGED — still serves the legacy raw-message contract
│   │   ├── TypedHandlerAdapter.cs              # UNCHANGED — RpcRouter still uses it internally
│   │   └── SessionManager.cs                   # UNCHANGED
│   ├── Transports/
│   │   ├── IRpcTransport.cs                    # UNCHANGED
│   │   ├── IRpcRequestHandler.cs               # UNCHANGED
│   │   ├── InProcessTransport.cs               # UNCHANGED
│   │   ├── NamedPipeTransport.cs               # NEW (P2) — renamed from Server/PipeRpcServer.cs; ≤ 150 LOC; frame I/O + accept loop + dispatch hand-off only
│   │   ├── WebSocketTransport.cs               # UNCHANGED
│   │   └── WebSocketTransportOptions.cs        # UNCHANGED
│   ├── Handlers/
│   │   └── Ai/
│   │       ├── AiHandlerBase.cs                # NEW (P3) — abstract base
│   │       ├── AiTextToSqlHandler.cs           # NEW (P3) — ≤ 80 LOC
│   │       ├── AiExplainHandler.cs             # NEW (P3) — ≤ 80 LOC
│   │       ├── AiFixHandler.cs                 # NEW (P3) — ≤ 80 LOC
│   │       ├── AiOptimizeHandler.cs            # NEW (P3) — ≤ 80 LOC
│   │       ├── AiIndexAnalysisHandler.cs       # NEW (P3) — ≤ 80 LOC
│   │       ├── AiChatHandler.cs                # NEW (P3) — ≤ 80 LOC
│   │       ├── AiGhostTextHandler.cs           # NEW (P3) — ≤ 80 LOC
│   │       └── AiMessageHandlers.cs            # DELETED (P3) — bridge no longer used
│   ├── Ai/
│   │   ├── AiPipelineServices.cs               # NEW (P3) — shared collaborators (schema-context builder, privacy transformer, prompt builder, provider router) carved out of AiRequestHandler
│   │   └── AiRequestHandler.cs                 # DELETED (P3) — content split into AiHandlerBase, the seven subclasses, and AiPipelineServices
│   └── (everything else unchanged)
│
tests/
└── AkmlSql.Engine.Tests/
    ├── RpcContextTests.cs                      # NEW (P1) — EnsureSettings / InvalidateSettings
    ├── Handlers/
    │   └── Ai/
    │       ├── AiHandlerBaseTests.cs           # NEW (P3) — privacy-consent + retry + error envelope via a test-only subclass
    │       ├── AiTextToSqlHandlerTests.cs      # NEW (P3) — smoke test
    │       ├── AiExplainHandlerTests.cs        # NEW (P3) — smoke test
    │       ├── AiFixHandlerTests.cs            # NEW (P3) — smoke test
    │       ├── AiOptimizeHandlerTests.cs       # NEW (P3) — smoke test
    │       ├── AiIndexAnalysisHandlerTests.cs  # NEW (P3) — smoke test
    │       ├── AiChatHandlerTests.cs           # NEW (P3) — smoke test
    │       └── AiGhostTextHandlerTests.cs      # NEW (P3) — smoke test
    ├── PerformanceBaselineTests.cs             # MODIFIED (P4) — 10× corpus, BulkFormat measurement, MaxRegressionFraction = 0.05
    ├── InProcess/
    │   └── AllMessageTypesInProcessTests.cs    # MODIFIED (P2 verification) — references RpcRouter.RegisteredMessageTypes instead of PipeRpcServer.RegisteredMessageTypeCodes
    ├── Transports/
    │   └── PipeRoundTripTests.cs               # MODIFIED (P2) — updates class-name references
    └── (everything else unchanged)
```

**Structure Decision**: The existing single-tree engine layout already accommodates this work without restructuring. The closure adds three new top-level engine source files (`EngineComposition.cs`, `EngineHandlerRegistry.cs`, `AiPipelineServices.cs`), one new transport file (`Transports/NamedPipeTransport.cs` replacing `Server/PipeRpcServer.cs`), eight new AI-handler files under `Handlers/Ai/`, and seven new test files under `tests/AkmlSql.Engine.Tests/`. Four existing files are deleted (`Server/PipeRpcServer.cs`, `Server/PipeRpcServer.Handlers.cs`, `Ai/AiRequestHandler.cs`, `Handlers/Ai/AiMessageHandlers.cs`). Five existing files are modified (`RpcContext.cs`, `RpcRouter.cs`, `EngineHost.cs`, `PerformanceBaselineTests.cs`, plus the two test files that reference the renamed transport class). No file outside the engine project and engine test project is touched.

## Complexity Tracking

> Fill ONLY if Constitution Check has violations that must be justified.

No constitution violations. The closure removes complexity (1896-LOC monolith → 8 small classes; 354 + 353 LOC transport-plus-partial → 150-LOC transport + 350-LOC registry) without introducing new abstractions beyond two named places that already existed as informal code regions.
