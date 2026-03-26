using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.BestPractices;

/// <summary>BP014 — INSERT without explicit column list is fragile when table schema changes.</summary>
public sealed class BP014_InsertWithoutColumnList : IAnalysisRule
{
    public string RuleId => "BP014";
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

        public override void Visit(InsertStatement node)
        {
            var spec = node.InsertSpecification;
            if (spec == null) return;

            // Only flag when no column list is specified
            if (spec.Columns.Count > 0) return;

            // Only flag for VALUES or SELECT sources (not DEFAULT VALUES, EXECUTE, etc.)
            if (spec.InsertSource is not ValuesInsertSource and not SelectInsertSource) return;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "BP014",
                CategoryCode = "BP",
                Severity     = ctx.Settings.GetSeverity("BP014", DiagnosticSeverity.Warning),
                Message      = "INSERT without explicit column list is fragile — add column names to avoid issues when table schema changes",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
        }
    }
}
