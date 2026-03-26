using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Style;

/// <summary>ST007 — Missing schema prefix (covered by PE002 at Warning severity) — stub; returns no diagnostics.</summary>
public sealed class St007MissingSchemaPrefix : IAnalysisRule
{
    public string RuleId => "ST007";
    public string Category => "Style";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Hint;
    public bool RequiresSchema => false;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        return [];
    }
}
