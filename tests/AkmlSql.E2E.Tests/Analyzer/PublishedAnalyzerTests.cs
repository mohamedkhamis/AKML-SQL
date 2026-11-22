using AkmlSql.E2E.Tests.Infrastructure;
using Xunit;

namespace AkmlSql.E2E.Tests.Analyzer;

/// <summary>
/// Spec 047 (CLI-001): the shipped analyzer is the trimmed, single-file Release publish, and trimming once removed every
/// analysis rule (they are found by reflection), so the installed exe answered "No issues found" for any SQL. The other
/// analyzer tests run the untrimmed Debug build and could not see it. These run the real publish, on committed fixtures.
/// <para>The publish compiles the referenced libraries into their <c>obj/Release</c> folders, which the engine-launch
/// fixture's Release build also writes: run alone (<see cref="PublishedCliCollection"/>), never beside it.</para>
/// </summary>
[Trait("Category", "PublishedCli")]
[Collection(PublishedCliCollection.Name)]
public sealed class PublishedAnalyzerTests(PublishedAnalyzerFixture published) : IClassFixture<PublishedAnalyzerFixture>
{
    [Fact]
    public async Task Reports_the_three_findings_of_the_multi_statement_fixture()
    {
        var r = await published.RunAsync("--file", published.Fixture("multi.sql"));

        Assert.Contains("PE002", r.Stdout);
        Assert.Contains("PE003", r.Stdout);
        Assert.Contains("PE004", r.Stdout);
    }

    [Fact]
    public async Task Check_mode_fails_on_the_multi_statement_fixture()
    {
        var r = await published.RunAsync("--file", published.Fixture("multi.sql"), "--check");

        Assert.Equal(1, r.ExitCode);
    }

    [Fact]
    public async Task Reports_one_missing_semicolon_when_only_that_rule_runs()
    {
        var r = await published.RunAsync("--file", published.Fixture("sem1.sql"), "--rules", "ST004");

        var findings = r.Stdout.Split('\n').Where(l => l.Contains("ST004", StringComparison.Ordinal)).ToList();
        var line = Assert.Single(findings);
        Assert.Contains("information", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_explicit_settings_file_turns_a_rule_off()
    {
        var r = await published.RunAsync(
            "--file", published.Fixture("multi.sql"), "--settings", published.Fixture("pe004-off.casettings"));

        Assert.Contains("PE003", r.Stdout);
        Assert.DoesNotContain("PE004", r.Stdout);
    }
}

/// <summary>
/// Spec 047: the published-CLI tests run in a collection of their own with parallelization off, so xUnit runs them alone
/// after the parallel collections. Their Release publish and the engine-launch fixture's Release build compile the same
/// libraries into the same obj folders, and side by side one of them failed on a locked AkmlSql.Analysis.dll (CS2012).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PublishedCliCollection
{
    public const string Name = "PublishedCli";
}

/// <summary>Publishes the analyzer exactly as the release does (Release, win-x64, trimmed single file) once per run.</summary>
public sealed class PublishedAnalyzerFixture : IAsyncLifetime
{
    private string _publishDir = string.Empty;

    public string RepoRoot { get; private set; } = string.Empty;

    public string Exe { get; private set; } = string.Empty;

    public string Fixture(string name) => Path.Combine(RepoRoot, "tests", "fixtures", "cli", name);

    public Task<CliResult> RunAsync(params string[] args) => CliRunner.RunAsync(Exe, args, timeoutMs: 60_000);

    public async Task InitializeAsync()
    {
        RepoRoot = FindRepoRoot();
        _publishDir = Path.Combine(Path.GetTempPath(), "akml-published-analyzer-" + Guid.NewGuid().ToString("N"));
        var project = Path.Combine(RepoRoot, "src", "AkmlSql.Analyzer", "AkmlSql.Analyzer.csproj");

        var result = await CliRunner.RunAsync(
            "dotnet",
            ["publish", project, "-c", "Release", "-r", "win-x64", "-o", _publishDir, "--nologo", "-v", "q"],
            timeoutMs: 900_000);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"Publishing the analyzer failed (exit {result.ExitCode}).\n{result.AllOutput}");

        Exe = Path.Combine(_publishDir, "AkmlSql.Analyzer.exe");
        if (!File.Exists(Exe))
            throw new FileNotFoundException($"The publish produced no AkmlSql.Analyzer.exe in {_publishDir}.");
    }

    public Task DisposeAsync()
    {
        try { Directory.Delete(_publishDir, recursive: true); }
        catch (IOException) { /* a scanner may still hold the exe; the temp folder is cleaned later */ }
        catch (UnauthorizedAccessException) { }
        return Task.CompletedTask;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("*.slnx").Length > 0 || dir.GetFiles("*.sln").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException($"Could not find the repo root from '{AppContext.BaseDirectory}'.");
    }
}
