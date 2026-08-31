using AkmlSql.E2E.Tests.Infrastructure;
using Xunit;

namespace AkmlSql.E2E.Tests.Analyzer;

/// <summary>
/// Tests: inline suppression comments, through the shipped analyzer executable — both the
/// documented <c>-- akml-disable</c> family and the original <c>-- noqa</c> forms, which must keep
/// working for scripts written before the documented syntax was implemented.
/// </summary>
[Collection("Cli")]
public sealed class AnalyzerSuppressionTests(CliFixture cli)
{
    // ── Single-line suppression ───────────────────────────────────────────────

    [Fact]
    public async Task Suppress_DisableLine_HidesDiagnostic()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("suppressed.sql", SqlSamples.DeleteSuppressed);

        var r = await CliRunner.RunAsync(cli.AnalyzerExe, ["--file", file.FullName]);

        // PE003 must NOT appear in output
        Assert.DoesNotContain("PE003", r.Stdout);
    }

    [Fact]
    public async Task Suppress_DisableLine_Check_ExitsZero()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("suppressed.sql", SqlSamples.DeleteSuppressed);

        // With all violations suppressed, --check should exit 0
        var r = await CliRunner.RunAsync(
            cli.AnalyzerExe, ["--file", file.FullName, "--check"]);

        Assert.Equal(0, r.ExitCode);
    }

    // ── Block suppression (disable / enable) ─────────────────────────────────

    [Fact]
    public async Task Suppress_DisableEnableBlock_HidesDiagnostic()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("block.sql", SqlSamples.DeleteBlockSuppressed);

        var r = await CliRunner.RunAsync(cli.AnalyzerExe, ["--file", file.FullName]);

        Assert.DoesNotContain("PE003", r.Stdout);
    }

    [Fact]
    public async Task Suppress_DisableEnableBlock_Check_ExitsZero()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("block.sql", SqlSamples.DeleteBlockSuppressed);

        var r = await CliRunner.RunAsync(
            cli.AnalyzerExe, ["--file", file.FullName, "--check"]);

        Assert.Equal(0, r.ExitCode);
    }

    // ── Without suppression, violation is present ─────────────────────────────

    [Fact]
    public async Task NoSuppression_DeleteWithoutWhere_ShowsPE003()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("bad.sql", SqlSamples.DeleteNoWhere);

        var r = await CliRunner.RunAsync(cli.AnalyzerExe, ["--file", file.FullName]);

        Assert.Contains("PE003", r.Stdout);
    }

    // ── The documented akml-disable family ────────────────────────────────────

    [Fact]
    public async Task Suppress_AkmlDisableLine_HidesDiagnostic()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("akml-line.sql", SqlSamples.DeleteSuppressedAkmlLine);

        var r = await CliRunner.RunAsync(cli.AnalyzerExe, ["--file", file.FullName]);

        Assert.DoesNotContain("PE003", r.Stdout);
    }

    [Fact]
    public async Task Suppress_AkmlDisableEnableBlock_HidesDiagnostic()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("akml-block.sql", SqlSamples.DeleteBlockSuppressedAkml);

        var r = await CliRunner.RunAsync(cli.AnalyzerExe, ["--file", file.FullName]);

        Assert.DoesNotContain("PE003", r.Stdout);
    }

    [Fact]
    public async Task Suppress_AkmlDisableWithoutEnable_CoversTheWholeScript()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("akml-script.sql", SqlSamples.DeleteScriptSuppressedAkml);

        var r = await CliRunner.RunAsync(cli.AnalyzerExe, ["--file", file.FullName]);

        // Both DELETEs are covered, and the unclosed directive is not itself reported.
        Assert.DoesNotContain("PE003", r.Stdout);
        Assert.DoesNotContain("NOQA001", r.Stdout);
    }

    [Fact]
    public async Task Suppress_AkmlDisableLine_Check_ExitsZero()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("akml-line.sql", SqlSamples.DeleteSuppressedAkmlLine);

        var r = await CliRunner.RunAsync(
            cli.AnalyzerExe, ["--file", file.FullName, "--check"]);

        Assert.Equal(0, r.ExitCode);
    }

    [Fact]
    public async Task Suppress_AkmlIsRuleScoped_OtherRulesStillReport()
    {
        using var tmp = new TempSqlDir();
        var sql = SqlSamples.DeleteNoWhere + " -- akml-disable-line PE003\n"
                  + SqlSamples.HardcodedPassword;
        var file = tmp.Write("akml-partial.sql", sql);

        var r = await CliRunner.RunAsync(cli.AnalyzerExe, ["--file", file.FullName]);

        Assert.DoesNotContain("PE003", r.Stdout);
        Assert.Contains("SE002", r.Stdout);
    }

    // ── Partial suppression (only one rule suppressed) ────────────────────────

    [Fact]
    public async Task PartialSuppression_OtherRulesStillReport()
    {
        using var tmp = new TempSqlDir();
        // Suppress PE003 but not SE002 — both present in the file
        var sql = SqlSamples.DeleteNoWhere + " -- noqa: PE003\n"
                  + SqlSamples.HardcodedPassword;
        var file = tmp.Write("partial.sql", sql);

        var r = await CliRunner.RunAsync(cli.AnalyzerExe, ["--file", file.FullName]);

        Assert.DoesNotContain("PE003", r.Stdout);
        Assert.Contains("SE002",  r.Stdout);
    }
}
