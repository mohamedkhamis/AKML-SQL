using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Performance;

/// <summary>PE016 — Correlated subquery in WHERE clause executes once per row.</summary>
public sealed class Pe016CorrelatedSubquery : IAnalysisRule
{
    public string RuleId => "PE016";
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
        private bool _inWhere;

        public override void ExplicitVisit(WhereClause node)
        {
            _inWhere = true;
            base.ExplicitVisit(node);
            _inWhere = false;
        }

        public override void Visit(ScalarSubquery node)
        {
            if (!_inWhere) return;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "PE016",
                CategoryCode = "PE",
                Severity     = ctx.Settings.GetSeverity("PE016", DiagnosticSeverity.Information),
                Message      = "Correlated subquery in WHERE clause executes once per row — consider rewriting as a JOIN",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
        }
    }
}
