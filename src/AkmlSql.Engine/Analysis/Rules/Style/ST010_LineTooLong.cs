using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Style;

/// <summary>ST010 — Line exceeds 120 characters — consider breaking into multiple lines for readability.</summary>
public sealed class ST010_LineTooLong : IAnalysisRule
{
    public string RuleId => "ST010";
    public string Category => "Style";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Hint;
    public bool RequiresSchema => false;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        var text = ctx.DocumentText;
        if (string.IsNullOrEmpty(text)) yield break;

        var batchStart = ctx.CurrentBatch.StartOffset;
        var batchEnd   = ctx.CurrentBatch.StartOffset + ctx.CurrentBatch.FragmentLength;

        var lines      = text.Split('\n');
        int lineOffset = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            var line           = lines[i];
            var thisLineOffset = lineOffset;
            lineOffset        += line.Length + 1; // +1 for the '\n' consumed by Split

            if (thisLineOffset < batchStart || thisLineOffset >= batchEnd) continue;

            var trimmedLine = line.TrimEnd('\r');
            if (trimmedLine.Length <= 120) continue;

            yield return new AnalysisDiagnostic
            {
                RuleId       = "ST010",
                CategoryCode = "ST",
                Severity     = ctx.Settings.GetSeverity("ST010", DiagnosticSeverity.Hint),
                Message      = "Line exceeds 120 characters — consider breaking into multiple lines for readability",
                StartOffset  = thisLineOffset + 120,
                EndOffset    = thisLineOffset + line.Length,
                Line         = i + 1,
                Column       = 121,
                FixActions   = []
            };
        }
    }
}
