# Feature Specification: M1 — ScriptDom-in-WASM Runtime Spike & Decision Gate

**Feature Branch**: `023-m1-wasm-spike`
**Created**: 2026-05-21
**Status**: Draft
**Input**: User description: "Based on master after git-fetch + the M1 PRD (ScriptDom-in-WASM Spike & Blazor Project Skeleton). Scoped as a closure spec — the `AkmlSql.Web` scaffold from spec 021 already exists; this covers the genuinely-unmet M1 work: the deferred in-browser runtime spike (task T027) and the `docs/m1-wasm-decision.md` decision gate."

---

## Overview

The M1 PRD asks the project to prove, before committing M2 to a "thick browser" architecture, that `Microsoft.SqlServer.TransactSql.ScriptDom` and the AKML SQL formatter can run inside a Blazor WebAssembly runtime. The PRD assumes nothing exists yet.

That assumption is now out of date. Spec 021 (web edition) already landed the Blazor project: `src/AkmlSql.Web/` is a .NET 10 Blazor WASM standalone app that builds clean and references the netstandard2.0 libraries (`AkmlSql.Core`, `AkmlSql.Formatting`, `AkmlSql.Analysis`, `AkmlSql.IntelliSense`, `AkmlSql.AI`, `AkmlSql.Web.Shared`). `specs/021-web-edition/M1-SPIKE-RESULTS.md` records that **compile-time / link viability is confirmed** and the uncompressed `_framework/` payload measures ≈ 45 MB.

What was **never done** is the part the PRD actually cares about: task **T027 — the runnable spike page and the in-browser runtime execution — is unchecked**. ScriptDom has been proven to *compile* for `browser-wasm`; it has **never been observed to parse a single T-SQL statement at runtime in a browser**. No cold-load time was measured, no AOT-vs-interpreted comparison was run, and the go/no-go decision document `docs/m1-wasm-decision.md` does not exist.

This specification covers exactly that unmet work — the runtime spike and the decision gate — leaving the existing scaffold and the in-progress M2 surfaces in place. It is a **closure spec**, structured the same way spec 022 closed M0.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Prove ScriptDom parses and formats T-SQL in the browser (Priority: P1)

A web-edition maintainer can open a page in a browser — with no engine process running and no network connection — paste a T-SQL statement, trigger parse-and-format, and see correctly formatted SQL. This proves the ScriptDom parser and the formatter pipeline actually *execute* inside the WebAssembly runtime, not merely that they compile against it.

**Why this priority**: This is the single highest-risk assumption in the entire web-edition plan. Spec 021 confirmed only compile-time viability; the runtime spike (T027) was deferred and never run. Every M2 in-browser feature already being built rests on an assumption no one has empirically verified. This story retires that risk. If only this slice ships, the project knows whether its in-browser architecture is sound.

**Independent Test**: Serve the web-edition build, open the spike page in a current Chromium-based browser with the engine not running, paste a 10-line SELECT, trigger parse-and-format, and confirm formatted SQL appears with no browser-console exception (no `BadImageFormatException`, `TypeLoadException`, or `PlatformNotSupportedException`).

**Acceptance Scenarios**:

1. **Given** the web edition is loaded in a browser and no engine process is running, **When** the maintainer pastes a 10-line SELECT and triggers parse-and-format, **Then** the formatted SQL is displayed in the output area with no exception.
2. **Given** a SQL input that is syntactically invalid, **When** the maintainer triggers parse-and-format, **Then** the spike displays the parser's reported error information in the output area and the page stays responsive.
3. **Given** parsing or formatting throws any exception, **When** the spike handles it, **Then** the exception type and message are rendered in the output area rather than crashing the page or failing silently.
4. **Given** the spike page exists, **When** a maintainer opens the M2 editor surface instead, **Then** the M2 surface is unaffected — the spike is an additive, separate route.

---

### User Story 2 — Validate rich T-SQL and the reflection-discovered analyser (Priority: P2)

A maintainer can run the spike against T-SQL that resembles real user code — multi-statement batches, a 50-line stored procedure, common table expressions, window functions, MERGE — and can also run the analysis rule set against that corpus, getting either correct results or a precisely recorded failure for each item.

**Why this priority**: The PRD's highest-likelihood failure mode is "passes basic SQL but fails on complex T-SQL." WASM trimming can silently remove a reflection path that only a richer construct reaches. The analyser is the sharpest case: it discovers its 120+ rules by reflection at startup — exactly the construct trimming is most likely to break. A parse-and-format-only pass would leave the riskiest code path untested. This story turns "it ran once" into "it runs on the surface M2 actually needs."

**Independent Test**: Run each corpus item through both the formatter and the analyser in the browser; confirm every item produces output/findings or a recorded failure naming the exact error and the triggering construct; confirm the analyser discovers and executes its rule set.

**Acceptance Scenarios**:

1. **Given** a stored procedure of at least 50 lines, **When** the maintainer runs it through the spike, **Then** it parses and formats end-to-end with no exception.
2. **Given** corpus items covering a multi-statement batch, a CTE, a window function, and a MERGE statement, **When** each is run through the spike, **Then** each produces output or a recorded, explained finding — no silent failure.
3. **Given** the analysis rule set, **When** the spike runs it against the corpus in the browser, **Then** the analyser discovers its rules and produces findings, or the failure of reflection-based discovery under trimming is captured as an explicit finding.
4. **Given** a corpus item the AKML SQL engine processes successfully, **When** the same item is run through the spike with the same formatting profile and analysis settings, **Then** any divergence between the browser result and the engine result is recorded as a finding.

---

### User Story 3 — Quantify the cost of running ScriptDom in the browser (Priority: P3)

A maintainer planning M2's optimization work can read actual measured numbers for the compressed download size, the first-visit cold-load time, and the parse/format speed with and without ahead-of-time compilation — so M2's lazy-loading and AOT decisions rest on data, not estimates.

**Why this priority**: M2 is already in progress and needs concrete numbers to decide whether to lazy-load ScriptDom, whether to AOT-compile, and what bundle budget to hold. Spec 021 recorded only an *uncompressed* `_framework/` figure (≈ 45 MB) and an *estimate* for compressed size. This story replaces estimates with measurements. It is independent of Stories 1–2: the numbers can be captured from any successful spike build.

**Independent Test**: Inspect the decision document and confirm it records actual measured values — not ranges or estimates — for compressed payload size, cold-load time, and AOT-vs-interpreted parse/format time, each with the measurement method noted.

**Acceptance Scenarios**:

1. **Given** a production (Release) publish of the web edition, **When** its runtime payload is measured after compression, **Then** the actual compressed size is recorded as a single number.
2. **Given** a first visit in a browser with no warm cache, **When** the app's load is timed, **Then** the actual cold-load time is recorded with the machine and browser noted.
3. **Given** two publishes of the same input — one ahead-of-time compiled, one interpreted — **When** parse-and-format is timed on each, **Then** both numbers are recorded along with the build-time cost of the AOT publish.
4. **Given** the build logs of those publishes, **When** trim warnings are present, **Then** every warning is listed and each is either resolved or annotated with evidence it is safe to ignore.

---

### User Story 4 — A durable, evidenced decision document (Priority: P4)

A maintainer, or anyone planning the remaining web-edition milestones, can open one document — `docs/m1-wasm-decision.md` — and find every M1 investigation question answered with pass/fail and evidence, the measured numbers, a single outcome classification, and a go/no-go recommendation for the in-browser architecture.

**Why this priority**: The decision gate is the formal reason M1 exists. It is the capstone that converts Stories 1–3's evidence into a recorded, citable conclusion. It is lowest priority only because it depends on the evidence the other stories produce; on its own the file is the project's permanent record of why the M2 architecture was — or was not — sound.

**Independent Test**: Confirm `docs/m1-wasm-decision.md` exists, answers all seven investigation-matrix questions with pass/fail and evidence, records the three measurements, states one of the three defined outcomes, and gives a go/no-go recommendation.

**Acceptance Scenarios**:

1. **Given** the spike and measurements are complete, **When** the decision document is written, **Then** it answers each of the seven investigation-matrix questions with an explicit pass or fail and the evidence behind it.
2. **Given** the measurements from Story 3, **When** the decision document is written, **Then** it records the compressed bundle size, the cold-load time, and the AOT-vs-interpreted numbers.
3. **Given** all evidence, **When** the document states its conclusion, **Then** it classifies the result as exactly one of "clean pass", "works but heavy", or "does not work", and gives a go/no-go recommendation.
4. **Given** a no-go or qualified recommendation, **When** the document describes consequences, **Then** it states what the already-in-progress M2 in-browser work would need to change — without itself rolling anything back.

---

### Edge Cases

- **"Kinda works"** — The spike passes on a SELECT but throws on one specific rich construct (a CTE, a window function, or a MERGE) because trimming removed a reflection path only that construct reaches. The failure must be recorded as a precise finding naming the construct and error, not averaged into a general pass.
- **Analyser reflection discovery trimmed away** — The analyser's rule registry discovers its rules by reflection at startup. WASM trimming may strip rule types that have no static references, so the analyser silently discovers zero (or too few) rules. The spike must report the count of rules actually discovered at runtime so this is detectable.
- **AOT publish fails to build** — The AOT toolchain or a trim incompatibility prevents the AOT publish from completing. The interpreted result still stands; this is recorded as "AOT not currently viable", not a blanket no-go.
- **Cold-load time dominated by cache state** — A warm browser cache makes cold-load look artificially fast. The measurement must be taken under first-visit (cleared-cache) conditions and the method recorded, or the number is meaningless.
- **Full pipeline vs parse alone** — The formatter pipeline's later stages (semantic validation re-parses the formatted text) may behave differently under WASM than the parse step alone. The spike must run the whole pipeline, not just the parse, so a stage-specific failure surfaces.
- **Output mismatch — bug or profile** — A formatter-output divergence from the engine could be a real WASM defect or merely a different profile. The comparison must use the same default profile on both sides so a mismatch is unambiguous.
- **Oversized `.sql` file** — A file loaded via the file picker exceeds the established 10 MB per-document limit. The spike must surface that limit cleanly rather than freezing the browser tab.
- **Cross-browser divergence** — The spike passes on a Chromium browser but throws on a non-Chromium engine. Results must be recorded per browser, not collapsed into one pass/fail.

## Requirements *(mandatory)*

### Functional Requirements

#### Spike surface & runtime viability (Story 1)

- **FR-001**: A spike surface MUST exist within `AkmlSql.Web` as a route distinct from the M2 editor surface, runnable without any engine process or network connection.
- **FR-002**: The spike surface MUST accept T-SQL input both by paste/type into a text area and by loading a `.sql` file from the local machine.
- **FR-003**: On a triggered action, the spike MUST parse the input with the ScriptDom parser and run the full formatter pipeline on the parse result, entirely within the browser runtime.
- **FR-004**: For a valid 10-line SELECT, the spike MUST display formatted output with no load-time or runtime exception — specifically no `BadImageFormatException`, `TypeLoadException`, or `PlatformNotSupportedException`.
- **FR-005**: When parse or format throws, the spike MUST render the exception type and message in the output area and keep the page responsive.
- **FR-006**: Adding the spike surface MUST NOT alter, disable, or regress the existing `AkmlSql.Web` scaffold or any M2 surface already present; the spike is strictly additive.

#### Rich T-SQL & analyser validation (Story 2)

- **FR-007**: The spike MUST be exercised against a corpus that includes, at minimum: a 10-line SELECT, a multi-statement batch, a stored procedure of at least 50 lines, a common table expression, a window function, and a MERGE statement.
- **FR-008**: The spike MUST run both the formatter pipeline and the analysis rule set against the corpus, in the browser runtime.
- **FR-009**: For every corpus item, the spike MUST either produce output/findings or produce a recorded finding that names the exact error and the T-SQL construct that triggered it — no silent failure is acceptable.
- **FR-010**: The decision document MUST report whether the analyser's reflection-based rule discovery survives WASM trimming, citing the number of rules discovered at runtime or the exact failure observed.
- **FR-011**: For corpus items the AKML SQL engine processes successfully, the spike MUST use the same default formatting profile and analysis settings, and any divergence between the browser result and the engine result MUST be recorded as a finding.

#### Cost measurement (Story 3)

- **FR-012**: The compressed runtime payload size of a Release publish of `AkmlSql.Web` MUST be measured and recorded as an actual number.
- **FR-013**: First-visit cold-load time MUST be measured under cleared-cache conditions on a stated developer machine and browser, and recorded as an actual number with the measurement method.
- **FR-014**: Parse-and-format execution time MUST be measured for both an ahead-of-time-compiled publish and an interpreted publish of the same input; both numbers, and the AOT publish's build-time cost, MUST be recorded.
- **FR-015**: Build-time trim warnings from those publishes MUST be captured and listed; each MUST be either resolved or annotated with evidence that it is safe to ignore.

#### Decision document (Story 4)

- **FR-016**: A decision document MUST be written at `docs/m1-wasm-decision.md`.
- **FR-017**: The decision document MUST answer each of the seven M1 investigation-matrix questions — ScriptDom load, formatter pipeline run, bundle size, cold-load time, AOT justification, trim warnings, missing-API runtime errors — with an explicit pass or fail and the supporting evidence.
- **FR-018**: The decision document MUST record the measured compressed bundle size, cold-load time, and AOT-vs-interpreted numbers.
- **FR-019**: The decision document MUST classify the result as exactly one of three outcomes — clean pass, works but heavy, does not work — and give a go/no-go recommendation for the in-browser M2 architecture.
- **FR-020**: When the recommendation is no-go or qualified, the decision document MUST describe the consequences for the already-in-progress M2 in-browser work; it MUST NOT itself roll back, disable, or redesign any existing scaffold or M2 surface.

#### Cross-cutting constraints

- **FR-021**: This work MUST NOT modify the engine, any of the six shell extensions (SSMS 20/21/22, VS 2019/2022/2026), or the shared shell project.
- **FR-022**: `AkmlSql.Web` MUST continue to build and to complete `dotnet publish -c Release` with the spike surface present.
- **FR-023**: The browsers used for the runtime spike MUST be recorded; at least one current Chromium-based browser (Chrome or Edge) MUST be covered, and behaviour on other evergreen browsers (Firefox, Safari) MUST be either documented or explicitly marked untested.
- **FR-024**: The spike MUST NOT require a rewrite of `AkmlSql.Core`, `AkmlSql.Formatting`, `AkmlSql.Analysis`, or any other referenced library; if a referenced library proves WASM-incompatible, that fact is itself a recorded spike finding for the decision document.

### Key Entities *(include if feature involves data)*

- **Spike surface** — An additive browser route in `AkmlSql.Web`, separate from the M2 editor, that runs ScriptDom parse, the formatter pipeline, and the analysis rule set on demand and shows output, findings, or exception text. The vehicle for the runtime spike.
- **T-SQL test corpus** — The set of SQL inputs the spike is run against: a 10-line SELECT, a multi-statement batch, a 50-line-or-longer stored procedure, a CTE, a window function, and a MERGE statement.
- **WASM cost measurements** — The recorded numbers: compressed runtime payload size, first-visit cold-load time, AOT-vs-interpreted parse/format time, AOT build-time cost, and the trim-warning list.
- **M1 decision document** — `docs/m1-wasm-decision.md`. The durable record: seven investigation-matrix answers with evidence, the measurements, one outcome classification, a go/no-go recommendation, and — if not a clean pass — the consequences for in-progress M2 work.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With no engine process running, the spike opened in a browser parses and formats a 10-line SELECT and displays formatted output with zero runtime exceptions.
- **SC-002**: The spike parses and formats a stored procedure of at least 50 lines end-to-end with no exception.
- **SC-003**: Every item in the rich T-SQL corpus produces either formatted output / analyser findings or a recorded, explained finding — there is no silent failure across the corpus.
- **SC-004**: The decision document states, with evidence, whether the analyser's reflection-based rule discovery survives WASM trimming.
- **SC-005**: The compressed payload size, cold-load time, and AOT-vs-interpreted parse/format times are each recorded in the decision document as actual measured numbers, not estimates.
- **SC-006**: `docs/m1-wasm-decision.md` exists and answers all seven investigation-matrix questions with a pass/fail verdict and evidence.
- **SC-007**: The decision document states exactly one of the three defined outcomes and a clear go/no-go recommendation.
- **SC-008**: `AkmlSql.Web` builds and completes `dotnet publish -c Release` with the spike surface present.
- **SC-009**: No file under the engine, the six shell extensions, or the shared shell project is changed by this work, and the existing `AkmlSql.Web` scaffold and M2 surfaces remain functional.
- **SC-010**: A maintainer can reproduce the spike outcome by following the steps recorded in the decision document and observe the same pass/fail results.

## Assumptions

- **The Blazor project already exists.** `src/AkmlSql.Web/` is a .NET 10 Blazor WASM standalone project from spec 021. The spike surface is *added* to it; the project is not recreated. The PRD's open question of ".NET 8 vs .NET 9" is therefore moot — the established target framework is .NET 10 and is not changed by this work.
- **The spike surface is a dedicated route** (e.g. a `Spike` page) separate from the M2 editor at the application's index route, so it can be run without depending on or disturbing M2 features. The page name is an implementation convention, not a fixed requirement.
- **The decision document path is `docs/m1-wasm-decision.md`** per the PRD. The repository uses both a `doc/` and a `docs/` tree; the PRD's choice is honoured.
- **Compile-time / link viability is already established** and recorded in `specs/021-web-edition/M1-SPIKE-RESULTS.md` (uncompressed `_framework/` ≈ 45 MB). This spec covers only the unmet runtime, measurement, and decision work.
- **The PRD's bundle (≤ 25 MB compressed) and cold-load (≤ 8 s) figures are negotiable reference thresholds** used to classify the outcome, not hard pass/fail gates. The actual measured numbers are what the decision records; the thresholds only inform which of the three outcomes applies.
- **The spike is retroactive risk-retirement.** Because M2 in-browser work has already progressed, the gate is no longer literally "before M2." A no-go finding does NOT auto-roll-back M2; it is raised as a rework risk for the M2 track to act on. This is the agreed "closure spec" framing.
- **The formatter "default profile"** used for the spike is the engine's built-in default formatting profile, and the analyser uses default analysis settings, so spike output can be compared against engine output for the same input.
- **The reference browser is a current Chromium-based browser** (Chrome or Edge); Firefox and Safari are documented separately and may be marked untested. Mobile browsers are out of scope.
- **The engine, the six shell extensions, and the shared shell project are out of scope** and are not touched by this work.

## Dependencies

- **Spec 021 (web edition) M1 scaffold** — `AkmlSql.Web`, its `Program.cs` bootstrap, layout components, and the netstandard2.0 library extractions (`AkmlSql.Core`, `AkmlSql.Formatting`, `AkmlSql.Analysis`, `AkmlSql.IntelliSense`, `AkmlSql.AI`, `AkmlSql.Web.Shared`) — assumed merged to master. This spec builds the spike surface on top of them.
- **Spec 022 (M0 engine transport closure)** — merged to master; the predecessor milestone. Not a functional dependency of the spike itself (the spike makes no engine call).
- **The ScriptDom parser, the formatter pipeline, and the analysis rule set** must be reachable from the netstandard2.0 libraries already referenced by `AkmlSql.Web`; no library rewrite is in scope.
- **A current Chromium-based browser** and a Windows machine with the **.NET 10 SDK** (required for the Release publish that produces the compressed-bundle and AOT measurements).
- **No external blockers.** No coordination with the M2–M6 tracks is required to produce the spike and decision; those tracks consume the decision but are not gated on it, given they have already progressed.

## Out of Scope

- Any M2-and-beyond UI work — editor component, theme system, design tokens, problems list, settings screen.
- IndexedDB schema cache, WebSocket transport, IIS deployment / installer integration, AI provider integration.
- **Performance optimization** — the spike *measures*; it does not lazy-load, trim-tune, or AOT-optimize beyond what is needed to produce the AOT-vs-interpreted comparison.
- Rolling back, redesigning, or re-validating the existing `AkmlSql.Web` scaffold or the M2 surfaces already on master.
- Changing the target framework of `AkmlSql.Web`.
- Mobile / tablet browsers.
- Any change to the engine, the shell extensions, or the shared shell project.

## Definition of Done

This spec is done when, for every functional requirement above, the runtime spike has produced the corresponding evidence, every success criterion has been verified, and `docs/m1-wasm-decision.md` has been written with a go/no-go recommendation. Per the PRD, the branch `023-m1-wasm-spike` is merged to master via PR regardless of the go/no-go outcome — the spike surface and the decision document stay in the repository as the permanent record of the M1 gate.
