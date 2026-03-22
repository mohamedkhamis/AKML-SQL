using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Security;

/// <summary>SE011 — SA login usage detection — 'sa' literal is too broad without connection-string context.</summary>
public sealed class SE011_SaLoginUsage : IAnalysisRule
{
    public string RuleId => "SE011";
    public string Category => "Security";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => false;

    // SA login detection — 'sa' literal is too broad without connection-string context;
    // a string value of 'sa' is common in general data (e.g. a column value for South Africa or a name abbreviation).
    // Reliable detection requires parsing connection-string patterns or sp_addlogin/sp_grantlogin call arguments,
    // which in turn requires data-flow analysis across statement boundaries. Deferred.
    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx) => [];
}
