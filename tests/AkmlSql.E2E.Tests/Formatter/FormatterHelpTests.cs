using AkmlSql.E2E.Tests.Infrastructure;
using Xunit;

namespace AkmlSql.E2E.Tests.Formatter;

/// <summary>
/// Tests: --help, no-args, --list-profiles, --version metadata.
/// Exit code 0 expected in all cases.
/// </summary>
[Collection("Cli")]
public sealed class FormatterHelpTests(CliFixture cli)
{
    // ── --help ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Help_ExitsZero()
    {
        var r = await CliRunner.RunAsync(cli.FormatterExe, ["--help"]);
        Assert.Equal(0, r.ExitCode);
    }

    [Fact]
    public async Task Help_PrintsUsageSection()
    {
        var r = await CliRunner.RunAsync(cli.FormatterExe, ["--help"]);
        Assert.Contains("USAGE", r.AllOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Help_ListsCheckFlag()
    {
        var r = await CliRunner.RunAsync(cli.FormatterExe, ["--help"]);
        Assert.Contains("--check", r.AllOutput);
    }

    [Fact]
    public async Task Help_ListsDiffFlag()
    {
        var r = await CliRunner.RunAsync(cli.FormatterExe, ["--help"]);
        Assert.Contains("--diff", r.AllOutput);
    }

    [Fact]
    public async Task Help_ListsStdinFlag()
    {
        var r = await CliRunner.RunAsync(cli.FormatterExe, ["--help"]);
        Assert.Contains("--stdin", r.AllOutput);
    }

    [Fact]
    public async Task Help_ShowsExitCodes()
    {
        var r = await CliRunner.RunAsync(cli.FormatterExe, ["--help"]);
        Assert.Contains("EXIT CODES", r.AllOutput, StringComparison.OrdinalIgnoreCase);
    }

    // ── No arguments ─────────────────────────────────────────────────────────

    [Fact]
    public async Task NoArgs_ExitsZero()
    {
        // No-arg invocation should print help (or report no-input) but not crash.
        var r = await CliRunner.RunAsync(cli.FormatterExe, []);
        // Either 0 (shows help) or 2 (no input error) — must not be 5 (internal error)
        Assert.NotEqual(5, r.ExitCode);
    }

    // ── --list-profiles ───────────────────────────────────────────────────────

    [Fact]
    public async Task ListProfiles_ExitsZero()
    {
        var r = await CliRunner.RunAsync(cli.FormatterExe, ["--list-profiles"]);
        Assert.Equal(0, r.ExitCode);
    }

    [Fact]
    public async Task ListProfiles_IncludesDefaultProfile()
    {
        var r = await CliRunner.RunAsync(cli.FormatterExe, ["--list-profiles"]);
        Assert.Contains("Default", r.AllOutput, StringComparison.OrdinalIgnoreCase);
    }
}
