# Contract — M1 Decision Document (`docs/m1-wasm-decision.md`)

**Status**: New deliverable for P4 of spec 023. This is the formal output of the M1 decision gate (FR-016…FR-020). The file is committed and stays in the repository permanently, regardless of the go/no-go outcome.

## Location

`docs/m1-wasm-decision.md` — exact path mandated by the PRD. (The repo has both `doc/` and `docs/`; the PRD's choice is honoured.)

## Required structure

### Header
Title, date, author, link to `specs/023-m1-wasm-spike/spec.md`, and the resolved environment (machine, OS, browser + version, .NET SDK version, whether `wasm-tools` was installed).

### § 1 — Investigation matrix
A table with **all seven** PRD questions, each with an explicit `PASS` / `FAIL` verdict and the evidence behind it (FR-017, SC-006):

| # | Question | Verdict | Evidence |
|---|----------|---------|----------|
| 1 | Does `Microsoft.SqlServer.TransactSql.ScriptDom` load in `browser-wasm`? | | no `BadImageFormatException` / `TypeLoadException`; SELECT parses |
| 2 | Does the formatter pipeline run end-to-end? | | output matches the desktop golden file |
| 3 | What is the compressed WASM bundle size? | | `_framework/*.br` sum; vs the ≤ 25 MB reference |
| 4 | What is the cold-load time? | | median of ≥ 3 true-cold runs; vs the ≤ 8 s reference |
| 5 | Does AOT justify its build-time / size cost? | | interpreted vs AOT parse-time and bundle-size numbers |
| 6 | Do trim warnings exist? | | the `IL2xxx` list with dispositions |
| 7 | Are there missing-API runtime errors? | | none for SELECT; any for richer SQL listed |

### § 2 — Measurements
The actual measured values (FR-018, SC-005) — never estimates:
- Compressed `_framework/` size (and uncompressed, for continuity with the 45 MB in `M1-SPIKE-RESULTS.md`).
- Cold-load time (median + method: machine, browser, host, cache-clear).
- Interpreted vs AOT parse/format execution time; AOT publish build duration; AOT compressed size.
- The `Stopwatch` timer-probe result (`Frequency`, effective resolution).

### § 3 — Corpus results
One row per corpus item (SELECT, batch, ≥ 50-line stored proc, CTE, window function, MERGE): parse outcome, format outcome, analyse outcome, golden-match for format and analysis, and any recorded finding (FR-009, FR-011, SC-002, SC-003).

### § 4 — Analyser reflection survival
The discovered-rule count vs the desktop baseline of 130, and a `PASS`/`FAIL` on whether `RuleRegistry` reflection discovery survives WASM trimming (FR-010, SC-004).

### § 5 — Trim warnings
The full `IL2xxx` list captured with `TrimmerSingleWarn=false`; per warning a disposition (`Resolved` or `SafeToIgnore` + evidence) (FR-015).

### § 6 — Outcome
Exactly one classification (FR-019, SC-007):
- **Clean pass** — runs; compressed bundle and cold-load within the reference thresholds.
- **Works but heavy** — runs, but bundle or cold-load substantially exceeds the thresholds.
- **Does not work** — a load-time exception, unfixable trim breakage, or a hard missing-API / native-dependency failure.

### § 7 — Recommendation
A `Go` / `No-go` / `Qualified` recommendation for the in-browser M2 architecture (FR-019).

### § 8 — Consequences for M2 (only if outcome ≠ clean pass)
When the recommendation is no-go or qualified: a description of what the **already-in-progress** M2 in-browser work would need to change. This section MUST NOT direct an automatic rollback of the existing `AkmlSql.Web` scaffold or any M2 surface — the spike records risk; it does not revert work (FR-020).

### § 9 — Reproduction
A pointer to `specs/023-m1-wasm-spike/quickstart.md` so any maintainer can repeat the run and observe the same verdicts (SC-010).

## Invariants

1. All seven matrix rows are answered — no blanks (SC-006).
2. Every § 2 number is a measured actual (SC-005).
3. Exactly one § 6 outcome; § 7 recommendation always present (SC-007).
4. § 8 present whenever § 6 is not "clean pass"; never rolls anything back (FR-020).
5. The file is committed even on a no-go outcome (PRD definition of done).
