using System.Text;
using System.Text.Json;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Web.Services;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.Tests.Parity;

/// <summary>
/// Spec 024 T007 — opt-in baseline generator for the format / analyse parity tests.
///
/// Runs the SAME <see cref="FormatterService"/> and <see cref="AnalyserService"/>
/// the web edition resolves through DI against every <c>tests/format-parity/corpus/*.sql</c>
/// under each built-in profile, and writes the byte-exact output as
/// <c>baselines/&lt;profile&gt;/&lt;id&gt;.expected.sql</c> (formatter, per-profile) and
/// <c>baselines/default/&lt;id&gt;.expected.json</c> (analyser, profile-independent) per
/// <c>specs/024-m2-web-closure/contracts/parity-baseline-format.md</c>.
///
/// **Opt-in** — this test WRITES into the source tree, so it is gated on the
/// <c>AKML_REGEN_PARITY_BASELINE</c> environment variable. Without it the test is a
/// no-op, so a plain <c>dotnet test</c> never mutates the committed baseline files.
/// The <c>[Trait("Category","ParityBaseline")]</c> tag additionally lets CI exclude it
/// by filter.
///
/// To regenerate:
/// <code>
///   AKML_REGEN_PARITY_BASELINE=1 dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj `
///     --filter "Category=ParityBaseline" --logger "console;verbosity=detailed"
/// </code>
/// The generator is round-trip safe: a second run with no code change is a no-op
/// (byte-identical output, zero PR noise).
///
/// See <c>specs/024-m2-web-closure/research.md</c> Decision 3.
/// </summary>
public sealed class ParityBaselineGenerator
{
    private const string RegenEnvVar = "AKML_REGEN_PARITY_BASELINE";

    private readonly ITestOutputHelper _output;

    public ParityBaselineGenerator(ITestOutputHelper output) => _output = output;

    [Fact]
    [Trait("Category", "ParityBaseline")]
    public async Task Generate_parity_baselines_for_the_corpus()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(RegenEnvVar)))
        {
            _output.WriteLine(
                $"SKIPPED: parity-baseline generation is opt-in. Set {RegenEnvVar}=1 to (re)generate " +
                $"tests/format-parity/baselines/**/*.expected.{{sql,json}} for the corpus.");
            return;
        }

        var revision = ParityCorpusLoader.CurrentBaselineRevision;
        _output.WriteLine($"Baseline revision: {revision}");

        var profiles = ParityCorpusLoader.ProfileIds
            .Select(id => (ProfileId: id, Profile: ParityCorpusLoader.GetProfile(id)))
            .ToArray();
        var formatter = new FormatterService();
        var analyser = new AnalyserService();

        EnsureDirectory(Path.Combine(ParityCorpusLoader.BaselinesDirectory(), "default"));
        foreach (var profileId in ParityCorpusLoader.ProfileIds)
        {
            EnsureDirectory(Path.Combine(ParityCorpusLoader.BaselinesDirectory(), profileId));
        }

        var corpusItems = ParityCorpusLoader.EnumerateCorpus().ToList();
        Assert.NotEmpty(corpusItems);
        _output.WriteLine($"Corpus items: {corpusItems.Count}");

        foreach (var (corpusId, sqlPath) in corpusItems)
        {
            var sql = await File.ReadAllTextAsync(sqlPath);

            // Formatter baseline per profile.
            foreach (var (profileId, profile) in profiles)
            {
                var result = formatter.Format(sql, profile);
                Assert.True(result.Success || string.IsNullOrEmpty(sql),
                    $"Formatter failed for corpus '{corpusId}' profile '{profileId}': " +
                    string.Join("; ", result.Diagnostics.Select(d => $"{d.Severity}:{d.Message}")));

                var marker = $"-- akml-parity-baseline revision={revision} corpus-item={corpusId} profile={profileId}\n";
                var body = ParityCorpusLoader.NormaliseLineEndings(result.FormattedText);
                var outputPath = Path.Combine(
                    ParityCorpusLoader.BaselinesDirectory(),
                    profileId,
                    corpusId + ".expected.sql");
                await File.WriteAllTextAsync(outputPath, marker + body, new UTF8Encoding(false));
                _output.WriteLine($"  format[{profileId}] {corpusId}: {body.Length} chars");
            }

            // Analyser baseline (default profile only — analysis is profile-independent).
            var analysis = await analyser.AnalyseAsync(sql);
            var envelope = new
            {
                akmlParityBaseline = new
                {
                    revision,
                    corpusItem = corpusId,
                    profile = "default",
                },
                findings = analysis.Issues
                    .OrderBy(i => i.Line)
                    .ThenBy(i => i.Column)
                    .ThenBy(i => i.RuleId, StringComparer.Ordinal)
                    .Select(i => new
                    {
                        ruleId = i.RuleId,
                        severity = ParityCorpusLoader.SeverityName(i.Severity),
                        message = i.Message,
                        line = i.Line,
                        column = i.Column,
                    })
                    .ToArray(),
            };
            var jsonPath = Path.Combine(
                ParityCorpusLoader.BaselinesDirectory(),
                "default",
                corpusId + ".expected.json");
            var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
            await File.WriteAllTextAsync(jsonPath, json + "\n", new UTF8Encoding(false));
            _output.WriteLine($"  analyse[default] {corpusId}: {analysis.Issues.Length} finding(s)");
        }

        _output.WriteLine(
            $"Done. Baselines under {ParityCorpusLoader.BaselinesDirectory()} updated; " +
            "commit the resulting files alongside any ide-plugin-version.txt bump.");
    }

    private static void EnsureDirectory(string path)
    {
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
    }
}
