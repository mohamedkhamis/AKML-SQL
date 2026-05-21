# Phase 0 — Research: M0 Engine Transport Closure

The spec contains **zero `[NEEDS CLARIFICATION]` markers**. The four PRD success metrics it closes are each measurable on their own. Research here is therefore not about resolving open questions — it is about documenting the concrete design decisions adopted for each gap so a future maintainer can trace the rationale without re-deriving it from the closure implementation plan.

## Decision 1: Sole-owner shape for `_cachedSettings`

**Decision**: Move the settings cache off `PipeRpcServer.cs` (where it lives today as a field) onto `RpcContext` as a private field with thread-safe `EnsureSettings()` / `InvalidateSettings()` accessors. The on-disk loader is injected as `Func<AppSettings>` through a required-init property `SettingsLoader`, defaulted to `ConfigManager.Load` by the composition root.

**Rationale**: The PRD's spec-021 T009 commit explicitly created the `RpcContext` structure but deferred the actual field move. This closure finishes the work. Putting the cache on the shared context means every transport (named-pipe, in-process, WebSocket) sees the same cache without a per-transport field — and the `AnalysisSettingsChanged` handler invalidates one place rather than two. Threading: a `lock` on a private object protects the cached reference; reads are infrequent (only across settings changes), so the lock contention cost is negligible.

**Alternatives considered**:
- *Settings as a lazy `Lazy<AppSettings>` property* — rejected because invalidation would require swapping the `Lazy<>` instance, which can race with concurrent `EnsureSettings()` callers reading the old `Lazy`.
- *Settings as an immutable record passed through every handler call* — rejected because the seven AI handlers and the AnalysisSettingsChanged path all currently mutate cache state on a notification; rewriting the notification flow to thread immutable state through would balloon the closure scope.
- *Settings exposed as a `Settings { get; set; }` property as today, but documented as "only the composition root may set it"* — rejected because the property is currently mutated from three call sites (the two registration callbacks plus the AnalysisSettingsChanged handler), and trusting future maintainers to obey a comment is fragile.

## Decision 2: Adding `RpcRouter.RegisterRaw` for delegating handlers

**Decision**: Add a non-generic `RpcRouter.RegisterRaw(int messageType, Func<RpcMessage, CancellationToken, Task<RpcMessage?>> handler)` overload that bypasses MessagePack deserialise/serialise on both sides. Use this to register the existing delegating handlers (session save/restore/delete, history record/search/action, productivity statement-boundary/document-outline, navigation get-definition/find-references/object-search, CRUD generation, ScriptAs, grid export, AI message bridge, AiStreamCancel notification).

**Rationale**: Those handlers process the raw `RpcMessage` themselves and produce a response envelope directly — they would gain nothing from being forced into the typed `IRpcRequestHandler<TReq, TResp>` contract because the typed contract's MessagePack serialise/deserialise loop would re-pack a response that is already packed. The router gains a small adapter (`RawHandlerAdapter` inside `RpcRouter`) that calls the function directly. The new overload is additive: existing typed handlers continue through `Register<TReq, TResp>`.

**Alternatives considered**:
- *Refactor every delegating handler to implement `IRpcRequestHandler<,>`* — rejected because the seven legacy delegating handler classes already have their own deserialisation logic; rewriting them is a much larger change than the closure intends. Some (history search) return polymorphic response shapes that don't fit the strict `TResponse` generic.
- *Keep the existing `_pluggableHandlers` dictionary on the transport* — rejected because that is the very coupling the closure is removing. The router is the single dispatch surface post-closure.
- *Make `IMessageHandler` extend `IRpcRequestHandler<RpcMessage, RpcMessage>`* — rejected because the bidirectional MessagePack pass would still apply through the typed adapter; the raw-overload is the only way to truly bypass it.

## Decision 3: `AiHandlerBase<TRequest, TResponse>` template shape

**Decision**: Define `AiHandlerBase<TRequest, TResponse>` as an abstract class implementing `IRpcRequestHandler<TRequest, TResponse>` with a `protected abstract Task<TResponse> InvokeAsync(TRequest, RpcContext, CancellationToken)` template method. The base owns: privacy-consent check (local-provider allowlist + cloud-provider consent gate), `SwallowCancellation = true` default, and standardised error-envelope construction via try/catch around `InvokeAsync`. Each concrete handler overrides `InvokeAsync` plus the two integer-code properties. The retry-with-backoff helper stays available via an `AiPipelineServices.ExecuteWithBackoffAsync<T>(...)` static method that subclasses opt into per-call when they invoke a provider.

**Rationale**: The current monolith (1896 LOC) inlines the consent-check at the top of every public `HandleXxxAsync` method and the try/catch / error-envelope at the bottom. Lifting these into a base means each subclass body is just "build prompt → invoke provider → shape response" — the ≤ 80 LOC budget is achievable.

The retry helper stays opt-in (rather than wrapping `InvokeAsync` unconditionally) because not every subclass should retry: e.g. `AiGhostTextHandler` is latency-sensitive and prefers fast failure over silent backoff. Making retry per-call preserves the existing per-method behaviour.

**Alternatives considered**:
- *Wrap retry around `InvokeAsync` in the base* — rejected: would change behaviour for handlers that today choose not to retry. Closure must be behaviour-preserving on the wire (FR-020).
- *Non-generic base with `RpcMessage` as the boundary type* — rejected because that removes the typed-request guarantee that `IRpcRequestHandler<,>` provides and pushes deserialise logic back into every subclass.
- *Static helper methods instead of a base class* — rejected because the consent gate must run before the per-message body; the easiest way to enforce that ordering is the template-method pattern.

## Decision 4: Perf-gate workload shape

**Decision**: Replace the current ~30-statement perf corpus (~ 750 bytes) with a 300-statement variant produced by repeating the four representative blocks 10× with renamed identifiers. Add `BulkFormatRequest` as a third measured workload — the bulk-format pipeline runs the 7-stage formatter on every statement boundary in the document, so its p50 sits comfortably in the 30–150 ms range. Set `MaxRegressionFraction = 0.05`. Keep `MeasureIterations = 50` and `Trials = 5` initially; raise to 200 / 5 only if a clean run flakes at the 5 % boundary.

**Rationale**: The current perf gate fails to discriminate real regressions because the measured p50 (sub-2 ms per call) is dominated by JIT and L1-cache jitter. Scaling the corpus pushes the dispatch path into a regime where 5 % equals at least 1 ms — well above per-trial noise. Adding `BulkFormat` covers the formatter pipeline's real-world hot path (the bulk format is the most expensive engine operation in normal use) and gives the gate teeth for the dispatch surface the closure refactors.

**Identifier renaming**: blocks are emitted as `-- block {0}` with each identifier suffixed by `_b{0}` so the T-SQL parser cannot reuse a cached AST node. This forces an honest per-block re-parse, which is what production workloads encounter.

**Alternatives considered**:
- *Raise `MeasureIterations` to 1000 without changing corpus size* — rejected: sub-2-ms calls still leave ~30 % variance across 5 trials' minimum-p50 readings; iterations alone cannot rescue the signal.
- *Replace the corpus with a single 50 KLOC SQL file* — rejected: the harness times the formatter call as a whole; a single huge call's p50 is the call's runtime, which makes it impossible to attribute regressions to dispatch vs. formatter stages. Many smaller statements gives the gate per-statement resolution.
- *Trim to 3 % threshold* — rejected: 3 % is at the edge of trial-to-trial repeatability even on a quiet desktop; the gate would flake. 5 % is the PRD's target.

## Decision 5: Reflection-discovery and silent-skip behaviour

**Decision**: `RpcRouter.RegisterAllInAssembly(...)` continues to silently skip handler classes whose constructor it cannot satisfy. Closure adds zero handlers that match the reflection path's parameterless-ctor requirement — every new handler (the seven AI subclasses, the new `AiHandlerBase`-derived smoke-test subclass) takes `AiPipelineServices` as a constructor argument, which is registered explicitly through `EngineHandlerRegistry`.

**Rationale**: The reflection path is documented (in `RpcRouter.cs:53–69`) as an additive convenience for handlers with no dependencies; the explicit registry remains the source of truth. The closure does not change this contract.

**Alternatives considered**:
- *Extend reflection-discovery to support constructor-DI via a `Func<Type, object>` factory* — rejected: out of scope. Spec FR-007 only requires the composition root to register every handler; reflection is not mandated.

## Decision 6: Atomic rename strategy for the transport file

**Decision**: Rename in three sub-steps inside one commit:
1. `git mv src/AkmlSql.Engine/Server/PipeRpcServer.cs src/AkmlSql.Engine/Transports/NamedPipeTransport.cs`
2. In-file rename: namespace `AkmlSql.Engine.Server` → `AkmlSql.Engine.Transports`; class `PipeRpcServer` → `NamedPipeTransport`; constructor signature reduced to `(string pipeName, RpcContext ctx, RpcRouter router)`.
3. Update every caller: `EngineHost.cs`, `EngineComposition.cs`, two test files. The build is broken between (1) and (3); the closure plan's Task 6 chains them inside one execution so no intermediate commit lands a broken state (Edge Case 5).

**Rationale**: Multi-commit renames have caused build breakage in past spec work. One-shot rename in a single commit honours FR-022 (test suite green at every commit boundary).

**Alternatives considered**:
- *Two-commit rename: file move + reference updates* — rejected because the file-move commit leaves the codebase un-buildable.
- *Rename via a type alias (`PipeRpcServer` → `NamedPipeTransport`) kept as a `using` alias* — rejected because aliases multiply the discovery surface and the PRD success metric is specifically the renamed file, not a typedef.

## Decision 7: `AiRequestHandler` deletion criteria

**Decision**: Delete `src/AkmlSql.Engine/Ai/AiRequestHandler.cs` after every concrete subclass migration is verified by a passing smoke test. The intermediate state — where some subclasses exist and the monolith still has the remaining `HandleXxxAsync` methods — is intentional and lives for the duration of the seven sub-tasks. Each sub-task removes one public method from the monolith and adds one concrete subclass, ending at a stop-point that compiles + tests green.

**Rationale**: Big-bang deletion of the monolith is risky; per-handler atomic migration preserves FR-022 (test suite green at every commit boundary) and lets the closure pause/resume between handlers without leaving the engine broken.

**`RefreshSettings()` removal note**: the monolith exposes a public `RefreshSettings()` called by the `AnalysisSettingsChanged` handler. After all seven migrations, AI handlers read settings through `Services.SettingsProvider()` which calls `ctx.EnsureSettings().Ai` — implicit refresh after `ctx.InvalidateSettings()`. The explicit `RefreshSettings()` call site is removed when the monolith is deleted. This is the FR-013 invariant the spec captures.

**Alternatives considered**:
- *Delete the monolith last, keeping every migrated method as a private call into the new subclass* — rejected: leaves 1896 lines of dead-or-bridge code during the entire P3 phase.
- *Keep the monolith permanently as a public facade around the subclasses* — rejected: spec FR-013 forbids the AI-specific refresh hook; keeping the facade would keep `RefreshSettings()` alive.
