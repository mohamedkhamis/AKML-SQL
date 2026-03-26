using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.BestPractices;

/// <summary>BP022 — PRINT statement should be removed from production code; use application logging instead.</summary>
public sealed class Bp022PrintStatement : IAnalysisRule
{
    public string RuleId => "BP022";
    public string Category => "BestPractices";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Hint;
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

        public override void Visit(PrintStatement node)
        {
            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "BP022",
                CategoryCode = "BP",
                Severity     = ctx.Settings.GetSeverity("BP022", DiagnosticSeverity.Hint),
                Message      = "PRINT statement should be removed from production code — use application logging instead",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
        }
    }
}
