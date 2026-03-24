using AkmlSql.E2E.Tests.Infrastructure;
using Xunit;

namespace AkmlSql.E2E.Tests.Analyzer;

/// <summary>
/// Tests: inline suppression comments (-- akml-disable-line, -- akml-disable / enable blocks).
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
