using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Deprecated;

/// <summary>DEP005 — Old-style RAISERROR syntax without explicit severity and state arguments is deprecated; use RAISERROR('message', severity, state) or THROW instead.</summary>
public sealed class Dep005RaiserrorOldSyntax : IAnalysisRule
{
    public string RuleId => "DEP005";
    public string Category => "Deprecated";
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

        public override void Visit(RaiseErrorStatement node)
        {
            // Old-style RAISERROR is missing severity or state parameters
            if (node.SecondParameter == null || node.ThirdParameter == null)
            {
                Diagnostics.Add(new AnalysisDiagnostic
                {
                    RuleId       = "DEP005",
                    CategoryCode = "DEP",
                    Severity     = ctx.Settings.GetSeverity("DEP005", DiagnosticSeverity.Warning),
                    Message      = "Old-style RAISERROR syntax — use RAISERROR('message', severity, state) or THROW instead",
                    StartOffset  = node.StartOffset,
                    EndOffset    = node.StartOffset + node.FragmentLength,
                    Line         = node.StartLine,
                    Column       = node.StartColumn,
                    FixActions   = []
                });
            }
        }
    }
}
