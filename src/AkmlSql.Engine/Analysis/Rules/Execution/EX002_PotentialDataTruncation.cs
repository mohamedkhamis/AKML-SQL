using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Execution;

/// <summary>EX002 — Potential data truncation from CAST/CONVERT to a smaller string type (stub — requires type inference).</summary>
public sealed class Ex002PotentialDataTruncation : IAnalysisRule
{
    public string RuleId => "EX002";
    public string Category => "Execution";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => false;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        return [];
    }
}
