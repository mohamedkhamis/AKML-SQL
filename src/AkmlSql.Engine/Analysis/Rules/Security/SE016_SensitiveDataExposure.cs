using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Security;

/// <summary>SE016 — Sensitive data exposure detection — requires schema and column metadata.</summary>
public sealed class Se016SensitiveDataExposure : IAnalysisRule
{
    public string RuleId => "SE016";
    public string Category => "Security";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => false;

    // Sensitive data exposure detection — reliably flagging SELECT * or unmasked column projections
    // from tables that hold PII/credentials (e.g. columns named ssn, password, credit_card) requires
    // resolving table and column aliases back to their base schema objects. Without column metadata
    // the heuristic of matching table/column name substrings produces an unacceptable false-positive
    // rate (any table named "passwords_audit" would fire on every SELECT). Deferred until the schema
    // provider exposes column-level metadata.
    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        return [];
    }
}
