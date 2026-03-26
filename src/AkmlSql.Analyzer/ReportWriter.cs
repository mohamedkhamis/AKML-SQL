using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using AkmlSql.Core.Models.Analysis;
using AkmlSql.Engine.Analysis;

namespace AkmlSql.Analyzer;

/// <summary>
/// Writes analysis results to stdout (text/JSON) and optionally to a JSON report file.
/// </summary>
internal sealed class ReportWriter
{
    private readonly AnalyzerOptions _opts;

    public ReportWriter(AnalyzerOptions opts) => _opts = opts;

    /// <summary>
    /// Writes output for all analyzed files and returns whether any violations at min severity exist.
    /// </summary>
    public bool WriteReport(
        IReadOnlyList<(string FilePath, IReadOnlyList<AnalysisDiagnostic> Diagnostics)> results,
        string? inputLabel)
    {
        // Flatten issues at or above min severity
        var allIssues = results
            .SelectMany(r => r.Diagnostics.Select(d => (r.FilePath, d)))
            .Where(x => x.d.Severity >= _opts.MinSeverity)
            .ToList();

        var summary = BuildSummary(results.Count, allIssues);

        if (_opts.Format == "json")
            WriteJsonToStdout(summary, allIssues, results.Count, inputLabel);
        else
            WriteTextToStdout(allIssues, summary, inputLabel ?? string.Empty, results.Count);

        if (!string.IsNullOrEmpty(_opts.ReportPath))
            WriteJsonToFile(_opts.ReportPath, summary, allIssues, results.Count, inputLabel);

        return allIssues.Any(x => x.d.Severity >= _opts.MinSeverity);
    }

    // ─── Text output ─────────────────────────────────────────────────────────

    private void WriteTextToStdout(
        List<(string FilePath, AnalysisDiagnostic d)> issues,
        SummaryModel summary,
        string inputLabel,
        int fileCount)
    {
        Console.WriteLine($"Analyzing: {inputLabel} ({fileCount} file{(fileCount == 1 ? "" : "s")})");
        Console.WriteLine();

        foreach (var (filePath, d) in issues)
        {
            var relPath = MakeRelative(filePath);
            var sev     = d.Severity.ToString().ToLowerInvariant().PadRight(11);
            Console.WriteLine($"  {relPath}({d.Line},{d.Column}): {sev} {d.RuleId} - {d.Message}");
        }

        if (issues.Count == 0)
            Console.WriteLine("  No issues found.");

        Console.WriteLine();
        Console.WriteLine($"Summary: {summary.TotalIssues} issue{(summary.TotalIssues == 1 ? "" : "s")} " +
            $"({summary.Errors} error{(summary.Errors == 1 ? "" : "s")}, " +
            $"{summary.Warnings} warning{(summary.Warnings == 1 ? "" : "s")}, " +
            $"{summary.Information} information) across {fileCount} file{(fileCount == 1 ? "" : "s")}");
    }

    // ─── JSON output ─────────────────────────────────────────────────────────

    private void WriteJsonToStdout(
        SummaryModel summary,
        List<(string FilePath, AnalysisDiagnostic d)> issues,
        int fileCount,
        string? inputLabel)
    {
        var report = BuildJsonReport(summary, issues, fileCount, inputLabel);
        Console.WriteLine(Serialize(report));
    }

    private void WriteJsonToFile(
        string path,
        SummaryModel summary,
        List<(string FilePath, AnalysisDiagnostic d)> issues,
        int fileCount,
        string? inputLabel)
    {
        var report = BuildJsonReport(summary, issues, fileCount, inputLabel);
        File.WriteAllText(path, Serialize(report));
    }

    // ─── Shared helpers ───────────────────────────────────────────────────────

    private static SummaryModel BuildSummary(
        int fileCount,
        List<(string FilePath, AnalysisDiagnostic d)> issues)
    {
        return new SummaryModel
        {
            FilesAnalyzed = fileCount,
            TotalIssues   = issues.Count,
            Errors        = issues.Count(x => x.d.Severity == DiagnosticSeverity.Error),
            Warnings      = issues.Count(x => x.d.Severity == DiagnosticSeverity.Warning),
            Information   = issues.Count(x => x.d.Severity == DiagnosticSeverity.Information),
            Hints         = issues.Count(x => x.d.Severity == DiagnosticSeverity.Hint)
        };
    }

    private static ReportModel BuildJsonReport(
        SummaryModel summary,
        List<(string FilePath, AnalysisDiagnostic d)> issues,
        int fileCount,
        string? inputLabel)
    {
        var byCategory = issues
            .GroupBy(x => x.d.CategoryCode)
            .ToDictionary(g => g.Key, g => g.Count());

        return new ReportModel
        {
            Timestamp  = DateTime.UtcNow,
            Settings   = inputLabel ?? string.Empty,
            Summary    = summary,
            ByCategory = byCategory,
            Issues     = issues.Select(x => new IssueModel
            {
                File     = MakeRelative(x.FilePath),
                Line     = x.d.Line,
                Column   = x.d.Column,
                Rule     = x.d.RuleId,
                Severity = x.d.Severity.ToString().ToLowerInvariant(),
                Message  = x.d.Message,
                Fix      = x.d.FixActions.Length > 0 ? "available" : "none"
            }).ToList()
        };
    }

    private static string MakeRelative(string path)
    {
        try
        {
            var rel = Path.GetRelativePath(Directory.GetCurrentDirectory(), path);
            return rel.Length < path.Length ? rel : path;
        }
        catch { return path; }
    }

    private static string Serialize(object obj) =>
        JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            WriteIndented        = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

    // ─── Report models ────────────────────────────────────────────────────────

    internal sealed class ReportModel
    {
        public DateTime                   Timestamp  { get; init; }
        public string                     Settings   { get; init; } = string.Empty;
        public SummaryModel               Summary    { get; init; } = new();
        public Dictionary<string, int>    ByCategory { get; init; } = [];
        public List<IssueModel>           Issues     { get; init; } = [];
    }

    internal sealed class SummaryModel
    {
        public int FilesAnalyzed { get; init; }
        public int TotalIssues   { get; init; }
        public int Errors        { get; init; }
        public int Warnings      { get; init; }
        public int Information   { get; init; }
        public int Hints         { get; init; }
    }

    internal sealed class IssueModel
    {
        public string File     { get; init; } = string.Empty;
        public int    Line     { get; init; }
        public int    Column   { get; init; }
        public string Rule     { get; init; } = string.Empty;
        public string Severity { get; init; } = string.Empty;
        public string Message  { get; init; } = string.Empty;
        public string Fix      { get; init; } = string.Empty;
    }
}
