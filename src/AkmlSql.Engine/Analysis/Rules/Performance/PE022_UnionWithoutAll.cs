using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Performance;

/// <summary>PE022 — UNION without ALL performs duplicate elimination via sort.</summary>
public sealed class PE022_UnionWithoutAll : IAnalysisRule
{
    public string RuleId => "PE022";
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

        public override void ExplicitVisit(BinaryQueryExpression node)
        {
            if (node.BinaryQueryExpressionType == BinaryQueryExpressionType.Union && !node.All)
            {
                Diagnostics.Add(new AnalysisDiagnostic
                {
                    RuleId       = "PE022",
                    CategoryCode = "PE",
                    Severity     = ctx.Settings.GetSeverity("PE022", DiagnosticSeverity.Warning),
                    Message      = "UNION removes duplicates — use UNION ALL if duplicates are acceptable to avoid sort overhead",
                    StartOffset  = node.StartOffset,
                    EndOffset    = node.StartOffset + node.FragmentLength,
                    Line         = node.StartLine,
                    Column       = node.StartColumn,
                    FixActions   = []
                });
            }

            base.ExplicitVisit(node);
        }
    }
}
