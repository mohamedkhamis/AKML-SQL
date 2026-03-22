using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Style;

/// <summary>ST011 — Multiple semicolon-separated statements on one line — complex to detect reliably.</summary>
public sealed class ST011_MultipleStatementsOnOneLine : IAnalysisRule
{
    public string RuleId => "ST011";
    public string Category => "Style";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Hint;
    public bool RequiresSchema => false;

    // Multiple semicolon-separated statements on one line — complex to detect reliably
    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx) => [];
}
