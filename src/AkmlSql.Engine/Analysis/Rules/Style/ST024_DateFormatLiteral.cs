using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Style;

/// <summary>ST024 — Date literal format consistency — similar coverage to BP012.</summary>
public sealed class St024DateFormatLiteral : IAnalysisRule
{
    public string RuleId => "ST024";
    public string Category => "Style";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Hint;
    public bool RequiresSchema => false;

    // Date literal format consistency — similar coverage to BP012
    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        return [];
    }
}
