# Implementation Plan: M1 — ScriptDom-in-WASM Runtime Spike & Decision Gate

**Branch**: `023-m1-wasm-spike` | **Date**: 2026-05-21 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/023-m1-wasm-spike/spec.md`

## Summary

Run the M1 in-browser runtime spike that spec 021 deferred (task T027), and write the go/no-go decision document `docs/m1-wasm-decision.md`. The primary requirement: prove — empirically, in a real browser — that the ScriptDom parser, the formatter pipeline, and the analysis rule set actually *execute* inside the Blazor WebAssembly runtime, not merely that they compile against it. The existing `AkmlSql.Web` scaffold and the in-progress M2 surfaces are treated as given; this is a **closure spec**, parallel to how spec 022 closed M0.

**Technical approach** (consolidated from the spec, the three Phase 0 research reports, and the codebase exploration):

1. **P1 — Spike surface.** Add `Pages/Spike.razor` at route `/spike`: a minimal, instrumented diagnostic harness, separate from the M2 editor. It `@inject`s the already-registered `IFormatterService` and `IAnalyserService` (so the spike validates the *exact* code path the editor uses), accepts SQL three ways (paste textarea, `<InputFile>` `.sql` load, corpus dropdown), times each call with `Stopwatch`, and renders formatted output / analyser findings / **verbatim exception text** (type + message + stack). A startup microbench records `Stopwatch.Frequency` and the effective timer resolution.
2. **P2 — Corpus, analyser, golden files.** Add a six-item T-SQL corpus under `wwwroot/spike-corpus/` (SELECT, multi-statement batch, ≥ 50-line stored procedure, CTE, window function, MERGE). A desktop generator in `AkmlSql.Web.Tests` runs the *same* `AkmlSql.Formatting` / `AkmlSql.Analysis` libraries on desktop .NET to produce `.expected.sql` / `.expected.json` golden files; the spike fetches both and diffs — any divergence is a pure WASM-runtime finding. The spike instantiates `RuleRegistry` directly to report the discovered-rule count (baseline 130) as trim-survival evidence. A Playwright test in `AkmlSql.Web.E2E.Tests` drives `/spike` in a real browser for a repeatable check.
3. **P3 — Measurements.** `dotnet publish -c Release`; measure the compressed `_framework/*.br` total on disk; serve the publish and measure first-visit cold-load in a Chromium browser under true-cold (cleared site-storage) conditions with DevTools; a second publish with `-p:RunAOTCompilation=true` for the AOT-vs-interpreted comparison; capture `IL2xxx` trim warnings with `-p:TrimmerSingleWarn=false`.
4. **P4 — Decision document.** Write `docs/m1-wasm-decision.md` per the decision-document contract: the seven-question investigation matrix (each with pass/fail + evidence), the three measurements, the corpus result table, the rule-discovery verdict, an outcome classification (clean pass / works but heavy / does not work), and a go/no-go recommendation for the in-browser M2 architecture.

**Cross-cutting**: the spike is **purely additive**. `HttpClient`, `IFormatterService`, and `IAnalyserService` are already registered in `Program.cs`; the assembly-scanning `<Router>` auto-routes any `@page` component — so `Program.cs`, `App.razor`, and `AkmlSql.Web.csproj` are **unchanged**. AOT and detailed-trim-warning capture use one-off publish flags, never committed csproj edits. No file under the engine, the six shell extensions, or the shared shell project is touched.

## Technical Context

**Language/Version**: C# / .NET 10. `AkmlSql.Web` is a standalone Blazor WebAssembly app (`Microsoft.NET.Sdk.BlazorWebAssembly`, `net10.0`); Razor components. The referenced AkmlSql libraries (`Core`, `Formatting`, `Analysis`, `IntelliSense`, `AI`, `Web.Shared`) target `netstandard2.0`.

**Primary Dependencies**: Existing only — `Microsoft.AspNetCore.Components.WebAssembly` 10.\*; `Microsoft.SqlServer.TransactSql.ScriptDom` (transitive via `AkmlSql.Formatting` / `AkmlSql.Analysis`); the AkmlSql libraries already referenced by `AkmlSql.Web`. **No new NuGet package reference.** Build-time only, for the AOT measurement: the **`wasm-tools`** .NET SDK workload (`dotnet workload install wasm-tools`). Dev-time only, for local serving: `dotnet-serve` global tool (or IIS / Nginx for accurate Brotli content-negotiation).

**Storage**: N/A. The spike persists nothing at runtime. The T-SQL corpus and the desktop-generated golden files are static assets under `src/AkmlSql.Web/wwwroot/spike-corpus/`. The decision document is a markdown file at `docs/m1-wasm-decision.md`.

**Testing**: The spike's **verification of record is the in-browser run**, captured in `docs/m1-wasm-decision.md` and reproducible via `quickstart.md`. WASM viability **cannot** be proven by `dotnet test` or bUnit (`AkmlSql.Web.Tests`) — those execute on desktop .NET, not the WASM runtime. The automated, repeatable browser check is a **Playwright E2E test** in the existing `AkmlSql.Web.E2E.Tests` project, which drives `/spike` in a real Chromium browser. A desktop generator test in `AkmlSql.Web.Tests` produces the `.expected.*` golden comparison files. No engine test, shell test, or existing web test is modified.

**Target Platform**: `browser-wasm` RID — a current Chromium-based browser (Chrome / Edge primary; Firefox / Safari documented or marked untested). Build / publish host: Windows x64 with the .NET 10 SDK and, for the AOT measurement, the `wasm-tools` workload.

**Project Type**: A time-boxed investigation spike that adds one diagnostic page to the existing `AkmlSql.Web` Blazor WebAssembly project. The deliverable is recorded evidence plus a decision document — not a shippable feature.

**Performance Goals**: The spike **measures**; it does not optimize. Reference thresholds (from the PRD investigation matrix, used only to classify the outcome): compressed `_framework/` ≤ 25 MB; first-visit cold-load ≤ 8 s on a representative dev machine. A ≥ 50-line stored procedure must parse + format without freezing the browser tab. Known prior baseline: uncompressed `_framework/` ≈ 45 MB (`specs/021-web-edition/M1-SPIKE-RESULTS.md`).

**Constraints**:
- Additive only — no modification of existing `AkmlSql.Web` source; no regression of the M2 surfaces (FR-006, FR-019).
- `dotnet publish -c Release` MUST continue to succeed with the spike present (FR-022).
- No change to the engine, the six shell extensions, or the shared shell project (FR-021).
- No new external NuGet reference (FR-024); no target-framework change; no rewrite of `AkmlSql.Core` / `AkmlSql.Formatting` / `AkmlSql.Analysis` (FR-024).
- AOT (`RunAOTCompilation`) and detailed trim-warning capture (`TrimmerSingleWarn=false`) are one-off **publish flags**, never committed to `AkmlSql.Web.csproj` — adopting AOT is an M2 decision the spike only informs.

**Scale/Scope**: 4 user stories, 24 functional requirements, 10 success criteria, 8 edge cases. One new Razor page; a six-item T-SQL corpus; three measurements (compressed bundle, cold-load, AOT-vs-interpreted); one decision document; two new test files. 130 analysis rules — the reflection-trim risk surface. Estimated effort ≈ 1 week (PRD): ~2 d P1, ~1.5 d P2, ~1.5 d P3, ~0.5 d P4.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

`.specify/memory/constitution.md` does not exist in this repository. The gate is therefore advisory only, following the same pattern as specs 021 and 022's plans. Applying common engineering gates by inspection:

| Gate | Result | Notes |
|------|--------|-------|
| Spike is bounded and decision-gated | **PASS** | One week, four stories, an explicit go/no-go artifact. Every question maps to recorded evidence. |
| No new technology introduced | **PASS** | Uses the existing `AkmlSql.Web` project and already-referenced libraries. `wasm-tools` is a build-time SDK workload used for one measurement; `dotnet-serve` is a dev-time tool. Neither is a shipped runtime dependency. |
| Additive — no regression to existing surfaces | **PASS** | FR-006 / FR-019. `Program.cs`, `App.razor`, and `AkmlSql.Web.csproj` are unchanged; the spike page is auto-routed and reuses already-registered services. |
| Independence from IDE plugins preserved | **PASS** | FR-021 forbids touching the engine, the six shell extensions, and the shared shell project. The spike makes no engine call. |
| Evidence-based, reproducible decision | **PASS** | The decision document records pass/fail + evidence per question; `quickstart.md` and the Playwright E2E test make the run reproducible (SC-010). |
| No premature abstraction | **PASS** | One diagnostic page reusing existing services and a flat corpus folder. No new framework, interface, or abstraction layer. |
| Reversibility | **PASS** | On a no-go outcome the spike page and decision document remain as the permanent record (PRD definition of done); nothing else in the codebase changes, so there is nothing to revert. |

No violations to track in **Complexity Tracking**.

## Project Structure

### Documentation (this feature)

```text
specs/023-m1-wasm-spike/
├── plan.md                          # this file
├── spec.md                          # produced by /speckit.specify
├── checklists/
│   └── requirements.md              # produced by /speckit.specify
├── research.md                      # Phase 0 output (this command)
├── data-model.md                    # Phase 1 output (this command)
├── quickstart.md                    # Phase 1 output (this command) — doubles as the spike runbook
├── contracts/
│   ├── spike-page.md                # Spike.razor UI + behaviour contract
│   ├── decision-document.md         # required structure of docs/m1-wasm-decision.md
│   └── measurement-protocol.md      # reproducible bundle / cold-load / AOT measurement procedures
└── tasks.md                         # produced by /speckit.tasks (next command)
```

### Source Code (repository root)

The spike is purely additive within the existing `AkmlSql.Web` project. No existing source file is modified.

```text
src/
└── AkmlSql.Web/
    ├── Pages/
    │   └── Spike.razor                       # NEW (P1) — /spike diagnostic harness
    ├── wwwroot/
    │   └── spike-corpus/                     # NEW (P2) — corpus + desktop-generated golden files
    │       ├── corpus.json                   # NEW — manifest (id, display name, description, construct)
    │       ├── 01-select.sql                 # NEW — 10-line SELECT
    │       ├── 02-batch.sql                  # NEW — multi-statement batch
    │       ├── 03-stored-proc.sql            # NEW — stored procedure, ≥ 50 lines
    │       ├── 04-cte.sql                    # NEW — common table expression
    │       ├── 05-window.sql                 # NEW — window function
    │       ├── 06-merge.sql                  # NEW — MERGE statement
    │       ├── *.expected.sql                # NEW — formatter golden output (desktop-generated)
    │       └── *.expected.json               # NEW — analyser golden output (desktop-generated)
    ├── Program.cs                            # UNCHANGED — HttpClient + IFormatterService + IAnalyserService already registered
    ├── App.razor                             # UNCHANGED — <Router> auto-routes the new @page
    └── AkmlSql.Web.csproj                    # UNCHANGED — no new reference; AOT / trim flags are publish-time only

docs/
└── m1-wasm-decision.md                       # NEW (P4) — the go/no-go decision document

tests/
├── AkmlSql.Web.Tests/
│   └── Spike/
│       └── SpikeCorpusGoldenTests.cs         # NEW (P2) — desktop generator for the .expected.* golden files
└── AkmlSql.Web.E2E.Tests/
    └── SpikePageTests.cs                     # NEW (P1/P2) — Playwright: drive /spike in a real browser, assert corpus runs

specs/021-web-edition/
└── M1-SPIKE-RESULTS.md                       # OPTIONALLY UPDATED (P4) — back-pointer to docs/m1-wasm-decision.md; closes follow-up F2
```

**Structure Decision**: The existing standalone Blazor WebAssembly project `src/AkmlSql.Web/` accommodates the spike with **zero modifications to existing source files**. `Spike.razor` is auto-routed by the assembly-scanning `<Router>` in `App.razor`; `HttpClient`, `IFormatterService`, and `IAnalyserService` are already DI-registered in `Program.cs`. The T-SQL corpus and its desktop-generated golden files live as static assets under `wwwroot/spike-corpus/` (tens of KB — harmless to ship; the PRD keeps the spike surface as a permanent record). The decision document lands at `docs/m1-wasm-decision.md`. Two new test files extend the existing `AkmlSql.Web.Tests` (desktop golden generator) and `AkmlSql.Web.E2E.Tests` (Playwright browser check) projects. The only optionally-modified existing file is `specs/021-web-edition/M1-SPIKE-RESULTS.md` — a back-pointer that closes its open follow-up F2.

## Complexity Tracking

> Fill ONLY if Constitution Check has violations that must be justified.

No constitution violations. The spike introduces no new abstraction: one Razor page that reuses two existing services, a flat folder of `.sql` fixtures, one decision document, and two test files in pre-existing test projects.

## Post-Design Constitution Re-Check

Re-evaluated after Phase 1 (research, data model, contracts, quickstart): **all gates still PASS**. Phase 1 confirmed the spike adds no new abstraction beyond five small in-memory record types local to `Spike.razor` (see `data-model.md`) and three documentation contracts. The data model holds no persistent storage; the contracts describe one diagnostic page, one markdown deliverable, and a measurement procedure. No gate moved.
