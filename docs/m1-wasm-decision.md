# M1 — ScriptDom-in-WASM Runtime Spike & Decision Gate

**Decision document for milestone M1.** This is the durable, citable record of the M1
decision gate. It is committed and stays in the repository permanently, regardless of
the outcome.

- **Date**: 2026-05-21
- **Author**: Mohamed Khamis
- **Specification**: [`specs/023-m1-wasm-spike/spec.md`](../specs/023-m1-wasm-spike/spec.md)
- **Reproduction runbook**: [`specs/023-m1-wasm-spike/quickstart.md`](../specs/023-m1-wasm-spike/quickstart.md)
- **Spike surface**: `src/AkmlSql.Web/Pages/Spike.razor`, route `/spike`

## Environment

| Item | Value |
|------|-------|
| Machine | Windows 11 Pro N, 10.0.26220, x64, 16 logical cores |
| .NET SDK | **11.0.100-preview.3.26207.106** — a .NET 11 *preview* SDK (see note) |
| Target framework | `AkmlSql.Web` targets `net10.0`; referenced AKML libraries are `netstandard2.0` |
| WebAssembly workload | `wasm-tools` 11.0.100-preview.3 installed; the **`wasm-tools-net10`** variant is the one a `net10.0` target needs on an 11-preview SDK — see §2 and §5 |
| Browser (primary) | Google Chrome / Chromium **148.0.0.0**, Windows x64 (`crossOriginIsolated = false`) |
| Browsers (other) | Firefox / Safari — **not tested** in this spike (FR-023); see §7 |
| Local static server | `dotnet-serve` 1.10.194 |
| Driver | Playwright (Chromium) via the repository's MCP integration |

> **SDK note (confounder).** The PRD and spec assume a .NET 10 SDK. The build host
> carries a .NET **11 preview** SDK. An 11-preview SDK builds `net10.0` projects, but
> WebAssembly **AOT** and **runtime relinking** for a `net10.0` target require the
> `wasm-tools-net10` workload variant, *not* the `wasm-tools` (net11) variant. This was
> discovered during the spike (§5) and is recorded as an environmental confounder, not
> a defect of the web edition.

---

## 1 — Investigation matrix

All seven PRD investigation questions, each with an explicit verdict and the evidence
behind it.

| # | Question | Verdict | Evidence |
|---|----------|---------|----------|
| 1 | Does `Microsoft.SqlServer.TransactSql.ScriptDom` load in `browser-wasm`? | **PASS** | `/spike` opened in Chrome 148. All seven corpus items parsed; no `BadImageFormatException` / `TypeLoadException` / `PlatformNotSupportedException`; **0 browser-console errors** across the whole session (load, parse, format, analyse, invalid input, oversized file, full corpus run). |
| 2 | Does the formatter pipeline run end-to-end? | **PASS** | Corpus item `00-simple` formats fully in WASM — `Success=True, ValidationPassed=True, WasModified=True`; output byte-identical to the desktop golden file. All 7 items: formatter output **matches the desktop golden** (see §3). |
| 3 | What is the compressed WASM bundle size? | **PASS** | `_framework/*.br` (Brotli) total = **4.83 MB** (5,062,660 bytes) — relinked Release publish. Reference ≤ 25 MB — ~5× under. |
| 4 | What is the cold-load time? | **PASS** | First-visit (true cold) time-to-interactive = **936 ms**; in-process reload median = 420 ms. Reference ≤ 8 s — ~8.5× under. See §2. |
| 5 | Does AOT justify its build-time / size cost? | **No** — not for M2's default | AOT Parse & Format of the 50-line proc is ~1.7× faster (9.59 ms vs 16.29 ms interpreted), but AOT ~2.4× the compressed bundle (11.51 MB vs 4.83 MB) and the publish takes ~13.5 min (vs ~1 min). At tens-of-ms operations the speed-up is imperceptible; the size/build cost is not. See §2. |
| 6 | Do trim warnings exist? | **PASS** | `dotnet publish -c Release -p:TrimmerSingleWarn=false` — IL trimming ran (`partial`); **zero `IL2xxx` trim warnings**. The 4 build warnings were all `NU1903` (a transitive package-vulnerability advisory, unrelated to trimming — since fixed, see §5). |
| 7 | Are there missing-API runtime errors? | **PASS** | None. SELECT, multi-statement batch, ≥ 50-line stored procedure, CTE, window functions and MERGE all parse / format / analyse in WASM with no runtime exception; the analyser discovered **130 / 130** rules (§4). |

---

## 2 — Measurements

Every figure below is an actual measured value (SC-005).

### Bundle size

| Metric | Value | Method |
|--------|-------|--------|
| Compressed `_framework/` (`.br`) | **5,062,660 bytes ≈ 4.83 MB** | Sum of every `_framework/*.br` on disk after a relinked `dotnet publish -c Release` (120 files) |
| `_framework/` gzip (`.gz`) | 6,443,199 bytes ≈ 6.14 MB | Sum of `_framework/*.gz` |
| Uncompressed `_framework/` | 21,697,016 bytes ≈ 20.69 MB | Sum of `_framework/*` excluding `.br`/`.gz` (120 files) |

The prior `specs/021-web-edition/M1-SPIKE-RESULTS.md` recorded ≈ 45 MB uncompressed —
that was a `dotnet build` (no trimming). A `dotnet publish -c Release` IL-trims
(`partial`) and, with native runtime relinking, takes the uncompressed `_framework/`
to 20.69 MB; Brotli then takes the wire size to **4.83 MB**. Both this interpreted
figure and the AOT figure below are from **fully relinked** publishes
(`wasm-tools-net10` installed — §5), so they differ only in `RunAOTCompilation`.

### Cold-load time

| Metric | Value | Method |
|--------|-------|--------|
| First-visit cold load (true cold) | **936 ms** | Time to interactive (`/spike` first render). Fresh Chromium process, empty HTTP cache + Cache Storage, localhost. In-app `performance.mark('akml-spike-ready')`. |
| In-process reload | **420 ms median** (412 / 420 / 527 ms) | 3 reloads with Cache Storage cleared and the server sending `Cache-Control: no-store`. |

**Method honesty.** Both numbers are localhost-loopback figures (transfer cost
negligible; the time is WASM compile + Mono init + assembly load + render). A true
cold load needs a fresh *browser process* — Chromium retains its compiled-WebAssembly
cache across in-process reloads, so the 420 ms reloads are warm-process figures, not
true-cold. The harness produced one true-cold load (the first visit, 936 ms); the
3-sample median is the repeatable in-process reload figure. Both are far inside the
8 s reference. A realistic-network figure would add transfer time for the 4.83 MB
Brotli payload (≈ 1–5 s on typical broadband) and is an M2 concern, not a gate blocker.

### Execution time — interpreted vs AOT

| Metric | Value |
|--------|-------|
| Interpreted Parse & Format, `03-stored-proc` (≥ 50 lines) | **16.29 ms** (average of 10 runs after a warm-up pass) |
| AOT Parse & Format, `03-stored-proc` (≥ 50 lines) | **9.59 ms** (average of 10 runs after a warm-up pass) |
| AOT publish build duration | **813 s ≈ 13.5 min** (plus a one-time 163 s `wasm-tools-net10` workload install) |
| AOT compressed `_framework/` (`.br`) | **12,070,221 bytes ≈ 11.51 MB** (uncompressed 74.77 MB) |

AOT compiles the managed code to native WebAssembly: Parse & Format of the 50-line
stored procedure is **~1.7× faster** (9.59 ms vs 16.29 ms interpreted), but the
compressed bundle is **~2.4× larger** (11.51 MB vs 4.83 MB) and the publish takes
~13.5 minutes (vs ~1 minute interpreted). The AOT build also ran the full corpus in
the browser with every formatter and analyser result **still byte-identical to the
desktop golden** — AOT does not change correctness. For a SQL editor whose operations
are already in the single-to-tens-of-milliseconds range, the speed-up is imperceptible
and does not justify doubling the download; **AOT is not recommended as M2's default**
(see §1 Q5 and §7). `RunAOTCompilation` was supplied as a one-off publish flag and is
*not* committed to `AkmlSql.Web.csproj`.

### `Stopwatch` timer probe

| Metric | Value |
|--------|-------|
| `Stopwatch.Frequency` | 1,000,000,000 ticks/sec |
| Effective resolution | **100.00 µs** (smallest observed non-zero delta: 100,000 ticks) |

The resolution matches the ~100 µs browser high-resolution-clock clamp expected in
Chromium without cross-origin isolation (`crossOriginIsolated = false`). For the
tens-of-milliseconds operations measured here that is < 1 % quantisation error.

---

## 3 — Corpus results

Seven T-SQL corpus items, each run through the formatter pipeline and the analyser
**in the browser WebAssembly runtime**, every result diffed against a desktop-generated
golden file. The golden files are produced on desktop .NET by the *same*
`AkmlSql.Formatting` / `AkmlSql.Analysis` libraries — so a match proves the WASM
runtime produces output **identical** to desktop, and the only variable isolated is
the runtime itself.

| Item | Construct | Parse & Format | Analyse | Findings | Format vs golden | Analysis vs golden |
|------|-----------|----------------|---------|----------|------------------|--------------------|
| `00-simple` | Simple SELECT (messy) | ✓ 2.87 ms | ✓ 54.30 ms | 4 | ✓ match | ✓ match |
| `01-select` | SELECT + JOIN | ✓ 5.85 ms | ✓ 9.90 ms | 16 | ✓ match | ✓ match |
| `02-batch` | Multi-statement batch | ✓ 6.50 ms | ✓ 22.80 ms | 15 | ✓ match | ✓ match |
| `03-stored-proc` | Stored procedure (> 50 lines) | ✓ 16.29 ms | ✓ 38.10 ms | 79 | ✓ match | ✓ match |
| `04-cte` | Common table expressions | ✓ 9.22 ms | ✓ 18.50 ms | 40 | ✓ match | ✓ match |
| `05-window` | Window functions | ✓ 6.41 ms | ✓ 11.60 ms | 26 | ✓ match | ✓ match |
| `06-merge` | MERGE statement | ✓ 5.33 ms | ✓ 10.90 ms | 26 | ✓ match | ✓ match |

**No silent failure** — every corpus operation produced a recorded result (SC-003).
Every formatter result and every analyser result is **byte-identical to the desktop
golden** (FR-011) — the WASM runtime reproduces desktop behaviour exactly.

Parse & Format timings are the average of 10 runs after a warm-up pass; Analyse is a
single timed call (the analysis engine caches per-batch results, so a repeated run
would measure cache hits). The `00-simple` Analyse figure (54.30 ms) is elevated
because it is the first analyser invocation of the session and absorbs one-time
interpreter warm-up of the rule engine; subsequent items run an order of magnitude
faster.

### Note on the formatter on rich T-SQL (not a WASM finding)

`00-simple` exercises the **complete** formatter pipeline: parse → layout → casing →
emit → **semantic validation** → idempotency. It validates cleanly
(`ValidationPassed=True`) and the formatter visibly transforms the input
(`select   a, b, c   from dbo.Foo   where a > 1` → `SELECT a, b, c FROM dbo.Foo WHERE a > 1`).

Items `01`–`06` reproduce a **pre-existing desktop formatter behaviour**: on rich
T-SQL the pipeline's Stage 6 semantic validator reports "formatted output differs from
original" and the pipeline returns the input unchanged
(`ValidationPassed=False, WasModified=False`). This is **byte-identical between WASM
and desktop** (every `Format vs golden` cell is ✓), which is itself the runtime-
equivalence evidence the spike exists to produce. It is a known formatter gap on rich
SQL — tracked by spec 020's deferred formatter pipeline gap-closures (T074–T084) — and
is **not** a WASM-runtime finding. Fixing it is out of scope for M1 (FR-024).

---

## 4 — Analyser reflection survival

| Metric | Value |
|--------|-------|
| Rules discovered at runtime in WASM | **130** |
| Desktop baseline | 130 |
| Verdict | **PASS** |

`RuleRegistry` discovers analysis rules by reflection —
`Assembly.GetExecutingAssembly().GetTypes()` followed by `Activator.CreateInstance` on
every `IAnalysisRule` implementation. This is the single highest-risk WASM-trim
interaction: IL trimming can strip types that have no static references. The spike
constructs `RuleRegistry` directly in the browser and reports the discovered count.

**All 130 rules are discovered in the trimmed WASM build** — reflection-based rule
discovery survives `partial` trimming intact (SC-004, FR-010). The corpus analyser
results in §3 confirm the discovered rules actually *run*: findings counts
(4 / 16 / 15 / 79 / 40 / 26 / 26) are byte-identical to the desktop golden output.

---

## 5 — Trim warnings

`dotnet publish src/AkmlSql.Web/AkmlSql.Web.csproj -c Release -p:TrimmerSingleWarn=false`

IL trimming ran at `partial` granularity (the only mode Blazor supports);
`TrimmerSingleWarn=false` un-collapses the one-per-assembly summary into per-call
detail.

| `IL2xxx` warnings | Count | Disposition |
|-------------------|-------|-------------|
| (none) | **0** | No action required — the trimmer reported no warnings against any assembly, including `AkmlSql.Analysis` (the `RuleRegistry` reflection scan) and `ScriptDom`. |

The publish log carried 4 warnings total, **all** `NU1903` — *"Package
'Microsoft.Bcl.Memory' 9.0.4 has a known high severity vulnerability"* — a transitive
NuGet package-vulnerability advisory reached via `AkmlSql.AI`. It is a pre-existing
dependency-hygiene item, **not** a trim warning and not introduced by this spike.
**Fixed in this PR** (review follow-up): `AkmlSql.AI` now pins `Microsoft.Bcl.Memory`
to the patched **9.0.14** (GHSA-73j8-2gch-69rq, a Base64Url out-of-bounds-read DoS),
which clears NU1903 — a Release publish now reports zero build warnings.

**Relinking note.** The spike's first Release publish logged *"Publishing without
optimizations … recommend `wasm-tools` workload"* even though a `wasm-tools` workload
was installed. Root cause: the `net10.0` target on a .NET 11-preview SDK requires the
**`wasm-tools-net10`** workload variant; the spike initially installed `wasm-tools`
(the net11 variant). After installing `wasm-tools-net10`, both the interpreted and the
AOT publishes relink natively; the §2 bundle figures (interpreted 4.83 MB, AOT
11.51 MB) are both from relinked publishes that differ only in `RunAOTCompilation`.

---

## 6 — Outcome

**Clean pass.**

ScriptDom, the 7-stage formatter pipeline and the 130-rule reflection-discovered
analyser all **execute inside the Blazor WebAssembly runtime** with no runtime
exception, and produce output **byte-identical to desktop .NET**. The compressed
bundle (4.83 MB) and the first-visit cold load (936 ms) are both comfortably inside
the PRD reference thresholds (≤ 25 MB, ≤ 8 s). Trimming is clean (zero `IL2xxx`
warnings) and reflection-based rule discovery survives it fully.

The two qualifications, neither of which moves the outcome:

1. The formatter's Stage-6 semantic validator returns the input unchanged on rich
   T-SQL — a **pre-existing desktop formatter limitation** (spec 020 T074–T084),
   reproduced byte-identically in WASM, not a WASM finding (§3).
2. The .NET 11-preview SDK required the `wasm-tools-net10` workload variant for
   relinking / AOT — an **environmental confounder**, recorded (§5), not a defect of
   the web edition.

---

## 7 — Recommendation

**Go.** The in-browser ("thick browser") M2 architecture is sound. The single highest-
risk assumption of the entire web-edition plan — *"ScriptDom + the formatter + the
analyser run in WASM"* — is **empirically retired**: they run, and they run with
desktop-identical results.

Carry-forward notes for the M2 track (informational — none block M2):

- **Bundle / cold-load** are well within budget. M2's lazy-loading work remains a
  nice-to-have for realistic-network first-load, not a correctness requirement.
- **AOT**: measured ~1.7× faster formatting at ~2.4× the bundle and a ~13.5-min build —
  not recommended as M2's default. `RunAOTCompilation` stays a publish-time flag, never
  committed to the csproj. Revisit only if M2 profiling surfaces a genuine hot path.
- **Browser coverage**: this spike covered Chrome/Chromium 148 only. Firefox and
  Safari are **untested** (FR-023) — M2 should add a cross-browser pass before ship.
- **Workload**: the AOT/relinking workload variant is SDK-major-specific
  (`wasm-tools-net10` vs `wasm-tools`). M2's build documentation and CI should pin the
  correct variant for the project's target framework.
- **Formatter rich-SQL gap** (spec 020 T074–T084) is independent of the web edition
  but affects what the M2 in-browser formatter delivers on real user code.

---

## 8 — Consequences for M2

**Not applicable** — the outcome (§6) is a clean pass and the recommendation (§7) is
*Go*. This spike directs no rollback, redesign, or re-validation of the existing
`AkmlSql.Web` scaffold or any in-progress M2 surface (FR-020). The spike is purely
additive: one diagnostic page, a corpus folder, two test files and this document.

---

## 9 — Reproduction

Any maintainer can reproduce this outcome by following
[`specs/023-m1-wasm-spike/quickstart.md`](../specs/023-m1-wasm-spike/quickstart.md) —
build, generate the golden files, serve the Release publish, open `/spike` in a
Chromium browser, and run the corpus. The repeatable automated form is the Playwright
test `tests/AkmlSql.Web.E2E.Tests/SpikePageTests.cs` (run it against a served
instance via `AKML_SPIKE_BASE_URL`). The measurement procedures — bundle size,
cold-load, AOT-vs-interpreted, trim warnings — are specified in
[`specs/023-m1-wasm-spike/contracts/measurement-protocol.md`](../specs/023-m1-wasm-spike/contracts/measurement-protocol.md).
