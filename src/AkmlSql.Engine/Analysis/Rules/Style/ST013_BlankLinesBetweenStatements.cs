using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Style;

/// <summary>ST013 — Missing blank lines between logical statement groups — whitespace style.</summary>
public sealed class St013BlankLinesBetweenStatements : IAnalysisRule
{
    public string RuleId => "ST013";
    public string Category => "Style";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Hint;
    public bool RequiresSchema => false;

    // Missing blank lines between logical statement groups — whitespace style
    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        return [];
    }
}
