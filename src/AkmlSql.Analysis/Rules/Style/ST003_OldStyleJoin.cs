using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Style;

/// <summary>ST003 — Old-style comma-separated JOIN syntax in FROM clause — use explicit ANSI JOIN syntax.</summary>
public sealed class St003OldStyleJoin : IAnalysisRule
{
    public string RuleId => "ST003";
    public string Category => "Style";
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

        public override void Visit(FromClause node)
        {
            if (node.TableReferences == null || node.TableReferences.Count < 2) return;

            // If all top-level table references are non-join references (NamedTableReference,
            // derived tables, etc.) and none is a JoinTableReference, it is old-style comma join.
            foreach (var tableRef in node.TableReferences)
            {
                if (tableRef is JoinTableReference)
                    return; // At least one explicit join exists — not old-style
            }

            // Multiple table references with no explicit JOIN — old-style comma join
            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "ST003",
                CategoryCode = "ST",
                Severity     = ctx.Settings.GetSeverity("ST003", DiagnosticSeverity.Warning),
                Message      = "Old-style comma-separated JOIN syntax detected — use explicit ANSI JOIN syntax (INNER JOIN, LEFT JOIN)",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
        }
    }
}
