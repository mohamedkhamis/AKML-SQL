using System.Text.Json;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Parser;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Engine.Tests.Completion;

/// <summary>
/// Spec 032 (T003) — corpus-driven completion gate. Runs the 2026-07-16 campaign corpus
/// (tests/completion-corpus/, 1,370 cases) through <see cref="CompletionEngine.GetCompletions"/>
/// against the fake Northwind_AutoTest cache and enforces RATCHETED per-family thresholds:
/// each user story's closing task adds its families to <see cref="FamilyThresholds"/> and
/// <see cref="ZeroItemAssertedFamilies"/>; from then on regressions fail the build.
/// Cases listed in exclusions.json (verified corpus mistakes) are reported, never failed.
/// At-cap misses (list hit the 50-item cap without the expected item) COUNT AS FAILURES —
/// correct scoping/ranking must surface expected items above the cap — and are tagged in output.
/// </summary>
public class CorpusGateTests
{
    private readonly ITestOutputHelper _output;

    public CorpusGateTests(ITestOutputHelper output) => _output = output;

    // ── Ratchet configuration (spec 032) ─────────────────────────────────────
    // Story gate tasks append entries here (T021, T026, T030, T035, T045, T051).
    // Fraction = minimum pass rate among non-excluded cases of the family.
    private static readonly Dictionary<string, double> FamilyThresholds = new(StringComparer.OrdinalIgnoreCase)
    {
        // Regression guards at the recorded ENGINE-LEVEL baseline (2026-07-17 pre-fix run:
        // 971/1346 = 72.1% overall) — any drop below these fails the build (SC-007).
        // Story gates RAISE entries to 0.90 as their fixes land; never lower one.
        // Final ratchet after US2–US6 (2026-07-17): engine-level 72.1% → 97.5% overall.
        // Never lower an entry; T057's live re-run is the acceptance authority.
        ["comments-strings"] = 1.00,
        ["brackets-quoted"] = 0.97,
        ["temp-tables"] = 0.98,
        ["multi-statement"] = 0.98,
        ["exec-procs"] = 0.98,
        ["delete"] = 0.98,
        ["functions"] = 0.98,
        ["cte"] = 0.97,
        ["subqueries"] = 0.97,
        ["join-on"] = 0.97,
        ["update"] = 0.97,
        ["where-having"] = 0.97,
        ["insert"] = 0.97,
        ["select-columns"] = 0.96,
        ["negative-controls"] = 0.95,
        ["star-and-misc"] = 0.94,
        ["schema-qualified"] = 0.94,
        ["keywords"] = 0.92,
        ["casing-prefix"] = 0.91,
        ["from-tables"] = 0.89,
    };

    // Families where "expected items but got ZERO items" must never happen (SC-002, ratcheted).
    // All previously-failing families are zero-item-free after US2–US6.
    private static readonly HashSet<string> ZeroItemAssertedFamilies = new(StringComparer.OrdinalIgnoreCase)
    {
        "update", "delete", "multi-statement", "exec-procs",
        "insert", "functions", "cte", "temp-tables", "subqueries",
        "brackets-quoted", "keywords", "where-having",
    };

    // Set to a fraction (e.g. 0.95) by the final acceptance task (T057) once all stories land.
    private static readonly double? OverallThreshold = null;

    // ── Corpus loading ───────────────────────────────────────────────────────

    private sealed class CorpusCase
    {
        public string Id { get; set; } = string.Empty;
        public string Family { get; set; } = string.Empty;
        public string Doc { get; set; } = string.Empty;
        public Expectation? Expect { get; set; }
        public string? Note { get; set; }
    }

    private sealed class Expectation
    {
        public string[]? MustContain { get; set; }
        public string[]? MustNotContain { get; set; }
        public int? MinCount { get; set; }
    }

    private sealed class ExclusionFile
    {
        public ExclusionEntry[] Excluded { get; set; } = [];
    }

    private sealed class ExclusionEntry
    {
        public string Id { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private static string CorpusDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "tests", "completion-corpus");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("tests/completion-corpus not found above " + AppContext.BaseDirectory);
    }

    private static IReadOnlyList<CorpusCase> LoadCases(string corpusDir)
    {
        var cases = new List<CorpusCase>();
        foreach (var file in Directory.GetFiles(corpusDir, "f*.json").OrderBy(f => f))
        {
            if (Path.GetFileName(file).StartsWith("f21", StringComparison.OrdinalIgnoreCase))
                continue; // formatting corpus — not completion
            var parsed = JsonSerializer.Deserialize<List<CorpusCase>>(File.ReadAllText(file), JsonOpts);
            if (parsed != null) cases.AddRange(parsed);
        }
        return cases;
    }

    private static HashSet<string> LoadExclusions(string corpusDir)
    {
        var path = Path.Combine(corpusDir, "exclusions.json");
        if (!File.Exists(path)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parsed = JsonSerializer.Deserialize<ExclusionFile>(File.ReadAllText(path), JsonOpts);
        return new HashSet<string>(
            (parsed?.Excluded ?? []).Select(e => e.Id),
            StringComparer.OrdinalIgnoreCase);
    }

    // ── Case evaluation ──────────────────────────────────────────────────────

    private sealed record CaseResult(string Id, string Family, bool Pass, bool Excluded, bool ZeroItem, bool AtCap, string? Detail);

    private static bool ItemMatches(CompletionItem item, string expected)
    {
        if (string.Equals(item.DisplayText, expected, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(item.InsertText, expected, StringComparison.OrdinalIgnoreCase)) return true;
        // Alias/schema-qualified display ("o.OrderID", "dbo.usp_GetCustomerOrders") counts as the item.
        if (item.DisplayText.EndsWith("." + expected, StringComparison.OrdinalIgnoreCase)) return true;
        if (item.InsertText.EndsWith("." + expected, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// mustContain matcher: additionally credits COMPOUND keyword items for their leading
    /// word ("OUTER JOIN" satisfies expected "OUTER") — the campaign verifiers' own
    /// "compound keyword items by design" ruling. NOT applied to mustNotContain: a banned
    /// keyword only fails on a real standalone item.
    /// </summary>
    private static bool ItemSatisfiesExpected(CompletionItem item, string expected)
    {
        if (ItemMatches(item, expected)) return true;
        if (item.DisplayText.StartsWith(expected + " ", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static CaseResult Evaluate(CompletionEngine engine, AkmlSql.Engine.Schema.DatabaseCache cache, CorpusCase c, bool excluded)
    {
        var caret = c.Doc.IndexOf('|');
        if (caret < 0)
            return new CaseResult(c.Id, c.Family, false, excluded, false, false, "corpus case has no caret marker");
        var sql = c.Doc.Remove(caret, 1);

        CompletionResponse response;
        try
        {
            response = engine.GetCompletions(sql, caret, cache);
        }
        catch (Exception ex)
        {
            return new CaseResult(c.Id, c.Family, false, excluded, false, false, $"exception: {ex.GetType().Name}: {ex.Message}");
        }

        var items = response.Items;
        var atCap = response.IsIncomplete;
        var failures = new List<string>();

        var mustContain = c.Expect?.MustContain ?? [];
        foreach (var expected in mustContain)
        {
            if (!items.Any(i => ItemSatisfiesExpected(i, expected)))
                failures.Add($"missing '{expected}'");
        }

        foreach (var banned in c.Expect?.MustNotContain ?? [])
        {
            if (items.Any(i => ItemMatches(i, banned)))
                failures.Add($"contains banned '{banned}'");
        }

        if (c.Expect?.MinCount is int min && items.Length < min)
            failures.Add($"count {items.Length} < minCount {min}");

        var zeroItem = mustContain.Length > 0 && items.Length == 0;
        var pass = failures.Count == 0;
        return new CaseResult(c.Id, c.Family, pass, excluded, zeroItem, atCap && !pass,
            pass ? null : string.Join("; ", failures));
    }

    private List<CaseResult> RunAll()
    {
        var corpusDir = CorpusDir();
        var cases = LoadCases(corpusDir);
        var exclusions = LoadExclusions(corpusDir);
        var cache = NorthwindAutoTestCacheFactory.Create();
        var engine = new CompletionEngine(new TsqlParserService());

        return cases
            .Select(c => Evaluate(engine, cache, c, exclusions.Contains(c.Id)))
            .ToList();
    }

    // ── The gate ─────────────────────────────────────────────────────────────

    [Fact]
    public void Corpus_loads_1376_cases_and_exclusions()
    {
        var corpusDir = CorpusDir();
        var cases = LoadCases(corpusDir);
        var exclusions = LoadExclusions(corpusDir);
        Assert.Equal(1376, cases.Count);
        // 24 campaign-verifier corpus mistakes + 9 spec-032 additions (engine-gate-only
        // CM6-filter cases that PASSED the live run + FUNC-060 fuzzy-by-design); see exclusions.json.
        Assert.Equal(33, exclusions.Count);
        Assert.All(cases, c => Assert.Contains('|', c.Doc));
    }

    [Fact]
    public void Family_pass_rates_meet_ratcheted_thresholds()
    {
        var results = RunAll();

        var families = results
            .GroupBy(r => r.Family, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _output.WriteLine($"{"family",-20} {"pass",5} {"total",5} {"rate",7} {"zero",5} {"atCap",5} {"excl",5}");
        var violations = new List<string>();
        int totalPass = 0, totalCounted = 0;

        foreach (var family in families)
        {
            var counted = family.Where(r => !r.Excluded).ToList();
            var pass = counted.Count(r => r.Pass);
            var zero = counted.Count(r => r.ZeroItem);
            var atCap = counted.Count(r => r.AtCap);
            var rate = counted.Count == 0 ? 1.0 : (double)pass / counted.Count;
            totalPass += pass;
            totalCounted += counted.Count;

            _output.WriteLine($"{family.Key,-20} {pass,5} {counted.Count,5} {rate,7:P1} {zero,5} {atCap,5} {family.Count(r => r.Excluded),5}");

            if (FamilyThresholds.TryGetValue(family.Key, out var threshold) && rate < threshold)
                violations.Add($"{family.Key}: {rate:P1} < ratcheted {threshold:P0}");

            if (ZeroItemAssertedFamilies.Contains(family.Key) && zero > 0)
                violations.Add($"{family.Key}: {zero} zero-item case(s), ratchet requires 0 — " +
                    string.Join(", ", counted.Where(r => r.ZeroItem).Take(5).Select(r => r.Id)));
        }

        var overall = totalCounted == 0 ? 1.0 : (double)totalPass / totalCounted;
        _output.WriteLine($"{"OVERALL",-20} {totalPass,5} {totalCounted,5} {overall,7:P1}");

        // Failing-case samples per family, for fast diagnosis.
        foreach (var family in families)
        {
            foreach (var fail in family.Where(r => !r.Excluded && !r.Pass).Take(4))
                _output.WriteLine($"  {fail.Id}{(fail.AtCap ? " [atCap]" : "")}: {fail.Detail}");
        }

        if (OverallThreshold is double ot && overall < ot)
            violations.Add($"overall: {overall:P1} < {ot:P0}");

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }
}
