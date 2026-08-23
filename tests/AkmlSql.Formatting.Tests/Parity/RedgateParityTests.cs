using System.IO;
using AkmlSql.Formatting.Profiles;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Formatting.Tests.Parity;

/// <summary>
/// Spec 031 FR-041 / SC-003 — formats the sp031 corpus with the imported MohamedKhamis style
/// (<c>tests/AkmlSql.Formatting.Tests/Fixtures/MohamedKhamis-style.json</c>, round-tripped through
/// <see cref="RedgateJsonStyleImporter"/>) and compares byte-exact (post-<see
/// cref="ParityHarness.Normalise"/>) against SQL Prompt 11 goldens at
/// <c>tests/format-parity/golden/sp031-NN-…__mohamedkhamis.sql</c>.
///
/// <para><b>Skip idiom:</b> the repo's xunit version is 2.x (no <c>Assert.SkipWhen</c>, which is
/// xunit v3), and this test project does not reference <c>Xunit.SkippableFact</c> (unlike
/// AkmlSql.Installer.Tests / AkmlSql.Engine.Tests / the E2E projects, which gate on host
/// capability). Rather than add a new package dependency for a golden-availability gate, each case
/// emits a visible "SKIP: ..." line via <see cref="ITestOutputHelper"/> and returns early —
/// reported as Passed, not Failed, while the golden is absent — pointing at
/// <c>specs/031-redgate-style-import/runbook-goldens.md</c> so the gap is self-documenting. This
/// mirrors <c>FormatParityTests</c>' own capture-mode early-return shape.</para>
///
/// <para>No goldens exist yet as of this writing (Phase 2) — the user generates them manually via
/// SQL Prompt 11 per the runbook. Every case below is expected to SKIP until then. Once goldens
/// land, re-run and record the pass count as the "starting fidelity" per Task 13 Step 3.</para>
/// </summary>
public class RedgateParityTests(ITestOutputHelper output)
{
    private const string RunbookPointer = "specs/031-redgate-style-import/runbook-goldens.md";

    public static TheoryData<string> CorpusFiles()
    {
        var dir = Path.Combine(RepoRoot(), "tests", "format-parity", "corpus");
        var data = new TheoryData<string>();
        foreach (var f in Directory.EnumerateFiles(dir, "sp031-*.sql").OrderBy(x => x))
            data.Add(Path.GetFileNameWithoutExtension(f));
        return data;
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Corpus_file_matches_sqlprompt11_golden(string stem)
    {
        var goldenPath = Path.Combine(RepoRoot(), "tests", "format-parity", "golden", stem + "__mohamedkhamis.sql");
        if (!File.Exists(goldenPath))
        {
            output.WriteLine($"SKIP: golden not yet delivered: {goldenPath} (see {RunbookPointer})");
            return;
        }

        var input = File.ReadAllText(Path.Combine(RepoRoot(), "tests", "format-parity", "corpus", stem + ".sql"));
        var style = RedgateJsonStyleImporter.Import(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MohamedKhamis-style.json"))).Profile;

        var formatted = ParityHarness.Format(input, style);
        var expected = ParityHarness.Normalise(File.ReadAllText(goldenPath));
        var actual = ParityHarness.Normalise(formatted);

        if (expected != actual)
            output.WriteLine(ParityHarness.FirstDiff(expected, actual));
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Not a gate — a measurement instrument (Task 13 Step 3's "starting fidelity"). Always passes;
    /// writes "SP11 goldens present: N/20; byte-match: M/N" to test output, or the runbook pointer
    /// when N==0.
    /// </summary>
    [Fact]
    public void Fidelity_summary()
    {
        var corpusDir = Path.Combine(RepoRoot(), "tests", "format-parity", "corpus");
        var goldenDir = Path.Combine(RepoRoot(), "tests", "format-parity", "golden");
        var stems = Directory.EnumerateFiles(corpusDir, "sp031-*.sql")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var stylePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "MohamedKhamis-style.json");
        var style = RedgateJsonStyleImporter.Import(File.ReadAllText(stylePath)).Profile;

        var present = 0;
        var matched = 0;
        var mismatched = new List<string>();

        foreach (var stem in stems)
        {
            var goldenPath = Path.Combine(goldenDir, stem + "__mohamedkhamis.sql");
            if (!File.Exists(goldenPath)) continue;
            present++;

            var input = File.ReadAllText(Path.Combine(corpusDir, stem + ".sql"));
            var formatted = ParityHarness.Format(input, style);
            var expected = ParityHarness.Normalise(File.ReadAllText(goldenPath));
            var actual = ParityHarness.Normalise(formatted);

            if (expected == actual) matched++;
            else mismatched.Add(stem);
        }

        if (present == 0)
        {
            output.WriteLine($"SP11 goldens present: 0/{stems.Count} -- no goldens yet -- see {RunbookPointer}");
        }
        else
        {
            output.WriteLine($"SP11 goldens present: {present}/{stems.Count}; byte-match: {matched}/{present}");
            if (mismatched.Count > 0)
                output.WriteLine("Mismatched: " + string.Join(", ", mismatched));
        }

        // Measurement only -- never fails the build.
        Assert.True(true);
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "AKML-SQL.slnx")))
            dir = Path.GetDirectoryName(dir) ?? throw new InvalidOperationException("repo root not found");
        return dir;
    }
}
