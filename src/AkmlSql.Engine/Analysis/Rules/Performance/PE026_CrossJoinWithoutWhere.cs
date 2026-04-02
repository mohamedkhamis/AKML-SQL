using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Performance;

/// <summary>PE026 — CROSS JOIN produces a cartesian product.</summary>
public sealed class Pe026CrossJoinWithoutWhere : IAnalysisRule
{
    public string RuleId => "PE026";
    public string Category => "Performance";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Information;
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

        public override void ExplicitVisit(UnqualifiedJoin node)
        {
            if (node.UnqualifiedJoinType == UnqualifiedJoinType.CrossJoin)
            {
                Diagnostics.Add(new AnalysisDiagnostic
                {
                    RuleId       = "PE026",
                    CategoryCode = "PE",
                    Severity     = ctx.Settings.GetSeverity("PE026", DiagnosticSeverity.Information),
                    Message      = "CROSS JOIN produces a cartesian product — verify this is intentional",
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
