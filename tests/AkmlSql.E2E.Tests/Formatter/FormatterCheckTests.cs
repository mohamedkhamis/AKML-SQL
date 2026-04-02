using AkmlSql.E2E.Tests.Infrastructure;
using Xunit;

namespace AkmlSql.E2E.Tests.Formatter;

/// <summary>
/// Tests: --check mode (validation without file modification).
/// Exit 0 = already formatted.  Exit 1 = violations found.
/// </summary>
[Collection("Cli")]
public sealed class FormatterCheckTests(CliFixture cli)
{
    // ── Already formatted → exit 0 ────────────────────────────────────────────

    [Fact]
    public async Task Check_AlreadyFormatted_ExitsZero()
    {
        using var tmp = new TempSqlDir();
        // Get canonical form first
        var file = tmp.Write("q.sql", SqlSamples.Dirty);
        await CliRunner.RunAsync(cli.FormatterExe, [file.FullName]);
        var canonical = await File.ReadAllTextAsync(file.FullName);

        // Write canonical and run --check
        await File.WriteAllTextAsync(file.FullName, canonical);
        var r = await CliRunner.RunAsync(cli.FormatterExe, ["--check", file.FullName]);

        Assert.Equal(0, r.ExitCode);
    }

    [Fact]
    public async Task Check_AlreadyFormatted_DoesNotModifyFile()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("q.sql", SqlSamples.Dirty);
        await CliRunner.RunAsync(cli.FormatterExe, [file.FullName]);
        var canonical = await File.ReadAllTextAsync(file.FullName);

        await File.WriteAllTextAsync(file.FullName, canonical);
        await CliRunner.RunAsync(cli.FormatterExe, ["--check", file.FullName]);

        Assert.Equal(canonical, await File.ReadAllTextAsync(file.FullName));
    }

    [Fact]
    public async Task Check_AlreadyFormatted_ReportsAllFormatted()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("q.sql", SqlSamples.Dirty);
        await CliRunner.RunAsync(cli.FormatterExe, [file.FullName]);
        var canonical = await File.ReadAllTextAsync(file.FullName);

        await File.WriteAllTextAsync(file.FullName, canonical);
        var r = await CliRunner.RunAsync(cli.FormatterExe, ["--check", file.FullName]);

        Assert.Contains("formatted", r.AllOutput, StringComparison.OrdinalIgnoreCase);
    }

    // ── Needs formatting → exit 1 ─────────────────────────────────────────────

    [Fact]
    public async Task Check_NeedsFormatting_ExitsOne()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("q.sql", SqlSamples.Dirty);

        var r = await CliRunner.RunAsync(cli.FormatterExe, ["--check", file.FullName]);

        Assert.Equal(1, r.ExitCode);
    }

    [Fact]
    public async Task Check_NeedsFormatting_DoesNotModifyFile()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("q.sql", SqlSamples.Dirty);
        var original = SqlSamples.Dirty;

        await CliRunner.RunAsync(cli.FormatterExe, ["--check", file.FullName]);

        Assert.Equal(original, await File.ReadAllTextAsync(file.FullName));
    }

    [Fact]
    public async Task Check_Directory_AllFormatted_ExitsZero()
    {
        using var tmp = new TempSqlDir();
        // Format first, then check
        var fileA = tmp.Write("a.sql", SqlSamples.Dirty);
        var fileB = tmp.Write("b.sql", SqlSamples.Dirty);
        await CliRunner.RunAsync(cli.FormatterExe, ["-d", tmp.Path]);

        var r = await CliRunner.RunAsync(cli.FormatterExe, ["--check", "-d", tmp.Path]);

        Assert.Equal(0, r.ExitCode);
    }

    [Fact]
    public async Task Check_Directory_SomeViolations_ExitsOne()
    {
        using var tmp = new TempSqlDir();
        tmp.Write("a.sql", SqlSamples.Dirty); // needs formatting
        var fileB = tmp.Write("b.sql", SqlSamples.Dirty);
        // Format only b.sql
        await CliRunner.RunAsync(cli.FormatterExe, [fileB.FullName]);

        var r = await CliRunner.RunAsync(cli.FormatterExe, ["--check", "-d", tmp.Path]);

        Assert.Equal(1, r.ExitCode);
    }
}
