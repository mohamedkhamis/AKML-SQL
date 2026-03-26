using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Deprecated;

/// <summary>DEP004 — Old-style outer join operators '*=' and '=*' are deprecated; use ANSI LEFT/RIGHT OUTER JOIN syntax.</summary>
public sealed class Dep004DeprecatedJoinSyntax : IAnalysisRule
{
    public string RuleId => "DEP004";
    public string Category => "Deprecated";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => false;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        var diags      = new List<AnalysisDiagnostic>();
        var batchStart = ctx.CurrentBatch.StartOffset;
        var batchEnd   = ctx.CurrentBatch.StartOffset + ctx.CurrentBatch.FragmentLength;

        foreach (var token in ctx.Tokens)
        {
            if (token.Offset < batchStart || token.Offset >= batchEnd) continue;
            if (token.Text != "*=" && token.Text != "=*") continue;

            diags.Add(new AnalysisDiagnostic
            {
                RuleId       = "DEP004",
                CategoryCode = "DEP",
                Severity     = ctx.Settings.GetSeverity("DEP004", DiagnosticSeverity.Warning),
                Message      = "Deprecated join syntax '*=' or '=*' detected — use ANSI LEFT/RIGHT OUTER JOIN syntax",
                StartOffset  = token.Offset,
                EndOffset    = token.Offset + token.Text.Length,
                Line         = token.Line,
                Column       = token.Column,
                FixActions   = []
            });
        }

        return diags;
    }
}
