using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Naming;

/// <summary>NM004 — Inconsistent naming convention detected across column/table names in the document (stub — requires cross-identifier analysis).</summary>
public sealed class Nm004InconsistentNaming : IAnalysisRule
{
    public string RuleId => "NM004";
    public string Category => "Naming";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Information;
    public bool RequiresSchema => false;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        return [];
    }
}
