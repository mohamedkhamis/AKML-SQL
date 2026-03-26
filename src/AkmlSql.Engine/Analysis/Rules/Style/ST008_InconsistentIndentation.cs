using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Style;

/// <summary>ST008 — Mixed tab/space indentation detected in the document.</summary>
public sealed class ST008_InconsistentIndentation : IAnalysisRule
{
    public string RuleId => "ST008";
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
            var line            = lines[i];
            var thisLineOffset  = lineOffset;
            lineOffset         += line.Length + 1; // +1 for the '\n' consumed by Split

            // Only inspect lines that belong to the current batch
            if (thisLineOffset < batchStart || thisLineOffset >= batchEnd) continue;

            // Collect leading whitespace
            int  j        = 0;
            bool hasTab   = false;
            bool hasSpace = false;
            while (j < line.Length && (line[j] == ' ' || line[j] == '\t'))
            {
                if (line[j] == '\t') hasTab   = true;
                else                 hasSpace = true;
                j++;
            }

            if (!hasTab || !hasSpace) continue;

            // Mixed indentation on this line
            yield return new AnalysisDiagnostic
            {
                RuleId       = "ST008",
                CategoryCode = "ST",
                Severity     = ctx.Settings.GetSeverity("ST008", DiagnosticSeverity.Hint),
                Message      = $"Mixed tab/space indentation on line {i + 1}",
                StartOffset  = thisLineOffset,
                EndOffset    = thisLineOffset + j,
                Line         = i + 1,
                Column       = 1,
                FixActions   = []
            };
        }
    }
}
