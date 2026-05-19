using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.BestPractices;

/// <summary>BP024 — Procedure parameters without defaults — requires caller analysis to determine if defaults are appropriate.</summary>
public sealed class Bp024MissingDefaultValue : IAnalysisRule
{
    public string RuleId => "BP024";
    public string Category => "BestPractices";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Hint;
    public bool RequiresSchema => false;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        return [];
    }
}
