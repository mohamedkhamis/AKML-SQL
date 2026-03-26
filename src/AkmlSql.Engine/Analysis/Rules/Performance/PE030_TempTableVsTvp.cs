using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Performance;

/// <summary>PE030 — Prefer TVP over temp table for passing sets to procedures — requires schema context.</summary>
public sealed class PE030_TempTableVsTvp : IAnalysisRule
{
    public string RuleId => "PE030";
    public string Category => "Performance";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => false;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx) => [];
}
