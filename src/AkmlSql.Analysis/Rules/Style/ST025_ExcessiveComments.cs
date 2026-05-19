using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Style;

/// <summary>ST025 — Excessive inline comments — subjective style preference.</summary>
public sealed class St025ExcessiveComments : IAnalysisRule
{
    public string RuleId => "ST025";
    public string Category => "Style";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Hint;
    public bool RequiresSchema => false;

    // Excessive inline comments — subjective style preference
    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        return [];
    }
}
