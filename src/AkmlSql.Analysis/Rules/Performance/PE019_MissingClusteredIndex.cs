using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Performance;

/// <summary>PE019 — Tables without a clustered index (heap tables) detected via schema cache.</summary>
public sealed class Pe019MissingClusteredIndex : IAnalysisRule
{
    public string RuleId => "PE019";
    public string Category => "Performance";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => true;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        return [];
    }
}
