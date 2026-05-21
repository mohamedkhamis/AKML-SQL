# Phase 1 — Data Model: M1 ScriptDom-in-WASM Runtime Spike

The spike introduces no persistent data. Every entity below is either a small in-memory record local to `Spike.razor`, a static file on disk, or the structure of the decision document. The model exists so a future maintainer sees the shape of the evidence the spike produces.

Real types referenced from the existing libraries (confirmed by Phase 0 codebase exploration):

- `FormatResult` (`AkmlSql.Formatting`) — `FormattedText` (string), `Success` (bool), `WasModified` (bool), `ValidationPassed` (bool), `Diagnostics[]`, `ElapsedMs` (long).
- `CodeAnalysisResponse` (`AkmlSql.Engine.Analysis`) — carries the `AnalysisDiagnostic` findings list.
- `AnalysisDiagnostic` — `RuleId`, `CategoryCode`, `Severity`, `Message`, `StartOffset`, `EndOffset`, `Line`, `Column`, `FixActions[]`.
- `FormattingProfile` (`AkmlSql.Formatting.Profiles`) — `new FormattingProfile()` is the default.
- `RuleRegistry` (`AkmlSql.Engine.Analysis`) — reflection rule discovery; desktop baseline 130 rules.

---

## Entity 1: `SpikeCorpusItem`

**Type**: `record`, internal to `AkmlSql.Web` (used by `Spike.razor` and `SpikeCorpusGoldenTests`)

**Responsibility**: One entry in the T-SQL test corpus. Deserialised from `wwwroot/spike-corpus/corpus.json`.

| Field | Type | Notes |
|---|---|---|
| `Id` | `string` | Stable id, e.g. `01-select`; matches the `.sql` filename stem |
| `DisplayName` | `string` | Human label for the corpus dropdown |
| `Description` | `string` | What the item exercises |
| `Construct` | `string` (enum-like) | One of: `Select`, `Batch`, `StoredProc`, `Cte`, `Window`, `Merge` |
| `SqlPath` | `string` | Relative path under `wwwroot/`, e.g. `spike-corpus/01-select.sql` |
| `ExpectedFormattedPath` | `string` | Golden formatter output, e.g. `spike-corpus/01-select.expected.sql` |
| `ExpectedAnalysisPath` | `string` | Golden analyser output, e.g. `spike-corpus/01-select.expected.json` |

**Validation rules**:
- The corpus MUST contain at least the six constructs listed (FR-007).
- Exactly one item MUST have `Construct = StoredProc` and its `.sql` MUST be ≥ 50 lines (SC-002).
- Every `SqlPath` MUST resolve to a fetchable static asset.

---

## Entity 2: `SpikeRunResult`

**Type**: `record`, internal to `AkmlSql.Web`; transient (held in `Spike.razor` component state, never persisted)

**Responsibility**: The outcome of running parse + format + analyse on one input (a corpus item, a pasted string, or a loaded file).

| Field | Type | Notes |
|---|---|---|
| `InputId` | `string` | Corpus id, or `paste` / `file:<name>` |
| `ParseAndFormat` | `OperationOutcome` | See Entity 3 — covers the parse+format pipeline call |
| `Analyse` | `OperationOutcome` | See Entity 3 — covers the analyser call |
| `FormattedOutput` | `string?` | `FormatResult.FormattedText` when format succeeded |
| `Findings` | `IReadOnlyList<AnalysisDiagnostic>` | Analyser findings; empty when none |
| `RulesDiscovered` | `int` | Count from a directly-instantiated `RuleRegistry` (baseline 130) |
| `FormattedMatchesGolden` | `bool?` | `null` when no golden file (paste/file input); else exact-match result |
| `AnalysisMatchesGolden` | `bool?` | `null` when no golden file; else findings-set match result |

**Validation rules**:
- No field combination may represent "silently failed": either an `OperationOutcome` is `Success`, or it carries a non-empty `Error` (FR-009).
- `RulesDiscovered` MUST be populated on every analyse run (FR-010).
- `FormattedMatchesGolden` / `AnalysisMatchesGolden` MUST be set for every corpus item that has golden files (FR-011).

---

## Entity 3: `OperationOutcome`

**Type**: `record`, internal to `AkmlSql.Web`; transient

**Responsibility**: A single timed operation (one pipeline stage group) and its success/failure. The atom the spike's exception-handling and timing contract is built on.

| Field | Type | Notes |
|---|---|---|
| `Operation` | `string` | `ParseAndFormat` or `Analyse` |
| `Success` | `bool` | True only when the operation completed without throwing |
| `ElapsedMs` | `double` | Averaged over N iterations after one warmup pass (Decision 5) |
| `ErrorType` | `string?` | Exception type name when `Success` is false |
| `ErrorMessage` | `string?` | Exception message — rendered verbatim (FR-005) |
| `ErrorStackTrace` | `string?` | Full stack trace — rendered verbatim, this is the spike's core evidence |

**State transitions**:

```text
(start) ──run──▶ Success = true,  Elapsed set, Error* = null
         └─throw─▶ Success = false, Error* set (type + message + stack)
```

**Validation rules**:
- A thrown exception MUST set all three `Error*` fields; the page MUST render them and stay responsive (FR-005, Story 1 AS-3).
- `ElapsedMs` is meaningful only when `Success` is true.

---

## Entity 4: `WasmCostMeasurements`

**Type**: Recorded values (not a runtime object) — captured by hand / by the measurement procedure and transcribed into the decision document.

**Responsibility**: The quantified cost of running the app in the browser. Maps directly to the FR-012…FR-015 deliverables.

| Field | Type | Source |
|---|---|---|
| `CompressedFrameworkBytes` | long | Sum of `_framework/*.br` on disk after Release publish (Decision 8) |
| `UncompressedFrameworkBytes` | long | Sum of `_framework/*` raw — continuity with M1-SPIKE-RESULTS (≈ 45 MB) |
| `ColdLoadMs` | int | Median of ≥ 3 true-cold runs (Decision 6) |
| `ColdLoadMethod` | string | Machine, browser, serving host, cache-clear method |
| `InterpretedParseFormatMs` | double | Spike timing on the default Release publish |
| `AotParseFormatMs` | double | Spike timing on the `RunAOTCompilation=true` publish |
| `AotPublishBuildSeconds` | int | Wall-clock duration of the AOT publish |
| `AotCompressedFrameworkBytes` | long | `_framework/*.br` sum for the AOT publish |
| `TrimWarnings` | list of `{ Code, Assembly, Detail, Disposition }` | From the publish log with `TrimmerSingleWarn=false` (Decision 9) |
| `TimerProbe` | `{ Frequency, SmallestDeltaTicks, EffectiveResolutionUs }` | Startup `Stopwatch` microbench (Decision 5) |

**Validation rules**:
- Every numeric field MUST be an actual measured value, not an estimate or a range (SC-005, FR-018).
- Each `TrimWarnings` entry's `Disposition` MUST be `Resolved` or `SafeToIgnore` with stated evidence (FR-015).

---

## Entity 5: `M1DecisionDocument`

**Type**: The markdown file `docs/m1-wasm-decision.md`. Structure pinned by `contracts/decision-document.md`.

**Responsibility**: The durable, citable record of the M1 gate.

| Section | Content |
|---|---|
| `InvestigationMatrix` | Seven rows — ScriptDom load, formatter pipeline run, bundle size, cold-load, AOT justification, trim warnings, missing-API runtime errors — each with `Verdict` (PASS/FAIL) + `Evidence` (FR-017) |
| `Measurements` | The `WasmCostMeasurements` values (Entity 4), transcribed (FR-018) |
| `CorpusResults` | One row per `SpikeCorpusItem` — parse / format / analyse outcome + golden-match (FR-009, FR-011) |
| `RuleDiscovery` | `Discovered` (int), `Expected` = 130, `Verdict` — trim-survival of `RuleRegistry` (FR-010, SC-004) |
| `Outcome` | Exactly one of `CleanPass`, `WorksButHeavy`, `DoesNotWork` (FR-019) |
| `Recommendation` | `Go`, `NoGo`, or `Qualified` for the in-browser M2 architecture (FR-019) |
| `M2Consequences` | Present only when `Outcome` ≠ `CleanPass` — what in-progress M2 work would need to change; never an automatic rollback (FR-020) |
| `Reproduction` | Link to `quickstart.md` so the run can be repeated (SC-010) |

**Validation rules**:
- All seven `InvestigationMatrix` rows MUST be answered (FR-017, SC-006).
- `Outcome` MUST be exactly one value; `Recommendation` MUST be present (FR-019, SC-007).
- When `Outcome` is `DoesNotWork` or `Recommendation` is `NoGo`/`Qualified`, `M2Consequences` MUST be present and MUST NOT direct a rollback of existing scaffold or M2 surfaces (FR-020).

---

## Cross-entity invariants

1. **No silent failure** — every `OperationOutcome` is either `Success` or carries a full `Error*` triple; every corpus item ends in a `SpikeRunResult` with a recorded outcome (FR-009, SC-003).
2. **Golden comparison is runtime-isolated** — `*.expected.sql` / `*.expected.json` are produced on desktop .NET by the *same* `AkmlSql.Formatting` / `AkmlSql.Analysis` libraries the spike runs in WASM; therefore any `FormattedMatchesGolden == false` or `AnalysisMatchesGolden == false` is a pure WASM-runtime finding (Decision 4, FR-011).
3. **Measurements are actuals** — every field of `WasmCostMeasurements` is a measured number; the decision document never substitutes an estimate (SC-005).
4. **The decision document is the single source of the verdict** — `Outcome` and `Recommendation` exist in exactly one place, `docs/m1-wasm-decision.md` (FR-016).
5. **Additive-only** — none of these entities requires a change to an existing `AkmlSql.Web` source file; all new types live in `Spike.razor` (or a small `Spike` code-behind / shared file) and the two new test files.
