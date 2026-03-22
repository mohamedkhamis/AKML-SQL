using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.BestPractices;

/// <summary>BP026 — SELECT without FROM — valid pattern for scalar expressions, detection not needed.</summary>
public sealed class BP026_SelectWithoutFrom : IAnalysisRule
{
    public string RuleId => "BP026";
    public string Category => "BestPractices";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Information;
    public bool RequiresSchema => false;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx) => [];
}
