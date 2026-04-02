using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Security;

/// <summary>SE012 — Blank password assignment — requires data flow analysis.</summary>
public sealed class Se012BlankPassword : IAnalysisRule
{
    public string RuleId => "SE012";
    public string Category => "Security";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => false;

    // Blank password assignment — detecting @password = '' or PASSWORD = '' requires correlating
    // an empty-string literal with a surrounding identifier that carries a password semantic.
    // This cannot be done reliably at the AST level without data-flow or symbol-table analysis
    // (a bare empty string is a legitimate value in countless non-password contexts).
    // SE002_HardcodedPassword already covers non-empty literal passwords. Deferred.
    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        return [];
    }
}
