# Implementation Plan: M2 — Web Edition Formatter & Analyser MVP Closure

**Branch**: `024-m2-web-closure` | **Date**: 2026-05-26 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/024-m2-web-closure/spec.md`

## Summary

Close five deferred verification tasks from spec 021 Phase 3 so the M2 milestone has recorded evidence — not just shipped code — behind every Definition-of-Done checkbox. Five user stories, one per deferred task: a side-by-side theme parity audit (T036), a formatter parity test over a 20-script × 3-profile corpus (T041), an analyser parity test over the same corpus (T047), a Playwright User Story 1 E2E suite (T053), and a Release-publish bundle-size audit (T054).

The closure is a verification slice. No code in `src/AkmlSql.Web/` is rewritten; the only `src/` changes are CSS edits to close the top five visible gaps the theme audit surfaces. All other artefacts are tests under `tests/AkmlSql.Web.Tests/` and `tests/AkmlSql.Web.E2E.Tests/` plus two audit documents under `specs/021-web-edition/` that replace existing placeholders.

## Technical Context

**Language/Version**: C# 12 on .NET 10 (`net10.0`) for the web project + tests; spec-021 web library code is `netstandard2.0` so it runs unchanged in the Blazor WASM runtime.
**Primary Dependencies**: Blazor WebAssembly (already integrated via spec 021); xUnit + bUnit (already integrated via `tests/AkmlSql.Web.Tests/`); Playwright .NET (already integrated via `tests/AkmlSql.Web.E2E.Tests/`); Microsoft.SqlServer.TransactSql.ScriptDom (already proven to run in WASM by spec 023).
**Storage**: No new persistence. The five audit/test artefacts are checked-in static files: two markdown audit documents (`M2-THEME-PARITY-AUDIT.md`, `M2-BUNDLE-SIZE.md`) and screenshots / `*.expected.sql` / `*.expected.json` baseline files alongside the existing parity corpus.
**Testing**: `dotnet test` (xUnit + bUnit) for the parity tests under `tests/AkmlSql.Web.Tests/Format/` and `tests/AkmlSql.Web.Tests/Analyse/`; `dotnet test` (Playwright .NET) for the E2E suite under `tests/AkmlSql.Web.E2E.Tests/UserStory1Tests.cs`. No new test framework introduced.
**Target Platform**: Browser (Chromium primary; Firefox/Safari out-of-scope per spec 023 §7) for the E2E suite; Windows 11 with the full .NET SDK + WebAssembly tooling for the bundle-size audit (so trimming + Brotli match production behaviour); Windows workstation running both the WPF IDE plugin and `dotnet run --project src/AkmlSql.Web` simultaneously for the theme audit.
**Project Type**: Verification slice over an existing Blazor WASM web application (spec 021 Phase 3 / User Story 1). No new application surfaces.
**Performance Goals**: Playwright headline flow (paste 100-line stored procedure → format → analyse → see findings) ≤ 5 seconds wall-clock (FR-016, SC-004); bundle compressed `_framework/*.br` total within the M1 decision document's target (FR-020, SC-005).
**Constraints**:

- Shipped M2 code paths must remain unchanged except for the CSS edits the theme audit identifies as top-5 closures (spec 024 Overview).
- Parity test divergences must be **explicit findings with dispositions**, never silently accepted (FR-008, FR-011).
- The desktop baselines and the web edition runs must be on the same `master` commit; baseline drift handled by embedding the baseline revision into each baseline file (Edge Case "Baseline-revision drift").
- The Playwright harness must build the project before launching the browser to prevent stale-build false positives (Edge Case "Playwright test runs against a stale `dotnet run`").
- Bundle-size measurement is invalid unless Brotli compression is active during the publish (Edge Case "Bundle measurement on a machine without Brotli").

**Scale/Scope**: Five user stories; the theme audit's screenshot matrix is 3 themes × 2 surfaces = 6 paired captures plus up to 5 CSS-level closures; the formatter parity test covers ≥ 20 scripts × 3 profiles = 60 (script, profile) pairs; the analyser parity test covers the same 20 scripts × 1 default profile = 20 finding-set comparisons; the Playwright suite covers 4 acceptance scenarios; the bundle audit produces one compressed-total number plus a per-asset breakdown.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

No `.specify/memory/constitution.md` exists for this repository, so no constitution gates apply. The closure spec already constrains itself in three ways that serve as effective gates:

- **No new application surfaces.** All five user stories are verification work over the existing M2 code; the only `src/` edits are CSS adjustments the theme audit identifies (US1 / FR-003).
- **No new test framework.** Existing xUnit + bUnit + Playwright stacks are extended; no new harness is introduced.
- **No spec-021 task is modified** other than the two placeholder audit documents and the deferred-task notes for T036, T041, T047, T053, T054, which flip from `[ ]` to `[X]` as their backing artefacts land (SC-006).

These three self-imposed gates are checked again in the Post-Design re-evaluation below.

## Project Structure

### Documentation (this feature)

```text
specs/024-m2-web-closure/
├── plan.md                                          # This file (/speckit.plan command output)
├── spec.md                                          # Already written by /speckit.specify
├── research.md                                      # Phase 0 output — five decisions, one per US
├── data-model.md                                    # Phase 1 output — five entities
├── quickstart.md                                    # Phase 1 output — how to run all five user stories
├── contracts/                                       # Phase 1 output — formats and harness contracts
│   ├── theme-audit-format.md
│   ├── parity-baseline-format.md
│   ├── playwright-harness-contract.md
│   └── bundle-measurement-protocol.md
├── checklists/
│   └── requirements.md                              # Created by /speckit.specify; all green
└── tasks.md                                         # Phase 2 output (/speckit.tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
└── AkmlSql.Web/
    └── wwwroot/
        └── css/                                     # ← may receive up to 5 edits (US1 / FR-003)
            ├── editor.css
            ├── chrome.css
            └── themes/
                ├── light.css
                ├── dark.css
                └── high-contrast.css

tests/
├── AkmlSql.Web.Tests/
│   ├── Format/
│   │   └── FormatterServiceTests.cs                 # ← extend existing 7-test class with parity tests (US2)
│   ├── Analyse/
│   │   └── AnalyserServiceTests.cs                  # ← extend existing class with parity tests (US3)
│   └── Parity/                                      # ← NEW; shared infra for US2 + US3
│       ├── ParityCorpusLoader.cs                    # walks tests/format-parity/corpus/ + reads baselines
│       ├── ParityBaselineGenerator.cs               # opt-in [Trait("Category","ParityBaseline")] generator
│       └── ParityDispositionsRegistry.cs            # accepted-with-reason entries → spec-020 links
└── AkmlSql.Web.E2E.Tests/
    ├── UserStory1Tests.cs                           # ← NEW; the four US1 acceptance scenarios (US4)
    └── Harness/
        ├── DotnetRunFixture.cs                      # builds + launches dotnet run, tears down on dispose
        └── HeadlineFlowTimer.cs                     # captures paste→format→analyse wall clock

tests/format-parity/                                 # ← spec-020 corpus; reused, not modified
├── corpus/                                          # ≥ 20 representative scripts
└── baselines/                                       # ← NEW subfolder of the existing corpus dir
    ├── default/
    │   ├── 01-select.expected.sql                   # IDE plugin baseline outputs (per profile)
    │   └── ...
    ├── compact/
    └── expanded/

specs/021-web-edition/
├── M2-THEME-PARITY-AUDIT.md                         # ← replace placeholder (US1 / FR-004)
├── M2-BUNDLE-SIZE.md                                # ← replace placeholder (US5 / FR-018)
└── screenshots/                                     # ← NEW; embedded into the theme audit
    ├── light-wpf.png       light-web.png
    ├── dark-wpf.png        dark-web.png
    └── high-contrast-wpf.png   high-contrast-web.png
```

**Structure Decision**: Verification slice over an existing single-tree solution. All new code is test code under `tests/AkmlSql.Web.Tests/` (parity infra + extended test classes) and `tests/AkmlSql.Web.E2E.Tests/` (Playwright suite + harness fixture). All new artefacts beyond test code are checked-in static files under `specs/021-web-edition/` (two audit documents + screenshot pairs) and a `baselines/` subfolder of the existing spec-020 parity corpus at `tests/format-parity/`. The only `src/` writes are scoped to `wwwroot/css/` for the top-5 visual gap closures the theme audit surfaces — no service, no component, no shared library is touched.

## Phase 2 planning note

Tasks are generated by `/speckit.tasks`, not here. The tasks file will turn each user story into a sequence of concrete tasks: in US1 order, capture six screenshots → write the deltas table → close the top-5 CSS gaps → file remaining gaps; in US2/US3, build the parity-baseline generator → run it once → wire the parity test → record dispositions; in US4, write the Playwright fixture → encode the four scenarios → assert the headline-flow timer; in US5, run the Release publish → measure → record verdict → (if over) apply lazy-loading.

## Complexity Tracking

No constitution gate violations to justify (no constitution). The three self-imposed gates from the Constitution Check section all hold post-design:

- **No new application surfaces** — the only `src/` writes are CSS in `wwwroot/css/`.
- **No new test framework** — every new test file uses the existing xUnit / bUnit / Playwright .NET stack already configured in `tests/AkmlSql.Web.Tests/` and `tests/AkmlSql.Web.E2E.Tests/`.
- **Spec-021 surfaces untouched** — only the two placeholder audit documents and the five deferred-task notes change in `specs/021-web-edition/`.

Every artefact listed in the Project Structure block is either a test file, a checked-in document, a baseline data file, or a CSS edit. No new persistence layer, no new service interface, no new public API. Closure spec discipline holds.
