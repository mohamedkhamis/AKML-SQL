using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Style;

/// <summary>ST005 — Mix of AS alias and bare alias in the same SELECT — stub; returns no diagnostics to avoid false positives.</summary>
public sealed class ST005_InconsistentAlias : IAnalysisRule
{
    public string RuleId => "ST005";
    public string Category => "Style";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Hint;
    public bool RequiresSchema => false;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx) => [];
}
