using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Performance;

/// <summary>PE023 — Detects subquery nesting depth > 3 — complex analysis deferred.</summary>
public sealed class PE023_ExcessiveSubqueryNesting : IAnalysisRule
{
    public string RuleId => "PE023";
    public string Category => "Performance";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => false;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx) => [];
}
