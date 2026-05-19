# Feature Specification: M0 Engine Transport Closure

**Feature Branch**: `022-m0-engine-closure`
**Created**: 2026-05-19
**Status**: Draft
**Input**: User description: "BASED ON docs/superpowers/plans/2026-05-19-m0-engine-transport-closure.md"

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Single source of truth for engine settings (Priority: P1)

The engine maintainer can rely on a single, well-defined location where the cached application settings live. Today the cache is dual-owned between the named-pipe transport and the shared request context: a field on the transport plus a mirrored property on the context, with the invalidation logic spread across both. This is a half-finished refactor that leaks transport coupling into shared state.

**Why this priority**: This is the only deferred PRD item that represents a real architectural smell shipping in production today. Drift between the two copies has already been observed in code reviews. It is also the cheapest gap to close (≈ half a day) and unblocks the cleaner rename in P2.

**Independent Test**: A maintainer can verify the gap is closed by reading the engine source: the settings cache appears in exactly one source file, and that file is the shared context — not any transport. A targeted unit test confirms `EnsureSettings()` caches on first call and `InvalidateSettings()` forces a reload.

**Acceptance Scenarios**:

1. **Given** the engine has loaded settings once for a connected client, **When** any subsequent handler asks the context for settings, **Then** the same `AppSettings` instance is returned without re-reading the on-disk config file.
2. **Given** a client sends an `AnalysisSettingsChanged` notification, **When** the next handler asks the context for settings, **Then** the on-disk config is re-read and the new values are returned.
3. **Given** any two concurrently registered handlers share the context, **When** one reads settings and the other calls `InvalidateSettings()`, **Then** the next read by either handler observes the invalidation.

---

### User Story 2 — Clean named-pipe transport file as a reference shape (Priority: P2)

A future-transport author (M3+ WebSocket TLS, M3+ pairing UI, M4 HTTP/SSE) can read the named-pipe transport source as a focused template: pipe ACL configuration, accept loop, frame I/O, and the dispatch hand-off to the router. Today that file is 354 lines plus a 353-line partial that mixes ACL config with ~50 handler registrations and full engine service construction.

**Why this priority**: The current shape works for the named-pipe transport today, but it is unusable as a reference for new transports. Closing this gap unblocks every future transport addition without touching the named-pipe behaviour. Independent from P1 in scope but cleaner to land after P1.

**Independent Test**: The named-pipe transport source file is ≤ 150 lines as measured by a line counter, contains no service-construction code, and a new transport can be implemented by copying its shape and substituting the wire-level concerns. The full engine test suite passes with identical results before and after this refactor.

**Acceptance Scenarios**:

1. **Given** the post-refactor engine source tree, **When** a maintainer runs a line counter on the named-pipe transport file, **Then** the result is ≤ 150 lines of code.
2. **Given** the engine starts up via its main entry point, **When** a client connects on the named pipe and sends one of every existing message type, **Then** every request returns the same response bytes as the pre-refactor engine.
3. **Given** the engine starts up via its main entry point, **When** the same client requests are routed through the in-process transport (used by Blazor WASM tests), **Then** the responses match the named-pipe responses byte-for-byte.
4. **Given** a developer wants to add a new transport, **When** they inspect the source, **Then** they find one composition root that builds every service and registers every handler, reusable by any `IRpcTransport` implementation.

---

### User Story 3 — Small focused handler class per AI message type (Priority: P3)

A maintainer adding or modifying an AI message type can do so in a single file ≤ 80 lines. Today every AI handler must inline a privacy-consent check, retry-with-backoff, settings refresh, and error-envelope construction; the aggregate AI dispatcher file is 1896 lines and a new AI message type requires another ≈ 200 lines of mostly-duplicated code.

**Why this priority**: Maintainability gain only — no user-visible behaviour changes and the current monolith works. The team's prior judgement (T019) explicitly preferred the monolith on the grounds that "consolidation already happened inside the existing dispatcher". Reversing that decision is interpretive work, sized at roughly two days, and is therefore the right candidate for "after the structural gaps are closed".

**Independent Test**: A maintainer can verify the gap is closed by listing the per-handler files: every concrete AI handler is its own class, each file is ≤ 80 lines, and they all derive from a common abstract base that owns the cross-handler boilerplate. Each handler still works end-to-end through both the named-pipe and in-process transports.

**Acceptance Scenarios**:

1. **Given** the post-refactor engine source tree, **When** a maintainer lists the AI-handler source files, **Then** each concrete handler file is ≤ 80 lines of code.
2. **Given** the engine receives any AI message (TextToSql, Explain, Fix, Optimize, IndexAnalysis, Chat, GhostText), **When** the handler runs against a local AI provider that does not require consent, **Then** the response matches the pre-refactor response in shape and content.
3. **Given** the engine receives an AI message bound to a cloud provider, **When** the user has not granted privacy consent, **Then** the same error envelope is returned that was returned pre-refactor (no behaviour change observable from the client).
4. **Given** a maintainer wants to add a new AI message type, **When** they write the new handler class, **Then** they only override the per-message logic — privacy, retries, settings, and error shaping are inherited from the base.

---

### User Story 4 — Meaningful performance-regression gate (Priority: P4)

A maintainer who introduces a 5–10 % performance regression in the engine's dispatch hot path discovers it in CI rather than in production. Today the gate fires only above a 25 % regression because the measured workloads are sub-2 ms per call and machine-level noise dominates the signal. A real 10 % regression in completion latency would pass CI silently.

**Why this priority**: Prevention only — there is no current regression to fix. Lowest priority because it pays off only on future PRs that genuinely regress performance. Independent from P1–P3 in code scope.

**Independent Test**: The performance test runs three consecutive times on the same machine against identical code and passes every time; introducing an artificial 6 % slowdown into the dispatch hot path causes the test to fail.

**Acceptance Scenarios**:

1. **Given** an unchanged engine codebase, **When** the performance gate runs three times in a row on the same machine, **Then** all three runs pass with the 5 % threshold.
2. **Given** a synthetic 10 % slowdown injected into the completion dispatch path, **When** the performance gate runs, **Then** it fails with a clear message naming the regressed metric.
3. **Given** the measured workloads, **When** a maintainer inspects the baseline file, **Then** every measured p50 is ≥ 20 ms — large enough that 5 % represents at least 1 ms of real signal, well above per-run jitter.

---

### Edge Cases

- **AnalysisSettingsChanged during a long-running request**: A handler that began before the invalidation event must complete with the old settings; the next request observes the new settings. Concurrent reads while the cache is being invalidated must not crash or return a half-initialised object.
- **Delegating handlers (legacy raw-message handlers)**: Some handlers — session save/restore, history, productivity, navigation, the AI bridge — process the raw envelope themselves and produce a response envelope directly. The router must continue to support this without forcing a full conversion to the typed-request/typed-response contract.
- **Handler exceptions**: An uncaught exception inside any handler must produce a standard error envelope sent back to the requesting client; the transport accept loop must not die. Cancellation exceptions in handlers flagged "swallow cancellation" must return an empty response rather than tearing down the transport.
- **Reflection-discovered handlers with constructor dependencies**: The router's reflection-based discovery path must continue to silently skip handlers it cannot construct (e.g., those requiring injected services). Explicit registration through the composition root remains the source of truth for those handlers.
- **Renaming during in-flight builds**: When the transport file is renamed, every reference (engine entry point, test files, doc comments) must update atomically; no intermediate state where the engine cannot be built.
- **Performance noise on shared CI runners**: A run with a transient noisy neighbour must not flake the gate. Mitigation: measurement uses the minimum p50 across N independent trials, not the average, and trial count is set so the noisy-neighbour case is dominated.
- **AI handler that needs settings refresh**: When `AnalysisSettingsChanged` arrives, the next AI handler call must observe the new settings without an explicit `RefreshSettings()` method call — the settings lookup must go through the same cache that other handlers use.

## Requirements *(mandatory)*

### Functional Requirements

#### Settings cache (Story 1)

- **FR-001**: The cached `AppSettings` instance MUST have exactly one persistent owner in the engine. That owner MUST be the shared request context, accessible to every handler regardless of which transport delivered the request.
- **FR-002**: The shared context MUST expose an idempotent settings accessor that loads from the on-disk config on first call and returns the cached instance thereafter.
- **FR-003**: The shared context MUST expose an explicit invalidation method that drops the cache; the next accessor call MUST re-read the on-disk config.
- **FR-004**: The handler bound to `AnalysisSettingsChanged` MUST call the invalidation method exactly once per event, with no side effects on other transports' lifetimes.

#### Named-pipe transport shape (Story 2)

- **FR-005**: The source file implementing the named-pipe transport MUST contain only named-pipe-specific concerns: pipe access-control configuration, accept loop, framed read/write, and the dispatch hand-off to the router.
- **FR-006**: The source file implementing the named-pipe transport MUST be ≤ 150 lines of code.
- **FR-007**: A single composition root MUST exist as the engine's service-construction entry point. It MUST build every engine service, construct the shared context, register every handler with the router, and return values consumable by every transport implementation.
- **FR-008**: All three transports (named-pipe, in-process, WebSocket) MUST consume the same composition root output. No transport MUST replicate service-construction or handler-registration logic.
- **FR-009**: The router MUST provide a registration path for delegating handlers that process the raw message and return a complete response envelope themselves, without forcing them to adopt the typed-request/typed-response contract.

#### AI handler structure (Story 3)

- **FR-010**: An abstract AI handler base class MUST own the cross-handler boilerplate that today is inlined in every AI handler: privacy-consent check, settings retrieval through the shared context, retry-with-exponential-backoff for rate-limited responses, and standardised error-envelope construction.
- **FR-011**: Each of the seven concrete AI message types — TextToSql, Explain, Fix, Optimize, IndexAnalysis, Chat, GhostText — MUST have its own concrete handler class that overrides only the per-message logic.
- **FR-012**: Each concrete AI handler source file MUST be ≤ 80 lines of code.
- **FR-013**: AI handlers MUST observe settings changes by going through the same context-based settings accessor as every other handler; no AI-specific settings-refresh hook MAY remain.

#### Performance-regression gate (Story 4)

- **FR-014**: The performance-regression gate MUST be set at 5 % over the captured baseline for both the completion-dispatch path and the formatting-dispatch path.
- **FR-015**: The measured workloads MUST be sized such that the p50 measurement is ≥ 20 ms per call. If existing workloads do not meet this threshold, they MUST be replaced or supplemented with heavier corpora until they do.
- **FR-016**: The performance test MUST pass on three consecutive runs of the same machine with identical engine code. A failure on any of three back-to-back runs is treated as flake and resolved by tuning iteration count, not by relaxing the threshold.

#### Cross-cutting invariants

- **FR-017**: The on-wire frame format (length prefix, integrity check, MessagePack body) MUST NOT change. No client (shell extension, browser, in-process consumer) MUST require any update.
- **FR-018**: No integer message-type code MUST change. The set of registered codes after the closure MUST equal the set before.
- **FR-019**: No file under the shared shell project or any of the six shell extension projects (SSMS 20 / 21 / 22, VS 2019 / 2022 / 2026) MUST be modified during this work.
- **FR-020**: All ~50 existing message types MUST continue to round-trip through every transport with identical request and response bytes.
- **FR-021**: All six shell extensions MUST continue to build to completion against the unchanged shared shell sources and the post-closure engine.
- **FR-022**: The engine test suite MUST remain green at every commit boundary. Skipped or quarantined tests MUST NOT be added to mask intermediate breakage.

### Key Entities *(include if feature involves data)*

- **Shared request context**: The single per-process object passed to every handler invocation. Sole owner of the cached application settings after this closure.
- **Named-pipe transport**: One implementation of the engine's transport contract; specialised for pipe ACL + accept-loop + framed I/O.
- **Composition root**: New service-construction entry point that wires every engine service, builds the shared context, and registers every handler with the router. Replaces the constructor block on the current named-pipe transport.
- **AI handler base**: Abstract class owning privacy-consent / settings / retry / error-envelope concerns shared by every AI message handler. Replaces the monolithic AI dispatcher.
- **AI handler subclass**: One per AI message type (seven total). Each carries only the per-message logic — prompt construction, provider invocation, response shaping.
- **Performance baseline**: Per-machine reference file recording the p50 latency of representative dispatch workloads. Compared against on every test run; tolerance set by the regression threshold.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The cached application settings field appears in exactly one source file in the engine code tree, verifiable by source search.
- **SC-002**: The named-pipe transport source file measures ≤ 150 lines as reported by a standard line counter.
- **SC-003**: Every concrete AI handler source file measures ≤ 80 lines as reported by a standard line counter.
- **SC-004**: The engine test suite — including the matrix test that exercises every registered message type via the in-process transport, and the round-trip test that exercises a real named pipe — passes at every commit boundary in the closure work.
- **SC-005**: A synthetic 10 % slowdown injected into the completion or formatting dispatch path causes the performance-regression gate to fail on the next run.
- **SC-006**: Three consecutive runs of the performance-regression gate against unchanged engine code all pass at the 5 % threshold.
- **SC-007**: All six shell extensions build to completion against the post-closure engine without any change to shell source code.
- **SC-008**: A maintainer adding a new transport implementation can do so by writing only one new file that implements the transport contract, with zero new service-construction or handler-registration code.
- **SC-009**: A maintainer adding a new AI message type can do so by writing one new handler class ≤ 80 lines, inheriting the privacy / retry / settings / error-envelope concerns from the existing base.
- **SC-010**: The on-wire bytes returned for every existing message type are identical before and after the closure for the same input.

## Assumptions

- The shell extensions continue to use the named-pipe transport exclusively. The in-process and WebSocket transports are not exposed to shell extensions in this scope.
- Performance measurement on a developer workstation is representative enough for the 5 % gate. CI agents that exhibit pathological variance use the existing baseline-update environment variable so the gate captures fresh numbers per agent.
- The team has decided (via the user's answer to the gap scope question on 2026-05-19) that the prior judgement to keep the AI dispatcher consolidated is reversed for this closure work. The interpretive overhead of splitting into a base plus seven subclasses is accepted in exchange for the maintainability gain.
- The named-pipe wire envelope (length-prefixed framed MessagePack) does not change under this work. Any future transport-specific framing (e.g., one WebSocket frame = one envelope) belongs to that transport, not to the closure.
- The reflection-based handler discovery path remains additive: handlers with constructor dependencies continue to be registered explicitly through the composition root.

## Dependencies

- Spec 021 (web edition) M0 is the predecessor work and is assumed merged to master (PR #236, 2026-05-15). Every architectural piece this closure refines already exists in the codebase: `IRpcTransport`, `IRpcRequestHandler<,>`, `RpcRouter`, the shared request context, three transport implementations, the handler folder structure, the reflection-discovery path, and the in-process matrix test.
- No external blockers. All work is internal to the engine project and the engine test suite.
- No coordination required with the web edition tracks (M2 / M3 / M4 / M5). Those tracks consume the closure's outputs but are not gated on them.
