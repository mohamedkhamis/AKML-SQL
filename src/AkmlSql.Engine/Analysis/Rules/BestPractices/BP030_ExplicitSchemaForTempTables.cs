using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.BestPractices;

/// <summary>BP030 — Temp table without schema prefix — temp tables use #/## prefix and don't support schema qualification.</summary>
public sealed class Bp030ExplicitSchemaForTempTables : IAnalysisRule
{
    public string RuleId => "BP030";
    public string Category => "BestPractices";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Information;
    public bool RequiresSchema => false;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        return [];
    }
}
