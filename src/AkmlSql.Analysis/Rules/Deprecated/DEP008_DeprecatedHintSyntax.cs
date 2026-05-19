using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Deprecated;

/// <summary>DEP008 — Old-style table hint syntax without WITH keyword is deprecated (stub — OldStyleTableHint not available in this ScriptDom version).</summary>
public sealed class Dep008DeprecatedHintSyntax : IAnalysisRule
{
    public string RuleId => "DEP008";
    public string Category => "Deprecated";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => false;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        return [];
    }
}
