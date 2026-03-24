using AkmlSql.E2E.Tests.Infrastructure;
using Xunit;

namespace AkmlSql.E2E.Tests.Formatter;

/// <summary>
/// Tests: --stdin / --stdout pipe mode.
/// SQL is piped in via stdin; formatted SQL is written to stdout.
/// </summary>
[Collection("Cli")]
public sealed class FormatterStdinTests(CliFixture cli)
{
    [Fact]
    public async Task Stdin_Stdout_ExitsZero()
    {
        var r = await CliRunner.RunAsync(
            cli.FormatterExe, ["--stdin", "--stdout"],
            stdinText: SqlSamples.Dirty);

        Assert.Equal(0, r.ExitCode);
    }

    [Fact]
    public async Task Stdin_Stdout_OutputContainsUppercaseKeywords()
    {
        var r = await CliRunner.RunAsync(
            cli.FormatterExe, ["--stdin", "--stdout"],
            stdinText: SqlSamples.Dirty);

        Assert.Contains("SELECT", r.Stdout);
        Assert.Contains("FROM",   r.Stdout);
    }

    [Fact]
    public async Task Stdin_Check_DirtyInput_ExitsOne()
    {
        var r = await CliRunner.RunAsync(
            cli.FormatterExe, ["--stdin", "--check"],
            stdinText: SqlSamples.Dirty);

        Assert.Equal(1, r.ExitCode);
    }

    [Fact]
    public async Task Stdin_Check_AlreadyFormatted_ExitsZero()
    {
        // First get canonical form
        using var tmp = new TempSqlDir();
        var file = tmp.Write("q.sql", SqlSamples.Dirty);
        await CliRunner.RunAsync(cli.FormatterExe, [file.FullName]);
        var canonical = await File.ReadAllTextAsync(file.FullName);

        var r = await CliRunner.RunAsync(
            cli.FormatterExe, ["--stdin", "--check"],
            stdinText: canonical);

        Assert.Equal(0, r.ExitCode);
    }

    [Fact]
    public async Task Stdin_EmptyInput_ExitsZeroOrDoesNotCrash()
    {
        var r = await CliRunner.RunAsync(
            cli.FormatterExe, ["--stdin", "--stdout"],
            stdinText: "");

        // Must not be exit 5 (internal error)
        Assert.NotEqual(5, r.ExitCode);
    }

    [Fact]
    public async Task Stdin_Diff_DirtyInput_ExitsOne()
    {
        var r = await CliRunner.RunAsync(
            cli.FormatterExe, ["--stdin", "--diff"],
            stdinText: SqlSamples.Dirty);

        Assert.Equal(1, r.ExitCode);
    }
}
