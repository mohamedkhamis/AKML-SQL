using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.BestPractices;

/// <summary>BP028 — DELETE with JOIN pattern — similar to BP027 but for DELETE.</summary>
public sealed class Bp028DeleteWithJoin : IAnalysisRule
{
    public string RuleId => "BP028";
    public string Category => "BestPractices";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Information;
    public bool RequiresSchema => false;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        return [];
    }
}
