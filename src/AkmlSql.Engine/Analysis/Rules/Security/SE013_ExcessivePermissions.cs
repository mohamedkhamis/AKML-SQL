using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Security;

/// <summary>SE013 — Excessive GRANT permissions detection — deferred pending ScriptDom Permission API verification.</summary>
public sealed class Se013ExcessivePermissions : IAnalysisRule
{
    public string RuleId => "SE013";
    public string Category => "Security";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Error;
    public bool RequiresSchema => false;

    // Excessive GRANT permissions detection (CONTROL SERVER, ALL PRIVILEGES, etc.) — deferred pending
    // ScriptDom Permission API verification. The GrantStatement.Privileges collection and the shape of
    // each Permission node need to be confirmed against the installed ScriptDom version before
    // implementing to avoid a runtime MissingMethodException.
    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        return [];
    }
}
