# Contract — Spike Page (`Spike.razor`)

**Status**: New surface for P1/P2 of spec 023. Additive — auto-routed by the existing `<Router>`; reuses already-registered services.

## Route & placement

- **Route**: `@page "/spike"`.
- **Project**: `src/AkmlSql.Web/Pages/Spike.razor`.
- **Layout**: may use the existing `MainLayout`. The page body MUST NOT depend on any M2 feature — no editor component, no diagnostics ring buffer, no theme switching, no schema/bridge services, no engine call, no network call beyond fetching `wwwroot/spike-corpus/` static assets (FR-001).
- The page is **not** linked from `NavMenu` — it is a diagnostic surface reached by URL.

## Dependencies (constructor / `@inject`)

| Injected | Source | Use |
|---|---|---|
| `IFormatterService` | already registered in `Program.cs` | parse + format (the real M2 path) |
| `IAnalyserService` | already registered in `Program.cs` | run the analysis rule set |
| `HttpClient` | already registered in `Program.cs` | fetch corpus + golden files from `wwwroot/spike-corpus/` |

`RuleRegistry` is constructed directly in page code (not injected) solely to read the discovered-rule count.

## Inputs

1. **Paste / type** — a `<textarea>` accepting arbitrary T-SQL.
2. **File load** — Blazor's `<InputFile>`, restricted to `.sql`; loaded text replaces the textarea content. Oversized files surface the existing `DocumentSizeLimit` error cleanly (no tab freeze).
3. **Corpus dropdown** — a `<select>` populated from `corpus.json`; selecting an item fetches its `.sql` into the textarea.

## Actions

| Control | Behaviour |
|---|---|
| **Parse & Format** | Runs the formatter pipeline on the current input; warmup pass + N-iteration timed average; renders `FormatResult.FormattedText` or the verbatim exception. |
| **Analyse** | Runs `IAnalyserService.AnalyseAsync` on the current input; renders the `AnalysisDiagnostic` list or the verbatim exception; shows the discovered-rule count. |
| **Run all corpus** | Iterates every `SpikeCorpusItem`, runs Parse & Format and Analyse, diffs each against its golden files, renders a per-item result table. |
| **Timer probe** (runs on first render) | One-shot `Stopwatch` microbench; displays `Stopwatch.Frequency` and the smallest observed non-zero delta. |

## Outputs

- **Formatted output** — a `<pre>` block with the formatted SQL.
- **Findings list** — one row per `AnalysisDiagnostic`: `RuleId`, `Severity`, `Message`, `Line`:`Column`.
- **Exception panel** — when any operation throws: the exception **type**, **message**, and **full stack trace**, rendered verbatim (FR-005). This is the spike's primary evidence on failure.
- **Timings** — parse+format ms and analyse ms (averaged), plus the timer-probe resolution.
- **Rule-discovery readout** — `discovered / 130` from the directly-constructed `RuleRegistry` (FR-010).
- **Golden-match indicators** — for corpus items: `formatted == golden?` and `findings == golden?` (FR-011).

## Behavioural contract

1. **No silent failure** — every action ends in either rendered output/findings or a rendered exception panel; an action MUST NOT leave the page blank or unresponsive (FR-009).
2. **No unhandled crash** — all parse/format/analyse calls are wrapped so any exception (including `BadImageFormatException`, `TypeLoadException`, `PlatformNotSupportedException`) is caught and displayed (FR-004, FR-005).
3. **Engine-free** — the page issues no engine/IPC/WebSocket call; it works with the network disconnected once static assets are loaded (FR-001).
4. **Additive** — adding this page changes no existing file; the M2 editor at `/` is unaffected (FR-006, Story 1 AS-4).
5. **Full pipeline** — Parse & Format invokes the complete formatter pipeline (through semantic validation), not the parse step alone, so a stage-specific WASM failure surfaces (Edge Case: full pipeline vs parse alone).

## Out of scope for the page

No theming work, no editor component, no syntax highlighting, no persistence, no settings. The page is a harness, not a feature.
