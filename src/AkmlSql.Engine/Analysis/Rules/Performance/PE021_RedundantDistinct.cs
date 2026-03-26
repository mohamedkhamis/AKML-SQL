using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Performance;

/// <summary>PE021 — DISTINCT is redundant when GROUP BY is present.</summary>
public sealed class Pe021RedundantDistinct : IAnalysisRule
{
    public string RuleId => "PE021";
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

        public override void ExplicitVisit(QuerySpecification node)
        {
            if (node.UniqueRowFilter == UniqueRowFilter.Distinct && node.GroupByClause != null)
            {
                Diagnostics.Add(new AnalysisDiagnostic
                {
                    RuleId       = "PE021",
                    CategoryCode = "PE",
                    Severity     = ctx.Settings.GetSeverity("PE021", DiagnosticSeverity.Warning),
                    Message      = "DISTINCT is redundant when GROUP BY is present — GROUP BY already produces unique rows",
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
