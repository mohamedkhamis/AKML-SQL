using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.BestPractices;

/// <summary>BP027 — UPDATE with JOIN pattern detected; ensure join conditions correctly identify unique rows.</summary>
public sealed class BP027_UpdateWithJoin : IAnalysisRule
{
    public string RuleId => "BP027";
    public string Category => "BestPractices";
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

        public override void Visit(UpdateStatement node)
        {
            var spec = node.UpdateSpecification;
            if (spec == null) return;

            // Only fire when the UPDATE has a FROM clause containing a JOIN
            if (spec.FromClause == null) return;
            if (!HasJoin(spec.FromClause)) return;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "BP027",
                CategoryCode = "BP",
                Severity     = ctx.Settings.GetSeverity("BP027", DiagnosticSeverity.Information),
                Message      = "UPDATE with JOIN pattern detected — ensure join conditions correctly identify unique rows to avoid unintended multi-row updates",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
        }

        private static bool HasJoin(FromClause fromClause)
        {
            foreach (var reference in fromClause.TableReferences)
            {
                if (reference is JoinTableReference)
                    return true;
            }
            return false;
        }
    }
}
