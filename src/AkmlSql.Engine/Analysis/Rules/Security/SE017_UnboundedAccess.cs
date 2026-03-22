using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Security;

/// <summary>SE017 — Row-level security bypass detection — requires schema.</summary>
public sealed class SE017_UnboundedAccess : IAnalysisRule
{
    public string RuleId => "SE017";
    public string Category => "Security";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => true;

    // Row-level security bypass detection — determining whether a query is executed under a context
    // (EXECUTE AS / elevated role) that bypasses an active Row-Level Security policy requires
    // correlating the execution context with sys.security_policies and sys.security_predicates.
    // This is only possible when live schema metadata is available. Deferred.
    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx) => [];
}
