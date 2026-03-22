using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Security;

/// <summary>SE019 — Connection string exposure detection — similar coverage to SE010.</summary>
public sealed class SE019_ExposedConnectionDetails : IAnalysisRule
{
    public string RuleId => "SE019";
    public string Category => "Security";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => false;

    // Connection string exposure detection — the main signal (a string literal containing
    // "Server=", "Data Source=", "Password=" etc.) is already captured by
    // SE010_UnencryptedConnectionString. Implementing a separate rule here without access to
    // broader context (e.g. the literal being stored in a table, passed to xp_cmdshell, or
    // written to a file) would produce duplicate diagnostics. Deferred pending a distinct
    // detection scenario that SE010 does not already cover.
    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx) => [];
}
