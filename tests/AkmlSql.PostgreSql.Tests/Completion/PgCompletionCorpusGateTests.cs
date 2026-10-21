using System.Text.Json;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.PostgreSql.Completion;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.PostgreSql.Tests.Completion;

/// <summary>
/// Spec 042 (task T095, T109, SC-005) - the PostgreSQL completion corpus gate. Every case in <c>tests/pg-completion-corpus</c>
/// runs through <see cref="PgCompletionEngine"/> against the fake Northwind cache; the overall pass rate must be at least
/// <c>gate.json</c>'s <c>minPassRate</c> and no family may drop below its recorded threshold (a ratchet: a change that lowers
/// one is a regression to fix, not a baseline to bless). Cases in <c>exclusions.json</c> are reported and never counted.
/// </summary>
[Trait("Category", "CompletionCorpus")]
public class PgCompletionCorpusGateTests
{
    private readonly ITestOutputHelper _output;

    public PgCompletionCorpusGateTests(ITestOutputHelper output) => _output = output;

    private sealed class CorpusCase
    {
        public string Id { get; set; } = string.Empty;
        public string Family { get; set; } = string.Empty;
        public string Doc { get; set; } = string.Empty;
        public Expectation? Expect { get; set; }
        public string? Note { get; set; }
        public int? ServerMajor { get; set; }
        public string[]? SearchPath { get; set; }
    }

    private sealed class Expectation
    {
        public string[]? MustContain { get; set; }
        public string[]? MustNotContain { get; set; }
        public int? MinCount { get; set; }
    }

    private sealed class Gate
    {
        public double MinPassRate { get; set; } = 0.95;
        public int MinCases { get; set; } = 300;
        public Dictionary<string, double> Families { get; set; } = new();
    }

    private sealed class Exclusion
    {
        public string Id { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    private sealed record Result(CorpusCase Case, bool Pass, bool Excluded, string? Detail);

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static string CorpusDirectory()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "tests", "pg-completion-corpus");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("tests/pg-completion-corpus not found above " + AppContext.BaseDirectory);
    }

    private static List<CorpusCase> Load(string dir) =>
        Directory.GetFiles(dir, "p*.json").OrderBy(f => f, StringComparer.Ordinal)
            .SelectMany(f => JsonSerializer.Deserialize<List<CorpusCase>>(File.ReadAllText(f), Json) ?? new List<CorpusCase>())
            .ToList();

    private static bool Matches(CompletionItem item, string expected) =>
        string.Equals(item.DisplayText, expected, StringComparison.OrdinalIgnoreCase)
        || string.Equals(item.InsertText, expected, StringComparison.OrdinalIgnoreCase)
        || item.DisplayText.EndsWith("." + expected, StringComparison.OrdinalIgnoreCase)
        || item.InsertText.EndsWith("." + expected, StringComparison.OrdinalIgnoreCase);

    // A compound key word counts for its first word ("OUTER JOIN" satisfies "OUTER"): the same ruling as the T-SQL corpus.
    private static bool Satisfies(CompletionItem item, string expected) =>
        Matches(item, expected) || item.DisplayText.StartsWith(expected + " ", StringComparison.OrdinalIgnoreCase);

    private static Result Evaluate(PgCompletionEngine engine, CorpusCase c, bool excluded)
    {
        var caret = c.Doc.IndexOf('|');
        if (caret < 0) return new Result(c, false, excluded, "corpus case has no caret marker");
        var sql = c.Doc.Remove(caret, 1);

        var cache = NorthwindPgTestCacheFactory.Create(c.SearchPath ?? Array.Empty<string>());
        var options = new PgCompletionOptions { ServerMajor = c.ServerMajor ?? 18 };

        CompletionResponse response;
        try
        {
            response = engine.GetCompletions(sql, caret, cache, options);
        }
        catch (Exception ex)
        {
            return new Result(c, false, excluded, $"exception: {ex.GetType().Name}: {ex.Message}");
        }

        var failures = new List<string>();
        foreach (var expected in c.Expect?.MustContain ?? Array.Empty<string>())
            if (!response.Items.Any(i => Satisfies(i, expected)))
                failures.Add($"missing '{expected}'");

        foreach (var banned in c.Expect?.MustNotContain ?? Array.Empty<string>())
            if (response.Items.Any(i => Matches(i, banned)))
                failures.Add($"contains banned '{banned}'");

        if (c.Expect?.MinCount is int min && response.Items.Length < min)
            failures.Add($"count {response.Items.Length} < minCount {min}");

        if (failures.Count > 0 && response.IsIncomplete) failures.Add("(list hit the cap)");
        return new Result(c, failures.Count == 0, excluded, failures.Count == 0 ? null : string.Join("; ", failures));
    }

    private List<Result> RunAll(out Gate gate)
    {
        var dir = CorpusDirectory();
        var cases = Load(dir);
        gate = JsonSerializer.Deserialize<Gate>(File.ReadAllText(Path.Combine(dir, "gate.json")), Json) ?? new Gate();
        var exclusionsPath = Path.Combine(dir, "exclusions.json");
        var exclusions = File.Exists(exclusionsPath)
            ? (JsonSerializer.Deserialize<List<Exclusion>>(File.ReadAllText(exclusionsPath), Json) ?? new List<Exclusion>()).Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var engine = new PgCompletionEngine();
        return cases.Select(c => Evaluate(engine, c, exclusions.Contains(c.Id))).ToList();
    }

    [Fact]
    public void The_corpus_has_enough_cases_and_unique_ids()
    {
        var results = RunAll(out var gate);

        Assert.True(results.Count(r => !r.Excluded) >= gate.MinCases, $"the corpus has {results.Count(r => !r.Excluded)} counted cases, the gate needs {gate.MinCases}");
        Assert.Empty(results.GroupBy(r => r.Case.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key));
        Assert.All(results, r => Assert.True(r.Case.Doc.Contains('|'), r.Case.Id + " has no caret"));
    }

    [Fact]
    public void The_pass_rate_meets_the_gate_and_no_family_falls_below_its_ratchet()
    {
        var results = RunAll(out var gate);
        var counted = results.Where(r => !r.Excluded).ToList();

        var rate = counted.Count(r => r.Pass) / (double)counted.Count;
        _output.WriteLine($"overall: {counted.Count(r => r.Pass)}/{counted.Count} = {rate:P1} (gate {gate.MinPassRate:P0})");
        foreach (var family in counted.GroupBy(r => r.Case.Family).OrderBy(g => g.Key))
            _output.WriteLine($"  {family.Key,-22} {family.Count(r => r.Pass),3}/{family.Count(),3} = {family.Count(r => r.Pass) / (double)family.Count():P1}");
        foreach (var failed in counted.Where(r => !r.Pass))
            _output.WriteLine($"  FAIL {failed.Case.Id}: {failed.Case.Doc.Replace("\n", "\\n")}  ->  {failed.Detail}");
        foreach (var skipped in results.Where(r => r.Excluded))
            _output.WriteLine($"  excluded {skipped.Case.Id}");

        Assert.True(rate >= gate.MinPassRate, $"pass rate {rate:P1} is below the gate {gate.MinPassRate:P0}");

        foreach (var (family, threshold) in gate.Families)
        {
            var cases = counted.Where(r => string.Equals(r.Case.Family, family, StringComparison.OrdinalIgnoreCase)).ToList();
            if (cases.Count == 0) continue;
            var familyRate = cases.Count(r => r.Pass) / (double)cases.Count;
            Assert.True(familyRate >= threshold, $"family '{family}' passes {familyRate:P1}, below its ratchet {threshold:P0}");
        }
    }

    [Fact]
    public void No_case_throws()
    {
        var results = RunAll(out _);

        Assert.Empty(results.Where(r => r.Detail != null && r.Detail.StartsWith("exception:", StringComparison.Ordinal)).Select(r => r.Case.Id + " " + r.Detail));
    }

    [Fact]
    public void Every_exclusion_names_a_case_and_gives_a_reason()
    {
        var dir = CorpusDirectory();
        var cases = Load(dir).Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(dir, "exclusions.json");
        var exclusions = JsonSerializer.Deserialize<List<Exclusion>>(File.ReadAllText(path), Json) ?? new List<Exclusion>();

        Assert.All(exclusions, e =>
        {
            Assert.Contains(e.Id, cases);
            Assert.False(string.IsNullOrWhiteSpace(e.Reason), e.Id + " has no reason");
        });
        Assert.True(exclusions.Count <= 12, "the exclusion list should stay short");
    }
}
