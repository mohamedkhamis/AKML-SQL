using Xunit;

namespace AkmlSql.E2E.Tests.Infrastructure;

/// <summary>
/// Marks the "Cli" xUnit collection and wires in <see cref="CliFixture"/>.
/// Every test class decorated with [Collection("Cli")] shares one fixture instance.
/// </summary>
[CollectionDefinition("Cli")]
public sealed class CliCollection : ICollectionFixture<CliFixture> { }

/// <summary>
/// Collection fixture that builds the Formatter and Analyzer CLI projects exactly
/// once before any E2E test runs, then exposes the paths to the built executables.
///
/// Pre-requisite: the .NET 10 SDK must be on PATH (dotnet.exe).
/// </summary>
public sealed class CliFixture : IAsyncLifetime
{
    // ── Exposed paths ────────────────────────────────────────────────────────

    public string FormatterExe { get; private set; } = string.Empty;
    public string AnalyzerExe  { get; private set; } = string.Empty;

    // ── IAsyncLifetime ───────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        var root = FindRepoRoot();

        FormatterExe = await EnsureBuiltAsync(
            projRelPath: Path.Combine("src", "AkmlSql.Formatter",  "AkmlSql.Formatter.csproj"),
            exeRelPath:  Path.Combine("src", "AkmlSql.Formatter",  "bin", "Debug", "net10.0", "win-x64", "akmlsql-format.exe"),
            repoRoot:    root);

        AnalyzerExe = await EnsureBuiltAsync(
            projRelPath: Path.Combine("src", "AkmlSql.Analyzer",   "AkmlSql.Analyzer.csproj"),
            exeRelPath:  Path.Combine("src", "AkmlSql.Analyzer",   "bin", "Debug", "net10.0", "win-x64", "AkmlSql.Analyzer.exe"),
            repoRoot:    root);
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the project if the expected exe does not exist yet (incremental).
    /// Returns the absolute path to the exe.
    /// </summary>
    private static async Task<string> EnsureBuiltAsync(
        string projRelPath, string exeRelPath, string repoRoot)
    {
        var exePath  = Path.Combine(repoRoot, exeRelPath);
        var projPath = Path.Combine(repoRoot, projRelPath);

        // Always run an incremental build so source changes are reflected automatically.
        // dotnet build is fast when nothing changed (timestamps-based).
        var result = await CliRunner.RunAsync(
            exe:       "dotnet",
            args:      ["build", projPath, "-c", "Debug", "--nologo", "-v", "q"],
            timeoutMs: 600_000); // 10 minutes

        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"Build failed for {projRelPath} (exit {result.ExitCode}).\n" +
                $"stdout:\n{result.Stdout}\nstderr:\n{result.Stderr}");

        if (!File.Exists(exePath))
            throw new FileNotFoundException(
                $"Build succeeded but executable not found at: {exePath}");

        return exePath;
    }

    /// <summary>Walks up from the test assembly until a .slnx or .sln file is found.</summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("*.slnx").Length > 0 || dir.GetFiles("*.sln").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            $"Could not find repo root from '{AppContext.BaseDirectory}'. " +
            "Expected a .slnx or .sln file somewhere in the directory tree.");
    }
}
