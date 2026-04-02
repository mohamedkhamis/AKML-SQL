using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.BestPractices;

/// <summary>BP023 — OUTPUT parameter declared but callers never receive value — requires call-site analysis.</summary>
public sealed class Bp023UnusedOutputParameter : IAnalysisRule
{
    public string RuleId => "BP023";
    public string Category => "BestPractices";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => true;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        return [];
    }
}
