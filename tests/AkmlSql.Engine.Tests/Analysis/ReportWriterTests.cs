using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AkmlSql.Core.Models.Analysis;
using AkmlSql.Engine.Analysis;
using AkmlSql.Engine.Tests.Analysis;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis;

public sealed class ReportWriterTests
{
    private static AnalysisDiagnostic MakeDiag(string ruleId, DiagnosticSeverity sev, string categoryCode, int line = 1) =>
        new()
        {
            RuleId       = ruleId,
            CategoryCode = categoryCode,
            Severity     = sev,
            Message      = $"Test message for {ruleId}",
            StartOffset  = 0,
            EndOffset    = 10,
            Line         = line,
            Column       = 1,
            FixActions   = []
        };

    private static AkmlSql.Analyzer.AnalyzerOptions MakeOpts(string format = "text") =>
        AkmlSql.Analyzer.AnalyzerOptions.Parse(new[]
            { "--file", "dummy.sql", "--format", format });

    [Fact]
    public void TextOutput_ContainsFileLineAndRule()
    {
        var results = new List<(string, IReadOnlyList<AnalysisDiagnostic>)>
        {
            ("orders.sql", new[] { MakeDiag("PE001", DiagnosticSeverity.Warning, "PE", 15) })
        };

        var writer = new AkmlSql.Analyzer.ReportWriter(MakeOpts("text"));

        var stdout = CaptureStdout(() => writer.WriteReport(results, "orders.sql"));

        Assert.Contains("PE001", stdout);
        Assert.Contains("orders.sql", stdout);
        Assert.Contains("15", stdout); // line number
    }

    [Fact]
    public void TextOutput_SummaryLine_IsCorrect()
    {
        var results = new List<(string, IReadOnlyList<AnalysisDiagnostic>)>
        {
            ("f.sql", new[]
            {
                MakeDiag("PE003", DiagnosticSeverity.Error,   "PE"),
                MakeDiag("PE001", DiagnosticSeverity.Warning, "PE"),
            })
        };

        var writer = new AkmlSql.Analyzer.ReportWriter(MakeOpts("text"));
        var stdout = CaptureStdout(() => writer.WriteReport(results, "f.sql"));

        Assert.Contains("2 issue", stdout);
        Assert.Contains("1 error", stdout);
        Assert.Contains("1 warning", stdout);
    }

    [Fact]
    public void JsonReport_WrittenToFile()
    {
        var tmpFile = Path.GetTempFileName() + ".json";
        try
        {
            var results = new List<(string, IReadOnlyList<AnalysisDiagnostic>)>
            {
                ("q.sql", new[] { MakeDiag("BP004", DiagnosticSeverity.Error, "BP", 7) })
            };

            var opts   = AkmlSql.Analyzer.AnalyzerOptions.Parse(new[] { "--file", "q.sql", "--report", tmpFile });
            var writer = new AkmlSql.Analyzer.ReportWriter(opts);
            CaptureStdout(() => writer.WriteReport(results, "q.sql"));

            Assert.True(File.Exists(tmpFile));
            var json = File.ReadAllText(tmpFile);
            using var doc = JsonDocument.Parse(json);

            var root = doc.RootElement;
            Assert.Equal(1, root.GetProperty("summary").GetProperty("totalIssues").GetInt32());
            Assert.Equal(1, root.GetProperty("summary").GetProperty("errors").GetInt32());
            Assert.Equal("BP004", root.GetProperty("issues")[0].GetProperty("rule").GetString());
        }
        finally
        {
            if (File.Exists(tmpFile)) File.Delete(tmpFile);
        }
    }

    [Fact]
    public void JsonReport_ByCategoryCount_IsCorrect()
    {
        var tmpFile = Path.GetTempFileName() + ".json";
        try
        {
            var results = new List<(string, IReadOnlyList<AnalysisDiagnostic>)>
            {
                ("a.sql", new[]
                {
                    MakeDiag("PE001", DiagnosticSeverity.Warning, "PE"),
                    MakeDiag("PE003", DiagnosticSeverity.Error,   "PE"),
                    MakeDiag("BP004", DiagnosticSeverity.Error,   "BP"),
                })
            };

            var opts   = AkmlSql.Analyzer.AnalyzerOptions.Parse(new[] { "--file", "a.sql", "--report", tmpFile });
            var writer = new AkmlSql.Analyzer.ReportWriter(opts);
            CaptureStdout(() => writer.WriteReport(results, "a.sql"));

            var json = File.ReadAllText(tmpFile);
            using var doc = JsonDocument.Parse(json);
            var byCategory = doc.RootElement.GetProperty("byCategory");

            Assert.Equal(2, byCategory.GetProperty("PE").GetInt32());
            Assert.Equal(1, byCategory.GetProperty("BP").GetInt32());
        }
        finally
        {
            if (File.Exists(tmpFile)) File.Delete(tmpFile);
        }
    }

    [Fact]
    public void TextOutput_EmptyResult_PrintsNoIssues()
    {
        var results = new List<(string, IReadOnlyList<AnalysisDiagnostic>)>
        {
            ("clean.sql", Array.Empty<AnalysisDiagnostic>())
        };

        var writer = new AkmlSql.Analyzer.ReportWriter(MakeOpts("text"));
        var stdout = CaptureStdout(() => writer.WriteReport(results, "clean.sql"));

        Assert.Contains("No issues", stdout);
    }

    private static string CaptureStdout(Action action)
    {
        var orig = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);
        try { action(); }
        finally { Console.SetOut(orig); }
        return sw.ToString();
    }
}
