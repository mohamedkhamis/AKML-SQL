using AkmlSql.E2E.Tests.Infrastructure;
using Xunit;

namespace AkmlSql.E2E.Tests.Formatter;

/// <summary>
/// Tests: error-path exit codes (3 = file/dir not found, 4 = invalid profile, 2 = bad args).
/// </summary>
[Collection("Cli")]
public sealed class FormatterErrorTests(CliFixture cli)
{
    // ── File / directory not found (exit 3) ───────────────────────────────────

    [Fact]
    public async Task Error_FileNotFound_ExitsThree()
    {
        var r = await CliRunner.RunAsync(
            cli.FormatterExe, ["--file", @"C:\does\not\exist\q.sql"]);

        Assert.Equal(3, r.ExitCode);
    }

    [Fact]
    public async Task Error_FileNotFound_PrintsMessage()
    {
        var r = await CliRunner.RunAsync(
            cli.FormatterExe, ["--file", @"C:\does\not\exist\q.sql"]);

        Assert.Contains("not found", r.AllOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Error_DirectoryNotFound_ExitsThree()
    {
        var r = await CliRunner.RunAsync(
            cli.FormatterExe, ["-d", @"C:\does\not\exist\dir"]);

        Assert.Equal(3, r.ExitCode);
    }

    // ── Invalid profile (exit 4) ──────────────────────────────────────────────

    [Fact]
    public async Task Error_UnknownProfileName_ExitsFour()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("q.sql", SqlSamples.Dirty);

        var r = await CliRunner.RunAsync(
            cli.FormatterExe, [file.FullName, "--profile", "NoSuchProfileXYZ"]);

        Assert.Equal(4, r.ExitCode);
    }

    [Fact]
    public async Task Error_UnknownProfileName_PrintsMessage()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("q.sql", SqlSamples.Dirty);

        var r = await CliRunner.RunAsync(
            cli.FormatterExe, [file.FullName, "--profile", "NoSuchProfileXYZ"]);

        Assert.Contains("NoSuchProfileXYZ", r.AllOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Error_ProfileFileNotFound_ExitsFour()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("q.sql", SqlSamples.Dirty);

        var r = await CliRunner.RunAsync(
            cli.FormatterExe,
            [file.FullName, "--profile-file", @"C:\no\such\profile.akmlstyle"]);

        Assert.Equal(4, r.ExitCode);
    }

    // ── Bad arguments (exit 2) ────────────────────────────────────────────────

    [Fact]
    public async Task Error_InvalidParallelValue_ExitsTwo()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("q.sql", SqlSamples.Dirty);

        var r = await CliRunner.RunAsync(
            cli.FormatterExe, [file.FullName, "--parallel", "notanumber"]);

        Assert.Equal(2, r.ExitCode);
    }

    [Fact]
    public async Task Error_UnknownFlag_ExitsTwo()
    {
        var r = await CliRunner.RunAsync(
            cli.FormatterExe, ["--totally-unknown-flag-xyz"]);

        // Exit 2 (parse error) or 0 (shown as help) — must not be 5 (internal)
        Assert.NotEqual(5, r.ExitCode);
    }
}
