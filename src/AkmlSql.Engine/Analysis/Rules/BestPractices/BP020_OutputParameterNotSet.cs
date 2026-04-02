using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.BestPractices;

/// <summary>BP020 — OUTPUT parameter not assigned in all code paths — requires flow analysis.</summary>
public sealed class Bp020OutputParameterNotSet : IAnalysisRule
{
    public string RuleId => "BP020";
    public string Category => "BestPractices";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => false;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        return [];
    }
}
