using AkmlSql.E2E.Tests.Infrastructure;
using Xunit;

namespace AkmlSql.E2E.Tests.Analyzer;

/// <summary>
/// Tests: --help, --version, no-args error.
/// </summary>
[Collection("Cli")]
public sealed class AnalyzerHelpTests(CliFixture cli)
{
    [Fact]
    public async Task Help_ExitsZero()
    {
        var r = await CliRunner.RunAsync(cli.AnalyzerExe, ["--help"]);
        Assert.Equal(0, r.ExitCode);
    }

    [Fact]
    public async Task Help_PrintsUsage()
    {
        var r = await CliRunner.RunAsync(cli.AnalyzerExe, ["--help"]);
        Assert.Contains("Usage", r.AllOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Help_ListsFileFlag()
    {
        var r = await CliRunner.RunAsync(cli.AnalyzerExe, ["--help"]);
        Assert.Contains("--file", r.AllOutput);
    }

    [Fact]
    public async Task Help_ListsCheckFlag()
    {
        var r = await CliRunner.RunAsync(cli.AnalyzerExe, ["--help"]);
        Assert.Contains("--check", r.AllOutput);
    }

    [Fact]
    public async Task Version_ExitsZero()
    {
        var r = await CliRunner.RunAsync(cli.AnalyzerExe, ["--version"]);
        Assert.Equal(0, r.ExitCode);
    }

    [Fact]
    public async Task Version_PrintsVersionString()
    {
        var r = await CliRunner.RunAsync(cli.AnalyzerExe, ["--version"]);
        // Should contain the product name or a version number like "1.0.0"
        Assert.True(
            r.AllOutput.Contains("Analyzer",   StringComparison.OrdinalIgnoreCase) ||
            r.AllOutput.Contains("1.0",         StringComparison.Ordinal),
            $"Version output: '{r.AllOutput}'");
    }

    [Fact]
    public async Task NoArgs_ExitsTwo_WithErrorMessage()
    {
        var r = await CliRunner.RunAsync(cli.AnalyzerExe, []);
        // Without --file or --directory, must report an error
        Assert.Equal(2, r.ExitCode);
        Assert.True(
            r.AllOutput.Contains("--file",      StringComparison.OrdinalIgnoreCase) ||
            r.AllOutput.Contains("--directory", StringComparison.OrdinalIgnoreCase) ||
            r.AllOutput.Contains("required",    StringComparison.OrdinalIgnoreCase),
            $"Expected error about --file/--directory, got: '{r.AllOutput}'");
    }
}
