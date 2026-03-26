using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Security;

/// <summary>SE020 — Privilege escalation via EXECUTE AS — similar coverage to SE004.</summary>
public sealed class SE020_PrivilegeEscalation : IAnalysisRule
{
    public string RuleId => "SE020";
    public string Category => "Security";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => false;

    // Privilege escalation via EXECUTE AS — the primary detection pattern (EXECUTE AS OWNER on a
    // stored procedure) is already covered by SE004_ExecuteAsOwner. A broader rule that catches
    // standalone EXECUTE AS LOGIN / EXECUTE AS USER statements (as opposed to the procedure-option
    // form) would require distinguishing legitimate context-switch scenarios from actual escalation
    // attempts, which is not possible without runtime permission metadata. Deferred pending a
    // clearly distinct detection scenario beyond SE004's scope.
    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx) => [];
}
