using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Performance;

/// <summary>PE035 — View without SCHEMABINDING may prevent indexed view optimization — requires schema.</summary>
public sealed class PE035_SchemaBoundView : IAnalysisRule
{
    public string RuleId => "PE035";
    public string Category => "Performance";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => true;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx) => [];
}
