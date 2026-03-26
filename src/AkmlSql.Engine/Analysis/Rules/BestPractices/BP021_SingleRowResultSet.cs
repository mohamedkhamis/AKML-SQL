using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.BestPractices;

/// <summary>BP021 — SELECT returning always exactly 1 row should use scalar — requires schema.</summary>
public sealed class BP021_SingleRowResultSet : IAnalysisRule
{
    public string RuleId => "BP021";
    public string Category => "BestPractices";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Information;
    public bool RequiresSchema => false;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx) => [];
}
