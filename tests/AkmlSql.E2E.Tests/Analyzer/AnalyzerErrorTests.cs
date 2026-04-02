using AkmlSql.E2E.Tests.Infrastructure;
using Xunit;

namespace AkmlSql.E2E.Tests.Analyzer;

/// <summary>
/// Tests: invalid arguments and missing inputs all return exit code 2
/// with a descriptive error message.
/// </summary>
[Collection("Cli")]
public sealed class AnalyzerErrorTests(CliFixture cli)
{
    // ── Missing input ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Error_NoFileOrDirectory_ExitsTwo()
    {
        var r = await CliRunner.RunAsync(cli.AnalyzerExe, []);
        Assert.Equal(2, r.ExitCode);
    }

    // ── File / directory not found ────────────────────────────────────────────

    [Fact]
    public async Task Error_FileNotFound_ExitsTwo()
    {
        var r = await CliRunner.RunAsync(
            cli.AnalyzerExe, ["--file", @"C:\no\such\file.sql"]);

        Assert.Equal(2, r.ExitCode);
    }

    [Fact]
    public async Task Error_FileNotFound_PrintsPath()
    {
        var r = await CliRunner.RunAsync(
            cli.AnalyzerExe, ["--file", @"C:\no\such\file.sql"]);

        Assert.True(
            r.AllOutput.Contains("file.sql", StringComparison.OrdinalIgnoreCase) ||
            r.AllOutput.Contains("not found", StringComparison.OrdinalIgnoreCase),
            $"Expected path or 'not found' in output: {r.AllOutput}");
    }

    [Fact]
    public async Task Error_DirectoryNotFound_ExitsTwo()
    {
        var r = await CliRunner.RunAsync(
            cli.AnalyzerExe, ["--directory", @"C:\no\such\dir"]);

        Assert.Equal(2, r.ExitCode);
    }

    // ── Invalid --format ──────────────────────────────────────────────────────

    [Fact]
    public async Task Error_InvalidFormat_ExitsTwo()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("q.sql", SqlSamples.Clean);

        var r = await CliRunner.RunAsync(
            cli.AnalyzerExe, ["--file", file.FullName, "--format", "xml"]);

        Assert.Equal(2, r.ExitCode);
    }

    [Fact]
    public async Task Error_InvalidFormat_PrintsAllowedValues()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("q.sql", SqlSamples.Clean);

        var r = await CliRunner.RunAsync(
            cli.AnalyzerExe, ["--file", file.FullName, "--format", "xml"]);

        Assert.True(
            r.AllOutput.Contains("text", StringComparison.OrdinalIgnoreCase) ||
            r.AllOutput.Contains("json", StringComparison.OrdinalIgnoreCase),
            $"Expected 'text' or 'json' in error output: {r.AllOutput}");
    }

    // ── Invalid --severity ────────────────────────────────────────────────────

    [Fact]
    public async Task Error_InvalidSeverity_ExitsTwo()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("q.sql", SqlSamples.Clean);

        var r = await CliRunner.RunAsync(
            cli.AnalyzerExe, ["--file", file.FullName, "--severity", "critical"]);

        Assert.Equal(2, r.ExitCode);
    }

    [Fact]
    public async Task Error_InvalidSeverity_PrintsAllowedValues()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("q.sql", SqlSamples.Clean);

        var r = await CliRunner.RunAsync(
            cli.AnalyzerExe, ["--file", file.FullName, "--severity", "critical"]);

        Assert.True(
            r.AllOutput.Contains("error",       StringComparison.OrdinalIgnoreCase) ||
            r.AllOutput.Contains("warning",     StringComparison.OrdinalIgnoreCase) ||
            r.AllOutput.Contains("information", StringComparison.OrdinalIgnoreCase),
            $"Expected severity options in output: {r.AllOutput}");
    }

    // ── Settings file not found ───────────────────────────────────────────────

    [Fact]
    public async Task Error_SettingsFileNotFound_ExitsTwo()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("q.sql", SqlSamples.Clean);

        var r = await CliRunner.RunAsync(
            cli.AnalyzerExe,
            ["--file", file.FullName, "--settings", @"C:\no\such\.casettings"]);

        Assert.Equal(2, r.ExitCode);
    }
}
