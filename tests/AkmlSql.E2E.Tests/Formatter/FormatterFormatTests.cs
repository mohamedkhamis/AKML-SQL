using AkmlSql.E2E.Tests.Infrastructure;
using Xunit;

namespace AkmlSql.E2E.Tests.Formatter;

/// <summary>
/// Tests: in-place file formatting (single file and directory).
/// </summary>
[Collection("Cli")]
public sealed class FormatterFormatTests(CliFixture cli)
{
    // ── Single file ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Format_SingleFile_ExitsZero()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("q.sql", SqlSamples.Dirty);

        var r = await CliRunner.RunAsync(cli.FormatterExe, [file.FullName]);

        Assert.Equal(0, r.ExitCode);
    }

    [Fact]
    public async Task Format_SingleFile_UppercasesKeywords()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("q.sql", SqlSamples.Dirty);

        await CliRunner.RunAsync(cli.FormatterExe, [file.FullName]);

        var content = await File.ReadAllTextAsync(file.FullName);
        Assert.Contains("SELECT", content);
        Assert.Contains("FROM",   content);
        Assert.Contains("WHERE",  content);
    }

    [Fact]
    public async Task Format_AlreadyFormatted_IsIdempotent()
    {
        using var tmp = new TempSqlDir();
        // Format once to get canonical form
        var file = tmp.Write("q.sql", SqlSamples.Dirty);
        await CliRunner.RunAsync(cli.FormatterExe, [file.FullName]);
        var canonical = await File.ReadAllTextAsync(file.FullName);

        // Write the canonical form and format again
        await File.WriteAllTextAsync(file.FullName, canonical);
        var r = await CliRunner.RunAsync(cli.FormatterExe, [file.FullName]);

        Assert.Equal(0, r.ExitCode);
        Assert.Equal(canonical, await File.ReadAllTextAsync(file.FullName));
    }

    [Fact]
    public async Task Format_MultiStatement_FormatsAllStatements()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("multi.sql", SqlSamples.MultiStatement);

        await CliRunner.RunAsync(cli.FormatterExe, [file.FullName]);

        var content = await File.ReadAllTextAsync(file.FullName);
        // All three SELECT keywords should appear uppercased
        Assert.Equal(3, content.Split(["SELECT"], StringSplitOptions.None).Length - 1);
    }

    // ── Directory ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Format_Directory_FormatsAllSqlFiles()
    {
        using var tmp = new TempSqlDir();
        tmp.Write("a.sql", SqlSamples.Dirty);
        tmp.Write("b.sql", SqlSamples.Dirty);
        tmp.Write("readme.txt", "not a sql file"); // should be ignored

        var r = await CliRunner.RunAsync(cli.FormatterExe, ["-d", tmp.Path]);

        Assert.Equal(0, r.ExitCode);
        Assert.Contains("SELECT", await File.ReadAllTextAsync(tmp.FilePath("a.sql")));
        Assert.Contains("SELECT", await File.ReadAllTextAsync(tmp.FilePath("b.sql")));
        Assert.Equal("not a sql file", await File.ReadAllTextAsync(tmp.FilePath("readme.txt")));
    }

    [Fact]
    public async Task Format_Directory_NonRecursive_IgnoresSubdirs()
    {
        using var tmp = new TempSqlDir();
        tmp.Write("top.sql", SqlSamples.Dirty);
        var sub = tmp.SubDir("sub");
        File.WriteAllText(Path.Combine(sub, "nested.sql"), SqlSamples.Dirty);

        var r = await CliRunner.RunAsync(cli.FormatterExe, ["-d", tmp.Path]);
        // Non-recursive: nested.sql should NOT be formatted
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("SELECT", await File.ReadAllTextAsync(tmp.FilePath("top.sql")));
        // nested.sql remains lowercase (untouched)
        var nested = await File.ReadAllTextAsync(Path.Combine(sub, "nested.sql"));
        Assert.DoesNotContain("SELECT", nested);
    }

    [Fact]
    public async Task Format_Directory_Recursive_IncludesSubdirs()
    {
        using var tmp = new TempSqlDir();
        var sub = tmp.SubDir("sub");
        File.WriteAllText(Path.Combine(sub, "nested.sql"), SqlSamples.Dirty);

        var r = await CliRunner.RunAsync(cli.FormatterExe, ["-d", tmp.Path, "-r"]);

        Assert.Equal(0, r.ExitCode);
        Assert.Contains("SELECT", await File.ReadAllTextAsync(Path.Combine(sub, "nested.sql")));
    }

    [Fact]
    public async Task Format_EmptyDirectory_ExitsZero()
    {
        using var tmp = new TempSqlDir();

        var r = await CliRunner.RunAsync(cli.FormatterExe, ["-d", tmp.Path]);

        Assert.Equal(0, r.ExitCode);
    }

    // ── Positional argument (no flag) ─────────────────────────────────────────

    [Fact]
    public async Task Format_PositionalFilePath_FormatsFile()
    {
        using var tmp = new TempSqlDir();
        var file = tmp.Write("q.sql", SqlSamples.Dirty);

        // Pass path as positional argument (no --file flag)
        var r = await CliRunner.RunAsync(cli.FormatterExe, [file.FullName]);

        Assert.Equal(0, r.ExitCode);
        Assert.Contains("SELECT", await File.ReadAllTextAsync(file.FullName));
    }
}
