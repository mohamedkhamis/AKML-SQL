using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.BestPractices;

/// <summary>BP005 — EXEC(string) can enable SQL injection; use sp_executesql with parameterized queries instead.</summary>
public sealed class Bp005ExecString : IAnalysisRule
{
    public string RuleId => "BP005";
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

        public override void Visit(ExecuteStatement node)
        {
            var entity = node.ExecuteSpecification?.ExecutableEntity;
            if (entity is not ExecutableStringList) return;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "BP005",
                CategoryCode = "BP",
                Severity     = ctx.Settings.GetSeverity("BP005", DiagnosticSeverity.Warning),
                Message      = "EXEC(string) can enable SQL injection — use sp_executesql with parameterized queries instead",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
        }
    }
}
