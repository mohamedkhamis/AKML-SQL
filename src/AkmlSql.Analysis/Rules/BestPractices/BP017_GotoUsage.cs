using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.BestPractices;

/// <summary>BP017 — GOTO makes control flow difficult to follow; use structured flow control instead.</summary>
public sealed class Bp017GotoUsage : IAnalysisRule
{
    public string RuleId => "BP017";
    public string Category => "BestPractices";
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

        public override void Visit(GoToStatement node)
        {
            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "BP017",
                CategoryCode = "BP",
                Severity     = ctx.Settings.GetSeverity("BP017", DiagnosticSeverity.Warning),
                Message      = "GOTO makes control flow difficult to follow — use structured flow control (IF/WHILE/BREAK/CONTINUE) instead",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
        }
    }
}
