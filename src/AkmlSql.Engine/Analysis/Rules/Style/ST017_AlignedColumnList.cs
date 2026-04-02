using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Style;

/// <summary>ST017 — Column list alignment style — too subjective for static analysis.</summary>
public sealed class St017AlignedColumnList : IAnalysisRule
{
    public string RuleId => "ST017";
    public string Category => "Style";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Hint;
    public bool RequiresSchema => false;

    // Column list alignment style — too subjective for static analysis
    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        return [];
    }
}
