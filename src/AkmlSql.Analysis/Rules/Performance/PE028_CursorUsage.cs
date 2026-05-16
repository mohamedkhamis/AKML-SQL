using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Performance;

/// <summary>PE028 — Cursor-based row-by-row processing is slow.</summary>
public sealed class Pe028CursorUsage : IAnalysisRule
{
    public string RuleId => "PE028";
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

        public override void ExplicitVisit(DeclareCursorStatement node)
        {
            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "PE028",
                CategoryCode = "PE",
                Severity     = ctx.Settings.GetSeverity("PE028", DiagnosticSeverity.Warning),
                Message      = "Cursor-based row-by-row processing is slow — consider set-based alternatives",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });

            base.ExplicitVisit(node);
        }
    }
}
