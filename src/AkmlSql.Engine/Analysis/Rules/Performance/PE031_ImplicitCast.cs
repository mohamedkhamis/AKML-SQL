using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Performance;

/// <summary>PE031 — Implicit type conversion in join/filter prevents index seeks — requires schema.</summary>
public sealed class PE031_ImplicitCast : IAnalysisRule
{
    public string RuleId => "PE031";
    public string Category => "Performance";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => true;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx) => [];
}
