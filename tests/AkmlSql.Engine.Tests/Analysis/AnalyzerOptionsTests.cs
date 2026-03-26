using System;
using AkmlSql.Core.Models.Analysis;
using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis;

/// <summary>Tests for the CLI AnalyzerOptions argument parser.</summary>
public sealed class AnalyzerOptionsTests
{
    [Fact]
    public void ParseFile_SetsFilePath()
    {
        var opts = ParseOptions("--file", "test.sql");
        Assert.Equal("test.sql", opts.File);
    }

    [Fact]
    public void ParseDirectory_SetsDirectory()
    {
        var opts = ParseOptions("--directory", "scripts/");
        Assert.Equal("scripts/", opts.Directory);
    }

    [Fact]
    public void ParseRecursive_SetsFlag()
    {
        var opts = ParseOptions("--directory", ".", "--recursive");
        Assert.True(opts.Recursive);
    }

    [Fact]
    public void ParseCheck_SetsFlag()
    {
        var opts = ParseOptions("--file", "x.sql", "--check");
        Assert.True(opts.Check);
    }

    [Fact]
    public void ParseSeverityError_Maps()
    {
        var opts = ParseOptions("--file", "x.sql", "--severity", "error");
        Assert.Equal(DiagnosticSeverity.Error, opts.MinSeverity);
    }

    [Fact]
    public void ParseSeverityWarning_Maps()
    {
        var opts = ParseOptions("--file", "x.sql", "--severity", "warning");
        Assert.Equal(DiagnosticSeverity.Warning, opts.MinSeverity);
    }

    [Fact]
    public void ParseFormat_Json()
    {
        var opts = ParseOptions("--file", "x.sql", "--format", "json");
        Assert.Equal("json", opts.Format);
    }

    [Fact]
    public void ParseRules_SplitsOnComma()
    {
        var opts = ParseOptions("--file", "x.sql", "--rules", "PE001,BP004");
        Assert.NotNull(opts.OnlyRules);
        Assert.Contains("PE001", opts.OnlyRules);
        Assert.Contains("BP004", opts.OnlyRules);
    }

    [Fact]
    public void ParseExcludeRules_SplitsOnComma()
    {
        var opts = ParseOptions("--file", "x.sql", "--exclude-rules", "PE002");
        Assert.NotNull(opts.ExcludeRules);
        Assert.Contains("PE002", opts.ExcludeRules);
    }

    [Fact]
    public void ParseHelp_ReturnsEarly()
    {
        var opts = ParseOptions("--help");
        Assert.True(opts.Help);
    }

    [Fact]
    public void ParseVersion_ReturnsEarly()
    {
        var opts = ParseOptions("--version");
        Assert.True(opts.Version);
    }

    [Fact]
    public void InvalidArgument_Throws()
    {
        Assert.Throws<ArgumentException>(() => ParseOptions("--unknown-flag"));
    }

    [Fact]
    public void MissingValueForFlag_Throws()
    {
        Assert.Throws<ArgumentException>(() => ParseOptions("--file"));
    }

    [Fact]
    public void InvalidSeverityValue_Throws()
    {
        Assert.Throws<ArgumentException>(() => ParseOptions("--file", "x.sql", "--severity", "critical"));
    }

    [Fact]
    public void InvalidFormatValue_Throws()
    {
        Assert.Throws<ArgumentException>(() => ParseOptions("--file", "x.sql", "--format", "xml"));
    }

    [Fact]
    public void ValidateRequiresFileOrDirectory()
    {
        var opts = ParseOptions("--check");
        Assert.False(opts.TryValidate(out var error));
        Assert.Contains("--file or --directory", error);
    }

    // helper
    private static AkmlSql.Analyzer.AnalyzerOptions ParseOptions(params string[] args) =>
        AkmlSql.Analyzer.AnalyzerOptions.Parse(args);
}
