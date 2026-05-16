using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Execution;

/// <summary>EX003 — Ambiguous column reference that could match multiple tables (requires schema cache).</summary>
public sealed class Ex003AmbiguousColumnReference : IAnalysisRule
{
    public string RuleId => "EX003";
    public string Category => "Execution";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Error;
    public bool RequiresSchema => true;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        return [];
    }
}
