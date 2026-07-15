using System.IO;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Parity;

/// <summary>
/// Spec 020 T071 / T072 / T073 — parity-corpus driver.
///
/// <para>
/// Enumerates <c>tests/format-parity/corpus/*.sql</c> and runs each input through
/// <see cref="AkmlSql.Formatting.Pipeline.FormatterPipeline"/> using each of the AKML built-in
/// styles (via <see cref="ParityHarness.Format"/>). Compares the
/// normalised output against the matching golden file at
/// <c>tests/format-parity/golden/&lt;input-stem&gt;__&lt;style&gt;.sql</c>.
/// </para>
///
/// <para><b>SC-007 normalisation</b> (per <c>tests/format-parity/README.md</c>):
/// strip trailing whitespace per line, normalise line endings to <c>\n</c>, drop UTF-8 BOM,
/// then require byte-exact equality.</para>
///
/// <para><b>Capture vs compare modes</b> (mirrors the
/// <c>PerformanceBaselineTests.Capture_or_compare_M0_baseline</c> pattern):
/// when a golden file is missing or <c>AKML_UPDATE_PARITY_GOLDEN=1</c> is set, the test runs
/// in capture mode and writes the golden. Otherwise compare mode: byte-exact equality is asserted.
/// </para>
///
/// <para><b>Today's golden = AKML's own output.</b> Acts as a drift-guard: any change that quietly
/// alters formatter output will fail this suite. When Redgate goldens are later generated (see
/// <c>tests/format-parity/README.md</c> for the swap-in procedure using
/// <c>SqlPrompt.Format.CommandLine.exe</c>), drop them into <c>golden/</c> and the same driver
/// becomes the SC-007 parity measurement against Redgate. No driver change required — only the
/// content of <c>golden/</c> changes.</para>
/// </summary>
public class FormatParityTests
{
    private const string GoldenEnvVar = "AKML_UPDATE_PARITY_GOLDEN";

    /// <summary>
    /// Built-in styles to exercise. Each name maps to <c>src/AkmlSql.Formatting/Profiles/BuiltIn/&lt;name&gt;.akmlstyle</c>.
    /// </summary>
    private static readonly string[] BuiltInStyles =
    {
        "default",
        "compact",
        "indented",
        "aligned-left-bracket",
        "leading-commas",
        "minimalist",
    };

    public static IEnumerable<object[]> CorpusStyleMatrix()
    {
        var repoRoot = FindRepoRoot();
        var corpusDir = Path.Combine(repoRoot, "tests", "format-parity", "corpus");
        if (!Directory.Exists(corpusDir)) yield break;

        foreach (var path in Directory.EnumerateFiles(corpusDir, "*.sql").OrderBy(p => p))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            foreach (var style in BuiltInStyles)
            {
                yield return new object[] { name, style };
            }
        }
    }

    [Theory]
    [MemberData(nameof(CorpusStyleMatrix))]
    public void Corpus_Matches_Golden(string corpusName, string styleName)
    {
        var repoRoot = FindRepoRoot();
        var corpusPath = Path.Combine(repoRoot, "tests", "format-parity", "corpus", corpusName + ".sql");
        var goldenPath = Path.Combine(repoRoot, "tests", "format-parity", "golden", $"{corpusName}__{styleName}.sql");
        var stylePath = Path.Combine(repoRoot, "src", "AkmlSql.Formatting", "Profiles", "BuiltIn", styleName + ".akmlstyle");

        Assert.True(File.Exists(corpusPath), $"Corpus file missing: {corpusPath}");
        Assert.True(File.Exists(stylePath), $"Built-in style file missing: {stylePath}");

        var inputSql = File.ReadAllText(corpusPath);
        var profileJson = File.ReadAllText(stylePath);
        var profile = ProfileSerializer.Deserialize(profileJson);

        // ParityHarness.Format disables stage-7 idempotency so a single (input, style) pass
        // produces deterministic output even for inputs that wouldn't otherwise re-parse
        // identically. Stage 6 (semantic) still runs — if it rejects, we capture the original
        // input as the golden, which is the expected pipeline behaviour and will be visible in
        // the golden file for review.
        var formatted = ParityHarness.Format(inputSql, profile);
        var actual = ParityHarness.Normalise(formatted);

        var shouldUpdate = string.Equals(
            Environment.GetEnvironmentVariable(GoldenEnvVar), "1", StringComparison.Ordinal);

        if (!File.Exists(goldenPath) || shouldUpdate)
        {
            // Capture mode — record this run as the baseline.
            var dir = Path.GetDirectoryName(goldenPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(goldenPath, actual);
            // First-capture pass still asserts the output is non-empty so a broken harness fails loudly.
            Assert.False(string.IsNullOrWhiteSpace(actual),
                $"Capture mode produced empty output for ({corpusName}, {styleName}). Refusing to write empty golden.");
            return;
        }

        var expected = ParityHarness.Normalise(File.ReadAllText(goldenPath));
        Assert.Equal(expected, actual);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate AKML-SQL.slnx walking up from " + AppContext.BaseDirectory);
    }
}
