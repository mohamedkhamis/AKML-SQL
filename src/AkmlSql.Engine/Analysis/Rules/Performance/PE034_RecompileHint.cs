using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Performance;

/// <summary>PE034 — RECOMPILE hint causes the query plan to be discarded on every execution.
/// Stub: OPTION(RECOMPILE) hints are on SelectStatement.OptimizerHints in ScriptDom,
/// but the specific node API varies across versions — deferred pending verification.</summary>
public sealed class PE034_RecompileHint : IAnalysisRule
{
    public string RuleId => "PE034";
    public string Category => "Performance";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Information;
    public bool RequiresSchema => false;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx) => [];
}
