using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Performance;

/// <summary>PE015 — IN list with more than 100 values can degrade query performance.</summary>
public sealed class Pe015LargeInList : IAnalysisRule
{
    public string RuleId => "PE015";
    public string Category => "Performance";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => false;

    private const int Threshold = 100;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        var visitor = new Visitor(ctx);
        ctx.CurrentBatch.Accept(visitor);
        return visitor.Diagnostics;
    }

    private sealed class Visitor(AnalysisContext ctx) : TSqlFragmentVisitor
    {
        public List<AnalysisDiagnostic> Diagnostics { get; } = [];

        public override void Visit(InPredicate node)
        {
            if (node.Values == null || node.Values.Count <= Threshold) return;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "PE015",
                CategoryCode = "PE",
                Severity     = ctx.Settings.GetSeverity("PE015", DiagnosticSeverity.Warning),
                Message      = $"IN list with {node.Values.Count} values can degrade performance — consider using a temp table or table variable",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
        }
    }
}
