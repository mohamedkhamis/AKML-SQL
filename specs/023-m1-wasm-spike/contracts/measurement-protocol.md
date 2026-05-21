# Contract — Measurement Protocol

**Status**: New procedure for P3 of spec 023. Defines how the three quantified measurements (bundle size, cold-load, AOT-vs-interpreted) and the supporting numbers are produced **reproducibly**, so the decision document's figures are actuals and SC-010 (a maintainer can reproduce the outcome) holds.

All commands assume a Windows x64 host with the .NET 10 SDK on `PATH`, run from the repository root.

## Prerequisites

```bash
# AOT measurement only — installs the WebAssembly build tools (admin shell).
dotnet workload install wasm-tools

# Local serving for the cold-load run.
dotnet tool install -g dotnet-serve
```

`wasm-tools` also enables runtime relinking of `dotnet.wasm` on *every* Release publish — keep it installed for **both** the interpreted and AOT publishes so relinking is constant and only `RunAOTCompilation` varies.

## M1 — Compressed bundle size

```bash
dotnet publish src/AkmlSql.Web/AkmlSql.Web.csproj -c Release
```

- Locate the publish output — either `bin/Release/net10.0/publish/wwwroot/_framework/` or `bin/Release/net10.0/browser-wasm/publish/wwwroot/_framework/` (check both).
- **Compressed size** = sum of the byte sizes of every `_framework/*.br` file. Brotli `.br` siblings are emitted by default at highest level on a Release publish; they are exactly what a Brotli-capable production host transfers.
- Also record the **uncompressed** `_framework/` total (sum of the non-`.br`, non-`.gz` files) for continuity with the ≈ 45 MB in `M1-SPIKE-RESULTS.md`.
- Record both in decision-document § 2; compare the compressed figure against the ≤ 25 MB reference for § 1 question 3.

> A plain static server (including `dotnet-serve` default) does not negotiate `.br`, so the browser Network tab on such a host shows the **uncompressed** transfer. The disk-`.br`-sum is the faithful figure; the Network tab is used only to confirm `Content-Encoding: br` when served by a negotiating host.

## M2 — Cold-load time

1. Serve the Release publish: `dotnet serve -d <publish>/wwwroot` (or IIS/Nginx for Brotli negotiation + HTTP/2).
2. Open a current Chromium browser (Chrome or Edge), DevTools open, **no debugger attached to the app**.
3. Force a true cold load — Blazor caches boot resources in **Cache Storage**, not just the HTTP cache:
   - DevTools → Application → **Clear site data** (or use a fresh incognito window per run), and
   - DevTools → Network → **Disable cache**.
4. Reload; from the Performance trace / Network waterfall record **time to first interactive render** (and FCP / LCP).
5. Repeat ≥ 3 times; record the **median**, plus machine, browser+version, and serving host in decision-document § 2.
6. Optional precise in-app number: the spike emits `performance.mark('akml-spike-ready')` on first render; `performance.measure` from navigation start gives an exact figure.

Record the method honestly: a localhost loopback figure is optimistic; note it and, if a realistic-network number is wanted, apply DevTools network throttling.

## M3 — AOT vs interpreted

```bash
# Publish A — interpreted (default), runtime-relinked.
dotnet publish src/AkmlSql.Web/AkmlSql.Web.csproj -c Release

# Publish B — AOT. Publish-only; expect several minutes. Time the wall clock.
dotnet publish src/AkmlSql.Web/AkmlSql.Web.csproj -c Release -p:RunAOTCompilation=true
```

- For each publish, serve it, open `/spike`, run **Parse & Format** on the ≥ 50-line stored-procedure corpus item, and record the averaged execution time (the spike does warmup + N-iteration averaging).
- Record: interpreted parse/format ms, AOT parse/format ms, AOT publish build duration (wall clock), and the AOT publish's compressed `_framework/*.br` sum.
- `AkmlSql.Web.csproj` is **not** modified — `RunAOTCompilation` stays a command-line flag. Whether to adopt AOT is an M2 decision the spike informs.

## M4 — Trim warnings

```bash
dotnet publish src/AkmlSql.Web/AkmlSql.Web.csproj -c Release -p:TrimmerSingleWarn=false
```

- A Release publish trims (`partial`) by default; `TrimmerSingleWarn=false` un-collapses the one-per-assembly summary into per-call detail.
- List every `IL2xxx` warning in decision-document § 5; give each a disposition (`Resolved`, or `SafeToIgnore` with evidence).
- Pay special attention to warnings naming `AkmlSql.Analysis` (the `RuleRegistry` reflection scan) or ScriptDom.

## M5 — `Stopwatch` timer probe

On first render the spike runs a microbench: tight-loop `Stopwatch.GetTimestamp()`, report `Stopwatch.Frequency` and the smallest observed non-zero delta → effective resolution. Record in § 2. Expected: resolution tracks the browser clamp (~100 µs in Chromium without cross-origin isolation) — fine for tens-of-millisecond operations.

## M6 — Golden-file generation (engine-output comparison)

The desktop generator `SpikeCorpusGoldenTests` (in `AkmlSql.Web.Tests`) runs `FormatterPipeline.Format` and `AnalysisEngine.AnalyzeAsync` — the same libraries — on desktop .NET over every corpus `.sql`, writing `{name}.expected.sql` and `{name}.expected.json` into `wwwroot/spike-corpus/`. These golden files are committed. The spike (in WASM) fetches and diffs against them; the **only** variable between golden and spike output is the runtime, so a mismatch is a pure WASM finding.

## Invariants

1. Every recorded number is a measured actual, not an estimate (SC-005).
2. The interpreted and AOT publishes differ only in `RunAOTCompilation`; `wasm-tools` is installed for both.
3. Cold-load is measured under genuinely cold conditions (Cache Storage cleared), Release build, no debugger.
4. No measurement step modifies a committed file except the generated golden files under `wwwroot/spike-corpus/`.
