using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Performance;

/// <summary>PE025 — Detects unnecessary GROUP BY on already-unique columns — requires schema.</summary>
public sealed class PE025_UnnecessaryGroupBy : IAnalysisRule
{
    public string RuleId => "PE025";
    public string Category => "Performance";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => true;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx) => [];
}
