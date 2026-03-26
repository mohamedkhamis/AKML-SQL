using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Security;

/// <summary>SE015 — Role-based access control verification — requires schema/permission metadata.</summary>
public sealed class SE015_RoleBasedAccess : IAnalysisRule
{
    public string RuleId => "SE015";
    public string Category => "Security";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => true;

    // Role-based access control verification — determining whether direct user permissions are used
    // instead of role membership (and whether those roles are appropriately scoped) requires
    // querying sys.database_permissions, sys.database_principals, and sys.role_members at analysis
    // time. This is only possible when schema/permission metadata is available. Deferred until
    // the schema provider exposes permission graphs.
    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx) => [];
}
