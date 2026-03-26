using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Style;

/// <summary>ST021 — String delimiter consistency — SQL standard uses single quotes.</summary>
public sealed class ST021_SingleQuoteVsDoubleQuote : IAnalysisRule
{
    public string RuleId => "ST021";
    public string Category => "Style";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Hint;
    public bool RequiresSchema => false;

    // String delimiter consistency — SQL standard uses single quotes
    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx) => [];
}
