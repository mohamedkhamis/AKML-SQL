using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.BestPractices;

/// <summary>BP025 — Large procedure body detection — requires line count calculation.</summary>
public sealed class Bp025LargeProcedureBody : IAnalysisRule
{
    public string RuleId => "BP025";
    public string Category => "BestPractices";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Hint;
    public bool RequiresSchema => false;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        return [];
    }
}
