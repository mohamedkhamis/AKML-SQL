using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.BestPractices;

/// <summary>BP007 — CATCH block is empty; at minimum, use THROW to re-raise the original error.</summary>
public sealed class Bp007EmptyCatchBlock : IAnalysisRule
{
    public string RuleId => "BP007";
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

        public override void Visit(TryCatchStatement node)
        {
            if (node.CatchStatements != null && node.CatchStatements.Statements.Count > 0)
                return;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "BP007",
                CategoryCode = "BP",
                Severity     = ctx.Settings.GetSeverity("BP007", DiagnosticSeverity.Warning),
                Message      = "CATCH block is empty — at minimum, use THROW to re-raise the original error",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
        }
    }
}
