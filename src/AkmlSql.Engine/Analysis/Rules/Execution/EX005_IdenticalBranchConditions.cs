using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Execution;

/// <summary>EX005 — IF/ELSE branches with identical conditions (stub — requires expression equality comparison).</summary>
public sealed class EX005_IdenticalBranchConditions : IAnalysisRule
{
    public string RuleId => "EX005";
    public string Category => "Execution";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => false;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx) => [];
}
