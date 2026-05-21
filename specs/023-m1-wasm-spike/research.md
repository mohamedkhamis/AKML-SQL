# Phase 0 — Research: M1 ScriptDom-in-WASM Runtime Spike

The spec contains **zero `[NEEDS CLARIFICATION]` markers** — its one scope-significant ambiguity (greenfield vs. closure) was resolved with the user before drafting. Research here is not about resolving open requirements; it is about pinning down the *method* decisions for running the spike well, each backed by the three Phase 0 research reports (Blazor WASM AOT/publish/compression; Blazor WASM cold-load/timing; the AKML-SQL formatter/analyser codebase) so a future maintainer can trace the rationale.

Key version-sensitive facts established by research (.NET 10, current Chromium):

- WASM AOT is enabled by `<RunAOTCompilation>true</RunAOTCompilation>`, requires the `wasm-tools` SDK workload, runs **only on publish**, takes minutes, and roughly **doubles** bundle size (AOT does not trim managed assemblies).
- Installing `wasm-tools` *also* enables **runtime relinking** of `dotnet.wasm` on every Release publish — a size win independent of AOT.
- A Release publish trims (`partial` granularity, the only supported mode for Blazor) and statically compresses output to `.br` (Brotli, highest level) + `.gz` **by default**. The .NET 10 property to disable compression is `CompressionEnabled` (renamed from the obsolete `BlazorEnableCompression`).
- `_framework/` assemblies are **Webcil-packaged `.wasm` files** (not `.dll`) since .NET 8.
- `System.Diagnostics.Stopwatch` works in WASM but resolves to a browser high-resolution clock clamped to **~100 µs** in Chromium (5 µs only under cross-origin isolation; 1 ms in Firefox).
- The formatter entry point is `FormatterPipeline.Format(string sql, FormattingProfile profile)` (parser invoked internally); the analyser entry point is `AnalysisEngine.AnalyzeAsync(...)`; `RuleRegistry` discovers **130** rules by reflection (`Assembly.GetExecutingAssembly().GetTypes()` + `Activator.CreateInstance`). `AkmlSql.Web` already has real `FormatterService` / `AnalyserService` wrappers registered in DI.

## Decision 1: Reuse the existing `IFormatterService` / `IAnalyserService`

**Decision**: `Spike.razor` `@inject`s the DI-registered `IFormatterService` and `IAnalyserService`. For the rule-discovery count it additionally constructs a `RuleRegistry` directly.

**Rationale**: Those services already wrap `FormatterPipeline.Format` and `AnalysisEngine.AnalyzeAsync` in-process and are the exact path the M2 `Editor.razor` uses — so the spike validates the real M2 code path rather than a parallel one. They also already enforce `DocumentSizeLimit.EnsureWithinLimit`, which covers the oversized-`.sql`-file edge case for free. `RuleRegistry` is instantiated directly only because `AnalyserService` hides its internal registry, and the spike needs the discovered-rule count as trim-survival evidence (Decision 10).

**Alternatives considered**:
- *Call `FormatterPipeline` / `AnalysisEngine` directly* — rejected: duplicates the service wiring, re-implements the document-size guard, and proves nothing about the M2 path.
- *Reuse `Editor.razor` itself with a spike query string* — rejected: the editor pulls in theme, layout, the diagnostics ring buffer, and the editor component; any of those failing would confound the spike. FR-001 requires a surface independent of M2 features.

## Decision 2: A dedicated `Spike.razor` page at route `/spike`

**Decision**: Add a new Razor component `Pages/Spike.razor` with `@page "/spike"`. It is auto-discovered by the assembly-scanning `<Router>` in `App.razor` — no routing change.

**Rationale**: FR-001 and Story 1 acceptance scenario 4 require a surface runnable without depending on or disturbing M2. A focused, instrumented harness (per-operation timings, a verbatim-exception panel, the rule-discovery count, the corpus driver) is also far clearer than retrofitting the editor.

**Alternatives considered**:
- *A `/spike` mode on `Editor.razor`* — rejected: couples the spike to M2.
- *A separate throwaway Blazor project* — rejected: the spec scopes the work as additive within `AkmlSql.Web`, and the PRD keeps the spike page as a permanent record.

## Decision 3: Corpus as static `.sql` files under `wwwroot/spike-corpus/`

**Decision**: Six `.sql` files plus a `corpus.json` manifest under `src/AkmlSql.Web/wwwroot/spike-corpus/`; the spike fetches them with the already-registered `HttpClient`.

**Rationale**: The idiomatic Blazor WASM way to load text assets; the same "load text" path serves the corpus dropdown, the `<InputFile>` picker, and the paste box. The `.sql` files double as human-readable documentation and as inputs to the desktop golden-file generator. Total size is tens of KB — negligible to ship.

**Alternatives considered**:
- *Embedded resources* — rejected: less inspectable, no benefit over static files that ship anyway.
- *Inline C# string constants* — rejected: unwieldy for a 50-line stored procedure and not reusable as golden-comparison inputs.

## Decision 4: Engine-output comparison via desktop-generated golden files

**Decision**: A desktop generator — `SpikeCorpusGoldenTests` in the existing `AkmlSql.Web.Tests` project — runs `FormatterPipeline.Format` and `AnalysisEngine.AnalyzeAsync` (the *same libraries*) on desktop .NET and writes `{name}.expected.sql` + `{name}.expected.json` next to each corpus file. The spike (in WASM) fetches both and diffs against its own output.

**Rationale**: There is no separate "engine" formatter to capture from — the IDE engine and the web edition share the identical `netstandard2.0` `AkmlSql.Formatting` / `AkmlSql.Analysis` libraries. The only variable between the desktop golden output and the spike output is the **runtime** (desktop CoreCLR vs. browser WASM). A diff is therefore an unambiguous WASM-behaviour finding — exactly the FR-011 concern the advisor flagged. Golden files make the comparison reproducible (SC-010) instead of relying on eyeballing 50-line outputs.

**Alternatives considered**:
- *Capture reference output from the running IDE engine over IPC* — rejected: the engine runs the same library, so this adds an engine-process + IPC dependency for zero extra signal, and the spec forbids engine coupling.
- *Manual side-by-side comparison* — rejected: not reproducible, error-prone for long outputs.

## Decision 5: Execution timing via `Stopwatch` with warmup + N-loop averaging

**Decision**: Time parse / format / analyse with `System.Diagnostics.Stopwatch`. For each measured operation run one warmup pass, then average elapsed time over N iterations. At page load run a one-line microbench that prints `Stopwatch.Frequency` and the smallest observed non-zero delta; record both in the decision document.

**Rationale**: `Stopwatch` works in the WASM runtime but resolves to a browser high-resolution clock clamped to ~100 µs in Chromium. For tens-of-milliseconds operations (a 50-line proc parse+format is well above 1 ms) that is < 1 % quantisation error — acceptable. Warmup excludes interpreter/JIT tiering; averaging absorbs clamp jitter. Research flagged the exact `Stopwatch` backing under .NET 10 WASM as not nailed down by primary docs — so the microbench *confirms* the resolution empirically rather than assuming it.

**Alternatives considered**:
- *Single-shot timing* — rejected: unreliable near the clamp floor and against transient jitter.
- *Enable cross-origin isolation (COOP/COEP) for the 5 µs timer tier* — rejected: unnecessary for tens-of-ms work and would require serving-config changes outside spike scope.

## Decision 6: Cold-load measured from a Release publish, true-cold, in Chromium

**Decision**: `dotnet publish -c Release`; serve `publish/wwwroot`; in Chrome/Edge with DevTools, clear **site storage** (Cache Storage + IndexedDB, not just the HTTP cache) or use an incognito window, with no debugger attached; record time-to-first-interactive-render plus FCP/LCP from the Performance trace; take the **median of ≥ 3 runs**; record the machine and browser. Optionally emit a `performance.mark('akml-spike-ready')` from the page's first render for a precise in-app number.

**Rationale**: Research — Blazor caches boot resources in the browser **Cache Storage**, so a true cold load needs a storage clear, not just an HTTP-cache disable. The Debug build and an attached debugger both inflate startup materially. The number that matters to users is first-interactive, not first paint.

**Alternatives considered**:
- *Time a Debug `dotnet run`* — rejected: unrepresentative (no trimming or relinking, larger payload, dev server).
- *HTTP-cache-disable only* — rejected: misses Cache Storage, yielding a warm-ish reading.
- *Report a raw localhost number as the headline* — accepted only with the method recorded: localhost loopback is misleadingly fast; the decision document states the serving conditions so the figure is interpretable, and notes DevTools network throttling as the realistic-network variant.

## Decision 7: AOT-vs-interpreted via two Release publishes differing only in `RunAOTCompilation`

**Decision**: Install the `wasm-tools` workload. Publish A = `dotnet publish -c Release` (interpreted; runtime-relinked). Publish B = `dotnet publish -c Release -p:RunAOTCompilation=true`. Time the same parse+format operation in each; record both execution times, the AOT publish's build duration, and both compressed `_framework/` sizes. The committed `AkmlSql.Web.csproj` is **not** changed.

**Rationale**: Research — AOT is publish-only, multi-minute, and roughly doubles bundle size. Installing `wasm-tools` also enables runtime relinking on *every* Release publish; keeping the workload installed for both publishes holds relinking constant and isolates `RunAOTCompilation` as the single variable. Whether to adopt AOT is an M2 architecture decision the spike only informs — so AOT must not be committed.

**Alternatives considered**:
- *Compare a no-`wasm-tools` build against an AOT build* — rejected: conflates runtime relinking with AOT.
- *Commit `RunAOTCompilation` to the csproj* — rejected: an M2 decision, not a spike artifact, and it would roughly double every build's time and size.

## Decision 8: Compressed bundle size measured by summing `_framework/*.br` on disk

**Decision**: After `dotnet publish -c Release`, sum the byte sizes of `_framework/*.br` (Brotli, emitted by default at highest level). Also record the uncompressed `_framework/` total for continuity with `M1-SPIKE-RESULTS.md`'s 45 MB. When the publish is served by a Brotli-negotiating host, confirm in the browser Network tab that `_framework` responses carry `Content-Encoding: br`.

**Rationale**: Research — there is no build-emitted "bundle size" number, and plain static dev servers (including `dotnet-serve` by default) do not negotiate `.br`, so a Network-tab reading on a dumb host reflects the *uncompressed* download and is inflated. The `.br` files on disk are exactly what a production Brotli-capable host (IIS / Nginx / Azure Static Web Apps) transfers, so summing them is the faithful "compressed download" figure.

**Alternatives considered**:
- *Read the transferred size in the Network tab on `dotnet-serve`* — rejected: serves uncompressed, inflates the number.
- *Stand up IIS purely to measure* — viable but heavier; the disk-`.br`-sum is the cheap faithful proxy, and the decision document records the method either way.

## Decision 9: Trim-warning capture with detailed (un-collapsed) output

**Decision**: Capture `IL2xxx` warnings from the Release publish log; for full detail re-publish with `-p:TrimmerSingleWarn=false`. List every warning in the decision document; for each, either resolve it or annotate it as safe-to-ignore with evidence. Give special attention to warnings implicating `AkmlSql.Analysis` (the `RuleRegistry` reflection scan) and ScriptDom.

**Rationale**: Research — a Release publish trims (`partial`) by default and collapses trim warnings to at most one per assembly; `TrimmerSingleWarn=false` surfaces the per-call detail needed to judge each warning's safety. Reflection-dependent code can be trimmed even when `PublishTrimmed` is false.

**Alternatives considered**:
- *Trust the collapsed one-per-assembly summary* — rejected: too coarse to judge whether a specific reflective path is safe.
- *Disable trimming entirely to avoid warnings* — rejected: that hides the real production behaviour the spike exists to characterise.

## Decision 10: Reflection-survival evidence — report the discovered-rule count

**Decision**: The spike instantiates `RuleRegistry` and reports the count of discovered rules; the desktop baseline is **130**. If the WASM count is below 130, that is a recorded finding: trimming removed rule types — `RuleRegistry` discovers them via `Assembly.GetExecutingAssembly().GetTypes()` followed by `Activator.CreateInstance`, a classic trim-fragile pattern.

**Rationale**: The analyser's reflection-based rule discovery is the single highest-risk WASM-trim interaction (FR-010, the Story 2 concern). A bare discovered-count is the cleanest detectable signal, and a findings spot-check against the golden files confirms the rules that *did* load actually run. ScriptDom's own reflection is exercised implicitly by every parse.

**Alternatives considered**:
- *Assume the analyser works because it compiled* — rejected: that is exactly the compile-versus-runtime gap the spike exists to close.
- *Enumerate and assert every one of the 130 rule IDs* — rejected: the discovered count plus a golden-file findings comparison is sufficient spike evidence; a full per-rule audit belongs to M2.
