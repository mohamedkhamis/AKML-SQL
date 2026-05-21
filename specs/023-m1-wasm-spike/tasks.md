---
description: "Task list for M1 — ScriptDom-in-WASM Runtime Spike & Decision Gate"
---

# Tasks: M1 — ScriptDom-in-WASM Runtime Spike & Decision Gate

**Input**: Design documents from `/specs/023-m1-wasm-spike/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: No TDD was requested. This is a spike — its verification of record is the **in-browser run**, captured in `docs/m1-wasm-decision.md`. The two files in test projects (the desktop golden generator T017 and the Playwright browser check T022) are plan-mandated deliverables, not speculative unit tests, and appear below as ordinary implementation tasks. No bUnit unit tests are generated — they execute on desktop .NET and cannot prove WASM viability.

**Organization**: Tasks are grouped by user story. This spike is naturally sequential — US2, US3, and US4 build on the `Spike.razor` page from US1 — but each story remains independently *testable*.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story the task belongs to (US1–US4); omitted for Setup / Foundational / Polish

## Path Conventions

Single-tree solution. The spike is additive within `src/AkmlSql.Web/`; tests extend the existing `tests/AkmlSql.Web.Tests/` and `tests/AkmlSql.Web.E2E.Tests/` projects; the decision document lands in `docs/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm the starting point and install the tooling the measurement phase needs.

- [X] T001 Confirm the clean baseline — run `dotnet build src/AkmlSql.Web/AkmlSql.Web.csproj -c Release` and confirm `0 Error(s)`; this is the pre-spike starting state, later noted in `docs/m1-wasm-decision.md`.
- [X] T002 [P] Install the .NET WebAssembly build tools workload (`dotnet workload install wasm-tools`) — required for the Phase 5 AOT measurement; confirm with `dotnet workload list`.
- [X] T003 [P] Install the local static file server (`dotnet tool install -g dotnet-serve`) — required for the Phase 5 cold-load measurement.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Author the shared T-SQL corpus that every user story consumes (US1 uses the SELECT; US2 uses all six; US3 times the stored procedure).

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T004 [P] Author `src/AkmlSql.Web/wwwroot/spike-corpus/01-select.sql` — a ~10-line `SELECT` with a JOIN, a WHERE clause, and ORDER BY.
- [X] T005 [P] Author `src/AkmlSql.Web/wwwroot/spike-corpus/02-batch.sql` — a multi-statement batch (DECLARE/SET, INSERT, UPDATE, SELECT) with `GO` separators.
- [X] T006 [P] Author `src/AkmlSql.Web/wwwroot/spike-corpus/03-stored-proc.sql` — a `CREATE PROCEDURE` of **≥ 50 lines** with parameters, control-flow (IF/BEGIN/END), and several statements.
- [X] T007 [P] Author `src/AkmlSql.Web/wwwroot/spike-corpus/04-cte.sql` — a query using one or more common table expressions, including a recursive CTE.
- [X] T008 [P] Author `src/AkmlSql.Web/wwwroot/spike-corpus/05-window.sql` — a query using window functions (`ROW_NUMBER`, `SUM() OVER`, `LAG`) with `PARTITION BY` / `ORDER BY`.
- [X] T009 [P] Author `src/AkmlSql.Web/wwwroot/spike-corpus/06-merge.sql` — a `MERGE` statement with `WHEN MATCHED` and `WHEN NOT MATCHED` clauses.
- [X] T010 [P] Author `src/AkmlSql.Web/wwwroot/spike-corpus/corpus.json` — the manifest array of the six items (`id`, `displayName`, `description`, `construct`, `sqlPath`, `expectedFormattedPath`, `expectedAnalysisPath`) per `data-model.md` Entity 1.

**Checkpoint**: The corpus exists and is fetchable as static content — user stories can begin.

---

## Phase 3: User Story 1 — Prove ScriptDom parses and formats T-SQL in the browser (Priority: P1) 🎯 MVP

**Goal**: A working `/spike` diagnostic page that parses and formats a SELECT inside the WASM runtime with zero runtime exception — retiring the single highest-risk assumption of the web-edition plan.

**Independent Test**: Serve the app, open `/spike` with no engine process running, load `01-select.sql`, click Parse & Format → formatted SQL appears with no `BadImageFormatException` / `TypeLoadException` / `PlatformNotSupportedException`; invalid SQL renders a parser error without crashing the page.

- [X] T011 [P] [US1] Create `src/AkmlSql.Web/Pages/SpikeModels.cs` — the `internal record` types `SpikeCorpusItem`, `OperationOutcome`, and `SpikeRunResult` per `data-model.md` Entities 1–3 (`internal` so `AkmlSql.Web.Tests` sees them via the existing `InternalsVisibleTo`).
- [X] T012 [US1] Create `src/AkmlSql.Web/Pages/Spike.razor` at `@page "/spike"` per `contracts/spike-page.md` — scaffold with `@inject IFormatterService`, `@inject IAnalyserService`, `@inject HttpClient`; a SQL `<textarea>`; an `<InputFile>` `.sql` loader that guards the 10 MB document-size limit (shows the limit message instead of loading an oversized file — spec Edge Case "Oversized `.sql` file"); and a corpus `<select>` populated by fetching `spike-corpus/corpus.json`.
- [X] T013 [US1] Implement the Parse & Format action in `src/AkmlSql.Web/Pages/Spike.razor` — call `IFormatterService.Format` (full pipeline), time it with `Stopwatch` (one warmup pass + N-iteration average per `research.md` Decision 5), render `FormatResult.FormattedText` to a `<pre>` and show the elapsed ms.
- [X] T014 [US1] Implement the exception panel in `src/AkmlSql.Web/Pages/Spike.razor` — wrap every parse/format/analyse call so any throw is caught and rendered verbatim (exception **type + message + full stack trace**) while the page stays responsive (FR-005).
- [X] T015 [US1] Implement the `Stopwatch` timer microbench in `src/AkmlSql.Web/Pages/Spike.razor` — on first render, compute and display `Stopwatch.Frequency` and the smallest observed non-zero delta (`research.md` Decision 5; `contracts/measurement-protocol.md` M5).
- [X] T016 [US1] Verify User Story 1 in a browser per `specs/023-m1-wasm-spike/quickstart.md` §3 step 1 — `dotnet run -c Release`, open `/spike`, load `01-select.sql`, Parse & Format → formatted output with zero runtime exceptions; paste invalid SQL → parser error renders, page responsive; load an oversized `.sql` file (> 10 MB) → the document-size-limit message renders without freezing the tab (spec Edge Case "Oversized `.sql` file"). Capture the evidence for `docs/m1-wasm-decision.md` matrix Q1–Q2 (SC-001).

**Checkpoint**: ScriptDom + the formatter pipeline are proven to execute in the browser — the core M1 risk is retired. This is a shippable MVP.

---

## Phase 4: User Story 2 — Validate rich T-SQL and the reflection-discovered analyser (Priority: P2)

**Goal**: The spike runs all six corpus items through the formatter and the analyser in the browser, reports the discovered-rule count, and diffs every result against desktop-generated golden output.

**Independent Test**: Click "Run all corpus" — every item yields formatted output / findings or a recorded, explained finding (no silent failure); the ≥ 50-line stored procedure formats end-to-end; the discovered-rule count is shown; golden matches are computed.

- [X] T017 [P] [US2] Create the desktop golden generator `tests/AkmlSql.Web.Tests/Spike/SpikeCorpusGoldenTests.cs` — an opt-in test tagged `[Trait("Category", "SpikeGenerator")]` (excluded from normal CI runs) that reads `corpus.json`, runs `FormatterPipeline.Format` and `AnalysisEngine.AnalyzeAsync` on each `.sql`, and writes `{id}.expected.sql` + `{id}.expected.json` into `src/AkmlSql.Web/wwwroot/spike-corpus/` (`contracts/measurement-protocol.md` M6; `research.md` Decision 4).
- [X] T018 [US2] Run the golden generator (`dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj --filter "Category=SpikeGenerator"`) and commit the produced `src/AkmlSql.Web/wwwroot/spike-corpus/*.expected.sql` and `*.expected.json` files.
- [X] T019 [US2] Implement the Analyse action in `src/AkmlSql.Web/Pages/Spike.razor` — call `IAnalyserService.AnalyseAsync`, time it, render the `AnalysisDiagnostic` findings list (RuleId / Severity / Message / Line:Column).
- [X] T020 [US2] Implement the rule-discovery readout in `src/AkmlSql.Web/Pages/Spike.razor` — construct a `RuleRegistry` directly and display the discovered-rule count against the desktop baseline of 130 (FR-010; `research.md` Decision 10).
- [X] T021 [US2] Implement "Run all corpus" + golden diff in `src/AkmlSql.Web/Pages/Spike.razor` — iterate the corpus, run format + analyse on each, fetch `{id}.expected.sql` / `{id}.expected.json`, diff, and render a per-item result table with format/analyse outcome and golden-match indicators (FR-009, FR-011).
- [X] T022 [US2] Create the Playwright browser test `tests/AkmlSql.Web.E2E.Tests/SpikePageTests.cs` — drive `/spike` in a real Chromium browser, trigger "Run all corpus", and assert no runtime exception surfaces and every corpus row resolves (SC-010; `contracts/spike-page.md`).
- [X] T023 [US2] Verify User Story 2 in a browser per `specs/023-m1-wasm-spike/quickstart.md` §3 steps 2–5 — run all six corpus items; confirm the ≥ 50-line stored procedure formats end-to-end with no exception (SC-002); every item yields output/findings or a recorded finding, no silent failure (SC-003); the discovered-rule count is recorded (SC-004); golden matches/mismatches recorded (FR-011). Capture evidence for `docs/m1-wasm-decision.md` matrix Q7 and §3.

**Checkpoint**: The spike is proven against real-world T-SQL and the reflection-based analyser, not just a trivial SELECT.

---

## Phase 5: User Story 3 — Quantify the cost of running ScriptDom in the browser (Priority: P3)

**Goal**: Actual measured numbers — compressed bundle size, cold-load time, AOT-vs-interpreted parse/format time — plus the trim-warning list.

**Independent Test**: Inspect the recorded results — each of the three measurements is an actual number with its measurement method noted.

- [X] T024 [US3] Measure the compressed bundle size per `contracts/measurement-protocol.md` M1 — `dotnet publish src/AkmlSql.Web/AkmlSql.Web.csproj -c Release`; sum `_framework/*.br` on disk; record the compressed total and the uncompressed `_framework/` total. Feeds `docs/m1-wasm-decision.md` §2 and matrix Q3.
- [X] T025 [US3] Capture trim warnings per `contracts/measurement-protocol.md` M4 — `dotnet publish src/AkmlSql.Web/AkmlSql.Web.csproj -c Release -p:TrimmerSingleWarn=false`; list every `IL2xxx` warning and assign each a disposition (resolved / safe-to-ignore + evidence), flagging any that implicate `AkmlSql.Analysis` or ScriptDom. Feeds `docs/m1-wasm-decision.md` §5 and matrix Q6.
- [X] T026 [US3] Measure cold-load per `contracts/measurement-protocol.md` M2 — serve the Release publish with `dotnet serve`, open it in a Chromium browser with site storage cleared and no debugger attached, record time-to-first-interactive-render as the median of ≥ 3 runs with machine and browser noted. Feeds `docs/m1-wasm-decision.md` §2 and matrix Q4.
- [X] T027 [US3] Measure AOT vs interpreted per `contracts/measurement-protocol.md` M3 — `dotnet publish src/AkmlSql.Web/AkmlSql.Web.csproj -c Release -p:RunAOTCompilation=true` (time the build); run Parse & Format on `03-stored-proc.sql` in both the interpreted and the AOT publish; record both execution times, the AOT build duration, and the AOT compressed `_framework/*.br` total. Feeds `docs/m1-wasm-decision.md` §2 and matrix Q5.

**Checkpoint**: The cost of the in-browser architecture is quantified with real numbers.

---

## Phase 6: User Story 4 — A durable, evidenced decision document (Priority: P4)

**Goal**: `docs/m1-wasm-decision.md` written per `contracts/decision-document.md` — the seven-question investigation matrix, the measurements, the corpus results, one outcome classification, and a go/no-go recommendation.

**Independent Test**: The file exists, answers all seven matrix questions with verdict + evidence, records the three measurements, states exactly one outcome, and gives a go/no-go recommendation.

- [X] T028 [US4] Create `docs/m1-wasm-decision.md` per `contracts/decision-document.md` — the header + environment block (machine, OS, browser + version, .NET SDK, `wasm-tools` status, **and the Firefox/Safari coverage status per FR-023**) and the §1 seven-row investigation-matrix table skeleton.
- [X] T029 [US4] Fill `docs/m1-wasm-decision.md` §1–§5 — transcribe the US1/US2 verification evidence and the US3 measurements: the seven matrix verdicts with evidence, §2 measurements, §3 corpus result table, §4 analyser reflection-survival verdict (discovered vs 130), §5 trim-warning list with dispositions.
- [X] T030 [US4] Complete `docs/m1-wasm-decision.md` §6–§9 — classify the outcome as exactly one of {clean pass, works but heavy, does not work}, give the go/no-go recommendation, add §8 M2-consequences if the outcome is not a clean pass (no rollback directive — FR-020), and §9 the reproduction pointer to `specs/023-m1-wasm-spike/quickstart.md`.

**Checkpoint**: The M1 decision gate has a durable, citable, evidenced record.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Close the spec's cross-cutting requirements and confirm the work is additive-only.

- [X] T031 [P] Update `specs/021-web-edition/M1-SPIKE-RESULTS.md` — add a back-pointer to `docs/m1-wasm-decision.md` noting the M1 in-browser runtime spike is complete, closing its open follow-up F2.
- [X] T032 Verify additive-only per `specs/023-m1-wasm-spike/quickstart.md` §10 — `git status` shows only the expected new paths; confirm no file under the engine, the six shell extensions, the shared shell project, or existing `AkmlSql.Web` source (`Program.cs`, `App.razor`, `AkmlSql.Web.csproj`) is modified; confirm `dotnet publish src/AkmlSql.Web/AkmlSql.Web.csproj -c Release` still succeeds (SC-008, SC-009, FR-022).
- [X] T033 [P] Cross-check `specs/023-m1-wasm-spike/quickstart.md` end-to-end — confirm every step reproduces the recorded outcome and the health-summary paragraph holds (SC-010).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: After Setup. **Blocks all user stories.**
- **User Story 1 (Phase 3)**: After Foundational. The MVP. No dependency on other stories.
- **User Story 2 (Phase 4)**: After US1 — T019–T021 extend the `Spike.razor` page created in T012. Independently testable given US1.
- **User Story 3 (Phase 5)**: After US1 — the AOT measurement (T027) runs Parse & Format on the spike page. Independent of US2; **US2 and US3 can run in parallel.**
- **User Story 4 (Phase 6)**: After US1, US2, and US3 — it transcribes their evidence.
- **Polish (Phase 7)**: After US4.

### Critical path

`Setup → Foundational → US1 → (US2 ∥ US3) → US4 → Polish`

### Within each user story

- US1: T011 → T012 → T013 → T014 → T015 → T016. T011 is a separate file; T012–T015 all edit `Spike.razor` (sequential); T016 is the in-browser verification.
- US2: T017 (separate project, parallel with the page work) and T018 (after T017); T019 → T020 → T021 all edit `Spike.razor` (sequential); T022 after T021; T023 verifies everything.
- US3: T024 → T025 → T026 → T027 — sequential (each publishes/serves the one app).
- US4: T028 → T029 → T030 — sequential (same file).

## Parallel Opportunities

- **Setup**: T002 and T003 run in parallel.
- **Foundational**: T004–T010 are seven independent files — all `[P]`, fully parallel.
- **US1**: T011 (`SpikeModels.cs`) runs parallel to nothing else in-story but is independent.
- **US2**: T017 (golden generator, separate project) runs in parallel with the `Spike.razor` tasks T019–T021.
- **Story level**: once US1 is done, **US2 and US3 can be worked in parallel** by different people.
- **Polish**: T031 and T033 run in parallel.

### Parallel Example: Foundational corpus

```bash
# T004–T010 — all seven corpus files authored together:
Task: "Author wwwroot/spike-corpus/01-select.sql"
Task: "Author wwwroot/spike-corpus/02-batch.sql"
Task: "Author wwwroot/spike-corpus/03-stored-proc.sql"
Task: "Author wwwroot/spike-corpus/04-cte.sql"
Task: "Author wwwroot/spike-corpus/05-window.sql"
Task: "Author wwwroot/spike-corpus/06-merge.sql"
Task: "Author wwwroot/spike-corpus/corpus.json"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1: Setup — confirm the baseline, install tooling.
2. Phase 2: Foundational — author the corpus.
3. Phase 3: User Story 1 — build `Spike.razor`, prove a SELECT parses + formats in the browser.
4. **STOP and VALIDATE** — open `/spike`, run the SELECT, confirm zero runtime exceptions.

At this point the single highest-risk assumption of the entire web-edition plan is empirically retired — that alone is a worthwhile, shippable increment.

### Incremental Delivery

1. Setup + Foundational → corpus ready.
2. US1 → the spike page proves runtime viability on a SELECT → **MVP**.
3. US2 → rich T-SQL + analyser + golden comparison → confidence beyond the trivial case.
4. US3 → measured bundle, cold-load, AOT numbers → cost is quantified.
5. US4 → `docs/m1-wasm-decision.md` → the decision gate has its record.
6. Polish → back-pointer, additive-only verification, quickstart cross-check.

Per the spec's Definition of Done, the branch is merged to master via PR **regardless of the go/no-go outcome** — the spike page and the decision document remain as the permanent record of the M1 gate.

### Parallel Team Strategy

With two people: after US1 lands, one takes US2 (page features + golden generator + E2E test) while the other takes US3 (the three measurement runs). They reconverge for US4.

---

## Notes

- **No TDD**: this is a spike; verification of record is the in-browser run, captured in `docs/m1-wasm-decision.md`. The golden generator (T017) and the Playwright check (T022) are deliverables, not red-green-refactor tests.
- **Additive-only**: no existing `AkmlSql.Web` source file is modified — `Program.cs`, `App.razor`, and `AkmlSql.Web.csproj` are untouched (`HttpClient` + services already registered; `<Router>` auto-routes `@page`). T032 verifies this.
- **AOT / trim flags are publish-time only** — `RunAOTCompilation` and `TrimmerSingleWarn` are never committed to the csproj.
- `[P]` = different files, no dependency on an incomplete task. `[Story]` labels (US1–US4) map tasks to spec.md user stories.
- Many tasks (T016, T023–T027, T032–T033) are *run-and-record* tasks — inherent to a spike. Each references the contract or quickstart procedure it executes and the section of `docs/m1-wasm-decision.md` it feeds.
- Commit after each task or logical group. Per the project git rule, committing happens only on explicit user instruction.
