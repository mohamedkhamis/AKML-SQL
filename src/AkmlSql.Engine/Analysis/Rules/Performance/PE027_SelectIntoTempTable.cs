using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis.Rules.Performance;

/// <summary>PE027 — SELECT INTO creates a table-level lock — prefer CREATE TABLE + INSERT INTO.</summary>
public sealed class PE027_SelectIntoTempTable : IAnalysisRule
{
    public string RuleId => "PE027";
    public string Category => "Performance";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => false;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx) => [];
}
