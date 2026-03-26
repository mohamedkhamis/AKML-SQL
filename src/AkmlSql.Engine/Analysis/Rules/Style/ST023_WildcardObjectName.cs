using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Style;

/// <summary>ST023 — Wildcard in object names — not a valid SQL Server syntax concern.</summary>
public sealed class ST023_WildcardObjectName : IAnalysisRule
{
    public string RuleId => "ST023";
    public string Category => "Style";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Hint;
    public bool RequiresSchema => false;

    // Wildcard in object names — not a valid SQL Server syntax concern
    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx) => [];
}
