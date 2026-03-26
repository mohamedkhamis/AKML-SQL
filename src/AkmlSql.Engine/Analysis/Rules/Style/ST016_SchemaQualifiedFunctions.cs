using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Style;

/// <summary>ST016 — Built-in functions do not need schema qualification — no rule needed.</summary>
public sealed class St016SchemaQualifiedFunctions : IAnalysisRule
{
    public string RuleId => "ST016";
    public string Category => "Style";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Hint;
    public bool RequiresSchema => false;

    // Built-in functions do not need schema qualification — no rule needed
    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        return [];
    }
}
