using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Performance;

/// <summary>PE032 — Stale statistics detection — requires schema metadata.</summary>
public sealed class Pe032StatisticsStaleness : IAnalysisRule
{
    public string RuleId => "PE032";
    public string Category => "Performance";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => true;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        return [];
    }
}
