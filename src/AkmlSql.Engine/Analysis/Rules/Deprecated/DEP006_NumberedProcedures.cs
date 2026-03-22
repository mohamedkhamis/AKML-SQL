using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Deprecated;

/// <summary>DEP006 — Numbered procedures (PROC;1) are deprecated and not supported in all editions.</summary>
public sealed class DEP006_NumberedProcedures : IAnalysisRule
{
    public string RuleId => "DEP006";
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

        public override void Visit(CreateProcedureStatement node)
        {
            // ProcedureReference.Number holds the ;N suffix for numbered procedures
            if (node.ProcedureReference?.Number == null) return;

            var procName = node.ProcedureReference?.Name?.BaseIdentifier?.Value ?? "unknown";

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "DEP006",
                CategoryCode = "DEP",
                Severity     = ctx.Settings.GetSeverity("DEP006", DiagnosticSeverity.Warning),
                Message      = $"Numbered procedures (PROC;1) are deprecated and not supported in all editions — remove the number suffix",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
        }
    }
}
