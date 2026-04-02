using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Performance;

/// <summary>PE031 — Implicit type conversion in join/filter prevents index seeks — requires schema.</summary>
public sealed class Pe031ImplicitCast : IAnalysisRule
{
    public string RuleId => "PE031";
    public string Category => "Performance";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => true;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        return [];
    }
}
