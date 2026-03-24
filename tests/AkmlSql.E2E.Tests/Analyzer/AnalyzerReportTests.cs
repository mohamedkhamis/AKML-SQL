using System.Text.Json;
using AkmlSql.E2E.Tests.Infrastructure;
using Xunit;

namespace AkmlSql.E2E.Tests.Analyzer;

/// <summary>
/// Tests: --report flag writes a JSON file alongside stdout output.
/// </summary>
[Collection("Cli")]
public sealed class AnalyzerReportTests(CliFixture cli)
{
    [Fact]
    public async Task Report_CreatesFile()
    {
        using var tmp = new TempSqlDir();
        var file       = tmp.Write("bad.sql", SqlSamples.DeleteNoWhere);
        var reportPath = tmp.FilePath("report.json");

        var r = await CliRunner.RunAsync(
            cli.AnalyzerExe, ["--file", file.FullName, "--report", reportPath]);

        Assert.Equal(0, r.ExitCode);
        Assert.True(File.Exists(reportPath), $"Report not written to: {reportPath}");
    }

    [Fact]
    public async Task Report_ContainsValidJson()
    {
        using var tmp = new TempSqlDir();
        var file       = tmp.Write("bad.sql", SqlSamples.DeleteNoWhere);
        var reportPath = tmp.FilePath("report.json");

        await CliRunner.RunAsync(
            cli.AnalyzerExe, ["--file", file.FullName, "--report", reportPath]);

        var json = await File.ReadAllTextAsync(reportPath);
        using var doc = JsonDocument.Parse(json);
        Assert.NotNull(doc);
    }

    [Fact]
    public async Task Report_ContainsSummary()
    {
        using var tmp = new TempSqlDir();
        var file       = tmp.Write("bad.sql", SqlSamples.DeleteNoWhere);
        var reportPath = tmp.FilePath("report.json");

        await CliRunner.RunAsync(
            cli.AnalyzerExe, ["--file", file.FullName, "--report", reportPath]);

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
        Assert.True(doc.RootElement.TryGetProperty("summary", out _));
    }

    [Fact]
    public async Task Report_FilesAnalyzed_IsOne()
    {
        using var tmp = new TempSqlDir();
        var file       = tmp.Write("bad.sql", SqlSamples.DeleteNoWhere);
        var reportPath = tmp.FilePath("report.json");

        await CliRunner.RunAsync(
            cli.AnalyzerExe, ["--file", file.FullName, "--report", reportPath]);

        using var doc     = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
        var summary       = doc.RootElement.GetProperty("summary");
        var filesAnalyzed = summary.GetProperty("filesAnalyzed").GetInt32();
        Assert.Equal(1, filesAnalyzed);
    }

    [Fact]
    public async Task Report_JsonFormat_WritesStdoutAndReportFile()
    {
        using var tmp = new TempSqlDir();
        var file       = tmp.Write("bad.sql", SqlSamples.DeleteNoWhere);
        var reportPath = tmp.FilePath("report.json");

        var r = await CliRunner.RunAsync(
            cli.AnalyzerExe,
            ["--file", file.FullName, "--format", "json", "--report", reportPath]);

        // Both stdout (JSON) and report file should exist
        Assert.NotEmpty(r.Stdout);
        Assert.True(File.Exists(reportPath));
        // stdout should also be valid JSON
        using var stdoutDoc = JsonDocument.Parse(r.Stdout);
        Assert.NotNull(stdoutDoc);
    }

    [Fact]
    public async Task Report_IssueCountMatchesStdout()
    {
        using var tmp = new TempSqlDir();
        var file       = tmp.Write("bad.sql", SqlSamples.DeleteNoWhere);
        var reportPath = tmp.FilePath("report.json");

        var r = await CliRunner.RunAsync(
            cli.AnalyzerExe,
            ["--file", file.FullName, "--format", "json", "--report", reportPath]);

        using var stdoutDoc = JsonDocument.Parse(r.Stdout);
        using var fileDoc   = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));

        var stdoutCount = stdoutDoc.RootElement.GetProperty("summary")
                                               .GetProperty("totalIssues").GetInt32();
        var fileCount   = fileDoc.RootElement.GetProperty("summary")
                                             .GetProperty("totalIssues").GetInt32();

        Assert.Equal(stdoutCount, fileCount);
    }
}
