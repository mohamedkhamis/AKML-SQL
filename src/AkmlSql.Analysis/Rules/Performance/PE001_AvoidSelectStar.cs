using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Performance;

/// <summary>PE001 — Avoid SELECT * in stored procedures.</summary>
public sealed class Pe001AvoidSelectStar : IAnalysisRule
{
    public string RuleId => "PE001";
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
        private bool _inProcedure;

        public override void Visit(CreateProcedureStatement node) { _inProcedure = true; base.Visit(node); }
        public override void ExplicitVisit(CreateProcedureStatement node) { base.ExplicitVisit(node); _inProcedure = false; }
        public override void Visit(AlterProcedureStatement node) { _inProcedure = true; base.Visit(node); }
        public override void ExplicitVisit(AlterProcedureStatement node) { base.ExplicitVisit(node); _inProcedure = false; }

        public override void Visit(SelectStarExpression node)
        {
            if (!_inProcedure) return;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId      = "PE001",
                CategoryCode = "PE",
                Severity    = ctx.Settings.GetSeverity("PE001", DiagnosticSeverity.Warning),
                Message     = "Avoid SELECT * in stored procedures; use explicit column list",
                StartOffset = node.StartOffset,
                EndOffset   = node.StartOffset + node.FragmentLength,
                Line        = node.StartLine,
                Column      = node.StartColumn,
                FixActions  =
                [
                    new AnalysisFixAction
                    {
                        Label           = "Suppress PE001 for this line",
                        FixType         = FixType.Suppress,
                        SuppressRuleId  = "PE001",
                        SuppressScope   = SuppressScope.Line
                    }
                ]
            });
        }
    }
}
