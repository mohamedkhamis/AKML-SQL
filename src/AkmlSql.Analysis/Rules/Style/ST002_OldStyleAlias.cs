using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Style;

/// <summary>ST002 — Old-style alias syntax (alias = expression) — stub; returns no diagnostics to avoid false positives.</summary>
public sealed class St002OldStyleAlias : IAnalysisRule
{
    public string RuleId => "ST002";
    public string Category => "Style";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => false;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        return [];
    }
}
