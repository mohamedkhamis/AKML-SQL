using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Style;

/// <summary>ST022 — Column alias naming convention — too project-specific for global rule.</summary>
public sealed class ST022_CamelCaseColumnAlias : IAnalysisRule
{
    public string RuleId => "ST022";
    public string Category => "Style";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Hint;
    public bool RequiresSchema => false;

    // Column alias naming convention — too project-specific for global rule
    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx) => [];
}
