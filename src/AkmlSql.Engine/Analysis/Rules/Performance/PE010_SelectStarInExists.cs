using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Performance;

/// <summary>PE010 — Replace EXISTS (SELECT * ...) with EXISTS (SELECT 1 ...).</summary>
public sealed class PE010_SelectStarInExists : IAnalysisRule
{
    public string RuleId => "PE010";
    public string Category => "Performance";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => false;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        var visitor = new Visitor(ctx);
        ctx.CurrentBatch.Accept(visitor);
        return visitor.Diagnostics;
    }

    private sealed class Visitor(AnalysisContext ctx) : TSqlFragmentVisitor
    {
        public List<AnalysisDiagnostic> Diagnostics { get; } = [];

        public override void Visit(ExistsPredicate node)
        {
            if (node.Subquery?.QueryExpression is not QuerySpecification qs) return;

            foreach (var elem in qs.SelectElements)
            {
                if (elem is not SelectStarExpression star) continue;

                Diagnostics.Add(new AnalysisDiagnostic
                {
                    RuleId       = "PE010",
                    CategoryCode = "PE",
                    Severity     = ctx.Settings.GetSeverity("PE010", DiagnosticSeverity.Warning),
                    Message      = "Use SELECT 1 instead of SELECT * inside EXISTS",
                    StartOffset  = star.StartOffset,
                    EndOffset    = star.StartOffset + star.FragmentLength,
                    Line         = star.StartLine,
                    Column       = star.StartColumn,
                    FixActions   =
                    [
                        new AnalysisFixAction
                        {
                            Label            = "Replace SELECT * with SELECT 1",
                            FixType          = FixType.Transform,
                            ReplacementStart = star.StartOffset,
                            ReplacementEnd   = star.StartOffset + star.FragmentLength,
                            ReplacementText  = "1"
                        }
                    ]
                });
            }
        }
    }
}
