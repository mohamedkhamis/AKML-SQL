using AkmlSql.E2E.Tests.Infrastructure;
using Xunit;

namespace AkmlSql.E2E.Tests.Analyzer;

/// <summary>
/// Tests: basic analysis of known-bad and clean SQL, directory scans,
/// text-format output, and rule detection.
/// </summary>
[Collection("Cli")]
public sealed class AnalyzerAnalyzeTests(CliFixture cli)
{
    // ── Clean SQL ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Analyze_CleanFile_ExitsZero()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("clean.sql", SqlSamples.Clean);

        var r = await CliRunner.RunAsync(cli.AnalyzerExe, ["--file", file.FullName]);

        Assert.Equal(0, r.ExitCode);
    }

    [Fact]
    public async Task Analyze_CleanFile_ReportsNoIssues()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("clean.sql", SqlSamples.Clean);

        var r = await CliRunner.RunAsync(cli.AnalyzerExe, ["--file", file.FullName]);

        Assert.Contains("No issues", r.Stdout, StringComparison.OrdinalIgnoreCase);
    }

    // ── Rule detection ────────────────────────────────────────────────────────

    [Fact]
    public async Task Analyze_DeleteNoWhere_DetectsPE003()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("bad.sql", SqlSamples.DeleteNoWhere);

        var r = await CliRunner.RunAsync(cli.AnalyzerExe, ["--file", file.FullName]);

        Assert.Equal(0, r.ExitCode); // exit 0 without --check
        Assert.Contains("PE003", r.Stdout);
    }

    [Fact]
    public async Task Analyze_HardcodedPassword_DetectsSE002()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("bad.sql", SqlSamples.HardcodedPassword);

        var r = await CliRunner.RunAsync(cli.AnalyzerExe, ["--file", file.FullName]);

        Assert.Contains("SE002", r.Stdout);
    }

    [Fact]
    public async Task Analyze_EqualsNull_DetectsBP004()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("bad.sql", SqlSamples.EqualsNull);

        var r = await CliRunner.RunAsync(cli.AnalyzerExe, ["--file", file.FullName]);

        Assert.Contains("BP004", r.Stdout);
    }

    [Fact]
    public async Task Analyze_DeprecatedType_DetectsDEP001()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("bad.sql", SqlSamples.DeprecatedTextType);

        var r = await CliRunner.RunAsync(cli.AnalyzerExe, ["--file", file.FullName]);

        Assert.Contains("DEP001", r.Stdout);
    }

    [Fact]
    public async Task Analyze_DivisionByZero_DetectsEX001()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("bad.sql", SqlSamples.DivisionByZero);

        var r = await CliRunner.RunAsync(cli.AnalyzerExe, ["--file", file.FullName]);

        Assert.Contains("EX001", r.Stdout);
    }

    [Fact]
    public async Task Analyze_MultiIssueFile_DetectsMultipleRules()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("multi.sql", SqlSamples.MultiIssue);

        var r = await CliRunner.RunAsync(cli.AnalyzerExe, ["--file", file.FullName]);

        Assert.Contains("PE003", r.Stdout);
        Assert.Contains("BP004", r.Stdout);
    }

    // ── Text output format ────────────────────────────────────────────────────

    [Fact]
    public async Task Analyze_TextFormat_IncludesLineAndColumn()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("bad.sql", SqlSamples.DeleteNoWhere);

        var r = await CliRunner.RunAsync(cli.AnalyzerExe, ["--file", file.FullName]);

        // Text format: "file.sql(line,col): severity  RULE - message"
        Assert.Matches(@"\(\d+,\d+\)", r.Stdout);
    }

    [Fact]
    public async Task Analyze_TextFormat_IncludesSeverityLabel()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("bad.sql", SqlSamples.HardcodedPassword);

        var r = await CliRunner.RunAsync(cli.AnalyzerExe, ["--file", file.FullName]);

        // SE002 is error severity
        Assert.Contains("error", r.Stdout, StringComparison.OrdinalIgnoreCase);
    }

    // ── Directory scan ────────────────────────────────────────────────────────

    [Fact]
    public async Task Analyze_Directory_ScansAllSqlFiles()
    {
        using var tmp = new TempSqlDir();
        tmp.Write("a.sql", SqlSamples.DeleteNoWhere);
        tmp.Write("b.sql", SqlSamples.HardcodedPassword);

        var r = await CliRunner.RunAsync(cli.AnalyzerExe, ["--directory", tmp.Path]);

        Assert.Equal(0, r.ExitCode);
        Assert.Contains("PE003", r.Stdout);
        Assert.Contains("SE002", r.Stdout);
    }

    [Fact]
    public async Task Analyze_Directory_Recursive_IncludesSubdirs()
    {
        using var tmp = new TempSqlDir();
        var sub = tmp.SubDir("sub");
        File.WriteAllText(Path.Combine(sub, "nested.sql"), SqlSamples.DeleteNoWhere);

        var r = await CliRunner.RunAsync(
            cli.AnalyzerExe, ["--directory", tmp.Path, "--recursive"]);

        Assert.Contains("PE003", r.Stdout);
    }

    [Fact]
    public async Task Analyze_Directory_NonRecursive_IgnoresSubdirs()
    {
        using var tmp = new TempSqlDir();
        // Only put a file in a subdirectory
        var sub = tmp.SubDir("sub");
        File.WriteAllText(Path.Combine(sub, "nested.sql"), SqlSamples.DeleteNoWhere);

        var r = await CliRunner.RunAsync(
            cli.AnalyzerExe, ["--directory", tmp.Path]); // no --recursive

        // The nested file should NOT be scanned
        Assert.DoesNotContain("PE003", r.Stdout);
    }

    // ── Summary line ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Analyze_PrintsSummaryLine()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("bad.sql", SqlSamples.DeleteNoWhere);

        var r = await CliRunner.RunAsync(cli.AnalyzerExe, ["--file", file.FullName]);

        Assert.Contains("Summary", r.Stdout, StringComparison.OrdinalIgnoreCase);
    }
}
