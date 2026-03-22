using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Style;

/// <summary>ST018 — TOP with/without parentheses detection — ScriptDom normalizes both forms.</summary>
public sealed class ST018_TopWithParentheses : IAnalysisRule
{
    public string RuleId => "ST018";
    public string Category => "Style";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Hint;
    public bool RequiresSchema => false;

    // TOP with/without parentheses detection — ScriptDom normalizes both forms
    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx) => [];
}
