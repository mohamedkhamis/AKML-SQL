using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Style;

/// <summary>ST012 — Table alias without AS keyword — minor style issue.</summary>
public sealed class St012InlineTableAlias : IAnalysisRule
{
    public string RuleId => "ST012";
    public string Category => "Style";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Hint;
    public bool RequiresSchema => false;

    // Table alias without AS keyword — minor style issue
    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        return [];
    }
}
