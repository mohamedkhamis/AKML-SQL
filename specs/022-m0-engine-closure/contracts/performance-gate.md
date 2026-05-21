# Contract — Performance-Regression Gate

**Status**: Modified surface for P4 of spec 022 (M0 closure). Existing `PerformanceBaselineTests.Capture_or_compare_M0_baseline` is updated; threshold tightened from 25 % to 5 %; corpus scaled up; a third workload (`BulkFormatRequest`) is added.

## Why the gate has teeth now

The original M0 work captured the gate at `MaxRegressionFraction = 0.25` because the measured operations (`CompletionEngine.GetCompletions` and `FormatRequestHandler.HandleFormat` on a ~750-byte corpus) run in under 2 ms per call. At sub-2 ms latencies, JIT, L1-cache, and OS-scheduling jitter dominate the per-call cost — empirical variance across runs on a quiet desktop is 30–45 % even without code changes. A 5 % threshold would flake randomly.

The PRD's 5 % target is the contract this closure restores. To make it stable, the closure (a) replaces the sub-2-ms workloads with workloads whose p50 sits at ≥ 20 ms, and (b) adds a third measurement (`BulkFormat`) that exercises the full 7-stage formatter pipeline.

## Workload contract

### Corpus

```csharp
private static readonly string CorpusSql = BuildCorpus(repeats: 10);

private static string BuildCorpus(int repeats)
{
    var sb = new StringBuilder();
    for (int i = 0; i < repeats; i++)
    {
        sb.AppendFormat(CultureInfo.InvariantCulture, "-- block {0}\n", i);
        // The 4 representative statements from the original corpus,
        // with every identifier suffixed by "_b{0}" so the parser cannot
        // cache an AST across blocks.
    }
    return sb.ToString();
}
```

The post-closure corpus MUST contain ≥ 300 SQL statements and produce ≥ 30 KB of text. Identifier suffixing is mandatory — without it the parser's identifier cache flattens cost across blocks and the workload reverts to the sub-2-ms regime.

### Measurement points

Three workloads MUST be measured and persisted to the baseline:

| Workload | Existing? | Target p50 | Notes |
|---|---|---|---|
| `CompletionRequest` | Yes (modified) | ≥ 20 ms over 6 cursor positions × scaled corpus | One measurement = sum of completion calls over 6 cursor offsets ÷ 6 (preserves the existing per-cursor average shape; the corpus scale-up does the work) |
| `FormatRequest` | Yes (modified) | ≥ 20 ms per call on scaled corpus | One measurement = single format pass over the full scaled corpus |
| `BulkFormatRequest` | **New** | ≥ 30 ms per pipeline run | Drives the 7-stage formatter pipeline against every statement-boundary chunk in the corpus |

### Trials / iterations

```csharp
private const int WarmupIterations = 10;
private const int MeasureIterations = 50;
private const int Trials = 5;
```

Reading: `min(p50 across Trials independent measurement loops)`. The "min across trials" pattern (rather than mean) absorbs transient OS scheduling noise without inflating variance.

If a clean run flakes at the 5 % boundary, raise `MeasureIterations` to 200 — do NOT raise the threshold.

### Threshold

```csharp
private const double MaxRegressionFraction = 0.05;   // 5 %
```

Comment in the source MUST be updated to reflect the rationale (heavier workloads + min-of-trials reading). The previous 25 %-justification comment in `PerformanceBaselineTests.cs:39-48` is removed.

## Behavioural contract

### Capture mode

When `tests/AkmlSql.Engine.Tests/baselines/m0-baseline.json` does not exist OR the env var `AKML_UPDATE_BASELINE=1`:
- Measure all three workloads
- Write the baseline file
- Test passes if all three captured p50 values are non-zero AND ≥ 20 ms (≥ 30 ms for BulkFormat)
- Capture-mode failure with p50 < 20 ms is a **harness breakage**, not a code-under-test issue — the test message MUST clearly distinguish

### Compare mode

When the baseline file exists AND env var is unset:
- Measure all three workloads
- Compare each p50 against the persisted baseline
- Fail if any workload's p50 exceeds baseline × 1.05
- Failure message MUST name the regressed workload and report both numbers in milliseconds with 3 decimal places, plus the regression percentage

### Three-runs-clean contract

The same machine running the same code three times in succession MUST pass all three runs at the 5 % threshold. Local validation:

```bash
for i in 1 2 3; do
    dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj \
        --filter "Capture_or_compare_M0_baseline" -c Release
done
```

If any of three back-to-back runs flakes, the closure plan dictates raising `MeasureIterations`, not relaxing the threshold. This is FR-016.

## Synthetic-regression contract

To validate that the gate has teeth, the closure plan's verification step injects an artificial 10 % slowdown into the completion dispatch path (e.g. a `Thread.SpinWait(10000)` inside the dispatch loop). The test MUST fail on the next run with a message naming `CompletionRequest.p50` as the regressed metric.

## Per-machine reference, per-CI sync

The baseline file is per-developer-machine — it is intentionally `.gitignore`d (the `tests/AkmlSql.Engine.Tests/baselines/` convention from spec 021 T006 is preserved). CI agents that run the test capture a fresh baseline with `AKML_UPDATE_BASELINE=1` and discard the artefact at job end. The gate is meaningful locally during code review, not on shared CI.

## Invariants

1. The baseline JSON schema MUST be a strict superset of the pre-closure schema: `CompletionRequest` and `FormatRequest` blocks remain; `BulkFormatRequest` is added. Reading an old baseline that lacks the new block does NOT fail — the compare path treats a missing block as "no baseline; skip assertion for that workload".
2. The corpus generator MUST be deterministic — running it twice in the same process produces byte-identical strings. This guarantees a baseline captured today compares to a measurement taken tomorrow against the same workload.
3. The captured p50 for every workload after corpus scaling MUST be ≥ 20 ms on a typical developer workstation (e.g. recent Intel/AMD desktop, .NET 10 release build). If a workload's p50 drops below 20 ms after a future engine optimisation, the closure plan's follow-up is to scale the corpus further — not to relax the workload's role.
4. The gate MUST NOT be relaxed for noisy CI agents. Such agents use `AKML_UPDATE_BASELINE=1` per run, which makes the gate a capture (skipping the assertion). The gate's compare-mode value lives in pre-merge local verification.
