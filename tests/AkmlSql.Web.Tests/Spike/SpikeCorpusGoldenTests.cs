using System.Text.Json;
using AkmlSql.Web.Pages;
using AkmlSql.Web.Services;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.Tests.Spike;

/// <summary>
/// Spec 023 (M1 ScriptDom-in-WASM spike) -- T017. The desktop golden-file generator.
///
/// Runs the SAME libraries the spike runs in the browser -- <see cref="FormatterService"/>
/// (FormatterPipeline) and <see cref="AnalyserService"/> (AnalysisEngine) -- on desktop
/// .NET over every corpus .sql, writing {id}.expected.sql and {id}.expected.json into
/// src/AkmlSql.Web/wwwroot/spike-corpus/. The spike (in WASM) fetches and diffs against
/// these golden files; the ONLY variable between golden and spike output is the runtime
/// (desktop CoreCLR vs. browser WASM), so any mismatch is a pure WASM-runtime finding.
///
/// Opt-in -- this test WRITES into the source tree, so it is gated on the
/// <c>AKML_REGEN_GOLDEN</c> environment variable: without it the test is a no-op, so a
/// plain <c>dotnet test</c> never mutates the committed golden files. To (re)generate:
///   AKML_REGEN_GOLDEN=1 dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj
///     --filter "Category=SpikeGenerator" --logger "console;verbosity=detailed"
/// The [Trait("Category","SpikeGenerator")] tag additionally lets CI exclude it by filter.
///
/// See contracts/measurement-protocol.md M6 and research.md Decision 4.
/// </summary>
public sealed class SpikeCorpusGoldenTests
{
    private const string RegenEnvVar = "AKML_REGEN_GOLDEN";

    private readonly ITestOutputHelper _output;

    public SpikeCorpusGoldenTests(ITestOutputHelper output) => _output = output;

    [Fact]
    [Trait("Category", "SpikeGenerator")]
    public async Task Generate_golden_files_for_the_spike_corpus()
    {
        // Opt-in guard: this test writes into the source tree. Without AKML_REGEN_GOLDEN
        // it is a no-op so a plain `dotnet test` never mutates the committed golden files.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(RegenEnvVar)))
        {
            _output.WriteLine(
                $"SKIPPED: golden-file generation is opt-in. Set {RegenEnvVar}=1 to "
                + "(re)generate the spike-corpus/*.expected.* files. This guard keeps a "
                + "plain `dotnet test` from writing into the source tree.");
            return;
        }

        var corpusDir = LocateCorpusDirectory();
        var manifestPath = Path.Combine(corpusDir, "corpus.json");
        Assert.True(File.Exists(manifestPath), $"corpus.json not found at {manifestPath}");

        var items = JsonSerializer.Deserialize<SpikeCorpusItem[]>(
            File.ReadAllText(manifestPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(items);
        Assert.NotEmpty(items!);

        // The exact classes the Blazor surface resolves through DI.
        var formatter = new FormatterService();
        var analyser = new AnalyserService();

        foreach (var item in items!)
        {
            var sqlPath = Path.Combine(corpusDir, Path.GetFileName(item.SqlPath));
            Assert.True(File.Exists(sqlPath), $"corpus .sql missing: {sqlPath}");
            var sql = File.ReadAllText(sqlPath);

            var formatted = formatter.Format(sql);
            var analysis = await analyser.AnalyseAsync(sql);

            // Report what was generated -- a golden generator should be transparent
            // about whether the formatter actually transformed each item.
            var diag = formatted.Diagnostics.Length == 0
                ? "(none)"
                : string.Join(" | ", formatted.Diagnostics.Select(d => $"{d.Severity}:{d.Message}"));
            _output.WriteLine(
                $"{item.Id}: format Success={formatted.Success} ValidationPassed={formatted.ValidationPassed} "
                + $"WasModified={formatted.WasModified} | analyse issues={analysis.Issues.Length} "
                + $"| diagnostics={diag}");

            Assert.False(
                string.IsNullOrWhiteSpace(formatted.FormattedText),
                $"formatter produced empty output for corpus item '{item.Id}'");

            File.WriteAllText(
                Path.Combine(corpusDir, Path.GetFileName(item.ExpectedFormattedPath)),
                formatted.FormattedText);
            File.WriteAllText(
                Path.Combine(corpusDir, Path.GetFileName(item.ExpectedAnalysisPath)),
                SpikeGolden.FindingsToJson(analysis.Issues));
        }
    }

    /// <summary>Walk up from the test output directory to the repo root (marked by AKML-SQL.slnx).</summary>
    private static string LocateCorpusDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "repo root (AKML-SQL.slnx) not found above the test output directory");
        return Path.Combine(dir!.FullName, "src", "AkmlSql.Web", "wwwroot", "spike-corpus");
    }
}
