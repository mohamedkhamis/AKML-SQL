# Quickstart — Running & Verifying the M1 WASM Spike

This guide is both the **spike runbook** (how to execute the M1 spike) and the **verification walkthrough** (how to confirm a spike PR is complete). The spike's "implementation" is largely the act of running it and recording what happened — so the steps below and the decision document are nearly the same artefact.

## Prerequisites

- A clean checkout on the `023-m1-wasm-spike` branch.
- .NET 10 SDK on `PATH`.
- A current Chromium-based browser (Chrome or Edge).
- For the AOT measurement: `dotnet workload install wasm-tools` (admin shell).
- For local serving: `dotnet tool install -g dotnet-serve`.

## 1 — Establish a clean build

```bash
dotnet build src/AkmlSql.Web/AkmlSql.Web.csproj -c Release
```

Expected: `0 Error(s)`. This confirms compile-time viability is still intact (it was already established in `specs/021-web-edition/M1-SPIKE-RESULTS.md`).

## 2 — Generate the golden comparison files

```bash
AKML_REGEN_GOLDEN=1 dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj --filter "SpikeCorpusGoldenTests" -c Release
```

Expected: `src/AkmlSql.Web/wwwroot/spike-corpus/` now contains `*.expected.sql` and `*.expected.json` next to each `*.sql`. These are produced by desktop .NET running the same `AkmlSql.Formatting` / `AkmlSql.Analysis` libraries the spike runs in WASM — so any later mismatch in the browser is a pure runtime finding. The generator is opt-in: the `AKML_REGEN_GOLDEN` environment variable gates the write, so a plain `dotnet test` (without it) never mutates the committed golden files.

## 3 — Run the spike in a browser (the core of the gate)

```bash
dotnet run --project src/AkmlSql.Web/AkmlSql.Web.csproj -c Release
```

Open the printed URL, then navigate to **`/spike`**.

Verify each of these and record the result for the decision document:

1. **SELECT** — pick `01-select` from the corpus dropdown, click **Parse & Format**. Expected: formatted SQL appears, no exception. *(SC-001 — investigation matrix Q1, Q2.)*
2. **50-line stored procedure** — pick `03-stored-proc`, click **Parse & Format**. Expected: formats end-to-end, no exception, no tab freeze. *(SC-002.)*
3. **Run all corpus** — click it. Expected: every row (SELECT, batch, stored proc, CTE, window, MERGE) shows output or a recorded, explained finding — no blank/silent row. *(SC-003 — matrix Q7.)*
4. **Analyser** — for each corpus item, the rule-discovery readout shows `discovered / 130`. Expected: 130 (or a recorded finding if trimming removed rules). *(SC-004 — FR-010.)*
5. **Golden match** — corpus rows show `formatted == golden` and `findings == golden`. Any mismatch is a recorded finding. *(FR-011.)*
6. **Timer probe** — note the reported `Stopwatch` resolution (expected ~100 µs in Chromium).
7. **Exception path** — paste deliberately invalid SQL, click **Parse & Format**. Expected: the parser error renders; the page stays responsive. Force a throw if possible and confirm the exception panel shows type + message + full stack. *(FR-005.)*

If any step throws `BadImageFormatException`, `TypeLoadException`, or `PlatformNotSupportedException` — that is a **Q1 FAIL**; capture the full stack trace.

## 4 — Measure compressed bundle size

```bash
dotnet publish src/AkmlSql.Web/AkmlSql.Web.csproj -c Release
```

Sum the `_framework/*.br` byte sizes in the publish output (`.../publish/wwwroot/_framework/`). Record the compressed total and the uncompressed total. See `contracts/measurement-protocol.md` M1. *(Matrix Q3; reference ≤ 25 MB.)*

## 5 — Measure cold-load

```bash
dotnet serve -d <publish-path>/wwwroot
```

In Chrome/Edge: DevTools → Application → **Clear site data**; Network → **Disable cache**; reload; record **time to first interactive render** from the Performance trace. Repeat ≥ 3×, take the median. See `contracts/measurement-protocol.md` M2. *(Matrix Q4; reference ≤ 8 s.)*

## 6 — Measure AOT vs interpreted

```bash
dotnet publish src/AkmlSql.Web/AkmlSql.Web.csproj -c Release -p:RunAOTCompilation=true
```

Time the build (wall clock — expect minutes). Serve it, open `/spike`, run **Parse & Format** on `03-stored-proc`, record the averaged ms. Compare against the interpreted number from step 3. Record the AOT publish's `_framework/*.br` sum. See `contracts/measurement-protocol.md` M3. *(Matrix Q5.)*

## 7 — Capture trim warnings

```bash
dotnet publish src/AkmlSql.Web/AkmlSql.Web.csproj -c Release -p:TrimmerSingleWarn=false
```

List every `IL2xxx` warning; give each a disposition. See `contracts/measurement-protocol.md` M4. *(Matrix Q6.)*

## 8 — Run the automated browser check

```bash
dotnet test tests/AkmlSql.Web.E2E.Tests/AkmlSql.Web.E2E.Tests.csproj --filter "SpikePageTests" -c Release
```

Expected: the Playwright test drives `/spike` in a real browser, runs the corpus, and asserts no runtime exception. This is the repeatable form of step 3.

## 9 — Write the decision document

Fill in `docs/m1-wasm-decision.md` per `contracts/decision-document.md`: the seven-question matrix with verdicts + evidence, the measurements from steps 4–7, the corpus results from step 3, the rule-discovery verdict, one outcome classification, and a go/no-go recommendation. If the outcome is not a clean pass, add § 8 (consequences for in-progress M2 work — without rolling anything back).

## 10 — Confirm additive-only

```bash
git status --short
```

Expected new/changed paths only: `src/AkmlSql.Web/Pages/Spike.razor`, `src/AkmlSql.Web/wwwroot/spike-corpus/*`, `docs/m1-wasm-decision.md`, `tests/AkmlSql.Web.Tests/Spike/*`, `tests/AkmlSql.Web.E2E.Tests/SpikePageTests.cs`, the `specs/023-m1-wasm-spike/*` artefacts, and optionally a back-pointer line in `specs/021-web-edition/M1-SPIKE-RESULTS.md`. **Nothing else** — no engine, shell, shared-shell, or existing `AkmlSql.Web` source file may appear. *(SC-008, SC-009.)*

## Health summary

A successful spike looks like this:

> `AkmlSql.Web` builds and publishes Release clean with `Spike.razor` present. Opened in a browser at `/spike`, ScriptDom parses and the formatter pipeline formats a 10-line SELECT and a 50-line stored procedure with no runtime exception. Every corpus item — batch, CTE, window function, MERGE — produces output or a recorded finding. The analyser discovers its rules (130, or a recorded shortfall) and produces findings. Compressed bundle size, cold-load time, and AOT-vs-interpreted parse times are measured actuals. `docs/m1-wasm-decision.md` answers all seven investigation questions with evidence, classifies one outcome, and gives a go/no-go recommendation. Nothing outside `AkmlSql.Web`'s additive surface, `docs/`, and the two test projects changed.

Anything that does not match means the spike is incomplete — reopen the failing item and reference the relevant contract under `specs/023-m1-wasm-spike/contracts/`.
