using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Style;

/// <summary>ST014 — Comment formatting consistency — documentation style.</summary>
public sealed class ST014_CommentFormat : IAnalysisRule
{
    public string RuleId => "ST014";
    public string Category => "Style";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Hint;
    public bool RequiresSchema => false;

    // Comment formatting consistency — documentation style
    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx) => [];
}
