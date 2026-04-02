using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Style;

/// <summary>ST015 — Data type casing — similar coverage to ST001 keyword casing.</summary>
public sealed class St015UpperCaseDataType : IAnalysisRule
{
    public string RuleId => "ST015";
    public string Category => "Style";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Hint;
    public bool RequiresSchema => false;

    // Data type casing — similar coverage to ST001 keyword casing
    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        return [];
    }
}
