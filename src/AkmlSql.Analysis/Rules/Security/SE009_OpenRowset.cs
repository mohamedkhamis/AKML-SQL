using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Security;

/// <summary>SE009 — OPENROWSET allows ad-hoc distributed queries which may bypass security controls.</summary>
public sealed class Se009OpenRowset : IAnalysisRule
{
    public string RuleId => "SE009";
    public string Category => "Security";
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

        public override void Visit(OpenRowsetTableReference node)
        {
            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "SE009",
                CategoryCode = "SE",
                Severity     = ctx.Settings.GetSeverity("SE009", DiagnosticSeverity.Warning),
                Message      = "OPENROWSET allows ad-hoc distributed queries which may bypass security controls",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
        }
    }
}
