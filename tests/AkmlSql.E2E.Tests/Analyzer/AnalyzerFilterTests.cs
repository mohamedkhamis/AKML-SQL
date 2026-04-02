using AkmlSql.E2E.Tests.Infrastructure;
using Xunit;

namespace AkmlSql.E2E.Tests.Analyzer;

/// <summary>
/// Tests: --check exit codes, --severity, --rules, --exclude-rules, --format json.
/// </summary>
[Collection("Cli")]
public sealed class AnalyzerFilterTests(CliFixture cli)
{
    // ── --check ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Check_WithViolations_ExitsOne()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("bad.sql", SqlSamples.DeleteNoWhere);

        var r = await CliRunner.RunAsync(
            cli.AnalyzerExe, ["--file", file.FullName, "--check"]);

        Assert.Equal(1, r.ExitCode);
    }

    [Fact]
    public async Task Check_NoViolations_ExitsZero()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("clean.sql", SqlSamples.Clean);

        var r = await CliRunner.RunAsync(
            cli.AnalyzerExe, ["--file", file.FullName, "--check"]);

        Assert.Equal(0, r.ExitCode);
    }

    // ── --severity ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Severity_Error_FiltersOutWarnings()
    {
        using var tmp = new TempSqlDir();
        // NM002 (sp_ prefix) is a warning; SE002 is an error.
        // With --severity error, only SE002 should appear; NM002 should be filtered out.
        var file = tmp.Write("both.sql",
            SqlSamples.SpPrefix + "\n" + SqlSamples.HardcodedPassword);

        var r = await CliRunner.RunAsync(
            cli.AnalyzerExe, ["--file", file.FullName, "--severity", "error"]);

        Assert.Contains("SE002", r.Stdout);
        Assert.DoesNotContain("NM002", r.Stdout);
    }

    [Fact]
    public async Task Severity_Warning_IncludesWarningsAndErrors()
    {
        using var tmp = new TempSqlDir();
        // NM002 (sp_ prefix) is a warning; SE002 is an error.
        // With --severity warning, both should appear.
        var file = tmp.Write("both.sql",
            SqlSamples.SpPrefix + "\n" + SqlSamples.HardcodedPassword);

        var r = await CliRunner.RunAsync(
            cli.AnalyzerExe, ["--file", file.FullName, "--severity", "warning"]);

        Assert.Contains("SE002", r.Stdout);
        Assert.Contains("NM002", r.Stdout);
    }

    [Fact]
    public async Task Severity_Error_Check_ExitsZeroWhenNoErrors()
    {
        using var tmp = new TempSqlDir();
        // NM002 (sp_ prefix) is a warning — with --severity error it should not count as a violation
        var file = tmp.Write("warn.sql", SqlSamples.SpPrefix);

        var r = await CliRunner.RunAsync(
            cli.AnalyzerExe, ["--file", file.FullName, "--severity", "error", "--check"]);

        // NM002 is warning, not error → --check with --severity error = exit 0
        Assert.Equal(0, r.ExitCode);
    }

    // ── --rules ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rules_OnlySpecified_RunsOnlyThatRule()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("both.sql",
            SqlSamples.DeleteNoWhere + "\n" + SqlSamples.HardcodedPassword);

        var r = await CliRunner.RunAsync(
            cli.AnalyzerExe, ["--file", file.FullName, "--rules", "PE003"]);

        Assert.Contains("PE003", r.Stdout);
        Assert.DoesNotContain("SE002", r.Stdout);
    }

    // ── --exclude-rules ───────────────────────────────────────────────────────

    [Fact]
    public async Task ExcludeRules_ExcludesSpecifiedRule()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("both.sql",
            SqlSamples.DeleteNoWhere + "\n" + SqlSamples.HardcodedPassword);

        var r = await CliRunner.RunAsync(
            cli.AnalyzerExe, ["--file", file.FullName, "--exclude-rules", "PE003"]);

        Assert.DoesNotContain("PE003", r.Stdout);
        Assert.Contains("SE002", r.Stdout);
    }

    // ── --format json ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Format_Json_ProducesValidJson()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("bad.sql", SqlSamples.DeleteNoWhere);

        var r = await CliRunner.RunAsync(
            cli.AnalyzerExe, ["--file", file.FullName, "--format", "json"]);

        Assert.Equal(0, r.ExitCode);
        // Should not throw
        using var doc = System.Text.Json.JsonDocument.Parse(r.Stdout);
        Assert.NotNull(doc);
    }

    [Fact]
    public async Task Format_Json_ContainsSummaryObject()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("bad.sql", SqlSamples.DeleteNoWhere);

        var r = await CliRunner.RunAsync(
            cli.AnalyzerExe, ["--file", file.FullName, "--format", "json"]);

        using var doc = System.Text.Json.JsonDocument.Parse(r.Stdout);
        Assert.True(doc.RootElement.TryGetProperty("summary", out _));
    }

    [Fact]
    public async Task Format_Json_ContainsIssuesArray()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("bad.sql", SqlSamples.DeleteNoWhere);

        var r = await CliRunner.RunAsync(
            cli.AnalyzerExe, ["--file", file.FullName, "--format", "json"]);

        using var doc = System.Text.Json.JsonDocument.Parse(r.Stdout);
        Assert.True(doc.RootElement.TryGetProperty("issues", out var issues));
        Assert.Equal(System.Text.Json.JsonValueKind.Array, issues.ValueKind);
        Assert.True(issues.GetArrayLength() > 0);
    }

    [Fact]
    public async Task Format_Json_IssueHasExpectedFields()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("bad.sql", SqlSamples.DeleteNoWhere);

        var r = await CliRunner.RunAsync(
            cli.AnalyzerExe, ["--file", file.FullName, "--format", "json"]);

        using var doc    = System.Text.Json.JsonDocument.Parse(r.Stdout);
        var firstIssue = doc.RootElement.GetProperty("issues")[0];

        Assert.True(firstIssue.TryGetProperty("rule",     out _));
        Assert.True(firstIssue.TryGetProperty("severity", out _));
        Assert.True(firstIssue.TryGetProperty("message",  out _));
        Assert.True(firstIssue.TryGetProperty("line",     out _));
    }
}
