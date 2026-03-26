using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Style;

/// <summary>ST020 — Stylistic preference between DISTINCT and GROUP BY — context-dependent.</summary>
public sealed class ST020_SelectDistinctVsGroupBy : IAnalysisRule
{
    public string RuleId => "ST020";
    public string Category => "Style";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Hint;
    public bool RequiresSchema => false;

    // Stylistic preference between DISTINCT and GROUP BY — context-dependent
    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx) => [];
}
