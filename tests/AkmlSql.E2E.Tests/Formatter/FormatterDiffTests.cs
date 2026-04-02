using AkmlSql.E2E.Tests.Infrastructure;
using Xunit;

namespace AkmlSql.E2E.Tests.Formatter;

/// <summary>
/// Tests: --diff mode. Shows a unified diff without modifying files.
/// Exit 0 = no changes needed.  Exit 1 = diff output produced.
/// </summary>
[Collection("Cli")]
public sealed class FormatterDiffTests(CliFixture cli)
{
    [Fact]
    public async Task Diff_NeedsFormatting_ExitsOne()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("q.sql", SqlSamples.Dirty);

        var r = await CliRunner.RunAsync(cli.FormatterExe, ["--diff", file.FullName]);

        Assert.Equal(1, r.ExitCode);
    }

    [Fact]
    public async Task Diff_NeedsFormatting_ProducesDiffMarkers()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("q.sql", SqlSamples.Dirty);

        var r = await CliRunner.RunAsync(cli.FormatterExe, ["--diff", file.FullName]);

        // Unified diff must contain "---" and "+++" headers
        Assert.Contains("---", r.AllOutput);
        Assert.Contains("+++", r.AllOutput);
    }

    [Fact]
    public async Task Diff_NeedsFormatting_DoesNotModifyFile()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("q.sql", SqlSamples.Dirty);
        var original = SqlSamples.Dirty;

        await CliRunner.RunAsync(cli.FormatterExe, ["--diff", file.FullName]);

        Assert.Equal(original, await File.ReadAllTextAsync(file.FullName));
    }

    [Fact]
    public async Task Diff_AlreadyFormatted_ExitsZero()
    {
        using var tmp = new TempSqlDir();
        // Format to get canonical form, then run diff
        var file = tmp.Write("q.sql", SqlSamples.Dirty);
        await CliRunner.RunAsync(cli.FormatterExe, [file.FullName]);
        var canonical = await File.ReadAllTextAsync(file.FullName);

        await File.WriteAllTextAsync(file.FullName, canonical);
        var r = await CliRunner.RunAsync(cli.FormatterExe, ["--diff", file.FullName]);

        Assert.Equal(0, r.ExitCode);
    }

    [Fact]
    public async Task Diff_AlreadyFormatted_ProducesNoDiffOutput()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("q.sql", SqlSamples.Dirty);
        await CliRunner.RunAsync(cli.FormatterExe, [file.FullName]);
        var canonical = await File.ReadAllTextAsync(file.FullName);

        await File.WriteAllTextAsync(file.FullName, canonical);
        var r = await CliRunner.RunAsync(cli.FormatterExe, ["--diff", file.FullName]);

        // No diff markers when already formatted
        Assert.DoesNotContain("---", r.AllOutput);
        Assert.DoesNotContain("+++", r.AllOutput);
    }
}
