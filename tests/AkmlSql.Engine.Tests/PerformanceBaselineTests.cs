using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Formatter;
using AkmlSql.Engine.Parser;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Engine.Tests;

/// <summary>
/// Spec 021 (web edition) M0 task T006, tightened by spec 022 US4 (M0 closure). Captures the
/// baseline latency of three engine dispatch workloads and enforces a 5 % regression gate:
/// <list type="bullet">
///   <item><c>CompletionRequest</c> — <see cref="CompletionEngine.GetCompletions"/> averaged over 6 cursor offsets.</item>
///   <item><c>FormatRequest</c> — one <see cref="FormatRequestHandler.HandleFormat"/> pass over the whole corpus.</item>
///   <item><c>BulkFormatRequest</c> — the 7-stage formatter pipeline run once per statement-boundary chunk.</item>
/// </list>
///
/// Spec 022 US4 raised the gate from 25 % to 5 %. A 5 % threshold is only meaningful when the
/// measured p50 sits well above per-run jitter, so the corpus was scaled up (see
/// <see cref="CorpusRepeats"/>) until every workload's p50 lands at ≥ 20 ms (≥ 30 ms for
/// BulkFormat). The reading is the <em>minimum</em> p50 across <see cref="Trials"/> independent
/// loops — that absorbs transient OS scheduling noise without inflating variance. See the contract
/// at <c>specs/022-m0-engine-closure/contracts/performance-gate.md</c>.
///
/// Mode:
/// * If <c>tests/AkmlSql.Engine.Tests/baselines/m0-baseline.json</c> does NOT exist (or the env
///   var <c>AKML_UPDATE_BASELINE=1</c> is set), the test captures fresh numbers, asserts every
///   workload clears its p50 floor, and writes the file.
/// * If the baseline file exists, the test re-measures and fails if any workload's p50 exceeds the
///   stored value by more than 5 %.
///
/// Noise note: perf numbers vary across machines. The baseline file is a per-developer reference,
/// not a CI-wide guarantee — it is git-ignored. CI runs use <c>AKML_UPDATE_BASELINE=1</c> and
/// discard the artefact; the gate's value is in pre-merge local verification.
/// </summary>
public sealed class PerformanceBaselineTests
{
    private const int WarmupIterations = 10;

    // Spec 022 US4 — bumped 50 → 200 after the perf gate's 3-run verification produced a 5.5 %
    // boundary flake on this hardware. The contract's prescribed remedy (FR-016 /
    // contracts/performance-gate.md): more samples per trial tighten each per-trial p50 so a
    // 5 % comparison reflects real signal, not within-run jitter — the threshold is never relaxed.
    private const int MeasureIterations = 200;

    private const int Trials = 5;

    /// <summary>
    /// Number of statement blocks in the perf corpus. Each block emits 4 representative statements
    /// with block-unique identifiers, so the corpus holds <c>CorpusRepeats * 4</c> statements.
    /// Sized so every measured workload's p50 lands ≥ 20 ms (≥ 30 ms for BulkFormat) — the regime
    /// where a 5 % gate represents real signal rather than microbenchmark jitter. The contract
    /// (<c>contracts/performance-gate.md</c>) requires ≥ 300 statements and ≥ 30 KB of text;
    /// 80 blocks → 320 statements ≈ 90 KB. If a future engine optimisation drops a workload below
    /// its floor, raise this value — do not relax the threshold (FR-016, contract invariant 4).
    /// </summary>
    private const int CorpusRepeats = 80;

    // Spec 022 US4: the gate is 5 %. This is meaningful because the corpus above is scaled so the
    // measured p50 of every workload is ≥ 20 ms — 5 % of that is ≥ 1 ms, comfortably above the
    // per-run jitter that the min-of-trials reading already suppresses. (The pre-closure gate sat
    // at 25 % only because the workloads were sub-2 ms, where OS scheduling jitter dominated the
    // per-call cost and a 5 % threshold would have flaked randomly.)
    private const double MaxRegressionFraction = 0.05;   // 5 %

    /// <summary>Capture-mode p50 floor for Completion/Format — below this a 5 % gate is just noise.</summary>
    private const double WorkloadFloorMs = 20.0;

    /// <summary>BulkFormat runs the pipeline once per statement, so its capture-mode floor is higher.</summary>
    private const double BulkFormatFloorMs = 30.0;

    private static readonly string BaselineDir = Path.Combine(
        TestPaths.RepoRoot, "tests", "AkmlSql.Engine.Tests", "baselines");

    private static readonly string BaselinePath = Path.Combine(BaselineDir, "m0-baseline.json");

    // ----------------------------------------------------------------------
    // Corpus — scaled up for spec 022 US4. BuildBlock is the single source of
    // truth; both the concatenated text and the per-statement array derive from it.
    // ----------------------------------------------------------------------

    /// <summary>Every statement in the corpus, one complete T-SQL statement per element.</summary>
    private static readonly string[] CorpusStatements = BuildAllStatements(CorpusRepeats);

    /// <summary>The whole corpus as one string — block comments interleaved with statements.</summary>
    private static readonly string CorpusSql = BuildCorpusText(CorpusRepeats);

    /// <summary>
    /// The four representative statements for block <paramref name="block"/>: a multi-join SELECT,
    /// an INSERT, an UPDATE with a subquery, and a CTE. Every table and column identifier is
    /// suffixed <c>_b{block}</c> so no two blocks share an identifier — this defeats any
    /// identifier-level caching in the parser and keeps the workload in the heavy regime
    /// (contract: "Identifier suffixing is mandatory"). Single-character aliases (<c>c</c>,
    /// <c>o</c>, <c>t</c>) are block-local and left unsuffixed.
    /// </summary>
    private static string[] BuildBlock(int block)
    {
        var s = "_b" + block.ToString(CultureInfo.InvariantCulture);
        return new[]
        {
            // 1 — multi-join SELECT
            $"SELECT TOP 100 c.CustomerId{s}, c.FirstName{s}, c.LastName{s}, c.Email{s},\n" +
            $"       o.OrderId{s}, o.OrderDate{s}, o.TotalAmount{s}\n" +
            $"FROM dbo.Customers{s} c\n" +
            $"INNER JOIN dbo.Orders{s} o ON o.CustomerId{s} = c.CustomerId{s}\n" +
            $"WHERE c.CountryCode{s} IN ('US', 'CA', 'GB')\n" +
            $"  AND o.OrderDate{s} >= DATEADD(DAY, -30, GETUTCDATE())\n" +
            $"  AND o.TotalAmount{s} > 100.00\n" +
            $"ORDER BY o.OrderDate{s} DESC;",

            // 2 — INSERT
            $"INSERT INTO dbo.AuditLog{s} (Action{s}, ActorId{s}, Payload{s})\n" +
            $"VALUES (N'OrderQuery', SUSER_SNAME(), N'corpus');",

            // 3 — UPDATE with subquery
            $"UPDATE dbo.Customers{s}\n" +
            $"   SET LastSeenAt{s} = SYSUTCDATETIME()\n" +
            $" WHERE CustomerId{s} IN (SELECT CustomerId{s} FROM dbo.RecentLogins{s});",

            // 4 — common table expression
            $"WITH OrderTotals{s} AS (\n" +
            $"    SELECT CustomerId{s}, SUM(TotalAmount{s}) AS YearTotal{s}\n" +
            $"      FROM dbo.Orders{s}\n" +
            $"     WHERE OrderDate{s} >= DATEFROMPARTS(YEAR(GETUTCDATE()), 1, 1)\n" +
            $"     GROUP BY CustomerId{s}\n" +
            $")\n" +
            $"SELECT c.CustomerId{s}, c.FullName{s}, t.YearTotal{s}\n" +
            $"  FROM OrderTotals{s} t\n" +
            $" INNER JOIN dbo.Customers{s} c ON c.CustomerId{s} = t.CustomerId{s}\n" +
            $" WHERE t.YearTotal{s} > 1000\n" +
            $" ORDER BY t.YearTotal{s} DESC;",
        };
    }

    private static string[] BuildAllStatements(int repeats)
    {
        var all = new List<string>(repeats * 4);
        for (int i = 0; i < repeats; i++)
            all.AddRange(BuildBlock(i));
        return all.ToArray();
    }

    private static string BuildCorpusText(int repeats)
    {
        var sb = new StringBuilder();
        sb.Append("-- Spec 022 perf-baseline corpus — ")
          .Append(repeats.ToString(CultureInfo.InvariantCulture))
          .Append(" blocks\n");
        for (int i = 0; i < repeats; i++)
        {
            sb.Append("-- block ").Append(i.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (var stmt in BuildBlock(i))
                sb.Append(stmt).Append("\n\n");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Six cursor positions in the first block — SELECT list, JOIN target, WHERE clause, ORDER BY,
    /// INSERT VALUES, and CTE join. The per-call cost is dominated by parsing the whole (scaled)
    /// corpus, so the cursor's block does not affect the measurement; block 0 keeps the offsets
    /// deterministic. The landmark substrings carry the <c>_b0</c> suffix where the surrounding
    /// identifier was suffixed by <see cref="BuildBlock"/>.
    /// </summary>
    private static int[] CompletionOffsets()
    {
        return new[]
        {
            CorpusSql.IndexOf("c.CustomerId_b0", StringComparison.Ordinal) + 2,                  // after "c."
            CorpusSql.IndexOf("INNER JOIN dbo.", StringComparison.Ordinal) + "INNER JOIN dbo.".Length,
            CorpusSql.IndexOf("WHERE c.", StringComparison.Ordinal) + "WHERE c.".Length,
            CorpusSql.IndexOf("ORDER BY o.", StringComparison.Ordinal) + "ORDER BY o.".Length,
            CorpusSql.IndexOf("VALUES (N'OrderQuery'", StringComparison.Ordinal) + 1,
            CorpusSql.IndexOf("INNER JOIN dbo.Customers_b0 c ON c.", StringComparison.Ordinal)
                + "INNER JOIN dbo.Customers_b0 c ON c.".Length,
        };
    }

    // ----------------------------------------------------------------------
    // Measurement helpers
    // ----------------------------------------------------------------------

    private static (double p50, double p99) Percentiles(IEnumerable<double> samples)
    {
        var sorted = samples.OrderBy(x => x).ToArray();
        return (Percentile(sorted, 0.50), Percentile(sorted, 0.99));
    }

    private static double Percentile(double[] sortedAsc, double q)
    {
        if (sortedAsc.Length == 0) return 0.0;
        var index = (sortedAsc.Length - 1) * q;
        var lo = (int)Math.Floor(index);
        var hi = (int)Math.Ceiling(index);
        if (lo == hi) return sortedAsc[lo];
        var w = index - lo;
        return sortedAsc[lo] * (1 - w) + sortedAsc[hi] * w;
    }

    private static (double p50Ms, double p99Ms) MeasureCompletion()
    {
        var parser = new TsqlParserService();
        var engine = new CompletionEngine(parser);
        var offsets = CompletionOffsets();

        double bestP50 = double.MaxValue;
        double bestP99 = double.MaxValue;

        for (int trial = 0; trial < Trials; trial++)
        {
            // Warm-up (fresh each trial — JITed methods stay hot, but cache pressure resets).
            for (int i = 0; i < WarmupIterations; i++)
            {
                foreach (var off in offsets) _ = engine.GetCompletions(CorpusSql, off, null);
            }

            var samples = new List<double>(MeasureIterations);
            var sw = new Stopwatch();
            for (int i = 0; i < MeasureIterations; i++)
            {
                sw.Restart();
                foreach (var off in offsets) _ = engine.GetCompletions(CorpusSql, off, null);
                sw.Stop();
                samples.Add(sw.Elapsed.TotalMilliseconds / offsets.Length);
            }

            var (p50, p99) = Percentiles(samples);
            if (p50 < bestP50) bestP50 = p50;
            if (p99 < bestP99) bestP99 = p99;
        }

        return (bestP50, bestP99);
    }

    private static (double p50Ms, double p99Ms) MeasureFormat()
    {
        var builtInDir = Path.Combine(Path.GetTempPath(), $"akml_pb_builtin_{Guid.NewGuid():N}");
        var customDir = Path.Combine(Path.GetTempPath(), $"akml_pb_custom_{Guid.NewGuid():N}");
        Directory.CreateDirectory(builtInDir);
        Directory.CreateDirectory(customDir);
        try
        {
            var profiles = new ProfileManager(builtInDir, customDir);
            var handler = new FormatRequestHandler(profiles);
            var request = new FormatRequest { Text = CorpusSql, ProfileName = null };

            double bestP50 = double.MaxValue;
            double bestP99 = double.MaxValue;

            for (int trial = 0; trial < Trials; trial++)
            {
                for (int i = 0; i < WarmupIterations; i++) _ = handler.HandleFormat(request);

                var samples = new List<double>(MeasureIterations);
                var sw = new Stopwatch();
                for (int i = 0; i < MeasureIterations; i++)
                {
                    sw.Restart();
                    _ = handler.HandleFormat(request);
                    sw.Stop();
                    samples.Add(sw.Elapsed.TotalMilliseconds);
                }

                var (p50, p99) = Percentiles(samples);
                if (p50 < bestP50) bestP50 = p50;
                if (p99 < bestP99) bestP99 = p99;
            }

            return (bestP50, bestP99);
        }
        finally
        {
            try { Directory.Delete(builtInDir, recursive: true); } catch { }
            try { Directory.Delete(customDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Spec 022 US4 — the third workload. Drives the full 7-stage formatter pipeline once per
    /// statement-boundary chunk of the corpus (N small pipeline runs), in contrast to
    /// <see cref="MeasureFormat"/> which is a single pipeline run over the whole corpus. This
    /// stresses per-call pipeline setup cost that the single-pass measurement does not catch.
    /// One sample = the time to format every statement in the corpus once.
    ///
    /// It deliberately does NOT route through <c>BulkFormatHandler</c>: that handler reads files
    /// from disk and fans out across <c>Parallel.ForEachAsync</c>, reintroducing file-I/O and
    /// thread-scheduling noise — exactly what a 5 % gate must exclude (FR-016). The contract
    /// (<c>performance-gate.md</c>) specifies this workload behaviourally ("7-stage pipeline
    /// against every statement-boundary chunk"), which this in-memory loop satisfies
    /// deterministically.
    /// </summary>
    private static (double p50Ms, double p99Ms) MeasureBulkFormat()
    {
        var builtInDir = Path.Combine(Path.GetTempPath(), $"akml_pb_bulk_builtin_{Guid.NewGuid():N}");
        var customDir = Path.Combine(Path.GetTempPath(), $"akml_pb_bulk_custom_{Guid.NewGuid():N}");
        Directory.CreateDirectory(builtInDir);
        Directory.CreateDirectory(customDir);
        try
        {
            var profiles = new ProfileManager(builtInDir, customDir);
            var handler = new FormatRequestHandler(profiles);

            // Build the per-statement requests once — re-splitting per iteration would add a
            // string operation to every sample and couple the measurement to a parsing rule.
            var requests = CorpusStatements
                .Select(stmt => new FormatRequest { Text = stmt, ProfileName = null })
                .ToArray();

            double bestP50 = double.MaxValue;
            double bestP99 = double.MaxValue;

            for (int trial = 0; trial < Trials; trial++)
            {
                for (int i = 0; i < WarmupIterations; i++)
                    foreach (var request in requests) _ = handler.HandleFormat(request);

                var samples = new List<double>(MeasureIterations);
                var sw = new Stopwatch();
                for (int i = 0; i < MeasureIterations; i++)
                {
                    sw.Restart();
                    foreach (var request in requests) _ = handler.HandleFormat(request);
                    sw.Stop();
                    samples.Add(sw.Elapsed.TotalMilliseconds);
                }

                var (p50, p99) = Percentiles(samples);
                if (p50 < bestP50) bestP50 = p50;
                if (p99 < bestP99) bestP99 = p99;
            }

            return (bestP50, bestP99);
        }
        finally
        {
            try { Directory.Delete(builtInDir, recursive: true); } catch { }
            try { Directory.Delete(customDir, recursive: true); } catch { }
        }
    }

    // ----------------------------------------------------------------------
    // Test entrypoints
    // ----------------------------------------------------------------------

    /// <summary>
    /// Spec 022 US4 — guards the corpus contract (<c>performance-gate.md</c>): the generator is
    /// deterministic and the corpus is large enough (≥ 300 statements, ≥ 30 KB) that the measured
    /// workloads stay in the heavy regime where a 5 % gate is meaningful.
    /// </summary>
    [Fact]
    public void Corpus_meets_contract_size_and_determinism()
    {
        Assert.True(CorpusStatements.Length >= 300,
            $"Perf corpus has {CorpusStatements.Length} statements; contract requires ≥ 300. "
            + "Raise CorpusRepeats.");
        Assert.True(CorpusSql.Length >= 30 * 1024,
            $"Perf corpus is {CorpusSql.Length} chars; contract requires ≥ 30 KB. Raise CorpusRepeats.");

        // Deterministic: regenerating in the same process is byte-identical (contract invariant 2).
        Assert.Equal(CorpusSql, BuildCorpusText(CorpusRepeats));

        // Every completion offset must land inside the corpus — catches a stale landmark string.
        foreach (var off in CompletionOffsets())
        {
            Assert.True(off > 0 && off < CorpusSql.Length,
                $"Completion offset {off} is outside the corpus (length {CorpusSql.Length}) — "
                + "a landmark substring in CompletionOffsets() no longer matches the corpus.");
        }
    }

    /// <summary>
    /// Spec 022 US4, contract invariant 1 — a pre-closure baseline JSON has no
    /// <c>bulkFormatRequest</c> block. Reading it MUST NOT fail: the missing block deserialises to
    /// a zero-valued sample, which the compare path (<see cref="AssertNoRegression"/>, early-return
    /// on <c>baseline &lt;= 0</c>) then skips. Guards against a future change that makes the new
    /// block required and silently breaks older baselines.
    /// </summary>
    [Fact]
    public void Pre_closure_baseline_without_BulkFormat_block_deserialises_gracefully()
    {
        // The exact shape written by the spec-021 version of this test — no bulkFormatRequest key.
        const string preClosureJson = """
            {
              "captureDate": "2026-05-16T00:46:47.9275073Z",
              "machine": "OLD-MACHINE",
              "dotNetVersion": "10.0.8",
              "completionRequest": { "p50Ms": 0.367, "p99Ms": 0.760, "measureIterations": 50, "warmupIterations": 10, "corpusOffsetsPerIteration": 6 },
              "formatRequest": { "p50Ms": 0.844, "p99Ms": 1.400, "measureIterations": 50, "warmupIterations": 10, "corpusOffsetsPerIteration": 1 }
            }
            """;

        var doc = JsonSerializer.Deserialize<BaselineDocument>(preClosureJson, JsonOptions)!;

        // Present blocks read normally.
        Assert.True(doc.CompletionRequest.P50Ms > 0.0);
        Assert.True(doc.FormatRequest.P50Ms > 0.0);

        // Missing block ⇒ default sample with p50 = 0 ⇒ AssertNoRegression skips it (invariant 1).
        Assert.NotNull(doc.BulkFormatRequest);
        Assert.Equal(0.0, doc.BulkFormatRequest.P50Ms);
    }

    [Fact]
    public void Capture_or_compare_M0_baseline()
    {
        var (compP50, compP99) = MeasureCompletion();
        var (fmtP50, fmtP99) = MeasureFormat();
        var (bulkP50, bulkP99) = MeasureBulkFormat();

        var current = new BaselineDocument
        {
            CaptureDate = DateTime.UtcNow,
            Machine = Environment.MachineName,
            DotNetVersion = Environment.Version.ToString(),
            CompletionRequest = new BaselineSample
            {
                P50Ms = compP50,
                P99Ms = compP99,
                MeasureIterations = MeasureIterations,
                WarmupIterations = WarmupIterations,
                CorpusOffsetsPerIteration = CompletionOffsets().Length,
            },
            FormatRequest = new BaselineSample
            {
                P50Ms = fmtP50,
                P99Ms = fmtP99,
                MeasureIterations = MeasureIterations,
                WarmupIterations = WarmupIterations,
                CorpusOffsetsPerIteration = 1,
            },
            BulkFormatRequest = new BaselineSample
            {
                P50Ms = bulkP50,
                P99Ms = bulkP99,
                MeasureIterations = MeasureIterations,
                WarmupIterations = WarmupIterations,
                CorpusOffsetsPerIteration = CorpusStatements.Length,
            },
        };

        // Catch a broken harness in either mode (zero p50 ⇒ nothing was measured).
        Assert.True(compP50 > 0.0, $"Completion p50 unexpectedly zero (p50={compP50}, p99={compP99}).");
        Assert.True(fmtP50 > 0.0, $"Format p50 unexpectedly zero (p50={fmtP50}, p99={fmtP99}).");
        Assert.True(bulkP50 > 0.0, $"BulkFormat p50 unexpectedly zero (p50={bulkP50}, p99={bulkP99}).");

        var shouldUpdate = string.Equals(
            Environment.GetEnvironmentVariable("AKML_UPDATE_BASELINE"), "1", StringComparison.Ordinal);

        if (!File.Exists(BaselinePath) || shouldUpdate)
        {
            // Capture mode — the captured numbers become the gate, so every workload must be
            // heavy enough that a 5 % comparison is real signal rather than jitter.
            AssertWorkloadFloor("CompletionRequest", compP50, WorkloadFloorMs);
            AssertWorkloadFloor("FormatRequest", fmtP50, WorkloadFloorMs);
            AssertWorkloadFloor("BulkFormatRequest", bulkP50, BulkFormatFloorMs);

            Directory.CreateDirectory(BaselineDir);
            File.WriteAllText(BaselinePath, JsonSerializer.Serialize(current, JsonOptions));
            return;
        }

        // Compare mode — fail if any workload regressed by more than MaxRegressionFraction.
        var existingJson = File.ReadAllText(BaselinePath);
        var existing = JsonSerializer.Deserialize<BaselineDocument>(existingJson, JsonOptions)!;

        AssertNoRegression("CompletionRequest.p50",
            existing.CompletionRequest.P50Ms, current.CompletionRequest.P50Ms);
        AssertNoRegression("FormatRequest.p50",
            existing.FormatRequest.P50Ms, current.FormatRequest.P50Ms);
        // A pre-closure baseline has no BulkFormatRequest block; its P50Ms deserialises to 0 and
        // AssertNoRegression skips it (contract invariant 1 — missing block ⇒ no assertion).
        AssertNoRegression("BulkFormatRequest.p50",
            existing.BulkFormatRequest.P50Ms, current.BulkFormatRequest.P50Ms);
    }

    /// <summary>
    /// Capture-mode guard: a workload whose p50 is below the floor makes the 5 % gate meaningless.
    /// This is a harness/corpus problem, not a code regression — the message says so explicitly so
    /// a future maintainer scales the corpus instead of hunting a non-existent slowdown.
    /// </summary>
    private static void AssertWorkloadFloor(string workload, double p50, double floorMs)
    {
        Assert.True(p50 >= floorMs,
            $"HARNESS BREAKAGE (not a code regression): {workload} p50 = {p50:F3} ms is below the "
            + $"{floorMs:F0} ms floor. At this size a 5 % gate just measures jitter. Scale the perf "
            + "corpus up — raise CorpusRepeats in PerformanceBaselineTests.cs — until every workload "
            + "clears its floor. See specs/022-m0-engine-closure/contracts/performance-gate.md.");
    }

    private static void AssertNoRegression(string name, double baseline, double current)
    {
        if (baseline <= 0.0) return;   // no baseline for this workload yet — skip
        var regression = (current - baseline) / baseline;
        Assert.True(regression <= MaxRegressionFraction,
            $"{name} regressed by {regression:P1} (baseline {baseline:F3} ms, current {current:F3} ms). "
            + $"Allowed: {MaxRegressionFraction:P0}. Re-run with AKML_UPDATE_BASELINE=1 if intentional.");
    }

    // ----------------------------------------------------------------------
    // Serialisable record types
    // ----------------------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed class BaselineDocument
    {
        public DateTime CaptureDate { get; set; }
        public string Machine { get; set; } = string.Empty;
        public string DotNetVersion { get; set; } = string.Empty;
        public BaselineSample CompletionRequest { get; set; } = new();
        public BaselineSample FormatRequest { get; set; } = new();

        /// <summary>
        /// Spec 022 US4 — third workload. Defaults to an empty sample so a pre-closure baseline
        /// JSON (which has no <c>bulkFormatRequest</c> key) deserialises with P50Ms = 0, which
        /// <see cref="AssertNoRegression"/> then skips (contract invariant 1).
        /// </summary>
        public BaselineSample BulkFormatRequest { get; set; } = new();
    }

    private sealed class BaselineSample
    {
        public double P50Ms { get; set; }
        public double P99Ms { get; set; }
        public int MeasureIterations { get; set; }
        public int WarmupIterations { get; set; }
        public int CorpusOffsetsPerIteration { get; set; }
    }
}

/// <summary>
/// Resolves the repository root from the test assembly location so the baseline file lives
/// inside the source tree (not the bin output).
/// </summary>
internal static class TestPaths
{
    public static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        return AppContext.BaseDirectory;
    }
}
