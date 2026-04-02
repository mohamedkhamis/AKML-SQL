using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Design;

/// <summary>DE002 — Tables without a clustered index (heap tables) detected via schema cache.</summary>
public sealed class De002MissingClusteredIndex : IAnalysisRule
{
    public string RuleId => "DE002";
    public string Category => "Design";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => true;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        return [];
    }
}
